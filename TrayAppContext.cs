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
        private ContextMenuStrip _menu;
        private RemoteServer _remoteServer;

        public TrayAppContext()
        {
            _config = AppConfig.Load();

            // 应用防结束守护状态
            StartupManager.SetDaemon(_config.EnableDaemon);

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
                procName => _monitor.ForceCountdown(procName),
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
                }));

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
        }

        private void BuildTray()
        {
            _menu = new ContextMenuStrip();
            _menu.Opening += (s, e) => RebuildMenu();

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Shield,
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
            if (statuses.Count == 0)
            {
                _menu.Items.Add(new ToolStripMenuItem("（暂无监控游戏）") { Enabled = false });
            }
            else
            {
                foreach (var s in statuses)
                {
                    string quota = s.Game.DailyLimitMinutes > 0 ? s.Game.DailyLimitMinutes.ToString() : "不限";
                    string state = s.IsRunning ? "● 运行中" : "○ 未运行";
                    string text = $"{s.Game.DisplayName}: {s.UsedSecondsToday / 60}/{quota} 分  {state}";
                    _menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });
                }
            }

            _menu.Items.Add(new ToolStripSeparator());

            var miOverlay = new ToolStripMenuItem("显示桌面时长悬浮框")
            {
                Checked = _statusOverlay != null && !_statusOverlay.IsDisposed && _statusOverlay.Visible
            };
            miOverlay.Click += (s, e) => ToggleOverlay();
            _menu.Items.Add(miOverlay);

            var miTransparent = new ToolStripMenuItem("鼠标穿透")
            {
                Checked = _config.MouseTransparent,
                ToolTipText = "开启后倒计时大字和悬浮框都不接收鼠标操作"
            };
            miTransparent.Click += (s, e) => ToggleMouseTransparent();
            _menu.Items.Add(miTransparent);

            var miStartup = new ToolStripMenuItem("开机启动")
            {
                Checked = StartupManager.IsEnabled()
            };
            miStartup.Click += (s, e) => ToggleStartup();
            _menu.Items.Add(miStartup);

            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("设置...", null, (s, e) => OpenSettings());
            _menu.Items.Add(new ToolStripSeparator());

            _menu.Items.Add("测试倒计时", null, (s, e) =>
                _countdown.StartCountdown("测试游戏", _config.CountdownSeconds));
            _menu.Items.Add("测试禁用提示", null, (s, e) =>
                _countdown.ShowBlocked("测试游戏"));
            _menu.Items.Add("关闭所有目标游戏", null, (s, e) => _monitor.KillAllTargets());

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("退出", null, (s, e) =>
            {
                if (VerifyPassword()) Shutdown();
            });
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

        private void ToggleOverlay()
        {
            if (_statusOverlay != null && !_statusOverlay.IsDisposed && _statusOverlay.Visible)
            {
                _statusOverlay.Hide();
                _config.StatusOverlayEnabled = false;
            }
            else
            {
                if (_statusOverlay == null || _statusOverlay.IsDisposed)
                    _statusOverlay = new StatusOverlayForm(_config, _monitor);
                _statusOverlay.Show();
                _statusOverlay.ApplyMouseTransparent(_config.MouseTransparent);
                _statusOverlay.RefreshContent();
                _config.StatusOverlayEnabled = true;
            }
            _config.SaveQuiet();
        }

        private void ToggleMouseTransparent()
        {
            _config.MouseTransparent = !_config.MouseTransparent;
            _config.SaveQuiet();

            _countdown.ApplyMouseTransparent(_config.MouseTransparent);
            if (_statusOverlay != null && !_statusOverlay.IsDisposed)
                _statusOverlay.ApplyMouseTransparent(_config.MouseTransparent);
        }

        private void ToggleStartup()
        {
            bool newState = !StartupManager.IsEnabled();
            StartupManager.SetEnabled(newState);
            _config.StartWithWindows = newState;
            _config.SaveQuiet();
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
            _settingsForm.HotkeyApplyRequested += (mod, vk) =>
                _hotkey.Register(_hotkeyHost.Handle, mod, vk);
            _settingsForm.Show();
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
            _tray.Visible = !_config.HideTrayIcon;

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
        }

        private void Shutdown()
        {
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