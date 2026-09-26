using System.Diagnostics;

namespace ImaExtInstaller;

public static class Program
{
    public const string ExtId = "nhadmaebaahcleclmgchnacnlejhkhpi";
    public const string ExtVersion = "1.1.0";

    /// <summary>扩展部署目录</summary>
    public static string DeployDir
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DafengImaDownloader", "extension");

    [STAThread]
    public static void Main(string[] args)
    {
        bool silent = args.Any(a => a is "--silent" or "/silent" or "-s");
        if (silent) { RunConsole(); return; }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }

    // ---------- 资源清单 ----------
    public static readonly (string res, string file)[] Files =
    {
        ("Installer.manifest", "manifest.json"),
        ("Installer.sw", "sw.js"),
        ("Installer.content", "content.js"),
        ("Installer.downloader", "downloader.html"),
        ("Installer.downloaderjs", "downloader.js"),
        ("Installer.icon16", "icon16.png"),
        ("Installer.icon48", "icon48.png"),
        ("Installer.icon128", "icon128.png"),
    };

    public static byte[] LoadResource(string name)
    {
        using var s = typeof(Program).Assembly.GetManifestResourceStream(name);
        if (s == null) throw new Exception("缺少内嵌资源：" + name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    public static Icon AppIcon
    {
        get
        {
            try
            {
                using var img = Image.FromStream(new MemoryStream(LoadResource("Installer.wklogo")));
                if (img is Bitmap bmp) return Icon.FromHandle(bmp.GetHicon());
            }
            catch { }
            return SystemIcons.Application;
        }
    }

    /// <summary>部署扩展目录：返回错误消息，null=成功</summary>
    public static string? Install()
    {
        try
        {
            foreach (var (res, file) in Files)
            {
                var p = Path.Combine(DeployDir, file);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllBytes(p, LoadResource(res));
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static string? Uninstall()
    {
        try
        {
            if (Directory.Exists(Path.GetDirectoryName(DeployDir)))
                Directory.Delete(Path.GetDirectoryName(DeployDir)!, true);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static bool IsInstalled() => File.Exists(Path.Combine(DeployDir, "manifest.json"));

    /// <summary>打开 Edge 扩展管理页</summary>
    public static void OpenEdgeExtensions()
    {
        var edge = new[] {
            Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\\Microsoft\\Edge\\Application\\msedge.exe"),
            Environment.ExpandEnvironmentVariables("%ProgramFiles%\\Microsoft\\Edge\\Application\\msedge.exe")
        }.FirstOrDefault(File.Exists);
        if (edge != null)
            Process.Start(new ProcessStartInfo { FileName = edge, UseShellExecute = true,
                Arguments = "edge://extensions/ --profile-directory=Default" });
        else
            Process.Start(new ProcessStartInfo { FileName = "https://ima.qq.com/", UseShellExecute = true });
    }

    public static void OpenImaWeb() => Process.Start(new ProcessStartInfo { FileName = "https://ima.qq.com/", UseShellExecute = true });

    private static void RunConsole()
    {
        var err = Install();
        Console.WriteLine(err == null ? "OK: 扩展已部署到 " + DeployDir : "ERR: " + err);
        if (err == null) OpenEdgeExtensions();
        Environment.Exit(err == null ? 0 : 1);
    }
}
