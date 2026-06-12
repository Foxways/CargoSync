using System;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Auth
{
    /// <summary>Recover access: show the user's password hint, then optionally set a new password.</summary>
    public class ForgotPasswordForm : Form
    {
        private readonly UserStore _store;
        private Guna2TextBox _user = null!, _newPass = null!, _confirm = null!;
        private Label _hintLbl = null!, _msg = null!;

        public ForgotPasswordForm(UserStore store)
        {
            _store = store;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Forgot password";
            ClientSize = new Size(440, 560);
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
            Label Lbl(string t) => new Label { Text = t, Dock = DockStyle.Fill, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft };

            Row(new Label { Text = "Forgot password", Dock = DockStyle.Fill, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft }, 40, 10);
            Row(Lbl("Username"), 20);
            Row(_user, 38, 8);
            Row(showHint, 40, 6);
            Row(_hintLbl, 28, 12);
            Row(Lbl("New password"), 20);
            Row(_newPass, 38, 12);
            Row(Lbl("Confirm new password"), 20);
            Row(_confirm, 38, 12);
            Row(_msg, 24, 6);
            Row(resetBtn, 46, 10);
            Row(close, 42);
            tl.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tl.Controls.Add(new Panel { BackColor = Color.Transparent }, 0, r);

            card.Controls.Add(tl);
            Controls.Add(card);
            CancelButton = close;
            FormAnimator.FadeIn(this);
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
            if (_newPass.Text != _confirm.Text) { _msg.ForeColor = AppleTheme.Danger; _msg.Text = "Passwords do not match."; return; }
            string? err = _store.ResetPassword(u, _newPass.Text);
            if (err != null) { _msg.ForeColor = AppleTheme.Danger; _msg.Text = err; return; }
            _msg.ForeColor = AppleTheme.Success;
            _msg.Text = "Password reset. You can close this and sign in.";
        }
    }
}
