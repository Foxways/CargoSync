using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Enrichment
{
    /// <summary>
    /// Review screen for enrichment: every value fetched from an external source (Postal API / AI)
    /// to fill an empty field, with an Apply checkbox per suggestion. Nothing is filled unless the
    /// operator confirms; cancelling aborts the import.
    /// </summary>
    public class EnrichmentReviewForm : Form
    {
        private readonly List<EnrichmentSuggestion> _items;
        private Guna2DataGridView _grid = null!;

        public EnrichmentReviewForm(List<EnrichmentSuggestion> items)
        {
            _items = items;
            BuildUi();
            Populate();
        }

        private void BuildUi()
        {
            Text = "Review enrichment";
            ClientSize = new Size(940, 580);
            MinimumSize = new Size(760, 460);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            int api = _items.Count(i => i.Source == "Postal API");
            int ai = _items.Count(i => i.Source == "AI");

            var header = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = AppleTheme.Canvas, Padding = new Padding(0, 8, 0, 4) };
            var title = new Label
            {
                Text = $"✚  {_items.Count} field(s) can be enriched",
                Dock = DockStyle.Top, Height = 48, Padding = new Padding(20, 14, 0, 0),
                Font = AppleTheme.Title, ForeColor = AppleTheme.Accent
            };
            var desc = new Label
            {
                Text = $"These EMPTY fields can be filled from external sources ({api} from the Postal API, {ai} from AI). " +
                       "Enrichment never overwrites your data. Untick any you don't want.",
                Dock = DockStyle.Top, Height = 44, Padding = new Padding(22, 0, 16, 0),
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
                new DataGridViewTextBoxColumn { Name = "Field", HeaderText = "Empty field", ReadOnly = true, FillWeight = 26 },
                new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Suggested value", ReadOnly = true, FillWeight = 22 },
                new DataGridViewTextBoxColumn { Name = "Basis", HeaderText = "Derived from", ReadOnly = true, FillWeight = 25, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewTextBoxColumn { Name = "Src", HeaderText = "Source", ReadOnly = true, FillWeight = 12, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } });
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_grid);
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.RowIndex < _items.Count && _grid.Columns[e.ColumnIndex].Name == "Apply")
                    _items[e.RowIndex].Accept = Convert.ToBoolean(_grid.Rows[e.RowIndex].Cells["Apply"].Value ?? false);
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
            foreach (var s in _items)
                _grid.Rows.Add(s.Accept, s.RowNumber, s.FieldLabel, s.Value, s.Basis, s.Source);
        }

        private void SetAll(bool on)
        {
            for (int i = 0; i < _items.Count; i++) { _items[i].Accept = on; _grid.Rows[i].Cells["Apply"].Value = on; }
            _grid.Invalidate();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _items.Count) return;
            string col = _grid.Columns[e.ColumnIndex].Name;
            if (col == "Src")
            {
                bool ai = _items[e.RowIndex].Source == "AI";
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.BackColor = ai ? AppleTheme.Accent : Color.FromArgb(39, 130, 110);
            }
            else if (col == "Value")
            {
                e.CellStyle.ForeColor = AppleTheme.Success;
            }
        }
    }
}
