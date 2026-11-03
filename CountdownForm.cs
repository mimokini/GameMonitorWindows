using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public enum OverlayMode { None, Reminder, Countdown, Blocked }

    public class CountdownForm : Form
    {
        private readonly AppConfig _config;

        private OverlayMode _mode = OverlayMode.None;
        private int _remaining;
        private int _reminderMinutes;
        private string _gameName = "";
        private string _customMessage = "";
        private readonly Timer _tickTimer;
        private readonly Timer _autoHideTimer;

        // 缓存字体大小，避免每次刷新都重新测量（防止闪烁和卡顿）
        private float _cachedFontSize = 0f;
        private string _cachedFontFam = "";
        private int _cachedWidth = 0;
        private int _cachedHeight = 0;
        private int _cachedHeightPct = 0;
        private int _cachedWidthPct = 0;

        #region P/Invoke
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        private const byte AC_SRC_OVER = 0x00, AC_SRC_ALPHA = 0x01;
        private const int ULW_ALPHA = 0x02;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(
            IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int X, int Y, int cx, int cy, uint uFlags);

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TRANSPARENT = 0x00000020L;
        private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

        private static long GetWindowLongSafe(IntPtr hWnd, int nIndex)
            => IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex).ToInt64() : GetWindowLong32(hWnd, nIndex);

        private static void SetWindowLongSafe(IntPtr hWnd, int nIndex, long value)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(hWnd, nIndex, new IntPtr(value));
            else SetWindowLong32(hWnd, nIndex, (int)value);
        }
        #endregion

        public event Action Finished;

        public CountdownForm(AppConfig config)
        {
            _config = config;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;

            // 以鼠标所在屏幕为准（多屏时不会只贴主屏）
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = (int)(screen.Width * 0.6);
            Height = (int)(screen.Height * 0.6);
            Left = screen.Left + (screen.Width - Width) / 2;
            Top = screen.Top + (screen.Height - Height) / 2;

            _tickTimer = new Timer { Interval = 1000 };
            _tickTimer.Tick += TickTimer_Tick;

            _autoHideTimer = new Timer { Interval = 5000 };
            _autoHideTimer.Tick += AutoHideTimer_Tick;
        }

        /// <summary>
        /// 提醒/禁用提示的自动隐藏：如果倒计时仍在进行，只恢复倒计时画面，不能把倒计时关掉。
        /// </summary>
        private void AutoHideTimer_Tick(object sender, EventArgs e)
        {
            _autoHideTimer.Stop();
            if (_tickTimer.Enabled)
            {
                _mode = OverlayMode.Countdown;
                UpdateLayer();
                return;
            }
            StopAndHide();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                if (_config != null && _config.MouseTransparent)
                    cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        // 阻止系统重绘背景，消除闪屏
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 什么都不做，屏蔽系统默认背景
        }

        public void ApplyMouseTransparent(bool transparent)
        {
            if (!IsHandleCreated) return;

            long ex = GetWindowLongSafe(Handle, GWL_EXSTYLE);
            if (transparent) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLongSafe(Handle, GWL_EXSTYLE, ex);

            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);

            if (_mode != OverlayMode.None) UpdateLayer();
        }

        public void ApplyStyleChanges()
        {
            if (_mode != OverlayMode.None) UpdateLayer();
        }

        public void ShowReminder(string gameName, int minutes, string message = null)
        {
            _mode = OverlayMode.Reminder;
            _gameName = gameName;
            _reminderMinutes = minutes;
            _customMessage = message ?? $"「{gameName}」请在 {minutes} 分钟内退出";

            // 倒计时进行中时只用临时横幅覆盖，不能停掉倒计时定时器
            if (!_tickTimer.Enabled)
                _tickTimer.Stop();

            _autoHideTimer.Stop();
            _autoHideTimer.Interval = 5000;

            UpdateLayer(); // 先绘制再显示
            Show();
            _autoHideTimer.Start();
        }

        public void StartCountdown(string gameName, int seconds)
        {
            _mode = OverlayMode.Countdown;
            _gameName = gameName;
            _remaining = seconds;
            _tickTimer.Stop();
            _customMessage = "";
            _autoHideTimer.Stop();

            UpdateLayer(); // 先绘制再显示
            Show();
            _tickTimer.Start();
        }

        public void ShowBlocked(string gameName)
        {
            _mode = OverlayMode.Blocked;
            _gameName = gameName;

            // 同样：不打断正在进行的倒计时
            if (!_tickTimer.Enabled)
                _tickTimer.Stop();

            _autoHideTimer.Stop();
            _autoHideTimer.Interval = 5000;

            UpdateLayer();
            Show();
            _autoHideTimer.Start();
        }

        public void StopAndHide()
        {
            _tickTimer.Stop();
            _autoHideTimer.Stop();
            _mode = OverlayMode.None;
            Hide();
        }

        public static void ShowPreview(AppConfig config, int seconds = 3)
        {
            var preview = new CountdownForm(config);
            preview.Finished += () => { try { preview.Dispose(); } catch { } };
            preview.StartCountdown("预览", seconds);
        }

        private void TickTimer_Tick(object sender, EventArgs e)
        {
            // 即使当前被提醒/禁用横幅临时覆盖，倒计时的剩余秒数也必须继续走
            _remaining--;
            if (_remaining <= 0)
            {
                _tickTimer.Stop();
                _autoHideTimer.Stop();
                Hide();
                _mode = OverlayMode.None;
                Finished?.Invoke();
                return;
            }
            if (_mode == OverlayMode.Countdown) UpdateLayer();
        }

        private void UpdateLayer()
        {
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // 可配置背景
                if (_config.CountdownBackAlpha > 0)
                {
                    int bgAlpha = Math.Max(0, Math.Min(255, _config.CountdownBackAlpha));
                    using (var bg = new SolidBrush(Color.FromArgb(bgAlpha,
                        _config.CountdownBackColorR,
                        _config.CountdownBackColorG,
                        _config.CountdownBackColorB)))
                    {
                        g.FillRectangle(bg, 0, 0, Width, Height);
                    }
                }

                if (_mode == OverlayMode.Reminder)
                {
                    var fontFam = SafeFontFamily(_config.CountdownFontFamily);
                    Color color = Color.FromArgb(220, _config.CountdownFontColorR,
                        _config.CountdownFontColorG, _config.CountdownFontColorB);
                    using (var fontBig = new Font(fontFam, 72, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brushBig = new SolidBrush(color))
                    using (var fontSmall = new Font(fontFam, 34, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brushSmall = new SolidBrush(Color.FromArgb(230, 255, 220, 220)))
                    {
                        DrawCentered(g, "游戏时间到！", fontBig, brushBig, Height / 2 - 100);
                        DrawCentered(g, _customMessage, fontSmall, brushSmall, Height / 2 + 40);
                    }
                }
                else if (_mode == OverlayMode.Countdown)
                {
                    DrawBigNumber(g, _remaining.ToString());
                }
                else if (_mode == OverlayMode.Blocked)
                {
                    var fontFam = SafeFontFamily(_config.CountdownFontFamily);
                    Color color = Color.FromArgb(230, _config.CountdownFontColorR,
                        _config.CountdownFontColorG, _config.CountdownFontColorB);
                    using (var fontBig = new Font(fontFam, 76, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brushBig = new SolidBrush(color))
                    using (var fontSmall = new Font(fontFam, 34, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brushSmall = new SolidBrush(Color.FromArgb(240, 255, 220, 220)))
                    {
                        DrawCentered(g, "今日游戏时长已用完", fontBig, brushBig, Height / 2 - 110);
                        DrawCentered(g, $"「{_gameName}」已无法启动", fontSmall, brushSmall, Height / 2 + 40);
                    }
                }

                ApplyBitmap(bmp);
            }
        }

        private string SafeFontFamily(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Microsoft YaHei";
            try
            {
                using (var test = new FontFamily(name)) { return name; }
            }
            catch { return "Microsoft YaHei"; }
        }

        private void DrawBigNumber(Graphics g, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            // 只有当窗口大小或配置变化时，才重新计算字号（大幅提升性能，杜绝闪烁）
            if (_cachedFontSize == 0f || _cachedFontFam != _config.CountdownFontFamily ||
                _cachedWidth != Width || _cachedHeight != Height ||
                _cachedHeightPct != _config.CountdownHeightPercent || _cachedWidthPct != _config.CountdownWidthPercent)
            {
                var fontFam = SafeFontFamily(_config.CountdownFontFamily);
                float heightPct = Math.Max(10, Math.Min(100, _config.CountdownHeightPercent)) / 100f;
                float widthPct = Math.Max(10, Math.Min(100, _config.CountdownWidthPercent)) / 100f;

                float baseSize = 400f;
                float maxScale = float.MaxValue;

                // 用 "88" 测一下，因为 2 个数字的宽度通常是最宽的
                using (var probe = new Font(fontFam, baseSize, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var sz = g.MeasureString("88", probe);
                    if (sz.Width > 0 && sz.Height > 0)
                    {
                        float targetH = Height * heightPct;
                        float targetW = Width * widthPct;
                        maxScale = Math.Min(targetH / sz.Height, targetW / sz.Width);
                    }
                }

                _cachedFontSize = baseSize * maxScale;
                if (_cachedFontSize < 80f) _cachedFontSize = 80f;
                if (_cachedFontSize > 1600f) _cachedFontSize = 1600f;

                _cachedFontFam = _config.CountdownFontFamily;
                _cachedWidth = Width;
                _cachedHeight = Height;
                _cachedHeightPct = _config.CountdownHeightPercent;
                _cachedWidthPct = _config.CountdownWidthPercent;
            }

            int alpha = Math.Max(0, Math.Min(255, _config.CountdownFontAlpha));
            Color mainColor = Color.FromArgb(alpha,
                _config.CountdownFontColorR,
                _config.CountdownFontColorG,
                _config.CountdownFontColorB);

            using (var fontBig = new Font(SafeFontFamily(_config.CountdownFontFamily), _cachedFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brushMain = new SolidBrush(mainColor))
            {
                var size = g.MeasureString(text, fontBig);
                float x = (Width - size.Width) / 2f;
                float y = (Height - size.Height) / 2f;

                if (_config.CountdownShadow)
                {
                    using (var brushShadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    {
                        float offset = Math.Max(4f, _cachedFontSize * 0.03f);
                        g.DrawString(text, fontBig, brushShadow, x + offset, y + offset);
                    }
                }
                g.DrawString(text, fontBig, brushMain, x, y);
            }
        }

        private void DrawCentered(Graphics g, string text, Font font, Brush brush, int y)
        {
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, brush, (Width - size.Width) / 2, y);
        }

        private void ApplyBitmap(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);
            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT { X = 0, Y = 0 };
                var dst = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = AC_SRC_OVER,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tickTimer?.Dispose(); _autoHideTimer?.Dispose(); }
            base.Dispose(disposing);
        }
    }
}