using System.Diagnostics;
using System.Drawing;
using System.Text.Json;

namespace ImaDownloader;

public partial class MainForm : Form
{
    private ImaClient? _client;
    private KbInfo? _currentKb;
    private string _currentFolderId = "";       // 空 = 根目录
    private readonly Stack<(string folderId, string name)> _history = new();
    private List<ItemInfo> _currentItems = new();

    private CancellationTokenSource? _cts;

    private ComboBox _kbBox = null!;
    private Button _btnRefresh = null!;
    private Button _btnBack = null!;
    private Label _breadcrumb = null!;
    private ListView _list = null!;
    private TextBox _log = null!;
    private ToolStripProgressBar _progress = null!;
    private ToolStripStatusLabel _status = null!;
    private Button _btnDownloadSelected = null!;
    private Button _btnSettings = null!;
    private Button _btnOpenDir = null!;

    public string DownloadDir { get; private set; }

    public MainForm()
    {
        Text = "ima知识库下载器  by数智化教学大冯老师@冯萌刚";
        Font = new Font("Microsoft YaHei UI", 9F);
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1040, 760);
        MinimumSize = new Size(820, 580);
        Icon = AppIcon.WindowIcon;   // 左上角 wk-logo 图标

        var (cid, key, dir) = ImaClient.LoadConfig();
        DownloadDir = string.IsNullOrWhiteSpace(dir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ima知识库下载")
            : dir;

        BuildUi();
        Load += async (_, _) => await InitAsync(cid, key);
    }

    // ================= UI 构建 =================

