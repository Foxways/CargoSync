using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Eadaptor
{
    /// <summary>
    /// Professional, Apple-styled result viewer for a batch send: per-row outcome grid with a
    /// colour-coded status, a summary header, full request/response detail for the selected row,
    /// and a "Save Report" export.
    /// </summary>
    public class ResponsePreviewForm : Form
    {
        private readonly List<OrgSendOutcome> _outcomes;
        private readonly IReadOnlyList<string>? _sourceHeaders;
        private List<OrgSendOutcome> _view;
        private Guna.UI2.WinForms.Guna2DataGridView _grid = null!;
        private RichTextBox _detail = null!;
        private Label _summary = null!;
        private Guna.UI2.WinForms.Guna2CheckBox _attentionOnly = null!;

        public ResponsePreviewForm(List<OrgSendOutcome> outcomes, IReadOnlyList<string>? sourceHeaders = null)
        {
            _outcomes = outcomes;
            _sourceHeaders = sourceHeaders;
            _view = outcomes;
            BuildUi();
            Populate();
        }

        /// <summary>Rows the operator should look at: blocked, rejected, skipped or warned.</summary>
        private static bool NeedsAttention(OrgSendOutcome o) =>
            o.Response.NotSent || o.Response.IsWarning || (!o.Response.IsSuccess && !o.Response.IsSimulatedOk);

        private void BuildUi()
        {
            Text = "Import Results";
            ClientSize = new Size(980, 680);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            bool dryRun = _outcomes.Any(o => o.Response.Simulated);

            var header = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(126), BackColor = AppleTheme.Canvas, Padding = new Padding(0, 6, 0, 6) };
            var title = new Label
            {
                Text = dryRun ? "Import Preview  (Dry Run — nothing was sent)" : "Import Results",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(50),
                Padding = new Padding(20, 10, 0, 0),
                Font = AppleTheme.Title, ForeColor = dryRun ? AppleTheme.Accent : AppleTheme.TextPrimary
            };

            int dup = _outcomes.Count(o => o.Response.IsDuplicate);
            string summaryText; Color summaryColor;
            if (dryRun)
            {
                int wouldSend = _outcomes.Count(o => o.Response.IsSimulatedOk);
                int blocked = _outcomes.Count - wouldSend - dup;
                var parts = new List<string> { $"➜ {wouldSend} would be sent" };
                if (dup > 0) parts.Add($"⏭ {dup} duplicate(s) skipped");
                if (blocked > 0) parts.Add($"⛔ {blocked} blocked (validation)");
                parts.Add($"of {_outcomes.Count} total");
                summaryText = string.Join("     ", parts);
                summaryColor = blocked > 0 ? AppleTheme.Warning : AppleTheme.Accent;
            }
            else
            {
                int ok = _outcomes.Count(o => o.Response.IsSuccess);
                int warn = _outcomes.Count(o => o.Response.IsWarning);
                int fail = _outcomes.Count - ok - warn - dup;
                var parts = new List<string> { $"✓ {ok} succeeded" };
                if (warn > 0) parts.Add($"⚠ {warn} warnings");
                if (dup > 0) parts.Add($"⏭ {dup} duplicate(s) skipped");
                parts.Add($"✗ {fail} failed");
                parts.Add($"of {_outcomes.Count} total");
                summaryText = string.Join("     ", parts);
                summaryColor = fail > 0 ? AppleTheme.Danger : AppleTheme.Success;
            }
            _summary = new Label
            {
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(34),
                Padding = new Padding(22, 2, 0, 0),
                Font = AppleTheme.Headline,
                Text = summaryText,
                ForeColor = summaryColor
            };
            // CargoWise speak, translated - so PRS/WRN/ERR never need explaining in person.
            var legend = new Label
            {
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(22),
                Padding = new Padding(22, 2, 0, 0),
                Font = AppleTheme.Caption, ForeColor = AppleTheme.TextSecondary,
                Text = dryRun
                    ? "SIM = would send (dry run)   ·   DUP = skipped duplicate   ·   ERR = would be rejected/blocked"
                    : "PRS = Processed (accepted)   ·   WRN = stored with warnings   ·   ERR = rejected   ·   DUP = skipped duplicate"
            };
            header.Controls.Add(legend);
            header.Controls.Add(_summary);
            header.Controls.Add(title);

            var split = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
                SplitterDistance = 340, BackColor = AppleTheme.Canvas
            };

            _grid = new Guna.UI2.WinForms.Guna2DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
            };
            _grid.Columns.Add("Row", "Row");
            _grid.Columns.Add("Code", "Sent Code");
            _grid.Columns.Add("Status", "Status");
            _grid.Columns.Add("Outcome", "Outcome");
            _grid.Columns.Add("Stored", "Stored Code");
            _grid.Columns.Add("Msg", "Message #");
            _grid.Columns.Add("Detail", "Detail");
            _grid.Columns["Row"]!.FillWeight = 8;
            _grid.Columns["Detail"]!.FillWeight = 34;
            AppleTheme.StyleGrid(_grid);
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.SelectionChanged += (s, e) => ShowDetail();

            _detail = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, ScrollBars = RichTextBoxScrollBars.Both,
                WordWrap = false, BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9)
            };

            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_detail);
            AppleTheme.DarkScrollbars(_grid);     // dark scrollbars instead of the default white
            AppleTheme.DarkScrollbars(_detail);

            var save = GunaUi.Button("Save Report", primary: false); save.Size = new Size(TextRenderer.MeasureText(save.Text, save.Font).Width + 60, 34); save.Margin = new Padding(10, 0, 0, 0); save.Click += SaveReport_Click;
            var close = GunaUi.Button("Close", primary: true); close.Size = new Size(TextRenderer.MeasureText(close.Text, close.Font).Width + 60, 34); close.DialogResult = DialogResult.OK;

            // Re-export the rows that need fixing in their ORIGINAL columns + a Reason column,
            // so the operator can correct just those rows and re-import the file directly.
            var attention = _outcomes.Where(NeedsAttention).ToList();
            var exportAttention = GunaUi.Button($"Export rows needing attention ({attention.Count})", primary: false);
            exportAttention.Size = new Size(TextRenderer.MeasureText(exportAttention.Text, exportAttention.Font).Width + 60, 34);
            exportAttention.Click += ExportAttention_Click;
            exportAttention.Visible = _sourceHeaders is { Count: > 0 } && attention.Count > 0;

            _attentionOnly = GunaUi.Check("Only rows needing attention");
            _attentionOnly.Margin = new Padding(10, 10, 0, 0);
            _attentionOnly.Visible = attention.Count > 0 && attention.Count < _outcomes.Count;
            _attentionOnly.CheckedChanged += (s, e) =>
            {
                _view = _attentionOnly.Checked ? _outcomes.Where(NeedsAttention).ToList() : _outcomes;
                Populate();
            };

            var footer = GunaUi.ButtonBar(new Control[] { close, save, exportAttention }, new Control[] { _attentionOnly });

            Controls.Add(split);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = close;   // Esc closes the results viewer
            Ui.FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            _grid.Rows.Clear();
            foreach (var o in _view)
            {
                var r = o.Response;
                string detail =
                    r.IsWarning ? "Stored with warning: " + FirstLine(r.ProcessingLog) :
                    r.IsSuccess ? "OK" : (r.Error ?? r.ProcessingLog);
                _grid.Rows.Add(o.RowNumber, o.SentCode, r.Status, r.Outcome, r.LocalCode, r.MessageNumber, detail);
            }
            if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            ShowDetail();
        }

        private static string FirstLine(string text)
        {
            int i = (text ?? string.Empty).IndexOf('\n');
            return i < 0 ? (text ?? string.Empty) : text!.Substring(0, i).TrimEnd('\r');
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Status") return;
            var r = _view[e.RowIndex].Response;
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            e.CellStyle.BackColor =
                r.IsAlreadyImported ? AppleTheme.Warning :
                r.IsDuplicate ? AppleTheme.Warning :
                r.IsSimulatedOk ? AppleTheme.Accent :
                r.IsSuccess ? AppleTheme.Success :
                r.IsWarning ? AppleTheme.Warning : AppleTheme.Danger;
        }

        private void ShowDetail()
        {
            if (_grid.SelectedRows.Count == 0) { _detail.Clear(); return; }
            int i = _grid.SelectedRows[0].Index;
            if (i < 0 || i >= _view.Count) return;
            var o = _view[i];
            var sb = new StringBuilder();
            sb.AppendLine($"Row {o.RowNumber}   Sent Code: {o.SentCode}");
            sb.AppendLine($"Status: {o.Response.Status}   Outcome: {o.Response.Outcome}");
            sb.AppendLine($"Stored Code: {o.Response.LocalCode}   PK: {o.Response.EntityPk}");
            sb.AppendLine($"Message #: {o.Response.MessageNumber}   HTTP: {o.Response.HttpStatus}");
            sb.AppendLine();
            sb.AppendLine("── Processing Log ─────────────────────────────");
            sb.AppendLine(o.Response.ProcessingLog);
            if (!string.IsNullOrEmpty(o.Response.Error))
            {
                sb.AppendLine();
                sb.AppendLine("── Error ──────────────────────────────────────");
                sb.AppendLine(o.Response.Error);
            }
            sb.AppendLine();
            sb.AppendLine("── Sent XML ───────────────────────────────────");
            sb.AppendLine(o.SentXml);
            _detail.Text = sb.ToString();
            _detail.SelectionStart = 0;
        }

        /// <summary>
        /// Write the attention rows (blocked/rejected/skipped/warned) as a CSV with the ORIGINAL
        /// source columns plus Status + Reason. Because the headers are the original ones, the
        /// exported file re-imports directly - the learned mapping picks it up automatically.
        /// </summary>
        private void ExportAttention_Click(object? sender, EventArgs e)
        {
            if (_sourceHeaders == null || _sourceHeaders.Count == 0) return;
            using var dlg = new SaveFileDialog { Filter = "CSV file|*.csv", FileName = "rows-needing-attention.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                static string Csv(string? v)
                {
                    v ??= string.Empty;
                    return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
                        ? "\"" + v.Replace("\"", "\"\"") + "\""
                        : v;
                }

                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", _sourceHeaders.Select(Csv).Concat(new[] { "Status", "Reason" })));
                int exported = 0;
                foreach (var o in _outcomes.Where(NeedsAttention))
                {
                    if (o.SourceRow == null) continue;
                    var cells = _sourceHeaders.Select(h => Csv(o.SourceRow[h]))
                        .Concat(new[] { Csv(o.Response.Status), Csv(o.Response.Error ?? FirstLine(o.Response.ProcessingLog)) });
                    sb.AppendLine(string.Join(",", cells));
                    exported++;
                }
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show(this,
                    $"{exported} row(s) exported.\n\nFix the values in that file and import it again — " +
                    "the columns are unchanged, so the mapping is remembered automatically.",
                    "Exported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveReport_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "CSV file|*.csv|Text file|*.txt", FileName = "import-results.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Row,SentCode,Status,Outcome,StoredCode,MessageNumber,Detail");
                foreach (var o in _outcomes)
                {
                    string detail = (o.Response.IsSuccess ? "OK" : (o.Response.Error ?? "")).Replace("\"", "'").Replace("\n", " ");
                    sb.AppendLine($"{o.RowNumber},\"{o.SentCode}\",{o.Response.Status},\"{o.Response.Outcome}\",\"{o.Response.LocalCode}\",{o.Response.MessageNumber},\"{detail}\"");
                }
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show(this, "Report saved.", "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save report: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
