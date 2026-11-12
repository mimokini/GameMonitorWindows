using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Timer = System.Windows.Forms.Timer;

namespace GameMonitor
{
    public enum OverlayMode { None, Countdown, Notice }

    public class CountdownForm : Form
    {
        /// <summary>临时通知（提醒 / 配额用完 / 间隔期），可排队，不打断倒计时。</summary>
        private sealed class Notice
        {
            public string Headline = "";
            public string Message = "";
            /// <summary>关联的监控条目关键字；用于“加时取消提醒”时精确收起对应浮层。</summary>
            public string GameName = "";
            public PromptKind Kind = PromptKind.Reminding;
            public int DurationMs = 5000;
        }

        private readonly AppConfig _config;

        /// <summary>精确字形排版：去掉 GDI+ 默认左右各 1/6 em 内边距，测量与绘制同一口径，居中才不会偏移。</summary>
        private static readonly StringFormat PreciseText = StringFormat.GenericTypographic;

        private OverlayMode _mode = OverlayMode.None;
        private int _remaining;
        private readonly Timer _tickTimer;
        private readonly Timer _autoHideTimer;

        private readonly Queue<Notice> _noticeQueue = new Queue<Notice>();
        private Notice _currentNotice;

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
        /// 通知自动隐藏：还有排队的通知就继续显示下一条；
        /// 否则倒计时仍在进行时恢复倒计时画面，绝不把倒计时关掉。
        /// </summary>
        private void AutoHideTimer_Tick(object sender, EventArgs e)
        {
            _autoHideTimer.Stop();
            ShowNextNotice();
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

        /// <summary>提示文字停留时长（毫秒），来自配置 NoticeStaySeconds（1~60，默认 5）。</summary>
        private int NoticeDurationMs
        {
            get
            {
                int seconds = _config?.NoticeStaySeconds ?? 5;
                if (seconds < 1) seconds = 5;
                return Math.Max(1000, Math.Min(60, seconds) * 1000);
            }
        }

        /// <summary>时间到后的提醒（宽限期开始）。</summary>
        public void ShowReminder(string gameName, int minutes, string message = null)
        {
            var style = StyleOf(PromptKind.Reminding);
            string text = message ?? PromptText.Format(
                style.Template, gameName, minutes, 0, "", $"{minutes} 分钟", PromptKind.Reminding);
            ShowNotice("游戏时间到！", text, NoticeDurationMs, PromptKind.Reminding, gameName);
        }

        /// <summary>今日配额用完（或托盘测试用）；totalLimit=true 时提示“今日总时长已用完”。</summary>
        public void ShowBlocked(string gameName, bool totalLimit = false)
        {
            var style = StyleOf(PromptKind.QuotaExhausted);
            string text = PromptText.Format(
                style.Template, gameName, 0, 0, "", totalLimit ? "今日总时长已用完" : "今日已用完",
                PromptKind.QuotaExhausted);
            ShowNotice(totalLimit ? "今日总时长已用完" : "今日游戏时长已用完", text, NoticeDurationMs, PromptKind.QuotaExhausted);
        }

        /// <summary>禁止时段强制关闭提示（仅在被监控程序确实在运行时调用）。</summary>
        public void ShowBlockedBySchedule(string gameName)
        {
            var style = StyleOf(PromptKind.BlockedBySchedule);
            string text = PromptText.Format(
                style.Template, gameName, 0, 0, "", "禁止时段", PromptKind.BlockedBySchedule);
            ShowNotice("禁止时段", text, NoticeDurationMs, PromptKind.BlockedBySchedule);
        }

        /// <summary>间隔期：用户再次启动被监控程序时立即提示并结束进程。</summary>
        public void ShowCooldown(string gameName, int remainingSeconds, DateTime? availableAt = null)
        {
            int minutes = (int)Math.Ceiling(Math.Max(0, remainingSeconds) / 60.0);
            // 有精确的休息结束时刻就用它（避免“剩余秒数取整”带来的分钟偏差），否则按剩余秒数推算
            DateTime target = availableAt ?? DateTime.Now.AddSeconds(Math.Max(0, remainingSeconds));
            var style = StyleOf(PromptKind.Cooldown);
            string text = PromptText.Format(
                style.Template, gameName, minutes, remainingSeconds, FormatClock(remainingSeconds),
                $"{minutes} 分钟", PromptKind.Cooldown, FormatAvailableAt(target));
            ShowNotice("注意休息，等会再玩吧！", text, NoticeDurationMs, PromptKind.Cooldown);
        }

        private PromptStyleConfig StyleOf(PromptKind kind)
        {
            var s = _config.OverlayStyle ?? new OverlayStyleConfig();
            switch (kind)
            {
                case PromptKind.Countdown: return s.CountingDown;
                case PromptKind.Cooldown: return s.Cooldown;
                case PromptKind.QuotaExhausted: return s.QuotaExhausted;
                case PromptKind.BlockedBySchedule: return s.BlockedBySchedule;
                default: return s.Reminding;
            }
        }

        private static string FormatClock(int seconds)
        {
            if (seconds < 0) seconds = 0;
            var span = TimeSpan.FromSeconds(seconds);
            return string.Format("{0:00}:{1:00}:{2:00}", span.Hours, span.Minutes, span.Seconds);
        }

        /// <summary>
        /// “可再次运行”的绝对时刻：24 小时制、补零到分钟（如 14:00）；
        /// 结束时刻通常带秒，一律向上取整到下一分钟（14:10:30 → 14:11），
        /// 保证提示的时刻绝不早于真实可运行时刻。
        /// 跨天时才加前缀，次日为“明天 00:30”，更远为“M月d日 HH:mm”，避免同日/跨日混淆。
        /// </summary>
        private static string FormatAvailableAt(DateTime target)
        {
            DateTime t = target;
            if (t.Second > 0 || t.Millisecond > 0) t = t.AddMinutes(1);
            t = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);

            string hm = string.Format("{0:00}:{1:00}", t.Hour, t.Minute);
            int days = (t.Date - DateTime.Today).Days;
            if (days <= 0) return hm;
            if (days == 1) return "明天 " + hm;
            return string.Format("{0}月{1}日 {2}", t.Month, t.Day, hm);
        }

