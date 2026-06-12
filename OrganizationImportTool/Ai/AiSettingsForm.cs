using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Logging;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Ai
{
    /// <summary>
    /// Apple-styled AI &amp; logging configuration: manage any number of providers (OpenAI,
    /// OpenRouter, Anthropic, local), set their fallback order, store API keys securely, test
    /// connectivity, view token-usage history, and control log/token retention.
    /// </summary>
    public class AiSettingsForm : Form
    {
        private readonly AiSettings _settings;
        private readonly TokenUsageStore _usage;
        private readonly List<AiProviderProfile> _providers;

        private bool _loading;

        // provider editor controls
        private ListBox _list = null!;
        private Guna2CheckBox _chkAiEnabled = null!;
        private Guna2TextBox _txtName = null!, _txtBaseUrl = null!, _txtModel = null!, _txtApiKey = null!;
        private Guna2ComboBox _cmbKind = null!;
        private Guna2NumericUpDown _numMaxTokens = null!, _numTemp = null!;
        private Guna2CheckBox _chkProviderEnabled = null!;
        private Label _lblTest = null!, _lblStatus = null!;

        // usage / retention controls
        private Guna2DataGridView _usageGrid = null!;
        private Label _lblTotalTokens = null!;
        private Guna2CheckBox _chkSaveTokens = null!, _chkLowConfidence = null!;
        private Guna2NumericUpDown _numLogRetention = null!, _numTokenRetention = null!, _numOpTimeout = null!;

        public AiSettingsForm(AiSettings settings, TokenUsageStore usage)
        {
            _settings = settings;
            _usage = usage;
            _providers = settings.Providers.Select(p => p.Clone()).ToList();
            BuildUi();
            LoadList();
            LoadGlobals();
        }

        private void BuildUi()
        {
            Text = "AI & Logging Settings";
            ClientSize = new Size(960, 680);
            MinimumSize = new Size(840, 600);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            DoubleBuffered = true;
            WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            // Header: title on the left, master toggle docked right (no magic pixel offsets, so it
            // stays put at any window width/DPI), and a one-line privacy note underneath.
            var header = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = AppleTheme.Canvas };
            var title = new Label
            {
                Text = "AI Assistance",
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 14, 0, 0),
                Font = AppleTheme.Title,
                ForeColor = AppleTheme.TextPrimary
            };

            _chkAiEnabled = GunaUi.Check("Enable AI assistance");
            _chkAiEnabled.AutoSize = true;
            _chkAiEnabled.Font = AppleTheme.Headline;
            _chkAiEnabled.ForeColor = AppleTheme.Accent;
            var togglePanel = new Panel { Dock = DockStyle.Right, Width = 260, BackColor = Color.Transparent, Padding = new Padding(0, 18, 20, 0) };
            _chkAiEnabled.Dock = DockStyle.Top;
            togglePanel.Controls.Add(_chkAiEnabled);

            var privacyNote = new Label
            {
                Text = "Privacy: when AI is on, column names and sample values from your file are sent to the selected provider. " +
                       "Turn AI off any time — every feature still works without it.",
                Dock = DockStyle.Bottom,
                Height = 34,
                Padding = new Padding(22, 0, 12, 4),
                Font = AppleTheme.Caption,
                ForeColor = AppleTheme.TextSecondary
            };
            header.Controls.Add(title);
            header.Controls.Add(togglePanel);
            header.Controls.Add(privacyNote);

            var tabs = new GunaTabs { Dock = DockStyle.Fill };
            tabs.AddTab("Providers & Fallback", BuildProvidersPanel());
            tabs.AddTab("Usage & Retention", BuildUsagePanel());

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = AppleTheme.Canvas, Padding = new Padding(16, 12, 16, 12) };
            var save = GunaUi.Button("Save", primary: true); save.Size = new Size(120, 38); save.Click += Save_Click;
            var cancel = GunaUi.Button("Cancel", primary: false); cancel.Size = new Size(110, 38); cancel.DialogResult = DialogResult.Cancel;
            var footRight = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            footRight.Controls.Add(save);
            footRight.Controls.Add(cancel);
            _lblStatus = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 10, 0, 0),
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary,
                Text = "AI idle"
            };
            footer.Controls.Add(_lblStatus);
            footer.Controls.Add(footRight);
            CancelButton = cancel;

            Controls.Add(tabs);
            Controls.Add(footer);
            Controls.Add(header);
            FormAnimator.FadeIn(this);
        }

        // ---------------- Providers tab ----------------
        private Control BuildProvidersPanel()
        {
            var host = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(12), BackColor = AppleTheme.Canvas };
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
            host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // ---- left: provider list + reorder/add/remove ----
            var leftCard = GunaUi.Card();
            leftCard.Dock = DockStyle.Fill; leftCard.Margin = new Padding(0, 0, 12, 0); leftCard.Padding = new Padding(12);
            var listLbl = new Label { Text = "Providers (top = tried first)", Dock = DockStyle.Top, Height = 24, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };
            var listBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 168, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(0, 8, 0, 0), BackColor = Color.Transparent };
            _list = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 44,
                Font = AppleTheme.Body, BackColor = AppleTheme.CardFill
            };
            _list.DrawItem += List_DrawItem;
            _list.SelectedIndexChanged += (s, e) => LoadEditor();

            // One "Add Provider" menu instead of per-vendor buttons - ANY provider is a template away,
            // and anything OpenAI-compatible (most of the market + local Ollama) works via Custom.
            var addBtn = MakeMiniButton("+ Add Provider ▾");
            addBtn.Width = 262;
            var addMenu = new ContextMenuStrip();
            void MenuItem(string label, Func<AiProviderProfile> template) =>
                addMenu.Items.Add(label, null, (s, e) => AddProvider(template()));
            MenuItem("OpenAI", AiProviderProfile.OpenAiTemplate);
            MenuItem("OpenRouter (free tier available)", AiProviderProfile.OpenRouterTemplate);
            MenuItem("Anthropic (Claude)", AiProviderProfile.AnthropicTemplate);
            MenuItem("Google Gemini", AiProviderProfile.GeminiTemplate);
            MenuItem("Groq", AiProviderProfile.GroqTemplate);
            MenuItem("DeepSeek", AiProviderProfile.DeepSeekTemplate);
            MenuItem("Mistral", AiProviderProfile.MistralTemplate);
            MenuItem("Ollama (local, no API key)", AiProviderProfile.OllamaTemplate);
            addMenu.Items.Add(new ToolStripSeparator());
            MenuItem("Custom (any OpenAI-compatible URL)…", AiProviderProfile.CustomTemplate);
            addBtn.Click += (s, e) => addMenu.Show(addBtn, new Point(0, addBtn.Height));

            var up = MakeMiniButton("↑ Up");
            var down = MakeMiniButton("↓ Down");
            var remove = MakeMiniButton("✕ Delete");
            remove.FillColor = AppleTheme.Danger;
            remove.ForeColor = Color.White;
            remove.HoverState.FillColor = Color.FromArgb(220, 50, 45);
            remove.Width = 262; // full-width delete row for clarity
            up.Click += (s, e) => Move(-1);
            down.Click += (s, e) => Move(1);
            remove.Click += (s, e) => RemoveSelected();
            listBtns.Controls.AddRange(new Control[] { addBtn, up, down, remove });

            leftCard.Controls.Add(_list);     // Fill - add last so it fills remaining
            leftCard.Controls.Add(listBtns);
            leftCard.Controls.Add(listLbl);

            // ---- right: editor (scrollable, auto-sized rows so nothing is cut off) ----
            var rightCard = GunaUi.Card();
            rightCard.Dock = DockStyle.Fill; rightCard.Padding = new Padding(14);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Color.Transparent };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 8; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            var fieldMargin = new Padding(0, 4, 0, 4);
            _txtName = GunaUi.TextBox(); _txtName.Dock = DockStyle.Fill; _txtName.Margin = fieldMargin;
            _cmbKind = GunaUi.Combo(); _cmbKind.Dock = DockStyle.Fill; _cmbKind.Margin = fieldMargin;
            _cmbKind.Items.AddRange(new object[] { "OpenAI-compatible (OpenAI, OpenRouter, local)", "Anthropic (Claude)" });
            _txtBaseUrl = GunaUi.TextBox(); _txtBaseUrl.Dock = DockStyle.Fill; _txtBaseUrl.Margin = fieldMargin;
            _txtModel = GunaUi.TextBox(); _txtModel.Dock = DockStyle.Fill; _txtModel.Margin = fieldMargin;
            _txtApiKey = GunaUi.TextBox(); _txtApiKey.Dock = DockStyle.Fill; _txtApiKey.Margin = fieldMargin; _txtApiKey.UseSystemPasswordChar = true;
            _numMaxTokens = GunaUi.Numeric(); _numMaxTokens.Dock = DockStyle.Fill; _numMaxTokens.Margin = fieldMargin; _numMaxTokens.Minimum = 16; _numMaxTokens.Maximum = 32000; _numMaxTokens.Value = 1024;
            _numTemp = GunaUi.Numeric(); _numTemp.Dock = DockStyle.Fill; _numTemp.Margin = fieldMargin; _numTemp.Minimum = 0; _numTemp.Maximum = 2; _numTemp.Value = 0;
            _chkProviderEnabled = GunaUi.Check("Enabled (included in fallback chain)");

            _txtName.TextChanged += (s, e) => { CaptureEditor(); _list.Invalidate(); };
            _cmbKind.SelectedIndexChanged += (s, e) => CaptureEditor();
            _txtBaseUrl.TextChanged += (s, e) => CaptureEditor();
            _txtModel.TextChanged += (s, e) => { CaptureEditor(); _list.Invalidate(); };
            _txtApiKey.TextChanged += (s, e) => CaptureEditor();
            _numMaxTokens.ValueChanged += (s, e) => CaptureEditor();
            _numTemp.ValueChanged += (s, e) => CaptureEditor();
            _chkProviderEnabled.CheckedChanged += (s, e) => { CaptureEditor(); _list.Invalidate(); };

            int r = 0;
            AddRow(grid, r++, "Name", _txtName);
            AddRow(grid, r++, "Type", _cmbKind);
            AddRow(grid, r++, "Base URL", _txtBaseUrl);
            AddRow(grid, r++, "Model", _txtModel);
            AddRow(grid, r++, "API Key", _txtApiKey);
            AddRow(grid, r++, "Max Tokens", _numMaxTokens);
            AddRow(grid, r++, "Temperature", _numTemp);
            AddRow(grid, r++, "", _chkProviderEnabled);

            var testRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = Color.Transparent };
            var testBtn = GunaUi.Button("Test Connection", primary: false); testBtn.Size = new Size(150, 34);
            testBtn.Click += TestBtn_Click;
            _lblTest = new Label { AutoSize = true, Padding = new Padding(10, 8, 0, 0), Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            testRow.Controls.Add(testBtn);
            testRow.Controls.Add(_lblTest);
            grid.Controls.Add(testRow, 0, r);
            grid.SetColumnSpan(testRow, 2);

            rightCard.Controls.Add(grid);

            host.Controls.Add(leftCard, 0, 0);
            host.Controls.Add(rightCard, 1, 0);
            return host;
        }

        // ---------------- Usage / Retention tab ----------------
        private Control BuildUsagePanel()
        {
            var host = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = AppleTheme.Canvas };

            var retentionCard = GunaUi.Card(); retentionCard.Dock = DockStyle.Top; retentionCard.Height = 248; retentionCard.Margin = new Padding(0, 0, 0, 12);
            var rlbl = new Label { Text = "Logging & Retention", Dock = DockStyle.Top, Height = 28, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };
            var rgrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
            rgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            rgrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) rgrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            _numLogRetention = GunaUi.Numeric(); _numLogRetention.Width = 120; _numLogRetention.Minimum = 0; _numLogRetention.Maximum = 3650; _numLogRetention.Value = 30;
            _numTokenRetention = GunaUi.Numeric(); _numTokenRetention.Width = 120; _numTokenRetention.Minimum = 0; _numTokenRetention.Maximum = 3650; _numTokenRetention.Value = 90;
            _numOpTimeout = GunaUi.Numeric(); _numOpTimeout.Width = 120; _numOpTimeout.Minimum = 5; _numOpTimeout.Maximum = 120; _numOpTimeout.Value = 20;
            _chkSaveTokens = GunaUi.Check("Save token-usage history to disk");
            _chkLowConfidence = GunaUi.Check("Use AI only for low-confidence columns (cheaper)");

            AddRow(rgrid, 0, "Delete logs older than (days, 0 = keep)", _numLogRetention);
            AddRow(rgrid, 1, "Delete token history older than (days)", _numTokenRetention);
            AddRow(rgrid, 2, "Max seconds per AI call during an import", _numOpTimeout);
            AddRow(rgrid, 3, "", _chkSaveTokens);
            AddRow(rgrid, 4, "", _chkLowConfidence);
            retentionCard.Controls.Add(rgrid);
            retentionCard.Controls.Add(rlbl);

            var usageCard = GunaUi.Card(); usageCard.Dock = DockStyle.Fill;
            var ulbl = new Label { Text = "Token Usage History", Dock = DockStyle.Top, Height = 26, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };
            _lblTotalTokens = new Label { Dock = DockStyle.Top, Height = 24, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            _usageGrid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false, BorderStyle = BorderStyle.None,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            _usageGrid.Columns.Add("Provider", "Provider");
            _usageGrid.Columns.Add("Model", "Model");
            _usageGrid.Columns.Add("Calls", "Calls");
            _usageGrid.Columns.Add("In", "Input Tokens");
            _usageGrid.Columns.Add("Out", "Output Tokens");
            _usageGrid.Columns.Add("Total", "Total Tokens");
            AppleTheme.StyleGrid(_usageGrid);
            _usageGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

            var clearBtn = GunaUi.Button("Clear History", primary: false); clearBtn.Dock = DockStyle.Bottom; clearBtn.Height = 36;
            clearBtn.Click += (s, e) =>
            {
                if (MessageBox.Show(this, "Delete all token-usage history?", "Confirm",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                { _usage.Clear(); RefreshUsage(); }
            };

            usageCard.Controls.Add(_usageGrid);
            usageCard.Controls.Add(_lblTotalTokens);
            usageCard.Controls.Add(ulbl);
            usageCard.Controls.Add(clearBtn);

            host.Controls.Add(usageCard);
            host.Controls.Add(retentionCard);
            RefreshUsage();
            return host;
        }

        // ---------------- data plumbing ----------------
        private void LoadGlobals()
        {
            _loading = true;
            _chkAiEnabled.Checked = _settings.Enabled;
            _chkSaveTokens.Checked = _settings.SaveTokenHistory;
            _chkLowConfidence.Checked = _settings.UseAiForLowConfidenceOnly;
            _numLogRetention.Value = Clamp(_settings.LogRetentionDays, 0, 3650);
            _numTokenRetention.Value = Clamp(_settings.TokenHistoryRetentionDays, 0, 3650);
            _numOpTimeout.Value = Clamp(_settings.OperationTimeoutSeconds, 5, 120);
            _loading = false;
        }

        private void LoadList()
        {
            _list.Items.Clear();
            foreach (var p in _providers) _list.Items.Add(p);
            if (_providers.Count > 0) _list.SelectedIndex = 0;
            LoadEditor();
        }

        private AiProviderProfile? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _providers.Count
            ? _providers[_list.SelectedIndex] : null;

        private void LoadEditor()
        {
            var p = Selected;
            _loading = true;
            if (p == null)
            {
                _txtName.Text = _txtBaseUrl.Text = _txtModel.Text = _txtApiKey.Text = string.Empty;
                _cmbKind.SelectedIndex = -1;
                _chkProviderEnabled.Checked = false;
                _lblTest.Text = "Add a provider to begin  →";
                _lblTest.ForeColor = AppleTheme.TextSecondary;
                _loading = false;
                return;
            }

            _txtName.Text = p.Name;
            _cmbKind.SelectedIndex = p.Kind == AiProviderKind.Anthropic ? 1 : 0;
            _txtBaseUrl.Text = p.BaseUrl;
            _txtModel.Text = p.Model;
            _txtApiKey.Text = p.ApiKey;
            _numMaxTokens.Value = Clamp(p.MaxTokens, (int)_numMaxTokens.Minimum, (int)_numMaxTokens.Maximum);
            _numTemp.Value = (decimal)Math.Max(0, Math.Min(2, p.Temperature));
            _chkProviderEnabled.Checked = p.Enabled;
            _lblTest.Text = string.Empty;
            _loading = false;
        }

        private void CaptureEditor()
        {
            if (_loading) return;
            var p = Selected;
            if (p == null) return;
            p.Name = _txtName.Text.Trim();
            p.Kind = _cmbKind.SelectedIndex == 1 ? AiProviderKind.Anthropic : AiProviderKind.OpenAiCompatible;
            p.BaseUrl = _txtBaseUrl.Text.Trim();
            p.Model = _txtModel.Text.Trim();
            p.ApiKey = _txtApiKey.Text;
            p.MaxTokens = (int)_numMaxTokens.Value;
            p.Temperature = (double)_numTemp.Value;
            p.Enabled = _chkProviderEnabled.Checked;
        }

        private void AddProvider(AiProviderProfile p)
        {
            _providers.Add(p);
            _list.Items.Add(p);
            _list.SelectedIndex = _providers.Count - 1;
            LoadEditor();
        }

        private void RemoveSelected()
        {
            int i = _list.SelectedIndex;
            if (i < 0) { MessageBox.Show(this, "Select a provider in the list first.", "Delete provider", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            var p = _providers[i];
            if (MessageBox.Show(this, $"Delete the AI provider '{p.Name}'?", "Delete provider",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            _providers.RemoveAt(i);
            _list.Items.RemoveAt(i);
            if (_list.Items.Count > 0) _list.SelectedIndex = Math.Min(i, _list.Items.Count - 1);
            LoadEditor();
            _lblStatus.Text = "Provider removed — click Save to apply.";
            _lblStatus.ForeColor = AppleTheme.Warning;
        }

        private void Move(int delta)
        {
            int i = _list.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= _providers.Count) return;
            (_providers[i], _providers[j]) = (_providers[j], _providers[i]);
            LoadList2(j);
        }

        private void LoadList2(int select)
        {
            _list.Items.Clear();
            foreach (var p in _providers) _list.Items.Add(p);
            _list.SelectedIndex = select;
        }

        private async void TestBtn_Click(object? sender, EventArgs e)
        {
            CaptureEditor();
            var p = Selected;
            if (p == null) return;
            _lblTest.ForeColor = AppleTheme.TextSecondary;
            _lblTest.Text = "Testing…";
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var resp = await AiRouter.TestAsync(p, cts.Token);
                if (resp.Success)
                {
                    _lblTest.ForeColor = AppleTheme.Success;
                    _lblTest.Text = $"✓ Connected — {resp.TotalTokens} tokens, {resp.ElapsedMs} ms";
                }
                else
                {
                    _lblTest.ForeColor = AppleTheme.Danger;
                    _lblTest.Text = "✗ " + resp.Error;
                }
            }
            catch (Exception ex)
            {
                _lblTest.ForeColor = AppleTheme.Danger;
                _lblTest.Text = "✗ " + ex.Message;
            }
            RefreshUsage();
        }

        private void RefreshUsage()
        {
            _usageGrid.Rows.Clear();
            foreach (var t in _usage.TotalsByProviderModel())
                _usageGrid.Rows.Add(t.ProviderName, t.Model, t.Calls, t.InputTokens, t.OutputTokens, t.TotalTokens);
            _lblTotalTokens.Text = $"Total tokens used: {_usage.TotalTokens():N0}  across {_usage.All().Count:N0} calls";
        }

        private void Save_Click(object? sender, EventArgs e)
        {
            CaptureEditor();
            _settings.Enabled = _chkAiEnabled.Checked;
            _settings.Providers = _providers;
            _settings.SaveTokenHistory = _chkSaveTokens.Checked;
            _settings.UseAiForLowConfidenceOnly = _chkLowConfidence.Checked;
            _settings.LogRetentionDays = (int)_numLogRetention.Value;
            _settings.TokenHistoryRetentionDays = (int)_numTokenRetention.Value;
            _settings.OperationTimeoutSeconds = (int)_numOpTimeout.Value;

            try
            {
                _settings.Save();
                _usage.Persist = _settings.SaveTokenHistory;
                _usage.PurgeOlderThan(_settings.TokenHistoryRetentionDays);
                LogRetention.ApplyAll(_settings.LogRetentionDays);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save settings: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        }

        // ---------------- helpers ----------------
        private void List_DrawItem(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= _providers.Count) return;
            var p = _providers[e.Index];
            bool sel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            // dark theme: selected row = dark accent tint, never the light system highlight
            using var bg = new SolidBrush(sel ? Color.FromArgb(38, 70, 110) : AppleTheme.Surface);
            e.Graphics.FillRectangle(bg, e.Bounds);
            if (sel)
                using (var bar = new SolidBrush(AppleTheme.Accent))
                    e.Graphics.FillRectangle(bar, new Rectangle(e.Bounds.Left, e.Bounds.Top, 3, e.Bounds.Height));

            var nameColor = !p.Enabled ? AppleTheme.TextSecondary : (sel ? Color.White : AppleTheme.TextPrimary);
            using var nameBrush = new SolidBrush(nameColor);
            using var subBrush = new SolidBrush(sel ? Color.FromArgb(200, 215, 235) : AppleTheme.TextSecondary);
            string order = $"{e.Index + 1}.";
            e.Graphics.DrawString(order, AppleTheme.Caption, subBrush, e.Bounds.Left + 6, e.Bounds.Top + 6);
            e.Graphics.DrawString(p.Name + (p.Enabled ? "" : "  (off)"), AppleTheme.Headline, nameBrush, e.Bounds.Left + 30, e.Bounds.Top + 4);
            e.Graphics.DrawString(p.Model, AppleTheme.Caption, subBrush, e.Bounds.Left + 30, e.Bounds.Top + 24);
        }

        private static Guna2Button MakeMiniButton(string text)
        {
            var b = GunaUi.Button(text, primary: false);
            b.Size = new Size(128, 34);
            b.Margin = new Padding(3);
            b.Font = AppleTheme.Font(9f, FontStyle.Bold);
            return b;
        }

        private static void AddRow(TableLayoutPanel grid, int row, string label, Control field)
        {
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            grid.Controls.Add(lbl, 0, row);
            grid.Controls.Add(field, 1, row);
        }

        private static decimal Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));
    }
}
