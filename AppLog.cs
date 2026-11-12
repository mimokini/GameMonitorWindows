using System.Text;

namespace GameMonitor
{
    /// <summary>
    /// 极简文件日志：写入 %LOCALAPPDATA%\GameMonitor\logs\app-yyyyMMdd.log。
    /// 任何写日志失败都必须静默忽略，避免日志本身影响主流程。
    /// 留存策略：只保留最近 <see cref="RetentionDays"/> 天；单文件超过 <see cref="MaxFileBytes"/>
    /// 时滚动为 app-yyyyMMdd.N.log 另起新文件，避免长期运行无限累加。
    /// </summary>
    public static class AppLog
    {
        private static readonly object _lock = new object();

        /// <summary>日志保留天数（含当天）。</summary>
        private const int RetentionDays = 14;
        /// <summary>单个日志文件的大小上限（2 MB），超过后滚动到新文件。</summary>
        private const long MaxFileBytes = 2L * 1024 * 1024;

        /// <summary>上一次执行过期清理的日期，保证每天最多扫一次目录。</summary>
        private static DateTime _lastCleanupDate = DateTime.MinValue;

        /// <summary>可用环境变量 GAMEMONITOR_LOG_DIR 重定向日志目录（自动化测试用），生产环境保持默认。</summary>
        private static readonly string _dirOverride = Environment.GetEnvironmentVariable("GAMEMONITOR_LOG_DIR");

        private static string LogDirectory
            => string.IsNullOrEmpty(_dirOverride)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameMonitor", "logs")
                : _dirOverride;

        /// <summary>日志目录（设置页“关于”里展示、并提供打开目录按钮）。</summary>
        public static string DirectoryPath => LogDirectory;

        private static string CurrentFile
            => Path.Combine(LogDirectory, $"app-{DateTime.Now:yyyyMMdd}.log");

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warn(string message, Exception ex = null) => Write("WARN", message, ex);

        public static void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                  .Append(" [").Append(level).Append("] ")
                  .Append(message ?? "");
                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex.ToString());
                }
                sb.AppendLine();

                string text = sb.ToString();
                lock (_lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    CleanupIfNeeded();
                    File.AppendAllText(PrepareFileForAppend(text), text, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响程序
            }
        }

        /// <summary>按大小上限做滚动，返回本次应写入的文件路径。</summary>
        private static string PrepareFileForAppend(string text)
        {
            string path = CurrentFile;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists) return path;

                long incoming = Encoding.UTF8.GetByteCount(text);
                if (info.Length + incoming <= MaxFileBytes) return path;

                // 当前文件已到上限：滚动成 app-yyyyMMdd.N.log，另起一份新的当天日志
                string stamp = DateTime.Now.ToString("yyyyMMdd");
                for (int i = 1; i < 1000; i++)
                {
                    string rolled = Path.Combine(LogDirectory, $"app-{stamp}.{i}.log");
                    if (!File.Exists(rolled))
                    {
                        File.Move(path, rolled);
                        return path;
                    }
                }
            }
            catch
            {
                // 滚动失败就继续写原文件，宁可超出上限也不要丢日志
            }
            return path;
        }

        /// <summary>清理超过保留天数的日志（每天最多执行一次）。</summary>
        private static void CleanupIfNeeded()
        {
            DateTime today = DateTime.Today;
            if (_lastCleanupDate == today) return;
            _lastCleanupDate = today;

            try
            {
                DateTime cutoff = today.AddDays(-(RetentionDays - 1)); // 保留“今天 + 往前 13 天”
                string current = Path.GetFileName(CurrentFile);

                foreach (string file in Directory.EnumerateFiles(LogDirectory, "app-*.log"))
                {
                    string name = Path.GetFileName(file);
                    if (string.Equals(name, current, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!TryParseLogDate(name, out DateTime date)) continue;
                    if (date >= cutoff) continue;
                    try { File.Delete(file); } catch { /* 被占用就留到下次 */ }
                }
            }
            catch
            {
                // 清理失败不影响写日志
            }
        }

        /// <summary>从 app-yyyyMMdd.log / app-yyyyMMdd.N.log 解析出日期。</summary>
        private static bool TryParseLogDate(string fileName, out DateTime date)
        {
            date = DateTime.MinValue;
            const string prefix = "app-";
            if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            string body = fileName.Substring(prefix.Length);
            int dot = body.IndexOf('.');
            if (dot < 0) return false;

            string stamp = body.Substring(0, dot);
            if (stamp.Length != 8) return false;
            return DateTime.TryParseExact(stamp, "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out date);
        }
    }
}
