using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// An Apple-style "card": rounded rectangle with a hairline border and a soft drop shadow.
    /// Set <see cref="Glass"/> for a translucent frosted-panel approximation.
    /// </summary>
    public class Card : Panel
    {
        public int CornerRadius { get; set; } = 14;
        public bool Glass { get; set; } = false;
        public int ShadowDepth { get; set; } = 6;

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Padding = new Padding(16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int s = Math.Max(2, ShadowDepth);
            var body = new Rectangle(s, s, Width - (s * 2) - 1, Height - (s * 2) - 1);
            if (body.Width <= 0 || body.Height <= 0) { base.OnPaint(e); return; }

            // soft outer glow (subtle, reads as elevation on a dark canvas)
            for (int i = s; i > 0; i--)
            {
                int alpha = 4 + (s - i) * 2;
                using var sp = AppleTheme.RoundedRect(
                    new Rectangle(body.X - i, body.Y - i, body.Width + i * 2, body.Height + i * 2), CornerRadius + i);
                using var sb = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0));
                g.FillPath(sb, sp);
            }

            using var path = AppleTheme.RoundedRect(body, CornerRadius);
            using var fill = new SolidBrush(Glass ? AppleTheme.CardFillGlass : AppleTheme.CardFill);
            using var pen = new Pen(AppleTheme.Hairline, 1);
            g.FillPath(fill, path);
            // faint top highlight line for the glass feel
            using (var hi = new Pen(Color.FromArgb(22, 255, 255, 255), 1))
                g.DrawLine(hi, body.X + CornerRadius, body.Y + 1, body.Right - CornerRadius, body.Y + 1);
            g.DrawPath(pen, path);

            base.OnPaint(e);
        }
    }
}
