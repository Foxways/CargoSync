using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Auth;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Logging;
using OrganizationImportTool.Mapping;
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
        private Guna2WinProgressIndicator _busySpinner;
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
        }

        private void InitializeAi()
        {
            try
            {
                _aiSettings = AiSettings.Load();
                _aiUsage = new TokenUsageStore { Persist = _aiSettings.SaveTokenHistory };
                _aiRouter = new AiRouter(_aiSettings, _aiUsage);
                // Apply retention at startup: trim old logs + old token history.
                LogRetention.ApplyAll(_aiSettings.LogRetentionDays);
                _aiUsage.PurgeOlderThan(_aiSettings.TokenHistoryRetentionDays);
            }
            catch (Exception ex) { Logging.AppLog.Warn("AI initialization failed - continuing without AI", ex); }
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));  // input card
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // log card
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));   // status + progress
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));   // footer

            // ---- Header bar ----
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            var titleLbl = new Label { Text = "CargoSync", Dock = DockStyle.Fill, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft };
            aiSettingsBtn = GunaButton("⚙   AI Settings", primary: true);
            aiSettingsBtn.Dock = DockStyle.Fill;
            aiSettingsBtn.Margin = new Padding(0, 8, 0, 8);
            aiSettingsBtn.Click += AiSettingsBtn_Click;
            header.Controls.Add(titleLbl, 0, 0);
            header.Controls.Add(aiSettingsBtn, 1, 0);

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

            _busySpinner = new Guna2WinProgressIndicator { Size = new Size(30, 30), Visible = false, ProgressColor = AppleTheme.Accent };
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
                MessageBox.Show("Error loading clients: " + ex.Message);
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
                if (openFileDialog.ShowDialog() == DialogResult.OK)
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
                EAdaptorSetupForm addClientForm = new EAdaptorSetupForm();
                addClientForm.ShowDialog(); // Waits until the form is closed
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
                _aiRouter = new AiRouter(_aiSettings, _aiUsage);
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
            var result = MessageBox.Show("Are you sure you want to stop the upload?",
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
                MessageBox.Show("Error retrieving environment: " + ex.Message);
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
                MessageBox.Show("Error retrieving EAdaptor details: " + ex.Message);
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
            // Reentrancy guard: an in-flight import pumps the message queue (Application.DoEvents),
            // so without this a second click (e.g. after Stop re-enables the button) would start a
            // concurrent import on top of the first — duplicate sends + a disposed-CTS crash.
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
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            string clientName = clientBox.SelectedItem?.ToString() ?? clientId;
            ImportLog importLog = null;

            try
            {
                // 1) Read the file - ANY structure (xlsx/csv), no required headers.
                logBox.AppendText($"Reading {Path.GetFileName(selectedFilePath)} ...\r\n");
                var table = _readerFactory.Read(selectedFilePath);
                if (table.RowCount == 0)
                {
                    logBox.AppendText("No data rows found in the file.\r\n");
                    labelCounter.Text = "No data found !";
                    return;
                }
                logBox.AppendText($"Loaded {table.RowCount} rows, {table.ColumnCount} columns.\r\n");

                // Start a detailed per-run audit log in the client's configured log folder.
                importLog = new ImportLog(logDir, clientName);
                importLog.Header(clientName, details.Environment, details.URL, details.SenderID, selectedFilePath, table.RowCount, _currentUser.Username);
                if (importLog.Ok) logBox.AppendText($"Detailed log: {importLog.FilePath}\r\n");

                // 2) Auto-suggest header -> CargoWise field mapping (alias + fuzzy).
                var contract = GetContract();
                var suggester = new MappingSuggester(contract);
                var mapping = suggester.Suggest(table);

                // 2a) Self-learning memory: overlay what this client confirmed on previous uploads.
                // Highest trust, applied before AI so the AI only works on columns still unknown.
                var templateStore = new TemplateStore();
                var autoMemory = templateStore.GetAuto(clientId);
                if (autoMemory != null)
                {
                    int learned = TemplateMapper.ApplyLearned(autoMemory, table, contract, mapping);
                    if (learned > 0)
                    {
                        logBox.AppendText($"Recalled {learned} mapping(s) from this client's history — getting smarter every upload.\r\n");
                        importLog?.Note($"Self-learning: applied {learned} remembered column mapping(s).");
                    }
                }

                // 2b) Optional AI refinement of low-confidence / unmapped columns.
                if (_aiRouter?.IsConfigured == true)
                {
                    try
                    {
                        labelCounter.Text = "Asking AI to refine mapping...";
                        Application.DoEvents();
                        var advisor = new AiMappingAdvisor(_aiRouter, _aiSettings.UseAiForLowConfidenceOnly);
                        mapping = await advisor.RefineAsync(table, mapping, contract, token);
                        logBox.AppendText($"AI refined {advisor.LastChangedCount} column mapping(s) using {_aiRouter.Current.ProviderName}.\r\n");
                    }
                    catch (Exception aiEx)
                    {
                        logBox.AppendText($"AI refinement skipped: {aiEx.Message}\r\n");
                    }
                }

                // 3) MANDATORY user-validation gate - operator confirms/overrides every mapping.
                using (var mapForm = new MappingForm(contract, table, mapping, clientId, templateStore, _aiRouter))
                {
                    if (mapForm.ShowDialog(this) != DialogResult.OK)
                    {
                        logBox.AppendText("Mapping cancelled by user. Nothing was sent.\r\n");
                        labelCounter.Text = "Cancelled";
                        return;
                    }
                    mapping = mapForm.ConfirmedResult;
                }

                // 3b) Self-learning: remember exactly what the operator confirmed for this client,
                // folding it into the accumulating memory so the next file maps itself.
                // Skipped on a dry run — a preview must not mutate the client's learned memory.
                if (!dryRun)
                {
                    try
                    {
                        var updatedMemory = TemplateMapper.LearnFrom(mapping, autoMemory, clientId);
                        templateStore.SaveAuto(updatedMemory, DateTime.UtcNow.ToString("o"));
                        logBox.AppendText("Learned this mapping — future files from this client will auto-map.\r\n");
                        importLog?.Note("Self-learning: saved confirmed mapping to client memory.");
                    }
                    catch (Exception learnEx)
                    {
                        logBox.AppendText($"Could not save learned mapping: {learnEx.Message}\r\n");
                    }
                }

                var includedCols = mapping.Columns
                    .Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath))
                    .ToList();

                // Build each row's mapped values once - shared by dedup and data-cleaning pre-passes.
                var rowValues = table.Rows
                    .Select(r => new RowValues { RowNumber = r.RowNumber, Values = BuildRowValues(r, includedCols, mapping) })
                    .ToList();

                // Pre-flight scans (cheap, deterministic) feed the profile dashboard's risk picture.
                var dupGroups = new List<DuplicateGroup>();
                try
                {
                    var keys = rowValues.Select(rv => new OrgKey
                    {
                        RowNumber = rv.RowNumber,
                        Code = rv.Values.TryGetValue("orgHeader.code", out var c) ? c : string.Empty,
                        Name = rv.Values.TryGetValue("orgHeader.fullName", out var n) ? n : string.Empty,
                        Country = rv.Values.TryGetValue("orgAddressCollection[].countryCode.code", out var cc) ? cc : string.Empty,
                        City = rv.Values.TryGetValue("orgAddressCollection[].city", out var ct) ? ct : string.Empty
                    }).ToList();
                    var scanner = new DuplicateScanner();
                    dupGroups = scanner.Scan(keys);
                    if (scanner.NameMatchingLimited)
                        logBox.AppendText($"Dedup: large file ({rowValues.Count} rows) — used exact-code matching only (fuzzy name matching skipped for performance).\r\n");
                }
                catch (Exception dupEx) { logBox.AppendText($"Dedup scan skipped: {dupEx.Message}\r\n"); }
                int duplicateRowCount = dupGroups.Sum(g => g.Extras.Count());

                int cleaningPreviewCount = 0;
                try { cleaningPreviewCount = (await new DataCleaner().AnalyzeAsync(rowValues, contract, null, false, token)).Count; }
                catch (Exception cex) { Logging.AppLog.Warn("Cleaning preview count failed (profile dashboard will show 0)", cex); }

                // CargoWise feedback sync: how many of these rows were already imported for this client.
                int alreadySynced = 0;
                try
                {
                    var synced = new FeedbackStore().SyncedCodes(clientId);
                    if (synced.Count > 0)
                        alreadySynced = rowValues.Count(rv => rv.Values.TryGetValue("orgHeader.code", out var cd)
                            && !string.IsNullOrWhiteSpace(cd) && synced.Contains(cd));
                    if (alreadySynced > 0)
                        logBox.AppendText($"Sync: {alreadySynced} of {rowValues.Count} row(s) were already imported to CargoWise for this client (will update).\r\n");
                }
                catch (Exception sex) { Logging.AppLog.Warn("Sync-ledger pre-check failed (already-imported count unavailable)", sex); }

                // 3b) Data-profiling risk dashboard — a pre-flight data-health overview.
                try
                {
                    var report = new DataProfiler().Profile(rowValues, contract, duplicateRowCount, cleaningPreviewCount, alreadySynced);
                    logBox.AppendText($"Profile: {report.RowCount} rows, risk {report.Level} (score {report.Score}/100).\r\n");
                    importLog?.Note($"Profile: risk {report.Level} score {report.Score}; blocking {report.BlockingRows}, dupes {report.DuplicateRows}, warnings {report.WarningRows}.");
                    using (var dash = new ProfileDashboardForm(report))
                    {
                        if (dash.ShowDialog(this) != DialogResult.OK)
                        {
                            logBox.AppendText("Import cancelled at data profile. Nothing was sent.\r\n");
                            labelCounter.Text = "Cancelled";
                            return;
                        }
                    }
                }
                catch (Exception profEx) { logBox.AppendText($"Profile skipped: {profEx.Message}\r\n"); }

                // 3c) Fuzzy dedup review.
                var skipRowNums = new HashSet<int>();
                var dupReasonByRow = new Dictionary<int, string>();
                if (dupGroups.Count > 0)
                {
                    int extras = dupGroups.Sum(g => g.Extras.Count());
                    logBox.AppendText($"Dedup: found {dupGroups.Count} possible duplicate group(s) covering {extras} extra row(s).\r\n");
                    importLog?.Note($"Dedup: {dupGroups.Count} possible duplicate group(s), {extras} extra row(s).");

                    using (var dlg = new DuplicateReviewForm(dupGroups))
                    {
                        if (dlg.ShowDialog(this) != DialogResult.OK)
                        {
                            logBox.AppendText("Import cancelled at duplicate review. Nothing was sent.\r\n");
                            labelCounter.Text = "Cancelled";
                            return;
                        }
                        if (dlg.SkipDuplicates)
                        {
                            skipRowNums = dlg.RowsToSkip;
                            foreach (var g in dupGroups)
                                foreach (var extra in g.Extras)
                                    dupReasonByRow[extra.RowNumber] = $"{g.Reason}; duplicate of row {g.Rows[0].RowNumber}";
                            logBox.AppendText($"Dedup: skipping {skipRowNums.Count} duplicate row(s); keeping the first of each group.\r\n");
                        }
                        else
                        {
                            logBox.AppendText("Dedup: operator chose to import all rows (duplicates included).\r\n");
                        }
                    }
                }

                // 3d) AI data cleaning + Auto-Fix: normalise values (and let AI resolve the rest) before sending.
                var cleanedByRow = new Dictionary<int, Dictionary<string, string>>();
                try
                {
                    // Only clean rows that will actually be sent (skip dedup-dropped rows).
                    var toClean = rowValues.Where(rv => !skipRowNums.Contains(rv.RowNumber)).ToList();
                    if (toClean.Count > 0)
                    {
                        labelCounter.Text = "Cleaning data...";
                        Application.DoEvents();
                        var changes = await new DataCleaner()
                            .AnalyzeAsync(toClean, contract, _aiRouter, _aiSettings.Enabled, token);
                        if (changes.Count > 0)
                        {
                            int aiCount = changes.Count(c => c.Source == CleanSource.Ai);
                            logBox.AppendText($"Data cleaning: {changes.Count} suggested fix(es){(aiCount > 0 ? $" ({aiCount} from AI)" : "")}.\r\n");
                            importLog?.Note($"Data cleaning: {changes.Count} suggested fix(es), {aiCount} from AI.");

                            using (var dlg = new DataCleaningForm(changes))
                            {
                                if (dlg.ShowDialog(this) != DialogResult.OK)
                                {
                                    logBox.AppendText("Import cancelled at data-cleaning review. Nothing was sent.\r\n");
                                    labelCounter.Text = "Cancelled";
                                    return;
                                }
                            }
                            cleanedByRow = DataCleaner.AcceptedOverrides(changes);
                            int applied = cleanedByRow.Sum(kv => kv.Value.Count);
                            logBox.AppendText($"Data cleaning: applying {applied} accepted fix(es).\r\n");
                        }
                    }
                }
                catch (Exception cleanEx)
                {
                    logBox.AppendText($"Data cleaning skipped: {cleanEx.Message}\r\n");
                }

                // 3e) Enrichment APIs: fill EMPTY fields from external sources (Postal API + AI).
                var enrichedByRow = new Dictionary<int, Dictionary<string, string>>();
                try
                {
                    // Enrich on the cleaned view (the Postal API needs the cleaned 2-letter country code).
                    var toEnrich = rowValues.Where(rv => !skipRowNums.Contains(rv.RowNumber))
                        .Select(rv =>
                        {
                            var v = new Dictionary<string, string>(rv.Values, StringComparer.OrdinalIgnoreCase);
                            if (cleanedByRow.TryGetValue(rv.RowNumber, out var fx)) foreach (var kv in fx) v[kv.Key] = kv.Value;
                            return new RowValues { RowNumber = rv.RowNumber, Values = v };
                        }).ToList();

                    if (toEnrich.Count > 0)
                    {
                        labelCounter.Text = "Enriching data...";
                        Application.DoEvents();
                        var suggestions = await new EnrichmentService(_aiRouter, _aiSettings.Enabled).RunAsync(toEnrich, contract, token);
                        if (suggestions.Count > 0)
                        {
                            int apiN = suggestions.Count(s => s.Source == "Postal API");
                            int aiN = suggestions.Count(s => s.Source == "AI");
                            logBox.AppendText($"Enrichment: {suggestions.Count} empty field(s) can be filled ({apiN} Postal API, {aiN} AI).\r\n");
                            importLog?.Note($"Enrichment: {suggestions.Count} suggestion(s) ({apiN} API, {aiN} AI).");

                            using (var dlg = new EnrichmentReviewForm(suggestions))
                            {
                                if (dlg.ShowDialog(this) != DialogResult.OK)
                                {
                                    logBox.AppendText("Import cancelled at enrichment review. Nothing was sent.\r\n");
                                    labelCounter.Text = "Cancelled";
                                    return;
                                }
                            }
                            enrichedByRow = EnrichmentService.AcceptedOverrides(suggestions);
                            logBox.AppendText($"Enrichment: filling {enrichedByRow.Sum(kv => kv.Value.Count)} accepted field(s).\r\n");
                        }
                    }
                }
                catch (Exception enrichEx)
                {
                    logBox.AppendText($"Enrichment skipped: {enrichEx.Message}\r\n");
                }

                logBox.AppendText(dryRun
                    ? $"Confirmed {includedCols.Count} field mappings. Simulating {table.RowCount} organizations (no send)...\r\n"
                    : $"Confirmed {includedCols.Count} field mappings. Sending {table.RowCount} organizations...\r\n");
                importLog?.Mapping(mapping.Columns, mapping.Constants);

                // 4) Build Native XML per row and submit to eAdaptor.
                string ownerCode = !string.IsNullOrWhiteSpace(details.Companycode)
                    ? details.Companycode.Trim()
                    : contract.OwnerCodeDefault;
                var builder = new OrganizationXmlBuilder(contract);
                var validator = new OrgValidator(contract);
                var client = new EadaptorClient(details.URL, details.SenderID, details.Password);
                var feedbackStore = new FeedbackStore();   // CargoWise feedback ledger
                var outcomes = new List<OrgSendOutcome>();

                int counter = 0;
                int throttleMs = 0; // adaptive inter-request delay; grows if CargoWise rate-limits
                foreach (var row in table.Rows)
                {
                    // Pause support: hold here until the user resumes (or stops).
                    while (_paused && !token.IsCancellationRequested)
                    {
                        labelCounter.Text = $"Paused at {counter}/{table.RowCount} — click Resume to continue";
                        Application.DoEvents();
                        await Task.Delay(200);
                    }
                    if (token.IsCancellationRequested)
                    {
                        logBox.AppendText($"Upload stopped by user at {counter}/{table.RowCount}.\r\n");
                        labelCounter.Text = "Stopped";
                        break;
                    }
                    counter++;
                    labelCounter.Text = $"Processing organization ({counter}/{table.RowCount})";
                    Application.DoEvents();

                    // Dedup: skip rows the operator chose to drop as duplicates of an earlier row.
                    if (skipRowNums.Contains(row.RowNumber))
                    {
                        string dupReason = dupReasonByRow.TryGetValue(row.RowNumber, out var dr) ? dr : "duplicate of an earlier row";
                        string dupCode = BuildRowValues(row, includedCols, mapping).TryGetValue("orgHeader.code", out var dcc) ? dcc : $"(row {row.RowNumber})";
                        var dupOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = dupCode, SentXml = string.Empty, Response = EadaptorResponse.SkippedDuplicate(dupReason) };
                        outcomes.Add(dupOutcome);
                        importLog?.Row(counter, dupOutcome, new List<string> { "skipped: " + dupReason });
                        logBox.AppendText($"  [{counter}] {dupCode}: SKIPPED (duplicate) - {dupReason}\r\n");
                        progressBar.Value = Math.Min(progressBar.Maximum, (int)(((double)counter / table.RowCount) * 100));
                        continue;
                    }

                    var values = BuildRowValues(row, includedCols, mapping);

                    // Apply the operator-accepted data-cleaning fixes for this row.
                    if (cleanedByRow.TryGetValue(row.RowNumber, out var fixes))
                        foreach (var fix in fixes)
                            values[fix.Key] = fix.Value;

                    // Apply accepted enrichment (fills empty fields from external sources).
                    if (enrichedByRow.TryGetValue(row.RowNumber, out var enrich))
                        foreach (var en in enrich)
                            values[en.Key] = en.Value;

                    // Apply the operator's no-code IF-THEN rules.
                    if (mapping.Rules.Count > 0)
                    {
                        var ruleHits = RuleEngine.Apply(mapping.Rules, row, values);
                        if (ruleHits.Count > 0)
                        {
                            logBox.AppendText($"  [{counter}] rule(s) applied: {string.Join(" | ", ruleHits)}\r\n");
                            importLog?.Note($"Row {row.RowNumber} rules: {string.Join(" | ", ruleHits)}");
                        }
                    }

                    // Inbuilt brain: derive values the client omitted but CargoWise needs
                    // (e.g. ClosestPort UN/LOCODE) - deterministically, with AI fallback when enabled.
                    var derived = await SmartDefaults.FillMissingAsync(values, _aiRouter, _aiSettings.Enabled);

                    string code = values.TryGetValue("orgHeader.code", out var cc) ? cc : $"(row {row.RowNumber})";
                    if (derived.Count > 0)
                        logBox.AppendText($"  [{counter}] {code}: auto-filled {string.Join("; ", derived)}\r\n");

                    // Pre-send validation: never POST a definitely-broken row to CargoWise.
                    var report = validator.Validate(values);
                    var warnList = report.Warnings.Select(w => $"{w.Label}: {w.Message}").ToList();
                    warnList.AddRange(derived.Select(d => "auto-filled " + d));
                    if (report.HasErrors)
                    {
                        var vr = EadaptorResponse.ValidationFailed(report.ErrorText);
                        if (dryRun) vr.Simulated = true; // preview label: "Would NOT send (validation)"
                        // include the would-be XML so the operator can inspect even a blocked row in a dry run
                        string failXml = dryRun ? SafeBuild(builder, values, ownerCode) : string.Empty;
                        var failOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = failXml, Response = vr };
                        outcomes.Add(failOutcome);
                        Logger.LogFailure($"{code} -> validation failed: {report.ErrorText}");
                        importLog?.Row(counter, failOutcome, warnList);
                        logBox.AppendText($"  [{counter}] {code}: {(dryRun ? "WOULD NOT SEND" : "NOT SENT")} - {report.ErrorText}\r\n");
                        progressBar.Value = Math.Min(progressBar.Maximum, (int)(((double)counter / table.RowCount) * 100));
                        continue;
                    }
                    foreach (var w in report.Warnings)
                        Logger.LogSuccess($"{code} warning - {w.Label}: {w.Message}");

                    string xml = builder.Build(values, ownerCode, enableCodeMapping: false);

                    // Dry run: build + record the would-be request, but never transmit it.
                    if (dryRun)
                    {
                        var simOutcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = xml, Response = EadaptorResponse.SimulatedOk(code) };
                        outcomes.Add(simOutcome);
                        importLog?.Row(counter, simOutcome, warnList);
                        logBox.AppendText($"  [{counter}] {code}: would send ✓ ({xml.Length} chars of Native XML built)\r\n");
                        progressBar.Value = Math.Min(progressBar.Maximum, (int)(((double)counter / table.RowCount) * 100));
                        continue;
                    }

                    var resp = await client.SendAsync(xml, token);
                    var outcome = new OrgSendOutcome { RowNumber = row.RowNumber, SentCode = code, SentXml = xml, Response = resp };
                    outcomes.Add(outcome);

                    // CargoWise feedback sync: record what CW told us back (sent -> stored code, PK, status).
                    try
                    {
                        feedbackStore.Record(new CwSyncEntry
                        {
                            ClientId = clientId, ClientName = clientName,
                            SentCode = code, StoredCode = resp.LocalCode, EntityPk = resp.EntityPk,
                            EntityName = resp.EntityName, Status = resp.Status, MessageNumber = resp.MessageNumber,
                            Username = _currentUser.Username, SyncedUtc = DateTime.UtcNow
                        });
                    }
                    catch (Exception fex) { Logging.AppLog.Warn($"Sync ledger record failed for '{code}' (resume detection may miss this row)", fex); }

                    if (resp.IsSuccess)
                        Logger.LogSuccess($"{code} -> {resp.Outcome} ({resp.LocalCode}) msg {resp.MessageNumber}");
                    else
                        Logger.LogFailure($"{code} -> {resp.Status}: {resp.Error}");

                    importLog?.Row(counter, outcome, warnList);
                    logBox.AppendText($"  [{counter}] {code}: {resp.Status} - {resp.Outcome}\r\n");
                    progressBar.Value = Math.Min(progressBar.Maximum, (int)(((double)counter / table.RowCount) * 100));

                    // Adaptive throttle: back off if CargoWise rate-limits / blocks, recover when clear.
                    if (resp.HttpStatus == 429 || resp.HttpStatus == 503 || (!resp.TransportOk && !resp.NotSent))
                        throttleMs = Math.Min(throttleMs == 0 ? 800 : throttleMs + 600, 5000);
                    else
                        throttleMs = Math.Max(0, throttleMs - 200);

                    if (throttleMs > 0)
                    {
                        labelCounter.Text = $"Sent {counter}/{table.RowCount} — easing off {throttleMs} ms to avoid blocking…";
                        Application.DoEvents();
                        await Task.Delay(throttleMs);
                    }
                }

                stopwatch.Stop();

                if (dryRun)
                {
                    int wouldSend = outcomes.Count(o => o.Response.IsSimulatedOk);
                    int blocked = outcomes.Count - wouldSend;
                    importLog?.Summary(outcomes.Count, wouldSend, 0, blocked, 0, stopwatch.Elapsed);
                    logBox.AppendText($"\r\nDry run complete. {wouldSend} would be sent, {blocked} blocked by validation, of {outcomes.Count}.\r\n");
                    logBox.AppendText("Nothing was transmitted to CargoWise. Review the preview, then click Upload to import for real.\r\n");
                    if (importLog?.Ok == true) logBox.AppendText($"Full details written to: {importLog.FilePath}\r\n");
                    labelCounter.Text = $"Dry run: {wouldSend}/{outcomes.Count} would send";
                }
                else
                {
                    int ok = outcomes.Count(o => o.Response.IsSuccess);
                    int warnCount = outcomes.Count(o => o.Response.IsWarning);
                    int notSentCount = outcomes.Count(o => o.Response.NotSent);
                    int rejected = outcomes.Count - ok - warnCount - notSentCount;
                    int failed = outcomes.Count - ok;
                    importLog?.Summary(outcomes.Count, ok, warnCount, notSentCount, rejected, stopwatch.Elapsed);

                    // Activity audit: record who imported what, for which client.
                    try
                    {
                        new ActivityStore().Record(_currentUser.Id, _currentUser.Username, clientName,
                            Path.GetFileName(selectedFilePath), outcomes.Count, ok, failed, notSentCount);
                    }
                    catch (Exception aex) { Logging.AppLog.Warn("Activity audit record failed", aex); }

                    logBox.AppendText($"\r\nDone. {ok} succeeded, {failed} failed of {outcomes.Count}.\r\n");
                    if (importLog?.Ok == true) logBox.AppendText($"Full details written to: {importLog.FilePath}\r\n");
                    labelCounter.Text = $"Complete: {ok}/{outcomes.Count} ok";
                }

                // 5) Professional response preview (titled "Dry Run" automatically when simulated).
                if (outcomes.Count > 0)
                    using (var preview = new ResponsePreviewForm(outcomes))
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
                importLog?.Dispose();
                SetBusy(false);
                cancellationTokenSource?.Dispose();
                cancellationTokenSource = null;
                _running = false;   // release the reentrancy guard only after the loop has truly exited
            }
        }

        /// <summary>Map one source row to CargoWise target-path values (column maps + constants), pre-derivation.</summary>
        private static Dictionary<string, string> BuildRowValues(SourceRow row, List<ColumnMapping> includedCols, MappingResult mapping)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in includedCols)
            {
                string v = row[col.SourceHeader];
                if (string.IsNullOrWhiteSpace(v)) continue;
                // apply any client value-map (code lookups / conditionals) before transform
                values[col.TargetPath!] = mapping.ApplyValueMap(col.TargetPath!, v.Trim());
            }
            // constants / defaults apply to every row
            foreach (var kv in mapping.Constants)
                if (!string.IsNullOrWhiteSpace(kv.Value))
                    values[kv.Key] = kv.Value;
            return values;
        }

        /// <summary>Build Native XML without throwing — used to preview a validation-blocked row in a dry run.</summary>
        private static string SafeBuild(OrganizationXmlBuilder builder, Dictionary<string, string> values, string ownerCode)
        {
            try { return builder.Build(values, ownerCode, enableCodeMapping: false); }
            catch { return string.Empty; }
        }
    }
}
