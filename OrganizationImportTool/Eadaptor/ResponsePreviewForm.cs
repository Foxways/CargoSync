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
        private Guna.UI2.WinForms.Guna2DataGridView _grid = null!;
        private TextBox _detail = null!;
        private Label _summary = null!;

        public ResponsePreviewForm(List<OrgSendOutcome> outcomes)
        {
            _outcomes = outcomes;
            BuildUi();
            Populate();
        }

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

            var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = AppleTheme.Canvas, Padding = new Padding(0, 6, 0, 6) };
            var title = new Label
            {
                Text = dryRun ? "Import Preview  (Dry Run — nothing was sent)" : "Import Results",
                Dock = DockStyle.Top, Height = 44,
                Padding = new Padding(20, 12, 0, 0),
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
                Dock = DockStyle.Top, Height = 34,
                Padding = new Padding(22, 2, 0, 0),
                Font = AppleTheme.Headline,
                Text = summaryText,
                ForeColor = summaryColor
            };
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

            _detail = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary,
                BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9)
            };

            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(_detail);
            AppleTheme.DarkScrollbars(_grid);     // dark scrollbars instead of the default white
            AppleTheme.DarkScrollbars(_detail);

            var save = GunaUi.Button("Save Report", primary: false); save.Size = new Size(140, 40); save.Margin = new Padding(10, 0, 0, 0); save.Click += SaveReport_Click;
            var close = GunaUi.Button("Close", primary: true); close.Size = new Size(120, 40); close.DialogResult = DialogResult.OK;
            var footer = GunaUi.ButtonBar(new Control[] { close, save });

            Controls.Add(split);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = close;   // Esc closes the results viewer
            Ui.FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            foreach (var o in _outcomes)
            {
                var r = o.Response;
                _grid.Rows.Add(o.RowNumber, o.SentCode, r.Status,
                    r.Outcome, r.LocalCode, r.MessageNumber,
                    r.IsSuccess ? "OK" : (r.Error ?? r.ProcessingLog));
            }
            if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
            ShowDetail();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _outcomes.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Status") return;
            var r = _outcomes[e.RowIndex].Response;
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            e.CellStyle.BackColor =
                r.IsDuplicate ? AppleTheme.Warning :
                r.IsSimulatedOk ? AppleTheme.Accent :
                r.IsSuccess ? AppleTheme.Success :
                r.IsWarning ? AppleTheme.Warning : AppleTheme.Danger;
        }

        private void ShowDetail()
        {
            if (_grid.SelectedRows.Count == 0) { _detail.Clear(); return; }
            int i = _grid.SelectedRows[0].Index;
            if (i < 0 || i >= _outcomes.Count) return;
            var o = _outcomes[i];
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
