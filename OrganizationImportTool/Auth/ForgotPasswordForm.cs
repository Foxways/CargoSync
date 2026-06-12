using System;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Auth
{
    /// <summary>
    /// Recover access WITH verification: either prove the current password, or have an
    /// administrator approve the reset. (Anyone-can-reset-anyone was an account-takeover hole.)
    /// The password hint stays available as a memory jogger.
    /// </summary>
    public class ForgotPasswordForm : Form
    {
        private readonly UserStore _store;
        private Guna2TextBox _user = null!, _verify1 = null!, _verify2 = null!, _newPass = null!, _confirm = null!;
        private Label _hintLbl = null!, _msg = null!, _verify1Lbl = null!, _verify2Lbl = null!;
        private Guna2ComboBox _mode = null!;

        public ForgotPasswordForm(UserStore store)
        {
            _store = store;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Forgot password";
            ClientSize = new Size(460, 660);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            var card = GunaUi.Card(); card.Dock = DockStyle.Fill; card.Padding = new Padding(28, 24, 28, 24);

            var tl = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, BackColor = Color.Transparent };
            tl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _user = GunaUi.TextBox("Your username");
            var showHint = GunaUi.Button("Show my hint", primary: false); showHint.Dock = DockStyle.Fill; showHint.Click += ShowHint_Click;
            _hintLbl = new Label { Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.Accent, TextAlign = ContentAlignment.MiddleLeft };

            _mode = GunaUi.Combo();
            _mode.Items.AddRange(new object[]
            {
                "I know my current password",
                "An administrator is with me",
            });
            _mode.SelectedIndex = 0;
            _mode.SelectedIndexChanged += (s, e) => ApplyMode();

            _verify1 = GunaUi.TextBox(""); _verify1.UseSystemPasswordChar = true;
            _verify2 = GunaUi.TextBox(""); _verify2.UseSystemPasswordChar = true;
            _verify1Lbl = Lbl("Current password");
            _verify2Lbl = Lbl("");

            _newPass = GunaUi.TextBox("New password"); _newPass.UseSystemPasswordChar = true;
            _confirm = GunaUi.TextBox("Confirm new password"); _confirm.UseSystemPasswordChar = true;
            _msg = new Label { Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.Danger, TextAlign = ContentAlignment.MiddleLeft };
            var resetBtn = GunaUi.Button("Reset password", primary: true); resetBtn.Dock = DockStyle.Fill; resetBtn.Click += Reset_Click;
            var close = GunaUi.Button("Close", primary: false); close.Dock = DockStyle.Fill; close.DialogResult = DialogResult.Cancel;

            int r = 0;
            void Row(Control c, int height, int gapBelow = 0)
            {
                tl.RowStyles.Add(new RowStyle(SizeType.Absolute, height + gapBelow));
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(0, 0, 0, gapBelow);
                tl.Controls.Add(c, 0, r++);
            }

            Row(new Label { Text = "Forgot password", Dock = DockStyle.Fill, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft }, 40, 8);
            Row(Lbl("Username"), 20);
            Row(_user, 38, 6);
            Row(showHint, 40, 4);
            Row(_hintLbl, 26, 8);
            Row(Lbl("How do you want to verify it's you?"), 20);
            Row(_mode, 38, 8);
            Row(_verify1Lbl, 20);
            Row(_verify1, 38, 6);
            Row(_verify2Lbl, 20);
            Row(_verify2, 38, 8);
            Row(Lbl("New password"), 20);
            Row(_newPass, 38, 6);
            Row(Lbl("Confirm new password"), 20);
            Row(_confirm, 38, 8);
            Row(_msg, 24, 6);
            Row(resetBtn, 46, 8);
            Row(close, 42);
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, r);

            card.Controls.Add(tl);
            Controls.Add(card);
            CancelButton = close;
            ApplyMode();
            FormAnimator.FadeIn(this);
        }

        private static Label Lbl(string t) => new()
        { Text = t, Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };

        private bool AdminMode => _mode.SelectedIndex == 1;

        private void ApplyMode()
        {
            if (AdminMode)
            {
                _verify1Lbl.Text = "Administrator username";
                _verify1.PlaceholderText = "Admin username";
                _verify1.UseSystemPasswordChar = false;
                _verify2Lbl.Text = "Administrator password";
                _verify2.PlaceholderText = "Admin password";
                _verify2Lbl.Visible = _verify2.Visible = true;
            }
            else
            {
                _verify1Lbl.Text = "Current password";
                _verify1.PlaceholderText = "Your current password";
                _verify1.UseSystemPasswordChar = true;
                _verify2Lbl.Text = string.Empty;
                _verify2Lbl.Visible = _verify2.Visible = false;
            }
            _msg.Text = string.Empty;
        }

        private void ShowHint_Click(object? sender, EventArgs e)
        {
            string u = _user.Text.Trim();
            if (u.Length == 0) { _hintLbl.ForeColor = AppleTheme.Danger; _hintLbl.Text = "Enter your username first."; return; }
            if (!_store.UserExists(u)) { _hintLbl.ForeColor = AppleTheme.Danger; _hintLbl.Text = "No account with that username."; return; }
            string? hint = _store.GetHint(u);
            _hintLbl.ForeColor = AppleTheme.Accent;
            _hintLbl.Text = string.IsNullOrWhiteSpace(hint) ? "No hint was set for this account." : $"Hint: {hint}";
        }

        private void Reset_Click(object? sender, EventArgs e)
        {
            string u = _user.Text.Trim();
            if (u.Length == 0) { Fail("Enter your username."); return; }
            if (_newPass.Text != _confirm.Text) { Fail("Passwords do not match."); return; }

            string? err = AdminMode
                ? _store.AdminResetPassword(_verify1.Text.Trim(), _verify2.Text, u, _newPass.Text)
                : _store.ChangePassword(u, _verify1.Text, _newPass.Text);

            if (err != null) { Fail(err); return; }
            _msg.ForeColor = AppleTheme.Success;
            _msg.Text = "Password reset. You can close this and sign in.";
        }

        private void Fail(string message)
        {
            _msg.ForeColor = AppleTheme.Danger;
            _msg.Text = message;
        }
    }
}
