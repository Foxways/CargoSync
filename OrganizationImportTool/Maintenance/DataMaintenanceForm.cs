using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Maintenance
{
    /// <summary>
    /// Data &amp; Maintenance screen: shows every category of accumulated data with its current
    /// footprint and lets the operator tick the ones to clear. Clearing is gated behind a
    /// confirmation dialog (stronger wording when a destructive category is included). Accounts,
    /// AI keys and preferences are never touched.
    /// </summary>
    public sealed class DataMaintenanceForm : Form
    {
        private readonly List<(CleanupCategory cat, Guna2CheckBox chk, Label stat)> _rows = new();
        private Label _footer = null!;

        public DataMaintenanceForm()
        {
            Text = "CargoSync — Data & Maintenance";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(660, 620);
            MinimumSize = new Size(560, 480);
            BackColor = AppleTheme.Canvas;
            ForeColor = AppleTheme.TextPrimary;
            Font = AppleTheme.Body;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = AppleTheme.Canvas, Padding = new Padding(18, 14, 18, 12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));    // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // category list
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));    // footer + buttons

            // ── header ──
            var head = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var title = new Label
            {
                Text = "Data & Maintenance", Dock = DockStyle.Top, Height = 30, UseMnemonic = false,
                Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary
            };
            var sub = new Label
            {
                Text = "Free up space and clear stored history. Your user accounts, AI keys and settings are never touched.",
                Dock = DockStyle.Top, Height = 30, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };
            head.Controls.Add(sub);
            head.Controls.Add(title);

            // ── category list (scrolls when it overflows) ──
            var list = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true, BackColor = AppleTheme.Canvas
            };
            list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int rowIdx = 0;
            foreach (var cat in DataMaintenance.Categories())
            {
                list.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
                list.Controls.Add(BuildRow(cat), 0, rowIdx++);
            }
            list.RowCount = rowIdx;

            // ── footer + buttons ──
            var footerBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            _footer = new Label
            {
                Dock = DockStyle.Left, AutoSize = false, Width = 280, TextAlign = ContentAlignment.MiddleLeft,
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };

            var clearBtn = GunaUi.Button("Clear Selected", primary: true);
            clearBtn.Size = new Size(TextRenderer.MeasureText(clearBtn.Text, clearBtn.Font).Width + 60, 36);
            clearBtn.Click += (s, e) => ClearSelected();
            var closeBtn = GunaUi.Button("Close", primary: false);
            closeBtn.Size = new Size(TextRenderer.MeasureText(closeBtn.Text, closeBtn.Font).Width + 60, 36);
            closeBtn.Click += (s, e) => Close();

            var btnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
                AutoSize = true, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0)
            };
            // Both top margins 0 so the buttons share one baseline; the 12px goes on the RIGHT button's
            // LEFT side, which is what creates the gap *between* them in a right-to-left flow.
            clearBtn.Margin = new Padding(12, 0, 0, 0);
            closeBtn.Margin = new Padding(0, 0, 0, 0);
            btnFlow.Controls.Add(clearBtn);   // RTL → rightmost
            btnFlow.Controls.Add(closeBtn);
            footerBar.Controls.Add(btnFlow);
            footerBar.Controls.Add(_footer);

            root.Controls.Add(head, 0, 0);
            root.Controls.Add(list, 0, 1);
            root.Controls.Add(footerBar, 0, 2);
            Controls.Add(root);

            RefreshStats();
            FormAnimator.FadeIn(this);
        }

        private Control BuildRow(CleanupCategory cat)
        {
            var card = new Guna2Panel
            {
                Dock = DockStyle.Fill, FillColor = AppleTheme.Surface, BorderRadius = 8,
                Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12, 8, 12, 8)
            };

            // Top band: checkbox (left) + footprint (right); description fills the rest. Docking is
            // used throughout so the layout is deterministic regardless of when the card first sizes.
            var topBand = new Panel { Dock = DockStyle.Top, Height = 28, BackColor = Color.Transparent };

            // Double the ampersand so titles like "AI usage & import activity" don't lose the "&"
            // to mnemonic (underline-accelerator) processing.
            var chk = GunaUi.Check(cat.Title.Replace("&", "&&"));
            chk.Font = AppleTheme.Font(10f, FontStyle.Bold);
            chk.Dock = DockStyle.Left;
            if (cat.Destructive) chk.CheckedState.FillColor = AppleTheme.Danger;

            var stat = new Label
            {
                Dock = DockStyle.Right, Width = 180, AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };
            topBand.Controls.Add(chk);
            topBand.Controls.Add(stat);

            var desc = new Label
            {
                Text = cat.Description, Dock = DockStyle.Fill, AutoSize = false,
                Padding = new Padding(2, 2, 2, 0), Font = AppleTheme.Caption,
                ForeColor = cat.Destructive ? Color.FromArgb(230, 150, 120) : AppleTheme.TextSecondary
            };

            card.Controls.Add(desc);       // Fill added first so the docked top band sits above it
            card.Controls.Add(topBand);
            _rows.Add((cat, chk, stat));
            return card;
        }

        /// <summary>Re-measure every category and update the per-row + total footprint labels.</summary>
        private void RefreshStats()
        {
            long totalBytes = 0;
            foreach (var (cat, _, stat) in _rows)
            {
                var (count, bytes) = cat.Measure();
                totalBytes += bytes;
                string size = bytes > 0 ? $"  ·  {DataMaintenance.Human(bytes)}" : string.Empty;
                stat.Text = count == 0 && bytes == 0 ? "empty" : $"{count:N0} item(s){size}";
                stat.ForeColor = (count == 0 && bytes == 0) ? AppleTheme.TextSecondary : AppleTheme.TextPrimary;
            }
            _footer.Text = totalBytes > 0 ? $"Reclaimable on disk: {DataMaintenance.Human(totalBytes)}" : string.Empty;
        }

        private void ClearSelected()
        {
            var selected = _rows.Where(r => r.chk.Checked).Select(r => r.cat).ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show(this, "Tick at least one category to clear.", "Data & Maintenance",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            bool destructive = selected.Any(c => c.Destructive);
            string body = "The following will be permanently cleared:\n\n  • " +
                          string.Join("\n  • ", selected.Select(c => c.Title)) +
                          "\n\nThis cannot be undone." +
                          (destructive ? "\n\n⚠ This includes hand-made data (templates and/or stored history)." : string.Empty);

            if (MessageBox.Show(this, body, "Confirm clear",
                    MessageBoxButtons.YesNo, destructive ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            int failed = 0;
            UseWaitCursor = true;
            try
            {
                foreach (var cat in selected)
                {
                    try { cat.Clear(); }
                    catch (Exception ex) { failed++; Logging.AppLog.Warn($"Clearing '{cat.Key}' failed", ex); }
                }
            }
            finally { UseWaitCursor = false; }

            foreach (var (_, chk, _) in _rows) chk.Checked = false;
            RefreshStats();

            MessageBox.Show(this,
                failed == 0 ? "Selected data cleared." : $"Cleared with {failed} item(s) that could not be removed (see logs).",
                "Data & Maintenance", MessageBoxButtons.OK,
                failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