        /// <summary>通知排队显示：任意时刻只显示一条，绝不打断正在进行的倒计时。</summary>
        public void ShowNotice(string headline, string message, int durationMs = 5000,
            PromptKind kind = PromptKind.Reminding, string gameName = null)
        {
            _noticeQueue.Enqueue(new Notice
            {
                Headline = headline ?? "",
                Message = message ?? "",
                GameName = gameName ?? "",
                Kind = kind,
                DurationMs = Math.Max(1000, durationMs)
            });

            if (_currentNotice == null) ShowNextNotice();
        }

        /// <summary>
        /// 收起指定条目的“提醒”通知（加时取消提醒时调用）：移除排队中的同条目提醒，
        /// 若当前正在显示也立即切换到下一条/隐藏；其他条目的提示不受影响。
        /// </summary>
        public void HideRemindingNotice(string gameName)
        {
            var kept = new List<Notice>();
            while (_noticeQueue.Count > 0)
            {
                var n = _noticeQueue.Dequeue();
                if (n.Kind == PromptKind.Reminding && SameGame(n.GameName, gameName)) continue;
                kept.Add(n);
            }
            foreach (var n in kept) _noticeQueue.Enqueue(n);

            if (_currentNotice != null && _currentNotice.Kind == PromptKind.Reminding &&
                SameGame(_currentNotice.GameName, gameName))
            {
                _currentNotice = null;
                _autoHideTimer.Stop();
                ShowNextNotice();
            }
        }

        private static bool SameGame(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(b)) return true;
            return a != null && a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private void ShowNextNotice()
        {
            _autoHideTimer.Stop();

            if (_noticeQueue.Count == 0)
            {
                _currentNotice = null;
                if (_tickTimer.Enabled)
                {
                    _mode = OverlayMode.Countdown;
                    UpdateLayer();
                }
                else
                {
                    StopAndHide();
                }
                return;
            }

            _currentNotice = _noticeQueue.Dequeue();
            _mode = OverlayMode.Notice;
            _autoHideTimer.Interval = _currentNotice.DurationMs;

            UpdateLayer(); // 先绘制再显示
            Show();
            _autoHideTimer.Start();
        }