    private void BuildUi()
    {
        // 根表格布局：工具栏 / 面包屑 / 列表 / 按钮条 / 日志 / 状态栏
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));    // 0 工具栏
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));    // 1 面包屑
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // 2 列表
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));    // 3 按钮条
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));   // 4 日志
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));    // 5 状态栏
        Controls.Add(root);

        // ---- 0 工具栏 ----
        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 7, 8, 4),
            WrapContents = false
        };
        top.Controls.Add(new Label { Text = "知识库：", AutoSize = true, Margin = new Padding(3, 8, 0, 0) });
        _kbBox = new ComboBox { Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        _kbBox.SelectedIndexChanged += async (_, _) => await OnKbChangedAsync();
        top.Controls.Add(_kbBox);
        _btnRefresh = new Button { Text = "刷新", AutoSize = true };
        _btnRefresh.Click += async (_, _) => await ReloadAsync();
        top.Controls.Add(_btnRefresh);
        _btnBack = new Button { Text = "← 上一级", AutoSize = true, Enabled = false };
        _btnBack.Click += (_, _) => GoBack();
        top.Controls.Add(_btnBack);
        _btnSettings = new Button { Text = "设置...", AutoSize = true };
        _btnSettings.Click += (_, _) => ShowSettings();
        top.Controls.Add(_btnSettings);
        _btnOpenDir = new Button { Text = "打开下载目录", AutoSize = true };
        _btnOpenDir.Click += (_, _) => OpenDownloadDir();
        top.Controls.Add(_btnOpenDir);
        var btnMore = new Button
        {
            Text = "更多技能",
            AutoSize = true,
            BackColor = Color.FromArgb(0, 120, 90),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        btnMore.FlatAppearance.BorderSize = 0;
        btnMore.Click += (_, _) => ShowMoreSkillsDialog();
        top.Controls.Add(btnMore);
        root.Controls.Add(top, 0, 0);

        // ---- 1 面包屑 ----
        _breadcrumb = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 4, 0),
            Text = "（请选择知识库）",
            ForeColor = Color.DimGray
        };
        root.Controls.Add(_breadcrumb, 0, 1);

        // ---- 2 列表（带复选框，支持批量勾选） ----
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            CheckBoxes = true
        };
        _list.Columns.Add("全选→", 34);
        _list.Columns.Add("名称", 560);
        _list.Columns.Add("类型", 100);
        _list.Columns.Add("操作", 100);
        _list.ItemCheck += (_, e) =>
        {
            // 文件夹不允许勾选
            if (_list.Items[e.Index].Tag is ItemInfo it && it.IsFolder) e.NewValue = CheckState.Unchecked;
        };
        _list.DoubleClick += async (_, _) => await OnItemActivateAsync();
        _list.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) await OnItemActivateAsync(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("下载 / 打开", null, async (_, _) => await DownloadSelectedAsync());
        menu.Items.Add("进入文件夹", null, async (_, _) => await EnterSelectedFolderAsync());
        menu.Items.Add("全选/全不选", null, (_, _) => ToggleCheckAll());
        _list.ContextMenuStrip = menu;
        root.Controls.Add(_list, 0, 2);

        // ---- 3 按钮条 ----
        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 7, 8, 4),
            WrapContents = false
        };
        _btnDownloadSelected = new Button
        {
            Text = "⬇ 下载勾选项",
            AutoSize = true,
            BackColor = Color.FromArgb(0, 120, 90),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnDownloadSelected.FlatAppearance.BorderSize = 0;
        _btnDownloadSelected.Click += async (_, _) => await DownloadSelectedAsync();
        bottom.Controls.Add(_btnDownloadSelected);
        bottom.Controls.Add(new Label
        {
            Text = "提示：勾选多条后点上面按钮可批量下载；双击单条直接下载。网页→PDF，网盘→本机下载器，文档→原格式，笔记→TXT",
            AutoSize = true,
            ForeColor = Color.Gray,
            Margin = new Padding(12, 10, 0, 0)
        });
        root.Controls.Add(bottom, 0, 3);

        // ---- 4 日志 ----
        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F)
        };
        root.Controls.Add(_log, 0, 4);

        // ---- 5 状态栏 ----
        var statusStrip = new ToolStrip
        {
            Dock = DockStyle.Fill,
            GripStyle = ToolStripGripStyle.Hidden,
            AutoSize = false,
            Height = 26
        };
        _status = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _progress = new ToolStripProgressBar { Visible = false, Width = 180 };
        statusStrip.Items.Add(_status);
        statusStrip.Items.Add(_progress);
        root.Controls.Add(statusStrip, 0, 5);
    }

    private void ToggleCheckAll()
    {
        bool anyUnchecked = _list.Items.Cast<ListViewItem>()
            .Where(i => i.Tag is ItemInfo it && !it.IsFolder)
            .Any(i => !i.Checked);
        foreach (ListViewItem lvi in _list.Items)
            if (lvi.Tag is ItemInfo it && !it.IsFolder)
                lvi.Checked = anyUnchecked;
    }

    // ================= 初始化 / 加载 =================

    private async Task InitAsync(string cid, string key)
    {
        if (string.IsNullOrWhiteSpace(cid) || string.IsNullOrWhiteSpace(key))
        {
            Log("未找到 ima 凭证，请先在「设置」中填写 Client ID 与 API Key。");
            ShowSettings();
            return;
        }
        _client = new ImaClient(cid, key);
        try
        {
            SetBusy(true, "正在获取知识库列表...");
            var kbs = await _client.GetKnowledgeBasesAsync(CancellationToken.None);
            _kbBox.Items.Clear();
            foreach (var kb in kbs) _kbBox.Items.Add(kb);
            if (kbs.Count > 0)
            {
                _kbBox.SelectedIndex = 0;
                Log($"已登录，共 {kbs.Count} 个知识库。下载目录：{DownloadDir}");
            }
            else
            {
                Log("该账号下没有可访问的知识库。");
            }
        }
        catch (Exception ex)
        {
            Log("加载知识库失败：" + ex.Message);
            ShowSettings();
        }
        finally { SetBusy(false); }
    }

    private async Task OnKbChangedAsync()
    {
        _currentKb = _kbBox.SelectedItem as KbInfo;
        _history.Clear();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_client == null || _currentKb == null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            SetBusy(true, "正在加载内容...");
            _list.Items.Clear();
            var items = await _client.ListItemsAsync(_currentKb.Id, _currentFolderId, ct,
                new Progress<string>(s => _status.Text = s));
            _currentItems = items;
            foreach (var it in items)
            {
                var lvi = new ListViewItem("") { Tag = it };
                lvi.SubItems.Add(it.Title);
                lvi.SubItems.Add(it.TypeName);
                lvi.SubItems.Add(it.IsFolder ? "双击进入" : "双击下载");
                if (it.IsFolder) lvi.ForeColor = Color.FromArgb(0, 100, 160);
                _list.Items.Add(lvi);
            }
            _breadcrumb.Text = $"【{_currentKb.Name}】 / " + string.Join(" / ",
                _history.Reverse().Select(h => h.name)) + (items.Count == 0 ? "　（空）" : $"　共 {items.Count} 项");
            _btnBack.Enabled = _history.Count > 0;
            _status.Text = $"加载完成，共 {items.Count} 项";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log("加载失败：" + ex.Message);
            _status.Text = "加载失败";
        }
        finally { SetBusy(false); }
    }

    private void GoBack()
    {
        if (_history.Count == 0) return;
        (_currentFolderId, _) = _history.Pop();
        _ = ReloadAsync();
    }

    // ================= 浏览 / 下载 =================

    private async Task OnItemActivateAsync()
    {
        if (_list.SelectedItems.Count == 0) return;
        var item = (ItemInfo)_list.SelectedItems[0].Tag!;
        if (item.IsFolder)
        {
            _history.Push((_currentFolderId, item.Title));
            _currentFolderId = item.MediaId;
            await ReloadAsync();
        }
        else
        {
            await DownloadOneAsync(item);
        }
    }

    private async Task EnterSelectedFolderAsync()
    {
        if (_list.SelectedItems.Count == 0) return;
        var item = (ItemInfo)_list.SelectedItems[0].Tag!;
        if (!item.IsFolder) { Log("「" + item.Title + "」不是文件夹"); return; }
        _history.Push((_currentFolderId, item.Title));
        _currentFolderId = item.MediaId;
        await ReloadAsync();
    }

    /// <summary>批量下载：优先取勾选(复选框)项，无勾选时取当前选中项</summary>
    private async Task DownloadSelectedAsync()
    {
        var source = _list.CheckedItems.Count > 0
            ? _list.CheckedItems.Cast<ListViewItem>()
            : _list.SelectedItems.Cast<ListViewItem>();

        var targets = source
            .Select(l => (ItemInfo)l.Tag!)
            .Where(i => !i.IsFolder)
            .Distinct()
            .ToList();
        if (targets.Count == 0) { Log("请先在列表第一列勾选要下载的条目"); return; }
        await DownloadManyAsync(targets);
    }

    private async Task DownloadOneAsync(ItemInfo item) => await DownloadManyAsync(new List<ItemInfo> { item });

    private async Task DownloadManyAsync(List<ItemInfo> items)
    {
        if (_client == null) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _btnDownloadSelected.Enabled = false;

        int ok = 0, fail = 0;
        try
        {
            SetBusy(true, $"开始下载 {items.Count} 项...");
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                int index = i;
                IProgress<int> prog = new Progress<int>(p =>
                {
                    int overall = (int)((index + p / 100.0) / items.Count * 100);
                    _progress.Value = Math.Min(100, overall);
                    _status.Text = $"({index + 1}/{items.Count}) {item.Title} — {p}%";
                });
                try
                {
                    _status.Text = $"({i + 1}/{items.Count}) 正在处理：{item.Title}";
                    var msg = await Downloader.DownloadAsync(_client, item, DownloadDir, ct, p => { try { prog.Report(p); } catch { } });
                    ok++;
                    Log("[成功] " + item.Title + " ｜ " + msg);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    fail++;
                    Log("[失败] " + item.Title + " ｜ " + ex.Message);
                }
            }
            _status.Text = $"下载完成：成功 {ok}，失败 {fail}";
            Log($"── 批次结束：成功 {ok}，失败 {fail} ──");
        }
        catch (OperationCanceledException) { Log("下载已取消"); }
        catch (Exception ex) { Log("下载异常：" + ex.Message); }
        finally
        {
            SetBusy(false);
            _btnDownloadSelected.Enabled = true;
        }
    }

    // ================= 杂项 =================

    /// <summary>「更多技能」弹窗：二维码 + 引导文案</summary>
    private void ShowMoreSkillsDialog()
    {
        using var dlg = new Form
        {
            Text = "更多技能 — 数智化教学大冯老师",
            Font = new Font("Microsoft YaHei UI", 9F),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(360, 440),
            BackColor = Color.White
        };

        var qr = new PictureBox
        {
            Image = AppIcon.LoadEmbeddedImage("qr.jpg"),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(320, 320),
            Location = new Point(20, 15)
        };
        dlg.Controls.Add(qr);

        var tip = new Label
        {
            Text = "扫码关注「数智化教学大冯老师」\r\n随时获取必需的资源与技能",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 96, 72),
            Dock = DockStyle.Bottom,
            Height = 80
        };
        dlg.Controls.Add(tip);
        dlg.ShowDialog(this);
    }

    private void ShowSettings()
    {
        using var dlg = new SettingsDialog(DownloadDir);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var (cid, key, dir) = dlg.GetResult();
        DownloadDir = dir;
        // 清除后凭证仍为空 → 保持「已清除」状态落盘，之后不再回退读取 ~/.config/ima/ 全局凭证
        bool keepCleared = dlg.Cleared && string.IsNullOrWhiteSpace(cid) && string.IsNullOrWhiteSpace(key);
        try { ImaClient.SaveConfig(cid, key, dir, keepCleared); } catch { /* 配置写入失败不影响使用 */ }
        Log("配置已保存。");
        if (keepCleared)
        {
            cid = ""; key = "";
            Log("个人凭证已彻底清除并保存（不再读取任何全局凭证）。请在「设置」中填写新的 Client ID 与 API Key。");
        }
        _ = InitAsync(cid, key);
    }

    private void OpenDownloadDir()
    {
        try
        {
            Directory.CreateDirectory(DownloadDir);
            Process.Start(new ProcessStartInfo { FileName = DownloadDir, UseShellExecute = true });
        }
        catch (Exception ex) { Log("打开目录失败：" + ex.Message); }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _progress.Visible = busy;
        if (!busy) _progress.Value = 0;
        if (status != null) _status.Text = status;
        _kbBox.Enabled = !busy;
        _btnRefresh.Enabled = !busy;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
    }
}
