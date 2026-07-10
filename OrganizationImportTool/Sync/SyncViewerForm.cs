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

        // Filtering: the grid always shows _view (a filtered projection of _entries).
        private List<CwSyncEntry> _view;
        private Guna2TextBox _search = null!;
        private Guna2DateTimePicker _from = null!, _to = null!;
        private Label _countLbl = null!;

        public SyncViewerForm(string clientName, List<CwSyncEntry> entries)
        {
            _clientName = clientName;
            _entries = entries;
            _view = entries;
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

            var header = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(98), BackColor = AppleTheme.Canvas, Padding = new Padding(0, 6, 0, 4) };
            var title = new Label
            {
                Text = $"CargoWise sync — {(_clientName.Length == 0 ? "all clients" : _clientName)}",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(48), Padding = new Padding(20, 10, 0, 0),
                Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary
            };
            var sub = new Label
            {
                Text = _entries.Count == 0
                    ? "Nothing synced yet. After a live import, every organization CargoWise accepts is recorded here."
                    : $"{_entries.Count} record(s)   •   {ok} currently in CargoWise (PRS).   Sent code → the code CargoWise stored (code-gen may rename).",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(32), Padding = new Padding(22, 2, 16, 0),
                Font = AppleTheme.Headline, ForeColor = ok > 0 ? AppleTheme.Success : AppleTheme.TextSecondary
            };
            header.Controls.Add(sub);
            header.Controls.Add(title);

            // ---- filter bar: search by code/name + optional date range ----
            var filterBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(54), BackColor = AppleTheme.Canvas,
                Padding = new Padding(20, 6, 16, 6), ColumnCount = 6
            };
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // search
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // "From"
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(130)));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // "To"
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(130)));
            filterBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // count

            _search = GunaUi.TextBox("Search organization name or code…");
            _search.Dock = DockStyle.Fill; _search.Margin = new Padding(0, 2, 12, 2);
            _search.TextChanged += (s, e) => ApplyFilter();

            Label DateLbl(string t) => new()
            {
                Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 10, 6, 0),
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };
            Guna2DateTimePicker Picker()
            {
                // Unchecked = no bound; ticking the box activates that side of the range.
                var p = GunaUi.DatePicker();
                p.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                p.Margin = new Padding(0, 8, 10, 0);
                p.ValueChanged += (s, e) => ApplyFilter();
                p.CheckedChanged += (s, e) => ApplyFilter();   // react the instant a bound is toggled
                return p;
            }
            _from = Picker();
            _to = Picker();
            _countLbl = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 10, 0, 0), Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };

            filterBar.Controls.Add(_search, 0, 0);
            filterBar.Controls.Add(DateLbl("From"), 1, 0);
            filterBar.Controls.Add(_from, 2, 0);
            filterBar.Controls.Add(DateLbl("to"), 3, 0);
            filterBar.Controls.Add(_to, 4, 0);
            filterBar.Controls.Add(_countLbl, 5, 0);

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
            AppleTheme.DarkScrollbars(_grid);
            _grid.CellFormatting += Grid_CellFormatting;

            // ButtonBar keeps the buttons clear of the Windows taskbar when maximized (the old
            // flush 56px footer was cut off at the bottom of the screen).
            var export = GunaUi.Button("Export CSV", primary: false); export.Size = new Size(TextRenderer.MeasureText(export.Text, export.Font).Width + 60, 34); export.Click += Export_Click;
            var close = GunaUi.Button("Close", primary: true); close.Size = new Size(TextRenderer.MeasureText(close.Text, close.Font).Width + 60, 34); close.DialogResult = DialogResult.OK;
            var footer = GunaUi.ButtonBar(new Control[] { close, export });

            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(filterBar);
            Controls.Add(header);
            CancelButton = close;   // Esc closes the sync ledger
            FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            _grid.Rows.Clear();
            foreach (var e in _view)
                _grid.Rows.Add(e.SentCode, e.StoredCode, e.Status, e.EntityName, e.EntityPk,
                    e.SyncedUtc.ToString("yyyy-MM-dd HH:mm"), e.Username);
        }

        private void ApplyFilter()
        {
            string q = _search.Text.Trim();
            DateTime? from = _from.Checked ? _from.Value.Date : null;
            DateTime? to = _to.Checked ? _to.Value.Date.AddDays(1) : null; // inclusive end day

            bool Matches(CwSyncEntry en) =>
                (q.Length == 0
                    || (en.EntityName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (en.SentCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)
                    || (en.StoredCode?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                && (from == null || en.SyncedUtc >= from)
                && (to == null || en.SyncedUtc < to);

            _view = _entries.Where(Matches).ToList();
            _countLbl.Text = _view.Count == _entries.Count ? string.Empty : $"{_view.Count} of {_entries.Count}";
            Populate();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _view.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Status") return;
            var en = _view[e.RowIndex];
            e.CellStyle.ForeColor = Color.White;
            e.CellStyle.BackColor = en.IsSuccess ? AppleTheme.Success :
                string.Equals(en.Status, "WRN", StringComparison.OrdinalIgnoreCase) ? AppleTheme.Warning : AppleTheme.Danger;
        }

        // Wraps a value in double-quotes and escapes any embedded double-quotes per RFC 4180.
        private static string Csv(string? v) => "\"" + (v ?? "").Replace("\"", "\"\"") + "\"";

        private void Export_Click(object? sender, EventArgs e)
        {
            using var dlg = new SaveFileDialog { Filter = "CSV file|*.csv", FileName = "cargowise-sync.csv" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                // Export what the operator is looking at - the filtered view.
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("SentCode,StoredCode,Status,Entity,PrimaryKey,SyncedUtc,By");
                foreach (var en in _view)
                    sb.AppendLine($"{Csv(en.SentCode)},{Csv(en.StoredCode)},{Csv(en.Status)},{Csv(en.EntityName)},{Csv(en.EntityPk)},{en.SyncedUtc:o},{Csv(en.Username)}");
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
