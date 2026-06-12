using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Transform
{
    /// <summary>
    /// Review screen for the data-cleaning pass: every proposed cell fix (deterministic or AI),
    /// with an Apply checkbox per change. The operator keeps full control - nothing is altered
    /// unless they confirm. Cancelling aborts the import.
    /// </summary>
    public class DataCleaningForm : Form
    {
        private readonly List<CleaningChange> _changes;
        private Guna2DataGridView _grid = null!;

        public DataCleaningForm(List<CleaningChange> changes)
        {
            _changes = changes;
            // Conservative default: nothing is applied unless the operator opts in. The client's data
            // is sent AS-IS unless they explicitly tick a fix they want.
            foreach (var c in _changes) c.Accept = false;
            BuildUi();
            Populate();
        }

        private void BuildUi()
        {
            Text = "Review data cleaning";
            ClientSize = new Size(960, 600);
            MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            int ai = _changes.Count(c => c.Source == CleanSource.Ai);

            var header = new Panel { Dock = DockStyle.Top, Height = LogicalToDeviceUnits(106), BackColor = AppleTheme.Canvas, Padding = new Padding(0, 8, 0, 4) };
            var title = new Label
            {
                Text = $"🧹  {_changes.Count} suggested fix(es) before import",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(52), Padding = new Padding(20, 12, 0, 0),
                Font = AppleTheme.Title, ForeColor = AppleTheme.Accent
            };
            var desc = new Label
            {
                Text = $"Your data is sent AS-IS by default — tick only the fixes you want applied ({ai} are AI suggestions). " +
                       "Nothing here changes your original file.",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(46), Padding = new Padding(22, 0, 16, 0),
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };
            header.Controls.Add(desc);
            header.Controls.Add(title);

            _grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.Columns.AddRange(
                new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "Apply", FillWeight = 8 },
                new DataGridViewTextBoxColumn { Name = "Row", HeaderText = "Row", ReadOnly = true, FillWeight = 7, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Field", ReadOnly = true, FillWeight = 24 },
                new DataGridViewTextBoxColumn { Name = "From", HeaderText = "Original", ReadOnly = true, FillWeight = 20, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewTextBoxColumn { Name = "To", HeaderText = "Cleaned", ReadOnly = true, FillWeight = 18 },
                new DataGridViewTextBoxColumn { Name = "Why", HeaderText = "Why", ReadOnly = true, FillWeight = 19 },
                new DataGridViewTextBoxColumn { Name = "By", HeaderText = "By", ReadOnly = true, FillWeight = 8, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_grid);
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _changes.Count && _grid.Columns[e.ColumnIndex].Name == "Apply")
                    _changes[e.RowIndex].Accept = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Apply"].Value ?? false);
            };
            _grid.DataError += (s, e) => { e.ThrowException = false; };

            var none = GunaUi.Button("Untick all", primary: false); none.Size = new Size(120, 40); none.Click += (s, e) => SetAll(false);
            var all = GunaUi.Button("Tick all", primary: false); all.Size = new Size(110, 40); all.Margin = new Padding(8, 0, 0, 0); all.Click += (s, e) => SetAll(true);
            var apply = GunaUi.Button("Continue", primary: true); apply.Size = new Size(150, 40); apply.DialogResult = DialogResult.OK;
            var cancel = GunaUi.Button("Cancel import", primary: false); cancel.Size = new Size(150, 40); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(10, 0, 0, 0);
            var bottom = GunaUi.ButtonBar(new Control[] { apply, cancel }, new Control[] { none, all });

            Controls.Add(_grid);
            Controls.Add(bottom);
            Controls.Add(header);
            AcceptButton = apply;
            CancelButton = cancel;
            FormAnimator.FadeIn(this);
        }

        private void Populate()
        {
            foreach (var c in _changes)
                _grid.Rows.Add(c.Accept, c.RowNumber, c.FieldLabel, Trunc(c.Original, 40), Trunc(c.Cleaned, 40), c.Reason,
                    c.Source == CleanSource.Ai ? "AI" : "auto");
        }

        private void SetAll(bool on)
        {
            for (int i = 0; i < _changes.Count; i++)
            {
                _changes[i].Accept = on;
                _grid.Rows[i].Cells["Apply"].Value = on;
            }
            _grid.Invalidate();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _changes.Count) return;
            string col = _grid.Columns[e.ColumnIndex].Name;
            if (col == "By")
            {
                bool ai = _changes[e.RowIndex].Source == CleanSource.Ai;
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.BackColor = ai ? AppleTheme.Accent : Color.FromArgb(90, 90, 98);
            }
            else if (col == "To")
            {
                e.CellStyle.ForeColor = AppleTheme.Success;
            }
        }

        private static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? "(empty)" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
