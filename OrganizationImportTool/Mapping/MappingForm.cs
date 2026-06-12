using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// The mandatory user-validation screen. Tab 1 maps each source column to a CargoWise field
    /// (auto-suggested, with confidence + sample); Tab 2 sets constant/default values. Mappings
    /// can be saved as reusable templates (global or per-client) and re-applied to later files.
    /// "Confirm" is blocked until all required fields are satisfied - the human accuracy gate.
    /// </summary>
    public class MappingForm : Form
    {
        private readonly FieldContract _contract;
        private readonly SourceTable _table;
        private readonly MappingResult _result;
        private readonly MappingSuggester _suggester;
        private readonly string? _clientId;
        private readonly TemplateStore _store;
        private readonly AiRouter? _aiRouter;

        private Guna2DataGridView _grid = null!;
        private Guna2DataGridView _constGrid = null!;
        private Guna2DataGridView _rulesGrid = null!;
        private Guna2DataGridView _valueMapGrid = null!;
        private Guna2DataGridView _candGrid = null!;
        private Label _statusLabel = null!;
        private Label _explainBadge = null!;
        private Label _explainText = null!;
        private Guna2Button _approveBtn = null!;
        private Guna2Button _confirmBtn = null!;
        private readonly List<FieldOption> _fieldOptions;
        private readonly List<FieldOption> _constFieldOptions;

        private readonly List<ColumnMapping> _rowMap = new List<ColumnMapping>();

        /// <summary>The confirmed mapping (valid only when DialogResult == OK).</summary>
        public MappingResult ConfirmedResult => _result;

        public MappingForm(FieldContract contract, SourceTable table, MappingResult result,
                           string? clientId = null, TemplateStore? store = null, AiRouter? aiRouter = null)
        {
            _contract = contract;
            _table = table;
            _result = result;
            _clientId = clientId;
            _store = store ?? new TemplateStore();
            _aiRouter = aiRouter;
            _suggester = new MappingSuggester(contract);

            _fieldOptions = BuildFieldOptions(includeIgnore: true);
            _constFieldOptions = BuildFieldOptions(includeIgnore: false);
            InitializeUi();
            PopulateRows();
            PopulateConstants();
            PopulateRules();
            PopulateValueMaps();
            UpdateStatus();
        }

        private List<FieldOption> BuildFieldOptions(bool includeIgnore)
        {
            var options = new List<FieldOption>();
            if (includeIgnore) options.Add(new FieldOption(string.Empty, "— (ignore this column) —"));
            options.AddRange(_contract.MappableFields
                .OrderBy(f => f.Group)
                .ThenBy(f => f.Label)
                .Select(f => new FieldOption(f.Path, f.DisplayName + (f.Required ? "  *required" : string.Empty))));
            return options;
        }

        private void InitializeUi()
        {
            Text = "Review & Confirm Field Mapping";
            ClientSize = new Size(960, 660);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            var header = new Label
            {
                Text = $"Map your file's columns to CargoWise fields  —  {_table.SourceName}  ({_table.RowCount} rows)",
                Dock = DockStyle.Fill,
                Padding = new Padding(14, 12, 0, 0),
                Font = AppleTheme.Headline,
                ForeColor = AppleTheme.Accent
            };

            var tabs = new GunaTabs { Dock = DockStyle.Fill };
            tabs.AddTab("Column Mapping", BuildColumnsGrid());
            tabs.AddTab("Constants & Defaults", BuildConstantsPanel());
            tabs.AddTab("Rules", BuildRulesPanel());
            tabs.AddTab("Value Maps", BuildValueMapsPanel());

            var explainPanel = BuildExplainPanel();
            explainPanel.Dock = DockStyle.Fill;

            var bottom = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };

            _statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(14, 10, 12, 0),
                Font = AppleTheme.Body
            };

            var buttonRow = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(14, 4, 14, 8) };

            var saveTpl = GunaUi.Button("Save Template", primary: false); saveTpl.Size = new Size(130, 36); saveTpl.Click += SaveTemplate_Click;
            var loadTpl = GunaUi.Button("Load Template", primary: false); loadTpl.Size = new Size(130, 36); loadTpl.Click += LoadTemplate_Click;
            var copilotBtn = GunaUi.Button("✨ Copilot", primary: false); copilotBtn.Size = new Size(120, 36); copilotBtn.Click += CopilotBtn_Click;
            var leftFlow = new FlowLayoutPanel { Dock = DockStyle.Left, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            leftFlow.Controls.Add(saveTpl);
            leftFlow.Controls.Add(loadTpl);
            leftFlow.Controls.Add(copilotBtn);

            _confirmBtn = GunaUi.Button("Confirm Mapping  ✓", primary: true); _confirmBtn.Size = new Size(190, 36); _confirmBtn.Click += ConfirmBtn_Click;
            var cancelBtn = GunaUi.Button("Cancel", primary: false); cancelBtn.Size = new Size(100, 36); cancelBtn.DialogResult = DialogResult.Cancel;
            var rightFlow = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            rightFlow.Controls.Add(_confirmBtn);
            rightFlow.Controls.Add(cancelBtn);

            buttonRow.Controls.Add(leftFlow);
            buttonRow.Controls.Add(rightFlow);
            bottom.Controls.Add(buttonRow);
            bottom.Controls.Add(_statusLabel);

            // Explicit row heights in a TableLayoutPanel — GunaTabs (Dock.Fill) misbehaves as a
            // docked sibling and steals the space the panel + Confirm bar need, so we pin each band.
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = AppleTheme.Canvas
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // tabs (grid)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));  // explainability panel
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));   // status + buttons
            header.Margin = tabs.Margin = explainPanel.Margin = bottom.Margin = new Padding(0);
            root.Controls.Add(header, 0, 0);
            root.Controls.Add(tabs, 0, 1);
            root.Controls.Add(explainPanel, 0, 2);
            root.Controls.Add(bottom, 0, 3);

            Controls.Add(root);
            CancelButton = cancelBtn;
            FormAnimator.FadeIn(this);
        }

        private Control BuildColumnsGrid()
        {
            _grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _grid.Columns.AddRange(
                new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Use", FillWeight = 6 },
                new DataGridViewCheckBoxColumn { Name = "Approve", HeaderText = "Approve", FillWeight = 9 },
                new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Your Column", ReadOnly = true, FillWeight = 19 },
                new DataGridViewTextBoxColumn { Name = "Sample", HeaderText = "Sample Value", ReadOnly = true, FillWeight = 18, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } },
                new DataGridViewComboBoxColumn { Name = "Target", HeaderText = "CargoWise Field", FillWeight = 36, DataSource = _fieldOptions, DisplayMember = "Display", ValueMember = "Path", FlatStyle = FlatStyle.Flat, DropDownWidth = 320 },
                new DataGridViewTextBoxColumn { Name = "Confidence", HeaderText = "Match", ReadOnly = true, FillWeight = 12, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } });

            // Never sort: row order must stay locked to _rowMap (header-click sort would misalign every edit).
            foreach (DataGridViewColumn c in _grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            AppleTheme.StyleGrid(_grid);
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;
            _grid.CellFormatting += Grid_CellFormatting;
            _grid.CurrentCellDirtyStateChanged += (s, e) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _grid.CellValueChanged += Grid_CellValueChanged;
            _grid.CurrentCellChanged += (s, e) => UpdateExplain();
            _grid.DataError += (s, e) => { e.ThrowException = false; };
            return _grid;
        }

        private Control BuildExplainPanel()
        {
            var panel = new Guna2Panel
            {
                Dock = DockStyle.Bottom, Height = 158,
                FillColor = AppleTheme.Surface, BorderRadius = 10,
                Padding = new Padding(14, 8, 14, 10), Margin = new Padding(0, 6, 0, 0)
            };

            // Two columns: left = plain-language narrative, right = the full-height candidate grid.
            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));

            // ---- left: narrative + approve action ----
            var left = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 0, 12, 0) };
            var titleRow = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = Color.Transparent };
            var title = new Label { Text = "Why this mapping?", Dock = DockStyle.Fill, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = AppleTheme.Headline, ForeColor = AppleTheme.Accent };
            _approveBtn = GunaUi.Button("Approve  ✓", primary: true);
            _approveBtn.Dock = DockStyle.Right; _approveBtn.Width = 150; _approveBtn.Click += (s, e) => ApproveCurrent();
            titleRow.Controls.Add(title);          // Fill (added first so the Right button keeps its width)
            titleRow.Controls.Add(_approveBtn);
            _explainBadge = new Label { Dock = DockStyle.Top, Height = 34, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, Padding = new Padding(0, 2, 0, 2) };
            _explainText = new Label { Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextPrimary, Padding = new Padding(0, 4, 0, 0) };
            left.Controls.Add(_explainText);   // Fill first
            left.Controls.Add(_explainBadge);
            left.Controls.Add(titleRow);

            // ---- right: candidate grid ----
            var right = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            var candTitle = new Label { Text = "Top fields the matcher considered", Dock = DockStyle.Top, Height = 24, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            _candGrid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true,
                RowHeadersVisible = false, ColumnHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
                AllowUserToResizeRows = false
            };
            _candGrid.Columns.AddRange(
                new DataGridViewTextBoxColumn { Name = "Pick", HeaderText = "", FillWeight = 7, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "CField", HeaderText = "CargoWise Field", FillWeight = 45 },
                new DataGridViewTextBoxColumn { Name = "CScore", HeaderText = "Match", FillWeight = 16, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "CMatched", HeaderText = "Matched on", FillWeight = 32, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray } });
            foreach (DataGridViewColumn c in _candGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_candGrid);
            right.Controls.Add(_candGrid);     // Fill first
            right.Controls.Add(candTitle);

            split.Controls.Add(left, 0, 0);
            split.Controls.Add(right, 1, 0);
            panel.Controls.Add(split);
            return panel;
        }

        /// <summary>Refresh the explainability panel for the row the operator is looking at.</summary>
        private void UpdateExplain()
        {
            if (_candGrid == null || _explainText == null) return;
            int idx = _grid.CurrentCell?.RowIndex ?? -1;
            if (idx < 0 || idx >= _rowMap.Count) return;
            var col = _rowMap[idx];

            bool mapped = !string.IsNullOrEmpty(col.TargetPath) && col.Include;
            string approvalTag = !mapped ? string.Empty
                : col.Approved ? "   •   ✓ approved" : "   •   ⚠ needs your approval";
            _explainBadge.Text = $"Your column “{col.SourceHeader}”   •   {SourceLabel(col.Source)}   •   {ConfidenceText(col)}{approvalTag}";
            _explainBadge.ForeColor = NeedsApproval(col) ? Color.FromArgb(243, 156, 18) : AppleTheme.TextSecondary;
            _explainText.Text = string.IsNullOrWhiteSpace(col.Rationale)
                ? "Choose the CargoWise field this column should fill, or untick to ignore it."
                : col.Rationale;

            // Approve action: only meaningful for a mapped row that still needs sign-off.
            if (NeedsApproval(col))
            {
                _approveBtn.Visible = true; _approveBtn.Enabled = true;
                _approveBtn.Text = "Approve  ✓";
            }
            else if (mapped)
            {
                _approveBtn.Visible = true; _approveBtn.Enabled = false;
                _approveBtn.Text = "Approved  ✓";
            }
            else
            {
                _approveBtn.Visible = false;
            }

            _candGrid.Rows.Clear();
            foreach (var cand in col.Candidates.OrderByDescending(c => c.Score))
            {
                int r = _candGrid.Rows.Add(cand.Chosen ? "✓" : "", cand.Label, $"{cand.Score:P0}", cand.MatchedOn);
                if (cand.Chosen)
                {
                    var st = _candGrid.Rows[r].DefaultCellStyle;
                    st.ForeColor = Color.White;
                    st.BackColor = Color.FromArgb(33, 90, 60);
                    st.SelectionBackColor = Color.FromArgb(33, 90, 60);
                }
            }
            if (col.Candidates.Count == 0)
                _candGrid.Rows.Add("", "(no scored alternatives — pick from the dropdown above)", "", "");
        }

        /// <summary>Sign off on the AI/low-confidence match for the row the operator is viewing.</summary>
        private void ApproveCurrent()
        {
            int idx = _grid.CurrentCell?.RowIndex ?? -1;
            if (idx < 0 || idx >= _rowMap.Count) return;
            var col = _rowMap[idx];
            if (string.IsNullOrEmpty(col.TargetPath)) return;
            col.Approved = true;
            _grid.Rows[idx].Cells["Approve"].Value = true;
            _grid.InvalidateRow(idx);
            UpdateExplain();
            UpdateStatus();
        }

        private static string SourceLabel(MappingSource s) => s switch
        {
            MappingSource.ExactAlias => "Exact name match",
            MappingSource.Fuzzy => "Fuzzy match",
            MappingSource.Ai => "AI suggestion",
            MappingSource.Template => "Recalled / template",
            MappingSource.Manual => "Set by you",
            _ => "Unmapped"
        };

        private string LabelFor(string path) =>
            _contract.MappableFields.FirstOrDefault(f =>
                string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? path;

        private Control BuildConstantsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(8) };
            var help = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = AppleTheme.Canvas, Padding = new Padding(4, 6, 4, 4) };
            var helpDesc = new Label
            {
                Dock = DockStyle.Top, Height = 44, AutoSize = false, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary,
                Text = "Set a fixed value applied to every organization (e.g. Category = BUS, Is Active = true).\nThese fill or override the column mapping for the chosen field."
            };
            var helpTitle = new Label
            {
                Dock = DockStyle.Top, Height = 26, AutoSize = false, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary,
                Text = "Constants & Defaults", TextAlign = ContentAlignment.MiddleLeft
            };
            help.Controls.Add(helpDesc);
            help.Controls.Add(helpTitle);

            _constGrid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None
            };
            _constGrid.Columns.Add(new DataGridViewComboBoxColumn
            {
                Name = "Field", HeaderText = "CargoWise Field", FillWeight = 60,
                DataSource = _constFieldOptions, DisplayMember = "Display", ValueMember = "Path",
                FlatStyle = FlatStyle.Flat, DropDownWidth = 320
            });
            _constGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Constant Value", FillWeight = 40 });
            AppleTheme.StyleGrid(_constGrid);
            _constGrid.AllowUserToAddRows = true;
            _constGrid.DataError += (s, e) => { e.ThrowException = false; };
            _constGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_constGrid.IsCurrentCellDirty) _constGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _constGrid.CellValueChanged += (s, e) => { CaptureConstants(); UpdateStatus(); };

            var removeBtn = GunaUi.Button("Remove Selected", primary: false); removeBtn.Dock = DockStyle.Bottom; removeBtn.Height = 34;
            removeBtn.Click += (s, e) =>
            {
                foreach (DataGridViewRow r in _constGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
                    if (!r.IsNewRow) _constGrid.Rows.Remove(r);
                CaptureConstants(); UpdateStatus();
            };

            panel.Controls.Add(_constGrid);
            panel.Controls.Add(removeBtn);
            panel.Controls.Add(help);
            return panel;
        }

        private Control BuildRulesPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(8) };
            var help = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = AppleTheme.Canvas, Padding = new Padding(4, 6, 4, 4) };
            var helpDesc = new Label
            {
                Dock = DockStyle.Top, Height = 46, AutoSize = false, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary,
                Text = "Build IF–THEN rules without code: when one of your columns matches a condition, set a CargoWise field.\n" +
                       "Example: If \"Type\" contains \"IMP\" → set Is Consignee = true.   Rules run on every row before sending."
            };
            var helpTitle = new Label
            {
                Dock = DockStyle.Top, Height = 26, AutoSize = false, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary,
                Text = "Rules (no-code IF → THEN)", TextAlign = ContentAlignment.MiddleLeft
            };
            help.Controls.Add(helpDesc);
            help.Controls.Add(helpTitle);

            var opOptions = Enum.GetValues(typeof(RuleOp)).Cast<RuleOp>()
                .Select(o => new OpOption(o, TransformRule.OpText(o))).ToList();

            _rulesGrid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _rulesGrid.Columns.AddRange(
                new DataGridViewCheckBoxColumn { Name = "Enabled", HeaderText = "On", FillWeight = 6 },
                new DataGridViewTextBoxColumn { Name = "IfLbl", HeaderText = "If column", ReadOnly = true, FillWeight = 6, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray, Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewComboBoxColumn { Name = "When", HeaderText = "Your column", FillWeight = 20, DataSource = _table.Headers.ToList(), FlatStyle = FlatStyle.Flat, DropDownWidth = 220 },
                new DataGridViewComboBoxColumn { Name = "Op", HeaderText = "Condition", FillWeight = 14, DataSource = opOptions, DisplayMember = "Display", ValueMember = "Op", FlatStyle = FlatStyle.Flat },
                new DataGridViewTextBoxColumn { Name = "Value", HeaderText = "Value", FillWeight = 16 },
                new DataGridViewTextBoxColumn { Name = "ThenLbl", HeaderText = "then set", ReadOnly = true, FillWeight = 7, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray, Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewComboBoxColumn { Name = "Field", HeaderText = "CargoWise field", FillWeight = 21, DataSource = _constFieldOptions, DisplayMember = "Display", ValueMember = "Path", FlatStyle = FlatStyle.Flat, DropDownWidth = 320 },
                new DataGridViewTextBoxColumn { Name = "To", HeaderText = "to value", FillWeight = 14 });
            foreach (DataGridViewColumn c in _rulesGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_rulesGrid);
            _rulesGrid.AllowUserToAddRows = true;
            _rulesGrid.DataError += (s, e) => { e.ThrowException = false; };
            _rulesGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_rulesGrid.IsCurrentCellDirty) _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _rulesGrid.CellValueChanged += (s, e) => { CaptureRules(); UpdateStatus(); };
            // seed the static "If"/"then set" hint cells on every new row
            _rulesGrid.RowsAdded += (s, e) =>
            {
                for (int i = e.RowIndex; i < e.RowIndex + e.RowCount && i < _rulesGrid.Rows.Count; i++)
                {
                    if (_rulesGrid.Rows[i].IsNewRow) continue;
                    if (_rulesGrid.Rows[i].Cells["Enabled"].Value == null) _rulesGrid.Rows[i].Cells["Enabled"].Value = true;
                    _rulesGrid.Rows[i].Cells["IfLbl"].Value = "IF";
                    _rulesGrid.Rows[i].Cells["ThenLbl"].Value = "THEN";
                }
            };

            var removeBtn = GunaUi.Button("Remove Selected", primary: false); removeBtn.Dock = DockStyle.Bottom; removeBtn.Height = 34;
            removeBtn.Click += (s, e) =>
            {
                foreach (DataGridViewRow r in _rulesGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
                    if (!r.IsNewRow) _rulesGrid.Rows.Remove(r);
                CaptureRules(); UpdateStatus();
            };

            panel.Controls.Add(_rulesGrid);
            panel.Controls.Add(removeBtn);
            panel.Controls.Add(help);
            return panel;
        }

        private void PopulateRules()
        {
            _rulesGrid.Rows.Clear();
            foreach (var r in _result.Rules)
            {
                int i = _rulesGrid.Rows.Add(r.Enabled, "IF", r.WhenColumn, r.Op, r.WhenValue, "THEN", r.ThenField ?? string.Empty, r.ThenValue);
            }
        }

        private void CaptureRules()
        {
            _result.Rules.Clear();
            foreach (DataGridViewRow row in _rulesGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string when = row.Cells["When"].Value?.ToString() ?? string.Empty;
                string field = row.Cells["Field"].Value?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(when) && string.IsNullOrWhiteSpace(field)) continue;
                var rule = new TransformRule
                {
                    Enabled = Convert.ToBoolean(row.Cells["Enabled"].Value ?? true),
                    WhenColumn = when,
                    Op = row.Cells["Op"].Value is RuleOp op ? op : RuleOp.Equals,
                    WhenValue = row.Cells["Value"].Value?.ToString() ?? string.Empty,
                    ThenField = field,
                    ThenValue = row.Cells["To"].Value?.ToString() ?? string.Empty
                };
                _result.Rules.Add(rule);
            }
        }

        private sealed class OpOption
        {
            public RuleOp Op { get; }
            public string Display { get; }
            public OpOption(RuleOp op, string display) { Op = op; Display = display; }
        }

        private Control BuildValueMapsPanel()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(8) };
            var help = new Panel { Dock = DockStyle.Top, Height = 80, BackColor = AppleTheme.Canvas, Padding = new Padding(4, 6, 4, 4) };
            var helpDesc = new Label
            {
                Dock = DockStyle.Top, Height = 46, AutoSize = false, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary,
                Text = "Translate a client's own codes into CargoWise values for a field. Example: for Country map \"AUS\" → \"AU\", \"NZL\" → \"NZ\".\n" +
                       "Each row: when a column's value equals the source, it's swapped for the output before sending. Add several rows per field."
            };
            var helpTitle = new Label
            {
                Dock = DockStyle.Top, Height = 26, AutoSize = false, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary,
                Text = "Value Maps (client code → CargoWise value)", TextAlign = ContentAlignment.MiddleLeft
            };
            help.Controls.Add(helpDesc);
            help.Controls.Add(helpTitle);

            _valueMapGrid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = true, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.None,
                EditMode = DataGridViewEditMode.EditOnEnter
            };
            _valueMapGrid.Columns.AddRange(
                new DataGridViewComboBoxColumn { Name = "Field", HeaderText = "CargoWise field", FillWeight = 42, DataSource = _constFieldOptions, DisplayMember = "Display", ValueMember = "Path", FlatStyle = FlatStyle.Flat, DropDownWidth = 320 },
                new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "When the value is…", FillWeight = 29 },
                new DataGridViewTextBoxColumn { Name = "ArrowLbl", HeaderText = "", ReadOnly = true, FillWeight = 5, DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.Gray, Alignment = DataGridViewContentAlignment.MiddleCenter } },
                new DataGridViewTextBoxColumn { Name = "Output", HeaderText = "…send this instead", FillWeight = 29 });
            foreach (DataGridViewColumn c in _valueMapGrid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
            AppleTheme.StyleGrid(_valueMapGrid);
            _valueMapGrid.AllowUserToAddRows = true;
            _valueMapGrid.DataError += (s, e) => { e.ThrowException = false; };
            _valueMapGrid.CurrentCellDirtyStateChanged += (s, e) => { if (_valueMapGrid.IsCurrentCellDirty) _valueMapGrid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            _valueMapGrid.CellValueChanged += (s, e) => { CaptureValueMaps(); UpdateStatus(); };
            _valueMapGrid.RowsAdded += (s, e) =>
            {
                for (int i = e.RowIndex; i < e.RowIndex + e.RowCount && i < _valueMapGrid.Rows.Count; i++)
                    if (!_valueMapGrid.Rows[i].IsNewRow) _valueMapGrid.Rows[i].Cells["ArrowLbl"].Value = "→";
            };

            var removeBtn = GunaUi.Button("Remove Selected", primary: false); removeBtn.Dock = DockStyle.Bottom; removeBtn.Height = 34;
            removeBtn.Click += (s, e) =>
            {
                foreach (DataGridViewRow r in _valueMapGrid.SelectedRows.Cast<DataGridViewRow>().ToList())
                    if (!r.IsNewRow) _valueMapGrid.Rows.Remove(r);
                CaptureValueMaps(); UpdateStatus();
            };

            panel.Controls.Add(_valueMapGrid);
            panel.Controls.Add(removeBtn);
            panel.Controls.Add(help);
            return panel;
        }

        private void PopulateValueMaps()
        {
            _valueMapGrid.Rows.Clear();
            foreach (var kv in _result.ValueMaps)
                foreach (var inner in kv.Value)
                    _valueMapGrid.Rows.Add(kv.Key, inner.Key, "→", inner.Value);
        }

        private void CaptureValueMaps()
        {
            _result.ValueMaps.Clear();
            foreach (DataGridViewRow row in _valueMapGrid.Rows)
            {
                if (row.IsNewRow) continue;
                string path = row.Cells["Field"].Value?.ToString() ?? string.Empty;
                string src = row.Cells["Source"].Value?.ToString() ?? string.Empty;
                string outp = row.Cells["Output"].Value?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(src)) continue;
                if (!_result.ValueMaps.TryGetValue(path, out var map))
                    _result.ValueMaps[path] = map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                map[src.Trim()] = outp.Trim();
            }
        }

        private void PopulateRows()
        {
            _grid.Rows.Clear();
            _rowMap.Clear();
            foreach (var col in _result.Columns)
            {
                _grid.Rows.Add(col.Include, col.Approved, col.SourceHeader, Truncate(col.SampleValue, 60),
                    col.TargetPath ?? string.Empty, ConfidenceText(col));
                _rowMap.Add(col);
            }
            UpdateExplain();
        }

        private void PopulateConstants()
        {
            _constGrid.Rows.Clear();
            foreach (var kv in _result.Constants)
                _constGrid.Rows.Add(kv.Key, kv.Value);
        }

        private void CaptureConstants()
        {
            _result.Constants.Clear();
            foreach (DataGridViewRow r in _constGrid.Rows)
            {
                if (r.IsNewRow) continue;
                string path = r.Cells["Field"].Value?.ToString() ?? string.Empty;
                string val = r.Cells["Value"].Value?.ToString() ?? string.Empty;
                if (!string.IsNullOrEmpty(path) && !string.IsNullOrWhiteSpace(val))
                    _result.Constants[path] = val.Trim();
            }
        }

        private void Grid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rowMap.Count) return;
            var col = _rowMap[e.RowIndex];
            var row = _grid.Rows[e.RowIndex];
            switch (_grid.Columns[e.ColumnIndex].Name)
            {
                case "Include":
                    col.Include = Convert.ToBoolean(row.Cells["Include"].Value ?? false);
                    break;
                case "Approve":
                    col.Approved = Convert.ToBoolean(row.Cells["Approve"].Value ?? false);
                    _grid.InvalidateRow(e.RowIndex);
                    UpdateExplain();
                    break;
                case "Target":
                    string path = row.Cells["Target"].Value?.ToString() ?? string.Empty;
                    col.TargetPath = string.IsNullOrEmpty(path) ? null : path;
                    col.Source = MappingSource.Manual;
                    col.Confidence = string.IsNullOrEmpty(path) ? MappingConfidence.Unmapped : MappingConfidence.High;
                    col.Approved = true; // a manual selection is the operator's own decision
                    if (string.IsNullOrEmpty(path))
                    {
                        col.Rationale = $"You chose to ignore “{col.SourceHeader}”.";
                        col.Candidates = new List<MappingCandidate>();
                    }
                    else
                    {
                        string lbl = LabelFor(path);
                        col.Rationale = $"You set this manually — “{col.SourceHeader}” → {lbl}.";
                        col.Candidates = new List<MappingCandidate>
                        {
                            new MappingCandidate { Path = path, Label = lbl, Score = 1.0, MatchedOn = "your choice", Chosen = true }
                        };
                    }
                    row.Cells["Confidence"].Value = ConfidenceText(col);
                    row.Cells["Approve"].Value = col.Approved;
                    _grid.InvalidateRow(e.RowIndex);
                    UpdateExplain();
                    break;
            }
            UpdateStatus();
        }

        private void Grid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= _rowMap.Count) return;
            var col = _rowMap[e.RowIndex];
            string colName = _grid.Columns[e.ColumnIndex].Name;

            if (colName == "Confidence")
            {
                e.CellStyle.ForeColor = Color.White;
                e.CellStyle.BackColor = col.Confidence switch
                {
                    MappingConfidence.High => Color.FromArgb(39, 174, 96),
                    MappingConfidence.Medium => Color.FromArgb(243, 156, 18),
                    MappingConfidence.Low => Color.FromArgb(211, 84, 0),
                    _ => Color.FromArgb(149, 165, 166)
                };
            }
            else if (colName == "Approve")
            {
                // Amber-tint the approve cell of any mapped row still awaiting sign-off.
                e.CellStyle.BackColor = NeedsApproval(col)
                    ? Color.FromArgb(120, 60, 0)
                    : _grid.DefaultCellStyle.BackColor;
            }
        }

        /// <summary>A mapped, included column that hasn't been approved yet (below-High or AI).</summary>
        private static bool NeedsApproval(ColumnMapping c) =>
            c.Include && !string.IsNullOrEmpty(c.TargetPath) && !c.Approved;

        private void UpdateStatus()
        {
            _suggester.RecomputeUnmappedRequired(_result);
            int mapped = _result.Columns.Count(c => c.Include && !string.IsNullOrEmpty(c.TargetPath));
            int consts = _result.Constants.Count;
            string constText = consts > 0 ? $" + {consts} constant(s)" : string.Empty;

            if (_result.UnmappedRequired.Count > 0)
            {
                _statusLabel.ForeColor = AppleTheme.Danger;
                _statusLabel.Text = $"⚠ {mapped} columns mapped{constText}.  Required fields still missing: " +
                                    string.Join(", ", _result.UnmappedRequired.Select(f => f.Label));
                _confirmBtn.Enabled = false;
            }
            else
            {
                int pending = _result.Columns.Count(NeedsApproval);
                if (pending > 0)
                {
                    _statusLabel.ForeColor = Color.FromArgb(243, 156, 18);
                    _statusLabel.Text = $"✓ {mapped} columns mapped{constText}.  {pending} AI/low-confidence match(es) need your approval — review the Approve column, or confirm to approve all.";
                }
                else
                {
                    _statusLabel.ForeColor = AppleTheme.Success;
                    _statusLabel.Text = $"✓ {mapped} columns mapped{constText}.  All required fields satisfied and approved — ready to confirm.";
                }
                _confirmBtn.Enabled = true;
            }
        }

        private void ConfirmBtn_Click(object? sender, EventArgs e)
        {
            _grid.EndEdit();
            _constGrid.EndEdit();
            _rulesGrid.EndEdit();
            _valueMapGrid.EndEdit();
            CaptureConstants();
            CaptureRules();
            CaptureValueMaps();

            var dupes = _result.Columns
                .Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath))
                .GroupBy(c => c.TargetPath!, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();
            if (dupes.Count > 0)
            {
                var proceed = MessageBox.Show(this,
                    "These CargoWise fields are mapped from more than one column — only the last column's value will be used:\n\n  " +
                    string.Join("\n  ", dupes) + "\n\nContinue anyway?",
                    "Duplicate mappings", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (proceed != DialogResult.Yes) return;
            }

            _suggester.RecomputeUnmappedRequired(_result);
            if (_result.UnmappedRequired.Count > 0)
            {
                MessageBox.Show(this,
                    "These required CargoWise fields are not mapped yet:\n\n  " +
                    string.Join("\n  ", _result.UnmappedRequired.Select(f => f.Label)),
                    "Mapping incomplete", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Approval gate: AI-suggested / below-High matches must be signed off before they update orgs.
            var pending = _result.Columns.Where(NeedsApproval).ToList();
            if (pending.Count > 0)
            {
                var res = MessageBox.Show(this,
                    $"{pending.Count} mapping(s) are AI-suggested or below \"High\" confidence and haven't been approved:\n\n  " +
                    string.Join("\n  ", pending.Select(c => $"{c.SourceHeader}  →  {LabelFor(c.TargetPath!)}  ({ConfidenceText(c)})")) +
                    "\n\nApprove all and continue?\n   Yes = approve every one and import\n   No = go back and review them individually",
                    "Approvals needed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (res != DialogResult.Yes) return;
                foreach (var c in pending)
                {
                    c.Approved = true;
                    int ri = _rowMap.IndexOf(c);
                    if (ri >= 0) _grid.Rows[ri].Cells["Approve"].Value = true;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------------- Copilot ----------------
        private void CopilotBtn_Click(object? sender, EventArgs e)
        {
            // Refresh the live result so the copilot sees the operator's latest edits.
            _grid.EndEdit();
            CaptureConstants();
            CaptureRules();
            _suggester.RecomputeUnmappedRequired(_result);

            if (_aiRouter == null || !_aiRouter.IsConfigured)
            {
                MessageBox.Show(this,
                    "AI isn't configured yet. Add a provider in AI Settings to chat with the Copilot.",
                    "Copilot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using var f = new CopilotForm(_aiRouter, _contract, _table, _result);
            f.ShowDialog(this);
        }

        // ---------------- Templates ----------------
        private void SaveTemplate_Click(object? sender, EventArgs e)
        {
            // Commit + capture EVERYTHING so the saved template includes constants, rules and value-maps.
            _grid.EndEdit();
            _constGrid.EndEdit();
            _rulesGrid.EndEdit();
            _valueMapGrid.EndEdit();
            CaptureConstants();
            CaptureRules();
            CaptureValueMaps();
            var dlg = PromptSaveTemplate();
            if (dlg == null) return;

            var template = TemplateMapper.ToTemplate(_result, dlg.Value.name, dlg.Value.isGlobal ? null : _clientId);
            try
            {
                _store.Save(template, DateTime.UtcNow.ToString("o"));
                MessageBox.Show(this, $"Template '{template.Name}' saved ({template.ScopeLabel}).", "Saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save template: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadTemplate_Click(object? sender, EventArgs e)
        {
            var templates = _store.ForClient(_clientId);
            if (templates.Count == 0)
            {
                MessageBox.Show(this, "No saved templates yet. Map your columns, then 'Save Template' to reuse them next time.",
                    "No templates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var picked = PickTemplate(templates);
            if (picked == null) return;

            TemplateMapper.Apply(picked, _table, _contract, _result);
            // Refresh ALL tabs so the loaded constants/rules/value-maps are visible AND aren't wiped
            // when Confirm re-captures from the grids.
            PopulateRows();
            PopulateConstants();
            PopulateRules();
            PopulateValueMaps();
            UpdateStatus();
        }

        private (string name, bool isGlobal)? PromptSaveTemplate()
        {
            using var f = new Form
            {
                Text = "Save Mapping Template", ClientSize = new Size(440, 280),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false
            };
            AppleTheme.ApplyWindow(f);
            var card = GunaUi.Card(); card.Dock = DockStyle.Fill; card.Padding = new Padding(22, 18, 22, 16);

            var lbl = new Label { Text = "Template name", Dock = DockStyle.Top, Height = 22, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            var txt = GunaUi.TextBox("e.g. Acme standard mapping");
            var chk = GunaUi.Check("Global (available to all clients)"); chk.Checked = string.IsNullOrEmpty(_clientId); chk.Enabled = !string.IsNullOrEmpty(_clientId);
            var err = new Label { Text = "", Dock = DockStyle.Top, Height = 22, Font = AppleTheme.Body, ForeColor = AppleTheme.Danger };
            var title = new Label { Text = "Save Mapping Template", Dock = DockStyle.Top, Height = 34, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 50, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent };
            var ok = GunaUi.Button("Save", primary: true); ok.Size = new Size(110, 38);
            var cancel = GunaUi.Button("Cancel", primary: false); cancel.Size = new Size(100, 38); cancel.Margin = new Padding(8, 0, 0, 0);
            ok.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txt.Text)) { err.Text = "Please enter a template name."; return; }
                f.DialogResult = DialogResult.OK; f.Close();
            };
            cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
            btnRow.Controls.Add(ok);
            btnRow.Controls.Add(cancel);

            // add Top controls bottom-up so visual order is: title, name, field, global, error
            chk.Dock = DockStyle.Top; chk.AutoSize = false; chk.Height = 30; chk.Margin = new Padding(0, 6, 0, 0);
            txt.Dock = DockStyle.Top; txt.Margin = new Padding(0, 0, 0, 6);
            card.Controls.Add(err);
            card.Controls.Add(chk);
            card.Controls.Add(txt);
            card.Controls.Add(lbl);
            card.Controls.Add(title);

            f.Controls.Add(card);
            f.Controls.Add(btnRow);
            f.AcceptButton = ok; f.CancelButton = cancel;

            if (f.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(txt.Text)) return null;
            return (txt.Text.Trim(), chk.Checked);
        }

        private MappingTemplate? PickTemplate(List<MappingTemplate> templates)
        {
            using var f = new Form
            {
                Text = "Load Mapping Template", ClientSize = new Size(480, 420),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false
            };
            AppleTheme.ApplyWindow(f);
            var card = GunaUi.Card(); card.Dock = DockStyle.Fill; card.Padding = new Padding(16, 14, 16, 14);

            var title = new Label { Text = "Load Mapping Template", Dock = DockStyle.Top, Height = 32, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };
            var list = new ListBox { Dock = DockStyle.Fill, Font = AppleTheme.Body, BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary, BorderStyle = BorderStyle.None, IntegralHeight = false };
            foreach (var t in templates)
                list.Items.Add($"{t.Name}   [{t.ScopeLabel}]   ({t.Entries.Count} fields)");
            if (list.Items.Count > 0) list.SelectedIndex = 0;

            var btnRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 54, FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0) };
            var apply = GunaUi.Button("Apply", primary: true); apply.Size = new Size(110, 38);
            var del = GunaUi.Button("Delete", primary: false); del.Size = new Size(100, 38); del.Margin = new Padding(8, 0, 0, 0);
            var cancel = GunaUi.Button("Cancel", primary: false); cancel.Size = new Size(100, 38); cancel.Margin = new Padding(8, 0, 0, 0);
            apply.Click += (s, e) => { if (list.SelectedIndex >= 0) { f.DialogResult = DialogResult.OK; f.Close(); } };
            cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; f.Close(); };
            del.Click += (s, e) =>
            {
                if (list.SelectedIndex < 0) return;
                var t = templates[list.SelectedIndex];
                if (MessageBox.Show(f, $"Delete template '{t.Name}'?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    _store.Delete(t.Id);
                    templates.RemoveAt(list.SelectedIndex);
                    list.Items.RemoveAt(list.SelectedIndex);
                    if (list.Items.Count > 0) list.SelectedIndex = 0;
                }
            };
            btnRow.Controls.Add(apply);  // RightToLeft: apply rightmost
            btnRow.Controls.Add(cancel);
            btnRow.Controls.Add(del);

            card.Controls.Add(list);     // Fill added first
            card.Controls.Add(btnRow);
            card.Controls.Add(title);
            f.Controls.Add(card);
            f.AcceptButton = apply; f.CancelButton = cancel;

            if (f.ShowDialog(this) != DialogResult.OK || list.SelectedIndex < 0 || list.SelectedIndex >= templates.Count) return null;
            return templates[list.SelectedIndex];
        }

        private static string ConfidenceText(ColumnMapping c)
        {
            if (c.Source == MappingSource.Ai) return "AI";
            return c.Confidence switch
            {
                MappingConfidence.High => "High",
                MappingConfidence.Medium => "Medium",
                MappingConfidence.Low => "Low",
                _ => "—"
            };
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max) + "…");

        private sealed class FieldOption
        {
            public string Path { get; }
            public string Display { get; }
            public FieldOption(string path, string display) { Path = path; Display = display; }
        }
    }
}
