using System.Runtime.InteropServices;

namespace GameMonitor
{
    public class HotkeyManager : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0x9001;
        private const uint MOD_NOREPEAT = 0x4000;

        private IntPtr _hwnd;
        private bool _registered;

        public event Action HotkeyPressed;

        public bool Register(IntPtr hwnd, uint modifiers, uint vk)
        {
            _hwnd = hwnd;
            Unregister();
            _registered = RegisterHotKey(hwnd, HOTKEY_ID, modifiers | MOD_NOREPEAT, vk);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered && _hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HOTKEY_ID);
                _registered = false;
            }
        }

        public void HandleMessage(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
                HotkeyPressed?.Invoke();
        }

        public void Dispose() => Unregister();
    }
}