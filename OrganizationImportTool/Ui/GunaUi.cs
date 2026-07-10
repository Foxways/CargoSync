using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
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

        /// <summary>
        /// Guna2Button subclass that paints a subtle glass highlight over the base button —
        /// a semi-transparent white gradient covering the top 45% gives a premium glossy depth
        /// without needing image assets. Works on both primary (teal) and secondary (dark) buttons.
        /// </summary>
        internal sealed class GlossButton : Guna2Button
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (Width < 6 || Height < 6) return;

                var g = e.Graphics;
                g.SmoothingMode   = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // Clip the gloss to the button's rounded outline
                int r = Math.Max(0, BorderRadius);
                using var clip = AppleTheme.RoundedRect(new Rectangle(1, 1, Width - 2, Height - 2), r);

                // Teal primary buttons are much lighter than dark secondary ones, so a fixed white
                // alpha looks faint on teal and strong on dark.  Scale the highlight to perceived
                // brightness so both look equally glossy: bright fill → stronger sheen needed.
                float brightness = (FillColor.R + FillColor.G + FillColor.B) / (3f * 255f);
                int topAlpha = brightness > 0.30f ? 90 : 52;  // primary≈0.51 → 90; secondary≈0.16 → 52

                using var gloss = new LinearGradientBrush(
                    new Point(0, 0), new Point(0, Math.Max(1, Height)),
                    Color.FromArgb(topAlpha, 255, 255, 255),
                    Color.FromArgb(0,        255, 255, 255));
                // Ease-out: fast fade over the top ~45%, then essentially invisible — no hard line.
                gloss.Blend = new Blend(4)
                {
                    Factors   = new[] { 0f, 0.60f, 0.90f, 1f },
                    Positions = new[] { 0f, 0.38f, 0.55f, 1f }
                };

                g.FillPath(gloss, clip);
            }
        }

        public static Guna2Button Button(string text, bool primary)
        {
            var b = new GlossButton
            {
                Text = text,
                BorderRadius = 10,
                Font = AppleTheme.Font(10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                ForeColor = Color.White,
                Animated = true,
                AutoRoundedCorners = false,
                TextAlign = HorizontalAlignment.Center,
                Padding   = new Padding(0)   // zero internal padding so TextAlign.Center uses the full client width
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

            // Explicit-width FlowLayouts (no AutoSize+DockStyle) prevent the known WinForms layout
            // bug where AutoSize fights DockStyle and causes buttons to overlap or be hidden.
            // 8px horizontal gap, 5px top/bottom → 5+34+5=44px = strip height → buttons centred.
            foreach (var b in right) b.Margin = new Padding(8, 5, 8, 5);
            var rf = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, BackColor = Color.Transparent,
                Width = right.Sum(b => b.Width + 16)   // 8 left + 8 right margin per button
            };
            foreach (var b in right) rf.Controls.Add(b);
            strip.Controls.Add(rf);

            if (left != null && left.Length > 0)
            {
                foreach (var b in left) b.Margin = new Padding(8, 5, 8, 5);
                var lf = new FlowLayoutPanel
                {
                    Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false, BackColor = Color.Transparent,
                    Width = left.Sum(b => b.Width + 16)
                };
                foreach (var b in left) lf.Controls.Add(b);
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
            var c = new Guna2ComboBox
            {
                FillColor = AppleTheme.ControlFill,
                BorderColor = AppleTheme.Hairline,
                BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body,
                ItemHeight = 28,
                FocusedColor = AppleTheme.Accent   // dropdown arrow colour when focused/open
            };
            c.HoverState.BorderColor   = AppleTheme.Accent;
            c.HoverState.FillColor     = AppleTheme.ControlFill;
            c.FocusedState.BorderColor = AppleTheme.Accent;
            c.FocusedState.FillColor   = AppleTheme.ControlFill;
            return c;
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

        /// <summary>
        /// Dark date picker matching the app's inputs (the native DateTimePicker can't be darkened —
        /// it stays a white box). Shows date only (no time), keeps the optional check box so an
        /// unchecked picker means "no bound", and darkens its dropdown calendar instead of the
        /// default white one.
        /// </summary>
        public static Guna2DateTimePicker DatePicker()
        {
            var d = new Guna2DateTimePicker
            {
                FillColor = AppleTheme.ControlFill,
                BorderColor = AppleTheme.Hairline,
                BorderRadius = 8,
                BorderThickness = 1,
                ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body,
                // Guna's "Short" still appends the time; force an explicit date-only custom format.
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd/MM/yyyy",
                ShowCheckBox = true,
                Checked = false,
                Animated = false                       // no focus animation → filter bar doesn't reflow
            };
            d.HoverState.FillColor = AppleTheme.ControlFill;
            d.HoverState.BorderColor = AppleTheme.Accent;
            d.HoverState.ForeColor = AppleTheme.TextPrimary;
            d.CheckedState.FillColor = AppleTheme.ControlFill;
            d.CheckedState.BorderColor = AppleTheme.Accent;
            d.CheckedState.ForeColor = AppleTheme.TextPrimary;
            d.FocusedColor = AppleTheme.Accent;
            // The dropdown calendar is a native white SysMonthCal32 hosted in Guna's (modal) popup.
            // A WinForms Timer still ticks inside that modal loop, so use one to recolour the calendar
            // dark the moment it appears (BeginInvoke wouldn't run until the popup closed).
            d.DropDown += (s, e) =>
            {
                int ticks = 0;
                var timer = new Timer { Interval = 15 };
                timer.Tick += (ts, te) =>
                {
                    AppleTheme.DarkenOpenMonthCalendars();
                    if (++ticks >= 6) timer.Stop();   // a few passes catch it once created, then stop
                };
                timer.Start();
                d.CloseUp += (cs, ce) => timer.Stop();
            };
            return d;
        }

        public static Guna2NumericUpDown Numeric()
        {
            var n = new Guna2NumericUpDown
            {
                FillColor             = AppleTheme.ControlFill,
                BorderColor           = AppleTheme.Hairline,
                BorderRadius          = 8,
                ForeColor             = AppleTheme.TextPrimary,
                Font                  = AppleTheme.Body,
                UpDownButtonFillColor = AppleTheme.ControlFill,  // button bg matches field
                UpDownButtonForeColor = AppleTheme.Accent        // teal arrows
            };
            // Guna2NumericUpDown has no HoverState/FocusedState objects — wire Enter/Leave instead.
            n.Enter += (s, e) => { n.BorderColor = AppleTheme.Accent; n.UpDownButtonFillColor = Color.FromArgb(0, 50, 48); };
            n.Leave += (s, e) => { n.BorderColor = AppleTheme.Hairline; n.UpDownButtonFillColor = AppleTheme.ControlFill; };
            return n;
        }

        public static Guna2CheckBox Check(string text)
        {
            var c = new Guna2CheckBox { Text = text, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body, AutoSize = true };
            c.CheckedState.FillColor   = AppleTheme.Accent;
            c.CheckedState.BorderColor = AppleTheme.Accent;
            c.CheckMarkColor           = AppleTheme.Accent;      // tick mark inside the checked box
            c.UncheckedState.BorderColor = AppleTheme.TextSecondary;
            return c;
        }
    }
}
