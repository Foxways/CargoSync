using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Auth
{
    /// <summary>Sign-in screen. Sets <see cref="AuthenticatedUser"/> and closes on success.</summary>
    public class LoginForm : Form
    {
        private readonly UserStore _store;
        private Guna2TextBox _user = null!, _pass = null!;
        private Label _error = null!;
        private Guna2Button _loginBtn = null!;
        private Guna2WinProgressIndicator _spinner = null!;

        public User? AuthenticatedUser { get; private set; }

        public LoginForm(UserStore store)
        {
            _store = store;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Sign in";
            ClientSize = new Size(430, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            var card = GunaUi.Card();
            card.Dock = DockStyle.Fill;
            card.Margin = new Padding(0);
            card.Padding = new Padding(28, 26, 28, 26);

            var logoHost = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.Transparent };
            var logo = new LogoBadge { Size = new Size(66, 66), Top = 12 };
            logoHost.Controls.Add(logo);
            logoHost.Resize += (s, e) => logo.Left = Math.Max(0, (logoHost.Width - logo.Width) / 2);

            var title = new Label { Text = "CargoSync", Dock = DockStyle.Top, Height = 34, Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, TextAlign = ContentAlignment.MiddleCenter };
            var subtitle = new Label { Text = "Sign in to continue", Dock = DockStyle.Top, Height = 26, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, TextAlign = ContentAlignment.MiddleCenter };

            var spacer1 = new Panel { Dock = DockStyle.Top, Height = 18, BackColor = Color.Transparent };
            var userLbl = new Label { Text = "Username", Dock = DockStyle.Top, Height = 22, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            _user = GunaUi.TextBox("Enter username"); _user.Dock = DockStyle.Top; _user.Margin = new Padding(0, 4, 0, 10); _user.Height = 40;

            var spacer2 = new Panel { Dock = DockStyle.Top, Height = 8, BackColor = Color.Transparent };
            var passLbl = new Label { Text = "Password", Dock = DockStyle.Top, Height = 22, Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            _pass = GunaUi.TextBox("Enter password"); _pass.Dock = DockStyle.Top; _pass.Height = 40; _pass.UseSystemPasswordChar = true;

            _error = new Label { Text = "", Dock = DockStyle.Top, Height = 26, Font = AppleTheme.Body, ForeColor = AppleTheme.Danger, Padding = new Padding(0, 4, 0, 0) };

            _loginBtn = GunaUi.Button("Sign in", primary: true); _loginBtn.Dock = DockStyle.Top; _loginBtn.Height = 44; _loginBtn.Click += Login_Click;
            var loginBtn = _loginBtn;

            _spinner = new Guna2WinProgressIndicator { Size = new Size(36, 36), Visible = false, ProgressColor = AppleTheme.Accent };

            var links = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, FlowDirection = FlowDirection.LeftToRight, BackColor = Color.Transparent, Padding = new Padding(0, 10, 0, 0) };
            var create = LinkButton("Create account"); create.Click += (s, e) => OpenCreate();
            var forgot = LinkButton("Forgot password?"); forgot.Click += (s, e) => OpenForgot();
            links.Controls.Add(create);
            links.Controls.Add(forgot);

            // add bottom-up so docking stacks top-down in visual order
            card.Controls.Add(links);
            card.Controls.Add(loginBtn);
            card.Controls.Add(_error);
            card.Controls.Add(_pass);
            card.Controls.Add(passLbl);
            card.Controls.Add(spacer2);
            card.Controls.Add(_user);
            card.Controls.Add(userLbl);
            card.Controls.Add(spacer1);
            card.Controls.Add(subtitle);
            card.Controls.Add(title);
            card.Controls.Add(logoHost);

            Controls.Add(card);
            Controls.Add(_spinner);
            _spinner.BringToFront();
            void CenterSpinner() => _spinner.Location = new Point((ClientSize.Width - _spinner.Width) / 2, ClientSize.Height / 2 + 40);
            Resize += (s, e) => CenterSpinner();
            Shown += (s, e) => CenterSpinner();
            AcceptButton = loginBtn;

            if (!_store.AnyUsers())
            {
                _error.ForeColor = AppleTheme.TextSecondary;
                _error.Text = "No accounts yet — click \"Create account\" to begin.";
            }

            FormAnimator.FadeIn(this);
            FormAnimator.EnableFadeOnClose(this);
        }

        private static Guna2Button LinkButton(string text)
        {
            var b = new Guna2Button
            {
                Text = text, FillColor = Color.Transparent, ForeColor = AppleTheme.Accent,
                Font = AppleTheme.Body, BorderThickness = 0, Cursor = Cursors.Hand,
                Size = new Size(150, 28), Margin = new Padding(0, 0, 12, 0)
            };
            b.HoverState.FillColor = Color.FromArgb(40, 40, 48);
            return b;
        }

        private async void Login_Click(object? sender, EventArgs e)
        {
            string u = _user.Text.Trim(), p = _pass.Text;
            if (u.Length == 0 || p.Length == 0) { ShowError("Enter your username and password."); return; }

            try
            {
                SetBusy(true);
                var authTask = Task.Run(() => _store.Authenticate(u, p));
                await Task.WhenAll(authTask, Task.Delay(650)); // keep the spinner visible briefly
                var user = authTask.Result;

                if (user == null) { SetBusy(false); ShowError("Incorrect username or password."); return; }

                AuthenticatedUser = user;
                Close();
            }
            catch (Exception ex)
            {
                SetBusy(false);
                ShowError("Sign-in failed: " + ex.Message);
                Logging.AppLog.Error("Login failed unexpectedly", ex);
            }
        }

        private void SetBusy(bool busy)
        {
            _spinner.Visible = busy;
            if (busy) _spinner.Start(); else _spinner.Stop();
            _spinner.BringToFront();
            _loginBtn.Enabled = !busy;
            _loginBtn.Text = busy ? "Signing in…" : "Sign in";
            _user.Enabled = _pass.Enabled = !busy;
            if (busy) _error.Text = string.Empty;
        }

        private void OpenCreate()
        {
            using var f = new CreateUserForm(_store);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                _user.Text = f.CreatedUsername;
                _pass.Text = string.Empty;
                _error.ForeColor = AppleTheme.Success;
                _error.Text = "Account created — sign in now.";
                _pass.Focus();
            }
        }

        private void OpenForgot()
        {
            using var f = new ForgotPasswordForm(_store);
            f.ShowDialog(this);
        }

        private void ShowError(string msg)
        {
            _error.ForeColor = AppleTheme.Danger;
            _error.Text = msg;
        }
    }
}
