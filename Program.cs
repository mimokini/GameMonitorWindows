using System.Windows.Forms;

namespace GameMonitor
{
    internal static class Program
    {
        // 单实例互斥锁：字段必须保留引用，否则 Mutex 会被 GC 回收并释放句柄，
        // 导致「已存在实例」判定失效、出现多开。不要改成本地变量。
        private static Mutex _mutex;

        [STAThread]
        static void Main()
        {
            // 单实例互斥锁，防止计划任务拉起时多开
            bool createdNew;
            _mutex = new Mutex(true, "GameMonitor_Unique_Mutex", out createdNew);
            if (!createdNew)
            {
                // 如果进程已经存在，直接退出
                return;
            }

            // 全局异常兜底：UI 线程与后台线程异常都写日志，尽量不让进程无声退出
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLog.Error("未处理的 UI 线程异常", e.Exception);
                try
                {
                    MessageBox.Show("程序发生异常，详情已写入日志：\n" + e.Exception.Message,
                        "游戏监控", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLog.Error("未处理的后台线程异常", ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppLog.Info("程序启动");
            Application.Run(new TrayAppContext());
            AppLog.Info("程序退出");
        }
    }
}
