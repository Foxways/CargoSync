using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Dedup
{
    /// <summary>
    /// Shows the operator the likely-duplicate organizations found in their file before import,
    /// and lets them choose to skip the extras (keeping the first row of each group) or import
    /// everything anyway. Cancelling aborts the whole import.
    /// </summary>
    public class DuplicateReviewForm : Form
    {
        private readonly List<DuplicateGroup> _groups;
        private Guna2DataGridView _grid = null!;
        private Guna2CheckBox _skip = null!;

        /// <summary>True if the operator chose to skip duplicate rows.</summary>
        public bool SkipDuplicates => _skip.Checked;

        /// <summary>Row numbers to skip when <see cref="SkipDuplicates"/> is on (the 2nd+ of each group).</summary>
        public HashSet<int> RowsToSkip =>
            _skip.Checked
                ? _groups.SelectMany(g => g.Extras).Select(r => r.RowNumber).ToHashSet()
                : new HashSet<int>();

        public DuplicateReviewForm(List<DuplicateGroup> groups)
        {
            _groups = groups;
            BuildUi();
            Populate();
        }

        private void BuildUi()
        {
            Text = "Possible duplicate organizations";
            ClientSize = new Size(820, 520);
            MinimumSize = new Size(700, 440);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            AppleTheme.ApplyWindow(this);

            int extras = _groups.Sum(g => g.Extras.Count());

            var header = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = AppleTheme.Canvas, Padding = new Padding(0, 8, 0, 4) };
            var title = new Label
            {
                Text = $"⚠  {_groups.Count} possible duplicate group(s) found",
                Dock = DockStyle.Top, Height = 48, Padding = new Padding(20, 14, 0, 0),
                Font = AppleTheme.Title, ForeColor = AppleTheme.Warning
            };
            var desc = new Label
            {
                Text = "These rows look like the same organization. Importing all of them can create or overwrite the same CargoWise org twice.",
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(22, 0, 16, 0),
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };
            header.Controls.Add(desc);
            header.Controls.Add(title);

            _grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false
            };
            _grid.Columns.Add("Rows", "Rows");
            _grid.Columns.Add("Names", "Organizations");
            _grid.Columns.Add("Why", "Why flagged");
            _grid.Columns.Add("Conf", "Confidence");
            _grid.Columns["Rows"]!.FillWeight = 12;
            _grid.Columns["Names"]!.FillWeight = 42;
            _grid.Columns["Why"]!.FillWeight = 32;
            _grid.Columns["Conf"]!.FillWeight = 14;
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_grid);

            _skip = GunaUi.Check($"Skip the duplicates — keep the first row of each group, skip the other {extras} row(s)");
            _skip.Checked = true;
            _skip.Dock = DockStyle.Fill; _skip.AutoSize = false;
            var skipPanel = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = AppleTheme.Canvas, Padding = new Padding(20, 4, 18, 4) };
            skipPanel.Controls.Add(_skip);

            var cont = GunaUi.Button("Continue", primary: true); cont.Size = new Size(150, 40); cont.DialogResult = DialogResult.OK;
            var cancel = GunaUi.Button("Cancel import", primary: false); cancel.Size = new Size(150, 40); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(10, 0, 0, 0);
            var bottom = GunaUi.ButtonBar(new Control[] { cont, cancel });

            Controls.Add(_grid);
            Controls.Add(skipPanel);
            Controls.Add(bottom);
            Controls.Add(header);
            AcceptButton = cont;
            CancelButton = cancel;
            FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            foreach (var g in _groups)
                _grid.Rows.Add(g.RowList, g.Names, g.Reason, $"{g.Confidence:P0}");
            if (_grid.Rows.Count > 0) _grid.ClearSelection();
        }
    }
}
