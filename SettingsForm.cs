using System.Diagnostics;
using System.Drawing.Text;
using System.Net;
using System.Net.Sockets;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public class SettingsForm : Form
    {
        private readonly AppConfig _config;
        private readonly GameMonitor _monitor;

        // ===== 草稿数据 =====
        private List<GameItem> _editGames;
        private List<GameUsage> _editUsage;

        // ===== 游戏页 =====
        private ListView listGames;
        private TextBox txtDisplayName, txtProcessName, txtRunMinutes, txtDailyLimit;
        private Button btnAdd, btnUpdate, btnDelete, btnResetUsage;
        private NumericUpDown numReminder, numCountdown;
        private bool _isRefreshingList = false;

        // ===== 系统页 =====
        private ComboBox cmbModifier, cmbKey;
        private Button btnApplyHotkey;
        private CheckBox chkStartup, chkHideTrayIcon, chkMouseTransparent;
        private CheckBox chkEnableRemote, chkRemoteAllowLan, chkEnableDaemon;
        private NumericUpDown numRemotePort;
        private Label lblRemoteHint, lblRemoteAddress;
        private ComboBox cmbMatchMode;
        private string _localIp;
        private TextBox txtNewPass, txtConfirmPass;
        private CheckBox chkClearPassword;

        // ===== 悬浮框页 =====
        private CheckBox chkOverlayEnabled, chkOverlaySnap, chkOverlayAutoHide, chkOverlayTopMost;
        private NumericUpDown numOverlayOpacity, numOverlayAutoHideSeconds;

        // ===== 字体设置页 =====
        private ComboBox cmbCountdownFont;
        private Button btnCountdownColor, btnCountdownBackColor, btnPreviewCountdown;
        private NumericUpDown numCountdownAlpha, numCountdownHeightPct, numCountdownWidthPct;
        private NumericUpDown numCountdownBackAlpha;
        private CheckBox chkCountdownShadow;
        private Color _pendingCountdownFg;
        private Color _pendingCountdownBg;

        private ComboBox cmbOverlayFont;
        private NumericUpDown numOverlayFontSize;
        private Button btnOverlayTextColor, btnOverlayBackColor;
        private Color _pendingOverlayFg;
        private Color _pendingOverlayBg;

        // ===== 底部按钮 =====
        private Button btnApply, btnOk, btnCancel;

        public event Action ConfigSaved;
        public event Func<uint, uint, bool> HotkeyApplyRequested;

        public SettingsForm(AppConfig config, GameMonitor monitor)
        {
            _config = config;
            _monitor = monitor;

            Text = "游戏监控 - 设置";
            AutoScaleMode = AutoScaleMode.Font;
            Width = 750;
            Height = 810;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Icon = SystemIcons.Shield;

            var tabs = new TabControl { Left = 10, Top = 10, Width = 720, Height = 700 };
            tabs.TabPages.Add(BuildGamesTab());
            tabs.TabPages.Add(BuildSystemTab());
            tabs.TabPages.Add(BuildOverlayTab());
            tabs.TabPages.Add(BuildFontTab());
            Controls.Add(tabs);

            btnApply = new Button { Text = "应用", Left = 430, Top = 725, Width = 90, Height = 32 };
            btnOk = new Button { Text = "确定", Left = 530, Top = 725, Width = 90, Height = 32 };
            btnCancel = new Button { Text = "取消", Left = 630, Top = 725, Width = 90, Height = 32 };
            Controls.Add(btnApply);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            btnApply.Click += (s, e) => SaveAndNotify(closeAfter: false);
            btnOk.Click += (s, e) => SaveAndNotify(closeAfter: true);
            btnCancel.Click += (s, e) => Close();
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            LoadFromConfig();
        }

        public void ReloadData()
        {
            LoadFromConfig();
        }

        // ================= 页签 1：游戏与时长 =================

        private TabPage BuildGamesTab()
        {
            var page = new TabPage("游戏与时长");
            int y = 10;

            page.Controls.Add(new Label { Text = "监控游戏列表（每 3 秒刷新状态）：", Left = 10, Top = y, Width = 400 });
            y += 22;

            listGames = new ListView
            {
                Left = 10,
                Top = y,
                Width = 680,
                Height = 160,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false
            };
            listGames.Columns.Add("状态", 60);
            listGames.Columns.Add("显示名称", 130);
            listGames.Columns.Add("关键字匹配", 120);
            listGames.Columns.Add("匹配方式", 90);
            listGames.Columns.Add("单次(分)", 65);
            listGames.Columns.Add("配额(分)", 65);
            listGames.Columns.Add("今日已用", 75);
            page.Controls.Add(listGames);
            y += 168;

            btnAdd = new Button { Text = "添加", Left = 10, Top = y, Width = 100 };
            btnUpdate = new Button { Text = "更新选中", Left = 120, Top = y, Width = 100 };
            btnDelete = new Button { Text = "删除选中", Left = 230, Top = y, Width = 100 };
            btnResetUsage = new Button { Text = "重置选中今日用量", Left = 340, Top = y, Width = 140 };
            page.Controls.Add(btnAdd);
            page.Controls.Add(btnUpdate);
            page.Controls.Add(btnDelete);
            page.Controls.Add(btnResetUsage);
            y += 36;

            var grpGame = new GroupBox { Text = "游戏信息", Left = 10, Top = y, Width = 680, Height = 115 };
            page.Controls.Add(grpGame);

            grpGame.Controls.Add(new Label { Text = "显示名称：", Left = 10, Top = 28, Width = 70 });
            txtDisplayName = new TextBox { Left = 85, Top = 25, Width = 140 };
            grpGame.Controls.Add(txtDisplayName);

            grpGame.Controls.Add(new Label { Text = "关键字匹配：", Left = 235, Top = 28, Width = 90 });
            txtProcessName = new TextBox { Left = 330, Top = 25, Width = 140 };
            grpGame.Controls.Add(txtProcessName);

            grpGame.Controls.Add(new Label { Text = "（包含模式：进程名或窗口标题包含关键字，\r\n如填 minecraft 可监控 java 进程）", Left = 480, Top = 18, Width = 195, Height = 35, ForeColor = Color.DimGray });

            grpGame.Controls.Add(new Label { Text = "单次运行：", Left = 10, Top = 68, Width = 70 });
            txtRunMinutes = new TextBox { Left = 85, Top = 65, Width = 60, Text = "60" };
            grpGame.Controls.Add(txtRunMinutes);
            grpGame.Controls.Add(new Label { Text = "分钟", Left = 150, Top = 68, Width = 40 });

            grpGame.Controls.Add(new Label { Text = "每日配额：", Left = 215, Top = 68, Width = 70 });
            txtDailyLimit = new TextBox { Left = 290, Top = 65, Width = 60, Text = "0" };
            grpGame.Controls.Add(txtDailyLimit);
            grpGame.Controls.Add(new Label { Text = "分钟 (0=不限)", Left = 355, Top = 68, Width = 100 });

            grpGame.Controls.Add(new Label { Text = "匹配方式：", Left = 455, Top = 68, Width = 70 });
            cmbMatchMode = new ComboBox
            {
                Left = 525,
                Top = 65,
                Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbMatchMode.Items.AddRange(new object[] { "包含（进程名/标题）", "精确进程名", "精确窗口标题" });
            cmbMatchMode.SelectedIndex = 0;
            grpGame.Controls.Add(cmbMatchMode);
            y += 125;

            var grpGlobal = new GroupBox { Text = "全局时长", Left = 10, Top = y, Width = 680, Height = 70 };
            page.Controls.Add(grpGlobal);
            grpGlobal.Controls.Add(new Label { Text = "提醒后宽限：", Left = 10, Top = 28, Width = 80 });
            numReminder = new NumericUpDown { Left = 95, Top = 25, Width = 60, Minimum = 1, Maximum = 120 };
            grpGlobal.Controls.Add(numReminder);
            grpGlobal.Controls.Add(new Label { Text = "分钟", Left = 160, Top = 28, Width = 40 });

            grpGlobal.Controls.Add(new Label { Text = "倒计时：", Left = 230, Top = 28, Width = 70 });
            numCountdown = new NumericUpDown { Left = 305, Top = 25, Width = 60, Minimum = 5, Maximum = 600 };
            grpGlobal.Controls.Add(numCountdown);
            grpGlobal.Controls.Add(new Label { Text = "秒", Left = 375, Top = 28, Width = 30 });

            btnAdd.Click += BtnAdd_Click;
            btnUpdate.Click += BtnUpdate_Click;
            btnDelete.Click += BtnDelete_Click;
            btnResetUsage.Click += BtnResetUsage_Click;
            listGames.SelectedIndexChanged += (s, e) => LoadSelectedGame();

            var refresh = new Timer { Interval = 3000 };
            refresh.Tick += (s, e) => RefreshGameList();
            refresh.Start();
            page.Disposed += (s, e) => refresh.Dispose();

            return page;
        }

        // ================= 页签 2：系统与功能 =================

        private TabPage BuildSystemTab()
        {
            var page = new TabPage("系统与功能");
            int y = 20;

            page.Controls.Add(new Label { Text = "全局快捷键：", Left = 20, Top = y, Width = 90 });
            cmbModifier = new ComboBox { Left = 115, Top = y - 3, Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbModifier.Items.AddRange(new object[] { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Ctrl", "Alt" });
            page.Controls.Add(cmbModifier);

            cmbKey = new ComboBox { Left = 235, Top = y - 3, Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            for (char c = 'A'; c <= 'Z'; c++) cmbKey.Items.Add(c.ToString());
            for (int i = 1; i <= 12; i++) cmbKey.Items.Add("F" + i);
            page.Controls.Add(cmbKey);

            btnApplyHotkey = new Button { Text = "应用快捷键", Left = 305, Top = y - 3, Width = 100 };
            page.Controls.Add(btnApplyHotkey);
            y += 35;

            chkStartup = new CheckBox { Text = "开机启动（写注册表 HKCU Run）", Left = 20, Top = y, Width = 300 };
            page.Controls.Add(chkStartup);
            y += 25;

            chkHideTrayIcon = new CheckBox { Text = "隐藏托盘图标（必须保证快捷键可用）", Left = 20, Top = y, Width = 350 };
            page.Controls.Add(chkHideTrayIcon);
            y += 25;

            chkMouseTransparent = new CheckBox
            {
                Text = "鼠标穿透（倒计时大字 + 桌面悬浮框都不接收鼠标操作）",
                Left = 20,
                Top = y,
                Width = 480
            };
            page.Controls.Add(chkMouseTransparent);
            y += 25;

            chkEnableDaemon = new CheckBox
            {
                Text = "防结束守护（任务管理器强制杀死后，1分钟内自动复活）",
                Left = 20,
                Top = y,
                Width = 480,
                ForeColor = Color.DarkRed
            };
            page.Controls.Add(chkEnableDaemon);
            y += 35;

            var grpRemote = new GroupBox { Text = "远程控制台 (Web)", Left = 20, Top = y, Width = 640, Height = 205 };
            page.Controls.Add(grpRemote);

            chkEnableRemote = new CheckBox { Text = "启用 Web 控制台（需要先设置管理密码）", Left = 20, Top = 22, Width = 320 };
            grpRemote.Controls.Add(chkEnableRemote);

            chkRemoteAllowLan = new CheckBox { Text = "允许局域网访问", Left = 20, Top = 52, Width = 160 };
            grpRemote.Controls.Add(chkRemoteAllowLan);

            grpRemote.Controls.Add(new Label { Text = "端口号：", Left = 300, Top = 25, Width = 60 });
            numRemotePort = new NumericUpDown { Left = 365, Top = 22, Width = 80, Minimum = 1024, Maximum = 65535 };
            grpRemote.Controls.Add(numRemotePort);

            lblRemoteAddress = new Label
            {
                Text = "",
                Left = 20,
                Top = 80,
                Width = 600,
                ForeColor = Color.SteelBlue
            };
            grpRemote.Controls.Add(lblRemoteAddress);

            lblRemoteHint = new Label
            {
                Left = 20,
                Top = 103,
                Width = 600,
                Height = 95,
                ForeColor = Color.DimGray
            };
            grpRemote.Controls.Add(lblRemoteHint);

            // 密码未设置时禁用 Web 开关，并给出明确提示
            chkEnableRemote.CheckedChanged += (s, e) => UpdateRemoteUiState();
            chkRemoteAllowLan.CheckedChanged += (s, e) => UpdateRemoteAddressLabel();
            numRemotePort.ValueChanged += (s, e) => UpdateRemoteAddressLabel();

            y += 215;

            var grpSecurity = new GroupBox { Text = "安全设置 (管理密码)", Left = 20, Top = y, Width = 640, Height = 110 };
            page.Controls.Add(grpSecurity);

            grpSecurity.Controls.Add(new Label { Text = "新密码：", Left = 20, Top = 25, Width = 60 });
            txtNewPass = new TextBox { Left = 85, Top = 22, Width = 140, UseSystemPasswordChar = true };
            grpSecurity.Controls.Add(txtNewPass);

            grpSecurity.Controls.Add(new Label { Text = "确认密码：", Left = 240, Top = 25, Width = 70 });
            txtConfirmPass = new TextBox { Left = 315, Top = 22, Width = 140, UseSystemPasswordChar = true };
            grpSecurity.Controls.Add(txtConfirmPass);

            chkClearPassword = new CheckBox { Text = "清除已有密码", Left = 470, Top = 24, Width = 120 };
            grpSecurity.Controls.Add(chkClearPassword);

            // 密码输入变化时同步刷新 Web 控制台开关的可用状态
            txtNewPass.TextChanged += (s, e) => UpdateRemoteUiState();
            txtConfirmPass.TextChanged += (s, e) => UpdateRemoteUiState();
            chkClearPassword.CheckedChanged += (s, e) => UpdateRemoteUiState();

            grpSecurity.Controls.Add(new Label
            {
                Text = "提示：留空新密码框表示保持原密码不变。若需取消密码保护，请勾选“清除已有密码”。",
                Left = 20,
                Top = 60,
                Width = 600,
                ForeColor = Color.DimGray
            });

            btnApplyHotkey.Click += BtnApplyHotkey_Click;
            return page;
        }

        // ================= 页签 3：桌面悬浮框 =================

        private TabPage BuildOverlayTab()
        {
            var page = new TabPage("桌面悬浮框");
            int y = 20;

            chkOverlayEnabled = new CheckBox { Text = "显示桌面时长悬浮框", Left = 20, Top = y, Width = 300 };
            page.Controls.Add(chkOverlayEnabled);
            y += 30;

            chkOverlayTopMost = new CheckBox { Text = "置顶（在游戏上方显示）", Left = 20, Top = y, Width = 300 };
            page.Controls.Add(chkOverlayTopMost);
            y += 30;

            page.Controls.Add(new Label { Text = "背景不透明度：", Left = 20, Top = y + 3, Width = 100 });
            numOverlayOpacity = new NumericUpDown { Left = 125, Top = y, Width = 80, Minimum = 0, Maximum = 255 };
            page.Controls.Add(numOverlayOpacity);
            page.Controls.Add(new Label { Text = "（0=透明，255=不透明）", Left = 215, Top = y + 3, Width = 200 });
            y += 36;

            chkOverlaySnap = new CheckBox { Text = "拖动后靠近屏幕边缘时自动吸附", Left = 20, Top = y, Width = 320 };
            page.Controls.Add(chkOverlaySnap);
            y += 30;

            chkOverlayAutoHide = new CheckBox { Text = "启用自动隐藏", Left = 20, Top = y, Width = 200 };
            page.Controls.Add(chkOverlayAutoHide);

            page.Controls.Add(new Label { Text = "鼠标离开后", Left = 230, Top = y + 3, Width = 70 });
            numOverlayAutoHideSeconds = new NumericUpDown { Left = 305, Top = y, Width = 60, Minimum = 1, Maximum = 120 };
            page.Controls.Add(numOverlayAutoHideSeconds);
            page.Controls.Add(new Label { Text = "秒变为淡显，鼠标移入恢复", Left = 375, Top = y + 3, Width = 200 });
            y += 40;

            page.Controls.Add(new Label
            {
                Text = "说明：拖动可以直接调整位置；无论是否吸附，都不会拖出屏幕。\r\n字体、颜色、大小请在本窗口的「字体设置」页调整。",
                Left = 20,
                Top = y,
                Width = 640,
                Height = 50,
                AutoSize = false,
                ForeColor = Color.DimGray
            });

            return page;
        }

        // ================= 页签 4：字体设置 =================

        private TabPage BuildFontTab()
        {
            var page = new TabPage("字体设置");
            page.AutoScroll = true;

            int y = 10;

            var grpCountdown = new GroupBox
            {
                Text = "倒计时覆盖层（倒计时大字 / 宽限期提醒 / 禁用提示）",
                Left = 10,
                Top = y,
                Width = 680,
                Height = 340
            };
            page.Controls.Add(grpCountdown);
            y += 348;

            int cy = 24;
            grpCountdown.Controls.Add(new Label { Text = "字体：", Left = 10, Top = cy + 3, Width = 60 });
            cmbCountdownFont = new ComboBox
            {
                Left = 75,
                Top = cy,
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                MaxDropDownItems = 20
            };
            using (var installed = new InstalledFontCollection())
                foreach (var f in installed.Families.OrderBy(x => x.Name))
                    cmbCountdownFont.Items.Add(f.Name);
            grpCountdown.Controls.Add(cmbCountdownFont);
            cy += 32;

            grpCountdown.Controls.Add(new Label { Text = "字体颜色：", Left = 10, Top = cy + 3, Width = 70 });
            btnCountdownColor = new Button
            {
                Left = 85,
                Top = cy,
                Width = 80,
                Height = 26,
                Text = "",
                BackColor = _pendingCountdownFg,
                FlatStyle = FlatStyle.Flat
            };
            btnCountdownColor.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = _pendingCountdownFg, FullOpen = true })
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _pendingCountdownFg = dlg.Color;
                        btnCountdownColor.BackColor = _pendingCountdownFg;
                    }
            };
            grpCountdown.Controls.Add(btnCountdownColor);

            grpCountdown.Controls.Add(new Label { Text = "字体不透明度：", Left = 180, Top = cy + 3, Width = 90 });
            numCountdownAlpha = new NumericUpDown { Left = 275, Top = cy, Width = 60, Minimum = 0, Maximum = 255 };
            grpCountdown.Controls.Add(numCountdownAlpha);
            cy += 32;

            grpCountdown.Controls.Add(new Label { Text = "数字高度占比：", Left = 10, Top = cy + 3, Width = 90 });
            numCountdownHeightPct = new NumericUpDown { Left = 105, Top = cy, Width = 60, Minimum = 30, Maximum = 100 };
            grpCountdown.Controls.Add(numCountdownHeightPct);
            grpCountdown.Controls.Add(new Label { Text = "%", Left = 170, Top = cy + 3, Width = 20 });

            grpCountdown.Controls.Add(new Label { Text = "数字宽度占比：", Left = 220, Top = cy + 3, Width = 90 });
            numCountdownWidthPct = new NumericUpDown { Left = 315, Top = cy, Width = 60, Minimum = 30, Maximum = 100 };
            grpCountdown.Controls.Add(numCountdownWidthPct);
            grpCountdown.Controls.Add(new Label { Text = "%", Left = 380, Top = cy + 3, Width = 20 });
            cy += 32;

            grpCountdown.Controls.Add(new Label { Text = "背景颜色：", Left = 10, Top = cy + 3, Width = 70 });
            btnCountdownBackColor = new Button
            {
                Left = 85,
                Top = cy,
                Width = 80,
                Height = 26,
                Text = "",
                BackColor = _pendingCountdownBg,
                FlatStyle = FlatStyle.Flat
            };
            btnCountdownBackColor.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = _pendingCountdownBg, FullOpen = true })
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _pendingCountdownBg = dlg.Color;
                        btnCountdownBackColor.BackColor = _pendingCountdownBg;
                    }
            };
            grpCountdown.Controls.Add(btnCountdownBackColor);

            grpCountdown.Controls.Add(new Label { Text = "背景不透明度：", Left = 180, Top = cy + 3, Width = 90 });
            numCountdownBackAlpha = new NumericUpDown { Left = 275, Top = cy, Width = 60, Minimum = 0, Maximum = 255 };
            grpCountdown.Controls.Add(numCountdownBackAlpha);
            grpCountdown.Controls.Add(new Label { Text = "（0=无背景）", Left = 340, Top = cy + 3, Width = 100 });
            cy += 32;

            chkCountdownShadow = new CheckBox { Text = "显示黑色阴影（提高在亮背景上的可读性）", Left = 10, Top = cy, Width = 400 };
            grpCountdown.Controls.Add(chkCountdownShadow);
            cy += 32;

            btnPreviewCountdown = new Button { Text = "预览倒计时（3 秒）", Left = 10, Top = cy, Width = 160, Height = 30 };
            btnPreviewCountdown.Click += BtnPreviewCountdown_Click;
            grpCountdown.Controls.Add(btnPreviewCountdown);
            cy += 32;

            grpCountdown.Controls.Add(new Label
            {
                Text = "说明：预览会按本页未保存的控件值生成临时倒计时窗口，3 秒后自动关闭，不影响真实倒计时。",
                Left = 10,
                Top = cy,
                Width = 640,
                ForeColor = Color.DimGray
            });

            var grpOverlay = new GroupBox
            {
                Text = "桌面悬浮框（今日游戏时长）",
                Left = 10,
                Top = y,
                Width = 680,
                Height = 220
            };
            page.Controls.Add(grpOverlay);
            y += 228;

            int oy = 24;
            grpOverlay.Controls.Add(new Label { Text = "字体：", Left = 10, Top = oy + 3, Width = 60 });
            cmbOverlayFont = new ComboBox
            {
                Left = 75,
                Top = oy,
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
                MaxDropDownItems = 20
            };
            using (var installed = new InstalledFontCollection())
                foreach (var f in installed.Families.OrderBy(x => x.Name))
                    cmbOverlayFont.Items.Add(f.Name);
            grpOverlay.Controls.Add(cmbOverlayFont);

            grpOverlay.Controls.Add(new Label { Text = "字号：", Left = 320, Top = oy + 3, Width = 50 });
            numOverlayFontSize = new NumericUpDown { Left = 375, Top = oy, Width = 60, Minimum = 6, Maximum = 48 };
            grpOverlay.Controls.Add(numOverlayFontSize);
            grpOverlay.Controls.Add(new Label { Text = "pt", Left = 440, Top = oy + 3, Width = 30 });
            oy += 32;

            grpOverlay.Controls.Add(new Label { Text = "文字颜色：", Left = 10, Top = oy + 3, Width = 70 });
            btnOverlayTextColor = new Button
            {
                Left = 85,
                Top = oy,
                Width = 80,
                Height = 26,
                Text = "",
                BackColor = _pendingOverlayFg,
                FlatStyle = FlatStyle.Flat
            };
            btnOverlayTextColor.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = _pendingOverlayFg, FullOpen = true })
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _pendingOverlayFg = dlg.Color;
                        btnOverlayTextColor.BackColor = _pendingOverlayFg;
                    }
            };
            grpOverlay.Controls.Add(btnOverlayTextColor);
            oy += 32;

            grpOverlay.Controls.Add(new Label { Text = "背景颜色：", Left = 10, Top = oy + 3, Width = 70 });
            btnOverlayBackColor = new Button
            {
                Left = 85,
                Top = oy,
                Width = 80,
                Height = 26,
                Text = "",
                BackColor = _pendingOverlayBg,
                FlatStyle = FlatStyle.Flat
            };
            btnOverlayBackColor.Click += (s, e) =>
            {
                using (var dlg = new ColorDialog { Color = _pendingOverlayBg, FullOpen = true })
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        _pendingOverlayBg = dlg.Color;
                        btnOverlayBackColor.BackColor = _pendingOverlayBg;
                    }
            };
            grpOverlay.Controls.Add(btnOverlayBackColor);
            grpOverlay.Controls.Add(new Label
            {
                Text = "（背景不透明度请到「桌面悬浮框」页调整）",
                Left = 180,
                Top = oy + 3,
                Width = 300,
                ForeColor = Color.DimGray
            });
            oy += 32;

            grpOverlay.Controls.Add(new Label
            {
                Text = "说明：修改后点击底部「应用」或「确定」即可在悬浮框上立即看到效果。",
                Left = 10,
                Top = oy,
                Width = 640,
                ForeColor = Color.DimGray
            });

            return page;
        }

        /// <summary>当前表单里“是否已有可用密码”（包含尚未保存的输入）。</summary>
        private bool HasPasswordForRemote()
        {
            if (chkClearPassword.Checked) return false;
            if (!string.IsNullOrEmpty(txtNewPass.Text))
                return txtNewPass.Text == txtConfirmPass.Text;
            if (!string.IsNullOrEmpty(txtConfirmPass.Text)) return false;
            return !string.IsNullOrEmpty(_config.AdminPassword);
        }

        /// <summary>密码为空时禁用 Web 控制台开关，并更新说明文字。</summary>
        private void UpdateRemoteUiState()
        {
            if (chkEnableRemote == null || txtNewPass == null || lblRemoteHint == null) return;

            bool hasPassword = HasPasswordForRemote();
            chkEnableRemote.Enabled = hasPassword;
            chkRemoteAllowLan.Enabled = hasPassword;
            numRemotePort.Enabled = hasPassword;

            if (!hasPassword)
                chkEnableRemote.Checked = false;

            lblRemoteHint.Text = hasPassword
                ? "说明：开启后可用手机/电脑浏览器查看与调整各游戏时长、重置用量、强制倒计时或结束游戏。\r\n" +
                  "网页登录与桌面端共用同一个管理密码，密码是唯一凭证，请勿泄露。\r\n" +
                  "默认仅监听 127.0.0.1（只有本机能访问）；勾选“允许局域网访问”后监听 0.0.0.0，\r\n" +
                  "同一局域网内任何设备都能访问，且通信为明文 HTTP，存在被窃听/冒用的风险。\r\n" +
                  "建议只在需要时临时开启，用完后立即关闭；不要将端口暴露到公网。"
                : "尚未设置管理密码：Web 控制台开关与端口设置已禁用。\r\n" +
                  "请先在下方“安全设置”中设置并保存管理密码。密码为空时远程服务不会启动，\r\n" +
                  "因此不存在匿名访问的可能。";

            UpdateRemoteAddressLabel();
        }

        private void UpdateRemoteAddressLabel()
        {
            if (lblRemoteAddress == null || numRemotePort == null) return;

            bool lan = chkRemoteAllowLan != null && chkRemoteAllowLan.Checked;
            if (lan && _localIp == null)
                _localIp = GetLocalIP();

            string host = lan ? _localIp : "127.0.0.1";
            string bindInfo = lan ? "监听 0.0.0.0，局域网设备可访问" : "仅监听 127.0.0.1，本机可访问";
            lblRemoteAddress.Text = $"访问地址：http://{host}:{(int)numRemotePort.Value}   （{bindInfo}）";
        }

        private static MatchMode MatchModeFromIndex(int index)
        {
            switch (index)
            {
                case 1: return MatchMode.ExactProcess;
                case 2: return MatchMode.ExactWindowTitle;
                default: return MatchMode.Contains;
            }
        }

        private static int MatchModeIndex(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.ExactProcess: return 1;
                case MatchMode.ExactWindowTitle: return 2;
                default: return 0;
            }
        }

        // ================= 加载与保存 =================

        private void LoadFromConfig()
        {
            _editGames = _config.Games.Select(g => new GameItem
            {
                DisplayName = g.DisplayName,
                ProcessName = g.ProcessName,
                RunMinutes = g.RunMinutes,
                DailyLimitMinutes = g.DailyLimitMinutes,
                Enabled = g.Enabled,
                MatchMode = g.MatchMode
            }).ToList();

            _editUsage = _config.Usage.Select(u => new GameUsage
            {
                ProcessName = u.ProcessName,
                Date = u.Date,
                UsedSeconds = u.UsedSeconds
            }).ToList();

            _pendingCountdownFg = Color.FromArgb(_config.CountdownFontColorR,
                _config.CountdownFontColorG, _config.CountdownFontColorB);
            _pendingCountdownBg = Color.FromArgb(_config.CountdownBackColorR,
                _config.CountdownBackColorG, _config.CountdownBackColorB);
            _pendingOverlayFg = Color.FromArgb(_config.StatusOverlayTextColorR,
                _config.StatusOverlayTextColorG, _config.StatusOverlayTextColorB);
            _pendingOverlayBg = Color.FromArgb(_config.StatusOverlayBackColorR,
                _config.StatusOverlayBackColorG, _config.StatusOverlayBackColorB);

            RefreshGameList();
            numReminder.Value = Math.Max(1, Math.Min(120, _config.ReminderMinutes));
            numCountdown.Value = Math.Max(5, Math.Min(600, _config.CountdownSeconds));
            cmbModifier.SelectedIndex = IndexFromModifiers(_config.HotkeyModifiers);
            cmbKey.SelectedIndex = KeyIndexFromVk(_config.HotkeyVk);
            chkStartup.Checked = _config.StartWithWindows;
            chkHideTrayIcon.Checked = _config.HideTrayIcon;
            chkMouseTransparent.Checked = _config.MouseTransparent;
            chkEnableDaemon.Checked = _config.EnableDaemon;

            chkEnableRemote.Checked = _config.EnableRemote;
            numRemotePort.Value = Math.Max(1024, Math.Min(65535, _config.RemotePort));
            chkRemoteAllowLan.Checked = _config.RemoteAllowLan;

            txtNewPass.Text = "";
            txtConfirmPass.Text = "";
            chkClearPassword.Checked = false;
            UpdateRemoteUiState();

            chkOverlayEnabled.Checked = _config.StatusOverlayEnabled;
            chkOverlayTopMost.Checked = _config.StatusOverlayTopMost;
            chkOverlaySnap.Checked = _config.StatusOverlaySnap;
            chkOverlayAutoHide.Checked = _config.StatusOverlayAutoHide;
            numOverlayOpacity.Value = Math.Max(0, Math.Min(255, _config.StatusOverlayOpacity));
            numOverlayAutoHideSeconds.Value = Math.Max(1, Math.Min(120, _config.StatusOverlayAutoHideSeconds));

            if (cmbCountdownFont.Items.Contains(_config.CountdownFontFamily))
                cmbCountdownFont.SelectedItem = _config.CountdownFontFamily;
            else if (cmbCountdownFont.Items.Count > 0)
                cmbCountdownFont.SelectedIndex = 0;
            btnCountdownColor.BackColor = _pendingCountdownFg;
            btnCountdownBackColor.BackColor = _pendingCountdownBg;
            numCountdownAlpha.Value = Math.Max(0, Math.Min(255, _config.CountdownFontAlpha));
            numCountdownBackAlpha.Value = Math.Max(0, Math.Min(255, _config.CountdownBackAlpha));
            numCountdownHeightPct.Value = Math.Max(30, Math.Min(100, _config.CountdownHeightPercent));
            numCountdownWidthPct.Value = Math.Max(30, Math.Min(100, _config.CountdownWidthPercent));
            chkCountdownShadow.Checked = _config.CountdownShadow;

            if (cmbOverlayFont.Items.Contains(_config.StatusOverlayFontFamily))
                cmbOverlayFont.SelectedItem = _config.StatusOverlayFontFamily;
            else if (cmbOverlayFont.Items.Count > 0)
                cmbOverlayFont.SelectedIndex = 0;
            numOverlayFontSize.Value = Math.Max(6, Math.Min(48, _config.StatusOverlayFontSize));
            btnOverlayTextColor.BackColor = _pendingOverlayFg;
            btnOverlayBackColor.BackColor = _pendingOverlayBg;
        }

        private void SaveAndNotify(bool closeAfter)
        {
            if (chkClearPassword.Checked)
            {
                _config.AdminPassword = "";
            }
            else if (!string.IsNullOrEmpty(txtNewPass.Text) || !string.IsNullOrEmpty(txtConfirmPass.Text))
            {
                if (txtNewPass.Text != txtConfirmPass.Text)
                {
                    MessageBox.Show("两次输入的新密码不一致，请重新输入！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _config.AdminPassword = txtNewPass.Text;
            }

            // 密码为空时不允许开启 Web 控制台（密码是唯一凭证）
            if (chkEnableRemote.Checked && string.IsNullOrEmpty(_config.AdminPassword))
            {
                chkEnableRemote.Checked = false;
                MessageBox.Show("请先设置管理密码，再开启 Web 控制台。", "游戏监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (chkClearPassword.Checked && _config.EnableRemote)
            {
                chkEnableRemote.Checked = false;
                MessageBox.Show("管理密码已清除，Web 控制台将自动关闭。", "游戏监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            uint newMod = ModifiersFromIndex(cmbModifier.SelectedIndex);
            uint newVk = (uint)VkFromKeyIndex(cmbKey.SelectedIndex);
            if (newMod != _config.HotkeyModifiers || newVk != _config.HotkeyVk)
            {
                bool ok = HotkeyApplyRequested?.Invoke(newMod, newVk) ?? false;
                if (!ok)
                {
                    MessageBox.Show("当前快捷键已被占用或注册失败，请重新选择。",
                        "快捷键失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _config.HotkeyModifiers = newMod;
                _config.HotkeyVk = (int)newVk;
            }

            _config.Games.Clear();
            _config.Games.AddRange(_editGames);
            _config.Usage.Clear();
            _config.Usage.AddRange(_editUsage);

            _config.ReminderMinutes = (int)numReminder.Value;
            _config.CountdownSeconds = (int)numCountdown.Value;
            _config.StartWithWindows = chkStartup.Checked;
            _config.HideTrayIcon = chkHideTrayIcon.Checked;
            _config.MouseTransparent = chkMouseTransparent.Checked;

            _config.EnableRemote = chkEnableRemote.Checked;
            _config.RemotePort = (int)numRemotePort.Value;
            _config.RemoteAllowLan = chkRemoteAllowLan.Checked;

            _config.EnableDaemon = chkEnableDaemon.Checked;
            StartupManager.SetDaemon(_config.EnableDaemon);

            _config.StatusOverlayEnabled = chkOverlayEnabled.Checked;
            _config.StatusOverlayTopMost = chkOverlayTopMost.Checked;
            _config.StatusOverlaySnap = chkOverlaySnap.Checked;
            _config.StatusOverlayAutoHide = chkOverlayAutoHide.Checked;
            _config.StatusOverlayOpacity = (int)numOverlayOpacity.Value;
            _config.StatusOverlayAutoHideSeconds = (int)numOverlayAutoHideSeconds.Value;

            if (cmbCountdownFont.SelectedItem is string cfname && !string.IsNullOrWhiteSpace(cfname))
                _config.CountdownFontFamily = cfname;
            _config.CountdownFontColorR = _pendingCountdownFg.R;
            _config.CountdownFontColorG = _pendingCountdownFg.G;
            _config.CountdownFontColorB = _pendingCountdownFg.B;
            _config.CountdownFontAlpha = (int)numCountdownAlpha.Value;
            _config.CountdownHeightPercent = (int)numCountdownHeightPct.Value;
            _config.CountdownWidthPercent = (int)numCountdownWidthPct.Value;
            _config.CountdownShadow = chkCountdownShadow.Checked;
            _config.CountdownBackColorR = _pendingCountdownBg.R;
            _config.CountdownBackColorG = _pendingCountdownBg.G;
            _config.CountdownBackColorB = _pendingCountdownBg.B;
            _config.CountdownBackAlpha = (int)numCountdownBackAlpha.Value;

            if (cmbOverlayFont.SelectedItem is string ofname && !string.IsNullOrWhiteSpace(ofname))
                _config.StatusOverlayFontFamily = ofname;
            _config.StatusOverlayFontSize = (int)numOverlayFontSize.Value;
            _config.StatusOverlayTextColorR = _pendingOverlayFg.R;
            _config.StatusOverlayTextColorG = _pendingOverlayFg.G;
            _config.StatusOverlayTextColorB = _pendingOverlayFg.B;
            _config.StatusOverlayBackColorR = _pendingOverlayBg.R;
            _config.StatusOverlayBackColorG = _pendingOverlayBg.G;
            _config.StatusOverlayBackColorB = _pendingOverlayBg.B;

            _config.Save();
            ConfigSaved?.Invoke();

            if (closeAfter) Close();
        }

        // ================= 事件（操作草稿） =================

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            string name = txtDisplayName.Text.Trim();
            string keyword = txtProcessName.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(keyword))
            {
                MessageBox.Show("请填写显示名称和进程关键字。"); return;
            }
            if (keyword.Length < GameMonitor.MinKeywordLength)
            {
                MessageBox.Show($"关键字至少需要 {GameMonitor.MinKeywordLength} 个字符，过短的子串容易误杀其他进程。");
                return;
            }
            if (!int.TryParse(txtRunMinutes.Text.Trim(), out int minutes) || minutes < 1 || minutes > 1440)
            {
                MessageBox.Show("单次运行时长必须是 1 ~ 1440 分钟。"); return;
            }
            if (!int.TryParse(txtDailyLimit.Text.Trim(), out int limit) || limit < 0 || limit > 1440)
            {
                MessageBox.Show("每日配额必须是 0 ~ 1440 分钟（0 表示不限）。"); return;
            }

            var existing = _editGames.FirstOrDefault(g =>
                g.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                MessageBox.Show("该关键字已存在！请在列表中选中它并点击“更新选中”。"); return;
            }

            _editGames.Add(new GameItem
            {
                DisplayName = name,
                ProcessName = keyword,
                RunMinutes = minutes,
                DailyLimitMinutes = limit,
                MatchMode = MatchModeFromIndex(cmbMatchMode.SelectedIndex)
            });

            RefreshGameList();
        }

        private void BtnUpdate_Click(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedGame();
            if (selectedItem == null)
            {
                MessageBox.Show("请先在列表中选择要更新的游戏。"); return;
            }

            string name = txtDisplayName.Text.Trim();
            string keyword = txtProcessName.Text.Trim().ToLowerInvariant();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(keyword))
            {
                MessageBox.Show("请填写显示名称和进程关键字。"); return;
            }
            if (keyword.Length < GameMonitor.MinKeywordLength)
            {
                MessageBox.Show($"关键字至少需要 {GameMonitor.MinKeywordLength} 个字符，过短的子串容易误杀其他进程。");
                return;
            }
            if (!int.TryParse(txtRunMinutes.Text.Trim(), out int minutes) || minutes < 1 || minutes > 1440)
            {
                MessageBox.Show("单次运行时长必须是 1 ~ 1440 分钟。"); return;
            }
            if (!int.TryParse(txtDailyLimit.Text.Trim(), out int limit) || limit < 0 || limit > 1440)
            {
                MessageBox.Show("每日配额必须是 0 ~ 1440 分钟（0 表示不限）。"); return;
            }

            var existing = _editGames.FirstOrDefault(g => g == selectedItem);

            if (!existing.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var conflict = _editGames.FirstOrDefault(g =>
                    g.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (conflict != null)
                {
                    MessageBox.Show("新关键字与已有的游戏冲突，请更换关键字。"); return;
                }
            }

            string oldKeyword = existing.ProcessName;
            existing.DisplayName = name;
            existing.ProcessName = keyword;
            existing.RunMinutes = minutes;
            existing.DailyLimitMinutes = limit;
            existing.MatchMode = MatchModeFromIndex(cmbMatchMode.SelectedIndex);

            if (!oldKeyword.Equals(keyword, StringComparison.OrdinalIgnoreCase))
            {
                var oldUsage = _editUsage.FirstOrDefault(x => x.ProcessName.Equals(oldKeyword, StringComparison.OrdinalIgnoreCase));
                if (oldUsage != null)
                {
                    oldUsage.ProcessName = keyword;
                }
            }

            RefreshGameList();
            MessageBox.Show("已更新草稿。请点击底部“应用”或“确定”保存。");
        }

        private void BtnDelete_Click(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedGame();
            if (selectedItem == null) return;

            if (MessageBox.Show($"确定删除「{selectedItem.DisplayName}」？", "确认",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            _editUsage.RemoveAll(x =>
                x.ProcessName.Equals(selectedItem.ProcessName, StringComparison.OrdinalIgnoreCase));
            _editGames.Remove(selectedItem);
            RefreshGameList();
        }

        private void BtnResetUsage_Click(object sender, EventArgs e)
        {
            var selectedItem = GetSelectedGame();
            if (selectedItem == null) return;

            var u = _editUsage.FirstOrDefault(x =>
                x.ProcessName.Equals(selectedItem.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (u != null) { u.UsedSeconds = 0; u.Date = DateTime.Today; }
            else _editUsage.Add(new GameUsage { ProcessName = selectedItem.ProcessName, Date = DateTime.Today, UsedSeconds = 0 });
            RefreshGameList();
        }

        private void BtnApplyHotkey_Click(object sender, EventArgs e)
        {
            uint mod = ModifiersFromIndex(cmbModifier.SelectedIndex);
            uint vk = (uint)VkFromKeyIndex(cmbKey.SelectedIndex);
            bool ok = HotkeyApplyRequested?.Invoke(mod, vk) ?? false;
            if (ok)
            {
                _config.HotkeyModifiers = mod;
                _config.HotkeyVk = (int)vk;
                _config.Save();
                MessageBox.Show("快捷键已应用。");
            }
            else
            {
                MessageBox.Show("快捷键注册失败，可能已被占用。");
            }
        }

        private void BtnPreviewCountdown_Click(object sender, EventArgs e)
        {
            var preview = new AppConfig { MouseTransparent = true };
            if (cmbCountdownFont.SelectedItem is string fname) preview.CountdownFontFamily = fname;
            preview.CountdownFontColorR = _pendingCountdownFg.R;
            preview.CountdownFontColorG = _pendingCountdownFg.G;
            preview.CountdownFontColorB = _pendingCountdownFg.B;
            preview.CountdownFontAlpha = (int)numCountdownAlpha.Value;
            preview.CountdownHeightPercent = (int)numCountdownHeightPct.Value;
            preview.CountdownWidthPercent = (int)numCountdownWidthPct.Value;
            preview.CountdownShadow = chkCountdownShadow.Checked;
            preview.CountdownBackColorR = _pendingCountdownBg.R;
            preview.CountdownBackColorG = _pendingCountdownBg.G;
            preview.CountdownBackColorB = _pendingCountdownBg.B;
            preview.CountdownBackAlpha = (int)numCountdownBackAlpha.Value;

            CountdownForm.ShowPreview(preview, 3);
        }

        private GameItem GetSelectedGame()
        {
            if (listGames.SelectedItems.Count == 0) return null;
            return listGames.SelectedItems[0].Tag as GameItem;
        }

        private void LoadSelectedGame()
        {
            if (_isRefreshingList) return;
            var g = GetSelectedGame();
            if (g == null) return;

            txtDisplayName.Text = g.DisplayName;
            txtProcessName.Text = g.ProcessName;
            txtRunMinutes.Text = g.RunMinutes.ToString();
            txtDailyLimit.Text = g.DailyLimitMinutes.ToString();
            cmbMatchMode.SelectedIndex = MatchModeIndex(g.MatchMode);
        }

        private void RefreshGameList()
        {
            if (listGames == null) return;
            _isRefreshingList = true;

            var selectedGame = GetSelectedGame();

            listGames.BeginUpdate();
            listGames.Items.Clear();

            var allProcs = Process.GetProcesses();
            try
            {
                foreach (var g in _editGames)
                {
                    var u = _editUsage.FirstOrDefault(x =>
                        x.ProcessName != null &&
                        x.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int used = (u != null && u.Date.Date == DateTime.Today) ? u.UsedSeconds / 60 : 0;
                    string quota = g.DailyLimitMinutes > 0 ? $"{g.DailyLimitMinutes}" : "不限";

                    bool isRunning = false;
                    try
                    {
                        isRunning = allProcs.Any(p => IsProcessMatched(p, g));
                    }
                    catch { }

                    string modeText = g.MatchMode == MatchMode.ExactProcess ? "精确进程名"
                        : g.MatchMode == MatchMode.ExactWindowTitle ? "精确窗口标题"
                        : "包含";

                    var item = new ListViewItem(isRunning ? "运行中" : "未运行");
                    if (isRunning) item.ForeColor = Color.Green; else item.ForeColor = Color.Gray;

                    item.SubItems.Add(g.DisplayName);
                    item.SubItems.Add(g.ProcessName);
                    item.SubItems.Add(modeText);
                    item.SubItems.Add(g.RunMinutes.ToString());
                    item.SubItems.Add(quota);
                    item.SubItems.Add($"{used} 分");

                    item.Tag = g;
                    listGames.Items.Add(item);

                    if (selectedGame != null && g == selectedGame)
                    {
                        item.Selected = true;
                    }
                }
            }
            finally
            {
                foreach (var p in allProcs)
                {
                    try { p.Dispose(); } catch { }
                }
                listGames.EndUpdate();
                _isRefreshingList = false;
            }
        }

        private static bool IsProcessMatched(Process p, GameItem game)
        {
            string keyword = game.ProcessName ?? "";
            if (keyword.Length == 0) return false;

            string procName = p.ProcessName ?? "";

            switch (game.MatchMode)
            {
                case MatchMode.ExactProcess:
                    return procName.Equals(keyword, StringComparison.OrdinalIgnoreCase);

                case MatchMode.ExactWindowTitle:
                    {
                        string title = p.MainWindowTitle ?? "";
                        return title.Length > 0 && title.Equals(keyword, StringComparison.OrdinalIgnoreCase);
                    }

                default:
                    if (procName.ToLowerInvariant().Contains(keyword.ToLowerInvariant())) return true;
                    {
                        string title = p.MainWindowTitle ?? "";
                        return title.Length > 0 && title.ToLowerInvariant().Contains(keyword.ToLowerInvariant());
                    }
            }
        }

        // ================= 辅助工具 =================

        private string GetLocalIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return "127.0.0.1";
        }

        private static uint ModifiersFromIndex(int idx)
        {
            switch (idx)
            {
                case 0: return 0x0003;
                case 1: return 0x0006;
                case 2: return 0x0005;
                case 3: return 0x0007;
                case 4: return 0x0002;
                case 5: return 0x0001;
                default: return 0x0003;
            }
        }

        private static int IndexFromModifiers(uint mod)
        {
            switch (mod)
            {
                case 0x0003: return 0;
                case 0x0006: return 1;
                case 0x0005: return 2;
                case 0x0007: return 3;
                case 0x0002: return 4;
                case 0x0001: return 5;
                default: return 0;
            }
        }

        private static int VkFromKeyIndex(int idx)
        {
            if (idx >= 0 && idx < 26) return 0x41 + idx;
            if (idx >= 26 && idx < 38) return 0x70 + (idx - 26);
            return 0x47;
        }

        private static int KeyIndexFromVk(int vk)
        {
            if (vk >= 0x41 && vk <= 0x5A) return vk - 0x41;
            if (vk >= 0x70 && vk <= 0x7B) return 26 + (vk - 0x70);
            return 6;
        }
    }
}