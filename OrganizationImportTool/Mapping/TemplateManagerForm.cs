using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// Manage saved mapping templates: create from scratch (free-text column headers → full mapping
    /// editor), edit existing, rename, or delete. Reachable from the Scheduled Imports window.
    /// </summary>
    public sealed class TemplateManagerForm : Form
    {
        private readonly TemplateStore _store;
        private readonly Guna2DataGridView _grid = new();

        public TemplateManagerForm(TemplateStore? store = null)
        {
            _store = store ?? new TemplateStore();

            Text = "CargoSync — Mapping Templates";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(760, 520);
            MinimumSize = new Size(640, 400);
            BackColor = AppleTheme.Canvas;
            ForeColor = AppleTheme.TextPrimary;
            Font = AppleTheme.Body;
            AppleTheme.ApplyWindow(this);

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name",    HeaderText = "Template",     FillWeight = 38 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Scope",   HeaderText = "Scope",        FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind",    HeaderText = "Kind",         FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Entries", HeaderText = "Fields",       FillWeight = 10 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Saved",   HeaderText = "Saved (UTC)",  FillWeight = 18 });
            AppleTheme.StyleGrid(_grid);
            _grid.DoubleClick += (s, e) => EditTemplate();

            var create = GunaUi.Button("New",    primary: true);  create.Click += (s, e) => CreateTemplate();
            var edit   = GunaUi.Button("Edit",   primary: false); edit.Click   += (s, e) => EditTemplate();
            var rename = GunaUi.Button("Rename", primary: false); rename.Click += (s, e) => Rename();
            var delete = GunaUi.Button("Delete", primary: false); delete.Click += (s, e) => DeleteSelected();
            var close  = GunaUi.Button("Close",  primary: false); close.Click  += (s, e) => Close();

            // Auto-size every button width before ButtonBar so text never clips.
            // ButtonBar overrides Margin only — Size is preserved.
            foreach (var b in new[] { create, edit, rename, delete, close })
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + 60, 34);

            Controls.Add(_grid);
            Controls.Add(GunaUi.ButtonBar(
                new Control[] { close, delete, rename },   // right (management)
                new Control[] { create, edit }));          // left (creation)

            Refresh2();
            FormAnimator.FadeIn(this);
        }

        // ───────── create / edit ─────────

        private void CreateTemplate()
        {
            var result = ShowHeaderDialog("Create Mapping Template", defaultName: "", defaultHeaders: new List<string>());
            if (result == null) return;
            OpenMappingEditor(result.Value.name, result.Value.headers, priorTemplate: null);
            Refresh2();
        }

        private void EditTemplate()
        {
            var id = SelectedId();
            if (id == null) { Warn("Select a template to edit."); return; }
            var tmpl = _store.LoadAll().FirstOrDefault(x => x.Id == id);
            if (tmpl == null) return;

            // Collect existing source headers (excluding constants which have no source column).
            var existingHeaders = tmpl.Entries
                .Where(e => !e.IsConstant && !string.IsNullOrEmpty(e.SourceHeader))
                .Select(e => e.SourceHeader!)
                .ToList();

            var result = ShowHeaderDialog("Edit Mapping Template", tmpl.Name, existingHeaders);
            if (result == null) return;
            OpenMappingEditor(result.Value.name, result.Value.headers, priorTemplate: tmpl);
            Refresh2();
        }

        /// <summary>
        /// Builds a synthetic <see cref="SourceTable"/> from the supplied column headers, pre-populates
        /// the mapping from <paramref name="priorTemplate"/> (if any), and opens the full MappingForm
        /// so the user can edit field mappings, rules, constants and value-maps, then save as a template.
        /// </summary>
        private void OpenMappingEditor(string suggestedName, List<string> columnHeaders, MappingTemplate? priorTemplate)
        {
            if (columnHeaders.Count == 0) { Warn("Enter at least one column header."); return; }

            var contract = FieldContract.Load();

            // Synthetic table — no actual file; rows are empty (sample column will show blank).
            var table = new SourceTable
            {
                SourcePath = "(template designer)",
                SourceName  = suggestedName,
                Headers     = columnHeaders.ToList()
            };

            // Build a MappingResult with one ColumnMapping per header.
            var mappingResult = new MappingResult();
            foreach (var h in columnHeaders)
                mappingResult.Columns.Add(new ColumnMapping { SourceHeader = h });

            // For edits, restore the prior column assignments, constants, rules and value-maps.
            if (priorTemplate != null)
                TemplateMapper.Apply(priorTemplate, table, contract, mappingResult);

            // MappingForm handles all tabs and the "Save Template" button.  The form opens in
            // template-designer mode: user maps columns, tweaks rules / constants, then clicks
            // "Save Template" (existing button).  The Confirm button (import gate) is still present
            // so required-field validation gives useful feedback, but we don't use its return value.
            using var form = new MappingForm(contract, table, mappingResult,
                clientId: null, store: _store, aiRouter: null);
            form.ShowDialog(this);   // template is saved by the user via "Save Template" inside the form
        }

        // ───────── header collection dialog ─────────

        private (string name, List<string> headers)? ShowHeaderDialog(
            string title, string defaultName, List<string> defaultHeaders)
        {
            using var f = new Form
            {
                Text = $"CargoSync — {title}",
                ClientSize = new Size(500, 440),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false,
                BackColor = AppleTheme.Canvas, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body
            };
            AppleTheme.ApplyWindow(f);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1,
                Padding = new Padding(24, 18, 24, 14), BackColor = AppleTheme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // name label
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));  // name input
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));  // gap
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));  // headers label
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));  // sub-hint
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // headers textarea
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // buttons

            var nameLabel = new Label
            {
                Text = "Template name", Dock = DockStyle.Fill,
                ForeColor = AppleTheme.TextSecondary, Font = AppleTheme.Body,
                TextAlign = ContentAlignment.BottomLeft
            };
            var nameBox = GunaUi.TextBox(defaultName);
            nameBox.Dock = DockStyle.Fill; nameBox.Text = defaultName;

            var gap = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };

            var headersLabel = new Label
            {
                Text = "Source column headers", Dock = DockStyle.Fill,
                ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body,
                TextAlign = ContentAlignment.BottomLeft
            };
            var headersHint = new Label
            {
                Text = "One header per line — these are the column names in your source files.",
                Dock = DockStyle.Fill, ForeColor = AppleTheme.TextSecondary,
                Font = new Font(AppleTheme.Body.FontFamily, 8.5f),
                TextAlign = ContentAlignment.TopLeft
            };
            // Multiline TextBox with AcceptsReturn = true. A RichTextBox here had no AcceptsReturn,
            // so with f.AcceptButton set, pressing Enter to start a second header line instead fired
            // the OK button and submitted the dialog — the user could only ever add ONE header.
            var headersBox = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,                 // Enter = new line, NOT "submit dialog"
                AcceptsTab = false,
                WordWrap = false,
                ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(30, 30, 36), ForeColor = AppleTheme.TextPrimary,
                Font = AppleTheme.Body, BorderStyle = BorderStyle.None,
                Text = string.Join(Environment.NewLine, defaultHeaders)
            };

            var okBtn     = GunaUi.Button("Map Columns →", primary: true);
            var cancelBtn = GunaUi.Button("Cancel",        primary: false);
            okBtn.DialogResult     = DialogResult.OK;
            cancelBtn.DialogResult = DialogResult.Cancel;
            okBtn.Size     = new Size(TextRenderer.MeasureText(okBtn.Text, okBtn.Font).Width + 60, 34);
            cancelBtn.Size = new Size(TextRenderer.MeasureText(cancelBtn.Text, cancelBtn.Font).Width + 60, 34);
            okBtn.Margin     = new Padding(0, 0, 0, 0);
            cancelBtn.Margin = new Padding(0, 0, 10, 0);   // 10px gap to the right of Cancel (RTL flow)
            // FlowLayout (RTL) + Padding = 7px top/bottom → 7+34+7=48px row height → buttons centred
            var btnFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, BackColor = AppleTheme.Canvas,
                Padding = new Padding(0, 7, 0, 7)
            };
            btnFlow.Controls.AddRange(new Control[] { okBtn, cancelBtn });

            layout.Controls.Add(nameLabel,    0, 0);
            layout.Controls.Add(nameBox,      0, 1);
            layout.Controls.Add(gap,          0, 2);
            layout.Controls.Add(headersLabel, 0, 3);
            layout.Controls.Add(headersHint,  0, 4);
            layout.Controls.Add(headersBox,   0, 5);
            layout.Controls.Add(btnFlow,      0, 6);

            f.Controls.Add(layout);
            f.AcceptButton = okBtn; f.CancelButton = cancelBtn;
            f.Shown += (s, e) => nameBox.Focus();

            if (f.ShowDialog(this) != DialogResult.OK) return null;

            string name = nameBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "Untitled template";

            // Split on newlines AND tabs so pasting a header row straight from Excel (tab-separated)
            // becomes one column per header instead of a single run-on header.
            var cols = headersBox.Text
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(h => h.Trim())
                .Where(h => !string.IsNullOrEmpty(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return (name, cols);
        }

        // ───────── existing management actions ─────────

        private void Refresh2()
        {
            _grid.Rows.Clear();
            foreach (var t in _store.LoadAll())
            {
                int row = _grid.Rows.Add(t.Name, t.ScopeLabel, t.IsAuto ? "Auto-learned" : "Manual",
                    t.Entries.Count.ToString(), Short(t.SavedUtc));
                _grid.Rows[row].Tag = t.Id;
            }
        }

        private string? SelectedId() =>
            _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as string : null;

        private void Rename()
        {
            var id = SelectedId();
            if (id == null) { Warn("Select a template to rename."); return; }
            var t = _store.LoadAll().FirstOrDefault(x => x.Id == id);
            if (t == null) return;

            string name = Scheduling.Prompt.Show(this, "New template name:", t.Name);
            if (string.IsNullOrWhiteSpace(name)) return;
            t.Name = name.Trim();
            _store.Save(t, string.IsNullOrEmpty(t.SavedUtc) ? DateTime.UtcNow.ToString("o") : t.SavedUtc);
            Refresh2();
        }

        private void DeleteSelected()
        {
            var id = SelectedId();
            if (id == null) { Warn("Select a template to delete."); return; }
            var t = _store.LoadAll().FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            if (MessageBox.Show(this, $"Delete template '{t.Name}'?", "CargoSync",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            _store.Delete(id);
            Refresh2();
        }

        private static string Short(string iso) => iso.Length >= 10 ? iso.Substring(0, 10) : iso;
        private void Warn(string m) => MessageBox.Show(this, m, "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
