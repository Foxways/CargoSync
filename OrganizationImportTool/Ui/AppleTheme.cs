using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// Dark "glass" design system: deep charcoal canvas, translucent elevated cards, vivid
    /// accent, SF Pro / Segoe typography, pill buttons, and dark-styled grids/inputs. Every
    /// screen calls these helpers so the app stays visually consistent and modern.
    /// </summary>
    public static class AppleTheme
    {
        // ---- Dark palette ----
        public static readonly Color Canvas        = Color.FromArgb(18, 18, 22);    // #121216 window bg
        public static readonly Color Surface       = Color.FromArgb(30, 30, 36);    // #1E1E24 card / grid
        public static readonly Color SurfaceAlt     = Color.FromArgb(38, 38, 45);    // inputs / alt rows
        public static readonly Color CardFill       = Color.FromArgb(30, 30, 36);
        public static readonly Color CardFillGlass  = Color.FromArgb(210, 38, 38, 46); // translucent glass
        public static readonly Color Hairline       = Color.FromArgb(56, 56, 64);    // borders
        public static readonly Color TextPrimary    = Color.FromArgb(245, 245, 247); // near-white
        public static readonly Color TextSecondary  = Color.FromArgb(152, 152, 162);
        public static readonly Color Accent         = Color.FromArgb(0, 199, 190);   // #00C7BE Teal
        public static readonly Color AccentHover    = Color.FromArgb(40, 220, 212);  // #28DCD4 Teal hover
        public static readonly Color Success        = Color.FromArgb(48, 209, 88);   // #30D158
        public static readonly Color Danger         = Color.FromArgb(255, 69, 58);   // #FF453A
        public static readonly Color Warning        = Color.FromArgb(255, 159, 10);  // #FF9F0A
        public static readonly Color ControlFill     = Color.FromArgb(40, 40, 47);
        public static readonly Color SecondaryFill   = Color.FromArgb(48, 48, 56);
        public static readonly Color SecondaryHover  = Color.FromArgb(62, 62, 72);

        private static readonly string FontFamilyName = ResolveFontFamily();

        private static string ResolveFontFamily()
        {
            string[] preferred = { "SF Pro Display", "SF Pro Text", "SF Pro", "Segoe UI Variable Display", "Segoe UI" };
            try
            {
                using var installed = new InstalledFontCollection();
                var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var f in preferred) if (names.Contains(f)) return f;
            }
            catch { }
            return "Segoe UI";
        }

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
            => new Font(FontFamilyName, size, style, GraphicsUnit.Point);

        // Windows' built-in monochrome icon fonts (crisp single-colour glyphs, no colour-emoji
        // fallback). Segoe Fluent Icons ships on Win11, Segoe MDL2 Assets on Win10 — they share the
        // same glyph codepoints (e.g.  = Delete,  = RepeatAll).
        private static readonly string IconFamilyName = ResolveIconFamily();

        private static string ResolveIconFamily()
        {
            string[] preferred = { "Segoe Fluent Icons", "Segoe MDL2 Assets", "Segoe UI Symbol" };
            try
            {
                using var installed = new InstalledFontCollection();
                var names = installed.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var f in preferred) if (names.Contains(f)) return f;
            }
            catch { }
            return "Segoe UI Symbol";
        }

        /// <summary>A monochrome icon-font (Segoe Fluent Icons / MDL2) Font for glyph buttons.</summary>
        public static Font IconFont(float size, FontStyle style = FontStyle.Regular)
            => new Font(IconFamilyName, size, style, GraphicsUnit.Point);

        public static Font Title    => Font(18f, FontStyle.Bold);
        public static Font Headline => Font(12.5f, FontStyle.Bold);
        public static Font Body     => Font(10f, FontStyle.Regular);
        public static Font Caption  => Font(8.5f, FontStyle.Regular);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        // Dark title bar (immersive dark mode). 20 on Win10 2004+/Win11; 19 on earlier Win10 (1809–1903).
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_BORDER_COLOR  = 34;   // Win11 22000+ — thin accent border around the window
        private const int DWMWA_CAPTION_COLOR = 35;   // Win11 22000+ — caption bar background color
        private const int DWMWA_COLOR_NONE    = -2;   // 0xFFFFFFFE — removes the OS accent-colour border

        // Convert a System.Drawing.Color to Win32 COLORREF (0x00BBGGRR)
        private static int ToColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        /// <summary>Paint a form's title bar (the non-client "top") dark so it matches the dark UI
        /// instead of the default white Windows caption. Applied to every form via ApplyWindow.</summary>
        public static void DarkTitleBar(Form form)
        {
            void Apply()
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
                        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
                    // Remove the OS blue accent border ring.
                    int none = DWMWA_COLOR_NONE;
                    DwmSetWindowAttribute(form.Handle, DWMWA_BORDER_COLOR, ref none, sizeof(int));
                    // Lock the caption to Canvas (pure near-black) so Windows' system accent colour
                    // (which can be blue) doesn't bleed into the title bar on focused windows.
                    int captionColor = ToColorRef(Color.FromArgb(18, 18, 18));
                    DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
                }
                catch { }
            }
            if (form.IsHandleCreated) Apply(); else form.HandleCreated += (s, e) => Apply();
        }

        // Undocumented but stable Win10 1809+/Win11 uxtheme exports for dark mode.
        // #135 SetPreferredAppMode: tells Windows the app prefers dark mode (affects menus, common dialogs).
        // #133 AllowDarkModeForWindow: activates dark scrollbar track/thumb rendering for a specific HWND.
        // #104 RefreshImmersiveColorPolicyState: applies the preferred-app-mode change immediately.
        // #136 FlushMenuThemes: flushes cached menu theme so dark menus repaint correctly.
        [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int mode);   // 0=Default 1=AllowDark 2=ForceDark 3=ForceLight
        [DllImport("uxtheme.dll", EntryPoint = "#133")]
        private static extern bool AllowDarkModeForWindow(IntPtr hWnd, bool allow);
        [DllImport("uxtheme.dll", EntryPoint = "#104")]
        private static extern void RefreshImmersiveColorPolicyState();
        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        /// <summary>Force the whole application into dark mode (call once at startup, before any form).</summary>
        public static void EnableDarkMode()
        {
            try { SetPreferredAppMode(2); RefreshImmersiveColorPolicyState(); FlushMenuThemes(); }
            catch { /* older Windows - ignore */ }
        }

        private const int WM_THEMECHANGED = 0x031A;

        // ── Custom dark scrollbar painter ──────────────────────────────────────────────────────
        // Win32 dark-mode theme APIs do NOT reach WinForms scrollbars: their window class is
        // "WindowsForms10.ScrollBar.app.0.xxx" (a custom class, not native "SCROLLBAR"), so
        // SetWindowTheme/AllowDarkModeForWindow have no effect and the track stays white.
        // Solution: subclass each managed VScrollBar/HScrollBar with a NativeWindow that
        // intercepts WM_PAINT and fully custom-draws a dark track, rounded thumb and arrows.
        // Thumb metrics come from the managed ScrollBar (Value/Maximum/LargeChange) because
        // GetScrollInfo(SB_CTL) is unreliable for WinForms' internally-managed scrollbars.
        private sealed class DarkScrollPainter : NativeWindow
        {
            private static readonly Color _track = Color.FromArgb(20, 20, 24);   // near-black track
            private static readonly Color _thumb = Color.FromArgb(78, 78, 90);   // grey thumb
            private static readonly Color _arrow = Color.FromArgb(150, 150, 162);

            private const int WM_PAINT        = 0x000F;
            private const int WM_ERASEBKGND   = 0x0014;
            private const int WM_NCPAINT      = 0x0085;
            private const int WM_NCDESTROY    = 0x0082;
            private const int WM_MOUSEMOVE    = 0x0200;
            private const int WM_LBUTTONDOWN  = 0x0201;
            private const int WM_LBUTTONUP    = 0x0202;
            private const int WM_LBUTTONDBLCLK= 0x0203;
            private const int WM_MOUSEWHEEL   = 0x020A;
            private const int WM_TIMER        = 0x0113;
            private const int WM_MOUSELEAVE   = 0x02A3;
            private const int WM_KEYDOWN      = 0x0100;
            private const int WM_KEYUP        = 0x0101;
            private const int WM_SETFOCUS     = 0x0007;
            private const int WM_KILLFOCUS    = 0x0008;
            private const int WM_ENABLE       = 0x000A;
            // Scrollbar-specific position/range messages — sent when the host (e.g. DataGridView)
            // scrolls via wheel/keyboard and pushes a new position into the bar. The native control
            // repaints the thumb white SYNCHRONOUSLY in response, so we must overpaint right after.
            private const int SBM_SETPOS         = 0x00E0;
            private const int SBM_SETRANGE       = 0x00E2;
            private const int SBM_SETRANGEREDRAW = 0x00E6;
            private const int SBM_SETSCROLLINFO  = 0x00E9;

            [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out RC r);
            [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
            [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr hdc);
            [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h, IntPtr r);
            [DllImport("user32.dll")] static extern IntPtr SetCapture(IntPtr h);
            [DllImport("user32.dll")] static extern bool ReleaseCapture();

            [StructLayout(LayoutKind.Sequential)]
            struct RC { public int L, T, Rt, B; }

            // ScrollBar.OnScroll is the only way to make the host (DataGridView) actually scroll:
            // setting Value alone does NOT — the host listens to the Scroll event, raised here via reflection.
            private static readonly System.Reflection.MethodInfo _onScroll =
                typeof(ScrollBar).GetMethod("OnScroll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            private enum Drag { None, Thumb, LineUp, LineDown, PageUp, PageDown }

            private readonly ScrollBar _sb;
            private readonly bool _vert;
            private Drag _mode = Drag.None;
            private int _grab;                         // offset of mouse within thumb when drag began
            private System.Windows.Forms.Timer _repeat; // auto-repeat while an arrow/track is held
            private DarkScrollPainter(ScrollBar sb) { _sb = sb; _vert = sb is VScrollBar; }

            // Static collections keep painters alive (prevent GC) and dedupe by HWND.
            private static readonly List<DarkScrollPainter> _live = new();
            private static readonly HashSet<IntPtr>         _seen = new();

            /// <summary>Attach the dark painter to a managed WinForms scrollbar.</summary>
            public static void Attach(ScrollBar sb)
            {
                if (sb == null) return;
                void Hook()
                {
                    if (!sb.IsHandleCreated || !_seen.Add(sb.Handle)) return;
                    var p = new DarkScrollPainter(sb);
                    try
                    {
                        p.AssignHandle(sb.Handle);
                        _live.Add(p);
                        // Repaint SYNCHRONOUSLY on value change. WinForms' ScrollBar.Value setter calls
                        // the Win32 SetScrollInfo(redraw:true) API directly (not a window message), which
                        // repaints the thumb white in-place. A synchronous Paint here overwrites that white
                        // immediately in the same tick — an async Invalidate would leave a visible flash.
                        sb.ValueChanged += (_, _) => { try { if (sb.IsHandleCreated) p.Paint(); } catch { } };
                        sb.Invalidate();
                    }
                    catch { _seen.Remove(sb.Handle); }
                }
                if (sb.IsHandleCreated) Hook(); else sb.HandleCreated += (_, _) => Hook();
                // Re-hook if the HWND is (re)created when first shown.
                sb.VisibleChanged += (_, _) => { if (sb.Visible) Hook(); };
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_ERASEBKGND:
                        m.Result = (IntPtr)1;
                        return;
                    case WM_NCPAINT:
                        m.Result = IntPtr.Zero;
                        return;
                    case WM_PAINT:
                        Paint();
                        ValidateRect(Handle, IntPtr.Zero);
                        return;

                    // ── Fully managed mouse handling ── the native control NEVER processes these,
                    // so it never paints its own (white) thumb. We hit-test, drag, page and step
                    // ourselves, drive the host via OnScroll, and paint dark. Zero native paint = zero flicker.
                    case WM_LBUTTONDOWN:
                    case WM_LBUTTONDBLCLK:
                        OnDown(MouseAxis(m));
                        return;
                    case WM_MOUSEMOVE:
                        if (_mode == Drag.Thumb) OnDrag(MouseAxis(m));
                        return;
                    case WM_LBUTTONUP:
                        OnUp();
                        return;
                    case WM_MOUSEWHEEL:
                        {
                            int delta = (short)((long)m.WParam >> 16);
                            int lines = delta / 120;
                            ScrollTo(_sb.Value - lines * Math.Max(1, _sb.SmallChange) * 3, ScrollEventType.ThumbPosition);
                        }
                        return;

                    case WM_NCDESTROY:
                        StopRepeat();
                        _seen.Remove(Handle); _live.Remove(this);
                        base.WndProc(ref m);
                        return;
                }

                base.WndProc(ref m);

                // For scrolls the host triggers (wheel/keyboard while the mouse is over the grid BODY),
                // WinForms pushes a new position via SetScrollInfo, which repaints the thumb white.
                // Overpaint dark right after.
                switch (m.Msg)
                {
                    case WM_KEYDOWN:
                    case WM_KEYUP:
                    case WM_SETFOCUS:
                    case WM_KILLFOCUS:
                    case WM_ENABLE:
                    case SBM_SETPOS:
                    case SBM_SETRANGE:
                    case SBM_SETRANGEREDRAW:
                    case SBM_SETSCROLLINFO:
                        Paint();
                        break;
                }
            }

            // Extract the position along the scroll axis from a mouse message's LPARAM (client coords).
            private int MouseAxis(Message m)
            {
                int lp = (int)(long)m.LParam;
                short x = (short)(lp & 0xFFFF), y = (short)((lp >> 16) & 0xFFFF);
                return _vert ? y : x;
            }

            // Geometry of the bar in pixels for the current size + scroll state.
            private (int len, int btn, int trackPx, int thumbPx, int thumbStart, int scrollRange, int min) Geo()
            {
                GetClientRect(Handle, out RC rc);
                int w = rc.Rt - rc.L, h = rc.B - rc.T;
                int len = _vert ? h : w, thick = _vert ? w : h, btn = thick;
                int min = _sb.Minimum, large = Math.Max(1, _sb.LargeChange);
                int range = _sb.Maximum - min + 1;
                int trackPx = Math.Max(0, len - 2 * btn);
                int scrollRange = Math.Max(0, range - large);
                int thumbPx = 0, thumbStart = btn;
                if (range > large && trackPx > 4)
                {
                    thumbPx = Math.Max(24, (int)((double)large / range * trackPx));
                    thumbPx = Math.Min(thumbPx, trackPx);
                    int valueOff = Math.Max(0, Math.Min(_sb.Value - min, scrollRange));
                    thumbStart = btn + (scrollRange == 0 ? 0 : (int)((double)valueOff / scrollRange * (trackPx - thumbPx)));
                }
                return (len, btn, trackPx, thumbPx, thumbStart, scrollRange, min);
            }

            private void OnDown(int pos)
            {
                var g = Geo();
                SetCapture(Handle);
                if (pos < g.btn)                              { _mode = Drag.LineUp;   StepLine(-1); StartRepeat(); }
                else if (pos >= g.len - g.btn)                { _mode = Drag.LineDown; StepLine(+1); StartRepeat(); }
                else if (g.thumbPx > 0 && pos >= g.thumbStart && pos < g.thumbStart + g.thumbPx)
                                                              { _mode = Drag.Thumb; _grab = pos - g.thumbStart; }
                else if (g.thumbPx > 0 && pos < g.thumbStart) { _mode = Drag.PageUp;   StepPage(-1); StartRepeat(); }
                else                                          { _mode = Drag.PageDown; StepPage(+1); StartRepeat(); }
                Paint();
            }

            private void OnDrag(int pos)
            {
                var g = Geo();
                int travel = g.trackPx - g.thumbPx;
                if (travel <= 0 || g.scrollRange <= 0) return;
                int newOff = Math.Max(0, Math.Min(pos - g.btn - _grab, travel));
                int val = g.min + (int)Math.Round((double)newOff / travel * g.scrollRange);
                ScrollTo(val, ScrollEventType.ThumbTrack);
            }

            private void OnUp()
            {
                StopRepeat();
                if (_mode == Drag.None) return;
                _mode = Drag.None;
                ReleaseCapture();
                Raise(ScrollEventType.EndScroll, _sb.Value, _sb.Value);
                Paint();
            }

            private void StepLine(int dir) =>
                ScrollTo(_sb.Value + dir * Math.Max(1, _sb.SmallChange),
                         dir < 0 ? ScrollEventType.SmallDecrement : ScrollEventType.SmallIncrement);

            private void StepPage(int dir) =>
                ScrollTo(_sb.Value + dir * Math.Max(1, _sb.LargeChange),
                         dir < 0 ? ScrollEventType.LargeDecrement : ScrollEventType.LargeIncrement);

            private void ScrollTo(int value, ScrollEventType type)
            {
                int min = _sb.Minimum, maxVal = Math.Max(min, _sb.Maximum - Math.Max(1, _sb.LargeChange) + 1);
                value = Math.Max(min, Math.Min(value, maxVal));
                if (value == _sb.Value) { Paint(); return; }
                int old = _sb.Value;
                _sb.Value = value;          // raises ValueChanged → synchronous Paint
                Raise(type, old, value);    // raises Scroll → host (DataGridView) actually scrolls
                Paint();
            }

            private void Raise(ScrollEventType type, int oldV, int newV)
            {
                try
                {
                    _onScroll?.Invoke(_sb, new object[] { new ScrollEventArgs(type, oldV, newV,
                        _vert ? ScrollOrientation.VerticalScroll : ScrollOrientation.HorizontalScroll) });
                }
                catch { }
            }

            private void StartRepeat()
            {
                _repeat ??= new System.Windows.Forms.Timer();
                _repeat.Interval = 300;     // initial delay before auto-repeat kicks in
                _repeat.Tick -= RepeatTick;
                _repeat.Tick += RepeatTick;
                _repeat.Start();
            }

            private void RepeatTick(object sender, EventArgs e)
            {
                _repeat.Interval = 45;      // fast repeat once started
                switch (_mode)
                {
                    case Drag.LineUp:   StepLine(-1); break;
                    case Drag.LineDown: StepLine(+1); break;
                    case Drag.PageUp:   StepPage(-1); break;
                    case Drag.PageDown: StepPage(+1); break;
                    default: StopRepeat(); break;
                }
            }

            private void StopRepeat() => _repeat?.Stop();

            private void Paint()
            {
                IntPtr hdc = GetDC(Handle);
                if (hdc == IntPtr.Zero) return;
                try
                {
                    GetClientRect(Handle, out RC rc);
                    int w = rc.Rt - rc.L, h = rc.B - rc.T;
                    if (w <= 0 || h <= 0) return;

                    int btn = _vert ? w : h;   // arrow buttons are square (= bar thickness)

                    using var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.Clear(_track);

                        // ── Thumb (metrics from the managed scrollbar) ──
                        int min = _sb.Minimum, max = _sb.Maximum, large = Math.Max(1, _sb.LargeChange);
                        int range = max - min + 1;
                        int trackPx = (_vert ? h : w) - 2 * btn;
                        if (range > large && trackPx > 4)
                        {
                            int thumbPx = Math.Max(24, (int)((double)large / range * trackPx));
                            thumbPx = Math.Min(thumbPx, trackPx);
                            int scrollRange = range - large;                       // value span the thumb travels
                            int valueOff = Math.Max(0, Math.Min(_sb.Value - min, scrollRange));
                            int thumbOff = scrollRange == 0 ? 0
                                : (int)((double)valueOff / scrollRange * (trackPx - thumbPx));
                            var tr = _vert
                                ? new Rectangle(3, btn + thumbOff, Math.Max(1, w - 6), thumbPx)
                                : new Rectangle(btn + thumbOff, 3, thumbPx, Math.Max(1, h - 6));
                            using var br = new SolidBrush(_thumb);
                            using var gp = RoundPath(tr, 3);
                            g.FillPath(br, gp);
                        }

                        // ── Arrow glyphs ──
                        using var ab = new SolidBrush(_arrow);
                        if (_vert)
                        {
                            Arrow(g, ab, new Rectangle(0, 0, w, btn), true, true);
                            Arrow(g, ab, new Rectangle(0, h - btn, w, btn), true, false);
                        }
                        else
                        {
                            Arrow(g, ab, new Rectangle(0, 0, btn, h), false, true);
                            Arrow(g, ab, new Rectangle(w - btn, 0, btn, h), false, false);
                        }
                    }

                    using var sg = Graphics.FromHdc(hdc);
                    sg.DrawImage(bmp, 0, 0);
                }
                catch { }
                finally { ReleaseDC(Handle, hdc); }
            }

            internal static void Arrow(Graphics g, Brush b, Rectangle r, bool vert, bool back)
            {
                float cx = r.X + r.Width * .5f, cy = r.Y + r.Height * .5f;
                float s = Math.Min(r.Width, r.Height) * .22f;
                PointF[] pts = vert
                    ? (back ? new[] { new PointF(cx, cy - s), new PointF(cx - s, cy + s), new PointF(cx + s, cy + s) }
                            : new[] { new PointF(cx, cy + s), new PointF(cx - s, cy - s), new PointF(cx + s, cy - s) })
                    : (back ? new[] { new PointF(cx - s, cy), new PointF(cx + s, cy - s), new PointF(cx + s, cy + s) }
                            : new[] { new PointF(cx + s, cy), new PointF(cx - s, cy - s), new PointF(cx - s, cy + s) });
                g.FillPolygon(b, pts);
            }

            internal static System.Drawing.Drawing2D.GraphicsPath RoundPath(Rectangle r, int rad)
            {
                int d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
                if (d < 2) { var p0 = new System.Drawing.Drawing2D.GraphicsPath(); p0.AddRectangle(r); return p0; }
                var gp = new System.Drawing.Drawing2D.GraphicsPath();
                gp.AddArc(r.X, r.Y, d, d, 180, 90);
                gp.AddArc(r.Right - d, r.Y, d, d, 270, 90);
                gp.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
                gp.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
                gp.CloseFigure();
                return gp;
            }
        }
        // ──────────────────────────────────────────────────────────────────────────────────────

        // ── Dark painter for NATIVE non-client scrollbars ───────────────────────────────────────
        // RichTextBox/TextBox (RichEdit & Edit), ListBox, ListView, TreeView and AutoScroll panels
        // do NOT host managed child ScrollBar controls — their scrollbars live in the window's
        // non-client (NC) area and are drawn by the OS/control. SetWindowTheme("DarkMode_Explorer")
        // only tints those dark-GREY (and RichEdit ignores it entirely, staying WHITE). To get the
        // SAME near-black bar as the grids, we subclass the control and overpaint the NC scrollbar
        // region on WM_NCPAINT and after every scroll/resize. We deliberately do NOT intercept mouse
        // input, so native click/drag/wheel scrolling is preserved unchanged for every control type.
        private sealed class DarkNcScrollPainter : NativeWindow
        {
            private static readonly Color _track = Color.FromArgb(20, 20, 24);   // near-black track
            private static readonly Color _thumb = Color.FromArgb(78, 78, 90);   // grey thumb
            private static readonly Color _arrow = Color.FromArgb(150, 150, 162);

            private const int WM_NCPAINT      = 0x0085;
            private const int WM_NCDESTROY    = 0x0082;
            private const int WM_SIZE         = 0x0005;
            private const int WM_VSCROLL      = 0x0115;
            private const int WM_HSCROLL      = 0x0114;
            private const int WM_MOUSEWHEEL   = 0x020A;
            private const int WM_NCMOUSEMOVE  = 0x00A0;
            private const int WM_NCLBUTTONUP  = 0x00A2;
            private const int WM_STYLECHANGED = 0x007D;
            private const int WM_KEYUP        = 0x0101;

            private const int  SB_HORZ   = 0, SB_VERT = 1;
            private const uint SIF_ALL   = 0x17;
            private const int  GWL_STYLE = -16;
            private const int  WS_VSCROLL = 0x00200000, WS_HSCROLL = 0x00100000;
            // System metric indices: vbar width, hbar height, vbar arrow height, hbar arrow width.
            private const int SM_CXVSCROLL = 2, SM_CYHSCROLL = 3, SM_CYVSCROLL = 20, SM_CXHSCROLL = 21;

            [StructLayout(LayoutKind.Sequential)]
            private struct SCROLLINFO { public uint cbSize; public uint fMask; public int nMin, nMax, nPage, nPos, nTrackPos; }
            [StructLayout(LayoutKind.Sequential)] private struct RC { public int L, T, R, B; }
            [StructLayout(LayoutKind.Sequential)] private struct PT { public int X, Y; }

            [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr h, out RC r);
            [DllImport("user32.dll")] static extern bool   GetClientRect(IntPtr h, out RC r);
            [DllImport("user32.dll")] static extern bool   ClientToScreen(IntPtr h, ref PT p);
            [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
            [DllImport("user32.dll")] static extern int    ReleaseDC(IntPtr h, IntPtr hdc);
            [DllImport("user32.dll")] static extern bool   GetScrollInfo(IntPtr h, int bar, ref SCROLLINFO si);
            [DllImport("user32.dll")] static extern int    GetWindowLong(IntPtr h, int idx);
            [DllImport("user32.dll")] static extern int    GetSystemMetrics(int idx);

            private DarkNcScrollPainter() { }

            // Keep painters alive (prevent GC) and dedupe by HWND.
            private static readonly List<DarkNcScrollPainter> _live = new();
            private static readonly HashSet<IntPtr>           _seen = new();

            /// <summary>Subclass a control that owns native (non-client) scrollbars and paint them dark.</summary>
            public static void Attach(Control c)
            {
                if (c == null) return;
                void Hook()
                {
                    if (!c.IsHandleCreated || !_seen.Add(c.Handle)) return;
                    var p = new DarkNcScrollPainter();
                    try { p.AssignHandle(c.Handle); _live.Add(p); p.Paint(); }
                    catch { _seen.Remove(c.Handle); }
                }
                if (c.IsHandleCreated) Hook(); else c.HandleCreated += (_, _) => Hook();
                // Scrollbars appear lazily once content overflows; re-hook on (re)create / show.
                c.VisibleChanged += (_, _) => { if (c.Visible) Hook(); };
            }

            protected override void WndProc(ref Message m)
            {
                switch (m.Msg)
                {
                    case WM_NCDESTROY:
                        _seen.Remove(Handle); _live.Remove(this);
                        base.WndProc(ref m);
                        return;
                    case WM_NCPAINT:
                        base.WndProc(ref m);   // let the control lay out / paint its NC frame first
                        Paint();               // then overpaint the scrollbar(s) dark
                        return;
                }

                base.WndProc(ref m);

                // Repaint after anything that can move the thumb or re-show a (white) native bar.
                switch (m.Msg)
                {
                    case WM_SIZE:
                    case WM_VSCROLL:
                    case WM_HSCROLL:
                    case WM_MOUSEWHEEL:
                    case WM_NCMOUSEMOVE:
                    case WM_NCLBUTTONUP:
                    case WM_STYLECHANGED:
                    case WM_KEYUP:
                        Paint();
                        break;
                }
            }

            private void Paint()
            {
                if (Handle == IntPtr.Zero) return;
                int style = GetWindowLong(Handle, GWL_STYLE);
                bool vVis = (style & WS_VSCROLL) != 0;
                bool hVis = (style & WS_HSCROLL) != 0;
                if (!vVis && !hVis) return;   // nothing to paint until content overflows

                IntPtr hdc = GetWindowDC(Handle);
                if (hdc == IntPtr.Zero) return;
                try
                {
                    GetWindowRect(Handle, out RC wr);
                    GetClientRect(Handle, out RC cr);
                    var p = new PT(); ClientToScreen(Handle, ref p);
                    int clientW = cr.R - cr.L, clientH = cr.B - cr.T;
                    int bl = p.X - wr.L, bt = p.Y - wr.T;          // left/top border thickness
                    int cxv = GetSystemMetrics(SM_CXVSCROLL);
                    int cyh = GetSystemMetrics(SM_CYHSCROLL);
                    int cyv = GetSystemMetrics(SM_CYVSCROLL);       // vertical arrow-button height
                    int cxh = GetSystemMetrics(SM_CXHSCROLL);       // horizontal arrow-button width

                    using var g = Graphics.FromHdc(hdc);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // Scrollbars sit between the client area and the window border (window-local coords).
                    if (vVis)
                        DrawBar(g, new Rectangle(bl + clientW, bt, cxv, clientH - (hVis ? cyh : 0)), SB_VERT, cyv);
                    if (hVis)
                        DrawBar(g, new Rectangle(bl, bt + clientH, clientW, cyh), SB_HORZ, cxh);
                    if (vVis && hVis)
                    {
                        using var cb = new SolidBrush(_track);   // dead corner where the two bars meet
                        g.FillRectangle(cb, new Rectangle(bl + clientW, bt + clientH, cxv, cyh));
                    }
                }
                catch { }
                finally { ReleaseDC(Handle, hdc); }
            }

            private void DrawBar(Graphics g, Rectangle bar, int axis, int btn)
            {
                bool vert = axis == SB_VERT;
                using (var tb = new SolidBrush(_track)) g.FillRectangle(tb, bar);

                // Thumb metrics come straight from the OS scroll state (accurate for NC scrollbars).
                var si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
                if (GetScrollInfo(Handle, axis, ref si))
                {
                    int range = si.nMax - si.nMin + 1;
                    int page  = Math.Max(1, si.nPage);
                    int trackPx = (vert ? bar.Height : bar.Width) - 2 * btn;
                    if (range > page && trackPx > 4)
                    {
                        int thumbPx = Math.Max(24, (int)((double)page / range * trackPx));
                        thumbPx = Math.Min(thumbPx, trackPx);
                        int scrollRange = range - page;
                        int posOff = Math.Max(0, Math.Min(si.nPos - si.nMin, scrollRange));
                        int thumbOff = scrollRange <= 0 ? 0 : (int)((double)posOff / scrollRange * (trackPx - thumbPx));
                        Rectangle tr = vert
                            ? new Rectangle(bar.X + 3, bar.Y + btn + thumbOff, Math.Max(1, bar.Width - 6), thumbPx)
                            : new Rectangle(bar.X + btn + thumbOff, bar.Y + 3, thumbPx, Math.Max(1, bar.Height - 6));
                        using var br = new SolidBrush(_thumb);
                        using var gp = DarkScrollPainter.RoundPath(tr, 3);
                        g.FillPath(br, gp);
                    }
                }

                // Arrow glyphs (reuse the grid painter's shapes so every bar looks identical).
                using var ab = new SolidBrush(_arrow);
                if (vert)
                {
                    DarkScrollPainter.Arrow(g, ab, new Rectangle(bar.X, bar.Y, bar.Width, btn), true, true);
                    DarkScrollPainter.Arrow(g, ab, new Rectangle(bar.X, bar.Bottom - btn, bar.Width, btn), true, false);
                }
                else
                {
                    DarkScrollPainter.Arrow(g, ab, new Rectangle(bar.X, bar.Y, btn, bar.Height), false, true);
                    DarkScrollPainter.Arrow(g, ab, new Rectangle(bar.Right - btn, bar.Y, btn, bar.Height), false, false);
                }
            }
        }
        // ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Attach a custom dark painter to every scrollbar this control owns.</summary>
        public static void DarkScrollbars(Control c)
        {
            void Apply()
            {
                try
                {
                    // For controls with native (non-client) scrollbars — ListView, TreeView, ListBox,
                    // TextBox/RichTextBox — the Windows dark theme is the only lever; apply it.
                    AllowDarkModeForWindow(c.Handle, true);
                    if (c is not TextBoxBase)
                    {
                        SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                        SendMessage(c.Handle, WM_THEMECHANGED, IntPtr.Zero, IntPtr.Zero);
                    }

                    // DataGridView hosts real managed VScrollBar/HScrollBar children. Find them by
                    // TYPE (field names differ across runtimes: ".NET Framework" used vertScrollBar,
                    // .NET 8 uses _vertScrollBar) and custom-paint them dark.
                    if (c is DataGridView dgv)
                    {
                        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        foreach (var fi in typeof(DataGridView).GetFields(flags))
                            if (typeof(ScrollBar).IsAssignableFrom(fi.FieldType) && fi.GetValue(dgv) is ScrollBar sb)
                                DarkScrollPainter.Attach(sb);
                    }

                    // Any other managed ScrollBar children (e.g. on custom scrollable panels).
                    foreach (Control ch in c.Controls)
                        if (ch is ScrollBar sb2)
                            DarkScrollPainter.Attach(sb2);

                    // Controls whose scrollbars are NATIVE non-client bars (RichTextBox/TextBox,
                    // ListBox, ListView, TreeView, AutoScroll panels) — overpaint them near-black so
                    // they match the grids instead of staying white (RichEdit) or Explorer-grey.
                    if (c is TextBoxBase || c is ListBox || c is ListView || c is TreeView ||
                        (c is ScrollableControl sc && sc.AutoScroll && c is not DataGridView))
                        DarkNcScrollPainter.Attach(c);
                }
                catch { }
            }

            if (c.IsHandleCreated) Apply(); else c.HandleCreated += (_, _) => Apply();

            // Scrollbars are created/shown lazily once content overflows — re-apply on those events.
            if (c is RichTextBox rtb)
                rtb.ContentsResized += (_, _) => { try { if (rtb.IsHandleCreated) rtb.BeginInvoke(Apply); } catch { } };
            if (c is DataGridView dg)
                dg.RowsAdded += (_, _) => { try { if (dg.IsHandleCreated) dg.BeginInvoke(Apply); } catch { } };
        }

        /// <summary>Apply dark scrollbars to every scrollable child control under a form.</summary>
        public static void ApplyDarkScrollbarsRecursive(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is DataGridView || c is TextBoxBase || c is ListBox || c is ListView || c is TreeView ||
                    (c is ScrollableControl sc && sc.AutoScroll))
                    DarkScrollbars(c);
                if (c.HasChildren) ApplyDarkScrollbarsRecursive(c);
            }
        }

        // Month-calendar colour message (MCM_SETCOLOR) + its colour indices.
        private const int MCM_FIRST = 0x1000;
        private const int MCM_SETCOLOR = MCM_FIRST + 10;
        private const int MCSC_BACKGROUND = 0, MCSC_TEXT = 1, MCSC_TITLEBK = 2,
                          MCSC_TITLETEXT = 3, MCSC_MONTHBK = 4, MCSC_TRAILINGTEXT = 5;

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWndProc lpfn, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWndProc lpfn, IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
        // RealGetWindowClass returns the underlying Win32 class BEFORE any WinForms subclassing,
        // so a .NET VScrollBar/HScrollBar (registered as "WindowsForms10.SCROLLBAR.xxx") still
        // returns "ScrollBar" here — unlike GetClassName which returns the subclass name.
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RealGetWindowClass(IntPtr hWnd, System.Text.StringBuilder pszType, uint cchType);
        private delegate bool EnumWndProc(IntPtr hWnd, IntPtr lParam);

        private static int ColorRef(Color c) => c.R | (c.G << 8) | (c.B << 16);

        private static string ClassOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        private static string RealClassOf(IntPtr h)
        {
            var sb = new System.Text.StringBuilder(64);
            RealGetWindowClass(h, sb, (uint)sb.Capacity);
            return sb.ToString();
        }

        private static void DarkenIfMonthCal(IntPtr h)
        {
            if (ClassOf(h) != "SysMonthCal32") return;
            try { SetWindowTheme(h, "DarkMode_Explorer", "DarkMode_Explorer"); } catch { }
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_BACKGROUND,   (IntPtr)ColorRef(Surface));
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_MONTHBK,      (IntPtr)ColorRef(Surface));
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_TITLEBK,      (IntPtr)ColorRef(Color.FromArgb(24, 24, 30)));
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_TITLETEXT,    (IntPtr)ColorRef(TextPrimary));
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_TEXT,         (IntPtr)ColorRef(TextPrimary));
            SendMessage(h, MCM_SETCOLOR, (IntPtr)MCSC_TRAILINGTEXT, (IntPtr)ColorRef(TextSecondary));
        }

        private static void DarkenControlTree(Control c)
        {
            try
            {
                c.BackColor = Surface;
                // Guna panels paint with FillColor, not BackColor — set it reflectively if present.
                var fill = c.GetType().GetProperty("FillColor");
                if (fill != null && fill.CanWrite && fill.PropertyType == typeof(Color))
                    fill.SetValue(c, Surface);
            }
            catch { }
            foreach (Control child in c.Controls) DarkenControlTree(child);
        }

        /// <summary>
        /// Darken any month-calendar popup currently open on this UI thread (the dropdown shown by a
        /// date picker). Applies the dark explorer theme plus explicit dark colours so the calendar
        /// body, header and day text all match the app instead of the default white calendar.
        /// The calendar is a child window of the dropdown host, so every top-level thread window is
        /// searched recursively.
        /// </summary>
        public static void DarkenOpenMonthCalendars()
        {
            try
            {
                EnumThreadWindows(GetCurrentThreadId(), (top, l) =>
                {
                    bool hasCal = ClassOf(top) == "SysMonthCal32";
                    DarkenIfMonthCal(top);
                    EnumChildWindows(top, (c, l2) =>
                    {
                        string cls = ClassOf(c);
                        if (cls == "SysMonthCal32") { DarkenIfMonthCal(c); hasCal = true; }
                        else if (cls == "SysDateTimePick32") { try { SetWindowTheme(c, "DarkMode_CFD", null); } catch { } }
                        return true;
                    }, IntPtr.Zero);

                    // Darken the dropdown's host form (and every nested panel) so its frame/padding
                    // isn't a white border around the dark calendar.
                    if (hasCal && Control.FromHandle(top) is Control host)
                        DarkenControlTree(host);
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
        }

        private static Icon _appIcon;

        /// <summary>
        /// Teal import-arrow brand icon generated at runtime — matches the embedded appicon.ico exactly.
        /// Cached after first call so every form's title bar + taskbar button shows the same icon.
        /// </summary>
        public static Icon AppIcon()
        {
            if (_appIcon != null) return _appIcon;
            try
            {
                using var bmp = new Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    LogoBadge.Render(g, new RectangleF(0, 0, 64, 64));
                }
                _appIcon = Icon.FromHandle(bmp.GetHicon());
            }
            catch { }
            return _appIcon;
        }

        public static void ApplyWindow(Form form)
        {
            form.BackColor = Canvas;
            form.Font = Body;
            form.ForeColor = TextPrimary;
            var icon = AppIcon();
            if (icon != null) { form.Icon = icon; form.ShowIcon = true; }
            DarkTitleBar(form);
            // Allow dark mode for the form window itself so its own non-client scrollbar area
            // (if any) renders dark, then recurse into all controls once handles are created.
            void OnShown(object s, EventArgs e)
            {
                try { AllowDarkModeForWindow(form.Handle, true); } catch { }
                ApplyDarkScrollbarsRecursive(form);
            }
            form.Shown += OnShown;
        }

        public static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            if (radius <= 0) { path.AddRectangle(r); path.CloseFigure(); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void RoundRegion(Control c, int radius)
        {
            if (c.Width <= 0 || c.Height <= 0) return;
            c.Region = new Region(RoundedRect(new Rectangle(0, 0, c.Width, c.Height), radius));
        }

        /// <summary>Pill button: filled accent (primary) or subtle glass tint (secondary).</summary>
        public static void StylePillButton(Button b, bool primary = true)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Font = Font(10f, FontStyle.Bold);
            b.ForeColor = primary ? Color.White : TextPrimary;
            b.BackColor = primary ? Accent : SecondaryFill;
            b.Cursor = Cursors.Hand;
            if (b.Height < 30) b.Height = 32;

            Color baseColor = b.BackColor;
            Color hover = primary ? AccentHover : SecondaryHover;
            b.MouseEnter += (s, e) => b.BackColor = b.Enabled ? hover : baseColor;
            b.MouseLeave += (s, e) => b.BackColor = baseColor;
            b.EnabledChanged += (s, e) => b.BackColor = baseColor;
            b.Resize += (s, e) => RoundRegion(b, 9);
            RoundRegion(b, 9);
        }

        public static void StyleInput(Control c)
        {
            c.BackColor = ControlFill;
            c.ForeColor = TextPrimary;
            c.Font = Body;
            if (c is TextBox tb) tb.BorderStyle = BorderStyle.FixedSingle;
            if (c is ComboBox cb) { cb.FlatStyle = FlatStyle.Flat; cb.BackColor = ControlFill; cb.ForeColor = TextPrimary; }
        }

        /// <summary>Apply the app's dark palette to a ContextMenuStrip (background, text, hover).</summary>
        public static void DarkenMenu(ContextMenuStrip menu)
        {
            menu.BackColor  = Canvas;
            menu.ForeColor  = TextPrimary;
            menu.Font       = Body;
            menu.Renderer   = new ToolStripProfessionalRenderer(new DarkMenuColorTable());
            // ForeColor must propagate to existing and future items
            void Colour(ToolStripItemCollection items)
            {
                foreach (ToolStripItem item in items)
                {
                    item.ForeColor = TextPrimary;
                    item.BackColor = Canvas;
                    if (item is ToolStripMenuItem mi) Colour(mi.DropDownItems);
                }
            }
            Colour(menu.Items);
            menu.ItemAdded += (s, e) => { e.Item.ForeColor = TextPrimary; e.Item.BackColor = Canvas; };
        }

        private sealed class DarkMenuColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground       => Canvas;
            public override Color MenuBorder                        => Hairline;
            public override Color MenuItemBorder                    => Accent;
            public override Color MenuItemSelected                  => Color.FromArgb(36, 36, 42);
            public override Color MenuItemSelectedGradientBegin     => Color.FromArgb(36, 36, 42);
            public override Color MenuItemSelectedGradientEnd       => Color.FromArgb(36, 36, 42);
            public override Color MenuStripGradientBegin            => Canvas;
            public override Color MenuStripGradientEnd              => Canvas;
            public override Color ImageMarginGradientBegin          => Canvas;
            public override Color ImageMarginGradientMiddle         => Canvas;
            public override Color ImageMarginGradientEnd            => Canvas;
            public override Color SeparatorDark                     => Hairline;
            public override Color SeparatorLight                    => Hairline;
        }

        /// <summary>Dark-theme a DataGridView (headers, cells, selection, gridlines).</summary>
        public static void StyleGrid(DataGridView g)
        {
            DarkScrollbars(g);     // dark scrollbars on every themed grid
            g.EnableHeadersVisualStyles = false;
            g.BackgroundColor = Surface;
            g.GridColor = Hairline;
            g.BorderStyle = BorderStyle.None;
            g.RowHeadersVisible = false;
            g.Font = Body;
            g.ColumnHeadersHeight = 34;
            g.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(24, 24, 30);
            g.ColumnHeadersDefaultCellStyle.ForeColor = TextSecondary;
            g.ColumnHeadersDefaultCellStyle.Font = Font(9.5f, FontStyle.Bold);
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(24, 24, 30);
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = TextPrimary;
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(0, 70, 66);   // dark teal row selection
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(34, 34, 41);
            g.AlternatingRowsDefaultCellStyle.ForeColor = TextPrimary;
            g.RowTemplate.Height = 28;
        }
    }
}
