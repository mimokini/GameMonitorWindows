using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GameMonitor
{
    /// <summary>给 WinForms 输入框加灰色占位提示（EM_SETCUEBANNER），统一封装避免逐处重复。</summary>
    internal static class PlaceholderHelper
    {
        private const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void Attach(Control input, string cue)
        {
            if (input == null || string.IsNullOrEmpty(cue)) return;
            input.HandleCreated += (s, e) => Apply(input, cue);
            if (input.IsHandleCreated) Apply(input, cue);
        }

        private static void Apply(Control input, string cue)
        {
            try
            {
                Control target = input;
                if (target is NumericUpDown nup)
                {
                    foreach (Control child in nup.Controls)
                    {
                        if (child is TextBox) { target = child; break; }
                    }
                }
                if (!target.IsHandleCreated) return;
                SendMessage(target.Handle, EM_SETCUEBANNER, (IntPtr)1, cue);
            }
            catch
            {
                // 占位提示属于纯展示，失败不影响输入与保存
            }
        }
    }
}
