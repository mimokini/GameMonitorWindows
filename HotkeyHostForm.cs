using System.ComponentModel;

namespace GameMonitor
{
    internal class HotkeyHostForm : Form
    {
        // 告诉 WinForms 设计器不显示也不序列化此属性
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public HotkeyManager Hotkey { get; set; }

        public HotkeyHostForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            Opacity = 0;
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x00000080; return cp; }
        }

        protected override void WndProc(ref Message m)
        {
            Hotkey?.HandleMessage(ref m);
            base.WndProc(ref m);
        }
    }
}