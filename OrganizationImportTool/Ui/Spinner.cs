using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// The app's one loading indicator: a clean anti-aliased rotating arc in the accent color
    /// over a faint track ring. Replaces the dotted Guna2WinProgressIndicator everywhere.
    /// Same Start()/Stop() API, scales with its Size (DPI-safe), double-buffered (no flicker).
    ///
    /// Busy patterns: (1) corner/inline spinner next to live output (main window, Copilot),
    /// (2) centered spinner with inputs disabled for blocking waits (sign-in).
    /// </summary>
    public sealed class Spinner : Control
    {
        private readonly Timer _timer;
        private float _angle;

        public Spinner()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Size = new Size(28, 28);
            TabStop = false;
            _timer = new Timer { Interval = 16 }; // ~60 fps
            _timer.Tick += (s, e) => { _angle = (_angle + 6f) % 360f; Invalidate(); };
        }

        /// <summary>Arc color (defaults to the theme accent).</summary>
        public Color ArcColor { get; set; } = AppleTheme.Accent;

        public bool IsSpinning => _timer.Enabled;

        public void Start() { Visible = true; _timer.Start(); }

        public void Stop() { _timer.Stop(); Visible = false; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_timer.Enabled) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float pen = Math.Max(2.5f, Math.Min(Width, Height) / 9f);
            var rect = new RectangleF(pen / 2f + 1, pen / 2f + 1, Width - pen - 2, Height - pen - 2);

            using (var track = new Pen(Color.FromArgb(36, ArcColor), pen))
                g.DrawEllipse(track, rect);

            using (var arc = new Pen(ArcColor, pen) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(arc, rect, _angle, 280f);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
