using Microsoft.Win32;
using System.Diagnostics;

namespace GameMonitor
{
    public static class StartupManager
    {
        private const string AppName = "GameMonitor";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>
        /// 「防结束守护」计划任务名。创建、删除、孤儿清理都只引用这一处，
        /// 避免各处字符串不一致导致删错对象或删不掉。
        /// </summary>
        public const string DaemonTaskName = "GameMonitor_Daemon";

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

        /// <summary>
        /// 按配置同步「防结束守护」计划任务：开启则创建，关闭则删除。
        /// 启动时调用一次即可顺便清掉历史遗留的孤儿任务（配置已关闭但任务还在）。
        /// </summary>
        public static void SyncDaemonTask(bool enable)
        {
            if (enable) CreateDaemonTask();
            else DeleteDaemonTask();
        }

        private static bool CreateDaemonTask()
        {
            string exePath = Application.ExecutablePath;
            // 每 1 分钟检查一次，如果没运行则启动
            string args = $"/create /tn \"{DaemonTaskName}\" /tr \"\\\"{exePath}\\\"\" /sc minute /mo 1 /f";
            var r = RunSchtasks(args);

            if (r.ExitCode == 0)
            {
                AppLog.Info($"防结束守护计划任务已启用：{DaemonTaskName}");
                return true;
            }

            AppLog.Warn(
                $"创建防结束守护计划任务失败（schtasks 退出码 {r.ExitCode}）：{r.Output}。" +
                $"若需手动创建，请用管理员身份执行：schtasks /create /tn \"{DaemonTaskName}\" /tr \"{exePath}\" /sc minute /mo 1 /f");
            return false;
        }

        private static bool DeleteDaemonTask()
        {
            // 先确认任务是否存在：不存在就直接视为清理完成，避免把“本来就没有”记成失败
            if (!DaemonTaskExists())
            {
                AppLog.Info($"防结束守护计划任务不存在，无需删除：{DaemonTaskName}");
                return true;
            }

            var r = RunSchtasks($"/delete /tn \"{DaemonTaskName}\" /f");
            if (r.ExitCode == 0)
            {
                AppLog.Info($"已删除防结束守护计划任务：{DaemonTaskName}");
                return true;
            }

            AppLog.Warn(
                $"删除防结束守护计划任务失败（schtasks 退出码 {r.ExitCode}）：{r.Output}。" +
                $"通常是当前进程权限不足，请用管理员身份执行：schtasks /delete /tn \"{DaemonTaskName}\" /f");
            return false;
        }

        /// <summary>计划任务是否已注册（查询成功即存在）。</summary>
        public static bool DaemonTaskExists()
        {
            return RunSchtasks($"/query /tn \"{DaemonTaskName}\"").ExitCode == 0;
        }

        private static SchtasksResult RunSchtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc == null) return new SchtasksResult(-1, "无法启动 schtasks.exe");

                    string stdout = proc.StandardOutput.ReadToEnd();
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(3000);

                    string output = (stdout + "\n" + stderr).Trim();
                    return new SchtasksResult(proc.HasExited ? proc.ExitCode : -1, output);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("调用 schtasks.exe 失败。", ex);
                return new SchtasksResult(-1, ex.Message);
            }
        }

        private sealed class SchtasksResult
        {
            public readonly int ExitCode;
            public readonly string Output;

            public SchtasksResult(int exitCode, string output)
            {
                ExitCode = exitCode;
                Output = output ?? "";
            }
        }
    }
}
