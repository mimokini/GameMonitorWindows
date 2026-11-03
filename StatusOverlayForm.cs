using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public class StatusOverlayForm : Form
    {
        private readonly AppConfig _config;
        private readonly GameMonitor _monitor;

        private readonly Timer _refreshTimer;
        private readonly Timer _autoHideTimer;

        private List<GameStatus> _statuses = new List<GameStatus>();
        private bool _autoHidden;
        private bool _dragging;
        private Point _dragOffset;

        private readonly int _padding = 12;
        private Font _fontTitle;
        private Font _fontLine;

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

        public StatusOverlayForm(AppConfig config, GameMonitor monitor)
        {
            _config = config;
            _monitor = monitor;

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = _config.StatusOverlayTopMost;
            BackColor = Color.Black;

            RebuildFonts();

            Width = 220;
            Height = 80;

            // 使用 -9999 判断是否为首次启动，避免负数屏幕导致误判
            if (_config.StatusOverlayX == -9999 || _config.StatusOverlayY == -9999)
            {
                // 首次启动放在当前屏幕（鼠标所在屏）右上角
                var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                Left = wa.Right - Width - 30;
                Top = wa.Top + 30;
                _config.StatusOverlayX = Left;
                _config.StatusOverlayY = Top;
            }
            else
            {
                Left = _config.StatusOverlayX;
                Top = _config.StatusOverlayY;
            }

            _refreshTimer = new Timer { Interval = 1000 };
            _refreshTimer.Tick += (s, e) =>
            {
                RefreshContent();
                CheckCursorForAutoHide();
            };

            _autoHideTimer = new Timer { Interval = Math.Max(1, _config.StatusOverlayAutoHideSeconds) * 1000 };
            _autoHideTimer.Tick += (s, e) => EnterAutoHide();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;
                cp.ExStyle |= 0x00000080;
                if (_config != null && _config.MouseTransparent)
                    cp.ExStyle |= 0x00000020;
                return cp;
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

        private void RebuildFonts()
        {
            _fontTitle?.Dispose();
            _fontLine?.Dispose();

            string fam = SafeFontFamily(_config.StatusOverlayFontFamily);
            int size = Math.Max(6, Math.Min(48, _config.StatusOverlayFontSize));

            _fontTitle = new Font(fam, size, FontStyle.Bold);
            _fontLine = new Font(fam, size, FontStyle.Regular);
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

            if (_autoHidden) { _autoHidden = false; }
            ApplyBitmap();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            RefreshContent();
            ClampToVirtualScreen();

            _refreshTimer.Start();
            if (_config.StatusOverlayAutoHide) _autoHideTimer.Start();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (IsHandleCreated)
            {
                if (Visible)
                {
                    _refreshTimer.Start();
                    if (_config.StatusOverlayAutoHide) _autoHideTimer.Start();
                }
                else
                {
                    _refreshTimer.Stop();
                    _autoHideTimer.Stop();
                }
            }
        }

        public void RefreshContent()
        {
            _statuses = _monitor.GetStatuses();

            using (var g = CreateGraphics())
            {
                string title = "今日游戏时长";
                var titleSize = g.MeasureString(title, _fontTitle);
                int lineH = (int)Math.Ceiling(_fontLine.GetHeight(g)) + 6;
                int maxWidth = (int)Math.Ceiling(titleSize.Width);

                foreach (var s in _statuses)
                {
                    var sz = g.MeasureString(BuildLine(s), _fontLine);
                    if ((int)Math.Ceiling(sz.Width) > maxWidth) maxWidth = (int)Math.Ceiling(sz.Width);
                }

                int w = maxWidth + _padding * 2;
                int h = _padding * 2 + (int)titleSize.Height + 6 + lineH * Math.Max(1, _statuses.Count);
                if (_statuses.Count == 0) h = _padding * 2 + (int)titleSize.Height + 6 + lineH;

                if (Width != w || Height != h)
                {
                    Width = w;
                    Height = h;
                    if (_config.StatusOverlaySnap) SnapToEdge();
                }
            }
            ApplyBitmap();
        }

        private string BuildLine(GameStatus s)
        {
            string quota = s.Game.DailyLimitMinutes > 0 ? s.Game.DailyLimitMinutes.ToString() : "不限";
            int used = s.UsedSecondsToday / 60;
            string state = s.IsRunning ? "●" : "○";
            return $"{state} {s.Game.DisplayName}: {used}/{quota} 分";
        }

        private void ApplyBitmap()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                int alpha = _autoHidden ? 40 : _config.StatusOverlayOpacity;
                if (alpha < 0) alpha = 0;
                if (alpha > 255) alpha = 255;

                Color backColor = Color.FromArgb(alpha,
                    _config.StatusOverlayBackColorR,
                    _config.StatusOverlayBackColorG,
                    _config.StatusOverlayBackColorB);

                using (var bg = new SolidBrush(backColor))
                    g.FillRectangle(bg, 0, 0, Width, Height);

                using (var border = new Pen(Color.FromArgb(Math.Min(255, alpha + 20), 80, 80, 80), 1))
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                int textAlpha = _autoHidden ? 70 : 255;
                Color baseText = Color.FromArgb(textAlpha,
                    _config.StatusOverlayTextColorR,
                    _config.StatusOverlayTextColorG,
                    _config.StatusOverlayTextColorB);
                Color dimText = Color.FromArgb((int)(textAlpha * 0.6),
                    _config.StatusOverlayTextColorR,
                    _config.StatusOverlayTextColorG,
                    _config.StatusOverlayTextColorB);

                using (var titleBrush = new SolidBrush(baseText))
                {
                    var titleSize = g.MeasureString("今日游戏时长", _fontTitle);
                    float titleX = (Width - titleSize.Width) / 2f;
                    g.DrawString("今日游戏时长", _fontTitle, titleBrush, titleX, _padding);

                    int y = _padding + (int)titleSize.Height + 6;
                    int lineH = (int)Math.Ceiling(_fontLine.GetHeight(g)) + 6;

                    foreach (var s in _statuses)
                    {
                        using (var b = new SolidBrush(s.IsRunning ? baseText : dimText))
                            g.DrawString(BuildLine(s), _fontLine, b, _padding, y);
                        y += lineH;
                    }

                    if (_statuses.Count == 0)
                    {
                        using (var b = new SolidBrush(dimText))
                            g.DrawString("（暂无监控）", _fontLine, b, _padding, y);
                    }
                }

                ApplyLayeredBitmap(bmp);
            }
        }

        private void ApplyLayeredBitmap(Bitmap bmp)
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top);
                _autoHideTimer.Stop();
                if (_autoHidden) ExitAutoHide();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging) return;

            int newLeft = Cursor.Position.X - _dragOffset.X;
            int newTop = Cursor.Position.Y - _dragOffset.Y;

            var vScreen = SystemInformation.VirtualScreen;
            if (newLeft < vScreen.Left) newLeft = vScreen.Left;
            if (newTop < vScreen.Top) newTop = vScreen.Top;
            if (newLeft + Width > vScreen.Right) newLeft = vScreen.Right - Width;
            if (newTop + Height > vScreen.Bottom) newTop = vScreen.Bottom - Height;

            if (Left != newLeft || Top != newTop)
            {
                Left = newLeft;
                Top = newTop;
                ApplyBitmap();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging)
            {
                _dragging = false;
                if (_config.StatusOverlaySnap) SnapToEdge();
                SavePosition();
                if (_config.StatusOverlayAutoHide) _autoHideTimer.Start();
            }
        }

        private void SnapToEdge()
        {
            var screen = Screen.FromControl(this);
            var wa = screen.WorkingArea;
            const int threshold = 25;
            int newLeft = Left;
            int newTop = Top;

            if (Math.Abs(Left - wa.Left) < threshold) newLeft = wa.Left;
            else if (Math.Abs(wa.Right - (Left + Width)) < threshold) newLeft = wa.Right - Width;

            if (Math.Abs(Top - wa.Top) < threshold) newTop = wa.Top;
            else if (Math.Abs(wa.Bottom - (Top + Height)) < threshold) newTop = wa.Bottom - Height;

            if (newLeft != Left || newTop != Top)
            {
                Left = newLeft;
                Top = newTop;
                ApplyBitmap();
            }
        }

        private void ClampToVirtualScreen()
        {
            var vScreen = SystemInformation.VirtualScreen;
            if (Left < vScreen.Left) Left = vScreen.Left;
            if (Top < vScreen.Top) Top = vScreen.Top;
            if (Left + Width > vScreen.Right) Left = vScreen.Right - Width;
            if (Top + Height > vScreen.Bottom) Top = vScreen.Bottom - Height;
        }

        private void SavePosition()
        {
            if (_config.StatusOverlayX != Left || _config.StatusOverlayY != Top)
            {
                _config.StatusOverlayX = Left;
                _config.StatusOverlayY = Top;
                _config.SaveQuiet();
            }
        }

        private void EnterAutoHide()
        {
            _autoHideTimer.Stop();
            if (_dragging) return;
            if (Bounds.Contains(Cursor.Position)) return;
            _autoHidden = true;
            ApplyBitmap();
        }

        private void ExitAutoHide()
        {
            _autoHidden = false;
            ApplyBitmap();
            if (_config.StatusOverlayAutoHide) _autoHideTimer.Start();
        }

        private void CheckCursorForAutoHide()
        {
            if (!_autoHidden || _dragging) return;
            if (Bounds.Contains(Cursor.Position)) ExitAutoHide();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _autoHideTimer.Stop();
            if (_autoHidden) ExitAutoHide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging && _config.StatusOverlayAutoHide)
            {
                _autoHideTimer.Interval = Math.Max(1, _config.StatusOverlayAutoHideSeconds) * 1000;
                _autoHideTimer.Start();
            }
        }

        public void ApplyConfigChanges()
        {
            TopMost = _config.StatusOverlayTopMost;
            _autoHideTimer.Interval = Math.Max(1, _config.StatusOverlayAutoHideSeconds) * 1000;
            if (_config.StatusOverlayAutoHide) _autoHideTimer.Start();
            else _autoHideTimer.Stop();

            RebuildFonts();
            if (_autoHidden) _autoHidden = false;
            RefreshContent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SavePosition(); // 退出时强制做最后一次确认保存
                _refreshTimer?.Stop(); _refreshTimer?.Dispose();
                _autoHideTimer?.Stop(); _autoHideTimer?.Dispose();
                _fontTitle?.Dispose();
                _fontLine?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}