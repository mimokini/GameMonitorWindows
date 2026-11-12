using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public class TrayAppContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private GameMonitor _monitor;
        private CountdownForm _countdown;
        private StatusOverlayForm _statusOverlay;
        private HotkeyManager _hotkey;
        private HotkeyHostForm _hotkeyHost;
        private SettingsForm _settingsForm;
        private NotifyIcon _tray;
        private Icon _trayIcon;
        private ContextMenuStrip _menu;
        private RemoteServer _remoteServer;
        private Timer _trayTimer;

        public TrayAppContext()
        {
            _config = AppConfig.Load();

            // 启动时按配置同步「防结束守护」计划任务：开启则创建，关闭则删除，
            // 顺带清掉配置已关闭但任务仍残留的孤儿任务
            StartupManager.SyncDaemonTask(_config.EnableDaemon);

            _countdown = new CountdownForm(_config);
            _monitor = new GameMonitor(_config, _countdown);
            _monitor.Log += msg => AppLog.Info(msg);
            _monitor.ReloadGames();
            _monitor.Start();

            _hotkey = new HotkeyManager();
            _hotkey.HotkeyPressed += OpenSettings;
            _hotkeyHost = new HotkeyHostForm { Hotkey = _hotkey };
            var _ = _hotkeyHost.Handle;
            bool hotkeyOk = _hotkey.Register(_hotkeyHost.Handle, _config.HotkeyModifiers, (uint)_config.HotkeyVk);

            if (_config.StatusOverlayEnabled)
            {
                _statusOverlay = new StatusOverlayForm(_config, _monitor);
                _statusOverlay.Show();
                _statusOverlay.ApplyMouseTransparent(_config.MouseTransparent);
            }

            BuildTray();

            _remoteServer = new RemoteServer(_config, _hotkeyHost,
                () => RunOnUiThread(() =>
                {
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                        _settingsForm.ReloadData();
                    ApplyConfigChanges();
                }),
                procName => _monitor.KillTarget(procName),
                procName => _monitor.ForceExhaustSession(procName),
                () => RunOnUiThread(() =>
                {
                    // 网页清除密码后必须立刻关闭远程服务，避免匿名访问
                    _config.EnableRemote = false;
                    _config.SaveQuiet();
                    _remoteServer.Stop();
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                        _settingsForm.ReloadData();
                    AppLog.Warn("管理密码已被清除，远程控制台已自动关闭。");
                    MessageBox.Show("管理密码已被清除，远程控制台已自动关闭。", "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }),
                () => _monitor.GetStatuses(),
                minutes =>
                {
                    _monitor.Pause(minutes);
                    RefreshStateSurfaces();
                    return true;
                },
                () =>
                {
                    _monitor.ResumePause();
                    RefreshStateSurfaces();
                    return true;
                },
                (procName, minutes) =>
                {
                    var result = _monitor.AdjustSessionBonus(procName, minutes);
                    RefreshStateSurfaces();
                    // 加时/减时不再弹全屏提示层：托盘触发时用气泡反馈（非阻断），内容同时写入日志
                    if (result.Success && !string.IsNullOrEmpty(result.Message))
                        RunOnUiThread(() => ShowTrayBalloon(minutes > 0 ? "本轮加时" : "本轮减时", result.Message));
                    return result;
                },
                () => _monitor.ClearAllUsage(),
                procName => _monitor.ResetEntrySession(procName));

            if (_config.EnableRemote)
            {
                if (string.IsNullOrEmpty(_config.AdminPassword))
                {
                    _config.EnableRemote = false;
                    _config.SaveQuiet();
                    AppLog.Warn("远程控制台未启动：未设置管理密码。");
                }
                else if (!_remoteServer.Start(_config.RemotePort, _config.RemoteAllowLan, out string remoteError))
                {
                    _config.EnableRemote = false;
                    _config.SaveQuiet();
                    MessageBox.Show(remoteError, "远程控制台启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            if (!hotkeyOk && _config.HideTrayIcon)
            {
                MessageBox.Show("热键注册失败但托盘已隐藏，请用任务管理器结束进程并重新配置。",
                    "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // 托盘 tooltip 每 5 秒刷新，菜单打开时也会重建
            _trayTimer = new Timer { Interval = 5000 };
            _trayTimer.Tick += (s, e) => UpdateTrayText();
            _trayTimer.Start();
            UpdateTrayText();

            if (_monitor.IsPaused(DateTime.Now))
                AppLog.Info($"启动时仍处于暂停期，剩余约 {(int)Math.Ceiling((_config.PauseUntil - DateTime.Now).TotalMinutes)} 分钟。");
        }

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => RebuildMenu();

            _trayIcon = AppIcon.Resolve();
            _tray = new NotifyIcon
            {
                Icon = _trayIcon,
                Text = "游戏监控",
                Visible = !_config.HideTrayIcon,
                ContextMenuStrip = _menu
            };
            _tray.DoubleClick += (s, e) => OpenSettings();
            RebuildMenu();
        }

        private void RebuildMenu()
        {
            _menu.Items.Clear();

            var statuses = _monitor.GetStatuses();
            bool paused = _monitor.IsPaused(DateTime.Now);
            int pauseMinutes = paused
                ? Math.Max(1, (int)Math.Ceiling((_config.PauseUntil - DateTime.Now).TotalMinutes))
                : 0;
            bool hasPassword = !string.IsNullOrEmpty(_config.AdminPassword);

            // 首项：当前状态摘要（只读）
            _menu.Items.Add(new ToolStripMenuItem(BuildSummary(statuses, paused, pauseMinutes))
            {
                Enabled = false
            });

            // Web 控制页面地址（只读，与设置页共用同一份格式化逻辑；随端口/开关/局域网配置变化）
            _menu.Items.Add(new ToolStripMenuItem(SettingsForm.FormatWebAddress(
                _config.EnableRemote, _config.RemoteAllowLan, _config.RemotePort))
            {
                Enabled = false
            });

            _menu.Items.Add(new ToolStripSeparator());

            // ===== 暂停 / 取消暂停（需要管理密码） =====
            var miPause = new ToolStripMenuItem();
            if (paused)
            {
                miPause.Text = $"取消暂停（剩余 {pauseMinutes} 分钟）";
                miPause.Enabled = hasPassword;
                miPause.ToolTipText = hasPassword ? "立即恢复识别与计时" : "需先在设置中设置管理密码";
                miPause.Click += (s, e) =>
                {
                    if (!VerifyPassword()) return;
                    _monitor.ResumePause();
                    RefreshStateSurfaces();
                };
            }
            else
            {
                // 暂停时长取自配置（设置页「暂停监控(分)」可改并随保存生效），不再写死 30
                int pauseDuration = Math.Max(1, _config.PauseMinutes);
                miPause.Text = $"暂停监控 {pauseDuration} 分钟";
                miPause.Enabled = hasPassword;
                miPause.ToolTipText = hasPassword
                    ? "暂停期间不识别、不提示、不计时，到期自动恢复"
                    : "需先在设置中设置管理密码";
                miPause.Click += (s, e) =>
                {
                    if (!VerifyPassword()) return;
                    _monitor.Pause(pauseDuration);
                    RefreshStateSurfaces();
                };
            }
            _menu.Items.Add(miPause);

            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("打开设置...", null, (s, e) => OpenSettings());
            _menu.Items.Add("关于", null, (s, e) => ShowAboutDialog());

            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("退出", null, (s, e) =>
            {
                if (VerifyPassword()) Shutdown();
            });
        }

        /// <summary>
        /// 关于对话框：版本 + GitHub 发布页链接 + 打赏二维码（复用已嵌入的 GameMonitor.reward.png），
        /// 二维码居中约 160px，下方简短感谢文案；窗口按内容固定高度，无滚动条。
        /// 链接行按当前 DPI 下的实际文本宽度撑开窗口，避免高 DPI 下折行或截断。
        /// 不展示日志目录（日志目录只在设置页「关于」里保留一条）。
        /// </summary>
        internal static void ShowAboutDialog()
        {
            string version = typeof(TrayAppContext).Assembly.GetName().Version?.ToString() ?? "未知";
            Image reward = SettingsForm.LoadRewardImage();
            bool hasReward = reward != null;

            string linkText = "GitHub 发布页：" + SettingsForm.GitHubReleasesUrl;
            int linkWidth;
            using (var measureFont = new Font("Microsoft YaHei", 9f))
            {
                linkWidth = TextRenderer.MeasureText(linkText, measureFont).Width;
            }

            int width = Math.Max(440, linkWidth + 48);
            int height = hasReward ? 384 : 168;

            using (var dlg = new Form
            {
                Text = "关于",
                ClientSize = new Size(width, height),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                ShowIcon = true,
                Icon = AppIcon.Resolve(),
                TopMost = true,
                Font = new Font("Microsoft YaHei", 9f),
                AutoScaleMode = AutoScaleMode.Font
            })
            {
                dlg.Controls.Add(new Label
                {
                    Text = "游戏监控",
                    Left = 20, Top = 16, Width = width - 40, Height = 32,
                    Font = new Font("Microsoft YaHei", 14f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft
                });
                dlg.Controls.Add(new Label
                {
                    Text = $"版本：{version}",
                    Left = 20, Top = 52, Width = width - 40, Height = 22,
                    TextAlign = ContentAlignment.MiddleLeft
                });
                dlg.Controls.Add(SettingsForm.CreateGitHubLink(20, 80, width - 40));

                if (hasReward)
                {
                    const int side = 160;
                    var pic = new PictureBox
                    {
                        Left = (width - side) / 2, Top = 116, Width = side, Height = side,
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = reward,
                        BackColor = Color.Transparent
                    };
                    var caption = new Label
                    {
                        Text = "感谢打赏，你的支持是持续更新的动力！",
                        Left = 20, Top = 282, Width = width - 40, Height = 24,
                        TextAlign = ContentAlignment.MiddleCenter
                    };
                    dlg.Controls.Add(pic);
                    dlg.Controls.Add(caption);
                }

                var ok = new Button
                {
                    Text = "确定", Width = 108, Height = 32,
                    Left = (width - 108) / 2, Top = hasReward ? 328 : 112,
                    DialogResult = DialogResult.OK
                };
                dlg.Controls.Add(ok);
                dlg.AcceptButton = ok;
                dlg.CancelButton = ok;
                dlg.ShowDialog();
            }
            reward?.Dispose();
        }

        /// <summary>菜单顶部 / 托盘 tooltip 的状态摘要。</summary>
        private string BuildSummary(List<GameStatus> statuses, bool paused, int pauseMinutes)
        {
            if (paused)
                return $"暂停中：剩余 {pauseMinutes} 分钟（不识别、不提示、不计时）";

            if (statuses.Any(s => s.Phase == MonitorPhase.BlockedBySchedule || s.InBlockedPeriod))
                return "禁止时段：匹配程序已关闭";

            var allow = statuses.FirstOrDefault(s => s.InAllowRulePeriod && s.IsRunning);
            if (allow != null)
                return $"开放时段：{allow.Game.DisplayName} 照常计时（不弹窗、不强杀）";

            var cooldown = statuses.FirstOrDefault(s => s.Phase == MonitorPhase.Cooldown);
            if (cooldown != null)
            {
                int minutes = Math.Max(1, (int)Math.Ceiling(cooldown.CooldownRemainingSeconds / 60.0));
                return $"休息期：还需 {minutes} 分钟";
            }

            if (statuses.Any(s => s.QuotaExhausted || s.Phase == MonitorPhase.QuotaExhausted))
                return "今日已用完";

            var running = statuses.FirstOrDefault(s => s.IsRunning || s.Phase == MonitorPhase.Reminding || s.Phase == MonitorPhase.CountingDown);
            if (running != null)
            {
                string remain;
                if (running.EffectiveLimitMinutes <= 0)
                {
                    remain = "今日不限";
                }
                else
                {
                    int usedMinutes = running.UsedSecondsToday / 60;
                    int remaining = Math.Max(0, running.EffectiveLimitMinutes - usedMinutes);
                    remain = $"今日剩余 {remaining} 分钟";
                }
                return $"运行中：{running.Game.DisplayName} ｜ {remain}";
            }

            return "空闲";
        }

        private void UpdateTrayText()
        {
            if (_tray == null || !_tray.Visible) return;

            var statuses = _monitor.GetStatuses();
            bool paused = _monitor.IsPaused(DateTime.Now);
            int pauseMinutes = paused
                ? Math.Max(1, (int)Math.Ceiling((_config.PauseUntil - DateTime.Now).TotalMinutes))
                : 0;
            string text = "游戏监控 ｜ " + BuildSummary(statuses, paused, pauseMinutes);
            if (text.Length > 63) text = text.Substring(0, 63); // NotifyIcon.Text 长度上限
            _tray.Text = text;
        }

        /// <summary>暂停/加时/恢复后刷新托盘与各展示面。</summary>
        private void RefreshStateSurfaces()
        {
            UpdateTrayText();
            if (_settingsForm != null && !_settingsForm.IsDisposed)
                _settingsForm.ReloadData();
            if (_statusOverlay != null && !_statusOverlay.IsDisposed && _statusOverlay.Visible)
                _statusOverlay.RefreshContent();
        }

        private bool VerifyPassword()
        {
            if (string.IsNullOrEmpty(_config.AdminPassword)) return true;
            using (var dlg = new PasswordPromptForm("身份验证"))
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    if (dlg.Password == _config.AdminPassword) return true;
                    MessageBox.Show("密码错误！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return false;
        }

        private void OpenSettings()
        {
            if (!VerifyPassword()) return;

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.Show();
                if (_settingsForm.WindowState == FormWindowState.Minimized)
                    _settingsForm.WindowState = FormWindowState.Normal;
                _settingsForm.Activate();
                return;
            }

            _settingsForm = new SettingsForm(_config, _monitor);
            _settingsForm.FormClosed += (s, e) => { _settingsForm = null; };
            _settingsForm.ConfigSaved += ApplyConfigChanges;
            // 短关键字等提醒只走托盘气泡：非阻断，不弹确认框，也不占用屏幕中央的提示层
            _settingsForm.NoticeRequested += text => ShowTrayBalloon("关键字提示", text);
            _settingsForm.OverlayResetRequested += () =>
            {
                // 点“重置位置”即时生效：确保悬浮窗已创建，再复用 ApplyConfigChanges 的复位逻辑并持久化
                if (_statusOverlay == null || _statusOverlay.IsDisposed)
                    _statusOverlay = new StatusOverlayForm(_config, _monitor);
                if (_config.StatusOverlayEnabled && !_statusOverlay.Visible)
                    _statusOverlay.Show();
                _statusOverlay.ApplyConfigChanges();
            };
            _settingsForm.HotkeyApplyRequested += (mod, vk) =>
                _hotkey.Register(_hotkeyHost.Handle, mod, vk);
            _settingsForm.Show();
        }

        /// <summary>托盘气泡：用于加时/减时等非阻断反馈（不占用屏幕中央的提示层）。</summary>
        private void ShowTrayBalloon(string title, string text)
        {
            if (_tray == null || string.IsNullOrEmpty(text)) return;
            try
            {
                if (text.Length > 250) text = text.Substring(0, 250);
                _tray.ShowBalloonTip(3000, title ?? "游戏监控", text, ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                AppLog.Warn("显示托盘气泡失败（不影响加时/减时结果）。", ex);
            }
        }

        /// <summary>后台线程回调统一封送到 UI 线程；已在 UI 线程则直接执行。</summary>
        private void RunOnUiThread(Action action)
        {
            if (action == null) return;
            if (_hotkeyHost != null && _hotkeyHost.IsHandleCreated && _hotkeyHost.InvokeRequired)
                _hotkeyHost.BeginInvoke(action);
            else
                action();
        }

        private void ApplyConfigChanges()
        {
            _config.SaveQuiet();

            StartupManager.SetEnabled(_config.StartWithWindows);
            // 设置页关闭「防结束守护」后立即删除计划任务（失败会写日志并给出管理员命令）
            StartupManager.SyncDaemonTask(_config.EnableDaemon);
            _tray.Visible = !_config.HideTrayIcon;

            // 图标来自 exe 内嵌资源（编译期 ApplicationIcon），运行时不依赖同目录 app.ico
            var newIcon = AppIcon.Resolve();
            if (_trayIcon == null || newIcon.Width != _trayIcon.Width ||
                newIcon.Handle != _trayIcon.Handle)
            {
                _trayIcon = newIcon;
                _tray.Icon = _trayIcon;
            }

            if (_config.EnableRemote)
            {
                if (string.IsNullOrEmpty(_config.AdminPassword))
                {
                    _config.EnableRemote = false;
                    _config.SaveQuiet();
                    _remoteServer.Stop();
                    MessageBox.Show("请先设置管理密码，远程控制台已保持关闭。", "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else if (!_remoteServer.Start(_config.RemotePort, _config.RemoteAllowLan, out string remoteError))
                {
                    // 启动失败不能显示“已启用”，回写配置并提示原因
                    _config.EnableRemote = false;
                    _config.SaveQuiet();
                    MessageBox.Show(remoteError, "远程控制台启动失败",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    if (_settingsForm != null && !_settingsForm.IsDisposed)
                        _settingsForm.ReloadData();
                }
            }
            else
            {
                _remoteServer.Stop();
            }

            _monitor.ReloadGames();
            _monitor.CancelCountdownIfUsageReset();

            _countdown.ApplyMouseTransparent(_config.MouseTransparent);
            _countdown.ApplyStyleChanges();

            bool overlayWasVisible = _statusOverlay != null && !_statusOverlay.IsDisposed && _statusOverlay.Visible;
            if (_config.StatusOverlayEnabled)
            {
                if (!overlayWasVisible)
                {
                    if (_statusOverlay == null || _statusOverlay.IsDisposed)
                        _statusOverlay = new StatusOverlayForm(_config, _monitor);
                    _statusOverlay.Show();
                    _statusOverlay.RefreshContent();
                }
                _statusOverlay.ApplyMouseTransparent(_config.MouseTransparent);
                _statusOverlay.ApplyConfigChanges();
            }
            else if (overlayWasVisible)
            {
                _statusOverlay.Hide();
            }

            UpdateTrayText();
        }

        private void Shutdown()
        {
            _trayTimer?.Stop();
            _trayTimer?.Dispose();

            _remoteServer?.Stop();
            _monitor?.Dispose();
            _hotkey?.Dispose();

            if (_statusOverlay != null && !_statusOverlay.IsDisposed)
                _statusOverlay.Dispose();
            _countdown?.Dispose();

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_hotkeyHost != null && !_hotkeyHost.IsDisposed)
                _hotkeyHost.Dispose();

            ExitThread();
        }

        public AppConfig Config => _config;
    }
}
