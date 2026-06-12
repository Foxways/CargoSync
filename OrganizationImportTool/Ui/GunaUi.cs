using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// Factory helpers that build Guna UI 2 controls pre-styled for the app's dark theme,
    /// so every screen looks consistent and avoids custom GDI painting (which caused flicker).
    /// </summary>
    public static class GunaUi
    {
        /// <summary>The app's standard loading spinner (hidden until Start()).</summary>
        public static Spinner Spinner(int size = 28) => new Spinner { Size = new Size(size, size), Visible = false };

        public static Guna2Button Button(string text, bool primary)
        {
            var b = new Guna2Button
            {
                Text = text,
                BorderRadius = 10,
                Font = AppleTheme.Font(10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                ForeColor = Color.White,
                Animated = true,
                AutoRoundedCorners = false,
                TextAlign = HorizontalAlignment.Center
            };
            if (primary)
            {
                b.FillColor = AppleTheme.Accent;
                b.HoverState.FillColor = AppleTheme.AccentHover;
            }
            else
            {
                b.FillColor = AppleTheme.SecondaryFill;
                b.ForeColor = AppleTheme.TextPrimary;
                b.BorderColor = AppleTheme.Hairline;
                b.BorderThickness = 1;
                b.HoverState.FillColor = AppleTheme.SecondaryHover;
                b.HoverState.BorderColor = AppleTheme.Hairline;
            }
            b.DisabledState.FillColor = Color.FromArgb(40, 40, 46);
            b.DisabledState.ForeColor = AppleTheme.TextSecondary;
            b.DisabledState.BorderColor = Color.FromArgb(50, 50, 58);

            // Guna buttons don't auto-close a dialog via DialogResult like a standard WinForms button
            // does - wire it here so ANY GunaUi.Button with a DialogResult closes its form (works for
            // both modal ShowDialog and non-modal hosts; DialogResult buttons are always close actions).
            b.Click += (s, e) =>
            {
                if (b.DialogResult != DialogResult.None)
                {
                    var form = b.FindForm();
                    if (form != null) { form.DialogResult = b.DialogResult; form.Close(); }
                }
            };
            return b;
        }

        /// <summary>
        /// A consistent bottom action bar: right-aligned primary group (first item = rightmost) and an
        /// optional left group. Tall enough with bottom clearance so buttons never sit against the
        /// Windows taskbar on a maximized window, and both groups are vertically aligned.
        /// </summary>
        public static Panel ButtonBar(Control[] right, Control[] left = null)
        {
            // Tall bar (108) with the buttons pinned to the TOP via a 44px strip; the ~50px of empty
            // space BELOW keeps them well clear of the Windows taskbar (the maximized form's client
            // bottom sits right at the taskbar top).
            var bar = new Panel { Dock = DockStyle.Bottom, Height = 108, BackColor = AppleTheme.Canvas, Padding = new Padding(18, 14, 18, 0) };
            var strip = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent };
            var rf = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            // Uniform 8px each side → buttons are vertically aligned (top/bottom 0) with an even 16px gap.
            foreach (var b in right) { b.Margin = new Padding(8, 0, 8, 0); rf.Controls.Add(b); }
            strip.Controls.Add(rf);
            if (left != null && left.Length > 0)
            {
                var lf = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
                foreach (var b in left) { b.Margin = new Padding(8, 0, 8, 0); lf.Controls.Add(b); }
                strip.Controls.Add(lf);
            }
            bar.Controls.Add(strip);
            return bar;
        }

        public static Guna2Panel Card()
        {
            var p = new Guna2Panel
            {
                FillColor = AppleTheme.Surface,
                BorderColor = AppleTheme.Hairline,
                BorderThickness = 1,
                BorderRadius = 14,
                Padding = new Padding(14)
            };
            p.ShadowDecoration.Enabled = true;
            p.ShadowDecoration.Color = Color.Black;
            p.ShadowDecoration.Depth = 7;
            return p;
        }

        public static Guna2ComboBox Combo()
        {
            return new Guna2ComboBox
            {
                FillColor = AppleTheme.ControlFill,
                BorderColor = AppleTheme.Hairline,
                BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body,
                ItemHeight = 28
            };
        }

        public static Guna2TextBox TextBox(string placeholder = "")
        {
            var t = new Guna2TextBox
            {
                FillColor = AppleTheme.ControlFill,
                BorderColor = AppleTheme.Hairline,
                BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body,
                PlaceholderText = placeholder,
                PlaceholderForeColor = AppleTheme.TextSecondary
            };
            // keep disabled fields dark (Guna defaults to a light disabled fill)
            t.DisabledState.FillColor = Color.FromArgb(34, 34, 40);
            t.DisabledState.BorderColor = AppleTheme.Hairline;
            t.DisabledState.ForeColor = AppleTheme.TextSecondary;
            t.DisabledState.PlaceholderForeColor = AppleTheme.TextSecondary;
            t.HoverState.BorderColor = AppleTheme.Accent;
            t.FocusedState.BorderColor = AppleTheme.Accent;
            return t;
        }

        public static Guna2NumericUpDown Numeric()
        {
            return new Guna2NumericUpDown
            {
                FillColor = AppleTheme.ControlFill,
                BorderColor = AppleTheme.Hairline,
                BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body
            };
        }

        public static Guna2CheckBox Check(string text)
        {
            var c = new Guna2CheckBox { Text = text, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body, AutoSize = true };
            c.CheckedState.FillColor = AppleTheme.Accent;
            c.CheckedState.BorderColor = AppleTheme.Accent;
            c.UncheckedState.BorderColor = AppleTheme.TextSecondary;
            return c;
        }
    }
}
