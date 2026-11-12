using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameMonitor
{
    /// <summary>一个可见的顶层窗口：句柄、标题、所属 PID 与进程名（用于日志/关闭请求）。</summary>
    public sealed class WindowInfo
    {
        public IntPtr Handle;
        public string Title = "";
        public int ProcessId;
        public string ProcessName = "";
        /// <summary>命中该窗口的关键字（窗口存活判定时用于标题比对，避免 hwnd 复用误判）。</summary>
        public string MatchedKeyword = "";
    }

    /// <summary>单个进程对一个条目的匹配结果：进程名命中 / 标题命中的窗口集合。</summary>
    public sealed class MatchResult
    {
        public bool ProcessNameHit;
        public List<WindowInfo> Windows = new List<WindowInfo>();
        /// <summary>本轮第一个命中的关键字（按配置顺序）。</summary>
        public string MatchedKeyword = "";
        public bool Any => ProcessNameHit || Windows.Count > 0;
    }

    /// <summary>一次 tick 内一个条目的匹配集合（同名进程可能被多个条目共用，由调用方统一释放）。</summary>
    public sealed class ProcessMatchSet
    {
        public List<Process> Processes = new List<Process>();
        public List<Process> ProcessNameHits = new List<Process>();
        public List<WindowInfo> Windows = new List<WindowInfo>();
        /// <summary>被更靠前的条目“独占”后跳过的进程（仅用于限频日志）。</summary>
        public List<Process> SkippedProcesses = new List<Process>();
    }

    /// <summary>
    /// 全局顶层窗口标题快照。
    /// 只用一次 EnumWindows 扫描（默认每秒刷新一次，且在后台线程执行），
    /// 按 PID 分组后缓存；扫描前先用进程名黑名单过滤候选 PID，避免为系统进程收集窗口。
    /// 这样既能识别“同一进程的多个窗口”，又不会让 UI 线程每秒做一次全量枚举。
    /// </summary>
    public sealed class WindowTitleSnapshot
    {
        private static readonly object CacheLock = new object();
        /// <summary>缓存最大陈旧时间：超过则即使后台刷新线程失效也同步重建。</summary>
        private static readonly TimeSpan MaxStaleness = TimeSpan.FromSeconds(3);

        private static WindowTitleSnapshot _cached;
        private static DateTime _cachedAt = DateTime.MinValue;
        private static System.Threading.Timer _refresher;
        private static bool _refresherStarted;
        private static bool _building;

        private readonly Dictionary<int, List<WindowInfo>> _windowsByPid;

        private WindowTitleSnapshot(Dictionary<int, List<WindowInfo>> windowsByPid)
        {
            _windowsByPid = windowsByPid;
        }

        public int Count => _windowsByPid.Count;

        public IReadOnlyList<WindowInfo> GetWindows(int processId)
            => _windowsByPid.TryGetValue(processId, out var list) ? list : (IReadOnlyList<WindowInfo>)Array.Empty<WindowInfo>();

        /// <summary>取当前快照（后台每秒刷新；最长陈旧 3 秒）。首次调用会同步构建一次。</summary>
        public static WindowTitleSnapshot GetCurrent()
        {
            DateTime now = DateTime.UtcNow;
            WindowTitleSnapshot cached;
            bool stale;
            lock (CacheLock)
            {
                cached = _cached;
                stale = cached == null || now - _cachedAt > MaxStaleness;
            }

            // 后台刷新线程可能被阻塞/失效（极端情况），这里按最大陈旧时间兜底同步重建，
            // 保证标题匹配不会长时间使用旧窗口集合。
            if (stale)
            {
                var built = Build();
                lock (CacheLock)
                {
                    _cached = built;
                    _cachedAt = DateTime.UtcNow;
                }
                cached = built;
            }

            if (!_refresherStarted)
            {
                lock (CacheLock)
                {
                    if (!_refresherStarted)
                    {
                        _refresherStarted = true;
                        EnsureRefreshLoop();
                    }
                }
            }
            return cached;
        }

        /// <summary>后台 1 秒刷新循环：UI 线程只读缓存，不承担窗口枚举开销。</summary>
        private static void EnsureRefreshLoop()
        {
            if (_refresher != null) return;
            _refresher = new System.Threading.Timer(_ => RefreshInBackground(), null, 1000, 1000);
        }

        private static void RefreshInBackground()
        {
            lock (CacheLock)
            {
                if (_building) return;
                _building = true;
            }

            try
            {
                var snapshot = Build();
                lock (CacheLock)
                {
                    _cached = snapshot;
                    _cachedAt = DateTime.UtcNow;
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("刷新窗口标题快照失败", ex);
            }
            finally
            {
                lock (CacheLock) { _building = false; }
            }
        }

        private static WindowTitleSnapshot Build()
        {
            // 1) 先拿 进程ID → 进程名 映射，并按内置黑名单过滤出候选 PID
            var candidates = new Dictionary<int, string>();
            var procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        int pid = p.Id;
                        string name = p.ProcessName;
                        if (string.IsNullOrEmpty(name)) continue;
                        if (ProcessMatcher.IsProtectedProcess(name)) continue;
                        candidates[pid] = name;
                    }
                    catch
                    {
                        // 进程可能在枚举过程中退出
                    }
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }

            // 2) 只给候选 PID 收集窗口（可见、有标题、非零尺寸的顶层窗口）
            var map = new Dictionary<int, List<WindowInfo>>();
            try
            {
                EnumWindows((hwnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hwnd)) return true;

                        int len = GetWindowTextLength(hwnd);
                        if (len <= 0) return true;

                        var sb = new StringBuilder(len + 1);
                        if (!GetWindowText(hwnd, sb, sb.Capacity)) return true;
                        string title = sb.ToString().Trim();
                        if (title.Length == 0) return true;

                        if (!GetWindowRect(hwnd, out RECT rect)) return true;
                        if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0) return true;

                        int pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        if (pid <= 0 || !candidates.ContainsKey(pid)) return true;

                        if (!map.TryGetValue(pid, out var list))
                        {
                            list = new List<WindowInfo>();
                            map[pid] = list;
                        }
                        if (!list.Any(w => w.Title == title && w.Handle == hwnd))
                        {
                            list.Add(new WindowInfo
                            {
                                Handle = hwnd,
                                Title = title,
                                ProcessId = pid,
                                ProcessName = candidates[pid]
                            });
                        }
                    }
                    catch
                    {
                        // 单个窗口失败不影响整体扫描
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLog.Warn("枚举顶层窗口失败", ex);
            }

            var result = new Dictionary<int, List<WindowInfo>>(map.Count);
            foreach (var kv in map)
                result[kv.Key] = kv.Value;
            return new WindowTitleSnapshot(result);
        }

        #region P/Invoke

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

        #endregion
    }

    /// <summary>
    /// 全程序唯一的进程匹配实现：监控判定、/api/status、桌面悬浮框、设置页检测都必须走这里，
    /// 避免各处各写一份“只认 MainWindowTitle”的旧逻辑。
    /// </summary>
    public static class ProcessMatcher
    {
        /// <summary>WM_CLOSE：请求窗口正常关闭（不结束进程）。</summary>
        public const uint WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static readonly HashSet<string> ProtectedProcessNames =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "GameMonitor",
                "explorer", "winlogon", "csrss", "smss", "lsass", "services", "svchost",
                "wininit", "dwm", "System", "Idle", "Registry", "taskhostw", "sihost",
                "fontdrvhost", "runtimebroker", "searchhost", "StartMenuExperienceHost",
                "shellexperiencehost", "SecurityHealthService", "MsMpEng"
            };

        public static bool IsProtectedProcess(string processName)
            => !string.IsNullOrEmpty(processName) && ProtectedProcessNames.Contains(processName);

        /// <summary>按 MatchMode 统一判定（保留旧布尔接口，供设置页/查询运行状态使用）。</summary>
        public static bool Matches(GameItem game, Process process, WindowTitleSnapshot snapshot)
            => Match(game, process, snapshot).Any;

        /// <summary>
        /// 统一匹配：返回“进程名是否命中”与“命中的顶层窗口集合”。
        /// - ExactProcess：只认进程名；
        /// - ExactWindowTitle：只认窗口标题；
        /// - Contains：进程名包含或任一窗口标题包含。
        /// </summary>
        public static MatchResult Match(GameItem game, Process process, WindowTitleSnapshot snapshot)
        {
            var result = new MatchResult();
            if (game == null || process == null || snapshot == null) return result;

            var keywords = game.KeywordList;
            if (keywords.Count == 0) return result;

            string procName;
            int pid;
            try
            {
                procName = process.ProcessName ?? "";
                pid = process.Id;
            }
            catch { return result; }

            if (procName.Length == 0 || IsProtectedProcess(procName)) return result;

            // 按配置顺序逐个关键字匹配：同一轮内先命中即停，不锁定、不记忆
            foreach (var keyword in keywords)
            {
                if (keyword.Length < GameMonitor.MinKeywordLength) continue;

                if (game.MatchMode != MatchMode.ExactWindowTitle)
                {
                    bool keywordInName = procName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool nameHit = game.MatchMode == MatchMode.ExactProcess
                        ? procName.Equals(keyword, StringComparison.OrdinalIgnoreCase)
                        : keywordInName;
                    if (nameHit)
                    {
                        result.ProcessNameHit = true;
                        result.MatchedKeyword = keyword;
                        return result;
                    }
                }

                if (game.MatchMode != MatchMode.ExactProcess)
                {
                    var hits = CollectWindowHits(game, snapshot, pid, keyword, procName,
                        exact: game.MatchMode == MatchMode.ExactWindowTitle);
                    if (hits.Count > 0)
                    {
                        result.Windows = hits;
                        result.MatchedKeyword = keyword;
                        return result;
                    }
                }
            }

            return result;
        }

        private static List<WindowInfo> CollectWindowHits(GameItem game, WindowTitleSnapshot snapshot,
            int pid, string keyword, string procName, bool exact)
        {
            var hits = new List<WindowInfo>();
            foreach (var w in snapshot.GetWindows(pid))
            {
                bool hit = exact
                    ? w.Title.Equals(keyword, StringComparison.OrdinalIgnoreCase)
                    : w.Title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) continue;
                hits.Add(new WindowInfo
                {
                    Handle = w.Handle,
                    Title = w.Title,
                    ProcessId = w.ProcessId,
                    ProcessName = procName,
                    MatchedKeyword = keyword
                });
            }
            return hits;
        }

        /// <summary>
        /// 详细匹配：同时返回命中进程、进程名命中进程、命中窗口。
        /// claimedPids 非空时，被更靠前条目独占的进程会跳过并记入 SkippedProcesses
        /// （同一进程同时命中多个条目时按配置顺序归先者）。
        /// </summary>
        public static ProcessMatchSet FindMatchesDetailed(GameItem game, Process[] processes,
            ISet<int> claimedPids = null)
        {
            var set = new ProcessMatchSet();
            if (game == null || processes == null) return set;

            var snapshot = WindowTitleSnapshot.GetCurrent();
            foreach (var p in processes)
            {
                try
                {
                    int pid = p.Id;
                    if (claimedPids != null && claimedPids.Contains(pid))
                    {
                        // 只有“本来会命中”的进程才需要记录冲突，避免把所有进程都当成跳过
                        if (Match(game, p, snapshot).Any) set.SkippedProcesses.Add(p);
                        continue;
                    }

                    var m = Match(game, p, snapshot);
                    if (!m.Any) continue;
                    set.Processes.Add(p);
                    if (m.ProcessNameHit) set.ProcessNameHits.Add(p);
                    if (m.Windows.Count > 0) set.Windows.AddRange(m.Windows);
                    claimedPids?.Add(pid);
                }
                catch
                {
                    // 进程在判定过程中退出，忽略
                }
            }
            return set;
        }

        /// <summary>
        /// 判断“当时命中的那个窗口”是否仍然存在：句柄有效 + 可见 + 标题仍匹配。
        /// 通过标题比对规避 hwnd 被系统复用到别的窗口（复用后标题不匹配即视为已消失）。
        /// </summary>
        public static bool IsMatchedWindowAlive(WindowInfo window, GameItem game)
        {
            if (window == null || window.Handle == IntPtr.Zero || game == null) return false;
            try
            {
                if (!IsWindow(window.Handle)) return false;
                if (!IsWindowVisible(window.Handle)) return false;
                int len = GetWindowTextLength(window.Handle);
                if (len <= 0) return false;

                var sb = new StringBuilder(len + 1);
                if (!GetWindowText(window.Handle, sb, sb.Capacity)) return false;
                string title = sb.ToString().Trim();
                if (title.Length == 0) return false;

                string keyword = (string.IsNullOrEmpty(window.MatchedKeyword)
                    ? (game.KeywordList.FirstOrDefault() ?? "")
                    : window.MatchedKeyword).Trim();
                if (keyword.Length == 0) return false;
                return game.MatchMode == MatchMode.ExactWindowTitle
                    ? title.Equals(keyword, StringComparison.OrdinalIgnoreCase)
                    : title.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>向命中的顶层窗口发送 WM_CLOSE（只关窗口，不杀进程）。</summary>
        public static bool RequestWindowClose(WindowInfo window)
        {
            if (window == null || window.Handle == IntPtr.Zero) return false;
            try { return PostMessage(window.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero); }
            catch { return false; }
        }

        /// <summary>
        /// 一次性批量查询多个游戏是否在运行（设置页 / 网页用，避免每个游戏各枚举一遍进程）。
        /// </summary>
        public static bool[] QueryRunning(IList<GameItem> games)
        {
            int count = games?.Count ?? 0;
            var result = new bool[count];
            if (count == 0) return result;

            var snapshot = WindowTitleSnapshot.GetCurrent();
            var procs = Process.GetProcesses();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var game = games[i];
                    if (game == null || string.IsNullOrWhiteSpace(game.ProcessName)) continue;

                    foreach (var p in procs)
                    {
                        try
                        {
                            if (Matches(game, p, snapshot))
                            {
                                result[i] = true;
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return result;
        }
    }
}
