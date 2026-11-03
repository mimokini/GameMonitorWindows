using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public enum MonitorPhase { Idle, Running, Reminding, CountingDown }

    /// <summary>进程匹配方式，默认“包含”以兼容旧配置。</summary>
    public enum MatchMode { Contains, ExactProcess, ExactWindowTitle }

    public class GameStatus
    {
        public GameItem Game;
        public int UsedSecondsToday;
        public bool IsRunning;
        public MonitorPhase Phase;
    }

    public class GameRuntimeState
    {
        public GameItem Game;
        public MonitorPhase Phase = MonitorPhase.Idle;
        public int SessionSeconds;
        public DateTime? RemindAt;
        public DateTime? CountdownEnd;
        public List<Process> TargetProcesses = new List<Process>();
        public DateTime LastBlockedNotify = DateTime.MinValue;
        public DateTime? LastMatchAt;
        public bool IsRunning;
    }

    public class GameMonitor : IDisposable
    {
        /// <summary>关键字最小长度，避免 "a" 这类子串误杀大量进程。</summary>
        public const int MinKeywordLength = 3;

        /// <summary>进程短暂消失的续计宽限，防止“重启游戏”把单次时长清零。</summary>
        public const int RestartGraceSeconds = 30;

        /// <summary>单次 tick 最多计入的秒数，避免休眠/卡顿后一次性把睡眠时间算成游戏时间。</summary>
        private const int MaxTickSeconds = 15;

        /// <summary>内置黑名单：自身与系统关键进程，永不参与匹配/强杀。</summary>
        private static readonly HashSet<string> ProtectedProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "GameMonitor",
                "explorer", "winlogon", "csrss", "smss", "lsass", "services", "svchost",
                "wininit", "dwm", "System", "Idle", "Registry", "taskhostw", "sihost",
                "fontdrvhost", "runtimebroker", "searchhost", "StartMenuExperienceHost",
                "shellexperiencehost", "SecurityHealthService", "MsMpEng"
            };

        private readonly Timer _timer;
        private readonly Timer _saveTimer;
        private readonly AppConfig _config;
        private readonly CountdownForm _overlay;
        private Dictionary<string, GameRuntimeState> _states =
            new Dictionary<string, GameRuntimeState>(StringComparer.OrdinalIgnoreCase);

        private DateTime _lastTickAt = DateTime.Now;

        // 倒计时展示队列：GameMonitor 单点管理，避免 _countdownActive 卡死
        private readonly Queue<string> _countdownQueue = new Queue<string>();
        private bool _countdownShowing;
        private string _showingProcess;

        public event Action<string> Log;
        public event Action<GameItem> ReminderRequested;

        public GameMonitor(AppConfig config, CountdownForm overlay)
        {
            _config = config;
            _overlay = overlay;
            _timer = new Timer { Interval = 1000 };
            _timer.Tick += Tick;
            _saveTimer = new Timer { Interval = 10000 };
            _saveTimer.Tick += (s, e) => _config.SaveQuiet();
            _overlay.Finished += OnOverlayFinished;
        }

        public void Start()
        {
            _lastTickAt = DateTime.Now;
            _timer.Start();
            _saveTimer.Start();
        }

        public void Stop() { _timer.Stop(); _saveTimer.Stop(); }

        public void ReloadGames()
        {
            var newStates = new Dictionary<string, GameRuntimeState>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in _config.Games.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ProcessName)))
            {
                if (g.ProcessName.Trim().Length < MinKeywordLength)
                {
                    AddLog($"「{g.DisplayName}」的关键字过短（至少 {MinKeywordLength} 个字符），已跳过监控。");
                    continue;
                }

                var key = g.ProcessName.ToLowerInvariant();
                if (_states.TryGetValue(key, out var existing))
                {
                    existing.Game = g;
                    newStates[key] = existing;
                }
                else
                {
                    newStates[key] = new GameRuntimeState { Game = g };
                }
            }

            foreach (var kv in _states)
            {
                if (!newStates.ContainsKey(kv.Key))
                    DisposeTargets(kv.Value);
            }

            _states = newStates;

            // 队列里只保留仍然存在的倒计时状态，其余重新入队，避免刷新后倒计时画面消失
            _countdownQueue.Clear();
            if (_countdownShowing && _showingProcess != null &&
                !newStates.Values.Any(s => s.Game.ProcessName.Equals(_showingProcess, StringComparison.OrdinalIgnoreCase)))
            {
                _overlay.StopAndHide();
                _countdownShowing = false;
                _showingProcess = null;
            }

            foreach (var kv in newStates)
            {
                if (kv.Value.Phase == MonitorPhase.CountingDown &&
                    !(_countdownShowing && _showingProcess != null &&
                      _showingProcess.Equals(kv.Value.Game.ProcessName, StringComparison.OrdinalIgnoreCase)))
                {
                    _countdownQueue.Enqueue(kv.Key);
                }
            }

            SyncOverlayState();
        }

        /// <summary>根据当前状态刷新/收起倒计时展示；Finished 统一在本类处理。</summary>
        private void SyncOverlayState()
        {
            bool anyCounting = _states.Values.Any(s => s.Phase == MonitorPhase.CountingDown);

            if (!anyCounting)
            {
                if (_countdownShowing)
                {
                    _overlay.StopAndHide();
                    _countdownShowing = false;
                    _showingProcess = null;
                }
                _countdownQueue.Clear();
                return;
            }

            if (_countdownShowing && _showingProcess != null)
            {
                var shown = _states.Values.FirstOrDefault(s =>
                    s.Game.ProcessName.Equals(_showingProcess, StringComparison.OrdinalIgnoreCase));
                if (shown == null || shown.Phase != MonitorPhase.CountingDown)
                {
                    _overlay.StopAndHide();
                    _countdownShowing = false;
                    _showingProcess = null;
                }
            }

            PumpCountdown();
        }

        private void OnOverlayFinished()
        {
            _countdownShowing = false;
            _showingProcess = null;
            PumpCountdown();
        }

        private void EnqueueCountdown(string processName)
        {
            if (_countdownShowing && _showingProcess != null &&
                _showingProcess.Equals(processName, StringComparison.OrdinalIgnoreCase))
                return;
            if (_countdownQueue.Any(x => x.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                return;

            _countdownQueue.Enqueue(processName);
            PumpCountdown();
        }

        private void PumpCountdown()
        {
            while (!_countdownShowing)
            {
                if (_countdownQueue.Count == 0) return;

                string name = _countdownQueue.Dequeue();
                var st = _states.Values.FirstOrDefault(s =>
                    s.Game.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (st == null || st.Phase != MonitorPhase.CountingDown || !st.CountdownEnd.HasValue)
                    continue;

                int remaining = (int)Math.Ceiling((st.CountdownEnd.Value - DateTime.Now).TotalSeconds);
                if (remaining <= 0) continue;

                _countdownShowing = true;
                _showingProcess = st.Game.ProcessName;
                _overlay.StartCountdown(st.Game.DisplayName, remaining);
            }
        }

        private GameUsage GetOrCreateUsage(string processName)
        {
            var u = _config.Usage.FirstOrDefault(x =>
                x.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (u == null)
            {
                u = new GameUsage { ProcessName = processName, Date = DateTime.Today, UsedSeconds = 0 };
                _config.Usage.Add(u);
            }
            return u;
        }

        private void Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            DateTime today = now.Date;

            // 计时改为按真实时间差累加，避免 Timer 丢 tick / UI 卡顿少计时。
            // 只推进“整秒”部分，未满 1 秒的余量保留在 _lastTickAt 中，避免长期漂移。
            int elapsed;
            double deltaSeconds = (now - _lastTickAt).TotalSeconds;
            if (deltaSeconds > MaxTickSeconds)
            {
                // 系统休眠/长时间卡顿：这段空档不计入游戏时长
                elapsed = 0;
                _lastTickAt = now;
            }
            else
            {
                elapsed = (int)deltaSeconds;
                if (elapsed < 0) elapsed = 0;
                _lastTickAt = _lastTickAt.AddSeconds(elapsed);
            }

            lock (_config.SyncRoot)
            {
                // 释放上一轮快照，避免 Process 句柄持续累积
                foreach (var st in _states.Values)
                    DisposeTargets(st);

                var allProcesses = Process.GetProcesses();
                var kept = new HashSet<Process>();
                try
                {
                    foreach (var kv in _states)
                    {
                        var st = kv.Value;
                        var game = st.Game;
                        var usage = GetOrCreateUsage(game.ProcessName);

                        if (usage.Date.Date != today)
                        {
                            usage.Date = today;
                            usage.UsedSeconds = 0;
                            st.LastBlockedNotify = DateTime.MinValue;
                            if (st.Phase != MonitorPhase.Idle)
                            {
                                AddLog($"{game.DisplayName} 跨天重置：今日用量清零。");
                                st.Phase = MonitorPhase.Idle;
                                st.SessionSeconds = 0;
                                st.CountdownEnd = null;
                                st.RemindAt = null;
                                RemoveQueuedCountdown(kv.Key);
                                SyncOverlayState();
                            }
                        }

                        var matchedProcs = FindMatches(game, allProcesses);
                        foreach (var p in matchedProcs) kept.Add(p);

                        if (matchedProcs.Count == 0)
                        {
                            HandleMissingProcess(st, now);
                            continue;
                        }

                        st.TargetProcesses = matchedProcs;
                        st.LastMatchAt = now;
                        st.IsRunning = true;

                        int limitSeconds = game.DailyLimitMinutes > 0 ? game.DailyLimitMinutes * 60 : int.MaxValue;
                        bool quotaExceeded = usage.UsedSeconds >= limitSeconds;

                        switch (st.Phase)
                        {
                            case MonitorPhase.Idle:
                                if (quotaExceeded)
                                {
                                    KillSnapshot(st);
                                    if ((now - st.LastBlockedNotify).TotalSeconds > 15)
                                    {
                                        st.LastBlockedNotify = now;
                                        AddLog($"{game.DisplayName} 今日配额已用完，自动关闭并提示。");
                                        _overlay.ShowBlocked(game.DisplayName);
                                        System.Media.SystemSounds.Exclamation.Play();
                                    }
                                }
                                else
                                {
                                    st.Phase = MonitorPhase.Running;
                                    st.SessionSeconds = 0;
                                    int usedMin = usage.UsedSeconds / 60;
                                    string quotaText = game.DailyLimitMinutes > 0 ? game.DailyLimitMinutes.ToString() : "不限";
                                    AddLog($"{game.DisplayName} 已启动，单次 {game.RunMinutes} 分，今日已用 {usedMin}/{quotaText} 分。");
                                }
                                break;

                            case MonitorPhase.Running:
                                st.SessionSeconds += elapsed;
                                usage.UsedSeconds += elapsed;
                                bool sessionUp = st.SessionSeconds >= game.RunMinutes * 60;
                                bool quotaUp = game.DailyLimitMinutes > 0 && usage.UsedSeconds >= limitSeconds;
                                if (sessionUp || quotaUp)
                                {
                                    st.Phase = MonitorPhase.Reminding;
                                    st.RemindAt = now;
                                    string reason = quotaUp
                                        ? $"「{game.DisplayName}」今日累计 {game.DailyLimitMinutes} 分钟已用完"
                                        : $"「{game.DisplayName}」本次已运行 {game.RunMinutes} 分钟";
                                    AddLog(reason + $"，提示退出，宽限 {_config.ReminderMinutes} 分钟。");
                                    ReminderRequested?.Invoke(game);
                                    _overlay.ShowReminder(game.DisplayName, _config.ReminderMinutes, reason);
                                }
                                break;

                            case MonitorPhase.Reminding:
                                usage.UsedSeconds += elapsed;
                                if (st.RemindAt.HasValue &&
                                    (now - st.RemindAt.Value).TotalSeconds >= _config.ReminderMinutes * 60)
                                {
                                    st.Phase = MonitorPhase.CountingDown;
                                    st.CountdownEnd = now.AddSeconds(_config.CountdownSeconds);
                                    AddLog($"{game.DisplayName} 宽限期结束，开始 {_config.CountdownSeconds} 秒倒计时。");
                                    EnqueueCountdown(kv.Key);
                                }
                                break;

                            case MonitorPhase.CountingDown:
                                usage.UsedSeconds += elapsed;
                                if (st.CountdownEnd.HasValue && now >= st.CountdownEnd.Value)
                                {
                                    KillSnapshot(st);
                                    DisposeTargets(st);
                                    AddLog($"{game.DisplayName} 倒计时结束，已强制关闭所有匹配进程。");
                                    st.Phase = MonitorPhase.Idle;
                                    st.SessionSeconds = 0;
                                    st.CountdownEnd = null;
                                    st.RemindAt = null;
                                    RemoveQueuedCountdown(kv.Key);
                                    if (_showingProcess != null &&
                                        _showingProcess.Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _overlay.StopAndHide();
                                        _countdownShowing = false;
                                        _showingProcess = null;
                                    }
                                    SyncOverlayState();
                                }
                                break;
                        }
                    }
                }
                finally
                {
                    foreach (var p in allProcesses)
                    {
                        if (!kept.Contains(p))
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 进程短暂消失时不立刻清零阶段/会话秒数；宽限期内重现则续计。
        /// </summary>
        private void HandleMissingProcess(GameRuntimeState st, DateTime now)
        {
            bool withinGrace = st.LastMatchAt.HasValue
                && st.Phase != MonitorPhase.Idle
                && (now - st.LastMatchAt.Value).TotalSeconds <= RestartGraceSeconds;

            st.IsRunning = false;

            if (withinGrace)
                return;

            if (st.Phase != MonitorPhase.Idle)
                AddLog($"{st.Game.DisplayName} 已退出。");

            st.Phase = MonitorPhase.Idle;
            st.SessionSeconds = 0;
            st.LastMatchAt = null;
            st.RemindAt = null;
            st.CountdownEnd = null;
            st.IsRunning = false;
            RemoveQueuedCountdown(st.Game.ProcessName);
            SyncOverlayState();
        }

        private List<Process> FindMatches(GameItem game, Process[] allProcesses)
        {
            string keyword = game.ProcessName.Trim();
            var mode = game.MatchMode;

            return allProcesses.Where(p =>
            {
                try
                {
                    string procName = p.ProcessName ?? "";
                    if (ProtectedProcessNames.Contains(procName)) return false;

                    if (mode == MatchMode.ExactProcess)
                        return procName.Equals(keyword, StringComparison.OrdinalIgnoreCase);

                    string title = p.MainWindowTitle ?? "";

                    if (mode == MatchMode.ExactWindowTitle)
                        return title.Length > 0 && title.Equals(keyword, StringComparison.OrdinalIgnoreCase);

                    // Contains：保持原有行为（进程名或窗口标题包含关键字）
                    if (procName.ToLowerInvariant().Contains(keyword.ToLowerInvariant())) return true;
                    return title.Length > 0 && title.ToLowerInvariant().Contains(keyword.ToLowerInvariant());
                }
                catch
                {
                    return false;
                }
            }).ToList();
        }

        private void RemoveQueuedCountdown(string processName)
        {
            if (_countdownQueue.Count == 0) return;

            var keep = new List<string>();
            while (_countdownQueue.Count > 0)
            {
                var name = _countdownQueue.Dequeue();
                if (!name.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    keep.Add(name);
            }
            foreach (var name in keep) _countdownQueue.Enqueue(name);
        }

        private static void DisposeTargets(GameRuntimeState st)
        {
            foreach (var p in st.TargetProcesses)
            {
                try { p.Dispose(); } catch { }
            }
            st.TargetProcesses.Clear();
        }

        /// <summary>只杀本轮匹配快照中的进程，禁止按模糊子串重新批量结束。</summary>
        private bool KillSnapshot(GameRuntimeState st)
        {
            bool killed = false;
            foreach (var p in st.TargetProcesses)
            {
                try
                {
                    if (!p.HasExited)
                    {
                        p.Kill();
                        killed = true;
                    }
                }
                catch { }
            }
            return killed;
        }

        /// <summary>
        /// 检测今日用量是否被重置为0，如果是则立刻取消提醒/倒计时状态。
        /// </summary>
        public void CancelCountdownIfUsageReset()
        {
            DateTime today = DateTime.Today;
            bool changed = false;

            lock (_config.SyncRoot)
            {
                foreach (var kv in _states)
                {
                    var st = kv.Value;
                    if (st.Phase == MonitorPhase.CountingDown || st.Phase == MonitorPhase.Reminding)
                    {
                        var usage = _config.Usage.FirstOrDefault(x =>
                            x.ProcessName.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase));
                        int used = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds : 0;
                        // 只要用量归零，就立刻重置状态
                        if (used == 0)
                        {
                            st.Phase = st.IsRunning ? MonitorPhase.Running : MonitorPhase.Idle;
                            st.SessionSeconds = 0;
                            st.CountdownEnd = null;
                            st.RemindAt = null;
                            AddLog($"{st.Game.DisplayName} 今日用时已重置，自动取消倒计时。");
                            changed = true;
                        }
                    }
                }
            }

            if (changed) SyncOverlayState();
        }

        public void ForceCountdown(string processName)
        {
            var st = _states.Values.FirstOrDefault(s =>
                s.Game.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

            if (st != null && st.IsRunning && st.Phase != MonitorPhase.CountingDown)
            {
                st.Phase = MonitorPhase.CountingDown;
                st.CountdownEnd = DateTime.Now.AddSeconds(_config.CountdownSeconds);
                AddLog($"{st.Game.DisplayName} 被手动强制进入倒计时。");
                EnqueueCountdown(st.Game.ProcessName);
            }
        }

        public List<GameStatus> GetStatuses()
        {
            var result = new List<GameStatus>();
            DateTime today = DateTime.Today;

            lock (_config.SyncRoot)
            {
                foreach (var kv in _states)
                {
                    var st = kv.Value;
                    var usage = _config.Usage.FirstOrDefault(x =>
                        x.ProcessName.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int used = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds : 0;
                    result.Add(new GameStatus
                    {
                        Game = st.Game,
                        UsedSecondsToday = used,
                        IsRunning = st.IsRunning,
                        Phase = st.Phase
                    });
                }
            }
            return result;
        }

        /// <summary>按单个游戏结束本轮快照中的进程（供网页/托盘使用）。</summary>
        public bool KillTarget(string processName)
        {
            var st = _states.Values.FirstOrDefault(s =>
                s.Game.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            if (st == null) return false;

            bool killed = KillSnapshot(st);
            if (killed) AddLog($"{st.Game.DisplayName} 已被手动结束。");
            return killed;
        }

        public void KillAllTargets()
        {
            foreach (var kv in _states)
            {
                var st = kv.Value;
                if (KillSnapshot(st))
                    AddLog($"{st.Game.DisplayName} 的匹配进程已被手动关闭。");
                DisposeTargets(st);
            }
        }

        public void Dispose()
        {
            _timer?.Stop(); _timer?.Dispose();
            _saveTimer?.Stop(); _saveTimer?.Dispose();
            foreach (var st in _states.Values) DisposeTargets(st);
            _config.SaveQuiet();
        }

        private void AddLog(string msg)
        {
            try { Log?.Invoke(msg); } catch { }
        }
    }
}
