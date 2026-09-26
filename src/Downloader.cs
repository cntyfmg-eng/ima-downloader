using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ImaDownloader;

/// <summary>按资源类型分类下载：网页→PDF / 网盘→本机下载器 / 文档→原格式 / 笔记→TXT</summary>
public static class Downloader
{
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        AllowAutoRedirect = true
    })
    { Timeout = TimeSpan.FromMinutes(10) };

    /// <summary>常见网盘域名特征</summary>
    private static readonly (string name, Regex rx)[] NetDisks =
    {
        ("百度网盘", new Regex(@"pan\.baidu\.com", RegexOptions.IgnoreCase)),
        ("夸克网盘", new Regex(@"pan\.quark\.cn", RegexOptions.IgnoreCase)),
        ("阿里云盘", new Regex(@"(alipan|aliyundrive)\.com", RegexOptions.IgnoreCase)),
        ("天翼云盘", new Regex(@"cloud\.189\.cn", RegexOptions.IgnoreCase)),
        ("迅雷云盘", new Regex(@"pan\.xunlei\.com", RegexOptions.IgnoreCase)),
        ("蓝奏云", new Regex(@"lanzou[a-z]?\.com", RegexOptions.IgnoreCase)),
        ("123云盘", new Regex(@"123pan\.com", RegexOptions.IgnoreCase)),
        ("腾讯微云", new Regex(@"(share\.weiyun|pan\.weiyun)\.com", RegexOptions.IgnoreCase)),
        ("中国移动云盘", new Regex(@"caiyun\.139\.com", RegexOptions.IgnoreCase)),
        ("115网盘", new Regex(@"115\.com", RegexOptions.IgnoreCase)),
        ("文叔叔", new Regex(@"wenshushu\.cn", RegexOptions.IgnoreCase)),
    };

    // ---------- 文件名处理 ----------

    public static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "未命名";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.Trim())
            sb.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        var s = sb.ToString().TrimEnd('.', ' ');
        if (s.Length > 80) s = s[..80];
        return string.IsNullOrWhiteSpace(s) ? "未命名" : s;
    }

    private static string UniquePath(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;
        var ext = Path.GetExtension(fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (int i = 1; i < 999; i++)
        {
            path = Path.Combine(dir, $"{stem}({i}){ext}");
            if (!File.Exists(path)) return path;
        }
        return path;
    }

    private static string ExtFromContentType(string? ct) => (ct ?? "").ToLowerInvariant() switch
    {
        var x when x.Contains("application/pdf") => "pdf",
        var x when x.Contains("wordprocessingml") => "docx",
        var x when x.Contains("msword") => "doc",
        var x when x.Contains("presentationml") => "pptx",
        var x when x.Contains("ms-powerpoint") => "ppt",
        var x when x.Contains("spreadsheetml") => "xlsx",
        var x when x.Contains("ms-excel") => "xls",
        var x when x.Contains("text/csv") => "csv",
        var x when x.Contains("markdown") => "md",
        var x when x.Contains("text/html") => "html",
        var x when x.Contains("text/plain") => "txt",
        var x when x.Contains("image/png") => "png",
        var x when x.Contains("image/jpeg") => "jpg",
        var x when x.Contains("image/webp") => "webp",
        var x when x.Contains("image/gif") => "gif",
        var x when x.Contains("audio/mpeg") => "mp3",
        var x when x.Contains("x-m4a") => "m4a",
        var x when x.Contains("audio/wav") => "wav",
        var x when x.Contains("audio/aac") => "aac",
        var x when x.Contains("x-xmind") => "xmind",
        var x when x.Contains("application/zip") => "zip",
        _ => ""
    };

    // ---------- 主入口 ----------

    /// <summary>下载一个条目，返回结果描述</summary>
    public static async Task<string> DownloadAsync(ImaClient client, ItemInfo item, string downloadDir,
        CancellationToken ct, Action<int>? progress = null)
    {
        Directory.CreateDirectory(downloadDir);

        // 笔记：导出为 TXT
        if (item.MediaType == 11)
        {
            progress?.Invoke(10);
            var (_, _, _, notebookId) = await client.GetMediaInfoAsync(item.MediaId, ct);
            if (string.IsNullOrEmpty(notebookId))
                throw new Exception("未获取到笔记 ID（可能不是本人创建的笔记）");
            progress?.Invoke(40);
            var content = await client.GetNoteContentAsync(notebookId, ct);
            var path = UniquePath(downloadDir, SanitizeName(item.Title) + ".txt");
            await File.WriteAllTextAsync(path, content, System.Text.Encoding.UTF8, ct);
            progress?.Invoke(100);
            return "笔记已导出 → " + Path.GetFileName(path);
        }

        var (mediaType, url, headers, _) = await client.GetMediaInfoAsync(item.MediaId, ct);
        if (string.IsNullOrWhiteSpace(url))
            throw new Exception("ima 未提供该资源的下载链接，请在 ima 客户端中查看原文");

        ct.ThrowIfCancellationRequested();

        // 先看 URL 是否指向文件（按扩展名）
        string urlExt = GetUrlExtension(url);
        bool looksLikeFile = !string.IsNullOrEmpty(urlExt) && urlExt != "html" && urlExt != "htm";

        if (looksLikeFile)
        {
            progress?.Invoke(20);
            var path = await DownloadFileAsync(url, headers, downloadDir, SanitizeName(item.Title), urlExt, ct, progress);
            return "文件已下载 → " + Path.GetFileName(path);
        }

        // 网页类：探测 Content-Type
        progress?.Invoke(20);
        using var probe = await ProbeAsync(url, headers, ct);
        var contentType = probe.ContentType;
        if (!contentType.StartsWith("text/html"))
        {
            string ext = ExtFromContentType(contentType);
            if (string.IsNullOrEmpty(ext)) ext = urlExt is "php" or "aspx" or "" ? "bin" : urlExt;
            var path = await DownloadFileAsync(url, headers, downloadDir, SanitizeName(item.Title), ext, ct, progress, probe.Response);
            return "文件已下载 → " + Path.GetFileName(path);
        }

        // HTML 页面：先判断是否网盘链接
        foreach (var (name, rx) in NetDisks)
        {
            if (rx.IsMatch(url))
            {
                progress?.Invoke(60);
                return OpenNetDisk(name, url);
            }
        }

        // 普通网页 / 公众号文章 → Edge 无头打印为 PDF（失败时兜底保存 HTML）
        progress?.Invoke(40);
        var pdfPath = UniquePath(downloadDir, SanitizeName(item.Title) + ".pdf");
        try
        {
            await PrintToPdfAsync(url, pdfPath, ct);
            progress?.Invoke(100);
            return "网页已保存为 PDF → " + Path.GetFileName(pdfPath);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception pdfEx)
        {
            try
            {
                var htmlPath = await SaveHtmlAsync(url, headers, downloadDir, SanitizeName(item.Title), ct);
                progress?.Invoke(100);
                return $"PDF 转换失败（{pdfEx.Message}），已保存网页 HTML → " + Path.GetFileName(htmlPath);
            }
            catch (Exception htmlEx)
            {
                throw new Exception($"网页转 PDF 失败：{pdfEx.Message}；HTML 兜底也失败：{htmlEx.Message}");
            }
        }
    }

    private static string GetUrlExtension(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath;
            var m = Regex.Match(path, @"\.([A-Za-z0-9]{1,8})$");
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() : "";
        }
        catch { return ""; }
    }

    // ---------- 文件下载 ----------

    private sealed class ProbeResult(HttpResponseMessage response, string contentType) : IDisposable
    {
        public HttpResponseMessage Response { get; } = response;
        public string ContentType { get; } = contentType;
        public void Dispose() => Response.Dispose();
    }

    private static async Task<ProbeResult> ProbeAsync(string url, Dictionary<string, string> headers, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(req, headers, url);
        var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var ctType = resp.Content.Headers.ContentType?.MediaType ?? "";
        return new ProbeResult(resp, ctType);
    }

    private static void ApplyHeaders(HttpRequestMessage req, Dictionary<string, string> headers, string url)
    {
        if (headers.Count == 0)
        {
            // 无特殊凭证时给通用 UA（部分站点校验）
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36");
        }
        foreach (var kv in headers)
        {
            if (kv.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
            req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
    }

    private static async Task<string> DownloadFileAsync(string url, Dictionary<string, string> headers,
        string dir, string baseName, string ext, CancellationToken ct, Action<int>? progress,
        HttpResponseMessage? reuseResponse = null)
    {
        HttpResponseMessage resp;
        bool dispose = false;
        if (reuseResponse != null) { resp = reuseResponse; }
        else
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyHeaders(req, headers, url);
            resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            dispose = true;
        }
        try
        {
            resp.EnsureSuccessStatusCode();

            // 优先用响应头里的文件名
            string extFinal = ext;
            var cd = resp.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                     ?? resp.Content.Headers.ContentDisposition?.FileName?.Trim('"');
            if (!string.IsNullOrEmpty(cd) && Path.HasExtension(cd))
                extFinal = Path.GetExtension(cd).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(extFinal))
                extFinal = ExtFromContentType(resp.Content.Headers.ContentType?.MediaType);
            if (string.IsNullOrEmpty(extFinal)) extFinal = "bin";

            var path = UniquePath(dir, baseName + "." + extFinal);
            long? total = resp.Content.Headers.ContentLength;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long readTotal = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                readTotal += n;
                if (total.HasValue && total.Value > 0)
                    progress?.Invoke(Math.Min(95, 20 + (int)(readTotal * 75 / total.Value)));
            }
            progress?.Invoke(100);
            return path;
        }
        finally { if (dispose) resp.Dispose(); }
    }

    // ---------- 网盘 ----------

    private static string OpenNetDisk(string diskName, string url)
    {
        try { Clipboard.SetText(url); } catch { /* 忽略剪贴板失败 */ }

        // 依次寻找本机下载器
        var idmCandidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Internet Download Manager", "IDMan.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Internet Download Manager", "IDMan.exe"),
        };
        foreach (var idm in idmCandidates)
        {
            if (File.Exists(idm))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = idm,
                        Arguments = $"/d \"{url}\" /n",
                        UseShellExecute = true
                    });
                    return $"{diskName}链接已发送到 IDM 下载器（链接同时已复制到剪贴板）";
                }
                catch { /* 继续降级 */ }
            }
        }

        // 无 IDM：用默认浏览器打开网盘页面（网盘需要登录，浏览器可接管下载）
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new Exception($"打开浏览器失败：{ex.Message}。链接已复制到剪贴板，请手动粘贴下载。");
        }
        return $"{diskName}链接已在浏览器打开，请登录网盘后下载（链接已复制到剪贴板）";
    }

    // ---------- 网页转 PDF（Edge 无头 + CDP：等图片加载完再打印） ----------

    private static string? FindBrowser()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string NewProfileDir()
        => Path.Combine(Path.GetTempPath(), "ima_pdf_" + Guid.NewGuid().ToString("N")[..12]);

    private static async Task PrintToPdfAsync(string url, string pdfPath, CancellationToken ct)
    {
        var browser = FindBrowser()
            ?? throw new Exception("未找到 Edge/Chrome 浏览器");

        Exception? cdpErr = null;
        try
        {
            await PrintToPdfViaCdpAsync(browser, url, pdfPath, ct);
            return;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { cdpErr = ex; }

        // 兜底：旧命令行 --print-to-pdf（无法控制图片等待时机，仅在 CDP 失败时使用）
        try
        {
            await PrintToPdfByCommandLineAsync(browser, url, pdfPath, ct);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new Exception($"CDP 方式（{cdpErr?.Message}）与命令行方式（{ex.Message}）均失败");
        }
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try { return ((System.Net.IPEndPoint)l.LocalEndpoint).Port; }
        finally { l.Stop(); }
    }

    /// <summary>等待页面图片全部加载完成的注入脚本（滚动触发懒加载 + 轮询 img.complete）</summary>
    private const string WaitForImagesScript = @"
(async () => {
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  try {
    const H = () => Math.max(document.body ? document.body.scrollHeight : 0,
                             document.documentElement ? document.documentElement.scrollHeight : 0);
    for (let y = 0; y < H() + 700; y += 700) { window.scrollTo(0, y); await sleep(150); }
    window.scrollTo(0, 0); await sleep(300);
    const t0 = Date.now();
    while (Date.now() - t0 < 30000) {
      const imgs = Array.from(document.images || []);
      const pending = imgs.filter(i =>
        (!i.complete && i.src && !i.src.startsWith('data:')) ||   // 正在加载
        (i.dataset && i.dataset.src && i.src !== i.dataset.src)   // data-src 懒加载未替换
      );
      if (imgs.length === 0 || pending.length === 0) break;
      await sleep(400);
    }
    await sleep(400);
    return true;
  } catch (e) { return false; }
})()";

    private static async Task PrintToPdfViaCdpAsync(string browser, string url, string pdfPath, CancellationToken ct)
    {
        int port = GetFreePort();
        var profile = NewProfileDir();
        var tmpPdf = pdfPath + ".tmp.pdf";
        Process? proc = null;
        try
        {
            Directory.CreateDirectory(profile);
            var psi = new ProcessStartInfo
            {
                FileName = browser,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--disable-extensions",
                "--disable-background-networking", "--no-default-browser-check",
                "--remote-debugging-port=" + port, "--user-data-dir=" + profile, "about:blank"
            }) psi.ArgumentList.Add(a);
            proc = Process.Start(psi) ?? throw new Exception("浏览器进程启动失败");
            using var regKill = ct.Register(() => { try { proc.Kill(true); } catch { } });

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

            // 等 DevTools 就绪
            for (int i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i >= 60) throw new Exception("DevTools 端口未就绪");
                try
                {
                    using var resp = await http.GetAsync($"http://127.0.0.1:{port}/json/version", ct);
                    if (resp.IsSuccessStatusCode) break;
                }
                catch (OperationCanceledException) { throw; }
                catch { await Task.Delay(250, ct); }
                await Task.Delay(250, ct);
            }

            // 创建空白标签页（新版 Chromium 要求 PUT；query 直接是 URL，无 url= 键名）
            string targetJson;
            using (var req = new HttpRequestMessage(HttpMethod.Put,
                       $"http://127.0.0.1:{port}/json/new?about:blank"))
            {
                using var resp = await http.SendAsync(req, ct);
                resp.EnsureSuccessStatusCode();
                targetJson = await resp.Content.ReadAsStringAsync(ct);
            }
            using var targetDoc = JsonDocument.Parse(targetJson);
            var wsUrl = targetDoc.RootElement.GetProperty("webSocketDebuggerUrl").GetString()
                        ?? throw new Exception("未获取到 WebSocket 调试地址");

            // 连接 CDP
            using var ws = new System.Net.WebSockets.ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);

            int msgId = 0;
            async Task<JsonElement> Send(string method, object? parms, bool expectReply = true)
            {
                int id = ++msgId;
                var obj = new Dictionary<string, object?> { ["id"] = id, ["method"] = method };
                if (parms != null) obj["params"] = parms;
                var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);
                await ws.SendAsync(bytes, System.Net.WebSockets.WebSocketMessageType.Text, true, ct);
                if (!expectReply) return default;
                return await RecvUntil(ws, id, null, TimeSpan.FromSeconds(40), ct);
            }

            static async Task<string> RecvWs(System.Net.WebSockets.ClientWebSocket ws, CancellationToken ct)
            {
                var ms = new MemoryStream();
                var buf = new byte[65536];
                while (true)
                {
                    var r = await ws.ReceiveAsync(buf, ct);
                    if (r.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                        throw new Exception("调试连接已关闭");
                    ms.Write(buf, 0, r.Count);
                    if (r.EndOfMessage) return Encoding.UTF8.GetString(ms.ToArray());
                }
            }

            static async Task<JsonElement> RecvUntil(System.Net.WebSockets.ClientWebSocket ws, int? id, string? eventName,
                TimeSpan timeout, CancellationToken ct)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout);
                while (true)
                {
                    var text = await RecvWs(ws, cts.Token);
                    using var doc = JsonDocument.Parse(text);
                    var root = doc.RootElement;
                    if (id.HasValue && root.TryGetProperty("id", out var mid) && mid.GetInt32() == id)
                    {
                        if (root.TryGetProperty("error", out var err))
                            throw new Exception("CDP 错误: " + (err.TryGetProperty("message", out var em) ? em.GetString() : "?"));
                        return root.TryGetProperty("result", out var res) ? res.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
                    }
                    if (eventName != null && root.TryGetProperty("method", out var m) && m.GetString() == eventName)
                        return root.Clone();
                }
            }

            try { await Send("Page.enable", null); } catch { }

            // 连接就绪后再导航（避免 target 初始 about:blank 的时序问题）
            await Send("Page.navigate", new { url });

            // 轮询：目标页导航完成（readyState=complete 且已离开 about:blank）
            for (int i = 0; ; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i >= 80) break;
                try
                {
                    var rs = await Send("Runtime.evaluate",
                        new { expression = "JSON.stringify({s: document.readyState, u: location.href})", returnByValue = true });
                    var raw = rs.TryGetProperty("result", out var rr) && rr.TryGetProperty("value", out var rv)
                        ? rv.GetString() : "";
                    using var stDoc = JsonDocument.Parse(raw ?? "{}");
                    var s = stDoc.RootElement.TryGetProperty("s", out var sv) ? sv.GetString() : "";
                    var u = stDoc.RootElement.TryGetProperty("u", out var uv) ? uv.GetString() : "";
                    if (s == "complete" && !string.IsNullOrEmpty(u) && !u.Contains("about:blank")) break;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* 导航中执行上下文会被销毁，重试即可 */ }
                await Task.Delay(500, ct);
            }

            // 滚动触发懒加载 + 等全部图片加载完成
            try
            {
                await Send("Runtime.evaluate", new
                {
                    expression = WaitForImagesScript,
                    awaitPromise = true,
                    returnByValue = true
                });
            }
            catch (OperationCanceledException) { throw; }
            catch { /* 图片等待失败不阻断，尽量打印 */ }

            // 打印 PDF（printBackground 保证图片/背景完整渲染）
            var pdfResult = await Send("Page.printToPDF", new { printBackground = true });
            var b64 = pdfResult.TryGetProperty("data", out var d) ? d.GetString() : null;
            if (string.IsNullOrEmpty(b64)) throw new Exception("PDF 输出为空");
            if (File.Exists(tmpPdf)) File.Delete(tmpPdf);
            await File.WriteAllBytesAsync(tmpPdf, Convert.FromBase64String(b64), ct);
            if (File.Exists(pdfPath)) File.Delete(pdfPath);
            File.Move(tmpPdf, pdfPath);
        }
        finally
        {
            try { if (File.Exists(tmpPdf)) File.Delete(tmpPdf); } catch { }
            try { proc?.Kill(true); } catch { }
            try { proc?.Dispose(); } catch { }
            try { Directory.Delete(profile, true); } catch { }
        }
    }

    private static async Task PrintToPdfByCommandLineAsync(string browser, string url, string pdfPath, CancellationToken ct)
    {
        var tmpPdf = pdfPath + ".tmp.pdf";
        var profile = NewProfileDir();
        string[][] attempts =
        {
            new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--disable-extensions",
                    "--run-all-compositor-stages-before-draw", "--virtual-time-budget=15000" },
            new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--disable-extensions" }
        };
        try
        {
            Directory.CreateDirectory(profile);
            Exception? lastErr = null;
            foreach (var extraArgs in attempts)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    if (File.Exists(tmpPdf)) File.Delete(tmpPdf);
                    var psi = new ProcessStartInfo { FileName = browser, UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in extraArgs) psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add("--user-data-dir=" + profile);
                    psi.ArgumentList.Add("--print-to-pdf=" + tmpPdf);
                    psi.ArgumentList.Add("--no-pdf-header-footer");
                    psi.ArgumentList.Add(url);
                    using var proc = Process.Start(psi) ?? throw new Exception("浏览器进程启动失败");
                    using var reg = ct.Register(() => { try { proc.Kill(true); } catch { } });
                    await proc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(90), ct);
                    if (File.Exists(tmpPdf) && new FileInfo(tmpPdf).Length > 0)
                    {
                        if (File.Exists(pdfPath)) File.Delete(pdfPath);
                        File.Move(tmpPdf, pdfPath);
                        return;
                    }
                    lastErr = new Exception("页面可能需要登录或加载超时");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is not OperationCanceledException) { lastErr = ex; }
            }
            throw lastErr ?? new Exception("转换失败");
        }
        finally
        {
            try { if (File.Exists(tmpPdf)) File.Delete(tmpPdf); } catch { }
            try { Directory.Delete(profile, true); } catch { }
        }
    }

    /// <summary>兜底：直接抓取网页 HTML 原文保存</summary>
    private static async Task<string> SaveHtmlAsync(string url, Dictionary<string, string> headers,
        string dir, string baseName, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyHeaders(req, headers, url);
        using var resp = await Http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        var path = UniquePath(dir, baseName + ".html");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }
}
