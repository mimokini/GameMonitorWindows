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
        /// <summary>本轮“无操作”计时起点：淡显与贴边的到期顺序由 PlanHide 统一判定。</summary>
        private DateTime _hideTimerStartedAt = DateTime.Now;
        /// <summary>淡显 / 贴边滑动动画驱动（16ms 一帧，不阻塞 UI 线程）。</summary>
        private readonly Timer _animTimer;
        /// <summary>穿透态下轮询 Ctrl+Alt 是否按下（120ms 一次，仅穿透开启时运行）。</summary>
        private readonly Timer _transparencyTimer;

        /// <summary>空闲态时间文字往复滚动（约 30fps，只在允许滚动时运行）。</summary>
        private readonly Timer _marqueeTimer;
        private const int MarqueeFrameMs = 33;
        private const double MarqueeSpeedPxPerSec = 20.0;
        /// <summary>两端各停留多久（毫秒），便于看清「已用 / 限额」两段完整文字。</summary>
        private const int MarqueeHoldMs = 3000;
        private double _marqueeOffset;       // 0.._marqueeTravel 之间往复，不累计无限偏移
        private double _marqueeTravel = 1.0;
        private int _marqueeDir = 1;         // 1=向左（偏移增大），-1=向右（偏移减小）
        private DateTime _marqueeHoldUntil = DateTime.MinValue;
        private bool _marqueePrimed;         // 首次启动时从「已用」端停留起步
        private DateTime _marqueeLastTick;
        private int _marqueeFrames;          // 实际绘制帧数，便于验证停表

        /// <summary>用户设置的“鼠标穿透”目标态。</summary>
        private bool _mouseTransparent;
        /// <summary>是否因“按拖动热键想拖动”而临时摘掉了 WS_EX_TRANSPARENT。</summary>
        private bool _altPeekActive;
        /// <summary>当前是否处于“可拖动”提示态（Ctrl+Alt 按住且光标在窗内）。</summary>
        private bool _dragReady;

        // 淡显进度：1 = 完全显示，0 = 淡显态；由 _animTimer 逐帧逼近目标，鼠标进入时从当前值反向渐变。
        private double _showT = 1.0;
        private bool _fadeAnimating;
        private bool _posAnimating;
        private int _posFromLeft, _posFromTop, _posToLeft, _posToTop;
        private DateTime _posAnimStart;
        /// <summary>贴边方向：0 左、1 右、2 上、3 下；-1 表示未贴边隐藏。</summary>
        private int _edgeSide = -1;
        private const int FadeDurationMs = 180;
        private const int SlideDurationMs = 200;

        private List<GameStatus> _statuses = new List<GameStatus>();
        private List<OverlayLine> _lines = new List<OverlayLine>();
        private string _titleText = DefaultTitleText;
        private bool _autoHidden;
        private bool _dragging;
        /// <summary>监控器缺失的 Warn 只记一次，避免每秒刷新刷爆日志。</summary>
        private bool _missingMonitorWarned;
        private Point _dragOffset;

        // 贴边隐藏：记录隐藏前的坐标，恢复时原样还原；隐藏形态为只露 3px 窄条。
        private bool _edgeHidden;
        private int _restoreLeft;
        private int _restoreTop;
        /// <summary>进入贴边隐藏时所在屏幕的工作区；隐藏后窗口大部分在屏幕外，FromControl 可能误判到相邻屏。</summary>
        private Rectangle _hideArea;

        // 吸附边：水平 0=自由 1=左 2=右，垂直 0=自由 1=上 2=下。
        // 吸附后宽度/高度变化按吸附边反算坐标，避免只按左边缘定位导致右侧留空/窄条跑出屏幕。
        private int _anchorX;
        private int _anchorY;
        private int _anchorOffsetX;
        private int _anchorOffsetY;
        /// <summary>吸附边是否已判定（老配置/首次启动需按当前坐标判定一次）。</summary>
        private bool _anchorResolved;

        private const string DefaultTitleText = "今日游戏时长";
        /// <summary>上一次刷新时处于运行态的被监控程序（用于识别“新程序开始运行/切换到另一个程序”）。</summary>
        private readonly HashSet<string> _lastRunningGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>距屏幕边缘小于该像素即视为“贴边”。</summary>
        private const int EdgeThresholdPx = 8;
        /// <summary>贴边隐藏时保留的可见窄条宽度。</summary>
        private const int EdgeVisiblePx = 3;
        /// <summary>“贴边吸附”判定阈值（沿用原 SnapToEdge 的 25px），吸附边判定与之一致，不另造一套。</summary>
        private const int SnapThresholdPx = 25;

        private const int DotSize = 10;
        private const int DotGap = 8;
        /// <summary>程序名后的全角冒号：仅悬浮窗显示用，不进配置/关键字/进程名。</summary>
        private const string Colon = "：";
        /// <summary>单行文本最大宽度，超出时截断程序名，保留数值部分。</summary>
        private const int MaxTextWidth = 420;
        /// <summary>精确字形排版：去掉 GDI+ 默认的左右各 1/6 em 内边距，测量与绘制同一口径，居中才不会偏移。</summary>
        private static readonly StringFormat PreciseText = StringFormat.GenericTypographic;

        // 状态圆点默认颜色（可在“提示与样式 → 悬浮窗样式”里逐状态修改）
        private static readonly Color DotIdleDefault = Color.FromArgb(154, 163, 175);    // #9CA3AF 灰：未运行
        private static readonly Color DotRunningDefault = Color.FromArgb(34, 197, 94);   // #22C55E 绿：运行中
        private static readonly Color DotRemindingDefault = Color.FromArgb(245, 158, 11); // #F59E0B 橙：提醒/倒计时
        private static readonly Color DotCooldownDefault = Color.FromArgb(255, 193, 7);  // #FFC107 黄：间隔期
        private static readonly Color DotQuotaDefault = Color.FromArgb(239, 68, 68);     // #EF4444 红：配额用完

        private sealed class OverlayLine
        {
            public Color Dot;
            public bool Filled;
            public string Name;
            public string Tail;
            public string Display;
            /// <summary>是否为“真正空闲”行：只有这类行的时间文字跑马灯滚动。</summary>
            public bool Idle;
            /// <summary>空闲行的另一段文字（已用 / 限额 交替），非空闲行为 null。</summary>
            public string TailAlt;
            /// <summary>实际绘制用的程序名（过长时可能被截断）及其宽度。</summary>
            public string NameText;
            public float NameWidth;
            /// <summary>时间文字可视区的固定宽度 = max(已用宽度, 限额宽度)，滚动期间不变。</summary>
            public float TailViewport;
            public float TailWidth;
            public float TailAltWidth;
            /// <summary>两段文字之间的空档（= 可视区宽 - 已用宽），保证两端都不出现半截文字。</summary>
            public float SegGap;
            /// <summary>往复滚动的单程距离（= 可视区宽）。</summary>
            public float Travel;
        }

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
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int GWL_EXSTYLE = -20;
        private const int VK_CONTROL = 0x11;
        private const int VK_MENU = 0x12;
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
                _anchorX = 2;
                _anchorOffsetX = 30;
                _anchorY = 1;
                _anchorOffsetY = 30;
                _anchorResolved = true;
                Left = wa.Right - Width - _anchorOffsetX;
                Top = wa.Top + _anchorOffsetY;
                _config.StatusOverlayX = Left;
                _config.StatusOverlayY = Top;
                SyncAnchorToConfig();
            }
            else
            {
                // 有吸附记录就按吸附边 + 偏移还原；老配置没有吸附边字段时先按绝对坐标落位，
                // 吸附边留到 OnLoad 再判定——构造时 Width/Height 还是占位值，据此算右/下间隙会算错
                LoadAnchorFromConfig();
                if (_anchorResolved) ApplyAnchorPosition();
                else
                {
                    Left = _config.StatusOverlayX;
                    Top = _config.StatusOverlayY;
                }
            }

            _refreshTimer = new Timer { Interval = 1000 };
            _refreshTimer.Tick += (s, e) =>
            {
                RefreshContent();
                CheckCursorForAutoHide();
            };

            // 到点时间由 ArmHideTimer 每次启动前重算，这里不设初值
            _autoHideTimer = new Timer();
            _autoHideTimer.Tick += (s, e) => AutoHideNow();

            _animTimer = new Timer { Interval = 16 };
            _animTimer.Tick += (s, e) => OnAnimTick();

            _transparencyTimer = new Timer { Interval = 120 };
            _transparencyTimer.Tick += (s, e) => OnTransparencyTimer();
            SyncTransparencyState();

            // 空闲态时间文字跑马灯：只有需要滚动时才启动，停表后不再做无效重绘
            _marqueeTimer = new Timer { Interval = MarqueeFrameMs };
            _marqueeTimer.Tick += (s, e) => OnMarqueeTick();
        }

        /// <summary>逐帧推进淡显与贴边滑动；两者都到位后自动停表。</summary>
        private void OnAnimTick()
        {
            if (_fadeAnimating)
            {
                double target = _autoHidden ? 0.0 : 1.0;
                double step = 16.0 / FadeDurationMs;
                if (_showT < target) _showT = Math.Min(target, _showT + step);
                else if (_showT > target) _showT = Math.Max(target, _showT - step);
                if (Math.Abs(_showT - target) < 1e-6) { _showT = target; _fadeAnimating = false; }
                ApplyBitmap();
            }

            if (_posAnimating)
            {
                double p = Math.Min(1.0, (DateTime.Now - _posAnimStart).TotalMilliseconds / SlideDurationMs);
                double ease = 1 - Math.Pow(1 - p, 2); // ease-out
                int left = _posFromLeft + (int)Math.Round((_posToLeft - _posFromLeft) * ease);
                int top = _posFromTop + (int)Math.Round((_posToTop - _posFromTop) * ease);
                Left = left;
                Top = top;
                ApplyBitmap();
                if (p >= 1.0)
                {
                    Left = _posToLeft;
                    Top = _posToTop;
                    _posAnimating = false;
                }
            }

            if (!_fadeAnimating && !_posAnimating) _animTimer.Stop();
        }

        private void StartSlideTo(int left, int top)
        {
            _posFromLeft = Left;
            _posFromTop = Top;
            _posToLeft = left;
            _posToTop = top;
            _posAnimStart = DateTime.Now;
            _posAnimating = true;
            _animTimer.Start();
        }

        /// <summary>把进行中的滑动立即落位到目标点（鼠标接管位置前调用）。</summary>
        private void FinishSlideNow()
        {
            if (!_posAnimating) return;
            _posAnimating = false;
            Left = _posToLeft;
            Top = _posToTop;
            ApplyBitmap();
        }

        /// <summary>「无操作自动隐藏」或「贴边隐藏」任一开启时需要跑同一个隐藏计时器。</summary>
        private bool HideFeaturesActive => _config.StatusOverlayAutoHide || _config.StatusOverlayEdgeHide;

        private void StartHideTimer()
        {
            if (!HideFeaturesActive) { _autoHideTimer.Stop(); return; }
            _hideTimerStartedAt = DateTime.Now; // 重新开始一轮“无操作”计时
            ArmHideTimer(0);
        }

        /// <summary>一次无操作隐藏的判定结果：此刻该做的动作 + 下一个未完成节点的到期秒数。</summary>
        private struct HidePlan
        {
            public bool FadeDue;      // 此刻应进入半透明
            public bool EdgeDue;      // 此刻应收成窄条
            public double NextAt;     // 下一个未完成节点距本轮起点的秒数；MaxValue 表示没有节点
        }

        /// <summary>
        /// 淡显与贴边串联的唯一判定入口（ArmHideTimer 与 AutoHideNow 共用，避免两处规则漂移）：
        /// 贴边可用且淡显秒数 &lt; 贴边秒数 → 先淡显、继续到贴边秒数再收窄条；
        /// 贴边可用且淡显秒数 &gt;= 贴边秒数 → 不做淡显，直接按贴边时间收窄条；
        /// 未贴边 → 只走淡显。elapsedSeconds 为本轮无操作已过去的秒数。
        /// </summary>
        private HidePlan PlanHide(double elapsedSeconds)
        {
            bool edgeOn = EdgeHideApplicableNow();
            bool fadeOn = _config.StatusOverlayAutoHide;
            double fadeSec = Math.Max(1, _config.StatusOverlayAutoHideSeconds);
            double edgeSec = Math.Max(1, _config.StatusOverlayEdgeHideSeconds);

            // 贴边时间先到（或与淡显相等）时淡显没有意义，直接收窄条
            bool fadeSuppressed = edgeOn && fadeOn && fadeSec >= edgeSec;
            bool fadePending = fadeOn && !_autoHidden && !fadeSuppressed;

            var plan = new HidePlan { NextAt = double.MaxValue };
            if (fadePending)
            {
                plan.FadeDue = elapsedSeconds >= fadeSec;
                plan.NextAt = fadeSec - elapsedSeconds;
            }
            if (edgeOn)
            {
                plan.EdgeDue = elapsedSeconds >= edgeSec;
                plan.NextAt = Math.Min(plan.NextAt, edgeSec - elapsedSeconds);
            }
            return plan;
        }

        /// <summary>按统一判定排下一次到期检查。</summary>
        private void ArmHideTimer(double elapsedSeconds)
        {
            HidePlan plan = PlanHide(elapsedSeconds);
            if (plan.NextAt == double.MaxValue)
            {
                _autoHideTimer.Stop();
                return;
            }

            double next = plan.NextAt;
            if (next <= 0) next = 0.05;
            _autoHideTimer.Interval = Math.Max(50, (int)Math.Round(next * 1000));
            _autoHideTimer.Start();
        }

        /// <summary>贴边隐藏此刻是否适用：开关开启、窗口贴边、且还不在窄条态。</summary>
        private bool EdgeHideApplicableNow()
            => _config.StatusOverlayEdgeHide && !_edgeHidden && IsAtEdgeNow();

        /// <summary>只读判定：窗口是否贴在所在屏幕工作区边缘（与 TryEdgeHide 同一套阈值，不移动窗口）。</summary>
        private bool IsAtEdgeNow()
        {
            var wa = CurrentWorkArea();
            int best = Math.Min(
                Math.Min(Math.Abs(Left - wa.Left), Math.Abs(wa.Right - (Left + Width))),
                Math.Min(Math.Abs(Top - wa.Top), Math.Abs(wa.Bottom - (Top + Height))));
            return best <= EdgeThresholdPx;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000;
                cp.ExStyle |= 0x00000080;
                // WS_EX_NOACTIVATE：窗口永不抢前台焦点。穿透态下临时摘掉穿透并拖动时也不会激活，
                // 不会把键盘焦点从底层游戏抢走；拖动只依赖鼠标消息与捕获，不依赖窗口被激活。
                cp.ExStyle |= 0x08000000;
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
            _mouseTransparent = transparent;

            if (transparent)
            {
                ApplyTransparencyExStyle(true);
                if (IsHandleCreated)
                {
                    _transparencyTimer.Start();
                    SyncTransparencyState();
                }
            }
            else
            {
                _transparencyTimer.Stop();
                _altPeekActive = false;
                _dragReady = false;
                ApplyTransparencyExStyle(false);
            }

            if (_autoHidden) { _autoHidden = false; }
            if (IsHandleCreated) ApplyBitmap();
        }

        /// <summary>按“是否真的穿透”写 WS_EX_TRANSPARENT。只有状态变化时才调用，避免逐帧 FRAMECHANGED 造成闪烁。</summary>
        private void ApplyTransparencyExStyle(bool transparent)
        {
            if (!IsHandleCreated) return;

            long ex = GetWindowLongSafe(Handle, GWL_EXSTYLE);
            if (transparent) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLongSafe(Handle, GWL_EXSTYLE, ex);

            SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }

        /// <summary>拖动热键是否按下：必须 Ctrl 与 Alt 同时按下（单独 Alt 在游戏里太常见，容易误触发）。</summary>
        private static bool IsDragHotkeyDown()
            => (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0
            && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

        /// <summary>
        /// 每一拍都按“Ctrl+Alt 是否同时按下 + 光标是否在窗口矩形内 + 是否正在拖动”重新推导目标穿透态，
        /// 再与实际窗口样式比对，不一致才改样式 —— 自愈式推导，不依赖一次性开/关标记，
        /// 任何漏掉的恢复路径（失焦、快速松开、跨屏移动）都会在下一拍自动纠正。
        /// 用 GetAsyncKeyState 轮询而不是 WH_KEYBOARD_LL：反作弊游戏对低级键盘钩子敏感，
        /// 轮询不注入系统输入、不被拦截，120ms(≈8Hz) 只有两次轻量 API 调用，无分配，不影响性能与帧率。
        /// 贴边隐藏态不参与（窗口只剩 3px 窄条，鼠标移上去本来就会先恢复整窗），属设计如此。
        /// </summary>
        private void SyncTransparencyState()
        {
            if (!IsHandleCreated) return;

            bool hotkey = IsDragHotkeyDown();

            // 贴边窄条 + Ctrl+Alt：直接滑出整窗，不必等 1 秒后鼠标轮询兜底，随后即可拖动
            if (_edgeHidden && hotkey && EdgeStripBounds().Contains(Cursor.Position))
                RestoreFromEdge();

            // 拖动中松开任一键（Ctrl 或 Alt）= 放弃拖动：就地停在当前位置，收尾吸附判定与持久化，释放捕获后恢复穿透
            if (_dragging && !hotkey)
            {
                _dragging = false;
                Capture = false;
                ResolveAnchorFromPosition(_config.StatusOverlaySnap);
                SavePosition();
                StartHideTimer();
            }

            bool inside = Bounds.Contains(Cursor.Position);
            bool peek = _mouseTransparent && !_edgeHidden && hotkey && (inside || _dragging);
            bool desiredTransparent = _mouseTransparent && !peek;
            _altPeekActive = peek;

            if (_dragReady != peek)
            {
                // 可拖动提示只在状态切换时重绘一次，不参与逐帧动画
                _dragReady = peek;
                if (Visible) ApplyBitmap();
            }

            bool actualTransparent = (GetWindowLongSafe(Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0;
            if (actualTransparent != desiredTransparent) ApplyTransparencyExStyle(desiredTransparent);
        }

        private void OnTransparencyTimer()
        {
            if (!_mouseTransparent || !Visible || !IsHandleCreated)
            {
                _transparencyTimer.Stop();
                return;
            }
            SyncTransparencyState();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // 失焦不是唯一兜底：这里主动重算一次，真正兜底的是 120ms 自愈推导
            SyncTransparencyState();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            RefreshContent();
            ClampToVirtualScreen();
            // 老配置（无吸附边字段）此时已按实际内容尺寸落位，补做一次吸附边判定：只记录不移动
            if (!_anchorResolved) ResolveAnchorFromPosition(false);

            _refreshTimer.Start();
            if (HideFeaturesActive) StartHideTimer();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (IsHandleCreated)
            {
                if (Visible)
                {
                    _refreshTimer.Start();
                    if (HideFeaturesActive) StartHideTimer();
                    if (_mouseTransparent) { _transparencyTimer.Start(); SyncTransparencyState(); }
                    SyncMarqueeTimer(); // 重新显示后按内容恢复空闲态滚动节拍
                }
                else
                {
                    _refreshTimer.Stop();
                    _autoHideTimer.Stop();
                    _transparencyTimer.Stop();
                    _marqueeTimer.Stop();
                    _dragReady = false; // 隐藏时不做提示重绘，重新显示后由自愈推导决定
                    // 隐藏时不改样式；重新显示后由自愈推导决定
                }
            }
        }

        public void RefreshContent()
        {
            if (_monitor == null)
            {
                // 防御：浮层被独立构造（设计器/布局测试）时没有状态源。
                // 记录一条 Warn 后跳过本次刷新并保留上一帧，避免整个托盘程序弹出异常对话框。
                if (!_missingMonitorWarned)
                {
                    _missingMonitorWarned = true;
                    AppLog.Warn("状态浮层跳过刷新：监控器未初始化（_monitor 为 null）。");
                }
                return;
            }

            _statuses = _monitor.GetStatuses();
            _lines = _statuses.Select(BuildLine).ToList();
            RecomputeLayout();
            ShowIfNewGameStarted();
        }

        /// <summary>
        /// 按缓存的 _statuses/_lines 重算标题、各行宽度与窗口尺寸，尺寸变化后沿用吸附边定位。
        /// 空闲行的时间文字可视区按「已用 / 限额」较长者一次算定，滚动期间宽度不再变化，
        /// 因此不会触发吸附/贴边重排，动画与隐藏计时的时序互不打扰。
        /// </summary>
        private void RecomputeLayout()
        {
            using (var g = CreateGraphics())
            {
                _titleText = BuildTitleText();
                var titleSize = MeasurePrecise(g, _titleText, _fontTitle);
                if (titleSize.Width > MaxTextWidth)
                {
                    _titleText = FitText(_titleText, _fontTitle, g, MaxTextWidth);
                    titleSize = MeasurePrecise(g, _titleText, _fontTitle);
                }
                int lineH = (int)Math.Ceiling(_fontLine.GetHeight(g)) + 6;
                int maxWidth = 0;
                double maxTravel = 0;

                foreach (var line in _lines)
                {
                    if (line.Idle)
                    {
                        // 固定可视区：取「已用 N 分」与「限额 M 分」里较长者，两段都能完整显示
                        float w1 = MeasurePrecise(g, line.Tail, _fontLine).Width;
                        float w2 = MeasurePrecise(g, line.TailAlt, _fontLine).Width;
                        float viewport = Math.Max(w1, w2);
                        float colonW = MeasurePrecise(g, Colon, _fontLine).Width;

                        // 程序名后带全角冒号（仅显示用）：冒号字形自带右侧留白，直接接时间文字不贴也不疏
                        float maxNameW = MaxTextWidth - DotSize - DotGap - viewport;
                        if (maxNameW < 40f) maxNameW = 40f; // 极端长名称兜底，避免可视区被挤没
                        line.NameText = line.Name + Colon;
                        line.NameWidth = MeasurePrecise(g, line.NameText, _fontLine).Width;
                        if (line.NameWidth > maxNameW)
                        {
                            string fitted = FitText(line.Name, _fontLine, g, (int)Math.Max(10f, maxNameW - colonW));
                            line.NameText = fitted + Colon;
                            line.NameWidth = MeasurePrecise(g, line.NameText, _fontLine).Width;
                        }

                        line.TailViewport = viewport;
                        line.TailWidth = w1;
                        line.TailAltWidth = w2;
                        // 两段之间留出 (可视区 - 已用宽) 的空档：起点时「限额」正好排在可视区右边缘外，
                        // 往复两端都不会出现半截文字；滚动行程 = 可视区宽度
                        line.SegGap = Math.Max(0f, viewport - w1);
                        line.Travel = viewport;
                        line.Display = line.NameText + line.Tail + " / " + line.TailAlt;

                        if (line.Travel > maxTravel) maxTravel = line.Travel;
                        int linePx = (int)Math.Ceiling(line.NameWidth + viewport);
                        if (linePx > maxWidth) maxWidth = linePx;
                        continue;
                    }

                    line.NameText = null;
                    line.Display = FitLine(line.Name, line.Tail, g);
                    int lineWidth = (int)Math.Ceiling(MeasurePrecise(g, line.Display, _fontLine).Width);
                    if (lineWidth > maxWidth) maxWidth = lineWidth;
                }

                // 数字位数变化可能让行程变短，把偏移夹回合法区间，避免停在“滚过头”的位置
                _marqueeTravel = Math.Max(1.0, maxTravel);
                if (_marqueeOffset > _marqueeTravel)
                {
                    _marqueeOffset = _marqueeTravel;
                    _marqueeDir = -1;
                }

                int w = _padding * 2 + DotSize + DotGap + Math.Max((int)Math.Ceiling(titleSize.Width), maxWidth);
                // 行距 lineH 只在“行与行之间”生效：最后一行不再额外留 6px，否则底部留白比顶部多 6px
                int rowTextH = (int)Math.Ceiling(_fontLine.GetHeight(g));
                int h = _padding * 2 + (int)titleSize.Height + 6
                    + lineH * (Math.Max(1, _statuses.Count) - 1) + rowTextH;

                if (Width != w || Height != h)
                {
                    Width = w;
                    Height = h;
                    // 尺寸变化后按吸附边反算坐标：右侧吸附时宽度变化仍贴右边（旧实现只沿用左边缘，
                    // 变窄后右侧会留空）；自由位置沿用原来的工作区收敛逻辑，不会被强行吸附。
                    // 若当前处于贴边隐藏态，只重算窄条位置，保持隐藏不弹出。
                    if (_edgeHidden) ReapplyEdgeStrip();
                    else ApplyPositionAfterResize();
                }
            }
            ApplyBitmap();
        }

        /// <summary>是否有真正空闲行（只有它为 true 时才需要滚动）。</summary>
        private bool HasIdleLines => _lines.Any(l => l.Idle);

        /// <summary>
        /// 空闲跑马灯是否该运行：可见 + 不是窄条/拖动 + 未淡显到中途（淡显过渡期间暂停滚动，
        /// 但透明度计算不受影响）。淡显到底（_showT≈0）后也不再跑，避免无意义重绘。
        /// </summary>
        private bool MarqueeShouldRun()
        {
            if (!Visible || !IsHandleCreated) return false;
            if (_edgeHidden || _dragging) return false;
            if (_showT < 0.999 || _fadeAnimating) return false;
            return HasIdleLines;
        }

        /// <summary>按当前状态启停帧定时器：条件不满足立即停表，不保留空转的动画循环。</summary>
        private void SyncMarqueeTimer()
        {
            if (MarqueeShouldRun())
            {
                if (!_marqueeTimer.Enabled)
                {
                    _marqueeLastTick = DateTime.Now; // 从暂停位置继续，不重置 _marqueeOffset
                    if (!_marqueePrimed)
                    {
                        // 首次从「已用 N 分」起点开始，先停留 1 秒再向左滚
                        _marqueePrimed = true;
                        _marqueeHoldUntil = _marqueeLastTick.AddMilliseconds(MarqueeHoldMs);
                    }
                    _marqueeTimer.Start();
                }
            }
            else
            {
                _marqueeTimer.Stop();
            }
        }

        /// <summary>
        /// 往复滚动：从「已用 N 分」起，向左移动直到「限额 M 分」完整落在可视区，停留 1 秒，
        /// 再向右滚回起点，停留 1 秒，如此往复。偏移始终夹在 [0, Travel] 之间，不会无限累计。
        /// </summary>
        private void OnMarqueeTick()
        {
            if (!MarqueeShouldRun()) { _marqueeTimer.Stop(); return; }

            var now = DateTime.Now;
            if (now < _marqueeHoldUntil)
            {
                // 端点停留：位置不变，不做无效重绘（透明度/隐藏计时都不受影响）
                _marqueeLastTick = now;
                return;
            }

            double dt = (now - _marqueeLastTick).TotalMilliseconds;
            _marqueeLastTick = now;
            if (dt <= 0) return;

            _marqueeOffset += _marqueeDir * (MarqueeSpeedPxPerSec * dt / 1000.0);
            if (_marqueeOffset >= _marqueeTravel)
            {
                _marqueeOffset = _marqueeTravel;
                _marqueeDir = -1;
                _marqueeHoldUntil = now.AddMilliseconds(MarqueeHoldMs);
            }
            else if (_marqueeOffset <= 0)
            {
                _marqueeOffset = 0;
                _marqueeDir = 1;
                _marqueeHoldUntil = now.AddMilliseconds(MarqueeHoldMs);
            }

            _marqueeFrames++;
            ApplyBitmap();
        }

        /// <summary>
        /// 贴边隐藏态下，如果有被监控程序“新开始运行”（从空闲变为运行，或换成另一个程序），
        /// 用现有的滑入动画把悬浮窗从贴边位置划出来，按“无操作自动隐藏秒数”停留后再划回。
        /// 只比较“新出现的运行程序”，已在运行的不重复触发；鼠标在窗口上/拖动中由 AutoHideNow 负责不收起。
        /// 与“无操作自动淡显”的关系由 PlanHide 统一判定：淡显秒数 &lt; 贴边秒数时先半透明、
        /// 到贴边秒数再收窄条；淡显秒数 &gt;= 贴边秒数时不做淡显，直接收窄条。
        /// </summary>
        private void ShowIfNewGameStarted()
        {
            var runningNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in _statuses)
            {
                if (!s.IsRunning) continue;
                string key = s.Game?.ProcessName ?? s.Game?.DisplayName;
                if (!string.IsNullOrEmpty(key)) runningNow.Add(key);
            }

            bool started = false;
            foreach (var key in runningNow)
            {
                if (!_lastRunningGames.Contains(key))
                {
                    started = true;
                    break;
                }
            }

            _lastRunningGames.Clear();
            _lastRunningGames.UnionWith(runningNow);

            if (started && _edgeHidden) RestoreFromEdge();
        }

        /// <summary>
        /// 悬浮窗顶部说明文字：模板来自「提示文案与字体 → 悬浮窗说明文字」，支持 {game} {used} {limit}；
        /// 留空或全空白回退默认“今日游戏时长”。
        /// </summary>
        private string BuildTitleText()
        {
            string raw = _config?.StatusOverlayTitleText ?? "";
            if (string.IsNullOrWhiteSpace(raw)) return DefaultTitleText;

            string game;
            if (_statuses.Count == 0) game = "暂无";
            else if (_statuses.Count == 1) game = _statuses[0].Game?.DisplayName ?? _statuses[0].Game?.ProcessName ?? "";
            else game = $"{_statuses.Count} 个程序";

            int usedSeconds = 0;
            foreach (var s in _statuses) usedSeconds = Math.Max(usedSeconds, Math.Max(0, s.TotalUsedSecondsToday));
            int usedMinutes = (int)Math.Ceiling(usedSeconds / 60.0);
            int totalLimit = Math.Max(0, _config?.DailyTotalLimitMinutes ?? 0);

            return raw.Trim()
                .Replace("{game}", game)
                .Replace("{used}", usedMinutes.ToString())
                .Replace("{limit}", totalLimit > 0 ? totalLimit.ToString() : "不限");
        }

        /// <summary>与 DrawString 同一口径（PreciseText）测量文本，避免默认内边距造成居中偏差。</summary>
        private static SizeF MeasurePrecise(Graphics g, string text, Font font)
            => g.MeasureString(text ?? "", font, new PointF(0, 0), PreciseText);

        /// <summary>按最大宽度截断文本并补“…”。</summary>
        private static string FitText(string text, Font font, Graphics g, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (MeasurePrecise(g, text, font).Width <= maxWidth) return text;
            for (int len = text.Length - 1; len >= 1; len--)
            {
                string candidate = text.Substring(0, len).TrimEnd() + "…";
                if (MeasurePrecise(g, candidate, font).Width <= maxWidth) return candidate;
            }
            return text.Substring(0, 1) + "…";
        }

        private OverlayLine BuildLine(GameStatus s)
        {
            // 文案与圆点归属统一由 PhaseText 给出，数字来自 GetStatuses，悬浮窗不再自行重算；
            // 空闲行同时取「已用 N 分」与「限额 M 分」两段，交给跑马灯在固定可视区内循环滚动
            bool idle = s.Phase == MonitorPhase.Idle && !s.IsRunning && !s.IsPaused && !s.InAllowRulePeriod;
            PhaseInfo info = PhaseText.Describe(s, idle ? PhaseText.IdleVariantUsed : PhaseText.IdleVariantFull);
            return new OverlayLine
            {
                Name = s.Game.DisplayName,
                Dot = DotColorFor(info.DotKey),
                Filled = info.Filled,
                Tail = info.Text,
                TailAlt = idle ? PhaseText.Describe(s, PhaseText.IdleVariantLimit).Text : null,
                Idle = idle
            };
        }

        /// <summary>读取配置里的状态圆点颜色；配置缺失时回落到默认色。</summary>
        private Color DotColorFor(string key)
        {
            var c = _config?.DotColors;
            ColorSetting setting = null;
            Color fallback = DotIdleDefault;
            switch (key)
            {
                case "running": setting = c?.Running; fallback = DotRunningDefault; break;
                case "reminding": setting = c?.Reminding; fallback = DotRemindingDefault; break;
                case "cooldown": setting = c?.Cooldown; fallback = DotCooldownDefault; break;
                case "quota": setting = c?.Quota; fallback = DotQuotaDefault; break;
                default: setting = c?.Idle; fallback = DotIdleDefault; break;
            }
            if (setting == null) return fallback;
            return Color.FromArgb(
                Math.Max(0, Math.Min(255, setting.R)),
                Math.Max(0, Math.Min(255, setting.G)),
                Math.Max(0, Math.Min(255, setting.B)));
        }

        /// <summary>文本超宽时截断程序名并保留“…”与后面的数值部分（程序名后统一带全角冒号）。</summary>
        private string FitLine(string name, string tail, Graphics g)
        {
            string text = name + "：" + tail;
            if (MeasurePrecise(g, text, _fontLine).Width <= MaxTextWidth) return text;

            for (int len = name.Length - 1; len >= 1; len--)
            {
                string candidate = name.Substring(0, len).TrimEnd() + "…：" + tail;
                if (MeasurePrecise(g, candidate, _fontLine).Width <= MaxTextWidth) return candidate;
            }
            return "…：" + tail;
        }

        private void ApplyBitmap()
        {
            if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                int fullPercent = Math.Max(16, Math.Min(100, _config.StatusOverlayOpacityPercent));
                double t = Math.Max(0.0, Math.Min(1.0, _showT));
                int percent = (int)Math.Round(16 + (fullPercent - 16) * t);
                int alpha = (int)Math.Round(percent / 100.0 * 255);
                if (alpha < 0) alpha = 0;
                if (alpha > 255) alpha = 255;

                Color backColor = Color.FromArgb(alpha,
                    _config.StatusOverlayBackColorR,
                    _config.StatusOverlayBackColorG,
                    _config.StatusOverlayBackColorB);

                // 圆角底色：四档半径由配置决定（0=方角）。分层窗口是逐像素 Alpha，
                // 圆角外侧保持位图的透明像素即可透出桌面，圆弧必须走 AntiAlias 填充而不是裁掉四角，
                // 否则边缘会出现硬锯齿。贴边隐藏的窄条按“短边一半”收敛半径，不会被画成胶囊或消失。
                FillBackground(g, backColor, _config.StatusOverlayCornerRadius);

                // 可拖动提示：Ctrl+Alt 按住且光标在窗内时才画一圈极淡的亮边，只改变描边、不动底色与状态色
                if (_dragReady) DrawDragReadyBorder(g, _config.StatusOverlayCornerRadius);

                int textAlpha = (int)Math.Round(70 + 185 * t);
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
                    var titleSize = MeasurePrecise(g, _titleText, _fontTitle);
                    float titleX = (Width - titleSize.Width) / 2f;
                    g.DrawString(_titleText, _fontTitle, titleBrush, titleX, _padding, PreciseText);

                    int y = _padding + (int)titleSize.Height + 6;
                    int lineH = (int)Math.Ceiling(_fontLine.GetHeight(g)) + 6;

                    for (int i = 0; i < _lines.Count; i++)
                    {
                        var line = _lines[i];
                        int dotY = y + (int)Math.Round(_fontLine.GetHeight(g) / 2f) - DotSize / 2;

                        if (line.Idle)
                        {
                            // 空闲行：「圆点 + 程序名：」静止，右侧固定可视区内「已用 / 限额」两段往复滚动。
                            // 窗口宽度按较长的那段一次算定，滚动中宽度不变，因此不会触发吸附/贴边重排。
                            float nameW = line.NameWidth;
                            float viewport = line.TailViewport;
                            float groupW = DotSize + DotGap + nameW + viewport;
                            float groupX = (Width - groupW) / 2f;

                            Color dotColor = Color.FromArgb(textAlpha, line.Dot);
                            DrawDot(g, (int)Math.Round(groupX), dotY, dotColor, line.Filled);

                            float nameX = groupX + DotSize + DotGap;
                            using (var b = new SolidBrush(baseText))
                                g.DrawString(line.NameText, _fontLine, b, nameX, y, PreciseText);

                            // 冒号自带右侧留白，时间文字直接接在程序名之后
                            float tailX = nameX + nameW;
                            var tailColor = Color.FromArgb(textAlpha,
                                _config.StatusOverlayTextColorR,
                                _config.StatusOverlayTextColorG,
                                _config.StatusOverlayTextColorB);

                            // 可视区裁剪：只让时间文字段滚动，圆点与程序名不受影响
                            var clip = new Rectangle((int)Math.Floor(tailX), y,
                                (int)Math.Ceiling(viewport), (int)Math.Ceiling(_fontLine.GetHeight(g)) + 2);
                            var state = g.Save();
                            g.SetClip(clip);

                            // 往复平移：偏移夹在 [0, Travel]，两端分别是「已用」完整、与「限额」完整
                            double travel = line.Travel > 1f ? line.Travel : 1f;
                            double off = Math.Max(0.0, Math.Min(_marqueeOffset, travel));
                            float baseX = tailX - (float)off;
                            using (var b = new SolidBrush(tailColor))
                            {
                                g.DrawString(line.Tail, _fontLine, b, baseX, y, PreciseText);
                                // 两段之间的空档 = 可视区宽 - 已用宽：起点时「限额」正好在可视区右边缘外，
                                // 两端都不会露出半截文字；中段经过时按可视区裁剪
                                g.DrawString(line.TailAlt, _fontLine, b,
                                    baseX + line.TailWidth + line.SegGap, y, PreciseText);
                            }
                            g.Restore(state);
                        }
                        else
                        {
                            // 其它状态：整串（程序名： + 状态文字）作为一个整体在背景块内居中，行为与之前一致
                            string full = line.Name + Colon + line.Tail;
                            string display = line.Display ?? full;
                            // 超宽被 FitLine 截断时降级为整串绘制（极长名称场景，不拆成两段）
                            bool split = display == full;
                            string nameText = split ? line.Name + Colon : display;
                            string tailText = split ? line.Tail : "";

                            float nameW = MeasurePrecise(g, nameText, _fontLine).Width;
                            float tailW = tailText.Length > 0 ? MeasurePrecise(g, tailText, _fontLine).Width : 0f;
                            float groupW = DotSize + DotGap + nameW + tailW;
                            float groupX = (Width - groupW) / 2f;

                            Color dotColor = Color.FromArgb(textAlpha, line.Dot);
                            DrawDot(g, (int)Math.Round(groupX), dotY, dotColor, line.Filled);

                            float nameX = groupX + DotSize + DotGap;
                            using (var b = new SolidBrush(baseText))
                                g.DrawString(nameText, _fontLine, b, nameX, y, PreciseText);

                            if (tailText.Length > 0)
                            {
                                using (var b = new SolidBrush(baseText))
                                    g.DrawString(tailText, _fontLine, b, nameX + nameW, y, PreciseText);
                            }
                        }

                        y += lineH;
                    }

                    if (_lines.Count == 0)
                    {
                        using (var b = new SolidBrush(dimText))
                        {
                            var emptySize = MeasurePrecise(g, "（暂无监控）", _fontLine);
                            g.DrawString("（暂无监控）", _fontLine, b, (Width - emptySize.Width) / 2f, y, PreciseText);
                        }
                    }
                }

                ApplyLayeredBitmap(bmp);
            }

            // 每次重绘后校准滚动节拍：条件不满足（非空闲/窄条/拖动/淡显过程中）立即停表
            SyncMarqueeTimer();
        }

        /// <summary>
        /// 可拖动提示描边：1px 淡亮蓝，画在底色内侧，不覆盖文字/圆点，也不改变状态色的语义。
        /// 仅在 Ctrl+Alt 可拖动状态切换时重绘一次，不做动画，避免引入新的闪烁。
        /// </summary>
        private void DrawDragReadyBorder(Graphics g, int radius)
        {
            int maxRadius = Math.Max(0, Math.Min(Width, Height) / 2);
            radius = Math.Max(0, Math.Min(radius, maxRadius));

            int alpha = (int)Math.Round(60 + 90 * Math.Max(0.0, Math.Min(1.0, _showT)));
            using (var pen = new Pen(Color.FromArgb(alpha, 120, 190, 255), 1))
            {
                if (radius <= 0)
                {
                    g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                }
                else
                {
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        int d = radius * 2;
                        var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
                        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
                        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
                        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
                        path.CloseFigure();
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>
        /// 圆角底色填充。radius 按配置的四档值使用，并收敛到“短边一半”，
        /// 保证贴边隐藏窄条、超小窗口下也不会因为半径过大而整块被画没或变成胶囊。
        /// </summary>
        private void FillBackground(Graphics g, Color backColor, int radius)
        {
            int maxRadius = Math.Max(0, Math.Min(Width, Height) / 2);
            radius = Math.Max(0, Math.Min(radius, maxRadius));

            if (radius <= 0)
            {
                using (var bg = new SolidBrush(backColor))
                    g.FillRectangle(bg, 0, 0, Width, Height);
                return;
            }

            using (var path = new System.Drawing.Drawing2D.GraphicsPath())
            using (var bg = new SolidBrush(backColor))
            {
                int d = radius * 2;
                path.AddArc(0, 0, d, d, 180, 90);                              // 左上
                path.AddArc(Width - d, 0, d, d, 270, 90);                      // 右上
                path.AddArc(Width - d, Height - d, d, d, 0, 90);                // 右下
                path.AddArc(0, Height - d, d, d, 90, 90);                       // 左下
                path.CloseFigure();

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.FillPath(bg, path);
            }
        }

        /// <summary>
        /// 圆点绘制：按 4 倍尺寸画到 32bpp ARGB 位图（AntiAlias + HighQuality），再高质量缩放到目标尺寸，
        /// 避免小尺寸直接 FillEllipse/DrawEllipse 的像素锯齿；空心圆用“外径实心 + 内径透明”保证描边均匀。
        /// </summary>
        private void DrawDot(Graphics g, int x, int y, Color color, bool filled)
        {
            const int scale = 4;
            int big = DotSize * scale;
            using (var bmp = new Bitmap(big, big, PixelFormat.Format32bppArgb))
            using (var sg = Graphics.FromImage(bmp))
            {
                sg.Clear(Color.Transparent);
                sg.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                sg.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                sg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;

                using (var brush = new SolidBrush(color))
                    sg.FillEllipse(brush, 0, 0, big - 1, big - 1);

                if (!filled)
                {
                    // 2px 描边 → 4x 下 8px；内圈用 SourceCopy + 全透明清空，避免半透明叠色。
                    int stroke = 2 * scale;
                    sg.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.FromArgb(0, 0, 0, 0)))
                        sg.FillEllipse(clear, stroke, stroke, big - 1 - stroke * 2, big - 1 - stroke * 2);
                }

                var oldInterp = g.InterpolationMode;
                var oldPixel = g.PixelOffsetMode;
                try
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(bmp, new Rectangle(x, y, DotSize, DotSize));
                }
                finally
                {
                    g.InterpolationMode = oldInterp;
                    g.PixelOffsetMode = oldPixel;
                }
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
                if (_edgeHidden) RestoreFromEdge();
                Capture = true; // 显式捕获，拖动跨越大范围/移出窗口也不丢鼠标消息
                FinishSlideNow(); // 若正在唤出/收起滑动，先落位到目标，避免停在半路导致位置判定失真
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
                Capture = false;
                // 拖动结束后按原有“贴边吸附”阈值重新判定吸附边并持久化；关闭吸附时只记录、不移动
                ResolveAnchorFromPosition(_config.StatusOverlaySnap);
                SavePosition();
                // 松开鼠标后立即重算：热键若已松开就恢复穿透并撤掉提示描边
                SyncTransparencyState();
            }
            // 任何一次点击抬起都重新起算隐藏计时（含未拖动的点击与非左键），
            // 避免贴边唤出后连续点击把计时器停掉却没人再启动。
            if (HideFeaturesActive) StartHideTimer();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            // 滚轮同样算一次交互：结束淡显并重新起算计时
            if (_autoHidden) ExitAutoHide();
            else if (HideFeaturesActive) StartHideTimer();
        }

        /// <summary>
        /// 按当前位置判定吸附边并记录偏移。allowSnap=true（“贴边吸附”开启）时同时把窗口贴到边缘；
        /// false 时只记录吸附边与偏移，不移动窗口。阈值沿用原“贴边吸附”的 25px，不另造一套。
        /// 未命中任何一侧的轴保持自由，不会被强行吸附。
        /// </summary>
        private void ResolveAnchorFromPosition(bool allowSnap)
        {
            var wa = CurrentWorkArea();
            int newLeft = Left;
            int newTop = Top;

            int leftGap = Left - wa.Left;
            if (Math.Abs(leftGap) <= SnapThresholdPx)
            {
                _anchorX = 1;
                _anchorOffsetX = allowSnap ? 0 : Math.Max(0, leftGap);
                if (allowSnap) newLeft = wa.Left;
            }
            else
            {
                int rightGap = wa.Right - (Left + Width);
                if (Math.Abs(rightGap) <= SnapThresholdPx)
                {
                    _anchorX = 2;
                    _anchorOffsetX = allowSnap ? 0 : Math.Max(0, rightGap);
                    if (allowSnap) newLeft = wa.Right - Width;
                }
                else
                {
                    _anchorX = 0;
                    _anchorOffsetX = 0;
                }
            }

            int topGap = Top - wa.Top;
            if (Math.Abs(topGap) <= SnapThresholdPx)
            {
                _anchorY = 1;
                _anchorOffsetY = allowSnap ? 0 : Math.Max(0, topGap);
                if (allowSnap) newTop = wa.Top;
            }
            else
            {
                int bottomGap = wa.Bottom - (Top + Height);
                if (Math.Abs(bottomGap) <= SnapThresholdPx)
                {
                    _anchorY = 2;
                    _anchorOffsetY = allowSnap ? 0 : Math.Max(0, bottomGap);
                    if (allowSnap) newTop = wa.Bottom - Height;
                }
                else
                {
                    _anchorY = 0;
                    _anchorOffsetY = 0;
                }
            }

            _anchorResolved = true;

            if (newLeft != Left || newTop != Top)
            {
                Left = newLeft;
                Top = newTop;
                ClampToWorkingArea();
                ApplyBitmap();
            }
        }

        /// <summary>按吸附边 + 偏移反算窗口坐标；自由轴保持当前坐标，最后统一做工作区收敛。</summary>
        private void ApplyAnchorPosition()
        {
            var wa = CurrentWorkArea();
            int left = Left;
            int top = Top;

            if (_anchorX == 1) left = wa.Left + _anchorOffsetX;
            else if (_anchorX == 2) left = wa.Right - Width - _anchorOffsetX;

            if (_anchorY == 1) top = wa.Top + _anchorOffsetY;
            else if (_anchorY == 2) top = wa.Bottom - Height - _anchorOffsetY;

            Left = left;
            Top = top;
            ClampToWorkingArea();
        }

        /// <summary>尺寸变化后的定位：已判定的吸附轴按吸附边反算，未吸附时沿用原来的工作区收敛。</summary>
        private void ApplyPositionAfterResize()
        {
            if (!_anchorResolved) ResolveAnchorFromPosition(false);
            if (_anchorX != 0 || _anchorY != 0) ApplyAnchorPosition();
            else ClampToWorkingArea();
        }

        /// <summary>读取配置里的吸附边与偏移；四项全为 0（老配置默认值）时保持“未判定”，由当前坐标判定一次。</summary>
        private void LoadAnchorFromConfig()
        {
            int ax = _config.StatusOverlayAnchorX;
            int ay = _config.StatusOverlayAnchorY;
            if (ax < 0 || ax > 2) ax = 0;
            if (ay < 0 || ay > 2) ay = 0;

            _anchorX = ax;
            _anchorY = ay;
            _anchorOffsetX = ax == 0 ? 0 : Math.Max(0, _config.StatusOverlayOffsetX);
            _anchorOffsetY = ay == 0 ? 0 : Math.Max(0, _config.StatusOverlayOffsetY);
            _anchorResolved = ax != 0 || ay != 0;
        }

        /// <summary>把当前吸附边与偏移写进配置对象（不落盘，落盘由 SavePosition / SaveQuiet 负责）。</summary>
        private void SyncAnchorToConfig()
        {
            _config.StatusOverlayAnchorX = _anchorX;
            _config.StatusOverlayAnchorY = _anchorY;
            _config.StatusOverlayOffsetX = _anchorX == 0 ? 0 : _anchorOffsetX;
            _config.StatusOverlayOffsetY = _anchorY == 0 ? 0 : _anchorOffsetY;
        }

        /// <summary>
        /// 当前所在屏幕的工作区。贴边隐藏时窗口大部分在屏幕外，FromControl 可能误判到相邻屏，
        /// 因此隐藏期间固定使用进入隐藏时记录的工作区。
        /// </summary>
        private Rectangle CurrentWorkArea()
        {
            if (_edgeHidden && !_hideArea.IsEmpty) return _hideArea;
            if (IsHandleCreated) return Screen.FromControl(this).WorkingArea;
            return Screen.FromPoint(new Point(Left, Top)).WorkingArea;
        }

        private void ClampToVirtualScreen()
        {
            var vScreen = SystemInformation.VirtualScreen;
            if (Left < vScreen.Left) Left = vScreen.Left;
            if (Top < vScreen.Top) Top = vScreen.Top;
            if (Left + Width > vScreen.Right) Left = vScreen.Right - Width;
            if (Top + Height > vScreen.Bottom) Top = vScreen.Bottom - Height;
        }

        /// <summary>把整块悬浮窗夹进所在屏幕的工作区（内容变宽/变高后立即完全可见）。</summary>
        private void ClampToWorkingArea()
        {
            var wa = CurrentWorkArea();
            if (Width >= wa.Width) Left = wa.Left;
            else if (Left + Width > wa.Right) Left = wa.Right - Width;
            if (Left < wa.Left) Left = wa.Left;

            if (Height >= wa.Height) Top = wa.Top;
            else if (Top + Height > wa.Bottom) Top = wa.Bottom - Height;
            if (Top < wa.Top) Top = wa.Top;
        }

        /// <summary>
        /// 贴边隐藏状态下的尺寸变化：吸附轴按吸附边反算隐藏前坐标（宽度变化后仍贴原来那条边，
        /// 窄条不会跑出屏幕），自由轴保持隐藏前坐标；再做工作区收敛，最后按原贴边方向重算窄条位置。
        /// </summary>
        private void ReapplyEdgeStrip()
        {
            var wa = CurrentWorkArea();

            if (_anchorX == 1) _restoreLeft = wa.Left + _anchorOffsetX;
            else if (_anchorX == 2) _restoreLeft = wa.Right - Width - _anchorOffsetX;

            if (_anchorY == 1) _restoreTop = wa.Top + _anchorOffsetY;
            else if (_anchorY == 2) _restoreTop = wa.Bottom - Height - _anchorOffsetY;

            if (_restoreLeft + Width > wa.Right) _restoreLeft = wa.Right - Width;
            if (_restoreLeft < wa.Left) _restoreLeft = wa.Left;
            if (_restoreTop + Height > wa.Bottom) _restoreTop = wa.Bottom - Height;
            if (_restoreTop < wa.Top) _restoreTop = wa.Top;

            _posAnimating = false;
            var pos = EdgeStripPosition(wa, _edgeSide, _restoreLeft, _restoreTop);
            Left = pos.X;
            Top = pos.Y;
            ApplyBitmap();
        }

        private Point EdgeStripPosition(Rectangle wa, int side, int restoreLeft, int restoreTop)
        {
            int left = restoreLeft;
            int top = restoreTop;
            if (side == 0) left = wa.Left - Width + EdgeVisiblePx;
            else if (side == 1) left = wa.Right - EdgeVisiblePx;
            else if (side == 2) top = wa.Top - Height + EdgeVisiblePx;
            else if (side == 3) top = wa.Bottom - EdgeVisiblePx;
            return new Point(left, top);
        }

        private void SavePosition()
        {
            // 贴边隐藏时窗口被移出屏幕，真实位置要取隐藏前的坐标，避免把窄条坐标写进配置。
            int left = _edgeHidden ? _restoreLeft : Left;
            int top = _edgeHidden ? _restoreTop : Top;
            int offsetX = _anchorX == 0 ? 0 : _anchorOffsetX;
            int offsetY = _anchorY == 0 ? 0 : _anchorOffsetY;

            if (_config.StatusOverlayX == left && _config.StatusOverlayY == top
                && _config.StatusOverlayAnchorX == _anchorX && _config.StatusOverlayAnchorY == _anchorY
                && _config.StatusOverlayOffsetX == offsetX && _config.StatusOverlayOffsetY == offsetY)
                return;

            _config.StatusOverlayX = left;
            _config.StatusOverlayY = top;
            SyncAnchorToConfig();
            _config.SaveQuiet();
        }

        /// <summary>
        /// 隐藏计时器到期：淡显与贴边按各自秒数串联（先淡显、到贴边秒数再收窄条）。
        /// 贴边可用且淡显秒数 &gt;= 贴边秒数时不做淡显，直接按贴边时间收窄条。
        /// </summary>
        private void AutoHideNow()
        {
            _autoHideTimer.Stop();
            if (_dragging) return;
            if (Bounds.Contains(Cursor.Position))
            {
                // 鼠标在窗口内：按规则不收起，但必须续排。
                // 若这里直接 return，而 OnMouseLeave 又因窗口位置变化（如贴边滑动中）丢失，
                // 计时器就永久停摆，之后再移开鼠标也不会淡显/贴边。
                _hideTimerStartedAt = DateTime.Now;
                _autoHideTimer.Interval = 300;
                _autoHideTimer.Start();
                return;
            }

            double elapsed = Math.Max(0, (DateTime.Now - _hideTimerStartedAt).TotalSeconds);
            // 淡显与贴边的先后关系由 PlanHide 统一决定，这里只负责执行到期的动作
            HidePlan plan = PlanHide(elapsed);
            if (plan.FadeDue) EnterAutoHide();
            if (plan.EdgeDue && TryEdgeHide()) return; // 已收成窄条，本轮不再续排

            if (HideFeaturesActive) ArmHideTimer(elapsed);
        }

        /// <summary>
        /// 贴边判定：窗口某条边与所在屏幕工作区边缘的距离 ≤ EdgeThresholdPx 即视为贴边；
        /// 命中最近的一条边后把窗口移出屏幕，只保留 EdgeVisiblePx 的窄条。
        /// 返回 false 表示当前不贴边（不做任何移动）。
        /// </summary>
        private bool TryEdgeHide()
        {
            var wa = CurrentWorkArea();

            int distLeft = Math.Abs(Left - wa.Left);
            int distRight = Math.Abs(wa.Right - (Left + Width));
            int distTop = Math.Abs(Top - wa.Top);
            int distBottom = Math.Abs(wa.Bottom - (Top + Height));

            int best = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));
            if (best > EdgeThresholdPx) return false;

            _restoreLeft = Left;
            _restoreTop = Top;
            // 固定隐藏期间的工作区，避免窄条（大部分在屏幕外）被 FromControl 判到相邻屏
            _hideArea = wa;

            // 贴边隐藏的这条边同时记为吸附边（偏移为贴边时的间隙），
            // 这样内容变宽/变窄后窄条仍按这条边反算，不会跑到屏幕外
            if (best == distLeft) { _edgeSide = 0; _anchorX = 1; _anchorOffsetX = Math.Max(0, Left - wa.Left); }
            else if (best == distRight) { _edgeSide = 1; _anchorX = 2; _anchorOffsetX = Math.Max(0, wa.Right - (Left + Width)); }
            else if (best == distTop) { _edgeSide = 2; _anchorY = 1; _anchorOffsetY = Math.Max(0, Top - wa.Top); }
            else { _edgeSide = 3; _anchorY = 2; _anchorOffsetY = Math.Max(0, wa.Bottom - (Top + Height)); }
            _anchorResolved = true;

            _edgeHidden = true;
            var target = EdgeStripPosition(wa, _edgeSide, _restoreLeft, _restoreTop);
            StartSlideTo(target.X, target.Y); // 逐帧滑出（ease-out），不阻塞 UI
            return true;
        }

        /// <summary>从贴边隐藏恢复：反向滑回隐藏前坐标、回到完全不透明，并重新开始隐藏计时。</summary>
        private void RestoreFromEdge()
        {
            if (!_edgeHidden) return;
            _edgeHidden = false;
            _edgeSide = -1;
            StartSlideTo(_restoreLeft, _restoreTop);
            if (_autoHidden) ExitAutoHide();
            else StartHideTimer();
        }

        /// <summary>
        /// 贴边隐藏时的唤出热区：只保留窄条本身（贴边露出的那几像素），隐藏前的整块区域不再算热区。
        /// 位置取「最终窄条位置」而不是动画中的 Bounds——滑动过程中热区固定在贴边处，
        /// 不会因为窗口正在滑过原位置而把鼠标误判成落在热区里，避免反复触发/抖动。
        /// </summary>
        private Rectangle EdgeStripBounds()
        {
            var wa = _hideArea.IsEmpty ? CurrentWorkArea() : _hideArea;
            var pos = EdgeStripPosition(wa, _edgeSide, _restoreLeft, _restoreTop);
            var strip = new Rectangle(pos.X, pos.Y, Width, Height);
            // 窄条大半在屏幕外，只把落在工作区内的那一截算作可命中的热区
            var visible = Rectangle.Intersect(strip, wa);
            return visible.IsEmpty ? strip : visible;
        }

        private void EnterAutoHide()
        {
            _autoHideTimer.Stop();
            if (_dragging) return;
            if (Bounds.Contains(Cursor.Position)) return;
            _autoHidden = true;
            _fadeAnimating = true; // 从当前不透明度逐帧渐变到淡显值
            _animTimer.Start();
            ApplyBitmap();
        }

        private void ExitAutoHide()
        {
            _autoHidden = false;
            _fadeAnimating = true; // 反向淡入，从当前值继续，不跳变
            _animTimer.Start();
            ApplyBitmap();
            StartHideTimer();
        }

        private void CheckCursorForAutoHide()
        {
            if (_dragging) return;

            // 贴边隐藏：只有鼠标指到贴边窄条（那几像素）才滑出，隐藏前的整块区域不再算热区；
            // 鼠标穿透开启时收不到 enter，只能靠这里轮询兜底。
            if (_edgeHidden)
            {
                if (EdgeStripBounds().Contains(Cursor.Position)) RestoreFromEdge();
                return;
            }

            if (!_autoHidden) return;
            if (Bounds.Contains(Cursor.Position)) ExitAutoHide();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _autoHideTimer.Stop();
            // 与轮询共用同一条热区规则：只有光标落在窄条上才唤出。
            // 收起动画中窗口会从原位置滑过，此时鼠标可能被“扫到”而收到 enter，
            // 但光标并不在窄条上，按规则不唤出，避免滑出即收回的抖动。
            if (_edgeHidden)
            {
                if (EdgeStripBounds().Contains(Cursor.Position)) RestoreFromEdge();
                return;
            }
            if (_autoHidden) ExitAutoHide();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!_dragging) StartHideTimer();
        }

        public void ApplyConfigChanges()
        {
            TopMost = _config.StatusOverlayTopMost;

            // 关闭“贴边隐藏”后如果当前正处于窄条状态，先恢复成完整窗口，避免留着一条无法操作的窄边。
            if (_edgeHidden && !_config.StatusOverlayEdgeHide)
            {
                _posAnimating = false;
                _edgeHidden = false;
                _edgeSide = -1;
                Left = _restoreLeft;
                Top = _restoreTop;
            }

            // 重置位置：设置页把 X/Y 置为 -9999，应用/点“重置位置”时在此真正复位并立即持久化，
            // 避免仅改配置、悬浮窗不动，以及退出时 SavePosition 又把旧坐标写回。
            if (_config.StatusOverlayX == -9999 || _config.StatusOverlayY == -9999)
            {
                var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                _anchorX = 2;
                _anchorOffsetX = 30;
                _anchorY = 1;
                _anchorOffsetY = 30;
                _anchorResolved = true;
                Left = wa.Right - Width - _anchorOffsetX;
                Top = wa.Top + _anchorOffsetY;
                _config.StatusOverlayX = Left;
                _config.StatusOverlayY = Top;
                SyncAnchorToConfig();
                _config.SaveQuiet();
            }
            else
            {
                // 应用配置时按记录的吸附边 + 偏移还原；老配置没有吸附边字段则按绝对坐标判定一次
                LoadAnchorFromConfig();
                if (_anchorResolved) ApplyAnchorPosition();
                else
                {
                    Left = _config.StatusOverlayX;
                    Top = _config.StatusOverlayY;
                    ResolveAnchorFromPosition(false);
                }
            }

            // 贴边隐藏态下保存：上面的定位把窗口按配置坐标摆回了屏幕内，但 _edgeHidden 仍为 true，
            // 会造成“标记已隐藏、窗口却在屏幕内”的错位——窄条热区留在屏幕边缘，鼠标移到窗口上唤不出，
            // 跑马灯也因 _edgeHidden 一直不跑，淡显后无法恢复（只有运行被监控程序或拖动才被唤醒）。
            // 这里以刚应用的配置坐标为隐藏前位置，重新按贴边方向收成窄条，保证隐藏态自洽。
            if (_edgeHidden)
            {
                _restoreLeft = Left;
                _restoreTop = Top;
                ReapplyEdgeStrip();
            }

            RebuildFonts();
            _autoHidden = false;
            _fadeAnimating = false;
            _posAnimating = false;
            _showT = 1.0;
            // 必须放在 _autoHidden 复位之后：PlanHide 用 _autoHidden 判断“淡显节点是否已完成”，
            // 若在复位前起表，淡显态下保存会因为“淡显已完成”而算出没有下一个节点，直接把计时器停掉。
            if (HideFeaturesActive) StartHideTimer();
            else _autoHideTimer.Stop();
            RefreshContent();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SavePosition(); // 退出时强制做最后一次确认保存
                _refreshTimer?.Stop(); _refreshTimer?.Dispose();
                _autoHideTimer?.Stop(); _autoHideTimer?.Dispose();
                _animTimer?.Stop(); _animTimer?.Dispose();
                _transparencyTimer?.Stop(); _transparencyTimer?.Dispose();
                _marqueeTimer?.Stop(); _marqueeTimer?.Dispose();
                _fontTitle?.Dispose();
                _fontLine?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