        public void StartCountdown(string gameName, int seconds)
        {
            // 倒计时优先：清掉排队中的通知，避免与倒计时画面互相打断
            _noticeQueue.Clear();
            _currentNotice = null;
            _autoHideTimer.Stop();

            _mode = OverlayMode.Countdown;
            _remaining = seconds;
            _tickTimer.Stop();

            UpdateLayer(); // 先绘制再显示
            Show();
            _tickTimer.Start();
        }

        public void StopAndHide()
        {
            _tickTimer.Stop();
            _autoHideTimer.Stop();
            _noticeQueue.Clear();
            _currentNotice = null;
            _mode = OverlayMode.None;
            Hide();
        }

        /// <summary>在设置页按行预览某一类提示；数秒后自动关闭，不影响真实监控。</summary>
        public static void ShowPreview(AppConfig config, PromptKind kind)
        {
            if (config == null) return;

            var preview = new CountdownForm(config);
            var closeTimer = new Timer { Interval = kind == PromptKind.Countdown ? 5600 : 6500 };
            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                try { preview.StopAndHide(); preview.Dispose(); } catch { }
            };
            preview.Finished += () =>
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                try { preview.StopAndHide(); preview.Dispose(); } catch { }
            };

            switch (kind)
            {
                case PromptKind.Countdown:
                    preview.StartCountdown("预览程序", 5);
                    break;
                case PromptKind.Cooldown:
                    preview.ShowCooldown("预览程序", 480);
                    break;
                case PromptKind.QuotaExhausted:
                    preview.ShowBlocked("预览程序");
                    break;
                case PromptKind.BlockedBySchedule:
                    preview.ShowBlockedBySchedule("预览程序");
                    break;
                default:
                    preview.ShowReminder("预览程序", 5);
                    break;
            }
            closeTimer.Start();
        }

        private void TickTimer_Tick(object sender, EventArgs e)
        {
            // 即使当前被通知横幅临时覆盖，倒计时的剩余秒数也必须继续走
            _remaining--;
            if (_remaining <= 0)
            {
                _tickTimer.Stop();
                _autoHideTimer.Stop();
                _noticeQueue.Clear();
                _currentNotice = null;
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

                if (_mode == OverlayMode.Notice && _currentNotice != null)
                {
                    // 背景改由 DrawNotice 按实际文字块绘制：多行合并为一整块，尺寸自适应文字。
                    DrawNotice(g, StyleOf(_currentNotice.Kind), _currentNotice);
                }
                else if (_mode == OverlayMode.Countdown)
                {
                    DrawBigNumber(g, _remaining.ToString());
                }

                ApplyBitmap(bmp);
            }
        }

        /// <summary>文字色（含字体色自身的不透明度）。</summary>
        private static Color TextColorOf(PromptStyleConfig style, double scale = 1.0)
        {
            int alpha = (int)Math.Round(Math.Max(0, Math.Min(100, style.TextAlphaPercent)) / 100.0 * 255 * scale);
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)),
                Math.Max(0, Math.Min(255, style.TextColorR)),
                Math.Max(0, Math.Min(255, style.TextColorG)),
                Math.Max(0, Math.Min(255, style.TextColorB)));
        }

        /// <summary>文字块底色（只铺在文字后面，不是整屏背景）。</summary>
        private static Color BlockBackColorOf(PromptStyleConfig style)
        {
            int alpha = (int)Math.Round(Math.Max(0, Math.Min(100, style.BackgroundAlphaPercent)) / 100.0 * 255);
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)),
                Math.Max(0, Math.Min(255, style.BackColorR)),
                Math.Max(0, Math.Min(255, style.BackColorG)),
                Math.Max(0, Math.Min(255, style.BackColorB)));
        }

        /// <summary>
        /// 提示层背景：按传入的文字外框画一整块矩形（多行已在外框里合并），
        /// 四周留内边距显得大气；颜色/透明度沿用该类型 PromptStyleConfig 的背景色与背景%。
        /// </summary>
        private void DrawNoticeBlockBackground(Graphics g, PromptStyleConfig style, RectangleF block)
        {
            var backColor = BlockBackColorOf(style);
            if (block.IsEmpty || backColor.A <= 0) return;

            float padX = Math.Max(26f, Math.Min(block.Width * 0.06f, 56f));
            float padY = Math.Max(20f, Math.Min(block.Height * 0.18f, 34f));
            var rect = RectangleF.Intersect(
                RectangleF.Inflate(block, padX, padY), new RectangleF(0, 0, Width, Height));
            if (rect.IsEmpty) return;

            using (var back = new SolidBrush(backColor))
                g.FillRectangle(back, rect);
        }

        private void DrawNotice(Graphics g, PromptStyleConfig style, Notice notice)
        {
            string family = SafeFontFamily(style.FontFamily);
            float bodySize = style.FontSize > 0 ? style.FontSize : 34f;
            float headSize = style.FontSize > 0 ? Math.Max(40f, style.FontSize * 1.8f) : 76f;

            int headY = Height / 2 - (int)(headSize * 1.45f);
            int bodyY = Height / 2 + (int)(bodySize * 1.2f);

            // 先量出两行文字外框并合并，再画一整块背景：多行只用一个背景，尺寸随文字自适应。
            var headRect = MeasureFitBounds(g, notice.Headline, family, headSize, FontStyle.Bold, headY);
            var bodyRect = MeasureFitBounds(g, notice.Message, family, bodySize,
                style.Bold ? FontStyle.Bold : FontStyle.Regular, bodyY);
            RectangleF block = headRect;
            if (!bodyRect.IsEmpty) block = block.IsEmpty ? bodyRect : RectangleF.Union(block, bodyRect);
            DrawNoticeBlockBackground(g, style, block);

            DrawCenteredFit(g, notice.Headline, family, headSize, FontStyle.Bold, style, headY);
            DrawCenteredFit(g, notice.Message, family, bodySize,
                style.Bold ? FontStyle.Bold : FontStyle.Regular, style, bodyY);
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

        /// <summary>按可用宽度自动缩小字号后居中绘制；文字块底色只铺在文字后面。</summary>
        private void DrawCenteredFit(Graphics g, string text, string fontFamily, float startPixelSize,
            FontStyle style, PromptStyleConfig prompt, int y)
        {
            if (string.IsNullOrEmpty(text)) return;

            float maxWidth = Math.Max(80, Width - 60);
            float size = startPixelSize;
            Font font = null;
            try
            {
                while (true)
                {
                    font?.Dispose();
                    font = new Font(fontFamily, size, style, GraphicsUnit.Pixel);
                    var measured = MeasurePrecise(g, text, font);
                    if (measured.Width <= maxWidth || size <= 18f) break;
                    size -= 4f;
                }

                // 按可见墨迹而不是字宽居中：中文标点（！、「」）与英文数字的侧边距差异很大，
                // 按字宽居中会让整行看起来偏右/偏左。
                var ink = MeasureInk(text, font);
                float x = (Width - ink.Width) / 2f - ink.Left;

                // 提示文字不再单独铺底色：四类提示统一由 UpdateLayer 绘制整块大背景。
                if (prompt.Shadow)
                {
                    using (var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
                        g.DrawString(text, font, shadow, x + Math.Max(2f, size * 0.03f), y + Math.Max(2f, size * 0.03f), PreciseText);
                }

                using (var brush = new SolidBrush(TextColorOf(prompt)))
                    g.DrawString(text, font, brush, x, y, PreciseText);
            }
            finally
            {
                font?.Dispose();
            }
        }

        /// <summary>与 DrawString 同一口径（去掉 GDI+ 默认两侧内边距），保证背景块与文字居中一致。</summary>
        private static SizeF MeasurePrecise(Graphics g, string text, Font font)
            => g.MeasureString(text ?? "", font, new PointF(0, 0), PreciseText);

        /// <summary>可见墨迹外框（相对 DrawString 的绘制原点）；空文本返回空。</summary>
        private static RectangleF MeasureInk(string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return RectangleF.Empty;
            using (var path = new GraphicsPath())
            {
                path.AddString(text, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, PreciseText);
                return path.GetBounds();
            }
        }

        /// <summary>按与 DrawCenteredFit 相同的缩放规则，返回该行文字在画布上的外框（用于合并背景块）。</summary>
        private RectangleF MeasureFitBounds(Graphics g, string text, string fontFamily,
            float startPixelSize, FontStyle style, int y)
        {
            if (string.IsNullOrEmpty(text)) return RectangleF.Empty;

            float maxWidth = Math.Max(80, Width - 60);
            float size = startPixelSize;
            Font font = null;
            try
            {
                while (true)
                {
                    font?.Dispose();
                    font = new Font(fontFamily, size, style, GraphicsUnit.Pixel);
                    var measured = MeasurePrecise(g, text, font);
                    if (measured.Width > maxWidth && size > 18f)
                    {
                        size -= 4f;
                        continue;
                    }
                    // 水平按墨迹宽度贴边，竖直沿用原来的行高（不改动字号/行距逻辑）
                    var ink = MeasureInk(text, font);
                    return new RectangleF((Width - ink.Width) / 2f, y, ink.Width, measured.Height);
                }
            }
            finally
            {
                font?.Dispose();
            }
        }

        private void DrawBigNumber(Graphics g, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var style = StyleOf(PromptKind.Countdown);
            string family = SafeFontFamily(style.FontFamily);
            FontStyle fontStyle = style.Bold ? FontStyle.Bold : FontStyle.Regular;

            // 只有当窗口大小或配置变化时，才重新计算字号（大幅提升性能，杜绝闪烁）
            if (_cachedFontSize == 0f || _cachedFontFam != family ||
                _cachedWidth != Width || _cachedHeight != Height ||
                _cachedHeightPct != _config.CountdownHeightPercent || _cachedWidthPct != _config.CountdownWidthPercent)
            {
                float heightPct = Math.Max(10, Math.Min(100, _config.CountdownHeightPercent)) / 100f;
                float widthPct = Math.Max(10, Math.Min(100, _config.CountdownWidthPercent)) / 100f;

                float baseSize = 400f;
                float maxScale = float.MaxValue;

                // 用 "88" 测一下，因为 2 个数字的宽度通常是最宽的
                using (var probe = new Font(family, baseSize, fontStyle, GraphicsUnit.Pixel))
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

                _cachedFontFam = family;
                _cachedWidth = Width;
                _cachedHeight = Height;
                _cachedHeightPct = _config.CountdownHeightPercent;
                _cachedWidthPct = _config.CountdownWidthPercent;
            }

            // 用户显式配置了字号（>0）时优先使用
            float fontSize = style.FontSize > 0 ? Math.Max(24f, Math.Min(1600f, style.FontSize)) : _cachedFontSize;

            using (var fontBig = new Font(family, fontSize, fontStyle, GraphicsUnit.Pixel))
            {
                var size = g.MeasureString(text, fontBig);
                float x = (Width - size.Width) / 2f;
                float y = (Height - size.Height) / 2f;

                var backColor = BlockBackColorOf(style);
                if (backColor.A > 0)
                {
                    const int pad = 24;
                    using (var back = new SolidBrush(backColor))
                        g.FillRectangle(back, x - pad, y - pad / 2, size.Width + pad * 2, size.Height + pad);
                }

                if (style.Shadow)
                {
                    using (var brushShadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0)))
                    {
                        float offset = Math.Max(4f, fontSize * 0.03f);
                        g.DrawString(text, fontBig, brushShadow, x + offset, y + offset);
                    }
                }

                using (var brushMain = new SolidBrush(TextColorOf(style)))
                    g.DrawString(text, fontBig, brushMain, x, y);
            }
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
