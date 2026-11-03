using System.Text;

namespace GameMonitor
{
    /// <summary>
    /// 极简文件日志：写入 %LOCALAPPDATA%\GameMonitor\logs\app-yyyyMMdd.log。
    /// 任何写日志失败都必须静默忽略，避免日志本身影响主流程。
    /// </summary>
    public static class AppLog
    {
        private static readonly object _lock = new object();

        private static string LogDirectory
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameMonitor", "logs");

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

                lock (_lock)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不能影响程序
            }
        }
    }
}
