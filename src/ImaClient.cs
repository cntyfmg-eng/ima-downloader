using System.Text;
using System.Text.Json;

namespace ImaDownloader;

/// <summary>ima OpenAPI 客户端（认证 + 知识库/条目/媒体接口封装）</summary>
public sealed class ImaClient
{
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    public string ClientId { get; }
    public string ApiKey { get; }

    public ImaClient(string clientId, string apiKey)
    {
        ClientId = clientId.Trim();
        ApiKey = apiKey.Trim();
    }

    // ---------- 配置读写 ----------

    public static string ConfigPath
    {
        get
        {
            var exeDir = AppContext.BaseDirectory;
            return Path.Combine(exeDir, "ima_config.json");
        }
    }

    public static (string clientId, string apiKey, string downloadDir) LoadConfig()
    {
        string cid = "", key = "", dir = "";
        bool cleared = false;
        try
        {
            if (File.Exists(ConfigPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigPath));
                cid = doc.RootElement.TryGetProperty("client_id", out var c) ? c.GetString() ?? "" : "";
                key = doc.RootElement.TryGetProperty("api_key", out var k) ? k.GetString() ?? "" : "";
                dir = doc.RootElement.TryGetProperty("download_dir", out var d) ? d.GetString() ?? "" : "";
                cleared = doc.RootElement.TryGetProperty("credential_cleared", out var cc) && cc.ValueKind == JsonValueKind.True;
            }
        }
        catch { }

