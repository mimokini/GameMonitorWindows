using System.Xml.Serialization;

namespace GameMonitor
{
    [Serializable]
    public class UsageDayRecord
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public string ProcessName { get; set; } = "";
        public int UsedSeconds { get; set; }
        public int LaunchCount { get; set; }
        /// <summary>最后一次结束原因：时长到 / 配额到 / 手动结束。</summary>
        public string LastEndReason { get; set; } = "";
        public bool WasInCooldown { get; set; }
        public int BonusMinutes { get; set; }
        /// <summary>按小时累计的“秒”桶（0~23 时，索引=小时）；旧版本记录为 null。展示时换算为分钟。</summary>
        public int[] HourlySeconds { get; set; }
        /// <summary>该记录是否包含小时桶数据（旧版本记录为 false，图表显示“此前版本无小时数据”）。</summary>
        public bool HasHourlyData { get; set; }

        /// <summary>当天第一次检测到该条目启动的时间（HH:mm:ss）；旧数据为空字符串，展示为“—”。</summary>
        public string FirstStartTime { get; set; } = "";

        /// <summary>当天最后一次归零/关闭的时间（HH:mm:ss）；跨天时记为 23:59:59；旧数据为空。</summary>
        public string LastEndTime { get; set; } = "";
    }

    /// <summary>按天的使用记录（与 config 解耦），保留最近 14 天。</summary>
    [XmlRoot("UsageHistory")]
    public class UsageHistoryFile
    {
        [XmlElement("Day")]
        public List<UsageDayRecord> Days { get; set; } = new List<UsageDayRecord>();
    }

    public class UsageDailySummary
    {
        public DateTime Date { get; set; }
        public int UsedMinutes { get; set; }
        public int LaunchCount { get; set; }
        public string LastEndReason { get; set; } = "";
        public bool WasInCooldown { get; set; }
        public int BonusMinutes { get; set; }
        /// <summary>当日 0~23 时各小时的使用秒数（聚合所有条目）；展示时换算为分钟。</summary>
        public int[] HourlySeconds { get; set; } = new int[24];
        /// <summary>当日是否有小时桶数据；旧版本记录的这一天为 false。</summary>
        public bool HasHourlyData { get; set; }

        /// <summary>当天所有条目中最早的启动时间（HH:mm:ss）；无数据时为空。</summary>
        public string FirstStartTime { get; set; } = "";

        /// <summary>当天所有条目中最晚的归零/关闭时间（HH:mm:ss）；无数据时为空。</summary>
        public string LastEndTime { get; set; } = "";

        /// <summary>这一天是否有任何可用数据；没有则明细表不显示该行（清空后只留今天有数据的一行）。</summary>
        public bool HasData => UsedMinutes > 0 || LaunchCount > 0 || BonusMinutes > 0 ||
                               WasInCooldown || HasHourlyData ||
                               !string.IsNullOrEmpty(FirstStartTime) || !string.IsNullOrEmpty(LastEndTime);
    }

    /// <summary>
    /// 使用记录存储：%LOCALAPPDATA%\GameMonitor\usage-history.xml。
    /// 与 config.xml 解耦，临时文件 + File.Replace 原子写；所有方法线程安全。
    /// </summary>
    public static class UsageHistoryStore
    {
        public const int RetentionDays = 14;

        private static readonly object _lock = new object();
        private static List<UsageDayRecord> _days;
        private static bool _dirty;
        /// <summary>测试用存储路径覆盖；正式运行保持 null，始终使用 %LOCALAPPDATA%\GameMonitor。</summary>
        private static string _testFilePath;

        public static string FilePath
            => _testFilePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameMonitor", "usage-history.xml");

        public static string DirectoryPath => Path.GetDirectoryName(FilePath);

        /// <summary>仅供自动化测试：切换存储路径并重置内存缓存，正式运行不会调用。</summary>
        internal static void SetStoragePathForTests(string path)
        {
            lock (_lock)
            {
                _testFilePath = path;
                _days = null;
                _dirty = false;
            }
        }

        private static void EnsureLoaded()
        {
            if (_days != null) return;
            _days = new List<UsageDayRecord>();

            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var ser = new XmlSerializer(typeof(UsageHistoryFile));
                        var file = ser.Deserialize(fs) as UsageHistoryFile;
                        if (file?.Days != null) _days = file.Days;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("读取使用记录失败，已从空记录开始。", ex);
                _days = new List<UsageDayRecord>();
            }

            Prune();
        }

        private static void Prune()
        {
            DateTime cutoff = DateTime.Today.AddDays(-(RetentionDays - 1));
            int removed = _days.RemoveAll(d => d == null || d.Date.Date < cutoff);
            if (removed > 0) _dirty = true;
        }

        private static UsageDayRecord FindToday(string processName)
        {
            return FindByDate(processName, DateTime.Today);
        }

        private static UsageDayRecord FindByDate(string processName, DateTime date)
        {
            return _days.FirstOrDefault(d =>
                d.Date.Date == date.Date &&
                (d.ProcessName ?? "").Equals(processName ?? "", StringComparison.OrdinalIgnoreCase));
        }

        private static UsageDayRecord GetOrCreateToday(string processName)
        {
            var rec = FindToday(processName);
            if (rec == null)
            {
                rec = new UsageDayRecord
                {
                    Date = DateTime.Today,
                    ProcessName = processName ?? "",
                    UsedSeconds = 0,
                    LaunchCount = 0,
                    LastEndReason = "",
                    WasInCooldown = false,
                    BonusMinutes = 0,
                    HourlySeconds = new int[24],
                    HasHourlyData = true
                };
                _days.Add(rec);
                _dirty = true;
            }
            return rec;
        }

        /// <summary>
        /// 用配置里的权威值同步今日已用秒数（只改内存，落盘由 SaveIfDirty 负责）。
        /// hour 为本次新增计时所属的小时（0~23）；-1 表示取当前小时。
        /// </summary>
        public static void UpdateUsed(string processName, int usedSeconds, int hour = -1)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                var rec = GetOrCreateToday(processName);
                int value = Math.Max(0, usedSeconds);
                int hourIndex = hour >= 0 && hour < 24 ? hour : DateTime.Now.Hour;
                if (rec.HourlySeconds == null || rec.HourlySeconds.Length != 24)
                {
                    rec.HourlySeconds = new int[24];
                    rec.HasHourlyData = true;
                }
                if (value > rec.UsedSeconds)
                    rec.HourlySeconds[hourIndex] += value - rec.UsedSeconds;
                if (rec.UsedSeconds != value)
                {
                    rec.UsedSeconds = value;
                    _dirty = true;
                }
            }
        }

        /// <summary>
        /// 记录一次“实际进入运行”：累计启动次数，并保留当天最早的真实启动时刻。
        /// when 传进程的真实启动时间（本地时间）；不传则回退到当前时间。
        /// 只写 when 所在那一天的记录：历史日期不存在记录时不凭空创建。
        /// </summary>
        public static void NoteLaunch(string processName, DateTime? when = null)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                DateTime t = when ?? DateTime.Now;
                var rec = FindByDate(processName, t.Date);
                if (rec == null)
                {
                    if (t.Date != DateTime.Today) return;
                    rec = GetOrCreateToday(processName);
                }

                rec.LaunchCount++;
                string text = t.ToString("HH:mm:ss");
                if (string.IsNullOrEmpty(rec.FirstStartTime) ||
                    string.CompareOrdinal(text, rec.FirstStartTime) < 0)
                    rec.FirstStartTime = text;
                _dirty = true;
            }
        }

        /// <summary>
        /// 记录一次“归零/关闭”的结束时间（LastEndTime 取当天最晚）。
        /// 跨天时用前一天 23:59:59 调用，保证结束时间仍落在前一天区间内。
        /// </summary>
        public static void NoteEndTime(string processName, DateTime when)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                var rec = FindByDate(processName, when.Date);
                if (rec == null)
                {
                    if (when.Date != DateTime.Today) return; // 不为了补时间凭空创建历史记录
                    rec = GetOrCreateToday(processName);
                }

                string text = when.ToString("HH:mm:ss");
                if (string.IsNullOrEmpty(rec.LastEndTime) ||
                    string.CompareOrdinal(text, rec.LastEndTime) > 0)
                {
                    rec.LastEndTime = text;
                    _dirty = true;
                }
            }
        }

        public static void NoteEndReason(string processName, string reason, DateTime? when = null)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                DateTime t = when ?? DateTime.Now;
                var rec = FindByDate(processName, t.Date);
                if (rec == null)
                {
                    if (t.Date != DateTime.Today) return;
                    rec = GetOrCreateToday(processName);
                }
                rec.LastEndReason = reason ?? "";
                _dirty = true;
            }
        }

        public static void NoteCooldown(string processName, bool inCooldown)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                GetOrCreateToday(processName).WasInCooldown = inCooldown;
                _dirty = true;
            }
        }

        public static void ResetUsed(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            lock (_lock)
            {
                EnsureLoaded();
                var rec = GetOrCreateToday(processName);
                rec.UsedSeconds = 0;
                rec.HourlySeconds = new int[24];
                rec.HasHourlyData = true;
                _dirty = true;
            }
        }

        /// <summary>
        /// 清空“今天以前”的历史行（Date &lt; 今天），今天这一行及其小时桶/首次开启/末次关闭/
        /// 启动次数/结束原因等原样保留；立即原子写文件。返回被删除的历史行数。
        /// </summary>
        public static int ClearBeforeToday()
        {
            int removed;
            UsageHistoryFile file;
            lock (_lock)
            {
                EnsureLoaded();
                DateTime today = DateTime.Today;
                removed = _days.Count(d => d != null && d.Date.Date < today);
                _days.RemoveAll(d => d == null || d.Date.Date < today);
                _dirty = false;
                file = new UsageHistoryFile { Days = _days.OrderBy(d => d.Date).ToList() };
            }
            WriteAtomically(file);
            return removed;
        }

        public static void SetBonus(int bonusMinutes)
        {
            lock (_lock)
            {
                EnsureLoaded();
                var todays = _days.Where(d => d.Date.Date == DateTime.Today).ToList();
                bool changed = false;
                foreach (var d in todays)
                {
                    if (d.BonusMinutes == bonusMinutes) continue;
                    d.BonusMinutes = bonusMinutes;
                    changed = true;
                }
                // 只有确实有加时时才补占位行；0 加时不得凭空创建“今天 0 分钟”的空行，
                // 否则清空历史后（今天原本没有记录）表格仍会多出一行零值。
                if (bonusMinutes > 0 && todays.Count == 0)
                {
                    _days.Add(new UsageDayRecord
                    {
                        Date = DateTime.Today,
                        ProcessName = "",
                        UsedSeconds = 0,
                        BonusMinutes = bonusMinutes,
                        HourlySeconds = new int[24],
                        HasHourlyData = false
                    });
                    changed = true;
                }
                if (changed) _dirty = true;
            }
        }

        /// <summary>生成最近 days 天的日期窗口，按日期递减（最新在前）；跨月/跨年由 DateTime.AddDays 处理。</summary>
        internal static List<DateTime> RecentDatesDescending(DateTime today, int days)
        {
            if (days < 1) days = 1;
            var list = new List<DateTime>(days);
            for (int i = 0; i < days; i++) list.Add(today.Date.AddDays(-i));
            return list;
        }

        /// <summary>按天聚合（跨进程求和），返回最近 days 天，日期从新到旧（最新在上）。</summary>
        public static List<UsageDailySummary> GetDailySummary(int days)
        {
            if (days < 1) days = 1;
            if (days > RetentionDays) days = RetentionDays;

            lock (_lock)
            {
                EnsureLoaded();
                Prune();

                var result = new List<UsageDailySummary>();
                // 统一在此降序：设置页与 Web 都直接用该顺序，不再各自排序
                foreach (DateTime date in RecentDatesDescending(DateTime.Today, days))
                {
                    var items = _days.Where(d => d != null && d.Date.Date == date).ToList();
                    var hourly = new int[24];
                    bool hasHourly = false;
                    foreach (var d in items)
                    {
                        if (!d.HasHourlyData || d.HourlySeconds == null || d.HourlySeconds.Length != 24) continue;
                        hasHourly = true;
                        for (int h = 0; h < 24; h++) hourly[h] += Math.Max(0, d.HourlySeconds[h]);
                    }

                    string firstStart = items.Select(d => d.FirstStartTime)
                                            .Where(t => !string.IsNullOrEmpty(t))
                                            .OrderBy(t => t, StringComparer.Ordinal)
                                            .FirstOrDefault() ?? "";
                    string lastEnd = items.Select(d => d.LastEndTime)
                                          .Where(t => !string.IsNullOrEmpty(t))
                                          .OrderByDescending(t => t, StringComparer.Ordinal)
                                          .FirstOrDefault() ?? "";

                    result.Add(new UsageDailySummary
                    {
                        Date = date,
                        UsedMinutes = items.Sum(d => d.UsedSeconds) / 60,
                        LaunchCount = items.Sum(d => d.LaunchCount),
                        LastEndReason = items.OrderBy(d => d.Date).Select(d => d.LastEndReason)
                                            .LastOrDefault(x => !string.IsNullOrEmpty(x)) ?? "",
                        WasInCooldown = items.Any(d => d.WasInCooldown),
                        BonusMinutes = items.Count == 0 ? 0 : items.Max(d => d.BonusMinutes),
                        HourlySeconds = hourly,
                        HasHourlyData = hasHourly,
                        FirstStartTime = firstStart,
                        LastEndTime = lastEnd
                    });
                }
                return result;
            }
        }

        public static void SaveIfDirty()
        {
            bool write;
            UsageHistoryFile file;
            lock (_lock)
            {
                EnsureLoaded();
                Prune();
                write = _dirty;
                if (!write) return;
                _dirty = false;
                file = new UsageHistoryFile { Days = _days.OrderBy(d => d.Date).ToList() };
            }
            if (write) WriteAtomically(file);
        }

        public static void SaveNow()
        {
            UsageHistoryFile file;
            lock (_lock)
            {
                EnsureLoaded();
                Prune();
                _dirty = false;
                file = new UsageHistoryFile { Days = _days.OrderBy(d => d.Date).ToList() };
            }
            WriteAtomically(file);
        }

        private static void WriteAtomically(UsageHistoryFile file)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string tmp = FilePath + ".tmp";
                using (var fs = File.Create(tmp))
                    new XmlSerializer(typeof(UsageHistoryFile)).Serialize(fs, file);

                if (File.Exists(FilePath))
                    File.Replace(tmp, FilePath, FilePath + ".bak", true);
                else
                    File.Move(tmp, FilePath);
            }
            catch (Exception ex)
            {
                AppLog.Warn("保存使用记录失败", ex);
            }
        }
    }
}
