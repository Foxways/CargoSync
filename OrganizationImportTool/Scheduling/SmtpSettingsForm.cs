using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Configures the Outlook / Office 365 mailbox used for scheduled-import notifications:
    /// from address, auth (app-password or O365 OAuth2) and a one-click test send.
    /// </summary>
    public sealed class SmtpSettingsForm : Form
    {
        private readonly SmtpSettings _settings;

        private readonly Guna2CheckBox _enabled = GunaUi.Check("Enable email notifications");
        private readonly Guna2TextBox _host = GunaUi.TextBox("smtp.office365.com");
        private readonly Guna2NumericUpDown _port = GunaUi.Numeric();
        private readonly Guna2CheckBox _tls = GunaUi.Check("Use STARTTLS (recommended)");
        private readonly Guna2TextBox _from = GunaUi.TextBox("noreply@yourcompany.com");
        private readonly Guna2TextBox _fromName = GunaUi.TextBox("CargoSync");
        private readonly Guna2ComboBox _auth = GunaUi.Combo();
        private readonly Guna2TextBox _user = GunaUi.TextBox("mailbox@yourcompany.com");
        private readonly Guna2TextBox _password = GunaUi.TextBox("app password");
        private readonly Guna2TextBox _tenant = GunaUi.TextBox("tenant id");
        private readonly Guna2TextBox _clientId = GunaUi.TextBox("application (client) id");
        private readonly Guna2TextBox _clientSecret = GunaUi.TextBox("client secret");

        public SmtpSettingsForm(SmtpSettings settings)
        {
            _settings = settings ?? SmtpSettings.Load();

            Text = "CargoSync — Email (SMTP) Settings";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(580, 640);
            BackColor = AppleTheme.Canvas;
            ForeColor = AppleTheme.TextPrimary;
            Font = AppleTheme.Body;

            _password.UseSystemPasswordChar = true;
            _clientSecret.UseSystemPasswordChar = true;
            _port.Minimum = 1; _port.Maximum = 65535;
            _auth.Items.AddRange(new object[] { "App password (Basic auth)", "Microsoft 365 (OAuth2)" });
            _auth.SelectedIndexChanged += (s, e) => ToggleAuthRows();

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(18, 16, 18, 8),
                AutoScroll = true,
                BackColor = AppleTheme.Canvas
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            AddSpan(layout, _enabled);
            AddRow(layout, "SMTP host", _host);
            AddRow(layout, "Port", _port);
            AddSpan(layout, _tls);
            AddRow(layout, "From address", _from);
            AddRow(layout, "From name", _fromName);
            AddRow(layout, "Authentication", _auth);
            AddRow(layout, "Username", _user);
            AddRow(layout, "Password", _password);
            AddRow(layout, "Tenant ID", _tenant);
            AddRow(layout, "Client ID", _clientId);
            AddRow(layout, "Client secret", _clientSecret);

            var saveBtn = GunaUi.Button("Save", primary: true);
            saveBtn.Size = new Size(TextRenderer.MeasureText(saveBtn.Text, saveBtn.Font).Width + 60, 34);
            saveBtn.Click += (s, e) => Save();
            var testBtn = GunaUi.Button("Send test…", primary: false);
            testBtn.Size = new Size(TextRenderer.MeasureText(testBtn.Text, testBtn.Font).Width + 60, 34);
            testBtn.Click += async (s, e) => await SendTestAsync();
            var closeBtn = GunaUi.Button("Close", primary: false);
            closeBtn.Size = new Size(TextRenderer.MeasureText(closeBtn.Text, closeBtn.Font).Width + 60, 34);
            closeBtn.Click += (s, e) => Close();

            Controls.Add(layout);
            Controls.Add(GunaUi.ButtonBar(new Control[] { saveBtn, testBtn, closeBtn }));

            AppleTheme.ApplyWindow(this);
            LoadFromSettings();
            ToggleAuthRows();
            FormAnimator.FadeIn(this);
        }

        private void LoadFromSettings()
        {
            _enabled.Checked = _settings.Enabled;
            _host.Text = _settings.Host;
            _port.Value = Math.Min(_port.Maximum, Math.Max(_port.Minimum, _settings.Port));
            _tls.Checked = _settings.UseStartTls;
            _from.Text = _settings.FromAddress;
            _fromName.Text = _settings.FromDisplayName;
            _auth.SelectedIndex = _settings.AuthMode == SmtpAuthMode.OAuth2 ? 1 : 0;
            _user.Text = _settings.Username;
            _password.Text = _settings.Password;
            _tenant.Text = _settings.TenantId;
            _clientId.Text = _settings.ClientIdOAuth;
            _clientSecret.Text = _settings.ClientSecret;
        }

        private void ApplyToSettings()
        {
            _settings.Enabled = _enabled.Checked;
            _settings.Host = _host.Text.Trim();
            _settings.Port = (int)_port.Value;
            _settings.UseStartTls = _tls.Checked;
            _settings.FromAddress = _from.Text.Trim();
            _settings.FromDisplayName = string.IsNullOrWhiteSpace(_fromName.Text) ? "CargoSync" : _fromName.Text.Trim();
            _settings.AuthMode = _auth.SelectedIndex == 1 ? SmtpAuthMode.OAuth2 : SmtpAuthMode.BasicOrAppPassword;
            _settings.Username = _user.Text.Trim();
            _settings.Password = _password.Text;
            _settings.TenantId = _tenant.Text.Trim();
            _settings.ClientIdOAuth = _clientId.Text.Trim();
            _settings.ClientSecret = _clientSecret.Text;
        }

        private void ToggleAuthRows()
        {
            bool oauth = _auth.SelectedIndex == 1;
            SetRowEnabled(_user, !oauth);
            SetRowEnabled(_password, !oauth);
            SetRowEnabled(_tenant, oauth);
            SetRowEnabled(_clientId, oauth);
            SetRowEnabled(_clientSecret, oauth);
        }

        private static void SetRowEnabled(Control c, bool on)
        {
            c.Enabled = on;
            if (c.Tag is Label lbl) lbl.ForeColor = on ? AppleTheme.TextSecondary : AppleTheme.Hairline;
        }

        private void Save()
        {
            ApplyToSettings();
            _settings.Save();
            MessageBox.Show(this, "Email settings saved.", "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task SendTestAsync()
        {
            ApplyToSettings();
            _settings.Save(); // persist before testing — the next scheduled run must use these settings
            if (!_settings.IsConfigured(out var err))
            {
                MessageBox.Show(this, err, "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string to = Prompt.Show(this, "Send a test email to:", _settings.FromAddress);
            if (string.IsNullOrWhiteSpace(to)) return;

            var outcome = await new Notifier().SendTestAsync(_settings, to);
            if (outcome.Sent)
                MessageBox.Show(this, "Test email sent.", "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, "Test failed: " + outcome.Error, "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void AddRow(TableLayoutPanel t, string label, Control control)
        {
            var lbl = new Label { Text = label, ForeColor = AppleTheme.TextSecondary, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 4, 0, 4);
            control.Tag = lbl;
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            t.Controls.Add(lbl, 0, row);
            t.Controls.Add(control, 1, row);
        }

        private void AddSpan(TableLayoutPanel t, Control control)
        {
            control.Margin = new Padding(0, 8, 0, 4);
            int row = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            t.Controls.Add(control, 0, row);
            t.SetColumnSpan(control, 2);
        }
    }

    /// <summary>Tiny single-line input dialog (themed) used for quick prompts.</summary>
    internal static class Prompt
    {
        public static string Show(IWin32Window owner, string message, string defaultValue = "")
        {
            using var f = new Form
            {
                Text = "CargoSync", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false,
                Size = new Size(420, 170), BackColor = AppleTheme.Canvas, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body
            };
            var lbl = new Label { Text = message, Dock = DockStyle.Top, Height = 32, Padding = new Padding(16, 12, 16, 0), ForeColor = AppleTheme.TextSecondary };
            var box = GunaUi.TextBox();
            box.Text = defaultValue;
            box.Location = new Point(16, 50); box.Width = 376;
            var ok = GunaUi.Button("OK", primary: true); ok.DialogResult = DialogResult.OK; ok.Location = new Point(232, 96); ok.Size = new Size(74, 32);
            var cancel = GunaUi.Button("Cancel", primary: false); cancel.DialogResult = DialogResult.Cancel; cancel.Location = new Point(318, 96); cancel.Size = new Size(74, 32);
            f.Controls.Add(box); f.Controls.Add(lbl); f.Controls.Add(ok); f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            return f.ShowDialog(owner) == DialogResult.OK ? box.Text.Trim() : string.Empty;
        }
    }
}