        // 回退到 ima-skill 凭证目录；用户已执行「清除凭证并保存」后不再回退，保证清除生效
        if (!cleared && (string.IsNullOrWhiteSpace(cid) || string.IsNullOrWhiteSpace(key)))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cid = TryRead(Path.Combine(home, ".config", "ima", "client_id"));
            key = TryRead(Path.Combine(home, ".config", "ima", "api_key"));
        }
        return (cid, key, dir);
    }

    public static void SaveConfig(string clientId, string apiKey, string downloadDir, bool credentialCleared = false)
    {
        var obj = new { client_id = clientId, api_key = apiKey, download_dir = downloadDir, credential_cleared = credentialCleared };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private static string TryRead(string p)
    {
        try { return File.Exists(p) ? File.ReadAllText(p).Trim() : ""; }
        catch { return ""; }
    }

    // ---------- 基础请求 ----------

    private async Task<JsonElement> PostAsync(string apiPath, object body, CancellationToken ct)
    {
        var url = "https://ima.qq.com/" + apiPath;
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.TryAddWithoutValidation("ima-openapi-clientid", ClientId);
        req.Headers.TryAddWithoutValidation("ima-openapi-apikey", ApiKey);
        req.Headers.TryAddWithoutValidation("ima-openapi-ctx", "skill_version=1.1.10");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonElement doc;
        try { doc = JsonDocument.Parse(text).RootElement.Clone(); }
        catch { throw new Exception("接口返回非 JSON：" + Truncate(text, 200)); }
        int code = doc.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : -1;
        if (code != 0)
        {
            string msg = doc.TryGetProperty("msg", out var m) ? m.GetString() ?? "未知错误" : "未知错误";
            throw new Exception($"ima 接口错误(code={code})：{msg}");
        }
        return doc.TryGetProperty("data", out var d) ? d.Clone() : JsonDocument.Parse("{}").RootElement;
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    // ---------- 业务接口 ----------

    /// <summary>列出可访问的全部知识库</summary>
    public async Task<List<KbInfo>> GetKnowledgeBasesAsync(CancellationToken ct)
    {
        var list = new List<KbInfo>();
        string cursor = "";
        while (true)
        {
            var data = await PostAsync("openapi/wiki/v1/get_addable_knowledge_base_list",
                new { cursor, limit = 50 }, ct);
            if (data.TryGetProperty("addable_knowledge_base_list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                    list.Add(new KbInfo(it.GetProperty("id").GetString() ?? "", it.GetProperty("name").GetString() ?? ""));
            bool end = data.TryGetProperty("is_end", out var e) && e.ValueKind == JsonValueKind.True;
            string next = data.TryGetProperty("next_cursor", out var nc) ? nc.GetString() ?? "" : "";
            if (end || string.IsNullOrEmpty(next)) break;
            cursor = next;
        }
        return list;
    }

    /// <summary>浏览知识库某文件夹（含分页），返回条目列表</summary>
    public async Task<List<ItemInfo>> ListItemsAsync(string kbId, string folderId, CancellationToken ct,
        IProgress<string>? progress = null)
    {
        var list = new List<ItemInfo>();
        string cursor = "";
        while (true)
        {
            var body = new Dictionary<string, object> { ["cursor"] = cursor, ["limit"] = 50, ["knowledge_base_id"] = kbId };
            if (!string.IsNullOrEmpty(folderId)) body["folder_id"] = folderId;
            var data = await PostAsync("openapi/wiki/v1/get_knowledge_list", body, ct);
            if (data.TryGetProperty("knowledge_list", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var it in arr.EnumerateArray())
                {
                    var item = new ItemInfo(
                        it.GetProperty("media_id").GetString() ?? "",
                        it.GetProperty("title").GetString() ?? "",
                        it.TryGetProperty("media_type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0);
                    list.Add(item);
                }
            bool end = data.TryGetProperty("is_end", out var e) && e.ValueKind == JsonValueKind.True;
            string next = data.TryGetProperty("next_cursor", out var nc) ? nc.GetString() ?? "" : "";
            progress?.Report($"已获取 {list.Count} 项...");
            if (end || string.IsNullOrEmpty(next)) break;
            cursor = next;
        }
        return list;
    }

    /// <summary>获取媒体下载信息。返回 (mediaType, url, headers, notebookId)</summary>
    public async Task<(int mediaType, string url, Dictionary<string, string> headers, string notebookId)> GetMediaInfoAsync(string mediaId, CancellationToken ct)
    {
        var data = await PostAsync("openapi/wiki/v1/get_media_info", new { media_id = mediaId }, ct);
        int type = data.TryGetProperty("media_type", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
        string url = "";
        var headers = new Dictionary<string, string>();
        if (data.TryGetProperty("url_info", out var ui) && ui.ValueKind == JsonValueKind.Object)
        {
            url = ui.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            if (ui.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Object)
                foreach (var p in hs.EnumerateObject())
                    headers[p.Name] = p.Value.GetString() ?? "";
        }
        string notebookId = "";
        if (data.TryGetProperty("notebook_ext_info", out var ne) && ne.ValueKind == JsonValueKind.Object)
            notebookId = ne.TryGetProperty("notebook_id", out var nid) ? nid.GetString() ?? "" : "";
        return (type, url, headers, notebookId);
    }

    /// <summary>获取笔记内容（纯文本，需为本人笔记）</summary>
    public async Task<string> GetNoteContentAsync(string noteId, CancellationToken ct)
    {
        var data = await PostAsync("openapi/note/v1/get_doc_content",
            new { note_id = noteId, target_content_format = 0 }, ct);
        return data.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
    }
}

public sealed class KbInfo
{
    public string Id { get; }
    public string Name { get; }
    public KbInfo(string id, string name) { Id = id; Name = string.IsNullOrWhiteSpace(name) ? id : name; }
    public override string ToString() => Name;   // ComboBox 直接显示知识库名称
}

/// <summary>知识库条目</summary>
public sealed class ItemInfo
{
    public string MediaId { get; }
    public string Title { get; }
    public int MediaType { get; }

    public ItemInfo(string mediaId, string title, int mediaType)
    {
        MediaId = mediaId;
        Title = CleanTitle(title);
        MediaType = mediaType;
    }

    private static string CleanTitle(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return "(未命名)";
        t = System.Text.RegularExpressions.Regex.Replace(t, "<[^>]+>", " ");
        t = System.Net.WebUtility.HtmlDecode(t);
        t = System.Text.RegularExpressions.Regex.Replace(t, @"\s+", " ").Trim();
        return t.Length == 0 ? "(未命名)" : t;
    }

    public bool IsFolder => MediaType == 99 || MediaId.StartsWith("folder_");

    public string TypeName => MediaType switch
    {
        99 => "文件夹",
        1 => "PDF",
        2 => "网页",
        3 => "Word",
        4 => "PPT",
        5 => "Excel",
        6 => "公众号文章",
        7 => "Markdown",
        9 => "图片",
        11 => "笔记",
        12 => "AI会话",
        13 => "TXT",
        14 => "Xmind",
        15 => "录音",
        16 => "视频",
        20 => "HTML",
        _ => $"类型{MediaType}"
    };
}
