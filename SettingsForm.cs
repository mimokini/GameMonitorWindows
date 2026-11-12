using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Xml;
using System.Xml.Serialization;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    /// <summary>
    /// 设置窗口：分页签布局 + 监控列表行内编辑。
    /// 列表编辑是“草稿”模式：点「保存列表」或底部「应用/确定」时统一校验并落盘，
    /// 未保存时切换页签或关闭窗口会提示是否放弃修改。
    /// </summary>
    public class SettingsForm : Form
    {
        private static readonly string[] MatchModeLabels = { "包含", "精确进程名", "精确窗口名" };
        private const string GitHubReleasesUrl = "https://github.com/mimokini/GameMonitorWindows/releases";
        private static readonly string[] HotkeyKeyNames =
        {
            "A","B","C","D","E","F","G","H","I","J","K","L","M","N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
            "0","1","2","3","4","5","6","7","8","9",
            "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12"
        };

        private readonly AppConfig _config;
        private readonly GameMonitor _monitor;

        private List<GameUsage> _editUsage = new List<GameUsage>();
        private bool _gamesDirty;
        private bool _suppressDirty;
        private bool _loading;
        private Timer _statusTimer;

        // ===== 常规 =====
        private ComboBox cmbModifier;
        private ComboBox cmbKey;
        private CheckBox chkHideTrayIcon;
        private CheckBox chkMouseTransparent;
        private CheckBox chkEnableDaemon;

        // ===== 监控列表 =====
        private DataGridView gridGames;
        private Button btnAddGame;
        private Button btnResetUsage;
        private DataGridViewCheckBoxColumn colEnabled;
        private DataGridViewTextBoxColumn colName;
        private DataGridViewComboBoxColumn colMode;
        private DataGridViewTextBoxColumn colKeyword;
        private DataGridViewTextBoxColumn colRun;
        private DataGridViewTextBoxColumn colDaily;
        private DataGridViewTextBoxColumn colCooldown;
        private DataGridViewTextBoxColumn colWeekend;
        private DataGridViewTextBoxColumn colUsed;
        private DataGridViewTextBoxColumn colStatus;
        private DataGridViewButtonColumn colDelete;

        // ===== 提醒与提示样式 =====
        private NumericUpDown numReminder;
        private NumericUpDown numCountdown;
        private NumericUpDown numNoticeStay;
        private NumericUpDown numDailyTotalLimit;
        private Label lblTotalToday;
        private readonly TextBox[] txtPromptTemplate = new TextBox[5];
        private readonly ComboBox[] cmbPromptFont = new ComboBox[5];
        private readonly NumericUpDown[] numPromptSize = new NumericUpDown[5];
        private readonly CheckBox[] chkPromptBold = new CheckBox[5];
        private readonly CheckBox[] chkPromptShadow = new CheckBox[5];
        private readonly Button[] btnPromptColor = new Button[5];
        private readonly Button[] btnPromptBackColor = new Button[5];
        private readonly NumericUpDown[] numPromptAlpha = new NumericUpDown[5];
        private readonly NumericUpDown[] numPromptTextAlpha = new NumericUpDown[5];
        private readonly Button[] btnPromptPreview = new Button[5];
        private readonly Color[] _promptColors =
        {
            Color.FromArgb(255, 220, 220), Color.FromArgb(255, 30, 30),
            Color.FromArgb(255, 220, 220), Color.FromArgb(255, 220, 220),
            Color.FromArgb(255, 120, 120)
        };
        private readonly Color[] _promptBackColors =
        {
            Color.Black, Color.Black, Color.Black, Color.Black, Color.Black
        };

        // ===== 提示声音 =====
        private readonly CheckBox[] chkSound = new CheckBox[5];
        private readonly TextBox[] txtSoundFile = new TextBox[5];
        private Label lblPauseState;
        private NumericUpDown numPauseMinutes;
        private Button btnPause;
        private Button btnResumePause;
        private TextBox txtQuickMinutes;
        private Button btnQuickBonus;
        private Button btnQuickReduce;
        private ComboBox cmbHistoryDays;
        private UsageBarChart chartHistory;
        private DataGridView gridHistory;
        private Label lblVersion;
        private Label lblLogPath;
        private LinkLabel lblGitHub;

        // ===== 悬浮框 =====
        private CheckBox chkOverlayEnabled;
        private CheckBox chkOverlayTopMost;
        private CheckBox chkOverlaySnap;
        private CheckBox chkOverlayAutoHide;
        private NumericUpDown numOverlayAutoHideSeconds;
        private NumericUpDown numOverlayEdgeHideSeconds;
        private CheckBox chkOverlayEdgeHide;
        private TextBox txtOverlayTitle;
        private NumericUpDown numOverlayOpacityPercent;
        /// <summary>悬浮窗圆角四档选择（索引对应 AppConfig.CornerRadiusOptions）。</summary>
        private ComboBox cmbOverlayCorner;
        private ComboBox cmbOverlayFont;
        private NumericUpDown numOverlayFontSize;
        private Button btnOverlayTextColor;
        private Button btnOverlayBackColor;
        private Color _overlayTextColor = Color.FromArgb(210, 210, 210);
        private Color _overlayBackColor = Color.FromArgb(32, 32, 32);
        private readonly Button[] btnDotColors = new Button[5];
        private readonly Color[] _dotColors =
        {
            Color.FromArgb(154, 163, 175), Color.FromArgb(34, 197, 94),
            Color.FromArgb(245, 158, 11), Color.FromArgb(255, 193, 7),
            Color.FromArgb(239, 68, 68)
        };

        // ===== Web 控制台 =====
        private CheckBox chkEnableRemote;
        private CheckBox chkRemoteAllowLan;
        private NumericUpDown numRemotePort;
        private Label lblRemoteAddress;
        private Label lblRemoteHint;

        // ===== 密码与启动项 =====
        private TextBox txtNewPass;
        private TextBox txtConfirmPass;
        private CheckBox chkClearPassword;
        private CheckBox chkStartWithWindows;
        private Label lblPasswordHint;

        private TabControl tabs;
        private TabPage TabGeneral;
        private TabPage tabGames;
        private TabPage TabSchedule;
        private TabPage TabHistory;
        private TabPage TabHotkey;
        private TabPage TabStyle;
        private TabPage TabPassword;

        // ===== 时段管控 =====
        private DataGridView gridRules;
        private Button btnAddRule;
        private Button btnDeleteRule;
        private DataGridViewCheckBoxColumn colRuleEnabled;
        private DataGridViewComboBoxColumn colRuleKind;
        private DataGridViewTextBoxColumn colRuleStart;
        private DataGridViewTextBoxColumn colRuleEnd;
        private DataGridViewButtonColumn colRuleDelete;
        private readonly DataGridViewCheckBoxColumn[] colRuleDays = new DataGridViewCheckBoxColumn[7];

        public event Action ConfigSaved;
        public event Action OverlayResetRequested;
        public event Action<uint, uint> HotkeyApplyRequested;
        /// <summary>非阻断提示（如「关键字较短」）：由宿主用状态栏/托盘气泡展示，不弹确认框。</summary>
        public event Action<string> NoticeRequested;

        public SettingsForm(AppConfig config, GameMonitor monitor)
        {
            _config = config;
            _monitor = monitor;

            Text = "游戏监控 - 设置";
            Font = new Font("Microsoft YaHei", 9f);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(940, 820);
            MinimumSize = new Size(940, 700);
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            Icon = AppIcon.Resolve();
            FormBorderStyle = FormBorderStyle.Sizable;

            _editUsage = CloneUsage(config.Usage);

            BuildUi();
            LoadFromConfig();

            _statusTimer = new Timer { Interval = 3000 };
            _statusTimer.Tick += (s, e) => RefreshGameStatus();
            _statusTimer.Start();
        }

        /// <summary>把持久化用量复制成设置页可编辑的副本（只拷三个字段）。</summary>
        private static List<GameUsage> CloneUsage(IEnumerable<GameUsage> usage)
            => usage.Select(u => new GameUsage
            {
                ProcessName = u.ProcessName,
                Date = u.Date,
                UsedSeconds = u.UsedSeconds
            }).ToList();

        #region 布局构建

        /// <summary>行内标签：与输入控件同顶边、同高度，保证垂直中心一致。</summary>
        private static Label MakeRowLabel(string text, int left, int top, int width = 110)
            => new Label { Text = text, Left = left, Top = top, Width = width, Height = 23, TextAlign = ContentAlignment.MiddleLeft };

        private static Label MakeHint(string text, int left, int top, int maxWidth = 760)
            => new Label
            {
                Text = text,
                Left = left,
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(maxWidth, 0),
                ForeColor = Color.DimGray
            };

        private static GroupBox MakeGroup(string title, int left, int top, int width, int height)
            => new GroupBox { Text = title, Left = left, Top = top, Width = width, Height = height };

        private static void FitGroups(TabPage page)
        {
            int width = page.ClientSize.Width - 28;
            if (page.VerticalScroll.Visible) width -= SystemInformation.VerticalScrollBarWidth;
            foreach (Control c in page.Controls)
            {
                if (c is GroupBox) c.Width = Math.Max(640, width);
            }
        }

        private static TabPage MakeTab(string title, bool scroll = true)
        {
            var page = new TabPage(title) { Padding = new Padding(12), AutoScroll = scroll };
            page.Resize += (s, e) => FitGroups(page);
            return page;
        }

        private void BuildUi()
        {
            var bottom = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0) };
            tabs = new TabControl { Dock = DockStyle.Fill };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
            layout.Controls.Add(tabs, 0, 0);
            layout.Controls.Add(bottom, 0, 1);
            Controls.Add(layout);

            TabGeneral = BuildGeneralTab();
            tabs.TabPages.Add(TabGeneral);
            tabGames = BuildGamesTab();
            tabs.TabPages.Add(tabGames);
            TabSchedule = BuildScheduleTab();
            tabs.TabPages.Add(TabSchedule);
            TabStyle = BuildStyleTab();
            tabs.TabPages.Add(TabStyle);
            TabHistory = BuildHistoryTab();
            tabs.TabPages.Add(TabHistory);
            TabHotkey = BuildHotkeyTab();
            tabs.TabPages.Add(TabHotkey);
            TabPassword = BuildPasswordTab();
            tabs.TabPages.Add(TabPassword);
            // 关于页放在最后（密码与远程控制之后）
            tabs.TabPages.Add(BuildAboutTab());

            AttachPlaceholders();

            tabs.Selecting += (s, e) =>
            {
                if (e.TabPage != tabGames && _gamesDirty)
                {
                    if (!ConfirmDiscardGames()) e.Cancel = true;
                }
                if (!e.Cancel && e.TabPage == TabHistory)
                {
                    RefreshHistory();
                }
            };

            var btnCancel = new Button { Text = "取消", Width = 88, Height = 32, Top = 12 };
            var btnOk = new Button { Text = "确定", Width = 88, Height = 32, Top = 12 };
            var btnApply = new Button { Text = "应用", Width = 88, Height = 32, Top = 12 };
            btnCancel.Left = ClientSize.Width - 104;
            btnOk.Left = btnCancel.Left - 96;
            btnApply.Left = btnOk.Left - 96;
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnOk.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnApply.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            btnApply.Click += (s, e) => SaveAndNotify(false);
            btnOk.Click += (s, e) => SaveAndNotify(true);
            btnCancel.Click += (s, e) => Close();
            bottom.Controls.Add(btnApply);
            bottom.Controls.Add(btnOk);
            bottom.Controls.Add(btnCancel);

            FormClosing += (s, e) =>
            {
                if (_gamesDirty && !ConfirmDiscardGames()) e.Cancel = true;
            };
        }

        private TabPage BuildGeneralTab()
        {
            var page = MakeTab("常规");

            var grpProgram = MakeGroup("程序设置", 12, 10, 850, 214);
            chkHideTrayIcon = new CheckBox { Text = "隐藏托盘图标（用快捷键呼出）", Left = 16, Top = 32, AutoSize = true };
            chkMouseTransparent = new CheckBox { Text = "点击穿透浮层（开启时可按住 Ctrl+Alt 再拖动）", Left = 16, Top = 64, AutoSize = true };
            chkEnableDaemon = new CheckBox { Text = "启用防结束守护（被结束后自动重启）", Left = 16, Top = 96, AutoSize = true };
            chkStartWithWindows = new CheckBox { Text = "登录 Windows 后自动启动游戏监控", Left = 16, Top = 128, AutoSize = true };
            grpProgram.Controls.Add(chkHideTrayIcon);
            grpProgram.Controls.Add(chkMouseTransparent);
            grpProgram.Controls.Add(chkEnableDaemon);
            grpProgram.Controls.Add(chkStartWithWindows);
            grpProgram.Controls.Add(MakeHint("开机启动写入当前用户的启动项，不需要管理员权限。", 16, 158, 780));
            page.Controls.Add(grpProgram);

            var grpOverlay = MakeGroup("悬浮窗设置", 12, 236, 850, 236);
            chkOverlayEnabled = new CheckBox { Text = "显示桌面状态悬浮框", Left = 16, Top = 32, AutoSize = true };
            chkOverlayTopMost = new CheckBox { Text = "总在最前", Left = 16, Top = 60, AutoSize = true };
            chkOverlaySnap = new CheckBox { Text = "贴近屏幕边缘时自动吸边", Left = 16, Top = 88, AutoSize = true };
            chkOverlayAutoHide = new CheckBox { Text = "无操作自动淡显", Left = 16, Top = 116, AutoSize = true };
            numOverlayAutoHideSeconds = new NumericUpDown { Left = 190, Top = 116, Width = 70, Minimum = 1, Maximum = 120 };
            chkOverlayEdgeHide = new CheckBox { Text = "贴边自动隐藏", Left = 16, Top = 144, AutoSize = true };
            numOverlayEdgeHideSeconds = new NumericUpDown { Left = 190, Top = 144, Width = 70, Minimum = 1, Maximum = 120 };
            grpOverlay.Controls.Add(chkOverlayEnabled);
            grpOverlay.Controls.Add(chkOverlayTopMost);
            grpOverlay.Controls.Add(chkOverlaySnap);
            grpOverlay.Controls.Add(chkOverlayAutoHide);
            grpOverlay.Controls.Add(numOverlayAutoHideSeconds);
            grpOverlay.Controls.Add(MakeRowLabel("秒", 266, 116, 30));
            grpOverlay.Controls.Add(chkOverlayEdgeHide);
            grpOverlay.Controls.Add(numOverlayEdgeHideSeconds);
            grpOverlay.Controls.Add(MakeRowLabel("秒", 266, 144, 30));

            // 重置位置：放在“贴边自动隐藏”这一项的下方
            var btnResetPos = new Button { Text = "重置位置", Left = 16, Top = 176, Width = 110, Height = 28 };
            btnResetPos.Click += (s, e) =>
            {
                // 立即复位：置哨兵值后马上通知主窗口执行 ApplyConfigChanges（会移动悬浮窗并持久化）
                _config.StatusOverlayX = -9999;
                _config.StatusOverlayY = -9999;
                OverlayResetRequested?.Invoke();
            };
            grpOverlay.Controls.Add(btnResetPos);
            grpOverlay.Controls.Add(MakeHint("整体透明度、字体与圆点颜色在“提示与样式 → 悬浮窗样式”里设置。", 16, 206, 470));
            page.Controls.Add(grpOverlay);

            var grpTime = MakeGroup("计时", 12, 484, 850, 186);
            grpTime.Controls.Add(MakeRowLabel("提醒宽限：", 16, 33, 90));
            numReminder = new NumericUpDown { Left = 110, Top = 33, Width = 90, Minimum = 1, Maximum = 60 };
            grpTime.Controls.Add(numReminder);
            grpTime.Controls.Add(MakeRowLabel("分钟", 205, 33, 50));

            grpTime.Controls.Add(MakeRowLabel("倒计时：", 300, 33, 60));
            numCountdown = new NumericUpDown { Left = 360, Top = 33, Width = 90, Minimum = 5, Maximum = 600 };
            grpTime.Controls.Add(numCountdown);
            grpTime.Controls.Add(MakeRowLabel("秒", 455, 33, 40));

            // 两行标签的左侧基准一致：标签 16、宽 90（右端 106），输入控件统一从 110 开始
            grpTime.Controls.Add(MakeRowLabel("每日总限额：", 16, 62, 90));
            numDailyTotalLimit = new NumericUpDown { Left = 110, Top = 62, Width = 90, Minimum = 0, Maximum = 1440 };
            numDailyTotalLimit.ValueChanged += (s, e) => MarkGlobalDirty();
            grpTime.Controls.Add(numDailyTotalLimit);
            grpTime.Controls.Add(MakeRowLabel("分钟", 205, 62, 50));

            // “文字停留时长”放在“倒计时”正下方、且处于“每日总限额”的右方
            grpTime.Controls.Add(MakeRowLabel("文字停留时长：", 300, 62, 100));
            numNoticeStay = new NumericUpDown { Left = 405, Top = 62, Width = 70, Minimum = 1, Maximum = 60 };
            grpTime.Controls.Add(numNoticeStay);
            grpTime.Controls.Add(MakeRowLabel("秒", 480, 62, 40));

            // “今日总已用 / 总限额”单独占一行，不再与输入框挤在一起
            lblTotalToday = new Label
            {
                Left = 16,
                Top = 92,
                Width = 820,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft
            };
            grpTime.Controls.Add(lblTotalToday);

            grpTime.Controls.Add(MakeHint("到点流程：提醒（宽限）→ 倒计时 → 强制关闭 → 间隔期 / 今日已用完；宽限和倒计时期间都会计时。", 16, 124, 800));
            grpTime.Controls.Add(MakeHint("每日总限额为 0 表示不启用；条目“每日配额”填 0 表示沿用总限额，只有总限额也为 0 时才不限。", 16, 150, 800));
            page.Controls.Add(grpTime);

            return page;
        }

        /// <summary>提示声音分组（五个时机的开关 + 自定义 wav 路径 + 试听/浏览/恢复默认）。</summary>
        private GroupBox BuildSoundGroup(int left, int top)
        {
            var grp = MakeGroup("提示与声音", left, top, 850, 300);
            string[] labels = { "宽限开始时播放", "倒计时开始时播放", "休息期提示时播放", "配额用完提示", "禁止时段提示" };
            for (int i = 0; i < labels.Length; i++)
            {
                int y = 36 + i * 44;
                var chk = new CheckBox { Text = labels[i], Left = 16, Top = y + 2, Width = 150, AutoSize = false };
                chkSound[i] = chk;
                grp.Controls.Add(chk);

                var file = new TextBox { Left = 176, Top = y, Width = 344 };
                txtSoundFile[i] = file;
                grp.Controls.Add(file);

                int index = i;
                var browse = new Button { Text = "浏览…", Left = 528, Top = y - 2, Width = 72, Height = 28 };
                browse.Click += (s, e) =>
                {
                    using (var dlg = new OpenFileDialog
                    {
                        Filter = "Wave 音频 (*.wav)|*.wav",
                        Title = "选择提示音"
                    })
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            if (!string.Equals(Path.GetExtension(dlg.FileName), ".wav", StringComparison.OrdinalIgnoreCase))
                            {
                                MessageBox.Show("只支持 .wav 文件。", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                return;
                            }
                            txtSoundFile[index].Text = dlg.FileName;
                            MarkGlobalDirty();
                        }
                    }
                };
                grp.Controls.Add(browse);

                // 试听：直接播放输入框里当前选中的文件，不需要先保存
                var preview = new Button { Text = "试听", Left = 608, Top = y - 2, Width = 68, Height = 28 };
                preview.Click += (s, e) => PreviewSoundFile(txtSoundFile[index].Text);
                grp.Controls.Add(preview);

                var reset = new Button { Text = "恢复默认", Left = 684, Top = y - 2, Width = 96, Height = 28 };
                reset.Click += (s, e) => { txtSoundFile[index].Text = ""; MarkGlobalDirty(); };
                grp.Controls.Add(reset);
            }

            grp.Controls.Add(MakeHint("留空或“恢复默认”表示使用内置系统提示音；自定义音频仅支持 .wav，保存时会校验扩展名与文件是否存在。", 16, 248, 800));
            grp.Controls.Add(MakeHint("“试听”直接播放输入框里的文件，无需保存；运行期间文件丢失/播放失败会回退系统音并写 WARN 日志。", 16, 270, 800));
            return grp;
        }

        /// <summary>试听：输入框为空时播放系统音；填了非 .wav 或文件不存在时给出提示，不静默失败。</summary>
        private void PreviewSoundFile(string path)
        {
            path = (path ?? "").Trim();
            if (path.Length > 0 &&
                !string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("试听只支持 .wav 文件。", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (path.Length > 0 && !File.Exists(path))
            {
                MessageBox.Show("试听文件不存在，或路径已失效。", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            PromptSoundPlayer.Preview(path);
        }

        private TabPage BuildHotkeyTab()
        {
            var page = MakeTab("快捷键");

            var grpKey = MakeGroup("全局快捷键", 12, 10, 850, 100);
            // 单行：打开设置：组合键 [组合键下拉] + [按键下拉] 按键
            grpKey.Controls.Add(MakeRowLabel("打开设置：组合键", 16, 22, 130));
            cmbModifier = new ComboBox { Left = 150, Top = 22, Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbModifier.Items.AddRange(new object[] { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift" });
            grpKey.Controls.Add(cmbModifier);

            grpKey.Controls.Add(MakeRowLabel("+", 278, 22, 16));
            cmbKey = new ComboBox { Left = 298, Top = 22, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbKey.Items.AddRange(HotkeyKeyNames);
            grpKey.Controls.Add(cmbKey);
            grpKey.Controls.Add(MakeRowLabel("按键", 396, 22, 40));

            cmbModifier.SelectedIndexChanged += (s, e) => MarkGlobalDirty();
            cmbKey.SelectedIndexChanged += (s, e) => MarkGlobalDirty();
            grpKey.Controls.Add(MakeHint("点击底部“应用/确定”统一保存并立即生效，无需重启程序；组合键或按键未选择时会定位到本页。", 16, 60, 780));
            page.Controls.Add(grpKey);

            return page;
        }

        private TabPage BuildGamesTab()
        {
            var page = MakeTab("监控列表", scroll: false);

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };
            btnAddGame = new Button { Text = "＋ 添加监控程序", Left = 8, Top = 6, Width = 150, Height = 32 };
            btnResetUsage = new Button { Text = "重置选中今日用量", Left = 166, Top = 6, Width = 150, Height = 32 };
            // 暂停监控：分钟输入（1~1440，默认 30）+ 暂停/取消暂停，与 Web 端一致
            top.Controls.Add(MakeRowLabel("暂停监控(分)", 326, 9, 84));
            numPauseMinutes = new NumericUpDown
            {
                Left = 412, Top = 8, Width = 58,
                Minimum = 1, Maximum = 1440, Value = 30
            };
            btnPause = new Button { Text = "暂停监控", Left = 478, Top = 6, Width = 88, Height = 32 };
            btnResumePause = new Button { Text = "取消暂停", Left = 572, Top = 6, Width = 88, Height = 32 };
            lblPauseState = new Label { Left = 668, Top = 8, Width = 232, Height = 28, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
            top.Controls.Add(btnAddGame);
            top.Controls.Add(btnResetUsage);
            top.Controls.Add(numPauseMinutes);
            top.Controls.Add(btnPause);
            top.Controls.Add(btnResumePause);
            top.Controls.Add(lblPauseState);

            // 快捷操作：表格下方一行，输入框 + 加时/减时（只作用于列表中选中的条目）
            var quick = new Panel { Dock = DockStyle.Fill };
            quick.Controls.Add(MakeRowLabel("快捷操作：", 8, 10, 74));
            txtQuickMinutes = new TextBox { Left = 84, Top = 10, Width = 70 };
            btnQuickBonus = new Button { Text = "临时加时", Left = 164, Top = 7, Width = 100, Height = 28 };
            btnQuickReduce = new Button { Text = "临时减时", Left = 272, Top = 7, Width = 100, Height = 28 };
            quick.Controls.Add(txtQuickMinutes);
            quick.Controls.Add(btnQuickBonus);
            quick.Controls.Add(btnQuickReduce);
            quick.Controls.Add(MakeRowLabel("只作用于列表中选中的条目，留空按 10 分钟", 372, 10, 400));

            gridGames = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 36,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Both
            };
            gridGames.RowTemplate.Height = 32;

            // 列宽策略：按内容设最小宽度 + 按内容量设权重，正常窗口下按权重铺满，
            // 窗口过窄时退到最小宽度并出现横向滚动条，保证“间隔/周末倍数”等列不被挤没。
            colName = new DataGridViewTextBoxColumn
            {
                HeaderText = "名称", Name = "DisplayName", MinimumWidth = 96, FillWeight = 152,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colStatus = new DataGridViewTextBoxColumn
            {
                HeaderText = "状态", Name = "Status", MinimumWidth = 120, FillWeight = 140, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colMode = new DataGridViewComboBoxColumn
            {
                HeaderText = "匹配方式", Name = "MatchMode", MinimumWidth = 96, FillWeight = 112,
                FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colMode.Items.AddRange(MatchModeLabels);
            colKeyword = new DataGridViewTextBoxColumn
            {
                HeaderText = "关键字", Name = "Keyword", MinimumWidth = 104, FillWeight = 150,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRun = new DataGridViewTextBoxColumn
            {
                HeaderText = "单次(分)", Name = "RunMinutes", MinimumWidth = 68, FillWeight = 72,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colDaily = new DataGridViewTextBoxColumn
            {
                HeaderText = "每日(分)", Name = "DailyLimit", MinimumWidth = 68, FillWeight = 72,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colCooldown = new DataGridViewTextBoxColumn
            {
                HeaderText = "间隔(分)", Name = "Cooldown", MinimumWidth = 68, FillWeight = 72,
                ValueType = typeof(int),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colWeekend = new DataGridViewTextBoxColumn
            {
                HeaderText = "周末倍数", Name = "WeekendMultiplier", MinimumWidth = 72, FillWeight = 80,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colUsed = new DataGridViewTextBoxColumn
            {
                HeaderText = "今日已用", Name = "Used", MinimumWidth = 88, FillWeight = 104, ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colEnabled = new DataGridViewCheckBoxColumn
            {
                HeaderText = "启用", Name = "Enabled", MinimumWidth = 50, FillWeight = 54,
                ValueType = typeof(bool),
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colDelete = new DataGridViewButtonColumn
            {
                HeaderText = "操作", Name = "Delete", MinimumWidth = 56, FillWeight = 60, Text = "删除",
                UseColumnTextForButtonValue = true, SortMode = DataGridViewColumnSortMode.NotSortable
            };

            gridGames.Columns.AddRange(colName, colStatus, colMode, colKeyword, colRun, colDaily, colCooldown,
                colWeekend, colUsed, colEnabled, colDelete);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118f));
            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(gridGames, 0, 1);
            layout.Controls.Add(quick, 0, 2);
            layout.Controls.Add(MakeGamesHints(), 0, 3);
            page.Controls.Add(layout);

            btnAddGame.Click += (s, e) => AddGameRow();
            btnResetUsage.Click += (s, e) => ResetSelectedUsage();
            btnQuickBonus.Click += (s, e) => ApplyQuickBonus(true);
            btnQuickReduce.Click += (s, e) => ApplyQuickBonus(false);
            btnPause.Click += (s, e) =>
            {
                if (!RequireAdminPassword()) return;
                int minutes = (int)numPauseMinutes.Value;
                _monitor.Pause(minutes);
                RefreshQuotaUi();
            };
            btnResumePause.Click += (s, e) =>
            {
                if (!RequireAdminPassword()) return;
                _monitor.ResumePause();
                RefreshQuotaUi();
            };
            gridGames.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == colDelete.Index)
                {
                    DeleteGameRow(e.RowIndex);
                    return;
                }
                // 单击编辑已关闭，复选框列必须单独处理，否则复选框点不动
                if (e.ColumnIndex == colEnabled.Index)
                {
                    var cell = gridGames.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    cell.Value = !(cell.Value is bool b && b);
                    _gamesDirty = true;
                    gridGames.InvalidateCell(cell);
                }
            };
            // 双击才进入编辑；单击只选中整行（F2/直接键入同样进入编辑）
            gridGames.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var column = gridGames.Columns[e.ColumnIndex];
                if (column.ReadOnly || column is DataGridViewButtonColumn || column is DataGridViewCheckBoxColumn)
                    return;
                gridGames.CurrentCell = gridGames.Rows[e.RowIndex].Cells[e.ColumnIndex];
                gridGames.BeginEdit(true);
            };
            gridGames.CellValueChanged += (s, e) => { if (!_suppressDirty && !_loading) _gamesDirty = true; };
            gridGames.CellValidating += GridGames_CellValidating;
            gridGames.CellParsing += GridGames_CellParsing;
            gridGames.DataError += GridGames_DataError;

            UpdateGamesEmptyHint();
            return page;
        }

        private Label _bottomHint;

        private TabPage BuildScheduleTab()
        {
            var page = MakeTab("时段管控", scroll: false);

            var top = new Panel { Dock = DockStyle.Top, Height = 44 };
            btnAddRule = new Button { Text = "＋ 新增时段规则", Left = 8, Top = 6, Width = 150, Height = 32 };
            btnDeleteRule = new Button { Text = "删除选中", Left = 166, Top = 6, Width = 120, Height = 32 };
            top.Controls.Add(btnAddRule);
            top.Controls.Add(btnDeleteRule);

            gridRules = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AutoGenerateColumns = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 36,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Both
            };
            gridRules.RowTemplate.Height = 30;

            colRuleEnabled = new DataGridViewCheckBoxColumn
            {
                HeaderText = "启用", Name = "RuleEnabled", MinimumWidth = 54, FillWeight = 56,
                ValueType = typeof(bool), SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRuleKind = new DataGridViewComboBoxColumn
            {
                HeaderText = "类型", Name = "RuleKind", MinimumWidth = 92, FillWeight = 96,
                FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRuleKind.Items.AddRange("禁止时段", "开放时段");
            colRuleStart = new DataGridViewTextBoxColumn
            {
                HeaderText = "开始", Name = "RuleStart", MinimumWidth = 78, FillWeight = 84,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            colRuleEnd = new DataGridViewTextBoxColumn
            {
                HeaderText = "结束", Name = "RuleEnd", MinimumWidth = 78, FillWeight = 84,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };

            // 星期列按“周一到周日”展示，存储索引仍与 DayOfWeek 对齐（0=周日）
            string[] dayNames = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
            int[] dayIndexes = { 1, 2, 3, 4, 5, 6, 0 };
            for (int i = 0; i < dayNames.Length; i++)
            {
                var col = new DataGridViewCheckBoxColumn
                {
                    HeaderText = dayNames[i], Name = "Day" + dayIndexes[i],
                    MinimumWidth = 40, FillWeight = 42, ValueType = typeof(bool),
                    SortMode = DataGridViewColumnSortMode.NotSortable
                };
                colRuleDays[dayIndexes[i]] = col;
            }

            colRuleDelete = new DataGridViewButtonColumn
            {
                HeaderText = "操作", Name = "RuleDelete", MinimumWidth = 56, FillWeight = 60, Text = "删除",
                UseColumnTextForButtonValue = true, SortMode = DataGridViewColumnSortMode.NotSortable
            };

            var columns = new List<DataGridViewColumn> { colRuleEnabled, colRuleKind, colRuleStart, colRuleEnd };
            columns.AddRange(colRuleDays.Where(c => c != null));
            columns.Add(colRuleDelete);
            gridRules.Columns.AddRange(columns.ToArray());

            // 四条说明按同一套行距排：第 4 条紧跟第 3 条实际高度，避免 3、4 之间出现过大空隙
            var hint1 = MakeHint("1. 优先级：禁止时段最高，其次开放时段；同类多条规则取并集。没有配置任何开放规则时不限制开放时间。", 8, 2, 880);
            var hint2 = MakeHint("2. 跨零点：开始时间晚于结束时间视为跨天（如 22:00-08:00），后半夜按“开始那一天”的星期生效；开始=结束视为无效。", 8, 26, 880);
            var hint3 = MakeHint("3. 禁止时段迁移：倒计时中进入→继续走完再关闭；运行/提醒中进入→立即开始倒计时，结束后关闭；空闲时启动→立即提示并关闭（30 秒内只提示一次）。", 8, 50, 880);
            var hint4 = MakeHint("4. 开放时段内：照常计时与记录用量，但不弹窗、不强杀；Web 的“结束本次/强制关闭”仍可用。", 8, Math.Max(74, hint3.Bottom + 6), 880);
            var hints = new Panel { Dock = DockStyle.Fill };
            hints.Controls.AddRange(new Control[] { hint1, hint2, hint3, hint4 });
            int hintsHeight = Math.Max(104, hint4.Bottom + 12);

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, hintsHeight));
            layout.Controls.Add(top, 0, 0);
            layout.Controls.Add(gridRules, 0, 1);
            layout.Controls.Add(hints, 0, 2);
            page.Controls.Add(layout);

            btnAddRule.Click += (s, e) => AddRuleRow(new ScheduleRule
            {
                Enabled = true,
                Kind = ScheduleRuleKind.Block,
                StartTime = "22:00:00",
                EndTime = "08:00:00",
                DaysOfWeek = new[] { true, true, true, true, true, true, true }
            });
            btnDeleteRule.Click += (s, e) =>
            {
                if (gridRules.CurrentRow == null) return;
                gridRules.Rows.RemoveAt(gridRules.CurrentRow.Index);
            };

            gridRules.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == colRuleDelete.Index)
                {
                    gridRules.Rows.RemoveAt(e.RowIndex);
                    return;
                }
                var column = gridRules.Columns[e.ColumnIndex];
                if (column == colRuleEnabled || column.Name.StartsWith("Day", StringComparison.Ordinal))
                {
                    // EditMode=EditOnKeystrokeOrF2 时单击不进入编辑，复选框需在此单独切换
                    var cell = gridRules.Rows[e.RowIndex].Cells[e.ColumnIndex];
                    cell.Value = !(cell.Value is bool b && b);
                    gridRules.InvalidateCell(cell);
                }
            };
            gridRules.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                var column = gridRules.Columns[e.ColumnIndex];
                if (column.ReadOnly || column is DataGridViewButtonColumn ||
                    column is DataGridViewCheckBoxColumn || column is DataGridViewComboBoxColumn)
                    return;
                gridRules.CurrentCell = gridRules.Rows[e.RowIndex].Cells[e.ColumnIndex];
                gridRules.BeginEdit(true);
            };
            gridRules.CellValidating += GridRules_CellValidating;
            gridRules.CellEndEdit += GridRules_CellEndEdit;

            return page;
        }

        private void GridRules_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var column = gridRules.Columns[e.ColumnIndex];
            if (column != colRuleStart && column != colRuleEnd) return;

            string text = Convert.ToString(e.FormattedValue)?.Trim() ?? "";
            if (ScheduleRules.IsValidTime(text)) return;

            MessageBox.Show("时间格式应为 HH:mm 或 HH:mm:ss（例如 22:00 或 22:00:00）。",
                "时段规则", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        private void GridRules_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var column = gridRules.Columns[e.ColumnIndex];
            if (column != colRuleStart && column != colRuleEnd) return;

            string text = Convert.ToString(gridRules.Rows[e.RowIndex].Cells[e.ColumnIndex].Value)?.Trim() ?? "";
            if (ScheduleRules.IsValidTime(text))
                gridRules.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = ScheduleRules.NormalizeTime(text);
        }

        private void AddRuleRow(ScheduleRule rule)
        {
            if (gridRules == null || rule == null) return;
            bool previous = _suppressDirty;
            _suppressDirty = true;
            try
            {
                int index = gridRules.Rows.Add();
                var row = gridRules.Rows[index];
                row.Cells[colRuleEnabled.Index].Value = rule.Enabled;
                row.Cells[colRuleKind.Index].Value =
                    rule.Kind == ScheduleRuleKind.Allow ? "开放时段" : "禁止时段";
                row.Cells[colRuleStart.Index].Value = ScheduleRules.NormalizeTime(rule.StartTime);
                row.Cells[colRuleEnd.Index].Value = ScheduleRules.NormalizeTime(rule.EndTime);
                for (int d = 0; d < 7; d++)
                    row.Cells[colRuleDays[d].Index].Value = rule.DaysOfWeek != null && rule.DaysOfWeek[d];

                gridRules.ClearSelection();
                row.Selected = true;
                gridRules.CurrentCell = row.Cells[colRuleStart.Index];
            }
            finally
            {
                _suppressDirty = previous;
            }
        }

        private void LoadScheduleRules()
        {
            if (gridRules == null) return;
            bool previous = _suppressDirty;
            _suppressDirty = true;
            try
            {
                gridRules.Rows.Clear();
                if (_config.ScheduleRules != null)
                {
                    foreach (var rule in _config.ScheduleRules)
                        if (rule != null) AddRuleRow(rule);
                }
                gridRules.ClearSelection();
            }
            finally
            {
                _suppressDirty = previous;
            }
        }

        /// <summary>把网格里的规则写回配置；非法/空时间在保存时兜底为 00:00:00。</summary>
        private void SaveScheduleRules()
        {
            if (gridRules == null) return;
            gridRules.EndEdit();

            var rules = new List<ScheduleRule>();
            foreach (DataGridViewRow row in gridRules.Rows)
            {
                if (row.IsNewRow) continue;
                var rule = new ScheduleRule
                {
                    Enabled = row.Cells[colRuleEnabled.Index].Value is bool enabled && enabled,
                    Kind = Convert.ToString(row.Cells[colRuleKind.Index].Value) == "开放时段"
                        ? ScheduleRuleKind.Allow : ScheduleRuleKind.Block
                };

                string start = Convert.ToString(row.Cells[colRuleStart.Index].Value)?.Trim() ?? "";
                string end = Convert.ToString(row.Cells[colRuleEnd.Index].Value)?.Trim() ?? "";
                rule.StartTime = ScheduleRules.IsValidTime(start) ? ScheduleRules.NormalizeTime(start) : "00:00:00";
                rule.EndTime = ScheduleRules.IsValidTime(end) ? ScheduleRules.NormalizeTime(end) : "00:00:00";

                rule.DaysOfWeek = new bool[7];
                for (int d = 0; d < 7; d++)
                    rule.DaysOfWeek[d] = row.Cells[colRuleDays[d].Index].Value is bool day && day;

                rules.Add(rule);
            }
            _config.ScheduleRules = rules;
        }

        /// <summary>监控列表页的说明文字：带序号排列的步骤式清单（表格 → 快捷操作 → 说明）。</summary>
        private Panel MakeGamesHints()
        {
            var panel = new Panel { Dock = DockStyle.Fill };
            _bottomHint = new Label
            {
                Text = "4. 提示：删除条目会同时清理该条目的今日用量记录；改关键字后用量会按新关键字重新统计。",
                Left = 8,
                Top = 86,
                Width = 880,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray
            };
            panel.Controls.Add(_bottomHint);
            panel.Controls.Add(MakeHint("1. 列表可直接编辑；修改后点底部“应用/确定”统一保存。未保存时切换页签或关闭会提示。", 8, 2, 880));
            panel.Controls.Add(MakeHint("2. 快捷操作作用于当日额外配额；输入 1~120 分钟（留空按 10），减时不会让当日配额低于已用时长。", 8, 26, 880));
            panel.Controls.Add(MakeHint("3. 匹配方式：包含=进程名或任一窗口名包含关键字；精确进程名=进程名完全相同；精确窗口名=任一窗口名与关键字完全相同。关键字可设置多个，以；隔开，按顺序匹配；关键字至少 2 个字，两字容易误匹配，建议写得更具体。", 8, 50, 880));
            return panel;
        }

        private TabPage BuildStyleTab()
        {
            var page = MakeTab("提示与样式");

            var grpPrompts = MakeGroup("提示文案与字体", 12, 10, 850, 344);
            // 表头与下方控件同列同宽；色卡列宽 = “字体色/背景色”文字标签宽度
            grpPrompts.Controls.Add(MakeRowLabel("类型", 12, 40, 76));
            grpPrompts.Controls.Add(MakeRowLabel("文案模板", 92, 40, 210));
            grpPrompts.Controls.Add(MakeRowLabel("字体", 306, 40, 118));
            grpPrompts.Controls.Add(MakeRowLabel("字号", 428, 40, 44));
            var hdrBold = MakeRowLabel("加粗", 476, 40, 38);
            hdrBold.TextAlign = ContentAlignment.MiddleCenter;
            grpPrompts.Controls.Add(hdrBold);
            var hdrShadow = MakeRowLabel("阴影", 518, 40, 38);
            hdrShadow.TextAlign = ContentAlignment.MiddleCenter;
            grpPrompts.Controls.Add(hdrShadow);
            grpPrompts.Controls.Add(MakeRowLabel("字体色", 560, 40, 50));
            grpPrompts.Controls.Add(MakeRowLabel("背景色", 614, 40, 50));
            grpPrompts.Controls.Add(MakeRowLabel("背景%", 668, 40, 48));
            grpPrompts.Controls.Add(MakeRowLabel("透明度%", 720, 40, 54));

            string[] installedFonts = InstalledFontNames();
            string[] promptNames = { "宽限提醒", "倒计时", "休息期", "配额用完", "禁止时段" };
            for (int i = 0; i < promptNames.Length; i++)
            {
                int y = 66 + i * 46;
                grpPrompts.Controls.Add(MakeRowLabel(promptNames[i], 12, y, 76));

                var tpl = new TextBox { Left = 92, Top = y, Width = 210 };
                var font = new ComboBox
                {
                    Left = 306, Top = y - 1, Width = 118,
                    DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 18
                };
                font.Items.AddRange(installedFonts);
                var size = new NumericUpDown { Left = 428, Top = y, Width = 44, Minimum = 0, Maximum = 200 };
                // 勾选框按“列中心”摆放（字形约 13px，控件取 16px 并居中于列宽）
                var bold = new CheckBox { Left = 476 + (38 - 16) / 2, Top = y + 4, Width = 16, Height = 16, Text = "" };
                var shadow = new CheckBox { Left = 518 + (38 - 16) / 2, Top = y + 4, Width = 16, Height = 16, Text = "", Checked = true };
                var color = new Button { Left = 560, Top = y, Width = 50, Height = 23, FlatStyle = FlatStyle.Flat };
                var backColor = new Button { Left = 614, Top = y, Width = 50, Height = 23, FlatStyle = FlatStyle.Flat };
                var alpha = new NumericUpDown { Left = 668, Top = y, Width = 48, Minimum = 0, Maximum = 100 };
                var textAlpha = new NumericUpDown { Left = 720, Top = y, Width = 54, Minimum = 0, Maximum = 100 };
                var preview = new Button { Text = "预览", Left = 778, Top = y - 1, Width = 60, Height = 24 };

                grpPrompts.Controls.Add(tpl);
                grpPrompts.Controls.Add(font);
                grpPrompts.Controls.Add(size);
                grpPrompts.Controls.Add(bold);
                grpPrompts.Controls.Add(shadow);
                grpPrompts.Controls.Add(color);
                grpPrompts.Controls.Add(backColor);
                grpPrompts.Controls.Add(alpha);
                grpPrompts.Controls.Add(textAlpha);
                grpPrompts.Controls.Add(preview);

                txtPromptTemplate[i] = tpl;
                cmbPromptFont[i] = font;
                numPromptSize[i] = size;
                chkPromptBold[i] = bold;
                chkPromptShadow[i] = shadow;
                btnPromptColor[i] = color;
                btnPromptBackColor[i] = backColor;
                numPromptAlpha[i] = alpha;
                numPromptTextAlpha[i] = textAlpha;
                btnPromptPreview[i] = preview;

                int index = i;
                tpl.TextChanged += (s, e) => MarkGlobalDirty();
                size.ValueChanged += (s, e) => MarkGlobalDirty();
                bold.CheckedChanged += (s, e) => MarkGlobalDirty();
                shadow.CheckedChanged += (s, e) => MarkGlobalDirty();
                alpha.ValueChanged += (s, e) => MarkGlobalDirty();
                textAlpha.ValueChanged += (s, e) => MarkGlobalDirty();
                color.Click += (s, e) => PickColor(color, c =>
                {
                    _promptColors[index] = c;
                    color.BackColor = c;
                    MarkGlobalDirty();
                });
                backColor.Click += (s, e) => PickColor(backColor, c =>
                {
                    _promptBackColors[index] = c;
                    backColor.BackColor = c;
                    MarkGlobalDirty();
                });
                preview.Click += (s, e) =>
                {
                    ApplyConfigFromControls();
                    CountdownForm.ShowPreview(_config, PromptKindOf(index));
                };
            }
            // 拆成两行短句，均能单行完整显示，避免出现被裁掉的半截文字
            grpPrompts.Controls.Add(MakeHint("占位符会原样替换：{game} {minutes} {seconds} {clock} {remaining} {time}（{time} 仅休息期提示）。", 12, 296, 820));
            grpPrompts.Controls.Add(MakeHint("字号 0 = 倒计时自动缩放；背景色/背景% = 提示文字整块底色（0% 为透明）。", 12, 316, 820));
            page.Controls.Add(grpPrompts);

            page.Controls.Add(BuildOverlayStyleGroup(12, 364));

            page.Controls.Add(BuildSoundGroup(12, 566));

            return page;
        }

        private static PromptKind PromptKindOf(int index)
        {
            switch (index)
            {
                case 1: return PromptKind.Countdown;
                case 2: return PromptKind.Cooldown;
                case 3: return PromptKind.QuotaExhausted;
                case 4: return PromptKind.BlockedBySchedule;
                default: return PromptKind.Reminding;
            }
        }

        /// <summary>系统已安装字体（保守回退到微软雅黑等常用字体）。</summary>
        private static string[] InstalledFontNames()
        {
            try
            {
                var names = FontFamily.Families
                    .Select(f => f.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(n => n, StringComparer.CurrentCulture)
                    .ToList();
                if (!names.Any(n => n.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase)))
                    names.Insert(0, "Microsoft YaHei");
                return names.ToArray();
            }
            catch
            {
                return new[] { "Microsoft YaHei", "SimSun", "Arial" };
            }
        }

        /// <summary>悬浮窗样式：说明文字模板 → 字体/字号/字体色/背景色/整体透明度 → 五种状态圆点颜色。</summary>
        private GroupBox BuildOverlayStyleGroup(int left, int top)
        {
            var grp = MakeGroup("悬浮窗样式", left, top, 850, 186);

            // 1) 说明文字模板（原独立分组“悬浮窗说明文字”，现并入本分组首位）
            grp.Controls.Add(MakeRowLabel("说明文字：", 16, 34, 76));
            txtOverlayTitle = new TextBox { Left = 96, Top = 32, Width = 620 };
            PlaceholderHelper.Attach(txtOverlayTitle, "留空使用默认：今日游戏时长");
            txtOverlayTitle.TextChanged += (s, e) => MarkGlobalDirty();
            grp.Controls.Add(txtOverlayTitle);

            // 2) 字体/字号/字体色/背景色/整体透明度
            grp.Controls.Add(MakeRowLabel("字体：", 16, 70, 52));
            cmbOverlayFont = new ComboBox
            {
                Left = 72, Top = 69, Width = 140,
                DropDownStyle = ComboBoxStyle.DropDownList, MaxDropDownItems = 18
            };
            cmbOverlayFont.Items.AddRange(InstalledFontNames());
            grp.Controls.Add(cmbOverlayFont);

            grp.Controls.Add(MakeRowLabel("字号：", 226, 70, 48));
            numOverlayFontSize = new NumericUpDown { Left = 278, Top = 70, Width = 56, Minimum = 6, Maximum = 30 };
            grp.Controls.Add(numOverlayFontSize);

            grp.Controls.Add(MakeRowLabel("字体色：", 350, 70, 64));
            btnOverlayTextColor = new Button { Left = 418, Top = 70, Width = 50, Height = 23, FlatStyle = FlatStyle.Flat };
            grp.Controls.Add(btnOverlayTextColor);

            grp.Controls.Add(MakeRowLabel("背景色：", 482, 70, 64));
            btnOverlayBackColor = new Button { Left = 550, Top = 70, Width = 50, Height = 23, FlatStyle = FlatStyle.Flat };
            grp.Controls.Add(btnOverlayBackColor);

            grp.Controls.Add(MakeRowLabel("整体透明度%：", 614, 70, 100));
            numOverlayOpacityPercent = new NumericUpDown { Left = 718, Top = 70, Width = 56, Minimum = 0, Maximum = 100 };
            grp.Controls.Add(numOverlayOpacityPercent);

            // 3) 圆点颜色（5 态）
            grp.Controls.Add(MakeRowLabel("圆点颜色：", 16, 112, 76));
            string[] dotNames = { "未运行", "运行中", "提醒倒计时", "间隔期", "配额用完" };
            for (int i = 0; i < dotNames.Length; i++)
            {
                int x = 100 + i * 138;
                // 标签右对齐 + 色卡固定偏移 ⇒ 每组“文字→色卡”的间距完全一致
                var nameLabel = MakeRowLabel(dotNames[i], x, 112, 76);
                nameLabel.TextAlign = ContentAlignment.MiddleRight;
                grp.Controls.Add(nameLabel);
                var btn = new Button { Left = x + 82, Top = 112, Width = 50, Height = 23, FlatStyle = FlatStyle.Flat };
                btnDotColors[i] = btn;
                grp.Controls.Add(btn);
                int index = i;
                btn.Click += (s, e) => PickColor(btn, c =>
                {
                    _dotColors[index] = c;
                    btn.BackColor = c;
                    MarkGlobalDirty();
                });
            }

            grp.Controls.Add(MakeRowLabel("圆角：", 16, 148, 52));
            cmbOverlayCorner = new ComboBox
            {
                Left = 72, Top = 147, Width = 130,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbOverlayCorner.Items.AddRange(new object[] { "方角", "小圆角", "中圆角", "大圆角" });
            cmbOverlayCorner.SelectedIndexChanged += (s, e) => MarkGlobalDirty();
            grp.Controls.Add(cmbOverlayCorner);

            btnOverlayTextColor.Click += (s, e) => PickColor(btnOverlayTextColor, c => _overlayTextColor = c);
            btnOverlayBackColor.Click += (s, e) => PickColor(btnOverlayBackColor, c => _overlayBackColor = c);
            return grp;
        }

        private TabPage BuildHistoryTab()
        {
            var page = MakeTab("使用记录", scroll: false);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f)); // 表格填充剩余高度
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));  // 说明紧跟表格下方

            var top = new Panel { Dock = DockStyle.Fill };
            cmbHistoryDays = new ComboBox { Left = 0, Top = 6, Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbHistoryDays.Items.AddRange(new object[] { "最近 7 天", "最近 14 天" });
            cmbHistoryDays.SelectedIndex = 1;
            var btnRefresh = new Button { Text = "刷新", Left = 142, Top = 4, Width = 80, Height = 28 };
            var btnClearHistory = new Button { Text = "清空", Left = 230, Top = 4, Width = 80, Height = 28 };
            top.Controls.Add(cmbHistoryDays);
            top.Controls.Add(btnRefresh);
            top.Controls.Add(btnClearHistory);
            layout.Controls.Add(top, 0, 0);

            chartHistory = new UsageBarChart
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White
            };
            layout.Controls.Add(chartHistory, 0, 1);

            gridHistory = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                ReadOnly = true,
                AutoGenerateColumns = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                ColumnHeadersHeight = 32,
                // 列宽随表格自适应铺满，窄窗口按最小宽度收缩，不再横向留白
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ScrollBars = ScrollBars.Vertical
            };
            gridHistory.RowTemplate.Height = 28;
            AddHistoryColumn("日期", "Date", 100, 110);
            AddHistoryColumn("首次开启", "FirstStart", 78, 96);
            AddHistoryColumn("末次关闭", "LastEnd", 78, 96);
            AddHistoryColumn("已用 (分钟)", "Used", 78, 96);
            AddHistoryColumn("启动次数", "Launches", 72, 88);
            AddHistoryColumn("加时 (分钟)", "Bonus", 78, 96);
            AddHistoryColumn("最后结束原因", "Reason", 140, 200);
            AddHistoryColumn("进入过间隔期", "Cooldown", 96, 108);
            layout.Controls.Add(gridHistory, 0, 2);

            // 说明精简为两条，放在表格下方
            var hints = new Panel { Dock = DockStyle.Fill };
            hints.Controls.Add(MakeHint("柱状图：每小时使用时长（当日 0~23 时）。", 0, 2, 800));
            hints.Controls.Add(MakeHint("表格按天汇总。", 0, 23, 800));
            layout.Controls.Add(hints, 0, 3);

            page.Controls.Add(layout);

            cmbHistoryDays.SelectedIndexChanged += (s, e) => RefreshHistory();
            btnRefresh.Click += (s, e) => RefreshHistory();
            btnClearHistory.Click += (s, e) => ClearUsageHistory();
            gridHistory.DataError += GridHistory_DataError;
            return page;
        }

        private TabPage BuildAboutTab()
        {
            var page = MakeTab("关于");

            var grp = MakeGroup("程序信息", 12, 10, 850, 170);
            lblVersion = MakeHint($"版本 {Application.ProductVersion}", 16, 32, 800);
            lblLogPath = MakeHint($"日志目录：{AppLog.DirectoryPath}", 16, 68, 800);
            lblGitHub = MakeGitHubLink(16, 104);
            grp.Controls.Add(lblVersion);
            grp.Controls.Add(lblLogPath);
            grp.Controls.Add(lblGitHub);
            page.Controls.Add(grp);

            var grpOps = MakeGroup("日志与配置", 12, 190, 850, 170);
            var btnOpenLog = new Button { Text = "打开日志目录", Left = 16, Top = 34, Width = 130, Height = 32 };
            var btnExport = new Button { Text = "导出配置", Left = 156, Top = 34, Width = 110, Height = 32 };
            var btnImport = new Button { Text = "导入配置", Left = 276, Top = 34, Width = 110, Height = 32 };
            grpOps.Controls.Add(btnOpenLog);
            grpOps.Controls.Add(btnExport);
            grpOps.Controls.Add(btnImport);
            grpOps.Controls.Add(MakeHint("导出为 GameMonitor-config-<日期>.xml，不包含管理密码；导入会覆盖当前设置（管理密码保持不变），导入前会提示覆盖风险。", 16, 82, 800));
            page.Controls.Add(grpOps);

            // 收款码：放在“日志与配置”分组下方，按剩余空间等比放大并居中（内容不足一屏时用留白）
            var reward = LoadRewardImage();
            if (reward != null)
            {
                var pic = new PictureBox
                {
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Image = reward
                };
                var caption = new Label
                {
                    Text = "感谢打赏，感谢支持！",
                    Height = 24,
                    TextAlign = ContentAlignment.MiddleCenter
                };
                page.Controls.Add(pic);
                page.Controls.Add(caption);
                page.Resize += (s, e) => LayoutRewardArea(page, pic, caption);
                page.Layout += (s, e) => LayoutRewardArea(page, pic, caption);
                LayoutRewardArea(page, pic, caption);
            }
            else
            {
                page.Controls.Add(MakeHint("收款码图片资源缺失，未能显示。", 12, 372, 800));
            }

            btnOpenLog.Click += (s, e) => OpenLogDirectory();
            btnExport.Click += (s, e) => ExportConfig();
            btnImport.Click += (s, e) => ImportConfig();
            return page;
        }

        /// <summary>关于页收款码区域：从分组下方开始，按剩余宽高取正方形，等比缩放到可用空间并水平居中。</summary>
        private static void LayoutRewardArea(TabPage page, PictureBox pic, Label caption)
        {
            const int top = 372;
            const int gap = 8;
            const int captionHeight = 24;
            const int margin = 12;

            int usableWidth = page.ClientSize.Width - margin * 2;
            if (page.VerticalScroll.Visible) usableWidth -= SystemInformation.VerticalScrollBarWidth;
            int usableHeight = page.ClientSize.Height - top - captionHeight - gap - margin;

            int side = Math.Max(100, Math.Min(usableWidth, usableHeight));
            pic.Width = side;
            pic.Height = side;
            pic.Left = Math.Max(margin, (page.ClientSize.Width - side) / 2);
            pic.Top = top;

            caption.Left = margin;
            caption.Width = Math.Max(100, usableWidth);
            caption.Top = top + side + gap;
        }

        internal static Image LoadRewardImage()
        {
            const string resourceName = "GameMonitor.reward.png";
            try
            {
                using (var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        AppLog.Warn($"未找到嵌入资源 {resourceName}，关于页不显示收款码。");
                        return null;
                    }

                    using (var buffer = new MemoryStream())
                    {
                        stream.CopyTo(buffer);
                        buffer.Position = 0;
                        return Image.FromStream(buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("加载收款码图片失败", ex);
                return null;
            }
        }

        private void AddHistoryColumn(string header, string name, int minWidth, int fillWeight)
        {
            gridHistory.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                Name = name,
                MinimumWidth = minWidth,
                FillWeight = fillWeight,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private void RefreshHistory()
        {
            if (gridHistory == null || chartHistory == null) return;

            int days = cmbHistoryDays != null && cmbHistoryDays.SelectedIndex == 0 ? 7 : 14;
            List<UsageDailySummary> summary;
            try { summary = UsageHistoryStore.GetDailySummary(days); }
            catch (Exception ex)
            {
                AppLog.Warn("读取使用记录失败", ex);
                return;
            }

            var todaySummary = summary.FirstOrDefault();
            chartHistory.SetHourly(todaySummary?.HourlySeconds, todaySummary?.HasHourlyData ?? false);

            gridHistory.SuspendLayout();
            try
            {
                gridHistory.Rows.Clear();
                foreach (var item in summary)
                {
                    // 无数据的日期不占行：清空历史后表格只保留有数据的今天
                    if (!item.HasData) continue;
                    gridHistory.Rows.Add(
                        item.Date.ToString("yyyy-MM-dd"),
                        string.IsNullOrEmpty(item.FirstStartTime) ? "—" : item.FirstStartTime,
                        string.IsNullOrEmpty(item.LastEndTime) ? "—" : item.LastEndTime,
                        item.UsedMinutes.ToString(),
                        item.LaunchCount.ToString(),
                        item.BonusMinutes.ToString(),
                        item.LastEndReason ?? "",
                        item.WasInCooldown ? "是" : "");
                }
            }
            finally
            {
                gridHistory.ResumeLayout();
            }
        }

        /// <summary>清空“今天以前”的使用记录（今天这一行与今日配额保持不变），只需二次确认，不再校验管理密码。</summary>
        private void ClearUsageHistory()
        {
            if (MessageBox.Show(
                    "将删除今天以前的历史记录。",
                    "清空确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            int removed;
            try
            {
                removed = _monitor.ClearAllUsage();
            }
            catch (Exception ex)
            {
                AppLog.Error("清空使用记录失败", ex);
                MessageBox.Show("清空失败：" + ex.Message, "游戏监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 只删除今天以前的行：今日已用/会话累计/运行态都不变，无需同步内存副本
            RefreshQuotaUi();
            RefreshHistory();
            AppLog.Info($"用户从设置页清空今天以前的使用记录：删除 {removed} 条历史行（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）。");
            MessageBox.Show($"已删除今天以前的历史记录（{removed} 条）。", "游戏监控",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RefreshQuotaUi()
        {
            if (lblPauseState == null) return;
            if (_monitor.IsPaused(DateTime.Now))
            {
                int minutes = Math.Max(1, (int)Math.Ceiling((_config.PauseUntil - DateTime.Now).TotalMinutes));
                lblPauseState.Text = $"暂停中（剩余约 {minutes} 分）";
                lblPauseState.ForeColor = Color.Crimson;
                btnPause.Enabled = false;
                btnResumePause.Enabled = true;
            }
            else
            {
                // 与网页/悬浮框统一：显示“本轮加时合计”，旧每日加时已迁移进总限额
                int bonus = 0;
                try
                {
                    foreach (var s in _monitor.GetStatuses())
                        bonus += Math.Max(0, s.SessionBonusSeconds) / 60;
                }
                catch { }
                lblPauseState.Text = bonus > 0 ? $"监控中 ｜ 本轮加时合计 {bonus} 分" : "监控中";
                lblPauseState.ForeColor = Color.DimGray;
                btnPause.Enabled = true;
                btnResumePause.Enabled = false;
            }

            if (lblTotalToday != null)
            {
                int totalUsed = _monitor.GetTotalUsedSecondsToday() / 60;
                int totalLimit = _monitor.GetDailyTotalLimitMinutes();
                lblTotalToday.Text = totalLimit > 0
                    ? $"今日总已用：{totalUsed} 分钟 ｜ 总限额：{totalLimit} 分钟（含加时）"
                    : $"今日总已用：{totalUsed} 分钟 ｜ 总限额：未启用";
                lblTotalToday.ForeColor = totalLimit > 0 && totalUsed >= totalLimit ? Color.Crimson : Color.DimGray;
            }
        }

        /// <summary>监控列表页“临时加时/减时”：只作用于列表中选中的条目（不写旧全局 bonus）。</summary>
        private void ApplyQuickBonus(bool add)
        {
            if (gridGames == null || gridGames.CurrentRow == null)
            {
                MessageBox.Show("请先在监控列表中选择一个条目。", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string keyword = EntryIdOfRow(gridGames.CurrentRow);
            if (string.IsNullOrWhiteSpace(keyword))
            {
                MessageBox.Show("该行未填写匹配关键字，无法加时/减时。", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int minutes = 10;
            string raw = txtQuickMinutes.Text.Trim();
            if (raw.Length > 0)
            {
                if (!int.TryParse(raw, out minutes) || minutes < 1 || minutes > 120)
                {
                    MessageBox.Show("请输入 1~120 之间的分钟数（留空按 10 分钟）。", "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    txtQuickMinutes.Focus();
                    return;
                }
            }

            var adjust = _monitor.AdjustSessionBonus(keyword, add ? minutes : -minutes);
            if (!adjust.Success)
            {
                MessageBox.Show(adjust.Error ?? "操作失败", "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            RefreshQuotaUi();
            RefreshGameStatus();
            if (gridGames.CurrentRow != null)
                UpdateRowUsage(gridGames.CurrentRow.Index, keyword);

            // 非阻断反馈：状态栏文字（不再弹模态框，也不走全屏提示层）
            if (lblPauseState != null)
            {
                lblPauseState.Text = adjust.Message ?? (add
                    ? $"已加时 {minutes} 分"
                    : $"已减时 {minutes} 分");
                lblPauseState.ForeColor = add ? Color.SeaGreen : Color.DarkOrange;
            }
        }

        /// <summary>统一注入输入框占位提示；占位符只是提示，不参与保存与校验。</summary>
        private void AttachPlaceholders()
        {
            for (int i = 0; i < txtPromptTemplate.Length; i++)
                PlaceholderHelper.Attach(txtPromptTemplate[i], "留空使用默认文案");
            for (int i = 0; i < txtSoundFile.Length; i++)
                PlaceholderHelper.Attach(txtSoundFile[i], "留空使用系统音");

            PlaceholderHelper.Attach(txtQuickMinutes, "默认 10");
            PlaceholderHelper.Attach(txtNewPass, "留空保持不变");
            PlaceholderHelper.Attach(txtConfirmPass, "再输入一次确认");
        }

        private bool RequireAdminPassword(string missingPasswordTip = null)
        {
            if (string.IsNullOrEmpty(_config.AdminPassword))
            {
                MessageBox.Show(missingPasswordTip ??
                        "请先在“密码与远程控制”页设置管理密码，暂停监控需要密码校验。",
                    "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            using (var dlg = new PasswordPromptForm("身份验证"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                if (dlg.Password == _config.AdminPassword) return true;
            }
            MessageBox.Show("密码错误！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        /// <summary>关于页的 GitHub 发布页：网址文本完整可见（便于复制/无法点击时查看），点击后用默认浏览器打开。</summary>
        private LinkLabel MakeGitHubLink(int left, int top)
        {
            const string prefix = "GitHub 发布页：";
            var link = new LinkLabel
            {
                Text = prefix + GitHubReleasesUrl,
                Left = left,
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(800, 0),
                ForeColor = Color.DimGray,
                LinkColor = Color.SteelBlue,
                ActiveLinkColor = Color.SteelBlue,
                VisitedLinkColor = Color.SteelBlue,
                // 仅网址部分作为链接，前缀保持普通提示文字样式
                LinkArea = new LinkArea(prefix.Length, GitHubReleasesUrl.Length)
            };
            link.LinkClicked += (s, e) => OpenGitHubReleases();
            return link;
        }

        private void OpenGitHubReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubReleasesUrl) { UseShellExecute = true });
                AppLog.Info("用户从关于页打开 GitHub 发布页。");
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开 GitHub 发布页失败", ex);
                MessageBox.Show("无法打开浏览器：" + ex.Message, "游戏监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenLogDirectory()
        {
            try
            {
                Directory.CreateDirectory(AppLog.DirectoryPath);
                Process.Start("explorer", AppLog.DirectoryPath);
                AppLog.Info("用户从设置页打开日志目录。");
            }
            catch (Exception ex)
            {
                AppLog.Warn("打开日志目录失败", ex);
                MessageBox.Show("无法打开日志目录：" + ex.Message, "游戏监控",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ExportConfig()
        {
            string defaultName = $"GameMonitor-config-{DateTime.Now:yyyyMMdd}.xml";
            using (var dlg = new SaveFileDialog
            {
                Filter = "配置文件 (*.xml)|*.xml",
                FileName = defaultName,
                Title = "导出配置"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    // 先序列化到内存，再把 AdminPassword 清空，避免导出密码
                    var ser = new XmlSerializer(typeof(AppConfig));
                    string xml;
                    using (var ms = new MemoryStream())
                    {
                        ser.Serialize(ms, _config);
                        ms.Position = 0;
                        using (var reader = new StreamReader(ms))
                            xml = reader.ReadToEnd();
                    }

                    var doc = new XmlDocument();
                    doc.LoadXml(xml);
                    var passNode = doc.SelectSingleNode("/AppConfig/AdminPassword");
                    if (passNode != null) passNode.InnerText = "";

                    doc.Save(dlg.FileName);
                    AppLog.Info($"配置已导出到 {dlg.FileName}（不含管理密码）。");
                    MessageBox.Show("配置已导出（不含管理密码）。", "导出成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppLog.Error("导出配置失败", ex);
                    MessageBox.Show("导出失败：" + ex.Message, "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ImportConfig()
        {
            using (var dlg = new OpenFileDialog
            {
                Filter = "配置文件 (*.xml)|*.xml",
                Title = "导入配置"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                AppConfig imported;
                try
                {
                    using (var fs = File.OpenRead(dlg.FileName))
                        imported = new XmlSerializer(typeof(AppConfig)).Deserialize(fs) as AppConfig;
                }
                catch (Exception ex)
                {
                    AppLog.Error("导入配置解析失败", ex);
                    MessageBox.Show("配置文件格式不正确：" + ex.Message, "导入失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (imported == null || imported.Games == null || imported.Games.Count > 100)
                {
                    MessageBox.Show("配置文件内容不合法（缺少监控列表或条目过多）。", "导入失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (MessageBox.Show(
                        "导入会覆盖当前的监控列表、时间与配额等全部设置（管理密码保持不变）。\n确定继续吗？",
                        "覆盖确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                try
                {
                    CopyImportableConfig(imported, _config);
                    _config.Save();
                    _gamesDirty = false;
                    LoadFromConfig();
                    RefreshQuotaUi();
                    RefreshHistory();
                    ConfigSaved?.Invoke();
                    AppLog.Info($"配置已从 {dlg.FileName} 导入并应用。");
                    MessageBox.Show("配置已导入并应用。", "导入成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    AppLog.Error("导入配置应用失败", ex);
                    MessageBox.Show("导入失败：" + ex.Message, "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>复制可导入字段；管理密码不参与导入（避免把别人的密码覆盖进来）。</summary>
        private static void CopyImportableConfig(AppConfig src, AppConfig dst)
        {
            foreach (var prop in typeof(AppConfig).GetProperties())
            {
                if (!prop.CanWrite) continue;
                if (prop.Name == "AdminPasswordStored" || prop.Name == "AdminPassword") continue;
                prop.SetValue(dst, prop.GetValue(src));
            }
            if (dst.Games == null) dst.Games = new List<GameItem>();
            if (dst.Usage == null) dst.Usage = new List<GameUsage>();
            if (dst.Cooldowns == null) dst.Cooldowns = new List<CooldownState>();
            if (dst.BonusRecords == null) dst.BonusRecords = new List<BonusRecord>();
        }

        /// <summary>远程控制台分组（Web 开关 / 局域网 / 端口 / 说明），并入“密码与远程控制”页。</summary>
        private GroupBox BuildRemoteGroup(int left, int top)
        {
            var grp = MakeGroup("远程控制台", left, top, 850, 320);
            chkEnableRemote = new CheckBox { Text = "启用 Web 控制台", Left = 16, Top = 28, AutoSize = true };
            chkRemoteAllowLan = new CheckBox { Text = "允许局域网访问（默认只监听 127.0.0.1）", Left = 16, Top = 60, AutoSize = true };
            grp.Controls.Add(chkEnableRemote);
            grp.Controls.Add(chkRemoteAllowLan);

            grp.Controls.Add(MakeRowLabel("监听端口：", 16, 97, 70));
            numRemotePort = new NumericUpDown { Left = 95, Top = 97, Width = 90, Minimum = 1024, Maximum = 65535 };
            grp.Controls.Add(numRemotePort);

            lblRemoteAddress = new Label { Left = 16, Top = 126, Width = 820, Height = 24, TextAlign = ContentAlignment.MiddleLeft };
            grp.Controls.Add(lblRemoteAddress);

            lblRemoteHint = MakeHint(
                "作用：在手机/另一台电脑的浏览器里查看今日用量、修改监控列表、结束本次、强制关闭、条目加时与暂停。\n" +
                "必须先在本页上方“管理密码”里设置管理密码，密码是唯一凭证；登录页与设置页共用同一密码。\n" +
                "默认只监听回环地址（127.0.0.1），只有本机能访问；勾选“允许局域网访问”后会监听 0.0.0.0，同一局域网内任何设备都能访问。\n" +
                "通信为明文 HTTP，且局域网内没有额外防护，建议只在需要时临时开启，用完立刻关闭。\n" +
                "端口被占用时启动会失败并提示，不会出现“显示已启用但其实没监听”的情况。",
                16, 158, 800);
            grp.Controls.Add(lblRemoteHint);

            chkEnableRemote.CheckedChanged += (s, e) => UpdateRemoteUiState();
            chkRemoteAllowLan.CheckedChanged += (s, e) => UpdateRemoteAddressLabel();
            numRemotePort.ValueChanged += (s, e) => UpdateRemoteAddressLabel();

            return grp;
        }

        private TabPage BuildPasswordTab()
        {
            var page = MakeTab("密码与远程控制");

            var grp = MakeGroup("管理密码", 12, 10, 850, 220);
            grp.Controls.Add(MakeRowLabel("新密码：", 16, 33, 70));
            txtNewPass = new TextBox { Left = 95, Top = 33, Width = 220, UseSystemPasswordChar = true };
            grp.Controls.Add(txtNewPass);

            grp.Controls.Add(MakeRowLabel("确认密码：", 16, 71, 70));
            txtConfirmPass = new TextBox { Left = 95, Top = 71, Width = 220, UseSystemPasswordChar = true };
            grp.Controls.Add(txtConfirmPass);

            chkClearPassword = new CheckBox { Text = "清除管理密码（清除后会立即关闭 Web 控制台）", Left = 16, Top = 110, AutoSize = true };
            grp.Controls.Add(chkClearPassword);

            lblPasswordHint = MakeHint("管理密码是 Web 控制台与暂停监控的唯一凭证；密码为空时这些功能不可用（临时加时/减时不需要密码）。", 16, 144, 800);
            grp.Controls.Add(lblPasswordHint);
            grp.Controls.Add(MakeHint("密码使用 Windows DPAPI（当前用户）加密后写入 config.xml，绑定当前 Windows 用户；换用户或换机器时密码不可用属预期行为。", 16, 182, 800));
            page.Controls.Add(grp);

            page.Controls.Add(BuildRemoteGroup(12, 242));

            chkClearPassword.CheckedChanged += (s, e) => UpdatePasswordUiState();
            txtNewPass.TextChanged += (s, e) => UpdateRemoteUiState();
            txtConfirmPass.TextChanged += (s, e) => UpdateRemoteUiState();

            return page;
        }

        #endregion

        #region 数据读写

        private void LoadFromConfig()
        {
            _loading = true;
            _suppressDirty = true;
            try
            {
                cmbModifier.SelectedIndex = ModifierIndex(_config.HotkeyModifiers);
                cmbKey.SelectedItem = VkToName(_config.HotkeyVk);
                if (cmbKey.SelectedIndex < 0) cmbKey.SelectedItem = "G";

                chkHideTrayIcon.Checked = _config.HideTrayIcon;
                chkMouseTransparent.Checked = _config.MouseTransparent;
                chkEnableDaemon.Checked = _config.EnableDaemon;

                numReminder.Value = Clamp(_config.ReminderMinutes, 1, 60);
                numCountdown.Value = Clamp(_config.CountdownSeconds, 5, 600);
                numDailyTotalLimit.Value = Clamp(_config.DailyTotalLimitMinutes, 0, 1440);
                numNoticeStay.Value = Clamp(_config.NoticeStaySeconds <= 0 ? 5 : _config.NoticeStaySeconds, 1, 60);
                chkSound[0].Checked = _config.SoundOnGrace;
                chkSound[1].Checked = _config.SoundOnCountdown;
                chkSound[2].Checked = _config.SoundOnCooldown;
                chkSound[3].Checked = _config.SoundOnQuota;
                chkSound[4].Checked = _config.SoundOnBlockedBySchedule;
                txtSoundFile[0].Text = _config.SoundGraceFile ?? "";
                txtSoundFile[1].Text = _config.SoundCountdownFile ?? "";
                txtSoundFile[2].Text = _config.SoundCooldownFile ?? "";
                txtSoundFile[3].Text = _config.SoundQuotaFile ?? "";
                txtSoundFile[4].Text = _config.SoundBlockedByScheduleFile ?? "";

                LoadPromptStyles(_config.OverlayStyle);

                chkOverlayEnabled.Checked = _config.StatusOverlayEnabled;
                chkOverlayTopMost.Checked = _config.StatusOverlayTopMost;
                chkOverlaySnap.Checked = _config.StatusOverlaySnap;
                chkOverlayAutoHide.Checked = _config.StatusOverlayAutoHide;
                chkOverlayEdgeHide.Checked = _config.StatusOverlayEdgeHide;
                numOverlayAutoHideSeconds.Value = Clamp(_config.StatusOverlayAutoHideSeconds, 1, 120);
                numOverlayEdgeHideSeconds.Value = Clamp(_config.StatusOverlayEdgeHideSeconds, 1, 120);
                txtOverlayTitle.Text = _config.StatusOverlayTitleText ?? "";
                numOverlayOpacityPercent.Value = Clamp(_config.StatusOverlayOpacityPercent, 0, 100);
                cmbOverlayCorner.SelectedIndex = CornerIndexFromRadius(_config.StatusOverlayCornerRadius);
                SelectFont(cmbOverlayFont, _config.StatusOverlayFontFamily);
                numOverlayFontSize.Value = Clamp(_config.StatusOverlayFontSize, 6, 30);
                _overlayTextColor = Color.FromArgb(
                    Clamp(_config.StatusOverlayTextColorR, 0, 255),
                    Clamp(_config.StatusOverlayTextColorG, 0, 255),
                    Clamp(_config.StatusOverlayTextColorB, 0, 255));
                _overlayBackColor = Color.FromArgb(
                    Clamp(_config.StatusOverlayBackColorR, 0, 255),
                    Clamp(_config.StatusOverlayBackColorG, 0, 255),
                    Clamp(_config.StatusOverlayBackColorB, 0, 255));
                btnOverlayTextColor.BackColor = _overlayTextColor;
                btnOverlayBackColor.BackColor = _overlayBackColor;
                LoadDotColors();

                chkEnableRemote.Checked = _config.EnableRemote;
                chkRemoteAllowLan.Checked = _config.RemoteAllowLan;
                numRemotePort.Value = Clamp(_config.RemotePort, 1024, 65535);
                numPauseMinutes.Value = Clamp(_config.PauseMinutes <= 0 ? 30 : _config.PauseMinutes, 1, 1440);

                chkStartWithWindows.Checked = _config.StartWithWindows;
                txtNewPass.Text = "";
                txtConfirmPass.Text = "";
                chkClearPassword.Checked = false;

                if (!_gamesDirty)
                {
                    RebuildGrid();
                    LoadScheduleRules();
                    _editUsage = CloneUsage(_config.Usage);
                }
            }
            finally
            {
                _loading = false;
                _suppressDirty = false;
                UpdateRemoteUiState();
                UpdatePasswordUiState();
                RefreshQuotaUi();
                RefreshHistory();
            }
        }

        /// <summary>网页/托盘改了配置后刷新设置页（有未保存的列表改动时不覆盖）。</summary>
        public void ReloadData()
        {
            if (IsDisposed) return;
            if (_gamesDirty) return;
            LoadFromConfig();
        }

        private void RebuildGrid()
        {
            bool previous = _suppressDirty;
            _suppressDirty = true;
            try
            {
                gridGames.Rows.Clear();
                foreach (var g in _config.Games)
                    AddGridRow(g, select: false);
                gridGames.ClearSelection();
            }
            finally
            {
                _suppressDirty = previous;
            }
            UpdateGamesEmptyHint();
        }

        /// <summary>无监控条目时在列表底部给出明确但不阻断保存的提示。</summary>
        private void UpdateGamesEmptyHint()
        {
            if (_bottomHint == null) return;
            bool empty = gridGames == null || gridGames.Rows.Count == 0;
            _bottomHint.Text = empty
                ? "4. 当前没有监控条目：保存后将不会监控任何程序。"
                : "4. 提示：删除条目会同时清理该条目的今日用量记录；改关键字后用量会按新关键字重新统计。";
            _bottomHint.ForeColor = empty ? Color.FromArgb(180, 83, 9) : Color.DimGray;
        }

        /// <summary>行的条目标识：多关键字行的标识是首个关键字（与配置/会话记录一致）。</summary>
        private string EntryIdOfRow(DataGridViewRow row)
        {
            var keywords = AppConfig.SplitKeywords(CellText(row, colKeyword.Index));
            return keywords.Count > 0 ? keywords[0] : "";
        }

        private void AddGridRow(GameItem g, bool select = true)
        {
            bool previous = _suppressDirty;
            _suppressDirty = true;
            try
            {
                int index = gridGames.Rows.Add(
                    g.DisplayName,
                    "—",
                    MatchModeLabels[ModeIndex(g.MatchMode)],
                    AppConfig.JoinKeywords(g.KeywordList),
                    g.RunMinutes,
                    g.DailyLimitMinutes,
                    g.CooldownMinutes > 0 ? g.CooldownMinutes : 10,
                    FormatMultiplier(g.WeekendMultiplier),
                    "—",
                    g.Enabled,
                    "删除");
                UpdateRowUsage(index, g.ProcessName);
                if (select) gridGames.Rows[index].Selected = true;
            }
            finally
            {
                _suppressDirty = previous;
            }
        }

        private void AddGameRow()
        {
            var g = new GameItem
            {
                DisplayName = "新监控程序",
                ProcessName = "",
                RunMinutes = 60,
                DailyLimitMinutes = 0,
                CooldownMinutes = 10,
                WeekendMultiplier = 1.0,
                Enabled = true,
                MatchMode = MatchMode.Contains
            };

            AddGridRow(g, select: false);
            _gamesDirty = true;

            int index = gridGames.Rows.Count - 1;
            gridGames.ClearSelection();
            gridGames.Rows[index].Selected = true;
            gridGames.CurrentCell = gridGames.Rows[index].Cells[colName.Index];
            gridGames.Focus();
            gridGames.BeginEdit(true);
            UpdateGamesEmptyHint();
        }

        private void DeleteGameRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= gridGames.Rows.Count) return;

            var row = gridGames.Rows[rowIndex];
            string keyword = EntryIdOfRow(row);
            if (MessageBox.Show($"确定删除第 {rowIndex + 1} 行（{CellText(row, colName.Index)}）？", "删除监控条目",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            gridGames.Rows.RemoveAt(rowIndex);
            var usage = _editUsage.FirstOrDefault(u =>
                u.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (usage != null) _editUsage.Remove(usage);
            _gamesDirty = true;
            UpdateGamesEmptyHint();
        }

        private void ResetSelectedUsage()
        {
            if (gridGames.CurrentRow == null || gridGames.CurrentRow.Index < 0)
            {
                MessageBox.Show("请先在列表中选择一行。", "游戏监控");
                return;
            }

            string keyword = EntryIdOfRow(gridGames.CurrentRow);
            if (string.IsNullOrWhiteSpace(keyword)) return;

            // 用量以运行中的监控实际读写的 _config.Usage 为准。
            // 只改 _editUsage 副本的话，监控线程仍按旧值继续计时，下次定时保存又把旧值写回磁盘，
            // 界面刷新（GetStatuses）也会立刻显示回旧值 —— 表现就是“重置不生效、改完又被还原”。
            DateTime today = DateTime.Today;
            lock (_config.SyncRoot)
            {
                var live = _config.Usage.FirstOrDefault(u =>
                    u != null && u.ProcessName != null &&
                    u.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                    u.Date.Date == today);
                if (live != null)
                    live.UsedSeconds = 0;
                else
                    _config.Usage.Add(new GameUsage { ProcessName = keyword, Date = today, UsedSeconds = 0 });
            }

            // 编辑快照同步清零，避免保存时把打开设置页时的旧值写回（旧值回写）
            var edit = _editUsage.FirstOrDefault(u =>
                u != null && u.ProcessName != null &&
                u.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (edit != null) edit.UsedSeconds = 0;

            // 与网页端统一：同时清掉使用记录里的今日已用与小时桶（柱状图归零）
            UsageHistoryStore.ResetUsed(keyword);
            UsageHistoryStore.SaveIfDirty();

            // 手动重置今日用量 = 清零时机③：同时把本次会话累计归零；内部会 SaveQuiet 把上面的清零落盘
            _monitor.ResetEntrySession(keyword);
            _monitor.CancelCountdownIfUsageReset();
            UpdateRowUsage(gridGames.CurrentRow.Index, keyword);
        }

        /// <summary>按关键字取该条目今日的条目级加时分钟数。</summary>
        private int EntryBonusForKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return 0;
            var game = _config.Games.FirstOrDefault(g =>
                g != null && g.ProcessName != null &&
                g.ProcessName.Equals(keyword.Trim(), StringComparison.OrdinalIgnoreCase));
            return _config.EntryBonusMinutesFor(game, DateTime.Today);
        }

        /// <summary>
        /// 刷新一行“已用/配额”。配额与周末倍数一律取 GameStatus 的生效值；
        /// 只有正在编辑、尚未保存的新行（拿不到状态）才按行内配置估算。
        /// 同时展示“本轮剩余 X/Y（本轮加时 ±N）”，与新加时模型一致。
        /// </summary>
        private void UpdateRowUsage(int rowIndex, string keyword, GameStatus status = null)
        {
            var row = gridGames.Rows[rowIndex];
            var st = status;
            if (st == null)
            {
                try
                {
                    st = _monitor.GetStatuses().FirstOrDefault(s =>
                        s.Game != null && keyword.Length > 0 &&
                        s.Game.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                }
                catch { st = null; }
            }

            int used;
            int quota;
            if (st != null)
            {
                // 与悬浮窗/网页保持一致：分钟一律向上取整，不截断
                used = (int)Math.Ceiling(st.UsedSecondsToday / 60.0);
                quota = st.EffectiveLimitMinutes;
            }
            else
            {
                var usage = _editUsage.FirstOrDefault(u =>
                    u.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase) && u.Date.Date == DateTime.Today);
                used = usage == null ? 0 : (int)Math.Ceiling(usage.UsedSeconds / 60.0);

                quota = ParseIntSafe(CellText(row, colDaily.Index), 0);
                bool weekendNow = GameMonitor.IsWeekend(DateTime.Today);
                double multiplierNow = ParseMultiplierSafe(CellText(row, colWeekend.Index));
                if (quota > 0)
                {
                    if (weekendNow)
                        quota = Math.Max(1, (int)Math.Round(quota * multiplierNow, MidpointRounding.AwayFromZero));
                    quota += EntryBonusForKeyword(keyword);
                }
                else
                {
                    int totalBase = Math.Max(0, _config.DailyTotalLimitMinutes);
                    quota = totalBase > 0 ? totalBase + EntryBonusForKeyword(keyword) : 0;
                }
            }

            string suffix = st != null
                ? (st.WeekendMultiplier > 1.0 ? $" 周末×{st.WeekendMultiplier:0.#}" : "")
                : "";
            string round = "";
            if (st != null && st.RoundLimitMinutes > 0)
            {
                round = $" ｜ 本轮剩余 {st.RoundRemainingMinutes}/{st.RoundLimitMinutes} 分";
                if (st.SessionBonusSeconds != 0)
                    round += $"（本轮加时 {(st.SessionBonusSeconds > 0 ? "+" : "-")}{Math.Abs(st.SessionBonusSeconds) / 60} 分）";
            }
            row.Cells[colUsed.Index].Value = $"{used} / {(quota > 0 ? quota.ToString() : "不限")}{suffix}{round}";
        }

        private void RefreshGameStatus()
        {
            RefreshQuotaUi();
            if (gridGames == null || gridGames.IsDisposed || gridGames.Rows.Count == 0) return;
            if (gridGames.IsCurrentCellInEditMode) return;

            var items = new List<GameItem>();
            var keywords = new List<string>();
            foreach (DataGridViewRow row in gridGames.Rows)
            {
                string keyword = EntryIdOfRow(row);
                keywords.Add(keyword);
                items.Add(new GameItem { ProcessName = keyword, MatchMode = ModeFromLabel(CellText(row, colMode.Index)) });
            }

            bool[] running = ProcessMatcher.QueryRunning(items);
            List<GameStatus> statuses;
            try { statuses = _monitor.GetStatuses(); }
            catch { statuses = new List<GameStatus>(); }

            _suppressDirty = true;
            try
            {
                for (int i = 0; i < gridGames.Rows.Count; i++)
                {
                    var row = gridGames.Rows[i];
                    string keyword = keywords[i];
                    var st = statuses.FirstOrDefault(s =>
                        s.Game != null && keyword.Length > 0 &&
                        s.Game.ProcessName.Equals(keyword, StringComparison.OrdinalIgnoreCase));

                    // 状态文字与悬浮窗共用 PhaseText，避免同一状态出现两套说法
                    string text = st != null
                        ? PhaseText.Describe(st).Text
                        : ((i < running.Length && running[i]) ? "运行中" : "未运行");

                    row.Cells[colStatus.Index].Value = text;
                    UpdateRowUsage(i, keyword, st);
                }
            }
            finally
            {
                _suppressDirty = false;
            }
        }

        /// <summary>校验列表并写入 _config.Games（失败时返回行号用于定位）。</summary>
        private bool TryCommitGameList(out string error, out int errorRow,
            out AppConfig.GameValidationResult validation, out List<GameItem> parsedGames)
        {
            error = null;
            errorRow = -1;
            validation = null;
            parsedGames = null;

            // 先把正在编辑的单元格提交，否则单元格仍处于编辑态时 Cell.Value 还是旧值，
            // 用户刚输入的时间会被静默丢弃（保存后看起来“改不生效”）。
            if (!gridGames.EndEdit())
            {
                error = "有单元格内容未通过校验，请先修正后再保存。";
                errorRow = gridGames.CurrentCell == null ? -1 : gridGames.CurrentCell.RowIndex;
                return false;
            }

            var games = new List<GameItem>();
            // 条目级加时按行保留：保存时重建 GameItem，不能丢 BonusRecords
            var previousBonuses = _config.Games.Select(g => g?.BonusRecords).ToList();

            for (int i = 0; i < gridGames.Rows.Count; i++)
            {
                var row = gridGames.Rows[i];
                if (row.IsNewRow) continue;
                int rowNo = i + 1;

                string displayName = CellText(row, colName.Index);
                var keywords = AppConfig.SplitKeywords(CellText(row, colKeyword.Index));
                MatchMode mode = ModeFromLabel(CellText(row, colMode.Index));

                if (displayName.Length == 0)
                {
                    error = $"第 {rowNo} 行：显示名称不能为空。";
                    errorRow = i;
                    return false;
                }

                if (keywords.Count == 0)
                {
                    error = $"第 {rowNo} 行：匹配关键字不能为空（多个关键字用；分隔）。";
                    errorRow = i;
                    return false;
                }

                // 关键字最短长度/保护名单对每个关键字独立生效
                foreach (var keyword in keywords)
                {
                    if (keyword.Length < GameMonitor.MinKeywordLength)
                    {
                        error = $"第 {rowNo} 行：关键字「{keyword}」至少需要 {GameMonitor.MinKeywordLength} 个字符。";
                        errorRow = i;
                        return false;
                    }
                    if (ProcessMatcher.IsProtectedProcess(keyword))
                    {
                        error = $"第 {rowNo} 行：关键字「{keyword}」命中内置保护名单，不能用于监控。";
                        errorRow = i;
                        return false;
                    }
                }

                // 空单元格按默认值兜底（单次 60 / 每日 0 / 间隔 10），避免程序性留下的空值被当成非法输入。
                if (!TryParseIntWithDefault(CellText(row, colRun.Index), 60, out int runMinutes) || runMinutes < 1 || runMinutes > 1440)
                {
                    error = $"第 {rowNo} 行：单次运行时长必须是 1~1440 的整数。";
                    errorRow = i;
                    return false;
                }

                if (!TryParseIntWithDefault(CellText(row, colDaily.Index), 0, out int daily) || daily < 0 || daily > 1440)
                {
                    error = $"第 {rowNo} 行：每日配额必须是 0~1440 的整数（0 表示沿用每日总限额）。";
                    errorRow = i;
                    return false;
                }

                if (!TryParseIntWithDefault(CellText(row, colCooldown.Index), 10, out int cooldown) || cooldown < 1 || cooldown > 180)
                {
                    error = $"第 {rowNo} 行：间隔时间必须是 1~180 的整数（分钟）。";
                    errorRow = i;
                    return false;
                }

                double multiplier = ParseMultiplierSafe(CellText(row, colWeekend.Index));
                if (multiplier < 0.5 || multiplier > 3.0)
                {
                    error = $"第 {rowNo} 行：周末倍数必须在 0.5~3.0 之间（步进 0.5）。";
                    errorRow = i;
                    return false;
                }
                multiplier = Math.Round(multiplier * 2, MidpointRounding.AwayFromZero) / 2;

                bool enabled = row.Cells[colEnabled.Index].Value is bool b && b;

                var item = new GameItem
                {
                    // 条目标识沿用“首个关键字”：与旧配置/使用记录/会话记录保持兼容；
                    // 修改首个关键字等价于改条目标识（历史统计会切到新标识），其余关键字可自由增删。
                    ProcessName = keywords[0],
                    DisplayName = displayName,
                    Keywords = keywords,
                    MatchMode = mode,
                    RunMinutes = runMinutes,
                    DailyLimitMinutes = daily,
                    CooldownMinutes = cooldown,
                    WeekendMultiplier = multiplier,
                    Enabled = enabled,
                    BonusRecords = i < previousBonuses.Count && previousBonuses[i] != null
                        ? previousBonuses[i]
                        : new List<BonusRecord>()
                };
                // 条目内重复输入：去重后保存，仅记 INFO（不弹窗）
                var intra = AppConfig.FindIntraDuplicates(CellText(row, colKeyword.Index));
                if (intra.Count > 0)
                    AppLog.Info($"设置页：条目「{displayName}」的关键字存在条目内重复（{string.Join("、", intra)}），已去重后保存。");
                games.Add(item);
            }

            // 无条目也允许保存：首次启动没有 config.xml 时，用户可先只调全局参数，
            // 列表为空仅作为提示（见 UpdateGamesEmptyHint），不作为阻断性校验。
            validation = AppConfig.ValidateGames(games);
            if (validation.Error != null)
            {
                error = validation.Error;
                errorRow = -1;
                return false;
            }

            _config.Games.Clear();
            _config.Games.AddRange(games);
            parsedGames = games;
            return true;
        }

        private void ShowGridError(string error, int rowIndex)
        {
            if (rowIndex >= 0 && rowIndex < gridGames.Rows.Count)
            {
                gridGames.ClearSelection();
                gridGames.Rows[rowIndex].Selected = true;
                gridGames.CurrentCell = gridGames.Rows[rowIndex].Cells[colKeyword.Index];
                gridGames.Focus();
            }
            MessageBox.Show(error, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>校验自定义提示音：只接受 .wav，且文件必须存在；错误定位到具体时机。</summary>
        private bool ValidateSoundFiles(out string error)
        {
            error = null;
            string[] names = { "宽限开始", "倒计时开始", "休息期提示", "配额用完", "禁止时段" };
            for (int i = 0; i < txtSoundFile.Length; i++)
            {
                string path = txtSoundFile[i].Text.Trim();
                if (path.Length == 0) continue;

                if (!string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"「{names[i]}」的音频只支持 .wav：{path}";
                    return false;
                }
                if (!File.Exists(path))
                {
                    error = $"「{names[i]}」的音频文件不存在：{path}";
                    return false;
                }
            }
            return true;
        }

        private bool ConfirmDiscardGames()
        {
            var result = MessageBox.Show("监控列表有未保存的修改，是否放弃这些修改？", "未保存的修改",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                _gamesDirty = false;
                RebuildGrid();
                return true;
            }
            return false;
        }

        private void GridGames_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e.ColumnIndex == colCooldown.Index)
            {
                string cooldownText = e.FormattedValue == null ? "" : e.FormattedValue.ToString().Trim();
                // 空单元格放行：交给 CellParsing 按默认 10 分钟兜底，避免“清空后点保存”被当成非法输入
                if (cooldownText.Length == 0) return;
                if (!int.TryParse(cooldownText, out int cooldown) || cooldown < 1 || cooldown > 180)
                {
                    e.Cancel = true;
                    MessageBox.Show("间隔时间需要填写 1~180 的整数。", "输入无效");
                }
                return;
            }

            if (e.ColumnIndex == colWeekend.Index)
            {
                double multiplier = ParseMultiplierSafe(e.FormattedValue == null ? "" : e.FormattedValue.ToString());
                if (multiplier < 0.5 || multiplier > 3.0)
                {
                    e.Cancel = true;
                    MessageBox.Show("周末倍数需要在 0.5~3.0 之间（步进 0.5）。", "输入无效");
                }
                return;
            }

            if (e.ColumnIndex != colRun.Index && e.ColumnIndex != colDaily.Index) return;
            string text = e.FormattedValue == null ? "" : e.FormattedValue.ToString().Trim();
            // 空单元格放行：CellParsing 会按默认值兜底（单次 60 / 每日 0），不再提示“需要填写整数”
            if (text.Length == 0) return;
            if (!int.TryParse(text, out int value))
            {
                e.Cancel = true;
                MessageBox.Show("这里需要填写整数。", "输入无效");
                return;
            }
            int min = e.ColumnIndex == colRun.Index ? 1 : 0;
            int max = e.ColumnIndex == colRun.Index ? 1440 : 1440;
            if (value < min || value > max)
            {
                e.Cancel = true;
                MessageBox.Show($"取值范围是 {min}~{max}。", "输入无效");
            }
        }

        /// <summary>单元格提交时把空值/非法值按默认值兜底，避免 "" 被当成 int/bool 解析。</summary>
        private void GridGames_CellParsing(object sender, DataGridViewCellParsingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            if (e.ColumnIndex == colEnabled.Index)
            {
                e.Value = ParseBoolLoose(e.Value);
                e.ParsingApplied = true;
                return;
            }

            if (e.ColumnIndex == colRun.Index || e.ColumnIndex == colDaily.Index || e.ColumnIndex == colCooldown.Index)
            {
                int fallback = e.ColumnIndex == colRun.Index ? 60 : (e.ColumnIndex == colCooldown.Index ? 10 : 0);
                string text = e.Value == null ? "" : e.Value.ToString().Trim();
                if (text.Length == 0)
                {
                    e.Value = fallback;
                    e.ParsingApplied = true;
                    return;
                }
                if (!int.TryParse(text, out int value))
                {
                    AppLog.Warn($"监控列表输入无效（列 {gridGames.Columns[e.ColumnIndex].HeaderText}）：\"{text}\"，已按默认值 {fallback} 处理。");
                    value = fallback;
                }
                e.Value = value;
                e.ParsingApplied = true;
            }
        }

        private static bool ParseBoolLoose(object value)
        {
            if (value is bool b) return b;
            string text = value == null ? "" : value.ToString().Trim();
            if (text.Length == 0) return true;
            if (bool.TryParse(text, out bool parsed)) return parsed;
            if (text == "1") return true;
            if (text == "0") return false;
            return true;
        }

        /// <summary>兜底：任何格式化/解析异常都不弹 WinForms 默认错误框，恢复默认值并写日志。</summary>
        private void GridGames_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            string column = e.ColumnIndex >= 0 && e.ColumnIndex < gridGames.Columns.Count
                ? gridGames.Columns[e.ColumnIndex].HeaderText
                : "?";
            AppLog.Warn($"监控列表单元格格式错误（行 {e.RowIndex + 1}，列 {column}）：{e.Exception?.Message}");

            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                gridGames.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = DefaultGridValue(e.ColumnIndex);
            if (_bottomHint != null)
                _bottomHint.Text = "表格中有无效值，已恢复默认并写入日志。";
        }

        private object DefaultGridValue(int columnIndex)
        {
            if (columnIndex == colEnabled.Index) return true;
            if (columnIndex == colRun.Index) return 60;
            if (columnIndex == colDaily.Index) return 0;
            if (columnIndex == colCooldown.Index) return 10;
            if (columnIndex == colWeekend.Index) return "1";
            return "";
        }

        private void GridHistory_DataError(object sender, DataGridViewDataErrorEventArgs e)
        {
            e.ThrowException = false;
            AppLog.Warn($"使用记录表格格式错误（行 {e.RowIndex + 1}，列 {e.ColumnIndex + 1}）：{e.Exception?.Message}");
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
                gridHistory.Rows[e.RowIndex].Cells[e.ColumnIndex].Value = "—";
        }

        private void SaveAndNotify(bool closeAfter)
        {
            // 0) 全局快捷键：原“应用快捷键”按钮的校验与下发已迁移到这里，随“应用/确定”一起保存
            if (!TryReadHotkeyFromUi(out uint hotkeyMod, out uint hotkeyVk, out string hotkeyError))
            {
                tabs.SelectedTab = TabHotkey;
                MessageBox.Show(hotkeyError, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                if (cmbModifier.SelectedIndex < 0) cmbModifier.Focus();
                else cmbKey.Focus();
                return;
            }

            // 1) 密码与远程控制
            if (!ApplyPassword(out string passwordError))
            {
                tabs.SelectedTab = TabPassword;
                MessageBox.Show(passwordError, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 2) 监控列表
            if (!TryCommitGameList(out string gameError, out int errorRow, out var gameValidation,
                    out var parsedGames))
            {
                tabs.SelectedTab = tabGames;
                ShowGridError(gameError, errorRow);
                return;
            }
            // 跨条目重复关键字：确认后仍可保存（归属按列表顺序先者独占）
            if (gameValidation != null && gameValidation.NeedsConfirm)
            {
                var answer = MessageBox.Show(
                    gameValidation.ConfirmText + "\n\n（保存后仍按列表顺序归属靠前的条目）",
                    "重复关键字", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    tabs.SelectedTab = tabGames;
                    _gamesDirty = true;   // 取消保存：脏标记保持，未写入任何配置
                    return;
                }
                AppLog.Info($"设置页保存时存在重复关键字：{string.Join("、", gameValidation.Duplicates)}（用户已确认）");
            }
            // 确认通过后才真正落到配置对象（取消时内存配置也不变）
            AppConfig.AssignUniqueEntryIds(parsedGames);
            _config.Games.Clear();
            _config.Games.AddRange(parsedGames);

            // 3) 提示声音文件校验（在“常规”页）
            if (!ValidateSoundFiles(out string soundError))
            {
                tabs.SelectedTab = TabGeneral;
                MessageBox.Show(soundError, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 4) 其它字段
            ApplyConfigFromControls();
            _config.HotkeyModifiers = hotkeyMod;
            _config.HotkeyVk = (int)hotkeyVk;
            _config.Save();
            _gamesDirty = false;

            _editUsage = _editUsage
                .Where(u => _config.Games.Any(g => g.ProcessName.Equals(u.ProcessName, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            ConfigSaved?.Invoke();
            HotkeyApplyRequested?.Invoke(hotkeyMod, hotkeyVk);
            AppLog.Info("设置页：配置已保存。");

            // 两字关键字不拦截保存，只在保存后给一次非阻断提示
            var shortKeywords = AppConfig.FindShortKeywords(_config.Games);
            if (shortKeywords.Count > 0)
            {
                string notice = $"关键字「{string.Join("、", shortKeywords)}」较短，可能匹配到多个窗口/进程，建议写得更具体。";
                AppLog.Info($"设置页保存：存在较短关键字（{string.Join("、", shortKeywords)}），已给出非阻断提示。");
                NoticeRequested?.Invoke(notice);
            }

            if (closeAfter) Close();
        }

        private bool ApplyPassword(out string error)
        {
            error = null;

            if (chkClearPassword.Checked)
            {
                _config.AdminPassword = "";
                return true;
            }

            string newPass = txtNewPass.Text.Trim();
            if (newPass.Length == 0)
            {
                // 未填写则保持原密码；但开启 Web 必须已有密码
                if (chkEnableRemote.Checked && string.IsNullOrEmpty(_config.AdminPassword))
                {
                    error = "启用 Web 控制台前必须先设置管理密码。";
                    return false;
                }
                return true;
            }

            if (newPass.Length < 4 || newPass.Length > 64)
            {
                error = "管理密码长度需为 4~64 个字符。";
                return false;
            }

            if (!newPass.Equals(txtConfirmPass.Text.Trim(), StringComparison.Ordinal))
            {
                error = "两次输入的密码不一致。";
                return false;
            }

            _config.AdminPassword = newPass;
            return true;
        }

        private void ApplyConfigFromControls()
        {
            _config.HideTrayIcon = chkHideTrayIcon.Checked;
            _config.MouseTransparent = chkMouseTransparent.Checked;
            _config.EnableDaemon = chkEnableDaemon.Checked;

            _config.ReminderMinutes = (int)numReminder.Value;
            _config.CountdownSeconds = (int)numCountdown.Value;
            _config.DailyTotalLimitMinutes = (int)numDailyTotalLimit.Value;
            _config.PauseMinutes = Clamp((int)numPauseMinutes.Value, 1, 1440);
            _config.NoticeStaySeconds = Clamp((int)numNoticeStay.Value, 1, 60);
            _config.SoundOnGrace = chkSound[0].Checked;
            _config.SoundOnCountdown = chkSound[1].Checked;
            _config.SoundOnCooldown = chkSound[2].Checked;
            _config.SoundOnQuota = chkSound[3].Checked;
            _config.SoundOnBlockedBySchedule = chkSound[4].Checked;
            _config.SoundGraceFile = txtSoundFile[0].Text.Trim();
            _config.SoundCountdownFile = txtSoundFile[1].Text.Trim();
            _config.SoundCooldownFile = txtSoundFile[2].Text.Trim();
            _config.SoundQuotaFile = txtSoundFile[3].Text.Trim();
            _config.SoundBlockedByScheduleFile = txtSoundFile[4].Text.Trim();

            SavePromptStyles();
            SaveScheduleRules();

            _config.StatusOverlayEnabled = chkOverlayEnabled.Checked;
            _config.StatusOverlayTopMost = chkOverlayTopMost.Checked;
            _config.StatusOverlaySnap = chkOverlaySnap.Checked;
            _config.StatusOverlayAutoHide = chkOverlayAutoHide.Checked;
            _config.StatusOverlayEdgeHide = chkOverlayEdgeHide.Checked;
            _config.StatusOverlayAutoHideSeconds = (int)numOverlayAutoHideSeconds.Value;
            _config.StatusOverlayEdgeHideSeconds = (int)numOverlayEdgeHideSeconds.Value;
            _config.StatusOverlayTitleText = txtOverlayTitle.Text.Trim();
            _config.StatusOverlayOpacityPercent = (int)numOverlayOpacityPercent.Value;
            _config.StatusOverlayCornerRadius = CornerRadiusFromIndex(cmbOverlayCorner.SelectedIndex);
            _config.StatusOverlayFontFamily = string.IsNullOrWhiteSpace(cmbOverlayFont.Text) ? "Microsoft YaHei" : cmbOverlayFont.Text.Trim();
            _config.StatusOverlayFontSize = (int)numOverlayFontSize.Value;
            _config.StatusOverlayTextColorR = _overlayTextColor.R;
            _config.StatusOverlayTextColorG = _overlayTextColor.G;
            _config.StatusOverlayTextColorB = _overlayTextColor.B;
            _config.StatusOverlayBackColorR = _overlayBackColor.R;
            _config.StatusOverlayBackColorG = _overlayBackColor.G;
            _config.StatusOverlayBackColorB = _overlayBackColor.B;

            if (_config.DotColors == null) _config.DotColors = new DotColorConfig();
            _config.DotColors.Idle = new ColorSetting(_dotColors[0].R, _dotColors[0].G, _dotColors[0].B);
            _config.DotColors.Running = new ColorSetting(_dotColors[1].R, _dotColors[1].G, _dotColors[1].B);
            _config.DotColors.Reminding = new ColorSetting(_dotColors[2].R, _dotColors[2].G, _dotColors[2].B);
            _config.DotColors.Cooldown = new ColorSetting(_dotColors[3].R, _dotColors[3].G, _dotColors[3].B);
            _config.DotColors.Quota = new ColorSetting(_dotColors[4].R, _dotColors[4].G, _dotColors[4].B);

            _config.EnableRemote = chkEnableRemote.Checked;
            _config.RemoteAllowLan = chkRemoteAllowLan.Checked;
            _config.RemotePort = (int)numRemotePort.Value;

            _config.StartWithWindows = chkStartWithWindows.Checked;

            // 用量以实时 _config.Usage 为准：设置页打开期间监控仍在计时，网页端也可能刚重置过。
            // 这里只按“当前监控列表”过滤（删除条目时连带丢弃其用量），不再用打开设置页时的旧快照覆盖实时累计值，
            // 否则保存一次无关设置就会把期间的计时/重置写回旧值（典型“改完又被还原”）。
            lock (_config.SyncRoot)
            {
                _config.Usage.RemoveAll(u =>
                    u == null || u.ProcessName == null ||
                    !_config.Games.Any(g =>
                        g != null && g.ProcessName != null &&
                        g.ProcessName.Equals(u.ProcessName, StringComparison.OrdinalIgnoreCase)));
            }
        }

        private void MarkGlobalDirty()
        {
            // 提示样式等非列表字段统一在“保存”时落地，不参与监控列表的“放弃修改”确认。
        }

        private void LoadPromptStyles(OverlayStyleConfig style)
        {
            style = style ?? new OverlayStyleConfig();
            PromptStyleConfig[] items =
            {
                style.Reminding ?? OverlayStyleConfig.CreateReminding(),
                style.CountingDown ?? OverlayStyleConfig.CreateCountdown(),
                style.Cooldown ?? OverlayStyleConfig.CreateCooldown(),
                style.QuotaExhausted ?? OverlayStyleConfig.CreateQuota(),
                style.BlockedBySchedule ?? OverlayStyleConfig.CreateBlockedBySchedule()
            };

            for (int i = 0; i < items.Length; i++)
            {
                var s = items[i];
                txtPromptTemplate[i].Text = s.Template ?? "";
                SelectFont(cmbPromptFont[i], s.FontFamily);
                numPromptSize[i].Value = Clamp(s.FontSize, 0, 200);
                chkPromptBold[i].Checked = s.Bold;
                chkPromptShadow[i].Checked = s.Shadow;
                numPromptAlpha[i].Value = Clamp(s.BackgroundAlphaPercent, 0, 100);
                numPromptTextAlpha[i].Value = Clamp(s.TextAlphaPercent, 0, 100);
                _promptColors[i] = Color.FromArgb(
                    Clamp(s.TextColorR, 0, 255), Clamp(s.TextColorG, 0, 255), Clamp(s.TextColorB, 0, 255));
                _promptBackColors[i] = Color.FromArgb(
                    Clamp(s.BackColorR, 0, 255), Clamp(s.BackColorG, 0, 255), Clamp(s.BackColorB, 0, 255));
                btnPromptColor[i].BackColor = _promptColors[i];
                btnPromptBackColor[i].BackColor = _promptBackColors[i];
                btnPromptColor[i].Text = "";
                btnPromptBackColor[i].Text = "";
            }
        }

        /// <summary>配置里的字体未安装时回退到微软雅黑，避免下拉框空白。</summary>
        private static void SelectFont(ComboBox combo, string fontFamily)
        {
            string target = string.IsNullOrWhiteSpace(fontFamily) ? "Microsoft YaHei" : fontFamily.Trim();
            combo.SelectedItem = target;
            if (combo.SelectedIndex < 0) combo.SelectedItem = "Microsoft YaHei";
            if (combo.SelectedIndex < 0 && combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void LoadDotColors()
        {
            var dots = _config.DotColors ?? new DotColorConfig();
            Color[] colors =
            {
                ToColor(dots.Idle, 154, 163, 175), ToColor(dots.Running, 34, 197, 94),
                ToColor(dots.Reminding, 245, 158, 11), ToColor(dots.Cooldown, 255, 193, 7),
                ToColor(dots.Quota, 239, 68, 68)
            };
            for (int i = 0; i < colors.Length && i < btnDotColors.Length; i++)
            {
                _dotColors[i] = colors[i];
                if (btnDotColors[i] != null) btnDotColors[i].BackColor = colors[i];
            }
        }

        private static Color ToColor(ColorSetting setting, int r, int g, int b)
        {
            if (setting == null) return Color.FromArgb(r, g, b);
            return Color.FromArgb(
                Clamp(setting.R, 0, 255), Clamp(setting.G, 0, 255), Clamp(setting.B, 0, 255));
        }

        private PromptStyleConfig CollectPromptStyle(int index)
        {
            Color color = _promptColors[index];
            Color back = _promptBackColors[index];
            string rawTemplate = txtPromptTemplate[index].Text;
            // 空模板一律落库为默认文案（不再保留空串），运行时也不会出现空提示
            string template = string.IsNullOrWhiteSpace(rawTemplate)
                ? PromptText.DefaultTemplate((PromptKind)index)
                : rawTemplate.Trim();
            return new PromptStyleConfig
            {
                Template = template,
                FontFamily = string.IsNullOrWhiteSpace(cmbPromptFont[index].Text)
                    ? "Microsoft YaHei" : cmbPromptFont[index].Text.Trim(),
                FontSize = (int)numPromptSize[index].Value,
                Bold = chkPromptBold[index].Checked,
                Shadow = chkPromptShadow[index].Checked,
                TextColorR = color.R,
                TextColorG = color.G,
                TextColorB = color.B,
                TextAlphaPercent = (int)numPromptTextAlpha[index].Value,
                BackColorR = back.R,
                BackColorG = back.G,
                BackColorB = back.B,
                BackgroundAlphaPercent = (int)numPromptAlpha[index].Value
            };
        }

        private void SavePromptStyles()
        {
            if (_config.OverlayStyle == null) _config.OverlayStyle = new OverlayStyleConfig();
            _config.OverlayStyle.Reminding = CollectPromptStyle(0);
            _config.OverlayStyle.CountingDown = CollectPromptStyle(1);
            _config.OverlayStyle.Cooldown = CollectPromptStyle(2);
            _config.OverlayStyle.QuotaExhausted = CollectPromptStyle(3);
            _config.OverlayStyle.BlockedBySchedule = CollectPromptStyle(4);
        }

        /// <summary>空文本按默认值处理；非空则必须能解析成整数。</summary>
        private static bool TryParseIntWithDefault(string text, int fallback, out int value)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                value = fallback;
                return true;
            }
            return int.TryParse(text.Trim(), out value);
        }

        private static double ParseMultiplierSafe(string text)
        {
            if (double.TryParse(text, out double value) && !double.IsNaN(value) && !double.IsInfinity(value))
                return value;
            return 1.0;
        }

        private static string FormatMultiplier(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.5 || value > 3.0) value = 1.0;
            return (Math.Round(value * 2, MidpointRounding.AwayFromZero) / 2).ToString("0.#");
        }

        private void UpdatePasswordUiState()
        {
            bool clear = chkClearPassword.Checked;
            txtNewPass.Enabled = !clear;
            txtConfirmPass.Enabled = !clear;
        }

        private void UpdateRemoteUiState()
        {
            bool hasPassword = !string.IsNullOrEmpty(_config.AdminPassword) || txtNewPass.Text.Trim().Length > 0;
            chkEnableRemote.Enabled = hasPassword;
            lblPasswordHint.ForeColor = hasPassword ? Color.DimGray : Color.Crimson;

            if (!hasPassword)
            {
                chkEnableRemote.Checked = false;
            }
            UpdateRemoteAddressLabel();
        }

        private void UpdateRemoteAddressLabel()
        {
            if (lblRemoteAddress == null || chkEnableRemote == null || chkRemoteAllowLan == null || numRemotePort == null)
                return;
            // 与托盘菜单共用同一份格式化逻辑，保证两处显示完全一致
            lblRemoteAddress.Text = FormatWebAddress(
                chkEnableRemote.Checked, chkRemoteAllowLan.Checked, (int)numRemotePort.Value);
            lblRemoteAddress.ForeColor = chkEnableRemote.Checked && chkRemoteAllowLan.Checked
                ? Color.Crimson
                : Color.DimGray;
        }

        /// <summary>下拉索引 → 圆角半径；索引异常时回退方角（0），保证不会画出意料之外的圆角。</summary>
        private static int CornerRadiusFromIndex(int index)
        {
            var options = AppConfig.CornerRadiusOptions;
            if (index < 0 || index >= options.Length) return options[0];
            return options[index];
        }

        /// <summary>圆角半径 → 下拉索引；配置里的非标准值取最接近的一档（老配置缺字段时为 0=方角）。</summary>
        private static int CornerIndexFromRadius(int radius)
        {
            var options = AppConfig.CornerRadiusOptions;
            int best = 0;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < options.Length; i++)
            {
                int diff = Math.Abs(options[i] - radius);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Web 控制页面的访问地址，供设置页与托盘菜单共用：
        /// 未启用 → “未开启”；仅本机 → 127.0.0.1:端口；允许局域网 → 本机局域网 IP:端口。
        /// </summary>
        internal static string FormatWebAddress(bool enabled, bool allowLan, int port)
        {
            if (!enabled) return "web控制页面：未开启";

            // 端口兜底：设置页控件范围是 1024~65535，但旧/脏配置里可能是 0 或越界值，
            // 直接拼接会得到 127.0.0.1:0 这种残缺地址，这里回退到默认端口 8080。
            int safePort = port >= 1 && port <= 65535 ? port : 8080;
            if (!allowLan) return $"web控制页面：127.0.0.1:{safePort}";

            string lanIp = GetLanIpAddress();
            if (lanIp == null) return $"web控制页面：127.0.0.1:{safePort}（未检测到局域网 IP）";
            return $"web控制页面：{lanIp}:{safePort}";
        }

        /// <summary>
        /// 取本机实际生效的局域网 IPv4。取舍：优先“配置了默认网关”的网卡（通常就是默认路由那张），
        /// 其次优先物理网卡（以太网/无线）而不是 VPN、虚拟网卡；仍无法区分时按枚举顺序取首个候选，
        /// 因为 Windows 托管 API 不直接暴露默认路由接口/跃点数，无法做到百分百判定。
        /// 跳过回环、隧道、未连接网卡与 169.254.x.x（APIPA 自动地址）。
        /// </summary>
        internal static string GetLanIpAddress()
        {
            try
            {
                var candidates = new List<(string Ip, bool HasGateway, bool Physical)>();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                        ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var props = ni.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var gw in props.GatewayAddresses)
                    {
                        if (gw?.Address != null && !gw.Address.Equals(IPAddress.Any))
                        {
                            hasGateway = true;
                            break;
                        }
                    }

                    bool physical = ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                                    ni.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                                    ni.NetworkInterfaceType == NetworkInterfaceType.FastEthernetT ||
                                    ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;

                    foreach (var ua in props.UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip == null || ip.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ip)) continue;
                        if (ip.ToString().StartsWith("169.254.", StringComparison.Ordinal)) continue;
                        candidates.Add((ip.ToString(), hasGateway, physical));
                    }
                }

                if (candidates.Count == 0) return null;

                // “有网关”优先于“物理网卡”；都不满足时保持枚举顺序，取首个可用候选
                var best = candidates
                    .OrderByDescending(c => c.HasGateway)
                    .ThenByDescending(c => c.Physical)
                    .First();
                return best.Ip;
            }
            catch (Exception ex)
            {
                AppLog.Warn("获取本机局域网 IP 失败，回退到 127.0.0.1：" + ex.Message);
                return null;
            }
        }

        #endregion

        #region 工具方法

        private void PickColor(Button button, Action<Color> apply)
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = button.BackColor })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                apply(dlg.Color);
                button.BackColor = dlg.Color;
            }
        }

        private static string CellText(DataGridViewRow row, int columnIndex)
        {
            var value = row.Cells[columnIndex].Value;
            return value == null ? "" : value.ToString().Trim();
        }

        private static MatchMode ModeFromLabel(string label)
        {
            if (string.Equals(label, MatchModeLabels[1], StringComparison.Ordinal)) return MatchMode.ExactProcess;
            if (string.Equals(label, MatchModeLabels[2], StringComparison.Ordinal)) return MatchMode.ExactWindowTitle;
            return MatchMode.Contains;
        }

        private static int ModeIndex(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.ExactProcess: return 1;
                case MatchMode.ExactWindowTitle: return 2;
                default: return 0;
            }
        }

        private MatchMode ModeFromCell(DataGridViewRow row) => ModeFromLabel(CellText(row, colMode.Index));

        private static int ParseIntSafe(string text, int fallback)
            => int.TryParse(text, out var value) ? value : fallback;

        private static int Clamp(int value, int min, int max)
            => Math.Max(min, Math.Min(max, value));

        private static int ModifierIndex(uint modifiers)
        {
            if (modifiers == (0x0002u | 0x0004u)) return 1;
            if (modifiers == (0x0001u | 0x0004u)) return 2;
            if (modifiers == (0x0001u | 0x0002u | 0x0004u)) return 3;
            return 0;
        }

        /// <summary>保存前校验快捷键下拉框，并换算成 Win32 修饰键/虚拟键值。</summary>
        private bool TryReadHotkeyFromUi(out uint mod, out uint vk, out string error)
        {
            mod = 0;
            vk = 0;
            error = null;

            if (cmbModifier == null || cmbModifier.SelectedIndex < 0)
            {
                error = "请选择快捷键的组合键（Ctrl+Alt / Ctrl+Shift / Alt+Shift / Ctrl+Alt+Shift）。";
                return false;
            }
            switch (cmbModifier.SelectedIndex)
            {
                case 0: mod = 0x0003; break;                       // Ctrl + Alt
                case 1: mod = 0x0002 | 0x0004; break;              // Ctrl + Shift
                case 2: mod = 0x0001 | 0x0004; break;              // Alt + Shift
                case 3: mod = 0x0001 | 0x0002 | 0x0004; break;     // Ctrl + Alt + Shift
                default: mod = 0x0003; break;
            }

            if (cmbKey == null || cmbKey.SelectedItem == null)
            {
                error = "请选择快捷键的按键。";
                return false;
            }
            int parsed = KeyToVk(cmbKey.SelectedItem.ToString());
            if (parsed <= 0)
            {
                error = "快捷键按键无效，请重新选择。";
                return false;
            }
            vk = (uint)parsed;
            return true;
        }

        private static int KeyToVk(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
                return 0;
            }
            if (name.StartsWith("F", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(name.Substring(1), out int n) && n >= 1 && n <= 12)
                    return 0x70 + n - 1; // VK_F1 = 0x70
            }
            return 0;
        }

        private static string VkToName(int vk)
        {
            if (vk >= 0x70 && vk <= 0x7B) return $"F{vk - 0x6F}";
            if (vk >= 'A' && vk <= 'Z') return ((char)vk).ToString();
            if (vk >= '0' && vk <= '9') return ((char)vk).ToString();
            return "G";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _statusTimer?.Dispose();
            base.Dispose(disposing);
        }

        #endregion
    }

    /// <summary>使用记录自绘柱状图：当日 0~23 时的小时用量（不引入第三方绘图库）。</summary>
    internal class UsageBarChart : Control
    {
        private int[] _hours = new int[24];
        private bool _hasHourly = true;

        public UsageBarChart()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>入参为该小时累计的“秒”，内部换算成分钟用于绘制。</summary>
        public void SetHourly(int[] secondsByHour, bool hasHourly)
        {
            _hours = new int[24];
            if (secondsByHour != null && secondsByHour.Length == 24)
            {
                for (int i = 0; i < 24; i++)
                    _hours[i] = (int)Math.Round(Math.Max(0, secondsByHour[i]) / 60.0, MidpointRounding.AwayFromZero);
            }
            _hasHourly = hasHourly;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            using (var borderPen = new Pen(Color.FromArgb(215, 215, 215)))
                g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            using (var smallFont = new Font(Font.FontFamily, 8f))
            {
                g.DrawString("单位：分钟", smallFont, Brushes.Gray, Width - 70, 6);
                if (!_hasHourly)
                {
                    g.DrawString("此前版本无小时数据，本日按 0 显示", smallFont, Brushes.Gray, 12, 6);
                }
                if (Height < 70 || Width < 120) return;

                int left = 10, right = 10, top = 22, bottom = 26;
                int plotW = Width - left - right;
                int plotH = Height - top - bottom;

                using (var gridPen = new Pen(Color.FromArgb(225, 225, 225)))
                    g.DrawLine(gridPen, left, Height - bottom, Width - right, Height - bottom);

                bool any = false;
                int max = 1;
                for (int i = 0; i < 24; i++)
                {
                    max = Math.Max(max, _hours[i]);
                    if (_hours[i] > 0) any = true;
                }

                float slot = plotW / 24f;
                float barW = Math.Max(4f, Math.Min(22f, slot * 0.62f));

                using (var barBrush = new SolidBrush(Color.FromArgb(79, 70, 229)))
                {
                    for (int i = 0; i < 24; i++)
                    {
                        int used = _hours[i];
                        int h = (int)Math.Round(used / (float)max * plotH);
                        if (used > 0 && h < 2) h = 2;

                        float x = left + slot * i + (slot - barW) / 2f;
                        g.FillRectangle(barBrush, x, Height - bottom - h, barW, h);

                        if (used > 0)
                        {
                            string valueText = used.ToString();
                            var valueSize = g.MeasureString(valueText, smallFont);
                            if (valueSize.Width <= slot)
                            {
                                g.DrawString(valueText, smallFont, Brushes.DimGray,
                                    x + barW / 2f - valueSize.Width / 2f,
                                    Height - bottom - h - valueSize.Height - 1);
                            }
                        }

                        // 0~23 时：窄时仅标偶数，保证可读
                        if (slot >= 18f || i % 2 == 0)
                        {
                            string hourText = i.ToString();
                            var hourSize = g.MeasureString(hourText, smallFont);
                            g.DrawString(hourText, smallFont, Brushes.Gray,
                                x + barW / 2f - hourSize.Width / 2f,
                                Height - bottom + 5);
                        }
                    }
                }

                if (!any)
                    g.DrawString("本日暂无计时数据", smallFont, Brushes.Gray, left + 6, top + 4);
            }
        }
    }
}
