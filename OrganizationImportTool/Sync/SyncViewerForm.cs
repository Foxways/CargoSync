using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Sync
{
    /// <summary>
    /// Read-only ledger of what has been synced to CargoWise for a client: sent code → stored code,
    /// primary key, status and when. The operator's record of what's already in CargoWise.
    /// </summary>
    public class SyncViewerForm : Form
    {
        private readonly List<CwSyncEntry> _entries;
        private readonly string _clientName;
        private Guna2DataGridView _grid = null!;

        public SyncViewerForm(string clientName, List<CwSyncEntry> entries)
        {
            _clientName = clientName;
            _entries = entries;
            BuildUi();
            Populate();
        }

        private void BuildUi()
        {
            Text = "CargoWise sync ledger";
            ClientSize = new Size(960, 600);
            MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            int ok = _entries.Count(e => e.IsSuccess);

            var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = AppleTheme.Canvas, Padding = new Padding(0, 6, 0, 4) };
            var title = new Label
            {
                Text = $"CargoWise sync — {(_clientName.Length == 0 ? "all clients" : _clientName)}",
                Dock = DockStyle.Top, Height = 42, Padding = new Padding(20, 12, 0, 0),
                Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary
            };
            var sub = new Label
            {
                Text = _entries.Count == 0
                    ? "Nothing synced yet. After a live import, every organization CargoWise accepts is recorded here."
                    : $"{_entries.Count} record(s)   •   {ok} currently in CargoWise (PRS).   Sent code → the code CargoWise stored (code-gen may rename).",
                Dock = DockStyle.Top, Height = 30, Padding = new Padding(22, 2, 16, 0),
                Font = AppleTheme.Headline, ForeColor = ok > 0 ? AppleTheme.Success : AppleTheme.TextSecondary
            };
            header.Controls.Add(sub);
            header.Controls.Add(title);

            _grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
            };
            _grid.Columns.AddRange(
                new DataGridViewTextBoxColumn { Name = "Sent", HeaderText = "Sent code", FillWeight = 14 },
                new DataGridViewTextBoxColumn { Name = "Stored", HeaderText = "Stored in CargoWise", FillWeight = 16 },
                new DataGridViewTextBoxColumn { Name = "Status", HeaderText = "Status", FillWeight = 10, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "Name", HeaderText = "Entity", FillWeight = 14, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewTextBoxColumn { Name = "Pk", HeaderText = "Primary key", FillWeight = 22, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewTextBoxColumn { Name = "When", HeaderText = "Synced (UTC)", FillWeight = 14 },
                new DataGridViewTextBoxColumn { Name = "By", HeaderText = "By", FillWeight = 10 });
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_grid);
            _grid.CellFormatting += Grid_CellFormatting;

            // ButtonBar keeps the buttons clear of the Windows taskbar when maximized (the old
            // flush 56px footer was cut off at the bottom of the screen).
            var export = GunaUi.Button("Export CSV", primary: false); export.Size = new Size(130, 40); export.Click += Export_Click;
            var close = GunaUi.Button("Close", primary: true); close.Size = new Size(110, 40); close.DialogResult = DialogResult.OK;
            var footer = GunaUi.ButtonBar(new Control[] { close, export });

            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = close;   // Esc closes the sync ledger
            FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            foreach (var e in _entries)
                _grid.Rows.Add(e.SentCode, e.StoredCode, e.Status, e.EntityName, e.EntityPk,
                    e.SyncedUtc.ToString("yyyy-MM-dd HH:mm"), e.Username);
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _entries.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Status") return;
            var en = _entries[e.RowIndex];
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.BackColor = en.IsSuccess ? AppleTheme.Success :
                string.Equals(en.Status, "WRN", StringComparison.OrdinalIgnoreCase) ? AppleTheme.Warning : AppleTheme.Danger;
        }

        private void Export_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "CSV file|*.csv", FileName = "cargowise-sync.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SentCode,StoredCode,Status,Entity,PrimaryKey,SyncedUtc,By");
                foreach (var en in _entries)
                    sb.AppendLine($"\"{en.SentCode}\",\"{en.StoredCode}\",{en.Status},\"{en.EntityName}\",\"{en.EntityPk}\",{en.SyncedUtc:o},\"{en.Username}\"");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show(this, "Exported.", "Sync ledger", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not export: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
