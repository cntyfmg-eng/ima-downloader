using System.Text.Json;

namespace ImaDownloader;

/// <summary>设置对话框：ima 凭证 + 下载目录</summary>
public sealed class SettingsDialog : Form
{
    private readonly TextBox _cid = new() { Width = 420 };
    private readonly TextBox _key = new() { Width = 420, UseSystemPasswordChar = true };
    private readonly TextBox _dir = new() { Width = 340 };
    private Label _note;   // 底部说明文字（清除后变为提示）

    /// <summary>是否执行了「清除凭证并保存」</summary>
    public bool Cleared { get; private set; }

    public SettingsDialog(string downloadDir)
    {
        Text = "设置";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, 210);

        var (cid, key, _) = ImaClient.LoadConfig();

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 4,
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        table.Controls.Add(new Label { Text = "Client ID：", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 0);
        _cid.Text = cid;
        table.Controls.Add(_cid, 1, 0);

        table.Controls.Add(new Label { Text = "API Key：", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 1);
        _key.Text = key;
        table.Controls.Add(_key, 1, 1);

        table.Controls.Add(new Label { Text = "下载目录：", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, 0, 2);
        var dirPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _dir.Text = downloadDir;
        dirPanel.Controls.Add(_dir);
        var btnBrowse = new Button { Text = "浏览...", AutoSize = true };
        btnBrowse.Click += (_, _) =>
        {
            using var fbd = new FolderBrowserDialog { SelectedPath = _dir.Text };
            if (fbd.ShowDialog(this) == DialogResult.OK) _dir.Text = fbd.SelectedPath;
        };
        dirPanel.Controls.Add(btnBrowse);
        table.Controls.Add(dirPanel, 1, 2);

        _note = new Label
        {
            Text = "凭证获取：ima.qq.com/agent-interface 申请后填写；\r\n留空则自动读取 ~/.config/ima/ 下的 ima-skill 凭证。",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(3, 6, 3, 3)
        };
        table.Controls.Add(_note, 1, 3);
        Controls.Add(table);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        var ok = new Button { Text = "保存并连接", DialogResult = DialogResult.OK, Width = 110 };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Width = 70 };
        var btnClear = new Button { Text = "清除凭证并保存", Width = 130 };
        btnClear.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    "确定清除本工具保存的 Client ID 与 API Key 吗？\r\n清除后将不再使用任何凭证（也不再回退读取 ~/.config/ima/ 全局凭证），\r\n需重新填写后才能下载数据。",
                    "清除凭证", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { ImaClient.SaveConfig("", "", _dir.Text.Trim(), credentialCleared: true); } catch { }
            _cid.Text = "";
            _key.Text = "";
            Cleared = true;
            // 不关闭对话框：可随时填写新凭证再次「保存并连接」，或直接关闭本窗口
            _note.ForeColor = Color.FromArgb(0, 128, 96);
            _note.Text = "个人凭证已彻底清除并保存（不再读取任何全局凭证）。\r\n可随时填写新凭证后点「保存并连接」，或直接关闭本窗口。";
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(btnClear);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public (string clientId, string apiKey, string downloadDir) GetResult()
        => (_cid.Text.Trim(), _key.Text.Trim(), _dir.Text.Trim());
}

/// <summary>图标与嵌入图片资源</summary>
public static class AppIcon
{
    private static Icon? _windowIcon;

    /// <summary>窗口左上角图标（wk-logo.png）</summary>
    public static Icon WindowIcon => _windowIcon ??= LoadWindowIcon();

    private static Icon LoadWindowIcon()
    {
        try
        {
            using var img = LoadEmbeddedImage("wk-logo.png");
            if (img is Bitmap bmp) return Icon.FromHandle(bmp.GetHicon());
        }
        catch { }
        return SystemIcons.Application;
    }

    /// <summary>读取编译时嵌入的图片资源（qr.jpg / wk-logo.png）</summary>
    public static Image? LoadEmbeddedImage(string fileName)
    {
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream("ImaDownloader." + fileName);
            if (s == null) return null;
            return Image.FromStream(s);
        }
        catch { return null; }
    }
}
