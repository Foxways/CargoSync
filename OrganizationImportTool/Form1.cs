using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Auth;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Logging;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Security;
using OrganizationImportTool.Sync;
using OrganizationImportTool.Transform;
using OrganizationImportTool.Ui;
using OrganizationImportTool.Validation;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OrganizationImportTool
{
    public partial class Form1 : Form
    {
        private SQLiteConnection conn;
        private Guna2ComboBox clientBox;
        private Guna2Button browseBtn, uploadBtn, dryRunBtn, addClientBtn, cancelBtn, aiSettingsBtn, pauseBtn, cwSyncBtn; // Add a button to open Form2
        private AiStatusChip aiChip;
        private Panel _getStartedOverlay;
        private Guna2TextBox filePathBox;
        private volatile bool _paused;
        private volatile bool _running; // true while an import/dry-run loop is in flight (reentrancy guard)

        // Intelligent-mapping pipeline
        private readonly SourceReaderFactory _readerFactory = new SourceReaderFactory();
        private FieldContract? _contract;
        private AiSettings _aiSettings = new AiSettings();
        private TokenUsageStore _aiUsage = new TokenUsageStore();
        private AiRouter? _aiRouter;
      
        public TextBox logBox;
        public Guna2ProgressBar progressBar;
        private Spinner _busySpinner;
        private Label footerLabel;
        private string selectedFilePath = string.Empty;
        // Add a CancellationTokenSource to manage cancellation
        private CancellationTokenSource cancellationTokenSource;


        private Dictionary<string, string> clientEnvironments = new Dictionary<string, string>();
        private Label environmentLabel;
        private Label labelCounter;

        string clientId = string.Empty;
        string environment = string.Empty;
        string url = string.Empty;
        string senderId = string.Empty;
        string password = string.Empty;
        string EnterpriseID = string.Empty;
        string CompanyCode = string.Empty;



        private readonly User _currentUser;

        public Form1(User currentUser)
        {
            _currentUser = currentUser ?? new User { Id = 0, Username = "unknown" };
            InitializeComponent();
            InitializeDatabase();
            LoadClients();
            InitializeAi();
            this.Shown += (s, e) => MaybeRunWelcomeWizard();
        }

        /// <summary>First launch with no clients: run the guided setup instead of showing dead buttons.</summary>
        private void MaybeRunWelcomeWizard()
        {
            var prefs = AppPrefs.Load();
            if (clientBox.Items.Count == 0 && !prefs.WelcomeWizardCompleted)
                RunWelcomeWizard();
            UpdateEmptyState();
        }

        private void RunWelcomeWizard()
        {
            try
            {
                using var wiz = new Onboarding.WelcomeWizard(
                    () => { LoadClients(); return clientBox.Items.Count; }, _aiSettings);
                wiz.ShowDialog(this);

                var prefs = AppPrefs.Load();
                prefs.WelcomeWizardCompleted = true;   // finished OR skipped - never nag on every launch
                prefs.AiConsentShown = true;           // the wizard's AI page carries the privacy notice
                prefs.Save();

                RebuildAiRouter();                     // the wizard may have flipped AI on/off
                LoadClients();
                UpdateEmptyState();
            }
            catch (Exception ex) { Logging.AppLog.Error("Welcome wizard failed", ex); }
        }

        /// <summary>Show the friendly Get Started overlay while no CargoWise client is configured.</summary>
        private void UpdateEmptyState()
        {
            bool empty = clientBox.Items.Count == 0;
            if (_getStartedOverlay != null)
            {
                _getStartedOverlay.Visible = empty;
                if (empty) _getStartedOverlay.BringToFront();
            }
            if (empty && labelCounter != null)
                labelCounter.Text = "No CargoWise connection yet — click Get started.";
        }

        private void InitializeAi()
        {
            try
            {
                _aiSettings = AiSettings.Load();
                _aiUsage = new TokenUsageStore { Persist = _aiSettings.SaveTokenHistory };
                RebuildAiRouter();
                // Apply retention at startup: trim old logs + old token history.
                LogRetention.ApplyAll(_aiSettings.LogRetentionDays);
                _aiUsage.PurgeOlderThan(_aiSettings.TokenHistoryRetentionDays);
            }
            catch (Exception ex) { Logging.AppLog.Warn("AI initialization failed - continuing without AI", ex); }
        }

        /// <summary>(Re)create the AI router from the current settings and re-bind the header chip.</summary>
        private void RebuildAiRouter()
        {
            _aiRouter = new AiRouter(_aiSettings, _aiUsage);
            aiChip?.Bind(_aiSettings, _aiRouter);
        }

        /// <summary>One-click AI on/off from the header chip - everything works either way.</summary>
        private void ToggleAi()
        {
            try
            {
                _aiSettings.Enabled = !_aiSettings.Enabled;
                _aiSettings.Save();
                RebuildAiRouter();
            }
            catch (Exception ex)
            {
                Logging.AppLog.Warn("AI toggle failed", ex);
                MessageBox.Show(this, "Could not change the AI setting: " + ex.Message, "CargoSync",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeDatabase()
        {
            string dbPath = AppPaths.DbPath;
            conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
        }

        private void AddHoverEffect(Button btn, Color hoverColor, Color defaultColor)
        {
            btn.MouseEnter += (s, e) => btn.BackColor = hoverColor;
            btn.MouseLeave += (s, e) => btn.BackColor = defaultColor;
        }
        private void InitializeComponent()
        {
            this.Text = "CargoSync — CargoWise Organization Importer";
            this.MinimumSize = new Size(760, 600);
            this.ClientSize = new Size(880, 720);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);
            BuildResponsiveLayout();
            FormAnimator.FadeIn(this);
            FormAnimator.EnableFadeOnClose(this);
        }

        private void BuildResponsiveLayout()
        {
            this.DoubleBuffered = true;
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                BackColor = AppleTheme.Canvas,
                Padding = new Padding(20, 14, 20, 12)
            };
            // Absolute row heights are in 96-DPI logical units - scale them so text rows
            // (especially the footer) don't clip at 125%/150% display scaling.
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(54)));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(240)));  // input card
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                          // log card
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(48)));   // status + progress
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(26)));   // footer

            // ---- Header bar ----
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));      // AI status chip
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            var titleLbl = new Label { Text = "CargoSync", Dock = DockStyle.Fill, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft };
            aiChip = new AiStatusChip();
            aiChip.ToggleRequested += ToggleAi;
            aiChip.OpenSettingsRequested += () => AiSettingsBtn_Click(this, EventArgs.Empty);
            aiSettingsBtn = GunaButton("⚙   AI Settings", primary: true);
            aiSettingsBtn.Dock = DockStyle.Fill;
            aiSettingsBtn.Margin = new Padding(0, 8, 0, 8);
            aiSettingsBtn.Click += AiSettingsBtn_Click;
            header.Controls.Add(titleLbl, 0, 0);
            header.Controls.Add(aiChip, 1, 0);
            header.Controls.Add(aiSettingsBtn, 2, 0);

            // ---- Input card ----
            var inputCard = GunaCard();
            inputCard.Margin = new Padding(0, 6, 0, 6);
            inputCard.Padding = new Padding(18, 14, 18, 14);
            var formGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4, BackColor = Color.Transparent };
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            formGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            formGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

            var clientLbl = MakeFieldLabel("Client");
            clientBox = new Guna2ComboBox
            {
                Dock = DockStyle.Fill, Margin = new Padding(4, 7, 6, 7),
                FillColor = AppleTheme.ControlFill, BorderColor = AppleTheme.Hairline, BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body, ItemHeight = 28
            };
            addClientBtn = GunaButton("Add Client", primary: false);
            addClientBtn.Dock = DockStyle.Fill; addClientBtn.Margin = new Padding(4, 6, 0, 6);
            addClientBtn.Click += AddClientBtn_Click;

            environmentLabel = new Label { Text = "Environment: —", Dock = DockStyle.Fill, Font = AppleTheme.Headline, ForeColor = AppleTheme.Accent, TextAlign = ContentAlignment.MiddleLeft };

            var fileLbl = MakeFieldLabel("File");
            filePathBox = new Guna2TextBox
            {
                Dock = DockStyle.Fill, Margin = new Padding(4, 7, 6, 7), ReadOnly = true,
                FillColor = AppleTheme.ControlFill, BorderColor = AppleTheme.Hairline, BorderRadius = 8,
                ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body, PlaceholderText = "No file selected"
            };
            browseBtn = GunaButton("Browse", primary: false);
            browseBtn.Dock = DockStyle.Fill; browseBtn.Margin = new Padding(4, 6, 0, 6); browseBtn.Enabled = false;
            browseBtn.Click += BrowseBtn_Click;

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Margin = new Padding(0, 2, 0, 0) };
            uploadBtn = GunaButton("Upload", primary: true);
            uploadBtn.Size = new Size(140, 40); uploadBtn.Enabled = false; uploadBtn.Margin = new Padding(4, 2, 10, 2);
            dryRunBtn = GunaButton("Dry Run", primary: false);
            dryRunBtn.Size = new Size(120, 40); dryRunBtn.Enabled = false; dryRunBtn.Margin = new Padding(0, 2, 10, 2);
            pauseBtn = GunaButton("Pause", primary: false);
            pauseBtn.Size = new Size(110, 40); pauseBtn.Enabled = false; pauseBtn.Margin = new Padding(0, 2, 8, 2);
            cancelBtn = GunaButton("Stop", primary: false);
            cancelBtn.Size = new Size(110, 40); cancelBtn.Enabled = false; cancelBtn.Margin = new Padding(0, 2, 4, 2);
            cwSyncBtn = GunaButton("CW Sync", primary: false);
            cwSyncBtn.Size = new Size(120, 40); cwSyncBtn.Enabled = false; cwSyncBtn.Margin = new Padding(10, 2, 4, 2);
            uploadBtn.Click += UploadBtn_ClickAsync;
            dryRunBtn.Click += UploadBtn_ClickAsync;   // same pipeline, simulate only (decided by sender)
            pauseBtn.Click += PauseBtn_Click;
            cancelBtn.Click += CancelBtn_Click;
            cwSyncBtn.Click += CwSyncBtn_Click;
            actions.Controls.Add(uploadBtn);
            actions.Controls.Add(dryRunBtn);
            actions.Controls.Add(pauseBtn);
            actions.Controls.Add(cancelBtn);
            actions.Controls.Add(cwSyncBtn);

            formGrid.Controls.Add(clientLbl, 0, 0);
            formGrid.Controls.Add(clientBox, 1, 0);
            formGrid.Controls.Add(addClientBtn, 2, 0);
            formGrid.Controls.Add(environmentLabel, 1, 1);
            formGrid.Controls.Add(fileLbl, 0, 2);
            formGrid.Controls.Add(filePathBox, 1, 2);
            formGrid.Controls.Add(browseBtn, 2, 2);
            formGrid.Controls.Add(actions, 1, 3);
            formGrid.SetColumnSpan(actions, 2);
            inputCard.Controls.Add(formGrid);

            // First-run empty state: a clear call to action instead of a row of dead disabled buttons.
            _getStartedOverlay = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.CardFill, Visible = false };
            var gsInner = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            var gsTitle = new Label { Text = "Welcome to CargoSync 👋", Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
            var gsText = new Label { Text = "Connect your CargoWise system to start importing organizations.", Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, AutoSize = true, Margin = new Padding(0, 0, 0, 14) };
            var gsBtn = GunaButton("Get started", primary: true);
            gsBtn.Size = new Size(180, 44);
            gsBtn.Click += (s, e) => RunWelcomeWizard();
            gsInner.Controls.Add(gsTitle);
            gsInner.Controls.Add(gsText);
            gsInner.Controls.Add(gsBtn);
            _getStartedOverlay.Controls.Add(gsInner);
            void CenterGetStarted() => gsInner.Location = new Point(
                Math.Max(0, (_getStartedOverlay.Width - gsInner.Width) / 2),
                Math.Max(0, (_getStartedOverlay.Height - gsInner.Height) / 2));
            _getStartedOverlay.Resize += (s, e) => CenterGetStarted();
            gsInner.SizeChanged += (s, e) => CenterGetStarted();
            inputCard.Controls.Add(_getStartedOverlay);
            _getStartedOverlay.BringToFront();

            // ---- Log card ----
            var logCard = GunaCard();
            logCard.Margin = new Padding(0, 6, 0, 6);
            logCard.Padding = new Padding(12);
            logBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = AppleTheme.Surface,
                ForeColor = AppleTheme.TextPrimary,
                Font = new Font("Consolas", 9),
                Name = "logBox"
            };
            logCard.Controls.Add(logBox);

            // ---- Status + progress ----
            var statusPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 14));
            labelCounter = new Label { Text = "Ready", Dock = DockStyle.Fill, Font = AppleTheme.Headline, ForeColor = AppleTheme.Accent, TextAlign = ContentAlignment.MiddleLeft, Name = "labelCounter" };
            progressBar = new Guna2ProgressBar
            {
                Dock = DockStyle.Fill, Minimum = 0, Maximum = 100, Value = 0, Visible = false, Name = "progressBar",
                FillColor = Color.FromArgb(44, 44, 52), ProgressColor = AppleTheme.Accent, ProgressColor2 = AppleTheme.AccentHover,
                BorderRadius = 4, Margin = new Padding(0, 0, 0, 2)
            };
            statusPanel.Controls.Add(labelCounter, 0, 0);
            statusPanel.Controls.Add(progressBar, 0, 1);

            // ---- Footer ----
            footerLabel = new Label { Text = $"CargoSync   ·   by Kishan Manohar © 2026   ·   v1.0.0          Signed in as: {_currentUser.Username}", Dock = DockStyle.Fill, Font = AppleTheme.Caption, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(inputCard, 0, 1);
            root.Controls.Add(logCard, 0, 2);
            root.Controls.Add(statusPanel, 0, 3);
            root.Controls.Add(footerLabel, 0, 4);
            this.Controls.Add(root);

            _busySpinner = new Spinner { Size = new Size(30, 30), Visible = false };
            this.Controls.Add(_busySpinner);
            _busySpinner.BringToFront();
            void PosSpinner() => _busySpinner.Location = new Point(ClientSize.Width - _busySpinner.Width - 28, ClientSize.Height - _busySpinner.Height - 36);
            this.Resize += (s, e) => PosSpinner();
            this.Shown += (s, e) => PosSpinner();

            // ---- state wiring ----
            clientBox.SelectedIndexChanged += ClientBox_SelectedIndexChanged;
            clientBox.SelectedIndexChanged += (s, e) =>
            {
                uploadBtn.Enabled = clientBox.SelectedIndex >= 0;
                dryRunBtn.Enabled = clientBox.SelectedIndex >= 0;
                cwSyncBtn.Enabled = clientBox.SelectedIndex >= 0;
                browseBtn.Enabled = clientBox.SelectedItem != null;
                filePathBox.Text = string.Empty;
                logBox.Text = string.Empty;
                labelCounter.Text = "Ready";
                cancelBtn.Enabled = false;
            };
        }

        /// <summary>A modern rounded Guna button styled for our dark theme.</summary>
        private static Guna2Button GunaButton(string text, bool primary)
        {
            var b = new Guna2Button
            {
                Text = text,
                BorderRadius = 10,
                Font = AppleTheme.Font(10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                ForeColor = Color.White,
                Animated = true
            };
            if (primary)
            {
                b.FillColor = AppleTheme.Accent;
                b.HoverState.FillColor = AppleTheme.AccentHover;
            }
            else
            {
                b.FillColor = AppleTheme.SecondaryFill;
                b.ForeColor = AppleTheme.TextPrimary;
                b.BorderColor = AppleTheme.Hairline;
                b.BorderThickness = 1;
                b.HoverState.FillColor = AppleTheme.SecondaryHover;
                b.HoverState.BorderColor = AppleTheme.Hairline;
            }
            b.DisabledState.FillColor = Color.FromArgb(40, 40, 46);
            b.DisabledState.ForeColor = AppleTheme.TextSecondary;
            b.DisabledState.BorderColor = Color.FromArgb(50, 50, 58);
            return b;
        }

        /// <summary>A rounded Guna panel used as a dark "card".</summary>
        private static Guna2Panel GunaCard()
        {
            var p = new Guna2Panel
            {
                Dock = DockStyle.Fill,
                FillColor = AppleTheme.Surface,
                BorderColor = AppleTheme.Hairline,
                BorderThickness = 1,
                BorderRadius = 14
            };
            p.ShadowDecoration.Enabled = true;
            p.ShadowDecoration.Color = Color.Black;
            p.ShadowDecoration.Depth = 8;
            return p;
        }

        private static Label MakeFieldLabel(string text) => new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = AppleTheme.Body,
            ForeColor = AppleTheme.TextSecondary,
            TextAlign = ContentAlignment.MiddleLeft
        };

        private void LoadClients()
        {
            SQLiteDataAdapter da = null;
            DataTable dt = null;

            try
            {
                dt = new DataTable();
                da = new SQLiteDataAdapter("SELECT C.Id, C.Name FROM Clients C JOIN EAdaptors E ON E.ClientId = C.Id WHERE E.Id IS NOT NULL", conn);
                da.Fill(dt);

                clientBox.Items.Clear();
                clientEnvironments.Clear();

                foreach (DataRow row in dt.Rows)
                {
                    string clientId = row["Id"].ToString();
                    string name = row["Name"].ToString();
                    clientBox.Items.Add(name);
                    clientEnvironments[name] = clientId;  // Store clientId for later use
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error loading clients: " + ex.Message);
            }
            finally
            {
                // Clean up resources
                if (da != null) da.Dispose();
                if (dt != null) dt.Dispose();
            }
        }


        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = _readerFactory.FileDialogFilter;
                if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                {
                    filePathBox.Text = openFileDialog.FileName;
                    selectedFilePath = openFileDialog.FileName;  // Store the path
                     logBox.Text = string.Empty;
                     labelCounter.Text = ("Ready !");
                }
            }
        }



        // Event handler to open Form2
        private void AddClientBtn_Click(object sender, EventArgs e)
        {
            try
            {
                using var addClientForm = new EAdaptorSetupForm();
                addClientForm.ShowDialog(this); // owned: opens on this monitor, never behind us
                // Refresh the client list after closing the form
                LoadClients();
                clientId = string.Empty;
                environment = string.Empty;
                url = string.Empty;
                senderId = string.Empty;
                password = string.Empty;
                EnterpriseID = string.Empty;
                CompanyCode = string.Empty;
                filePathBox.Text = string.Empty; ;
                selectedFilePath = string.Empty; ;  // Store the path
                logBox.Text = string.Empty;
                labelCounter.Text = ("Ready !");
                environmentLabel.Text =string.Empty;
            }
            catch (Exception ex)
            {
                // Log the exception or show an error message
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AiSettingsBtn_Click(object sender, EventArgs e)
        {
            try
            {
                using var form = new AiSettingsForm(_aiSettings, _aiUsage);
                form.ShowDialog(this);
                // settings instance is mutated in place; rebuild the router so changes take effect
                RebuildAiRouter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open AI settings: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private FieldContract GetContract()
        {
            return _contract ??= FieldContract.Load();
        }

        private void PauseBtn_Click(object sender, EventArgs e)
        {
            _paused = !_paused;
            pauseBtn.Text = _paused ? "Resume" : "Pause";
        }

        private void SetBusy(bool busy)
        {
            clientBox.Enabled = !busy;
            addClientBtn.Enabled = !busy;
            browseBtn.Enabled = !busy;
            uploadBtn.Enabled = !busy;
            dryRunBtn.Enabled = !busy;
            cwSyncBtn.Enabled = !busy && clientBox.SelectedIndex >= 0;
            aiSettingsBtn.Enabled = !busy;
            cancelBtn.Enabled = busy;
            pauseBtn.Enabled = busy;
            if (!busy) { _paused = false; pauseBtn.Text = "Pause"; }
            progressBar.Visible = busy;
            if (!busy) progressBar.Value = 0;
            if (_busySpinner != null)
            {
                _busySpinner.Visible = busy;
                if (busy) _busySpinner.Start(); else _busySpinner.Stop();
                _busySpinner.BringToFront();
            }
        }

        private async Task UpdateProgressBarAsync(int targetProgress)
        {
            while (progressBar.Value < targetProgress)
            {
                progressBar.Value += 1;
                await Task.Delay(5); // Smaller = smoother but slower
                progressBar.Refresh();
            }
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(this, "Are you sure you want to stop the upload?",
                                         "Stop Confirmation",
                                         MessageBoxButtons.YesNo,
                                         MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                // ONLY signal cancellation. The loop observes the token and exits, and its finally
                // block (SetBusy(false)) re-enables the controls AFTER it has truly stopped. Re-enabling
                // Upload here would be premature — the loop is still running — and could start a second,
                // concurrent import via the message pump. Disable Stop/Pause to prevent double-clicks.
                cancellationTokenSource?.Cancel();
                cancelBtn.Enabled = false;
                pauseBtn.Enabled = false;
                _paused = false;
                labelCounter.Text = "Stopping…";
            }
        }
        private void ClientBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Clear selected file path when client changes
            selectedFilePath = string.Empty;
            filePathBox.Text = string.Empty; // Also clear the textbox visually
            string selectedClient = clientBox.SelectedItem?.ToString();
            if (selectedClient != null && clientEnvironments.ContainsKey(selectedClient))
            {
                clientId = clientEnvironments[selectedClient];

                // Query the EAdaptors table to get the Environment based on clientId
                string environment = GetEnvironmentForClient(clientId);
                environmentLabel.Text = $"Environment to upload : {environment}";
            }
            else
            {
                environmentLabel.Text = "Environment to upload : -- ";
            }
        }
        private string GetEnvironmentForClient(string clientId)
        {
            string environment = "--";  // Default value
            try
            {
                using (var cmd = new SQLiteCommand("SELECT Environment FROM EAdaptors WHERE ClientId = @clientId", conn))
                {
                    cmd.Parameters.AddWithValue("@clientId", clientId);
                    object result = cmd.ExecuteScalar();
                    if (result != DBNull.Value && result != null)
                    {
                        environment = result.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error retrieving environment: " + ex.Message);
            }
            return environment;
        }

        private (string Environment, string URL, string SenderID, string Password, string LogPath, string EnterpriseID, string Companycode) GetEAdaptorDetailsForClient(string clientId)
        {
            environment = "--";
            url = "--";
            senderId = "--";
            password = "--";
            string logPath = "--";

            try
            {
                using (var cmd = new SQLiteCommand("SELECT Environment, URL, SenderID, Password, LogPath, EnterpriseID,CompanyCode FROM EAdaptors WHERE ClientId = @clientId", conn))
                {
                    cmd.Parameters.AddWithValue("@clientId", clientId);
                    using (SQLiteDataReader reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            environment = reader["Environment"]?.ToString() ?? "--";
                            url = reader["URL"]?.ToString() ?? "--";
                            senderId = reader["SenderID"]?.ToString() ?? "--";
                            password = SecretProtector.Unprotect(reader["Password"]?.ToString());
                            logPath = reader["LogPath"]?.ToString() ?? "--";
                            EnterpriseID = reader["EnterpriseID"]?.ToString() ?? "--";
                            CompanyCode = reader["CompanyCode"]?.ToString() ?? "--";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error retrieving EAdaptor details: " + ex.Message);
            }

            return (environment, url, senderId, password, logPath,EnterpriseID,CompanyCode);
        }

        private void CwSyncBtn_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                MessageBox.Show(this, "Choose a client to view its CargoWise sync ledger.", "CargoWise Sync",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string clientName = clientBox.SelectedItem?.ToString() ?? clientId;
            try
            {
                var entries = new FeedbackStore().ForClient(clientId);
                using var f = new SyncViewerForm(clientName, entries);
                f.ShowDialog(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not open sync ledger: " + ex.Message, "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void UploadBtn_ClickAsync(object sender, EventArgs e)
        {
            // Reentrancy guard: a second click while an import is in flight (e.g. after Stop
            // re-enables the button) must never start a concurrent run on top of the first.
            if (_running) return;

            // Dry run = same pipeline (read, map, derive, validate, build XML) but never transmit.
            bool dryRun = ReferenceEquals(sender, dryRunBtn);

            progressBar.Value = 0;
            progressBar.Maximum = 100;
            logBox.Clear();
            labelCounter.Text = "Ready !";

            if (string.IsNullOrEmpty(clientId))
            {
                logBox.AppendText("Please choose the client first.\r\n");
                return;
            }
            if (string.IsNullOrEmpty(selectedFilePath))
            {
                logBox.AppendText("Please choose a file to upload.\r\n");
                return;
            }

            var details = GetEAdaptorDetailsForClient(clientId);
            string logDir = Directory.Exists(details.LogPath) ? details.LogPath : AppPaths.LogsDir;
            try { Logger.Setup(logDir); } catch (Exception lex) { Logging.AppLog.Warn($"Logger setup failed for '{logDir}'", lex); }
            try { LogRetention.Apply(logDir, _aiSettings.LogRetentionDays, "*.txt"); LogRetention.Apply(logDir, _aiSettings.LogRetentionDays, "*.log"); } catch (Exception rex) { Logging.AppLog.Warn("Log retention sweep failed", rex); }

            if (dryRun)
                logBox.AppendText("=== DRY RUN — simulating the import. Nothing will be sent to CargoWise. ===\r\n");
            logBox.AppendText($"CargoWise environment : {details.Environment}\r\n");
            logBox.AppendText($"eAdaptor URL          : {details.URL}\r\n");
            logBox.AppendText($"eAdaptor user/sender  : {details.SenderID}\r\n");
            logBox.AppendText("--------------------------------------------------------------\r\n");

            _running = true;
            SetBusy(true);
            cancellationTokenSource = new CancellationTokenSource();
            var token = cancellationTokenSource.Token;
            string clientName = clientBox.SelectedItem?.ToString() ?? clientId;

            try
            {
                // The whole import flow lives in ImportPipeline (shared with the --pipeline CLI
                // harness and the unit tests); this form only supplies the dialogs + progress UI.
                var contract = GetContract();
                string ownerCode = !string.IsNullOrWhiteSpace(details.Companycode)
                    ? details.Companycode.Trim()
                    : contract.OwnerCodeDefault;

                var pipeline = new ImportPipeline(
                    contract, _readerFactory, new TemplateStore(), new FeedbackStore(),
                    _aiRouter, _aiSettings,
                    new EadaptorClient(details.URL, details.SenderID, details.Password),
                    new WinFormsPipelineUi(this, logBox, labelCounter, progressBar, () => _paused, _aiRouter));

                var request = new PipelineRequest
                {
                    FilePath = selectedFilePath,
                    ClientId = clientId,
                    ClientName = clientName,
                    Username = _currentUser.Username,
                    OwnerCode = ownerCode,
                    DryRun = dryRun,
                    LearnMapping = !dryRun,
                    LogDir = logDir,
                    Environment = details.Environment,
                    Url = details.URL,
                    SenderId = details.SenderID
                };

                var result = await pipeline.RunAsync(request, token);

                // Activity audit: record who imported what, for which client (real uploads only).
                if (!dryRun && !result.Cancelled && result.Outcomes.Count > 0)
                {
                    try
                    {
                        new ActivityStore().Record(_currentUser.Id, _currentUser.Username, clientName,
                            Path.GetFileName(selectedFilePath), result.Outcomes.Count, result.Ok, result.Failed, result.NotSent);
                    }
                    catch (Exception aex) { Logging.AppLog.Warn("Activity audit record failed", aex); }
                }

                // Professional response preview (titled "Dry Run" automatically when simulated).
                if (result.Outcomes.Count > 0)
                    using (var preview = new ResponsePreviewForm(result.Outcomes))
                        preview.ShowDialog(this);
            }
            catch (OperationCanceledException)
            {
                logBox.AppendText("Upload stopped.\r\n");
                labelCounter.Text = "Stopped";
            }
            catch (Exception ex)
            {
                logBox.AppendText($"Error during upload: {ex.Message}\r\n");
                Logging.AppLog.Error("Upload pipeline failed", ex);
                try { Logger.LogFailure("Upload error", ex); } catch { /* per-client logger may be unavailable */ }
            }
            finally
            {
                SetBusy(false);
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                _running = false;   // release the reentrancy guard only after the run has truly exited
            }
        }
    }
}
