using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Profiling
{
    /// <summary>
    /// Pre-flight data-health dashboard: headline stats, an overall risk grade with the top risk
    /// factors, and a per-field profile (fill rate, distinct values, sample, issues). The operator
    /// reviews the picture and chooses to continue the import or cancel.
    /// </summary>
    public class ProfileDashboardForm : Form
    {
        private readonly ProfileReport _r;
        private Guna2DataGridView _grid = null!;

        public ProfileDashboardForm(ProfileReport report)
        {
            _r = report;
            BuildUi();
            Populate();
        }

        private Color RiskColor => _r.Level switch
        {
            RiskLevel.Low => AppleTheme.Success,
            RiskLevel.Medium => AppleTheme.Warning,
            _ => AppleTheme.Danger
        };

        private void BuildUi()
        {
            Text = "Data profile & risk";
            ClientSize = new Size(1000, 660);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = AppleTheme.Canvas };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(52)));   // title
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(116)));  // stat cards
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(178)));  // risk factors
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // field grid
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));  // buttons (tall; pinned to top to clear the taskbar)

            var title = new Label
            {
                Text = "Pre-flight data profile", Dock = DockStyle.Fill,
                Padding = new Padding(20, 12, 0, 0), Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary
            };

            // ---- stat cards ----
            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = AppleTheme.Canvas, Padding = new Padding(14, 4, 14, 8) };
            for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            cards.Controls.Add(Card($"{_r.RowCount}", "Rows", AppleTheme.TextPrimary), 0, 0);
            cards.Controls.Add(Card($"{_r.MappedFieldCount}", "Mapped fields", AppleTheme.TextPrimary), 1, 0);
            cards.Controls.Add(Card(_r.Level.ToString().ToUpperInvariant(), "Risk level", RiskColor), 2, 0);
            cards.Controls.Add(Card($"{_r.Score}", "Risk score (0–100)", RiskColor), 3, 0);

            // ---- risk factors ----
            var factorsPanel = new Guna2Panel { Dock = DockStyle.Fill, FillColor = AppleTheme.Surface, BorderRadius = 10, Margin = new Padding(14, 2, 14, 6), Padding = new Padding(16, 10, 16, 10) };
            var factorsTitle = new Label { Text = "Top risk factors", Dock = DockStyle.Top, Height = 26, Font = AppleTheme.Headline, ForeColor = RiskColor };
            var factorsList = new Label
            {
                Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextPrimary,
                Text = "•  " + string.Join("\r\n•  ", _r.Factors.Take(6)), Padding = new Padding(2, 4, 2, 2)
            };
            factorsPanel.Controls.Add(factorsList);
            factorsPanel.Controls.Add(factorsTitle);

            // ---- field grid ----
            _grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false,
                Margin = new Padding(14, 0, 14, 6)
            };
            _grid.Columns.AddRange(
                new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", FillWeight = 30 },
                new DataGridViewTextBoxColumn { Name = "Fill", HeaderText = "Filled", FillWeight = 18, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "Distinct", HeaderText = "Distinct", FillWeight = 10, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "Sample", HeaderText = "Sample value", FillWeight = 24, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewTextBoxColumn { Name = "Note", HeaderText = "Notes", FillWeight = 18 });
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_grid);
            _grid.CellFormatting += Grid_CellFormatting;

            // ---- buttons: pinned to the TOP of the row so empty space below clears the taskbar ----
            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(18, 14, 18, 0) };
            var stripP = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.Transparent };
            var cont = GunaUi.Button("Continue import", primary: true); cont.Size = new Size(170, 34); cont.DialogResult = DialogResult.OK; cont.Margin = new Padding(8, 5, 0, 5);
            var back = GunaUi.Button("← Back", primary: false); back.Size = new Size(110, 34); back.DialogResult = DialogResult.Retry; back.Margin = new Padding(8, 5, 0, 5);
            var cancel = GunaUi.Button("Cancel import", primary: false); cancel.Size = new Size(150, 34); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(8, 5, 8, 5);
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            right.Controls.Add(cont); right.Controls.Add(cancel); right.Controls.Add(back);
            stripP.Controls.Add(right);
            bottom.Controls.Add(stripP);

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(cards, 0, 1);
            root.Controls.Add(factorsPanel, 0, 2);
            root.Controls.Add(_grid, 0, 3);
            root.Controls.Add(bottom, 0, 4);

            Controls.Add(root);
            AcceptButton = cont;
            CancelButton = cancel;
            FormAnimator.FadeIn(this);
        }

        private static Control Card(string value, string caption, Color valueColor)
        {
            var card = new Guna2Panel { Dock = DockStyle.Fill, FillColor = AppleTheme.Surface, BorderRadius = 10, Margin = new Padding(6, 0, 6, 0) };
            var val = new Label { Text = value, Dock = DockStyle.Fill, Font = new Font(AppleTheme.Title.FontFamily, 22, FontStyle.Bold), ForeColor = valueColor, TextAlign = ContentAlignment.MiddleCenter };
            var cap = new Label { Text = caption, Dock = DockStyle.Bottom, Height = 26, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(val);
            card.Controls.Add(cap);
            return card;
        }

        private void Populate()
        {
            foreach (var f in _r.Fields)
                _grid.Rows.Add(f.Label, $"{f.FillRate:P0}  ({f.Filled}/{f.Total})", f.Distinct,
                    Trunc(f.Sample, 40), f.Note);
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _r.Fields.Count) return;
            var f = _r.Fields[e.RowIndex];
            string col = _grid.Columns[e.ColumnIndex].Name;
            if (col == "Fill")
            {
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.BackColor = f.Required && f.FillRate < 1.0 ? AppleTheme.Danger
                    : f.FillRate >= 0.99 ? AppleTheme.Success
                    : f.FillRate >= 0.5 ? AppleTheme.Warning
                    : Color.FromArgb(120, 90, 30);
            }
            else if (col == "Note" && !string.IsNullOrEmpty(f.Note))
            {
                e.CellStyle.ForeColor = f.Required && f.FillRate < 1.0 ? AppleTheme.Danger : AppleTheme.Warning;
            }
        }

        private static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
