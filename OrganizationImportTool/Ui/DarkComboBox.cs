using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// A DropDownList combo that actually honours a dark theme. Standard WinForms combos paint
    /// their closed field white regardless of BackColor, so we owner-draw the items and repaint
    /// the closed control (background, selected text, chevron, border) after the system pass.
    /// </summary>
    public class DarkComboBox : ComboBox
    {
        private const int WM_PAINT = 0x000F;

        public DarkComboBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            DropDownStyle = ComboBoxStyle.DropDownList;
            BackColor = AppleTheme.ControlFill;
            ForeColor = AppleTheme.TextPrimary;
            ItemHeight = 22;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var bg = new SolidBrush(sel ? AppleTheme.Accent : AppleTheme.ControlFill);
            e.Graphics.FillRectangle(bg, e.Bounds);
            using var fg = new SolidBrush(sel ? Color.White : AppleTheme.TextPrimary);
            e.Graphics.DrawString(GetItemText(Items[e.Index]), Font, fg, e.Bounds.Left + 3, e.Bounds.Top + 2);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_PAINT) return;

            using var g = CreateGraphics();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var client = new Rectangle(0, 0, Width, Height);
            using (var b = new SolidBrush(AppleTheme.ControlFill)) g.FillRectangle(b, client);

            string text = SelectedIndex >= 0 ? GetItemText(SelectedItem) : string.Empty;
            TextRenderer.DrawText(g, text, Font, new Rectangle(5, 0, Width - 26, Height),
                Enabled ? AppleTheme.TextPrimary : AppleTheme.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            int cx = Width - 17, cy = Height / 2 - 2;
            using (var p = new Pen(AppleTheme.TextSecondary, 1.6f))
                g.DrawLines(p, new[] { new Point(cx, cy), new Point(cx + 4, cy + 4), new Point(cx + 8, cy) });

            using (var bp = new Pen(AppleTheme.Hairline))
                g.DrawRectangle(bp, new Rectangle(0, 0, Width - 1, Height - 1));
        }
    }
}
