using System;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Auth
{
    /// <summary>Create a new account: username, password (+ confirm) and a password hint.</summary>
    public class CreateUserForm : Form
    {
        private readonly UserStore _store;
        private Guna2TextBox _user = null!, _pass = null!, _confirm = null!, _hint = null!;
        private Label _error = null!;

        public string CreatedUsername { get; private set; } = string.Empty;

        public CreateUserForm(UserStore store)
        {
            _store = store;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Create account";
            ClientSize = new Size(440, 600);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent; // centers over the sign-in window
            MaximizeBox = false; MinimizeBox = false;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            var card = GunaUi.Card(); card.Dock = DockStyle.Fill; card.Padding = new Padding(28, 24, 28, 24);

            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent, AutoSize = false };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _user = GunaUi.TextBox("Choose a username");
            _pass = GunaUi.TextBox("Choose a password"); _pass.UseSystemPasswordChar = true;
            _confirm = GunaUi.TextBox("Re-enter password"); _confirm.UseSystemPasswordChar = true;
            _hint = GunaUi.TextBox("Hint to help you recover it");
            _error = new Label { Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.Danger, TextAlign = ContentAlignment.MiddleLeft };

            var createBtn = GunaUi.Button("Create account", primary: true); createBtn.Dock = DockStyle.Fill; createBtn.Click += Create_Click;
            var cancel = GunaUi.Button("Cancel", primary: false); cancel.Dock = DockStyle.Fill; cancel.DialogResult = DialogResult.Cancel;

            int r = 0;
            void Row(Control c, int height, int gapBelow = 0)
            {
                tl.RowStyles.Add(new RowStyle(SizeType.Absolute, height + gapBelow));
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(0, 0, 0, gapBelow);
                tl.Controls.Add(c, 0, r++);
            }
            Label Lbl(string t) => new Label { Text = t, Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };

            var logoHost = new Panel { BackColor = Color.Transparent };
            var logo = new LogoBadge { Size = new Size(56, 56), Top = 2 };
            logoHost.Controls.Add(logo);
            logoHost.Resize += (s, e) => logo.Left = Math.Max(0, (logoHost.Width - logo.Width) / 2);
            Row(logoHost, 60, 6);
            Row(new Label { Text = "Create account", Dock = DockStyle.Fill, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleCenter }, LogicalToDeviceUnits(44), 10);
            Row(Lbl("Username"), 20);
            Row(_user, 38, 12);
            Row(Lbl("Password"), 20);
            Row(_pass, 38, 12);
            Row(Lbl("Confirm password"), 20);
            Row(_confirm, 38, 12);
            Row(Lbl("Password hint"), 20);
            Row(_hint, 38, 14);
            Row(_error, 24, 6);
            Row(createBtn, 46, 10);
            Row(cancel, 42);
            // filler row absorbs any leftover height so the buttons keep their size
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, r);

            card.Controls.Add(tl);
            Controls.Add(card);
            AcceptButton = createBtn; CancelButton = cancel;
            FormAnimator.FadeIn(this);
        }

        private void Create_Click(object? sender, EventArgs e)
        {
            if (_pass.Text != _confirm.Text) { _error.Text = "Passwords do not match."; return; }
            string? err = _store.CreateUser(_user.Text, _pass.Text, _hint.Text);
            if (err != null) { _error.Text = err; return; }
            CreatedUsername = _user.Text.Trim();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
