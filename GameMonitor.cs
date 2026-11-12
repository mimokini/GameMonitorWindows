using System.Diagnostics;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    /// <summary>
    /// 监控状态机（单点管理，浮层只负责画）：
    /// Idle → Running → Reminding → CountingDown → Cooldown（休息期）/ QuotaExhausted（今日已用完）。
    /// 时段规则命中禁止时段时直接进入 BlockedBySchedule（期间不累计会话）。
    /// 全局暂停（PauseUntil）期间不识别、不提示、不计时。
    /// </summary>
    public enum MonitorPhase { Idle, Running, Reminding, CountingDown, Cooldown, QuotaExhausted, BlockedBySchedule }

    /// <summary>进程匹配方式，默认“包含”以兼容旧配置。</summary>
    public enum MatchMode { Contains, ExactProcess, ExactWindowTitle }

    /// <summary>倒计时的触发原因：加时只允许取消“单次超时/条目配额”引起的倒计时，禁止时段不允许。</summary>
    public enum CountdownCause { None, Session, Quota, TotalLimit, Schedule }

    public class GameStatus
    {
        public GameItem Game;
        public int UsedSecondsToday;
        public bool IsRunning;
        public MonitorPhase Phase;
        /// <summary>本轮已计时的秒数（用于悬浮框展示“剩余/单次”）。</summary>
        public int SessionSeconds;
        /// <summary>间隔期剩余秒数（仅 Cooldown 阶段有效）。</summary>
        public int CooldownRemainingSeconds;
        /// <summary>间隔期结束时刻（仅 Cooldown 阶段有效）；缺失时为 null，悬浮窗据此显示无数字文案。</summary>
        public DateTime? CooldownEnd;
        /// <summary>宽限（提醒）阶段剩余秒数（仅 Reminding 阶段有效，向上取整）。</summary>
        public int RemindRemainingSeconds;
        /// <summary>倒计时阶段剩余秒数（仅 CountingDown 阶段有效，向上取整）。</summary>
        public int CountdownRemainingSeconds;
        /// <summary>今日配额是否已用完（含临时加时）。</summary>
        public bool QuotaExhausted;
        /// <summary>今日实际配额分钟数（含周末倍数与加时；0 表示不限）。</summary>
        public int EffectiveLimitMinutes;
        /// <summary>今日额外加时分钟数（条目级）。</summary>
        public int BonusMinutes;
        /// <summary>全部监控条目今日已用秒数之和。</summary>
        public int TotalUsedSecondsToday;
        /// <summary>每日总限额是否已用完。</summary>
        public bool TotalLimitExceeded;
        /// <summary>今日实际单次时长分钟数（含周末倍数）。</summary>
        public int EffectiveRunMinutes;
        /// <summary>本轮加时秒数（可负：负数表示本轮上限被缩短）。</summary>
        public int SessionBonusSeconds;
        /// <summary>本轮可用总时长（分钟，含加时；按秒向上取整）。</summary>
        public int RoundLimitMinutes;
        /// <summary>本轮剩余可用分钟数（按秒向上取整，悬浮窗/设置页/网页的唯一来源）。</summary>
        public int RoundRemainingMinutes;
        /// <summary>今日生效的周末倍数（非周末为 1.0）。</summary>
        public double WeekendMultiplier;
        /// <summary>单次时长是否已被“没收”（网页/接口强制耗尽）。</summary>
        public bool SessionForced;
        /// <summary>全局暂停状态（与游戏无关，复制到每条状态里方便展示）。</summary>
        public bool IsPaused;
        public int PauseRemainingSeconds;
        /// <summary>当前时刻是否命中禁止时段（全局规则）。</summary>
        public bool InBlockedPeriod;
        /// <summary>当前时刻是否处于开放时段（未配置开放规则时恒为 true）。</summary>
        public bool InAllowedPeriod;
        /// <summary>当前是否处于“开放规则命中”的特殊模式（只计时，不弹窗、不强杀）。</summary>
        public bool InAllowRulePeriod;
        /// <summary>当前命中规则摘要（禁止/非开放时段）。</summary>
        public string ScheduleSummary = "";
        /// <summary>本次运行期间是否检测到系统时间相对单调时间跳变（不阻断功能，仅留痕）。</summary>
        public bool ClockJumpDetected;
    }

    /// <summary>状态文案 + 圆点归属。</summary>
    public struct PhaseInfo
    {
        public string Text;
        /// <summary>圆点配色键：idle/running/reminding/cooldown/quota。</summary>
        public string DotKey;
        public bool Filled;
    }

    /// <summary>
    /// 状态文案的唯一来源：悬浮窗与设置页都取 <see cref="PhaseInfo.Text"/>，
    /// 保证同一状态在两处是同一套说法、同一组数字（数字取自 GetStatuses）。
    /// 网页端另有一份 JS 实现，用词与本类保持一致。
    /// </summary>
    public static class PhaseText
    {
        /// <summary>空闲态文案变体：完整一行（默认）/ 只显示已用 / 只显示限额（悬浮窗轮换用）。</summary>
        public const int IdleVariantFull = 0;
        public const int IdleVariantUsed = 1;
        public const int IdleVariantLimit = 2;

        public static PhaseInfo Describe(GameStatus s, int idleVariant = IdleVariantFull)
        {
            if (s == null) return Make("", "idle", false);

            if (s.IsPaused) return Make("暂停中", "idle", false);

            // 开放时段且正在运行：只显示“开放中”，不展示剩余/已用时间（计时与统计照旧）
            if (s.InAllowRulePeriod && s.IsRunning) return Make("开放中", "running", true);

            switch (s.Phase)
            {
                case MonitorPhase.QuotaExhausted:
                    return Make("禁止运行", "quota", false);

                case MonitorPhase.BlockedBySchedule:
                    // 与配额用完同一红色，但文案区分：禁止运行 / 禁止时段
                    return Make("禁止时段", "quota", false);

                case MonitorPhase.Cooldown:
                    return Make(CooldownText(s), "cooldown", false);

                case MonitorPhase.Reminding:
                    // 宽限期：此时本轮剩余为 0，不能显示「还剩 0 分」
                    return Make(GraceText(s), "reminding", true);

                case MonitorPhase.CountingDown:
                    // 配额用完后的倒计时同样可能出现剩余 0 分，改为按秒展示
                    if (s.RoundRemainingMinutes > 0) return Make(RemainingText(s), "reminding", true);
                    return Make(s.CountdownRemainingSeconds > 0
                        ? $"倒计时 {s.CountdownRemainingSeconds} 秒"
                        : "倒计时", "reminding", true);

                case MonitorPhase.Running:
                    return Make(RemainingText(s), "running", true);

                default:
                    if (s.IsRunning) return Make(RemainingText(s), "running", true);
                    // 空闲态：已用 = 本轮上限 - 本轮剩余，避免第三份独立计算
                    int limit = RoundLimit(s);
                    int used = Math.Max(0, Math.Min(limit, limit - s.RoundRemainingMinutes));
                    if (idleVariant == IdleVariantUsed) return Make($"已用 {used} 分", "idle", false);
                    if (idleVariant == IdleVariantLimit) return Make($"限额 {limit} 分", "idle", false);
                    return Make($"已用 {used} 分 / {limit} 分", "idle", false);
            }
        }

        private static PhaseInfo Make(string text, string dotKey, bool filled)
            => new PhaseInfo { Text = text, DotKey = dotKey, Filled = filled };

        /// <summary>本轮上限分钟数；缺值（旧状态）时回退到生效单次时长。</summary>
        private static int RoundLimit(GameStatus s)
            => Math.Max(1, s.RoundLimitMinutes > 0 ? s.RoundLimitMinutes : s.EffectiveRunMinutes);

        private static string RemainingText(GameStatus s)
            => $"还剩 {Math.Max(0, s.RoundRemainingMinutes)} 分";

        /// <summary>
        /// 宽限期文案：剩余不足 60 秒按秒显示，否则按分钟向上取整；没有可用剩余秒数时只显示「宽限期」。
        /// 宽限期本身不显示「还剩 0 分」。
        /// </summary>
        private static string GraceText(GameStatus s)
        {
            int remainSec = Math.Max(0, s.RemindRemainingSeconds);
            if (remainSec <= 0) return "宽限期";
            if (remainSec < 60) return $"宽限期 {remainSec} 秒";
            return $"宽限期 {(int)Math.Ceiling(remainSec / 60.0)} 分";
        }

        /// <summary>
        /// 休息期文案：不足 60 秒按秒显示（向上取整、保底 1 秒），60 秒及以上按分钟向上取整，
        /// 上限为配置的休息分钟数；结束时刻缺失/默认值/已过期时显示「休息中」。
        /// </summary>
        private static string CooldownText(GameStatus s)
        {
            DateTime? end = s.CooldownEnd;
            DateTime now = DateTime.Now;
            if (!end.HasValue || end.Value <= DateTime.MinValue || end.Value <= now) return "休息中";

            double remainSeconds = (end.Value - now).TotalSeconds;
            int remainSec = Math.Max(1, (int)Math.Ceiling(remainSeconds));
            if (remainSec < 60) return $"休息 {remainSec} 秒";

            int total = s.Game != null ? Math.Max(1, s.Game.CooldownMinutes) : 1;
            int remainMin = Math.Max(1, (int)Math.Ceiling(remainSeconds / 60.0));
            return $"休息 {Math.Min(total, remainMin)} 分";
        }
    }

    /// <summary>仅标题命中时的“等待窗口关闭”状态：10 秒内容忍进程存活，到期仍关不掉才强杀。</summary>
    public class PendingWindowClose
    {
        /// <summary>宽限期截止时刻（每次发现新命中窗口都会重新计时）。</summary>
        public DateTime Deadline;
        /// <summary>已经发过 WM_CLOSE 的窗口句柄（避免每秒重复发送）。</summary>
        public HashSet<long> RequestedHandles = new HashSet<long>();
        public string Title = "";
        public string ProcessName = "";
        /// <summary>是否已记录“窗口已关、进程仍在后台”的 INFO（只记一次）。</summary>
        public bool BackgroundLogged;
    }

    public class GameRuntimeState
    {
        public GameItem Game;
        public MonitorPhase Phase = MonitorPhase.Idle;
        public int SessionSeconds;
        public DateTime? RemindAt;
        public DateTime? CountdownEnd;
        public DateTime? CooldownEnd;
        public List<Process> TargetProcesses = new List<Process>();
        /// <summary>本轮命中的顶层窗口（仅标题命中时用于发 WM_CLOSE，不杀进程）。</summary>
        public List<WindowInfo> TargetWindows = new List<WindowInfo>();
        /// <summary>按 PID 记录的待关闭窗口（跨 tick 保留，直到窗口关闭或强杀）。</summary>
        public Dictionary<int, PendingWindowClose> PendingWindowCloses = new Dictionary<int, PendingWindowClose>();
        public DateTime LastBlockedNotify = DateTime.MinValue;
        /// <summary>禁止时段提示的独立冷却时间，避免与配额/间隔期提示互相抑制。</summary>
        public DateTime LastScheduleNotify = DateTime.MinValue;
        public DateTime? LastMatchAt;
        public bool IsRunning;
        /// <summary>网页“结束本次”时，若当前未运行则标记，下次启动立即进入提醒流程。</summary>
        public bool SessionForced;
        /// <summary>当前“今日已用完”是因为跨条目总限额（用于提示“今日总时长已用完”）。</summary>
        public bool BlockedByTotalLimit;
        /// <summary>是否处于“开放时段内”的特殊模式（只计时，不弹窗、不强杀）。</summary>
        public bool InAllowPeriod;
        /// <summary>开放时段内已超限：离开开放时段后不补触发提醒，继续正常计时。</summary>
        public bool SuppressOverrun;
        /// <summary>本轮加时秒数（持久化到 SessionRecord.SessionBonusSeconds）。</summary>
        public int SessionBonusSeconds;
        /// <summary>当前倒计时的触发原因（加时/禁止时段交互需要区分）。</summary>
        public CountdownCause CountdownCause = CountdownCause.None;
    }

    /// <summary>加时/减时结果：成功与否、错误文案、给用户/网页的反馈文案。</summary>
    public class BonusAdjustResult
    {
        public bool Success;
        public string Error;
        public string Message;
        public int SessionBonusSeconds;
    }

    public class GameMonitor : IDisposable
    {
        /// <summary>关键字最小长度：允许两字程序名（如“原神”“QQ”），仍拦住单字这类会匹配大量进程的子串。</summary>
        public const int MinKeywordLength = 2;

        /// <summary>单次 tick 最多计入的秒数，避免休眠/卡顿后一次性把睡眠时间算成游戏时间。</summary>
        private const int MaxTickSeconds = 15;

        /// <summary>同一提示的最小重复间隔，避免每 tick 都弹一次。</summary>
        private const int NotifyIntervalSeconds = 15;

        /// <summary>禁止时段提示的冷却秒数：同一进程在此期间只提示一次。</summary>
        private const int ScheduleNotifySeconds = 30;

        /// <summary>系统时间相对单调时间跳变的告警阈值（秒）。</summary>
        private const int ClockJumpThresholdSeconds = 60;

        /// <summary>单次加时允许的最大分钟数。</summary>
        public const int MaxBonusMinutes = 600;

        /// <summary>仅标题命中时，等待窗口自行关闭的宽限秒数；到期窗口仍在才强杀整个进程。</summary>
        private const int WindowCloseGraceSeconds = 10;
        /// <summary>定时落盘间隔：配置有改动才写，空闲时完全不写。</summary>
        private const int SaveIntervalMs = 5 * 60 * 1000;

        private readonly Timer _timer;
        private readonly Timer _saveTimer;
        private readonly AppConfig _config;
        private readonly CountdownForm _overlay;
        private Dictionary<string, GameRuntimeState> _states =
            new Dictionary<string, GameRuntimeState>(StringComparer.OrdinalIgnoreCase);

        /// <summary>当前 tick 的进程快照（唯一所有者）：下一 tick 统一释放，条目之间共享同一份 Process 对象。</summary>
        private Process[] _tickProcesses = Array.Empty<Process>();

        /// <summary>同一进程被多条监控命中时的跳过日志限频（key=条目+PID，60 秒一次）。</summary>
        private readonly Dictionary<string, DateTime> _ownershipLogAt = new Dictionary<string, DateTime>();

        private DateTime _lastTickAt = DateTime.Now;
        private bool _pauseLogged;

        // 防改时钟：单调时钟与墙钟对照，跳变只告警留痕，不阻断功能
        private readonly Stopwatch _monoClock = Stopwatch.StartNew();
        private long _lastMonoMs;
        private DateTime _lastJumpCheckAt = DateTime.MinValue;
        private bool _clockJumpDetected;

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
            // 5 分钟才落盘一次，且只在配置内容真正变化过才写（空闲时不产生磁盘写入）；
            // 暂停/加时/设置等显式操作在各自入口里立即保存，不等这个定时器。
            _saveTimer = new Timer { Interval = SaveIntervalMs };
            _saveTimer.Tick += (s, e) =>
            {
                _config.SaveIfDirty();
                UsageHistoryStore.SaveIfDirty();
            };
            _overlay.Finished += OnOverlayFinished;
        }

        public void Start()
        {
            _lastTickAt = DateTime.Now;
            _lastMonoMs = _monoClock.ElapsedMilliseconds;
            _lastJumpCheckAt = _lastTickAt;
            _timer.Start();
            _saveTimer.Start();

            // 启动时把配置里的今日用量/加时同步到使用记录，保证“使用记录”和周统计连续
            lock (_config.SyncRoot)
            {
                _config.PruneSessionRecords();
                DateTime today = DateTime.Today;
                foreach (var g in _config.Games)
                {
                    if (string.IsNullOrWhiteSpace(g.ProcessName)) continue;
                    var usage = _config.Usage.FirstOrDefault(u =>
                        u.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                        u.Date.Date == today);
                    UsageHistoryStore.UpdateUsed(g.ProcessName, usage == null ? 0 : usage.UsedSeconds);
                }
                // 旧“条目级每日加时”一次性折算进今日总限额，避免与新本轮加时双重宽松
                if (_config.MigrateLegacyEntryBonusToTotalLimit() > 0)
                    _config.SaveQuiet();
            }
            UsageHistoryStore.SaveIfDirty();

            if (IsPaused(DateTime.Now))
                AddLog($"启动时处于暂停期，剩余 {FormatDuration(_config.PauseUntil - DateTime.Now)}；暂停期间不识别、不提示、不计时。");
        }

        public void Stop() { _timer.Stop(); _saveTimer.Stop(); }

        public void ReloadGames()
        {
            DateTime now = DateTime.Now;
            var newStates = new Dictionary<string, GameRuntimeState>(StringComparer.OrdinalIgnoreCase);

            foreach (var g in _config.Games.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.ProcessName)))
            {
                // 条目标识（ProcessName）只是显示/统计用的稳定 id，真正决定能否监控的是关键字列表：
                // 任一个关键字达到最小长度即可，不能因为首个关键字（恰好是标识）较短就整条跳过。
                if (!g.KeywordList.Any(k => k.Length >= MinKeywordLength))
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
                    var st = new GameRuntimeState { Game = g };

                    // 恢复重启前仍在进行的间隔期，避免靠重启绕过休息
                    var cd = GetPersistedCooldown(g.ProcessName);
                    if (cd != null && cd.EndTime > now)
                    {
                        st.CooldownEnd = cd.EndTime;
                        st.Phase = MonitorPhase.Cooldown;
                        AddLog($"「{g.DisplayName}」恢复间隔期，剩余 {FormatDuration(cd.EndTime - now)}。");
                    }

                    newStates[key] = st;
                }
            }

            foreach (var kv in _states)
            {
                if (!newStates.ContainsKey(kv.Key))
                    DisposeTargets(kv.Value);
            }

            _states = newStates;

            // 恢复/清零会话累计：持久化记录决定“关掉再开继续累计”，跨天与改单次设定时清零
            foreach (var st in newStates.Values)
                RestoreSessionState(st, now);

            // 清理已过期 / 已不存在的游戏的间隔期记录
            DateTime nowCheck = DateTime.Now;
            int expired = _config.Cooldowns.RemoveAll(c => c != null && c.EndTime <= nowCheck);
            if (expired > 0) AppLog.Info($"清理已过期的间隔期记录 {expired} 条。");
            if (_config.Cooldowns.Count > 0)
            {
                int removed = _config.Cooldowns.RemoveAll(c =>
                    c == null ||
                    string.IsNullOrWhiteSpace(c.ProcessName) ||
                    !_config.Games.Any(g => g.ProcessName != null &&
                        g.ProcessName.Equals(c.ProcessName, StringComparison.OrdinalIgnoreCase)));
                if (removed > 0) _config.SaveQuiet();
            }
            else if (expired > 0)
            {
                _config.SaveQuiet();
            }

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

        // ================= 暂停 / 条目加时 =================

        public bool IsPaused(DateTime now) => _config.PauseUntil > now;

        /// <summary>暂停监控：暂停期间不识别、不提示、不计时；截止时间持久化到 config。</summary>
        public void Pause(int minutes)
        {
            if (minutes <= 0) return;

            lock (_config.SyncRoot)
            {
                _config.PauseUntil = DateTime.Now.AddMinutes(minutes);
                _config.SaveQuiet();
            }

            _pauseLogged = true;
            AddLog($"已暂停监控 {minutes} 分钟（截止 {_config.PauseUntil:HH:mm:ss}）；暂停期间不识别、不提示、不计时。");
            _overlay.ShowNotice("已暂停监控", $"{minutes} 分钟内不识别、不提示、也不计时。到期后自动恢复。", 4000);
        }

        /// <summary>取消暂停，立即恢复识别与计时。</summary>
        public void ResumePause()
        {
            lock (_config.SyncRoot)
            {
                if (_config.PauseUntil <= DateTime.Now) return;
                _config.PauseUntil = DateTime.MinValue;
                _config.SaveQuiet();
            }

            _pauseLogged = false;
            _lastTickAt = DateTime.Now;
            AddLog("已取消暂停，恢复识别与计时。");
            _overlay.ShowNotice("已恢复监控", "暂停已取消，将重新识别监控程序并计时。", 3000);
        }

        /// <summary>按关键字查找监控条目（不区分大小写）。</summary>
        public GameItem FindGame(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            string key = processName.Trim();
            return _config.Games.FirstOrDefault(g =>
                g != null && g.ProcessName != null &&
                g.ProcessName.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 本轮单次可用时长的加时/减时（minutes 可为负）。加时作用于“当前这一轮”，
        /// 存到条目级 SessionBonusSeconds（随会话记录持久化，重启不丢），不再写每日 BonusRecords。
        /// 减时优先扣本轮加时，不足时缩短本轮上限；任何情况下不允许低于本轮已用时长。
        /// </summary>
        public BonusAdjustResult AdjustSessionBonus(string processName, int minutes)
        {
            var result = new BonusAdjustResult();
            if (string.IsNullOrWhiteSpace(processName))
            {
                result.Error = "请先在列表中选择一个条目";
                return result;
            }
            if (minutes == 0 || Math.Abs(minutes) > MaxBonusMinutes)
            {
                result.Error = $"请输入 1~{MaxBonusMinutes} 之间的分钟数";
                return result;
            }

            string displayName;
            string notice = null;

            lock (_config.SyncRoot)
            {
                var game = FindGame(processName);
                if (game == null)
                {
                    result.Error = "未找到该条目（可能已被删除或改名）";
                    return result;
                }
                displayName = string.IsNullOrWhiteSpace(game.DisplayName) ? game.ProcessName : game.DisplayName;

                var st = _states.Values.FirstOrDefault(s => s.Game.ProcessName
                    .Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase));
                if (st == null)
                {
                    result.Error = "该条目当前未启用（不在监控列表中）";
                    return result;
                }

                DateTime now = DateTime.Now;
                DateTime today = now.Date;
                bool totalExceeded = IsTotalQuotaExceeded(GetTotalUsedSecondsToday(), GetDailyTotalLimitMinutes());
                int roundLimitBefore = GetRoundLimitSeconds(st, today);
                int usedSeconds = Math.Max(0, st.SessionSeconds);

                if (minutes < 0)
                {
                    int reduce = -minutes * 60;
                    int maxReduce = Math.Max(0, roundLimitBefore - usedSeconds);
                    if (reduce > maxReduce)
                    {
                        result.Error = maxReduce <= 0
                            ? "本轮可用时长已用尽，无法再减时。"
                            : $"最多只能减 {maxReduce / 60} 分钟（再减会让本轮可用时长低于已用时长）。";
                        return result;
                    }

                    st.SessionBonusSeconds -= reduce;
                    int roundLimitAfter = GetRoundLimitSeconds(st, today);
                    _config.SetSessionUsage(game.ProcessName, st.SessionSeconds, game.RunMinutes, st.SessionBonusSeconds);

                    if (st.Phase == MonitorPhase.Running && usedSeconds >= roundLimitAfter)
                    {
                        EnterSessionReminding(st, now, "本轮时间已到");
                        notice = $"「{displayName}」已减时 {-minutes} 分钟，本轮已无可用时间，进入提醒。";
                    }
                    else
                    {
                        notice = $"「{displayName}」本轮可用时长 -{-minutes} 分钟（本轮上限 {DescribeRoundLimit(roundLimitAfter)}）。";
                    }

                    AddLog($"「{displayName}」本轮减时 {-minutes} 分钟，本轮加时 {FormatBonusSeconds(st.SessionBonusSeconds)}，上限 {DescribeRoundLimit(roundLimitAfter)}。");
                }
                else
                {
                    int add = minutes * 60;

                    // 只在“确实处于禁止时段流程”时拒绝：禁止态本身，或由禁止时段引起的倒计时。
                    // 休息期/配额用完/空闲时即使当前时间命中禁止规则，也允许加时（累加到下一次单次）。
                    if (st.Phase == MonitorPhase.BlockedBySchedule ||
                        (st.Phase == MonitorPhase.CountingDown && st.CountdownCause == CountdownCause.Schedule))
                    {
                        result.Error = "禁止时段生效中，无法加时";
                        return result;
                    }

                    st.SessionBonusSeconds += add;
                    _config.SetSessionUsage(game.ProcessName, st.SessionSeconds, game.RunMinutes, st.SessionBonusSeconds);

                    if (st.Phase == MonitorPhase.QuotaExhausted && totalExceeded)
                    {
                        // 总限额已用完：加时只记录到本轮，不解除总限额拦截
                        notice = $"「{displayName}」已加时 {minutes} 分钟，但今日总限额已用完，仍无法启动。";
                    }
                    else if (st.Phase == MonitorPhase.CountingDown && st.CountdownCause == CountdownCause.TotalLimit)
                    {
                        notice = $"「{displayName}」已加时 {minutes} 分钟，但今日总限额已用完，倒计时与拦截继续。";
                    }
                    else
                    {
                        switch (st.Phase)
                        {
                            case MonitorPhase.CountingDown:
                                // 只允许取消“单次超时/条目配额”引起的倒计时（禁止时段在前面已拒绝）
                                CancelCountdownForBonus(st, game.ProcessName);
                                notice = $"「{displayName}」已加时 {minutes} 分钟，倒计时已取消，继续本轮。";
                                break;

                            case MonitorPhase.Reminding:
                                st.RemindAt = null;
                                st.Phase = st.IsRunning ? MonitorPhase.Running : MonitorPhase.Idle;
                                HideRemindingNotice(game.ProcessName);
                                notice = $"「{displayName}」已加时 {minutes} 分钟，提醒已取消，继续计时。";
                                break;

                            case MonitorPhase.QuotaExhausted:
                                st.Phase = st.IsRunning ? MonitorPhase.Running : MonitorPhase.Idle;
                                st.LastBlockedNotify = DateTime.MinValue;
                                st.BlockedByTotalLimit = false;
                                st.CountdownCause = CountdownCause.None;
                                notice = $"「{displayName}」已加时 {minutes} 分钟，已解除禁止；本轮加时用完后重新进入提醒/倒计时。";
                                break;

                            case MonitorPhase.Running:
                                notice = $"「{displayName}」本轮已加时 {minutes} 分钟（本轮上限 {DescribeRoundLimit(GetRoundLimitSeconds(st, today))}），继续计时。";
                                break;

                            case MonitorPhase.Cooldown:
                                notice = $"「{displayName}」已为下一次单次加时 {minutes} 分钟，休息结束后生效。";
                                break;

                            default:
                                notice = $"「{displayName}」已为下一次单次加时 {minutes} 分钟。";
                                break;
                        }
                    }

                    AddLog($"「{displayName}」本轮加时 {minutes} 分钟，本轮加时累计 {FormatBonusSeconds(st.SessionBonusSeconds)}，上限 {DescribeRoundLimit(GetRoundLimitSeconds(st, today))}。");
                }

                _config.SaveQuiet();
            }

            // 立即重算一轮，让提醒/倒计时/配额状态与界面同步
            Tick(null, EventArgs.Empty);

            // 加时/减时不再走全屏提示层：托盘气泡、设置页消息框与网页端各自反馈，
            // 这里只写日志（非阻断），避免挡住正在玩的游戏。
            if (!string.IsNullOrEmpty(notice))
                AddLog(notice);

            result.Success = true;
            result.Message = notice;
            result.SessionBonusSeconds = GetSessionBonusFor(processName);
            return result;
        }

        /// <summary>本轮可用总秒数 = 生效单次（含周末倍数）×60 + 本轮加时（可负，最低 0）。</summary>
        private static int GetRoundLimitSeconds(GameRuntimeState st, DateTime date)
        {
            if (st == null || st.Game == null) return 0;
            int baseSeconds = GetEffectiveRunMinutes(st.Game, date) * 60;
            return Math.Max(0, baseSeconds + st.SessionBonusSeconds);
        }

        private static string DescribeRoundLimit(int seconds)
            => $"{Math.Max(0, seconds) / 60} 分钟";

        private static string FormatBonusSeconds(int seconds)
        {
            int minutes = (int)Math.Round(Math.Abs(seconds) / 60.0, MidpointRounding.AwayFromZero);
            if (minutes == 0) return "0 分钟";
            return seconds > 0 ? $"+{minutes} 分钟" : $"-{minutes} 分钟";
        }

        /// <summary>读取条目当前持久化的本轮加时秒数。</summary>
        private int GetSessionBonusFor(string processName)
        {
            var rec = _config.GetSessionRecord(processName);
            return rec == null ? 0 : rec.SessionBonusSeconds;
        }

        /// <summary>本轮上限已到（或减时扣到已用）：进入提醒宽限期。</summary>
        private void EnterSessionReminding(GameRuntimeState st, DateTime now, string reason)
        {
            st.Phase = MonitorPhase.Reminding;
            st.RemindAt = now;
            st.CountdownCause = CountdownCause.None;
            AddLog($"{st.Game.DisplayName} {reason}，开始提醒，宽限 {_config.ReminderMinutes} 分钟。");
            ReminderRequested?.Invoke(st.Game);
            _overlay.ShowReminder(st.Game.DisplayName, _config.ReminderMinutes, reason);
            PromptSoundPlayer.Play(_config, PromptSoundKind.Grace);
        }

        /// <summary>本轮加时：取消由单次/条目配额引起的倒计时，回到运行（或空闲）态。</summary>
        private void CancelCountdownForBonus(GameRuntimeState st, string processName)
        {
            st.CountdownEnd = null;
            st.CountdownCause = CountdownCause.None;
            st.RemindAt = null;
            st.Phase = st.IsRunning ? MonitorPhase.Running : MonitorPhase.Idle;
            RemoveQueuedCountdown(processName);
            if (_showingProcess != null &&
                _showingProcess.Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                _overlay.StopAndHide();
                _countdownShowing = false;
                _showingProcess = null;
            }
            PersistSession(st);
        }

        /// <summary>收起该条目的提醒浮层（加时取消提醒时用）。</summary>
        private void HideRemindingNotice(string processName)
        {
            _overlay.HideRemindingNotice(processName);
        }

        /// <summary>
        /// 条目配额是否构成拦截：总限额始终拦截；本轮还有加时（且未用完）时条目配额让位于加时，
        /// 加时耗尽（归零/负数）后才重新拦截。
        /// </summary>
        private static bool IsEntryQuotaBlocked(GameRuntimeState st, GameUsage usage, int effectiveLimit,
            int limitSeconds, bool totalExceeded, DateTime today)
        {
            if (totalExceeded) return true;
            if (effectiveLimit <= 0) return false;
            if (st != null && st.SessionBonusSeconds > 0 && st.SessionSeconds < GetRoundLimitSeconds(st, today))
                return false;
            // 刚添加的条目在下一轮 Tick（GetOrCreateUsage）之前还没有用量记录，按 0 秒处理：
            // 否则设置页首次保存触发 GetStatuses 时会因 usage 为 null 抛异常。
            int usedSeconds = usage == null ? 0 : Math.Max(0, usage.UsedSeconds);
            return usedSeconds >= limitSeconds;
        }

        /// <summary>今天是否周六/周日。</summary>
        public static bool IsWeekend(DateTime date)
            => date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday;

        /// <summary>条目今天生效的周末倍数（非周末恒为 1.0）。</summary>
        public static double GetWeekendMultiplier(GameItem game, DateTime date)
        {
            if (game == null || !IsWeekend(date)) return 1.0;
            double m = game.WeekendMultiplier;
            if (double.IsNaN(m) || double.IsInfinity(m)) m = 1.0;
            return Math.Max(0.5, Math.Min(3.0, m));
        }

        /// <summary>单次时长（周末按倍数放大，四舍五入到整分钟，最小 1 分钟）。</summary>
        public static int GetEffectiveRunMinutes(GameItem game, DateTime date)
        {
            if (game == null) return 1;
            int baseMinutes = Math.Max(1, game.RunMinutes);
            double multiplier = GetWeekendMultiplier(game, date);
            return Math.Max(1, (int)Math.Round(baseMinutes * multiplier, MidpointRounding.AwayFromZero));
        }

        /// <summary>
        /// 条目今日实际配额：每日配额×周末倍数 + 该条目今日加时；
        /// DailyLimit=0 表示沿用每日总限额（总限额也为 0 时返回 0=不限）。
        /// </summary>
        public int GetEffectiveDailyLimit(GameItem game)
        {
            if (game == null) return 0;

            int bonus = _config.EntryBonusMinutesFor(game, DateTime.Today);
            int baseLimit = game.DailyLimitMinutes;
            if (baseLimit <= 0)
            {
                int totalBase = Math.Max(0, _config.DailyTotalLimitMinutes);
                return totalBase <= 0 ? 0 : totalBase + bonus;
            }

            double multiplier = GetWeekendMultiplier(game, DateTime.Today);
            int effective = Math.Max(1, (int)Math.Round(baseLimit * multiplier, MidpointRounding.AwayFromZero));
            return effective + bonus;
        }

        /// <summary>每日总限额（跨条目，含今日全部条目加时）；0 表示不启用。</summary>
        public int GetDailyTotalLimitMinutes()
        {
            int configured = Math.Max(0, _config.DailyTotalLimitMinutes);
            if (configured <= 0) return 0;
            return configured + _config.TotalEntryBonusMinutes(DateTime.Today);
        }

        /// <summary>全部监控条目今日已用秒数之和。</summary>
        public int GetTotalUsedSecondsToday()
        {
            DateTime today = DateTime.Today;
            int sum = 0;
            foreach (var g in _config.Games)
            {
                if (g == null || string.IsNullOrWhiteSpace(g.ProcessName)) continue;
                var usage = _config.Usage.FirstOrDefault(u =>
                    u != null && u.ProcessName != null &&
                    u.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase) &&
                    u.Date.Date == today);
                if (usage != null) sum += Math.Max(0, usage.UsedSeconds);
            }
            return sum;
        }

        /// <summary>每日总限额是否已用完（总限额 0 时不判定）。</summary>
        public bool IsTotalQuotaExceeded(int totalUsedSeconds, int totalLimitMinutes)
            => totalLimitMinutes > 0 && totalUsedSeconds >= totalLimitMinutes * 60;

        /// <summary>
        /// 会话累计的恢复与清零：持久化记录保证“关掉游戏再开/重启程序”继续累计；
        /// 跨天首次启动、单次设定被修改时清零（运行中跨天则继续本次计时）。
        /// </summary>
        private void RestoreSessionState(GameRuntimeState st, DateTime now)
        {
            if (st == null || st.Game == null) return;
            var rec = _config.GetSessionRecord(st.Game.ProcessName);
            if (rec == null) return;

            int runMinutes = Math.Max(1, st.Game.RunMinutes);

            if (rec.RunMinutes > 0 && rec.RunMinutes != runMinutes)
            {
                bool had = st.SessionSeconds > 0 || rec.UsedSeconds > 0;
                st.SessionSeconds = 0;
                st.SessionBonusSeconds = 0;
                _config.ResetSessionRecord(st.Game.ProcessName, runMinutes);
                if (had)
                    AddLog($"「{st.Game.DisplayName}」单次设定已修改（{rec.RunMinutes}→{runMinutes} 分），本次会话累计与加时清零。");
                return;
            }

            if (rec.Date.Date == now.Date)
            {
                st.SessionSeconds = Math.Max(st.SessionSeconds, Math.Max(0, rec.UsedSeconds));
                st.SessionBonusSeconds = rec.SessionBonusSeconds;
                return;
            }

            if (st.Phase == MonitorPhase.Idle || st.Phase == MonitorPhase.Cooldown ||
                st.Phase == MonitorPhase.QuotaExhausted)
            {
                st.SessionSeconds = 0;
                st.SessionBonusSeconds = 0;
                _config.ResetSessionRecord(st.Game.ProcessName, runMinutes);
                AddLog($"「{st.Game.DisplayName}」跨天首次启动，本次会话累计清零。");
            }
        }

        /// <summary>把当前会话累计写入持久化记录（每 tick 调用，随 5 分钟定时保存落盘）。</summary>
        private void PersistSession(GameRuntimeState st)
        {
            if (st == null || st.Game == null) return;
            _config.SetSessionUsage(st.Game.ProcessName, st.SessionSeconds, st.Game.RunMinutes, st.SessionBonusSeconds);
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

            // 暂停期间：不识别、不提示、不计时（不清空阶段，恢复后继续）
            if (IsPaused(now))
            {
                _pauseLogged = true;
                _lastTickAt = now;
                _lastMonoMs = _monoClock.ElapsedMilliseconds;
                _lastJumpCheckAt = now;
                lock (_config.SyncRoot)
                {
                    foreach (var st in _states.Values)
                    {
                        DisposeTargets(st);
                        st.IsRunning = false;
                    }
                }
                return;
            }

            if (_pauseLogged)
            {
                _pauseLogged = false;
                AddLog("暂停期已结束，自动恢复识别与计时。");
            }

            CheckClockJump(now);

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
                // 上一轮快照统一释放（唯一所有者），条目之间不再各自 Dispose 共享的 Process 对象
                DisposeTickProcesses();

                int totalUsedSeconds = GetTotalUsedSecondsToday();
                int totalLimitMinutes = GetDailyTotalLimitMinutes();
                bool totalExceeded = IsTotalQuotaExceeded(totalUsedSeconds, totalLimitMinutes);
                // 时段规则统一判定（禁止优先、同类并集）：所有条目共享同一套全局规则
                ScheduleDecision schedule = ScheduleRules.Evaluate(_config, now);

                var allProcesses = Process.GetProcesses();
                _tickProcesses = allProcesses;
                // 同一进程只归“配置顺序最靠前”的条目：按 _config.Games 顺序遍历，已归属的 PID 不再分配
                var claimedPids = new HashSet<int>();
                var orderedStates = new List<GameRuntimeState>();
                foreach (var g in _config.Games)
                {
                    if (g == null) continue;
                    GameRuntimeState state;
                    if (_states.TryGetValue(g.ProcessName, out state) && state != null)
                        orderedStates.Add(state);
                }
                try
                {
                    foreach (var st in orderedStates)
                    {
                        var game = st.Game;
                        var usage = GetOrCreateUsage(game.ProcessName);

                        if (usage.Date.Date != today)
                        {
                            usage.Date = today;
                            usage.UsedSeconds = 0;
                            // 跨天仍在计时：把前一天最后一次“关闭”记为 23:59:59，落在前一天区间内
                            if (st.IsRunning)
                                UsageHistoryStore.NoteEndTime(game.ProcessName, today.AddSeconds(-1));
                            if (st.Phase == MonitorPhase.Running || st.Phase == MonitorPhase.Reminding ||
                                st.Phase == MonitorPhase.CountingDown)
                            {
                                // 跨天只重置“今日”额度，正在进行的单次会话与阶段继续
                                AddLog($"{game.DisplayName} 跨天：今日用量清零，本次单次计时继续（不因跨天重置）。");
                            }
                            else
                            {
                                ResetDailyState(st, game.ProcessName, "跨天重置：今日用量清零。");
                            }
                        }

                        var matchSet = ProcessMatcher.FindMatchesDetailed(game, allProcesses, claimedPids);
                        foreach (var skipped in matchSet.SkippedProcesses)
                            LogOwnershipConflict(game, skipped);

                        st.TargetProcesses = matchSet.Processes;
                        st.TargetWindows = matchSet.Windows;
                        st.LastMatchAt = now;
                        st.IsRunning = matchSet.Processes.Count > 0;

                        // 仅标题命中的窗口：10 秒宽限（窗口关掉即不杀，关不掉才强杀）
                        ProcessPendingWindowCloses(st, now);

                        // ① 禁止时段最高优先级：按修正后的状态迁移处理（倒计时走完 / Running→倒计时 / 空闲立即关闭）
                        if (schedule.Blocked)
                        {
                            HandleBlockedPeriod(st, usage, now, st.IsRunning, elapsed, game.ProcessName);
                            continue;
                        }

                        // ② 开放时段：仍照常累计时长/用量，但不弹窗、不强杀；Web 的“结束本次/强制关闭”不受限制
                        if (schedule.HasAllowRules && schedule.InAllowedPeriod)
                        {
                            HandleAllowPeriod(st, usage, now, st.IsRunning, elapsed);
                            continue;
                        }

                        if (st.InAllowPeriod)
                        {
                            st.InAllowPeriod = false;
                            if (st.Phase == MonitorPhase.Running)
                            {
                                // 离开开放时段：已经超限也不补触发提醒，继续正常计时
                                st.SuppressOverrun = true;
                                AddLog($"{game.DisplayName} 开放时段结束，已超限部分不补触发提醒，继续正常计时。");
                            }
                            else
                            {
                                AddLog($"{game.DisplayName} 开放时段结束，恢复正常监控。");
                            }
                        }

                        // 禁止时段结束，恢复到空闲态，后续按正常流程重新判定
                        if (st.Phase == MonitorPhase.BlockedBySchedule)
                        {
                            st.Phase = MonitorPhase.Idle;
                            st.LastScheduleNotify = DateTime.MinValue;
                            AddLog($"{game.DisplayName} 禁止时段已结束，恢复监控（本次会话累计保持不变）。");
                        }

                        if (st.TargetProcesses.Count == 0)
                        {
                            HandleMissingProcess(st, now);
                            continue;
                        }

                        int effectiveLimit = GetEffectiveDailyLimit(game);
                        int limitSeconds = effectiveLimit > 0 ? effectiveLimit * 60 : int.MaxValue;
                        int roundLimitSeconds = GetRoundLimitSeconds(st, today);
                        bool quotaExceeded = IsEntryQuotaBlocked(st, usage, effectiveLimit, limitSeconds, totalExceeded, today);

                        // 配额被重置 / 加时后，自动解除锁定
                        if (st.Phase == MonitorPhase.QuotaExhausted && !quotaExceeded)
                            ResetBlockedState(st);

                        switch (st.Phase)
                        {
                            case MonitorPhase.Idle:
                                if (quotaExceeded)
                                {
                                    st.Phase = MonitorPhase.QuotaExhausted;
                                    st.BlockedByTotalLimit = totalExceeded;
                                    st.SessionSeconds = 0;
                                    st.SessionBonusSeconds = 0;
                                    st.CountdownCause = CountdownCause.None;
                                    _config.ResetSessionRecord(game.ProcessName, game.RunMinutes);
                                    st.SessionForced = false;
                                    KillSnapshot(st);
                                    UsageHistoryStore.NoteEndTime(game.ProcessName, now);
                                    NoteEndReason(game.ProcessName, totalExceeded ? "总时长到" : "配额到", now);
                                    AddLog(totalExceeded
                                        ? $"{game.DisplayName} 今日总时长已用完，进入“今日已用完”状态。"
                                        : $"{game.DisplayName} 今日配额已用完，进入“今日已用完”状态。");
                                    NotifyQuotaExhausted(st, now);
                                    break;
                                }

                                if (st.CooldownEnd.HasValue)
                                {
                                    if (now < st.CooldownEnd.Value)
                                    {
                                        st.Phase = MonitorPhase.Cooldown;
                                        KillSnapshot(st);
                                        AddLog($"{game.DisplayName} 处于间隔期，剩余 {FormatDuration(st.CooldownEnd.Value - now)}，已阻止启动。");
                                        NotifyCooldown(st, now);
                                        break;
                                    }

                                    st.CooldownEnd = null;
                                    PersistCooldown(game.ProcessName, null);
                                    AddLog($"{game.DisplayName} 间隔期已结束，恢复监控。");
                                }

                                if (st.SessionForced)
                                {
                                    // 未运行时被“没收单次时间”：下一次启动立即进入提醒/倒计时流程
                                    st.SessionForced = false;
                                    st.Phase = MonitorPhase.Reminding;
                                    st.RemindAt = now;
                                    st.SessionSeconds = roundLimitSeconds;
                                    st.CountdownCause = CountdownCause.None;
                                    PersistSession(st);
                                    NoteLaunchFromProcesses(game.ProcessName, st.TargetProcesses, now);
                                    string forcedText = $"「{game.DisplayName}」本次已结束，启动即进入提醒";
                                    AddLog($"{game.DisplayName} 本次已被结束（未运行时标记），开始提醒，宽限 {_config.ReminderMinutes} 分钟。");
                                    ReminderRequested?.Invoke(game);
                                    _overlay.ShowReminder(game.DisplayName, _config.ReminderMinutes, forcedText);
                                    PromptSoundPlayer.Play(_config, PromptSoundKind.Grace);
                                    break;
                                }

                                st.Phase = MonitorPhase.Running;
                                // 会话累计不清零：关掉再开/多开同名进程都继续累计，直到清零时机到来
                                PersistSession(st);
                                NoteLaunchFromProcesses(game.ProcessName, st.TargetProcesses, now);
                                AddLog($"{game.DisplayName} 已启动，本次还剩 {DescribeSessionRemain(st, roundLimitSeconds)}/{DescribeRoundLimit(roundLimitSeconds)}，今日已用 {usage.UsedSeconds / 60}/{DescribeQuota(effectiveLimit)} 分。");
                                break;

                            case MonitorPhase.Running:
                                st.SessionSeconds += elapsed;
                                usage.UsedSeconds += elapsed;
                                _config.MarkDirty();
                                PersistSession(st);
                                UsageHistoryStore.UpdateUsed(game.ProcessName, usage.UsedSeconds, now.Hour);
                                bool sessionUp = st.SessionSeconds >= roundLimitSeconds;
                                bool quotaUp = IsEntryQuotaBlocked(st, usage, effectiveLimit, limitSeconds, totalExceeded, today);
                                if ((sessionUp || quotaUp) && st.SuppressOverrun)
                                {
                                    // 开放时段内已超限：不补触发提醒，继续正常计时
                                    break;
                                }
                                if (sessionUp || quotaUp)
                                {
                                    st.Phase = MonitorPhase.Reminding;
                                    st.RemindAt = now;
                                    st.CountdownCause = CountdownCause.None;
                                    string reason = totalExceeded
                                        ? $"「{game.DisplayName}」今日总时长已用完"
                                        : quotaUp
                                            ? $"「{game.DisplayName}」今日累计 {effectiveLimit} 分钟已用完"
                                            : $"「{game.DisplayName}」本次已运行 {DescribeRoundLimit(roundLimitSeconds)}";
                                    AddLog(reason + $"，开始提醒，宽限 {_config.ReminderMinutes} 分钟。");
                                    ReminderRequested?.Invoke(game);
                                    _overlay.ShowReminder(game.DisplayName, _config.ReminderMinutes, reason);
                                    PromptSoundPlayer.Play(_config, PromptSoundKind.Grace);
                                }
                                break;

                            case MonitorPhase.Reminding:
                                usage.UsedSeconds += elapsed;
                                _config.MarkDirty();
                                UsageHistoryStore.UpdateUsed(game.ProcessName, usage.UsedSeconds, now.Hour);
                                if (st.RemindAt.HasValue &&
                                    (now - st.RemindAt.Value).TotalSeconds >= _config.ReminderMinutes * 60)
                                {
                                    st.Phase = MonitorPhase.CountingDown;
                                    st.CountdownEnd = now.AddSeconds(_config.CountdownSeconds);
                                    st.CountdownCause = totalExceeded
                                        ? CountdownCause.TotalLimit
                                        : IsEntryQuotaBlocked(st, usage, effectiveLimit, limitSeconds, totalExceeded, today)
                                            ? CountdownCause.Quota
                                            : CountdownCause.Session;
                                    AddLog($"{game.DisplayName} 宽限期结束，开始 {_config.CountdownSeconds} 秒倒计时。");
                                    PromptSoundPlayer.Play(_config, PromptSoundKind.Countdown);
                                    EnqueueCountdown(game.ProcessName);
                                }
                                break;

                            case MonitorPhase.CountingDown:
                                usage.UsedSeconds += elapsed;
                                _config.MarkDirty();
                                UsageHistoryStore.UpdateUsed(game.ProcessName, usage.UsedSeconds, now.Hour);
                                if (st.CountdownEnd.HasValue && now >= st.CountdownEnd.Value)
                                {
                                    KillSnapshot(st);
                                    DisposeTargets(st);
                                    RemoveQueuedCountdown(game.ProcessName);
                                    UsageHistoryStore.NoteEndTime(game.ProcessName, now);

                                    if (_showingProcess != null &&
                                        _showingProcess.Equals(game.ProcessName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _overlay.StopAndHide();
                                        _countdownShowing = false;
                                        _showingProcess = null;
                                    }

                                    bool quotaReached = totalExceeded || (effectiveLimit > 0 && usage.UsedSeconds >= limitSeconds);
                                    st.SessionSeconds = 0;
                                    st.SessionBonusSeconds = 0;
                                    st.CountdownCause = CountdownCause.None;
                                    _config.ResetSessionRecord(game.ProcessName, game.RunMinutes);
                                    st.CountdownEnd = null;
                                    st.RemindAt = null;
                                    st.SessionForced = false;

                                    if (quotaReached)
                                    {
                                        st.Phase = MonitorPhase.QuotaExhausted;
                                        st.BlockedByTotalLimit = totalExceeded;
                                        NoteEndReason(game.ProcessName, totalExceeded ? "总时长到" : "配额到", now);
                                        AddLog(totalExceeded
                                            ? $"{game.DisplayName} 倒计时结束，已强制关闭匹配进程；今日总时长已用完。"
                                            : $"{game.DisplayName} 倒计时结束，已强制关闭匹配进程；今日配额已用完。");
                                        NotifyQuotaExhausted(st, now);
                                    }
                                    else
                                    {
                                        int cooldownMinutes = Math.Max(1, Math.Min(180,
                                            game.CooldownMinutes > 0 ? game.CooldownMinutes : 10));
                                        st.Phase = MonitorPhase.Cooldown;
                                        st.CooldownEnd = now.AddMinutes(cooldownMinutes);
                                        PersistCooldown(game.ProcessName, st.CooldownEnd);
                                        NoteEndReason(game.ProcessName, "时长到", now);
                                        UsageHistoryStore.NoteCooldown(game.ProcessName, true);
                                        UsageHistoryStore.SaveIfDirty();
                                        AddLog($"{game.DisplayName} 倒计时结束，已强制关闭匹配进程；进入 {cooldownMinutes} 分钟间隔期。");
                                        NotifyCooldown(st, now);
                                    }

                                    SyncOverlayState();
                                }
                                break;

                            case MonitorPhase.Cooldown:
                                if (!st.CooldownEnd.HasValue || now >= st.CooldownEnd.Value)
                                {
                                    st.Phase = MonitorPhase.Idle;
                                    st.CooldownEnd = null;
                                    PersistCooldown(game.ProcessName, null);
                                    st.LastBlockedNotify = DateTime.MinValue;
                                    // 清零时机①：该条目单次用完并进入间隔期，间隔期结束时归零；
                                    // 休息期里加的“下一次单次加时”保留，下一轮直接生效
                                    st.SessionSeconds = 0;
                                    _config.ResetSessionRecord(game.ProcessName, game.RunMinutes, resetBonus: false);
                                    AddLog($"{game.DisplayName} 间隔期结束，本次会话累计清零，下一 tick 重新开始单次计时。");
                                    break;
                                }

                                KillSnapshot(st);
                                NotifyCooldown(st, now);
                                break;

                            case MonitorPhase.QuotaExhausted:
                                KillSnapshot(st);
                                NotifyQuotaExhausted(st, now);
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Tick 内任何异常都不得抛到 UI 线程：记录后等下一次 tick 继续
                    AppLog.Error("监控 tick 执行失败（本次跳过，下一 tick 继续）", ex);
                    AddLog("监控 tick 执行失败，已记录日志；下一轮继续。");
                }
            }
        }

        /// <summary>
        /// 禁止时段的状态迁移（按用户修正后的规则）：
        /// ① 已在倒计时 → 不打断，倒计时走完再强杀；
        /// ② Running/Reminding → 立即跳到倒计时（按该条目配置的 CountdownSeconds），走完再杀；
        /// ③ 其余（空闲/休息期/配额用完）检测到启动 → 立即提示并关闭（同一进程 30 秒冷却）。
        /// 只有程序确实在运行时才提示。
        /// </summary>
        private void HandleBlockedPeriod(GameRuntimeState st, GameUsage usage, DateTime now,
            bool running, int elapsed, string key)
        {
            string name = st.Game.ProcessName;

            if (st.Phase == MonitorPhase.CountingDown)
            {
                if (!running)
                {
                    EnterBlockedState(st, now, false, false);
                    AddLog($"{st.Game.DisplayName} 处于禁止时段，进程已退出，已取消倒计时（不提示）。");
                    return;
                }

                if (st.CountdownEnd.HasValue && now < st.CountdownEnd.Value)
                {
                    // 倒计时继续走，期间照常计入用量，不打断
                    st.IsRunning = true;
                    st.SessionSeconds += elapsed;
                    usage.UsedSeconds += elapsed;
                    _config.MarkDirty();
                    PersistSession(st);
                    UsageHistoryStore.UpdateUsed(name, usage.UsedSeconds, now.Hour);
                    return;
                }

                // 倒计时到 0：此刻才强杀
                EnterBlockedState(st, now, true, true);
                return;
            }

            if (st.Phase == MonitorPhase.Running || st.Phase == MonitorPhase.Reminding)
            {
                if (running)
                {
                    st.Phase = MonitorPhase.CountingDown;
                    st.RemindAt = null;
                    st.CountdownEnd = now.AddSeconds(Math.Max(1, _config.CountdownSeconds));
                    st.CountdownCause = CountdownCause.Schedule;
                    st.SessionForced = false;
                    PersistSession(st);
                    AddLog($"{st.Game.DisplayName} 进入禁止时段：立即开始 {Math.Max(1, _config.CountdownSeconds)} 秒倒计时，结束后强制关闭。");
                    EnqueueCountdown(name);
                    return;
                }

                EnterBlockedState(st, now, false, false);
                return;
            }

            // 空闲 / 休息期 / 配额用完：立即提示并关闭（程序在跑才会提示）
            EnterBlockedState(st, now, running, running);
        }

        /// <summary>切换到“禁止时段”状态并清理倒计时展示；kill=true 时强杀当前快照。</summary>
        private void EnterBlockedState(GameRuntimeState st, DateTime now, bool running, bool kill)
        {
            bool entering = st.Phase != MonitorPhase.BlockedBySchedule;
            bool killed = false;
            if (kill)
            {
                killed = KillSnapshot(st);
                DisposeTargets(st);
            }

            RemoveQueuedCountdown(st.Game.ProcessName);
            if (_showingProcess != null &&
                _showingProcess.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                _overlay.StopAndHide();
                _countdownShowing = false;
                _showingProcess = null;
            }

            st.Phase = MonitorPhase.BlockedBySchedule;
            st.RemindAt = null;
            st.CountdownEnd = null;
            st.CountdownCause = CountdownCause.None;
            st.SessionForced = false;
            st.IsRunning = false;

            if (killed)
            {
                UsageHistoryStore.NoteEndTime(st.Game.ProcessName, now);
                NoteEndReason(st.Game.ProcessName, "禁止时段", now);
            }

            if (entering)
                AddLog($"{st.Game.DisplayName} 进入禁止时段，匹配进程已关闭。");

            if (running && (now - st.LastScheduleNotify).TotalSeconds >= ScheduleNotifySeconds)
            {
                st.LastScheduleNotify = now;
                AddLog($"{st.Game.DisplayName} 处于禁止时段，已关闭匹配进程并显示提示。");
                _overlay.ShowBlockedBySchedule(st.Game.DisplayName);
                PromptSoundPlayer.Play(_config, PromptSoundKind.BlockedBySchedule);
            }

            if (entering) SyncOverlayState();
        }

        /// <summary>
        /// 开放时段：照常累计运行时长/用量、推进会话累计，但不弹窗、不强制关闭；
        /// 超时也不进入提醒/倒计时，离开开放时段后不补触发。
        /// </summary>
        private void HandleAllowPeriod(GameRuntimeState st, GameUsage usage, DateTime now,
            bool running, int elapsed)
        {
            string name = st.Game.ProcessName;

            if (!st.InAllowPeriod)
            {
                st.InAllowPeriod = true;
                AddLog($"{st.Game.DisplayName} 进入开放时段：照常计时，但不弹窗、不强制关闭。");

                if (st.Phase == MonitorPhase.Reminding || st.Phase == MonitorPhase.CountingDown ||
                    st.Phase == MonitorPhase.BlockedBySchedule ||
                    st.Phase == MonitorPhase.Cooldown || st.Phase == MonitorPhase.QuotaExhausted)
                {
                    st.RemindAt = null;
                    st.CountdownEnd = null;
                    st.Phase = MonitorPhase.Idle;
                    RemoveQueuedCountdown(name);
                    if (_showingProcess != null &&
                        _showingProcess.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        _overlay.StopAndHide();
                        _countdownShowing = false;
                        _showingProcess = null;
                    }
                    SyncOverlayState();
                }
            }

            if (!running)
            {
                st.IsRunning = false;
                HandleMissingProcess(st, now);
                return;
            }

            int roundLimitSeconds = GetRoundLimitSeconds(st, now.Date);
            int effectiveLimit = GetEffectiveDailyLimit(st.Game);
            int limitSeconds = effectiveLimit > 0 ? effectiveLimit * 60 : int.MaxValue;

            if (st.Phase != MonitorPhase.Running)
            {
                bool launching = st.Phase == MonitorPhase.Idle;
                st.Phase = MonitorPhase.Running;
                st.RemindAt = null;
                st.CountdownEnd = null;
                st.CountdownCause = CountdownCause.None;
                PersistSession(st);
                if (launching) NoteLaunchFromProcesses(name, st.TargetProcesses, now);
                AddLog($"{st.Game.DisplayName} 开放时段内启动，照常累计时长（不提示、不强杀）。");
            }

            st.IsRunning = true;
            st.SessionSeconds += elapsed;
            usage.UsedSeconds += elapsed;
            _config.MarkDirty();
            PersistSession(st);
            UsageHistoryStore.UpdateUsed(name, usage.UsedSeconds, now.Hour);

            bool overRun = st.SessionSeconds >= roundLimitSeconds;
            bool overQuota = usage.UsedSeconds >= limitSeconds;
            if (overRun || overQuota)
            {
                if (!st.SuppressOverrun)
                    AddLog($"{st.Game.DisplayName} 开放时段内已超出限制，按规则不弹窗、不强杀，继续计时。");
                st.SuppressOverrun = true;
            }
        }

        /// <summary>系统时间跳变检测：墙钟与单调时钟间隔差超过阈值时写 WARN 日志并标记状态（不阻断）。</summary>
        private void CheckClockJump(DateTime now)
        {
            long monoMs = _monoClock.ElapsedMilliseconds;
            if (_lastJumpCheckAt != DateTime.MinValue)
            {
                double monoDelta = (monoMs - _lastMonoMs) / 1000.0;
                double wallDelta = (now - _lastJumpCheckAt).TotalSeconds;
                if (monoDelta >= 0 && monoDelta <= 300)
                {
                    double drift = wallDelta - monoDelta;
                    if (Math.Abs(drift) > ClockJumpThresholdSeconds)
                    {
                        _clockJumpDetected = true;
                        AppLog.Warn($"检测到系统时间跳变：墙钟间隔 {wallDelta:F1}s，单调间隔 {monoDelta:F1}s，偏差 {drift:F1}s（阈值 ±{ClockJumpThresholdSeconds}s）；功能不阻断，仅记录。");
                        AddLog($"检测到系统时间跳变（偏差 {drift:+0.0;-0.0} 秒），已写入 WARN 日志。");
                    }
                }
            }
            _lastMonoMs = monoMs;
            _lastJumpCheckAt = now;
        }

        private void ResetBlockedState(GameRuntimeState st)
        {
            st.Phase = MonitorPhase.Idle;
            st.SessionSeconds = 0;
            st.SessionBonusSeconds = 0;
            st.CountdownCause = CountdownCause.None;
            _config.ResetSessionRecord(st.Game.ProcessName, st.Game.RunMinutes);
            st.RemindAt = null;
            st.CountdownEnd = null;
            st.LastBlockedNotify = DateTime.MinValue;
            st.BlockedByTotalLimit = false;
            st.SuppressOverrun = false;
            AddLog($"{st.Game.DisplayName} 配额限制已解除，恢复监控。");
        }

        /// <summary>跨天或用量被重置时回到 Idle，同时清掉间隔期记录；会话累计在跨天时归零。</summary>
        private void ResetDailyState(GameRuntimeState st, string key, string reason)
        {
            bool wasActive = st.Phase != MonitorPhase.Idle;
            st.Phase = MonitorPhase.Idle;
            st.CountdownEnd = null;
            st.RemindAt = null;
            st.LastBlockedNotify = DateTime.MinValue;
            st.BlockedByTotalLimit = false;
            st.InAllowPeriod = false;
            st.SuppressOverrun = false;
            st.SessionSeconds = 0;
            st.SessionBonusSeconds = 0;
            st.CountdownCause = CountdownCause.None;
            _config.ResetSessionRecord(st.Game.ProcessName, st.Game.RunMinutes);
            if (st.CooldownEnd.HasValue)
            {
                st.CooldownEnd = null;
                PersistCooldown(st.Game.ProcessName, null);
            }
            RemoveQueuedCountdown(key);
            UsageHistoryStore.UpdateUsed(st.Game.ProcessName, 0);
            if (wasActive)
            {
                AddLog($"{st.Game.DisplayName} {reason}");
                SyncOverlayState();
            }
        }

        /// <summary>
        /// 进程消失即回到空闲态：不再保留 30 秒宽限；但本次会话累计必须保留，
        /// 这样“关掉游戏再开”仍是同一会话、可用时间继续减少。
        /// 间隔期/配额状态与“进程是否在跑”无关，必须保持。
        /// </summary>
        private void HandleMissingProcess(GameRuntimeState st, DateTime now)
        {
            st.IsRunning = false;

            if (st.Phase == MonitorPhase.QuotaExhausted) return;

            if (st.Phase == MonitorPhase.Cooldown)
            {
                if (!st.CooldownEnd.HasValue || now >= st.CooldownEnd.Value)
                {
                    st.Phase = MonitorPhase.Idle;
                    st.CooldownEnd = null;
                    PersistCooldown(st.Game.ProcessName, null);
                    st.LastBlockedNotify = DateTime.MinValue;
                    // 清零时机①：间隔期结束时会话累计归零；休息期里加的“下一次单次加时”保留
                    st.SessionSeconds = 0;
                    _config.ResetSessionRecord(st.Game.ProcessName, st.Game.RunMinutes, resetBonus: false);
                    AddLog($"{st.Game.DisplayName} 间隔期结束，本次会话累计清零。");
                }
                return;
            }

            bool wasActive = st.Phase != MonitorPhase.Idle;
            if (wasActive)
            {
                PersistSession(st);
                st.SuppressOverrun = false;
                UsageHistoryStore.NoteEndTime(st.Game.ProcessName, now);
                AddLog($"{st.Game.DisplayName} 已退出，立即回到空闲态；本次会话已用继续累计，再开不会清零。");

                // 提醒/倒计时期间进程被关闭：同步收起提示浮层，避免继续显示上一次会话
                if (_showingProcess != null &&
                    _showingProcess.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    _overlay.StopAndHide();
                    _countdownShowing = false;
                    _showingProcess = null;
                }
            }

            st.Phase = MonitorPhase.Idle;
            st.LastMatchAt = null;
            st.RemindAt = null;
            st.CountdownEnd = null;
            st.CountdownCause = CountdownCause.None;
            RemoveQueuedCountdown(st.Game.ProcessName);

            if (wasActive) SyncOverlayState();
        }

        private void NotifyQuotaExhausted(GameRuntimeState st, DateTime now)
        {
            if ((now - st.LastBlockedNotify).TotalSeconds <= NotifyIntervalSeconds) return;
            st.LastBlockedNotify = now;

            if (st.BlockedByTotalLimit)
            {
                AddLog($"{st.Game.DisplayName} 今日总时长已用完，已关闭匹配进程并显示提示。");
                _overlay.ShowBlocked(st.Game.DisplayName, true);
            }
            else
            {
                AddLog($"{st.Game.DisplayName} 今日时长已用完，已关闭匹配进程并显示提示。");
                _overlay.ShowBlocked(st.Game.DisplayName);
            }
            PromptSoundPlayer.Play(_config, PromptSoundKind.Quota);
        }

        private void NotifyCooldown(GameRuntimeState st, DateTime now)
        {
            if ((now - st.LastBlockedNotify).TotalSeconds <= NotifyIntervalSeconds) return;
            st.LastBlockedNotify = now;

            int remaining = 0;
            if (st.CooldownEnd.HasValue)
                remaining = Math.Max(0, (int)Math.Ceiling((st.CooldownEnd.Value - now).TotalSeconds));

            AddLog($"{st.Game.DisplayName} 处于间隔期，剩余 {FormatDuration(TimeSpan.FromSeconds(remaining))}，已阻止启动。");
            _overlay.ShowCooldown(st.Game.DisplayName, remaining, st.CooldownEnd);
            PromptSoundPlayer.Play(_config, PromptSoundKind.Cooldown);
        }

        private static void NoteEndReason(string processName, string reason, DateTime? when = null)
        {
            UsageHistoryStore.NoteEndReason(processName, reason, when);
            UsageHistoryStore.SaveIfDirty();
        }

        /// <summary>启动时刻是否落在“今天”（用于判定今天是否存在真实的“进入运行”事件）。</summary>
        internal static bool IsLaunchToday(DateTime? start, DateTime now)
            => start.HasValue && start.Value.Date == now.Date;

        /// <summary>取匹配合集中最早的进程真实启动时间（本地时间），取不到返回 null。</summary>
        private static DateTime? EarliestProcessStart(List<Process> procs)
        {
            DateTime? earliest = null;
            if (procs == null) return earliest;
            foreach (var p in procs)
            {
                try
                {
                    DateTime t = p.StartTime;
                    if (earliest == null || t < earliest.Value) earliest = t;
                }
                catch { }
            }
            return earliest;
        }

        /// <summary>
        /// 记录一次真实启动：使用进程真实启动时间而非检测时刻；
        /// 昨天（或更早）启动并延续到今天的进程，今天没有“进入运行”事件，不写今天的首次开启。
        /// </summary>
        private static void NoteLaunchFromProcesses(string processName, List<Process> procs, DateTime now)
        {
            DateTime? start = EarliestProcessStart(procs);
            if (start == null)
            {
                UsageHistoryStore.NoteLaunch(processName, now);
                return;
            }
            if (!IsLaunchToday(start, now)) return;
            UsageHistoryStore.NoteLaunch(processName, start.Value);
        }

        private static string DescribeQuota(int effectiveLimit)
            => effectiveLimit > 0 ? effectiveLimit.ToString() : "沿用总限额/不限";

        private static string FormatDuration(TimeSpan span)
        {
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            int totalMinutes = (int)Math.Ceiling(span.TotalMinutes);
            if (totalMinutes <= 0) return "不到 1 分钟";
            return $"{totalMinutes} 分钟";
        }

        private CooldownState GetPersistedCooldown(string processName)
        {
            return _config.Cooldowns.FirstOrDefault(c =>
                c != null && c.ProcessName != null &&
                c.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>写入/清除间隔期，并立即落盘（防止重启绕过休息期）。</summary>
        private void PersistCooldown(string processName, DateTime? endTime)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;

            var entry = GetPersistedCooldown(processName);
            if (endTime.HasValue)
            {
                if (entry == null)
                {
                    entry = new CooldownState { ProcessName = processName };
                    _config.Cooldowns.Add(entry);
                }
                if (entry.EndTime == endTime.Value) return;
                entry.EndTime = endTime.Value;
            }
            else
            {
                if (entry == null) return;
                _config.Cooldowns.Remove(entry);
            }

            _config.SaveQuiet();
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

        /// <summary>条目不再持有快照的“所有权”：只清空引用，Process 对象由 tick 快照统一释放。</summary>
        private static void DisposeTargets(GameRuntimeState st)
        {
            if (st == null) return;
            st.TargetProcesses.Clear();
            st.TargetWindows.Clear();
        }

        /// <summary>释放上一 tick 的进程快照（唯一所有者），避免多条监控项重复 Dispose 共享对象。</summary>
        private void DisposeTickProcesses()
        {
            var procs = _tickProcesses;
            _tickProcesses = Array.Empty<Process>();
            if (procs == null) return;
            foreach (var p in procs)
            {
                try { p.Dispose(); } catch { }
            }
        }

        /// <summary>同一进程同时命中多个条目时：按配置顺序归先匹配的条目，后者跳过；日志限频 60 秒。</summary>
        private void LogOwnershipConflict(GameItem later, Process process)
        {
            if (later == null || process == null) return;
            try
            {
                int pid = process.Id;
                string key = later.ProcessName + "#" + pid;
                var now = DateTime.Now;
                if (_ownershipLogAt.TryGetValue(key, out var last) &&
                    (now - last).TotalSeconds < 60) return;
                _ownershipLogAt[key] = now;
                if (_ownershipLogAt.Count > 200) _ownershipLogAt.Clear();
                AppLog.Info($"[归属] 进程 {process.ProcessName}(pid={pid}) 同时命中多个条目，按配置顺序归更靠前的条目；「{later.DisplayName}」本轮跳过该进程。");
            }
            catch { }
        }

        private static bool IsProcessNameHit(GameItem game, Process process)
        {
            if (game == null || process == null) return false;
            try
            {
                string name = process.ProcessName ?? "";
                if (name.Length == 0 || ProcessMatcher.IsProtectedProcess(name)) return false;
                if (game.MatchMode == MatchMode.ExactWindowTitle) return false;
                // 条目标识（ProcessName）只是显示名/稳定 id，真正的匹配依据是关键字列表
                foreach (string raw in game.KeywordList)
                {
                    string keyword = (raw ?? "").Trim();
                    if (keyword.Length < MinKeywordLength) continue;
                    if (game.MatchMode == MatchMode.ExactProcess)
                    {
                        if (name.Equals(keyword, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    else if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// 结束本轮快照：
        /// - 进程名命中 → 立即强杀整个进程；
        /// - 仅标题命中 → 给命中的顶层窗口发 WM_CLOSE，并进入 10 秒宽限（窗口关掉不杀，关不掉才杀）。
        /// </summary>
        private bool KillSnapshot(GameRuntimeState st)
        {
            bool acted = false;
            var nameHitPids = new HashSet<int>();

            foreach (var p in st.TargetProcesses)
            {
                try
                {
                    if (!IsProcessNameHit(st.Game, p)) continue;
                    nameHitPids.Add(p.Id);
                    if (!p.HasExited)
                    {
                        p.Kill();
                        acted = true;
                    }
                }
                catch { }
            }

            foreach (var w in st.TargetWindows)
            {
                if (w == null || nameHitPids.Contains(w.ProcessId)) continue;
                if (!ProcessMatcher.IsMatchedWindowAlive(w, st.Game)) continue;

                PendingWindowClose pending;
                bool isNew = !st.PendingWindowCloses.TryGetValue(w.ProcessId, out pending);
                if (isNew)
                {
                    pending = new PendingWindowClose
                    {
                        Title = w.Title,
                        ProcessName = w.ProcessName,
                        Deadline = DateTime.Now.AddSeconds(WindowCloseGraceSeconds)
                    };
                    st.PendingWindowCloses[w.ProcessId] = pending;
                }

                if (pending.RequestedHandles.Contains((long)w.Handle)) continue;

                ProcessMatcher.RequestWindowClose(w);
                pending.RequestedHandles.Add((long)w.Handle);
                pending.Title = w.Title;
                pending.Deadline = DateTime.Now.AddSeconds(WindowCloseGraceSeconds);
                acted = true;
                AppLog.Info($"已请求关闭窗口「{w.Title}」（仅标题命中，进程 {w.ProcessName}，pid={w.ProcessId}）");
                AddLog($"{st.Game.DisplayName} 已请求关闭窗口「{w.Title}」（进程 {w.ProcessName}）。");
            }

            return acted;
        }

        /// <summary>
        /// 处理“仅标题命中”的待关闭窗口：
        /// ① 出现新的命中窗口 → 重新发 WM_CLOSE 并重新计时（避免刚关又开被立刻强杀）；
        /// ② 命中窗口已消失 → 只记 INFO（进程仍在后台也不杀），随后条目按“未运行”回落；
        /// ③ 宽限期到仍存在 → 强杀整个进程。
        /// </summary>
        private void ProcessPendingWindowCloses(GameRuntimeState st, DateTime now)
        {
            if (st == null || st.PendingWindowCloses.Count == 0) return;

            foreach (var kv in st.PendingWindowCloses.ToList())
            {
                int pid = kv.Key;
                var pending = kv.Value;

                var current = st.TargetWindows.FindAll(w => w.ProcessId == pid);
                var fresh = current.FindAll(w =>
                    !pending.RequestedHandles.Contains((long)w.Handle) &&
                    ProcessMatcher.IsMatchedWindowAlive(w, st.Game));

                if (fresh.Count > 0)
                {
                    foreach (var w in fresh)
                    {
                        ProcessMatcher.RequestWindowClose(w);
                        pending.RequestedHandles.Add((long)w.Handle);
                    }
                    pending.Title = fresh[0].Title;
                    pending.Deadline = now.AddSeconds(WindowCloseGraceSeconds);
                    AppLog.Info($"标题命中窗口「{pending.Title}」重新出现，已重新请求关闭并重置 {WindowCloseGraceSeconds} 秒宽限（进程 {pending.ProcessName}，pid={pid}）");
                    AddLog($"{st.Game.DisplayName} 命中窗口重新出现，已重新请求关闭并重置 {WindowCloseGraceSeconds} 秒宽限。");
                    continue;
                }

                bool stillAlive = current.Exists(w => ProcessMatcher.IsMatchedWindowAlive(w, st.Game));
                if (!stillAlive)
                {
                    if (!pending.BackgroundLogged)
                    {
                        pending.BackgroundLogged = true;
                        string text = $"标题命中窗口「{pending.Title}」已关闭，进程 {pending.ProcessName}(pid={pid}) 仍在后台运行，未强制结束";
                        AppLog.Info(text);
                        AddLog(st.Game.DisplayName + " " + text);
                    }
                    st.PendingWindowCloses.Remove(pid);
                    continue;
                }

                if (now >= pending.Deadline)
                {
                    try
                    {
                        using (var p = Process.GetProcessById(pid))
                        {
                            p.Kill();
                        }
                        string text = $"标题命中窗口「{pending.Title}」关闭失败，已强制结束进程 {pending.ProcessName}（pid={pid}）";
                        AppLog.Info(text);
                        AddLog(st.Game.DisplayName + " " + text);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn($"结束进程 {pending.ProcessName}(pid={pid}) 失败", ex);
                    }
                    st.PendingWindowCloses.Remove(pid);
                }
            }
        }

        /// <summary>
        /// 检测今日用量是否被重置为 0，如果是则立刻取消提醒/倒计时/配额状态。
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
                    if (st.Phase != MonitorPhase.CountingDown &&
                        st.Phase != MonitorPhase.Reminding &&
                        st.Phase != MonitorPhase.QuotaExhausted)
                        continue;

                    var usage = _config.Usage.FirstOrDefault(x =>
                        x.ProcessName.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int used = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds : 0;
                    if (used == 0)
                    {
                        st.Phase = st.IsRunning ? MonitorPhase.Running : MonitorPhase.Idle;
                        st.SessionSeconds = 0;
                        st.SessionBonusSeconds = 0;
                        st.CountdownCause = CountdownCause.None;
                        st.SuppressOverrun = false;
                        _config.ResetSessionRecord(st.Game.ProcessName, st.Game.RunMinutes);
                        st.CountdownEnd = null;
                        st.RemindAt = null;
                        st.LastBlockedNotify = DateTime.MinValue;
                        AddLog($"{st.Game.DisplayName} 今日用时已重置，自动取消提醒/倒计时/配额状态。");
                        changed = true;
                    }
                }
            }

            if (changed) SyncOverlayState();
        }

        /// <summary>
        /// 清空“今天以前”的历史行（Date &lt; 今天）：今天这一行及其小时桶、首次开启/末次关闭、
        /// 启动次数、结束原因、是否进过间隔期全部原样保留；今日已用/配额/会话累计/运行态一律不动。
        /// 返回被删除的历史行数（只统计今天以前的行）。
        /// </summary>
        public int ClearAllUsage()
        {
            int removed = UsageHistoryStore.ClearBeforeToday();

            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string text = $"已清空今天以前的使用记录：删除 {removed} 条历史行；今天的数据与配额保持不变（{stamp}）。";
            AddLog(text);
            AppLog.Info(text);
            return removed;
        }

        /// <summary>
        /// 手动“重置今日用量”时一并清零该条目的会话累计（清零时机③）。
        /// </summary>
        public void ResetEntrySession(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;

            lock (_config.SyncRoot)
            {
                var game = FindGame(processName);
                var st = _states.Values.FirstOrDefault(s =>
                    s.Game.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
                if (st != null)
                {
                    st.SessionSeconds = 0;
                    st.SessionBonusSeconds = 0;
                    st.SuppressOverrun = false;
                }
                _config.ResetSessionRecord(processName, game != null ? game.RunMinutes : (st != null ? st.Game.RunMinutes : 1));
                _config.SaveQuiet();
            }
            AddLog($"已重置「{processName}」的今日用量与本次会话累计。");
        }

        private static int DescribeSessionRemain(GameRuntimeState st, int roundLimitSeconds)
        {
            int remainSeconds = Math.Max(0, roundLimitSeconds - (st == null ? 0 : st.SessionSeconds));
            int minutes = (int)Math.Ceiling(remainSeconds / 60.0);
            return Math.Min(minutes, Math.Max(1, roundLimitSeconds / 60));
        }

        /// <summary>
        /// “结束本次”：把本轮已用时长直接置为单次上限，立即进入提醒 → 倒计时 → 强杀。
        /// 若条目当前未运行，则标记 SessionForced，下一次启动立即进入该流程并给出提示。
        /// </summary>
        public bool ForceExhaustSession(string processName)
        {
            var st = _states.Values.FirstOrDefault(s =>
                s.Game.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

            if (st == null) return false;
            if (st.Phase == MonitorPhase.CountingDown ||
                st.Phase == MonitorPhase.Cooldown ||
                st.Phase == MonitorPhase.QuotaExhausted ||
                st.Phase == MonitorPhase.BlockedBySchedule)
                return false;

            DateTime now = DateTime.Now;
            int roundLimitSeconds = GetRoundLimitSeconds(st, now.Date);
            st.SessionForced = true;

            if (!st.IsRunning)
            {
                st.Phase = MonitorPhase.Idle;
                // 本次视为已用完：会话累计直接置为本轮上限（含加时）
                st.SessionSeconds = roundLimitSeconds;
                PersistSession(st);
                AddLog($"{st.Game.DisplayName} 本次已结束（未运行），下次启动将立即进入提醒/倒计时。");
                _overlay.ShowNotice("已结束本次", "下次启动将立即进入提醒与倒计时。", 4000, PromptKind.Reminding);
                return true;
            }

            st.SessionSeconds = roundLimitSeconds;
            PersistSession(st);
            st.Phase = MonitorPhase.Reminding;
            st.RemindAt = now;
            st.CountdownCause = CountdownCause.None;
            AddLog($"{st.Game.DisplayName} 本次已结束，本轮视为已用完（{DescribeRoundLimit(roundLimitSeconds)}），立即进入提醒。");
            _overlay.ShowNotice("已结束本次", "本轮立即结束，随后进入倒计时。", 3500, PromptKind.Reminding);
            ReminderRequested?.Invoke(st.Game);
            PromptSoundPlayer.Play(_config, PromptSoundKind.Grace);
            return true;
        }

        public List<GameStatus> GetStatuses()
        {
            var result = new List<GameStatus>();
            DateTime today = DateTime.Today;
            DateTime now = DateTime.Now;

            bool paused = IsPaused(now);
            int pauseRemaining = paused
                ? Math.Max(0, (int)Math.Ceiling((_config.PauseUntil - now).TotalSeconds))
                : 0;
            int totalUsed = GetTotalUsedSecondsToday();
            int totalLimit = GetDailyTotalLimitMinutes();
            bool totalExceeded = IsTotalQuotaExceeded(totalUsed, totalLimit);
            ScheduleDecision schedule = ScheduleRules.Evaluate(_config, now);

            lock (_config.SyncRoot)
            {
                foreach (var kv in _states)
                {
                    var st = kv.Value;
                    var usage = _config.Usage.FirstOrDefault(x =>
                        x.ProcessName.Equals(st.Game.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int used = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds : 0;

                    int effectiveLimit = GetEffectiveDailyLimit(st.Game);
                    int limitSeconds = effectiveLimit > 0 ? effectiveLimit * 60 : int.MaxValue;
                    int effectiveRun = GetEffectiveRunMinutes(st.Game, today);
                    int roundLimitSeconds = GetRoundLimitSeconds(st, today);
                    double multiplier = GetWeekendMultiplier(st.Game, today);

                    int cooldownRemaining = 0;
                    DateTime? cooldownEnd = null;
                    if (st.Phase == MonitorPhase.Cooldown && st.CooldownEnd.HasValue)
                    {
                        cooldownEnd = st.CooldownEnd;
                        cooldownRemaining = Math.Max(0, (int)Math.Ceiling((st.CooldownEnd.Value - now).TotalSeconds));
                    }

                    // 宽限/倒计时剩余秒数：文案需要，缺失时为 0（显示无数字文案）
                    int remindRemaining = 0;
                    if (st.Phase == MonitorPhase.Reminding && st.RemindAt.HasValue)
                    {
                        double graceTotal = Math.Max(0, _config.ReminderMinutes * 60.0);
                        remindRemaining = Math.Max(0, (int)Math.Ceiling(graceTotal - (now - st.RemindAt.Value).TotalSeconds));
                    }
                    int countdownRemaining = 0;
                    if (st.Phase == MonitorPhase.CountingDown && st.CountdownEnd.HasValue)
                    {
                        countdownRemaining = Math.Max(0, (int)Math.Ceiling((st.CountdownEnd.Value - now).TotalSeconds));
                    }

                    result.Add(new GameStatus
                    {
                        Game = st.Game,
                        UsedSecondsToday = used,
                        IsRunning = st.IsRunning && !paused,
                        Phase = st.Phase,
                        SessionSeconds = st.SessionSeconds,
                        CooldownRemainingSeconds = cooldownRemaining,
                        CooldownEnd = cooldownEnd,
                        RemindRemainingSeconds = remindRemaining,
                        CountdownRemainingSeconds = countdownRemaining,
                        QuotaExhausted = IsEntryQuotaBlocked(st, usage, effectiveLimit, limitSeconds, totalExceeded, today),
                        EffectiveLimitMinutes = effectiveLimit,
                        BonusMinutes = _config.EntryBonusMinutesFor(st.Game, today),
                        SessionBonusSeconds = st.SessionBonusSeconds,
                        RoundLimitMinutes = Math.Max(0, (int)Math.Ceiling(roundLimitSeconds / 60.0)),
                        RoundRemainingMinutes = Math.Max(0,
                            (int)Math.Ceiling(Math.Max(0, roundLimitSeconds - st.SessionSeconds) / 60.0)),
                        TotalUsedSecondsToday = totalUsed,
                        TotalLimitExceeded = totalExceeded,
                        EffectiveRunMinutes = effectiveRun,
                        WeekendMultiplier = multiplier,
                        SessionForced = st.SessionForced,
                        IsPaused = paused,
                        PauseRemainingSeconds = pauseRemaining,
                        InBlockedPeriod = schedule.Blocked,
                        InAllowedPeriod = schedule.InAllowedPeriod,
                        InAllowRulePeriod = schedule.HasAllowRules && schedule.InAllowedPeriod,
                        ScheduleSummary = schedule.Summary,
                        ClockJumpDetected = _clockJumpDetected
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
            if (killed)
            {
                DateTime now = DateTime.Now;
                UsageHistoryStore.NoteEndTime(processName, now);
                AddLog($"{st.Game.DisplayName} 已被手动结束。");
                NoteEndReason(processName, "手动结束", now);
            }
            return killed;
        }

        public void Dispose()
        {
            _timer?.Stop(); _timer?.Dispose();
            _saveTimer?.Stop(); _saveTimer?.Dispose();
            foreach (var st in _states.Values) DisposeTargets(st);
            DisposeTickProcesses();
            _config.SaveQuiet();
            UsageHistoryStore.SaveNow();
        }

        private void AddLog(string msg)
        {
            try { Log?.Invoke(msg); } catch { }
        }
    }
}
