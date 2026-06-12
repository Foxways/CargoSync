using DocumentFormat.OpenXml.CustomProperties;
using Guna.UI2.WinForms;
using OrganizationImportTool.Security;
using OrganizationImportTool.Ui;
using System;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace OrganizationImportTool
{
    public partial class EAdaptorSetupForm : Form
    {
        private SQLiteConnection conn;
        public Guna2TextBox clientBox, urlBox, senderBox, passwordBox, logPathBox, enterpriseBox, companyCodeBox;
        private Guna2ComboBox envBox;
        private Guna2DataGridView grid;
        private int selectedAdaptorId = -1; // To store the ID of the selected EAdaptor entry for update
        private Spinner _testSpinner;
        private Label _testLbl;



        public EAdaptorSetupForm()
        {
            InitializeComponent();
            InitializeDatabase();
            LoadGrid();
        }

        private void InitializeDatabase()
        {
            string dbPath = AppPaths.DbPath;
            conn = new SQLiteConnection($"Data Source={dbPath};Version=3;");
            conn.Open();
        }
        private void InitializeComponent()
        {
            this.Text = "Client & eAdaptor Setup";
            this.MinimumSize = new Size(940, 600);
            this.ClientSize = new Size(1040, 660);
            this.StartPosition = FormStartPosition.CenterParent; // maximizes on the OWNER's monitor
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.DoubleBuffered = true;
            this.WindowState = FormWindowState.Maximized;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(16), BackColor = AppleTheme.Canvas };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 560));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // ---- left: form card ----
            var leftCard = GunaUi.Card(); leftCard.Dock = DockStyle.Fill; leftCard.Margin = new Padding(0, 0, 12, 0); leftCard.Padding = new Padding(16);

            var formTitle = new Label { Text = "Client Details", Dock = DockStyle.Top, Height = 30, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 54, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoScroll = false, BackColor = Color.Transparent, Padding = new Padding(0, 12, 0, 0) };
            var newBtn = GunaUi.Button("+ New", primary: false); newBtn.Size = new Size(92, 38); newBtn.Margin = new Padding(0, 0, 7, 0); newBtn.Click += NewBtn_Click;
            var saveBtn = GunaUi.Button("Save", primary: true); saveBtn.Size = new Size(92, 38); saveBtn.Margin = new Padding(0, 0, 7, 0); saveBtn.Click += SaveBtn_Click;
            var updateBtn = GunaUi.Button("Update", primary: false); updateBtn.Size = new Size(92, 38); updateBtn.Margin = new Padding(0, 0, 7, 0); updateBtn.Click += UpdateBtn_Click;
            var refreshBtn = GunaUi.Button("Refresh", primary: false); refreshBtn.Size = new Size(92, 38); refreshBtn.Margin = new Padding(0, 0, 7, 0); refreshBtn.Click += RefreshBtn_Click;
            var closeBtn = GunaUi.Button("Close", primary: false); closeBtn.Size = new Size(92, 38); closeBtn.DialogResult = DialogResult.Cancel;
            buttons.Controls.AddRange(new Control[] { newBtn, saveBtn, updateBtn, refreshBtn, closeBtn });
            CancelButton = closeBtn;

            // Test Connection row: proves URL + Sender ID + Password against the live eAdaptor
            // BEFORE saving, so a typo never becomes a mysterious failed import later.
            var testRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 0) };
            var testBtn = GunaUi.Button("Test Connection", primary: false); testBtn.Size = new Size(150, 38); testBtn.Margin = new Padding(0, 0, 10, 0);
            testBtn.Click += async (s, e) => await TestConnectionAsync();
            _testSpinner = GunaUi.Spinner(24); _testSpinner.Margin = new Padding(0, 7, 8, 0);
            _testLbl = new Label { AutoSize = true, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, Padding = new Padding(0, 10, 0, 0) };
            testRow.Controls.AddRange(new Control[] { testBtn, _testSpinner, _testLbl });

            var fg = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Color.Transparent };
            fg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            fg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            clientBox = GunaUi.TextBox("Client name");
            envBox = GunaUi.Combo();
            envBox.Items.AddRange(new object[] { "TST", "PRD", "DEV", "UAT", "ENT", "SYD", "STG" });
            urlBox = GunaUi.TextBox("https://...eAdaptor");
            senderBox = GunaUi.TextBox("eAdaptor user / sender id");
            passwordBox = GunaUi.TextBox("password");
            passwordBox.UseSystemPasswordChar = true;
            passwordBox.IconRight = Properties.Resources.eye_closed;
            passwordBox.IconRightSize = new Size(18, 18);
            bool pwVisible = false;
            passwordBox.IconRightClick += (s, e) =>
            {
                pwVisible = !pwVisible;
                passwordBox.UseSystemPasswordChar = !pwVisible;
                passwordBox.IconRight = pwVisible ? Properties.Resources.eye_open : Properties.Resources.eye_closed;
            };
            enterpriseBox = GunaUi.TextBox("Enterprise id (e.g. CGD)");
            companyCodeBox = GunaUi.TextBox("Owner / company code (optional)");
            logPathBox = GunaUi.TextBox("Folder for logs"); logPathBox.ReadOnly = true;

            var logCell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0, 3, 0, 3) };
            logCell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logCell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            var browseBtn = GunaUi.Button("Browse", primary: false); browseBtn.Dock = DockStyle.Fill; browseBtn.Margin = new Padding(6, 0, 0, 0); browseBtn.Click += BrowseBtn_Click;
            logPathBox.Dock = DockStyle.Fill; logPathBox.Margin = new Padding(0);
            logCell.Controls.Add(logPathBox, 0, 0);
            logCell.Controls.Add(browseBtn, 1, 0);

            int r = 0;
            void Field(string label, Control field, int rowHeight = 50)
            {
                fg.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
                fg.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft }, 0, r);
                field.Dock = DockStyle.Fill;
                if (field is Guna2TextBox || field is Guna2ComboBox) field.Margin = new Padding(0, 5, 0, 5);
                fg.Controls.Add(field, 1, r);
                r++;
            }
            Field("Client Name", clientBox);
            Field("Environment", envBox);
            Field("eAdaptor URL", urlBox);
            Field("Sender ID", senderBox);
            Field("Password", passwordBox);
            Field("Enterprise ID", enterpriseBox);
            Field("Company Code", companyCodeBox);
            Field("Log Folder", logCell, 54);

            leftCard.Controls.Add(testRow);
            leftCard.Controls.Add(buttons);
            leftCard.Controls.Add(fg);
            leftCard.Controls.Add(formTitle);

            // ---- right: saved clients grid ----
            var rightCard = GunaUi.Card(); rightCard.Dock = DockStyle.Fill; rightCard.Padding = new Padding(12);
            var gridTitle = new Label { Text = "Saved Clients  (click a row to edit)", Dock = DockStyle.Top, Height = 38, Font = AppleTheme.Headline, ForeColor = AppleTheme.TextPrimary, Padding = new Padding(0, 4, 0, 6) };
            grid = new Guna2DataGridView
            {
                Dock = DockStyle.Fill,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                ReadOnly = true, AllowUserToAddRows = false, RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, BorderStyle = BorderStyle.None
            };
            grid.CellContentClick += Grid_CellContentClick;
            AppleTheme.StyleGrid(grid);
            rightCard.Controls.Add(grid);
            rightCard.Controls.Add(gridTitle);

            root.Controls.Add(leftCard, 0, 0);
            root.Controls.Add(rightCard, 1, 0);
            this.Controls.Add(root);

            // Plain-language hints (the jargon here is the #1 onboarding hurdle).
            Tips.Set(this, clientBox, "Any name you like for this connection, e.g. \"Acme Logistics (Test)\".");
            Tips.Set(this, envBox, "TST = CargoWise test system (practice here). PRD = the real live system.");
            Tips.Set(this, urlBox, "The web address of your CargoWise eAdaptor inbound service — ask your CargoWise administrator. Usually ends in /eAdaptor.");
            Tips.Set(this, senderBox, "The eAdaptor account name (Sender ID) from your CargoWise administrator.");
            Tips.Set(this, passwordBox, "The eAdaptor account password. Stored encrypted on this computer.");
            Tips.Set(this, enterpriseBox, "Your CargoWise enterprise code, e.g. CGD.");
            Tips.Set(this, companyCodeBox, "Optional: which CargoWise company owns the imported organizations.");
            Tips.Set(this, logPathBox, "Where detailed import reports are saved for this client.");
            Tips.Set(this, testBtn, "Checks the URL and sign-in details against the live eAdaptor — nothing is imported.");
            Tips.Set(this, newBtn, "Clear the form to add another connection.");
            Tips.Set(this, saveBtn, "Save this connection as a new client.");
            Tips.Set(this, updateBtn, "Save changes to the client selected in the grid.");

            FormAnimator.FadeIn(this);
        }





        /// <summary>Probe the live eAdaptor with the values currently typed into the form.</summary>
        private async System.Threading.Tasks.Task TestConnectionAsync()
        {
            string url = urlBox.Text.Trim(), sender = senderBox.Text.Trim(), pass = passwordBox.Text.Trim();
            if (url.Length == 0 || sender.Length == 0 || pass.Length == 0)
            {
                _testLbl.ForeColor = AppleTheme.Warning;
                _testLbl.Text = "Fill in the eAdaptor URL, Sender ID and Password first.";
                return;
            }

            _testLbl.ForeColor = AppleTheme.TextSecondary;
            _testLbl.Text = "Contacting CargoWise…";
            _testSpinner.Start();
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30));
                var client = new Eadaptor.EadaptorClient(url, sender, pass);
                var resp = await client.TestConnectionAsync(cts.Token);

                if (resp.TransportOk)
                {
                    _testLbl.ForeColor = AppleTheme.Success;
                    _testLbl.Text = "✓ Connected — CargoWise answered. URL and sign-in details work.";
                }
                else if (resp.HttpStatus is 401 or 403)
                {
                    _testLbl.ForeColor = AppleTheme.Danger;
                    _testLbl.Text = "✗ Reached the server, but sign-in failed — check the Sender ID and Password.";
                }
                else
                {
                    _testLbl.ForeColor = AppleTheme.Danger;
                    _testLbl.Text = "✗ Could not reach the eAdaptor: " + (resp.Error ?? $"HTTP {resp.HttpStatus}");
                }
            }
            catch (Exception ex)
            {
                _testLbl.ForeColor = AppleTheme.Danger;
                _testLbl.Text = "✗ " + ex.Message;
            }
            finally { _testSpinner.Stop(); }
        }

        private void SaveBtn_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(clientBox.Text) ||
                string.IsNullOrWhiteSpace(envBox.Text) ||
                string.IsNullOrWhiteSpace(urlBox.Text) ||
                string.IsNullOrWhiteSpace(senderBox.Text) ||
                string.IsNullOrWhiteSpace(passwordBox.Text) ||
                string.IsNullOrWhiteSpace(logPathBox.Text) ||
                string.IsNullOrWhiteSpace(enterpriseBox.Text)
                //||string.IsNullOrWhiteSpace(companyCodeBox.Text)
                )
            {
                MessageBox.Show(this, "Please fill in all required fields.");
                return;
            }

            try
            {
                // Check if the client already exists
                int clientId;
                using (var checkCmd = new SQLiteCommand("SELECT Id FROM Clients WHERE Name = @name", conn))
                {
                    checkCmd.Parameters.AddWithValue("@name", clientBox.Text.Trim());
                    var result = checkCmd.ExecuteScalar();
                    if (result != null)
                    {
                        MessageBox.Show(this, "Client name already exists. Please choose a different name.");
                        return;
                    }
                }

                // Insert new client
                using (var insertClientCmd = new SQLiteCommand("INSERT INTO Clients (Name) VALUES (@name)", conn))
                {
                    insertClientCmd.Parameters.AddWithValue("@name", clientBox.Text.Trim());
                    insertClientCmd.ExecuteNonQuery();
                }

                clientId = (int)conn.LastInsertRowId;

                // Insert into EAdaptors
                using (var cmd = new SQLiteCommand("INSERT INTO EAdaptors (ClientId, Environment, URL, SenderID, Password, LogPath, CompanyCode, EnterpriseID) VALUES (@clientId, @env, @url, @sender, @pass, @log, @CompanyCode, @EnterpriseID)", conn))
                {
                    cmd.Parameters.AddWithValue("@clientId", clientId);
                    cmd.Parameters.AddWithValue("@env", envBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@url", urlBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@sender", senderBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@pass", SecretProtector.Protect(passwordBox.Text.Trim()));
                    cmd.Parameters.AddWithValue("@log", logPathBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@CompanyCode", companyCodeBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@EnterpriseID", enterpriseBox.Text.Trim());
                    cmd.ExecuteNonQuery();
                }

                LoadGrid();
                MessageBox.Show(this, "Client saved successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error saving client data: " + ex.Message);
            }
        }


        private void UpdateBtn_Click(object sender, EventArgs e)
        {
            if (selectedAdaptorId == -1)
            {
                MessageBox.Show(this, "No entry selected for update.");
                return;
            }

            if (string.IsNullOrWhiteSpace(clientBox.Text) ||
                string.IsNullOrWhiteSpace(envBox.Text) ||
                string.IsNullOrWhiteSpace(urlBox.Text) ||
                string.IsNullOrWhiteSpace(senderBox.Text) ||
                string.IsNullOrWhiteSpace(passwordBox.Text) ||
                string.IsNullOrWhiteSpace(logPathBox.Text) ||
                string.IsNullOrWhiteSpace(enterpriseBox.Text))
            {
                MessageBox.Show(this, "Please fill in all required fields.");
                return;
            }

            try
            {
                // Step 1: Get current ClientId for the selected adaptor
                int clientId = -1;
                using (var getClientIdCmd = new SQLiteCommand("SELECT ClientId FROM EAdaptors WHERE Id = @id", conn))
                {
                    getClientIdCmd.Parameters.AddWithValue("@id", selectedAdaptorId);
                    var result = getClientIdCmd.ExecuteScalar();
                    if (result != null)
                        clientId = Convert.ToInt32(result);
                }

                if (clientId == -1)
                {
                    MessageBox.Show(this, "Client not found for selected adaptor.");
                    return;
                }

                // Step 2: Get current client name
                string currentClientName = null;
                using (var getClientNameCmd = new SQLiteCommand("SELECT Name FROM Clients WHERE Id = @id", conn))
                {
                    getClientNameCmd.Parameters.AddWithValue("@id", clientId);
                    currentClientName = getClientNameCmd.ExecuteScalar()?.ToString();
                }

                if (currentClientName == null)
                {
                    MessageBox.Show(this, "Client record missing.");
                    return;
                }

                string newClientName = clientBox.Text.Trim();

                // Step 3: If the name has changed, check for duplicates
                if (!string.Equals(currentClientName, newClientName, StringComparison.OrdinalIgnoreCase))
                {
                    using (var checkDuplicateCmd = new SQLiteCommand("SELECT Id FROM Clients WHERE Name = @name AND Id != @currentId", conn))
                    {
                        checkDuplicateCmd.Parameters.AddWithValue("@name", newClientName);
                        checkDuplicateCmd.Parameters.AddWithValue("@currentId", clientId);
                        var duplicateResult = checkDuplicateCmd.ExecuteScalar();

                        if (duplicateResult != null)
                        {
                            MessageBox.Show(this, "A different client with this name already exists. Please choose another name.");
                            return;
                        }
                    }

                    // Step 4: Update client name
                    using (var updateNameCmd = new SQLiteCommand("UPDATE Clients SET Name = @name WHERE Id = @id", conn))
                    {
                        updateNameCmd.Parameters.AddWithValue("@name", newClientName);
                        updateNameCmd.Parameters.AddWithValue("@id", clientId);
                        updateNameCmd.ExecuteNonQuery();
                    }
                }

                // Step 5: Update EAdaptor record
                using (var cmd = new SQLiteCommand(@"UPDATE EAdaptors 
                                             SET Environment = @env, URL = @url, SenderID = @sender, 
                                                 Password = @pass, LogPath = @log, CompanyCode = @CompanyCode, 
                                                 EnterpriseID = @EnterpriseID 
                                             WHERE Id = @id", conn))
                {
                    cmd.Parameters.AddWithValue("@env", envBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@url", urlBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@sender", senderBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@pass", SecretProtector.Protect(passwordBox.Text.Trim()));
                    cmd.Parameters.AddWithValue("@log", logPathBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@CompanyCode", companyCodeBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@EnterpriseID", enterpriseBox.Text.Trim());
                    cmd.Parameters.AddWithValue("@id", selectedAdaptorId);
                    cmd.ExecuteNonQuery();
                }

                LoadGrid();
                MessageBox.Show(this, "Updated successfully.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error updating data: " + ex.Message);
            }
        }





        private void LoadGrid()
        {
            try
            {
                var dt = new DataTable();
                var da = new SQLiteDataAdapter("SELECT E.Id, C.Name as Client, E.Environment, E.URL, E.SenderID, Password, E.LogPath,E.CompanyCode,E.EnterpriseID FROM EAdaptors E JOIN Clients C ON E.ClientId = C.Id", conn);
                da.Fill(dt);

                grid.Columns.Clear();
                grid.DataSource = dt;

                // Hide the ID column
                if (grid.Columns.Contains("Id"))
                {
                    grid.Columns["Id"].Visible = false;
                }

                // Hide the encrypted Password column from view
                if (grid.Columns.Contains("Password"))
                {
                    grid.Columns["Password"].Visible = false;
                }

                if (!grid.Columns.Contains("Delete"))
                {
                    DataGridViewButtonColumn deleteBtn = new DataGridViewButtonColumn
                    {
                        Name = "Delete",
                        HeaderText = "Delete",
                        Text = "Delete",
                        UseColumnTextForButtonValue = true
                    };
                    grid.Columns.Add(deleteBtn);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Error loading grid: " + ex.Message);
            }
        }

        private void Grid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            DataGridViewRow row = grid.Rows[e.RowIndex];

            if (e.ColumnIndex == grid.Columns["Delete"].Index)
            {
                int id = Convert.ToInt32(row.Cells["Id"].Value);

                DialogResult result = MessageBox.Show(this, "Are you sure you want to delete this record and its associated client?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (result == DialogResult.Yes)
                {
                    try
                    {
                        int clientId = -1;

                        // Get the clientId linked to this adaptor
                        using (var getClientIdCmd = new SQLiteCommand("SELECT ClientId FROM EAdaptors WHERE Id = @id", conn))
                        {
                            getClientIdCmd.Parameters.AddWithValue("@id", id);
                            var clientIdResult = getClientIdCmd.ExecuteScalar();
                            if (clientIdResult != null)
                                clientId = Convert.ToInt32(clientIdResult);
                        }

                        // Delete the EAdaptor
                        using (var deleteAdaptorCmd = new SQLiteCommand("DELETE FROM EAdaptors WHERE Id = @id", conn))
                        {
                            deleteAdaptorCmd.Parameters.AddWithValue("@id", id);
                            deleteAdaptorCmd.ExecuteNonQuery();
                        }

                        // Always delete the associated client (even if used by other adaptors)
                        if (clientId != -1)
                        {
                            using (var deleteClientCmd = new SQLiteCommand("DELETE FROM Clients WHERE Id = @clientId", conn))
                            {
                                deleteClientCmd.Parameters.AddWithValue("@clientId", clientId);
                                deleteClientCmd.ExecuteNonQuery();
                            }
                        }

                        if (selectedAdaptorId == id)
                            selectedAdaptorId = -1;

                        LoadGrid();
                        MessageBox.Show(this, "Adaptor and client deleted successfully.", "Deleted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "Error deleting data: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
            else
            {
                // Populate form fields for update
                clientBox.Text = row.Cells["Client"].Value?.ToString();
                envBox.SelectedItem = row.Cells["Environment"].Value?.ToString();
                urlBox.Text = row.Cells["URL"].Value?.ToString();
                senderBox.Text = row.Cells["SenderID"].Value?.ToString();
                passwordBox.Text = SecretProtector.Unprotect(row.Cells["Password"].Value?.ToString());
                logPathBox.Text = row.Cells["LogPath"].Value?.ToString();
                companyCodeBox.Text = row.Cells["CompanyCode"].Value?.ToString();
                enterpriseBox.Text = row.Cells["EnterpriseID"].Value?.ToString();
                selectedAdaptorId = Convert.ToInt32(row.Cells["Id"].Value);
            }
        }



        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                if (folderDialog.ShowDialog(this) == DialogResult.OK)
                {
                    logPathBox.Text = folderDialog.SelectedPath;
                }
            }
        }

        private void RefreshBtn_Click(object sender, EventArgs e)
        {
            // Refresh the grid by reloading the data
            LoadGrid();
        }

        // "+ New" - clear the form to enter a brand-new client
        private void NewBtn_Click(object sender, EventArgs e)
        {
            selectedAdaptorId = -1;
            clientBox.Text = string.Empty;
            envBox.SelectedIndex = -1;
            urlBox.Text = string.Empty;
            senderBox.Text = string.Empty;
            passwordBox.Text = string.Empty;
            enterpriseBox.Text = string.Empty;
            companyCodeBox.Text = string.Empty;
            logPathBox.Text = string.Empty;
            if (grid != null) grid.ClearSelection();
            clientBox.Focus();
        }
    }
}
