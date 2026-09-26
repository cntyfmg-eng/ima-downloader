namespace ImaExtInstaller;

public sealed class MainForm : Form
{
    public MainForm()
    {
        Text = "ima 知识库下载器 - 浏览器加载项安装器  by数智化教学大冯老师@冯萌刚";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        ClientSize = new Size(520, 300);
        Icon = Program.AppIcon;

        var tip1 = new Label
        {
            Text = "ima 桌面客户端为封闭扩展系统（仅允许官方扩展），因此本加载项\r\n安装到 Edge 浏览器，在 ima 网页版（ima.qq.com）中直接下载知识库资料。",
            Location = new Point(16, 14), AutoSize = true, ForeColor = Color.FromArgb(0, 100, 75)
        };

        var tip2 = new Label
        {
            Text = "安装步骤（约 30 秒）：\r\n  1. 点下方「安装加载项」—— 扩展会释放到本机并自动打开 Edge 扩展页\r\n  2. 在 Edge 扩展页左侧打开「开发人员模式」开关\r\n  3. 点「加载解压缩的扩展」，选择提示的目录，完成！\r\n之后打开 ima.qq.com 网页版，页面右下角即出现绿色下载按钮。",
            Location = new Point(16, 62), AutoSize = true, ForeColor = Color.DimGray
        };

        var btnInstall = new Button
        {
            Text = "安装加载项", Location = new Point(16, 200), Width = 130, Height = 32,
            BackColor = Color.FromArgb(0, 120, 90), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        btnInstall.FlatAppearance.BorderSize = 0;
        btnInstall.Click += (_, _) => DoInstall();

        var btnIma = new Button { Text = "打开 ima 网页版", Location = new Point(156, 200), Width = 130, Height = 32 };
        btnIma.Click += (_, _) => Program.OpenImaWeb();

        var btnRemove = new Button { Text = "卸载", Location = new Point(296, 200), Width = 80, Height = 32 };
        btnRemove.Click += (_, _) =>
        {
            var err = Program.Uninstall();
            MessageBox.Show(this, err ?? "已卸载。如 Edge 扩展列表中仍有条目，请手动移除。", err == null ? "完成" : "出错",
                MessageBoxButtons.OK, err == null ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        };

        var status = new Label
        {
            Text = Program.IsInstalled() ? "状态：已安装（" + Program.DeployDir + "）" : "状态：未安装",
            Location = new Point(16, 248), AutoSize = true, ForeColor = Color.Gray
        };

        Controls.AddRange(new Control[] { tip1, tip2, btnInstall, btnIma, btnRemove, status });
        AcceptButton = btnInstall;
    }

    private void DoInstall()
    {
        var err = Program.Install();
        if (err != null)
        {
            MessageBox.Show(this, "安装失败：" + err, "出错", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Program.OpenEdgeExtensions();
        MessageBox.Show(this,
            "扩展已释放到：\r\n" + Program.DeployDir + "\r\n\r\n" +
            "接下来在已打开的 Edge 扩展页：\r\n  1. 打开左侧「开发人员模式」\r\n  2. 点「加载解压缩的扩展」\r\n  3. 选择上面的目录\r\n\r\n" +
            "然后打开 ima.qq.com 登录，右下角出现绿色下载按钮即可使用。",
            "第一步完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }
}
