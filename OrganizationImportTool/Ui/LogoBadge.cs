using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// The app logo, drawn (no image asset): a rounded accent tile with a white "import into a
    /// tray" glyph - an arrow descending into an open box. Reusable on the auth screens / header.
    /// </summary>
    public class LogoBadge : Panel
    {
        public LogoBadge()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Size = new Size(72, 72);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Render(e.Graphics, new RectangleF(0, 0, Width - 1, Height - 1));
            base.OnPaint(e);
        }

        /// <summary>
        /// Draws the app mark (accent rounded tile + white "import into a tray" glyph) into the given
        /// square. Shared by the on-screen badge and the generated application icon.
        /// </summary>
        public static void Render(Graphics g, RectangleF rect)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float w = rect.Width, h = rect.Height, ox = rect.X, oy = rect.Y;

            // rounded gradient tile
            using (var path = AppleTheme.RoundedRect(Rectangle.Round(rect), (int)Math.Max(3, w / 5)))
            using (var brush = new LinearGradientBrush(rect, AppleTheme.Accent, AppleTheme.AccentHover, 60f))
                g.FillPath(brush, path);

            float cx = ox + w / 2f;
            using var pen = new Pen(Color.White, Math.Max(1.5f, w / 18f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

            // open tray (box) at the bottom
            float boxTop = oy + h * 0.58f, boxBottom = oy + h * 0.74f;
            float boxLeft = ox + w * 0.28f, boxRight = ox + w * 0.72f;
            g.DrawLines(pen, new[]
            {
                new PointF(boxLeft, boxTop), new PointF(boxLeft, boxBottom),
                new PointF(boxRight, boxBottom), new PointF(boxRight, boxTop),
            });

            // descending arrow into the tray
            float arrowTop = oy + h * 0.24f, arrowBottom = oy + h * 0.55f;
            g.DrawLine(pen, cx, arrowTop, cx, arrowBottom);
            float head = w * 0.12f;
            g.DrawLines(pen, new[]
            {
                new PointF(cx - head, arrowBottom - head),
                new PointF(cx, arrowBottom),
                new PointF(cx + head, arrowBottom - head),
            });
        }
    }
}
