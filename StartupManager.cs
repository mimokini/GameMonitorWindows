using Microsoft.Win32;
using System.Diagnostics;

namespace GameMonitor
{
    public static class StartupManager
    {
        private const string AppName = "GameMonitor";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string DaemonTaskName = "GameMonitor_Daemon";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                return key?.GetValue(AppName) != null;
        }

        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled)
                    key.SetValue(AppName, $"\"{Application.ExecutablePath}\"");
                else
                    key.DeleteValue(AppName, false);
            }
        }

        public static void SetDaemon(bool enable)
        {
            string exePath = Application.ExecutablePath;
            string args;

            if (enable)
            {
                // 每 1 分钟检查一次，如果没运行则启动
                args = $"/create /tn \"{DaemonTaskName}\" /tr \"\\\"{exePath}\\\"\" /sc minute /mo 1 /f";
            }
            else
            {
                // 删除计划任务
                args = $"/delete /tn \"{DaemonTaskName}\" /f";
            }

            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                // 启动进程并等待它执行完毕（最多等3秒），确保任务被彻底创建或删除
                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(3000);
                }
            }
            catch { }
        }
    }
}