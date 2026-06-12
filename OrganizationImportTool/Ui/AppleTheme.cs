using System;
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
        public static readonly Color Accent         = Color.FromArgb(10, 132, 255);  // #0A84FF iOS dark blue
        public static readonly Color AccentHover    = Color.FromArgb(56, 158, 255);
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

        public static Font Title    => Font(18f, FontStyle.Bold);
        public static Font Headline => Font(12.5f, FontStyle.Bold);
        public static Font Body     => Font(10f, FontStyle.Regular);
        public static Font Caption  => Font(8.5f, FontStyle.Regular);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        // Undocumented but stable Win10 1809+/Win11 uxtheme exports that put the whole app in dark mode,
        // so EVERY native scrollbar (grids, text boxes, lists, the main screen) renders dark.
        [DllImport("uxtheme.dll", EntryPoint = "#135", CharSet = CharSet.Unicode)]
        private static extern int SetPreferredAppMode(int mode);  // 0 Default, 1 AllowDark, 2 ForceDark, 3 ForceLight
        [DllImport("uxtheme.dll", EntryPoint = "#136")]
        private static extern void FlushMenuThemes();

        /// <summary>Force the whole application into dark mode (call once at startup, before any form).</summary>
        public static void EnableDarkMode()
        {
            try { SetPreferredAppMode(2); FlushMenuThemes(); } catch { /* older Windows - ignore */ }
        }

        /// <summary>
        /// Switch a control's native scrollbars (DataGridView, TextBox, etc.) to the Windows dark theme
        /// so they're dark instead of the default light/white — a more premium look.
        /// </summary>
        public static void DarkScrollbars(Control c)
        {
            void Apply()
            {
                try
                {
                    SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
                    // A DataGridView's scroll bars are private child ScrollBar controls — find them by
                    // type (field names vary across runtimes) and theme each directly.
                    if (c is DataGridView dgv)
                        foreach (var fi in typeof(DataGridView).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
                            if (typeof(ScrollBar).IsAssignableFrom(fi.FieldType) && fi.GetValue(dgv) is ScrollBar sb && sb.IsHandleCreated)
                                SetWindowTheme(sb.Handle, "DarkMode_Explorer", null);
                }
                catch { }
            }
            if (c.IsHandleCreated) Apply(); else c.HandleCreated += (s, e) => Apply();
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

        private static Icon _appIcon;
        private static bool _appIconTried;

        /// <summary>The application icon (appicon.ico), loaded once and cached. Null if missing.</summary>
        public static Icon AppIcon()
        {
            if (_appIconTried) return _appIcon;
            _appIconTried = true;
            try
            {
                string p = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appicon.ico");
                if (System.IO.File.Exists(p)) _appIcon = new Icon(p);
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
            // Dark scrollbars on every scrollable control once the form is shown (handles exist).
            form.Shown += (s, e) => ApplyDarkScrollbarsRecursive(form);
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
            g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(38, 70, 110);
            g.DefaultCellStyle.SelectionForeColor = Color.White;
            g.DefaultCellStyle.Padding = new Padding(4, 2, 4, 2);
            g.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(34, 34, 41);
            g.AlternatingRowsDefaultCellStyle.ForeColor = TextPrimary;
            g.RowTemplate.Height = 28;
        }
    }
}
