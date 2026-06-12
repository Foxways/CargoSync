using System;
using System.Drawing;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Onboarding
{
    /// <summary>
    /// First-run guided setup: Welcome → CargoWise connection → AI choice (with the privacy
    /// notice) → Done. Shown automatically when no clients are configured yet, so a brand-new
    /// user is never dropped on an empty screen with disabled buttons.
    /// </summary>
    public sealed class WelcomeWizard : Form
    {
        private readonly Func<int> _clientCount;
        private readonly AiSettings _aiSettings;
        private readonly Panel[] _pages = new Panel[4];
        private int _page;

        private Guna2Button _backBtn = null!, _nextBtn = null!;
        private Label _stepLbl = null!;
        private Label _connStatus = null!;
        private Guna2CheckBox _aiCheck = null!;

        public WelcomeWizard(Func<int> clientCount, AiSettings aiSettings)
        {
            _clientCount = clientCount;
            _aiSettings = aiSettings;
            BuildUi();
            ShowPage(0);
        }

        private void BuildUi()
        {
            Text = "Welcome to CargoSync";
            ClientSize = new Size(720, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = AppleTheme.Canvas };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(46)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(76)));

            _stepLbl = new Label
            {
                Dock = DockStyle.Fill, Padding = new Padding(28, 16, 0, 0),
                Font = AppleTheme.Caption, ForeColor = AppleTheme.TextSecondary
            };

            var deck = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(28, 4, 28, 4) };
            _pages[0] = WelcomePage();
            _pages[1] = ConnectPage();
            _pages[2] = AiPage();
            _pages[3] = DonePage();
            foreach (var p in _pages) { p.Dock = DockStyle.Fill; p.Visible = false; deck.Controls.Add(p); }

            var footer = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(28, 14, 28, 18) };
            var right = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, BackColor = Color.Transparent };
            _nextBtn = GunaUi.Button("Next  →", primary: true); _nextBtn.Size = new Size(130, 40); _nextBtn.Margin = new Padding(8, 0, 0, 0);
            _backBtn = GunaUi.Button("←  Back", primary: false); _backBtn.Size = new Size(110, 40); _backBtn.Margin = new Padding(8, 0, 8, 0);
            _nextBtn.Click += (s, e) => Next();
            _backBtn.Click += (s, e) => ShowPage(_page - 1);
            right.Controls.Add(_nextBtn);
            right.Controls.Add(_backBtn);

            var skip = new Guna2Button
            {
                Text = "Skip setup for now", FillColor = Color.Transparent, ForeColor = AppleTheme.TextSecondary,
                Font = AppleTheme.Body, BorderThickness = 0, Cursor = Cursors.Hand, AutoSize = true, Dock = DockStyle.Left
            };
            skip.HoverState.FillColor = Color.Transparent;
            skip.HoverState.ForeColor = AppleTheme.TextPrimary;
            skip.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
            footer.Controls.Add(right);
            footer.Controls.Add(skip);

            root.Controls.Add(_stepLbl, 0, 0);
            root.Controls.Add(deck, 0, 1);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);
            FormAnimator.FadeIn(this);
        }

        // ---------------- pages ----------------

        private static Label H(string text) => new()
        {
            Text = text, Dock = DockStyle.Top, AutoSize = true,
            Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary, Padding = new Padding(0, 8, 0, 10)
        };

        private static Label P(string text, int padTop = 0) => new()
        {
            Text = text, Dock = DockStyle.Top, AutoSize = true, MaximumSize = new Size(640, 0),
            Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary, Padding = new Padding(0, padTop, 0, 8)
        };

        private Panel WelcomePage()
        {
            var p = new Panel();
            p.Controls.Add(P("You can re-run this setup any time from the Get Started button.", 14));
            p.Controls.Add(P("3.  Review what CargoSync found — nothing is ever sent without your approval."));
            p.Controls.Add(P("2.  Pick any Excel or CSV file of organizations — no special format needed."));
            p.Controls.Add(P("1.  Connect your CargoWise system (one-time, takes 2 minutes)."));
            p.Controls.Add(P("CargoSync imports organizations into CargoWise from any spreadsheet, with smart column mapping, duplicate detection and data cleaning built in. Three steps and you're ready:", 6));
            p.Controls.Add(H("Welcome to CargoSync 👋"));
            return p;
        }

        private Panel ConnectPage()
        {
            var p = new Panel();
            var openBtn = GunaUi.Button("Add CargoWise connection…", primary: true);
            openBtn.Size = new Size(260, 44);
            openBtn.Dock = DockStyle.Top;
            openBtn.Margin = new Padding(0, 10, 0, 0);
            openBtn.Click += (s, e) =>
            {
                using var f = new EAdaptorSetupForm();
                f.ShowDialog(this);
                RefreshConnStatus();
            };
            _connStatus = new Label
            {
                Dock = DockStyle.Top, AutoSize = true, Font = AppleTheme.Headline,
                ForeColor = AppleTheme.TextSecondary, Padding = new Padding(0, 14, 0, 0)
            };

            p.Controls.Add(_connStatus);
            p.Controls.Add(Spacer(10));
            p.Controls.Add(openBtn);
            p.Controls.Add(P("You'll need: the eAdaptor URL, a Sender ID and its password, and your Enterprise ID. Your CargoWise administrator has these. Use the Test Connection button on that screen to check everything before saving."));
            p.Controls.Add(P("CargoSync talks to CargoWise through its eAdaptor — CargoWise's electronic mailbox for incoming data.", 6));
            p.Controls.Add(H("Step 1 — Connect your CargoWise system"));
            return p;
        }

        private Panel AiPage()
        {
            var p = new Panel();
            var checkRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Color.Transparent, Padding = new Padding(0, 12, 0, 0) };
            _aiCheck = GunaUi.Check("Turn on AI assistance");
            _aiCheck.Font = AppleTheme.Headline;
            _aiCheck.Checked = _aiSettings.Enabled;
            checkRow.Controls.Add(_aiCheck);

            p.Controls.Add(P("You can change this any time from the AI chip at the top of the main screen.", 12));
            p.Controls.Add(checkRow);
            p.Controls.Add(P("Privacy note: when AI is on, column names and sample values from your files are sent to the AI provider to improve suggestions. If your data must never leave this machine, leave AI off (or use a local Ollama provider in AI Settings)."));
            p.Controls.Add(P("AI can suggest mappings for cryptic column names, fix messy values (\"Ozztralia\" → AU) and answer questions about your file. CargoSync works fully without it — AI is a helper, never a requirement.", 6));
            p.Controls.Add(H("Step 2 — AI assistance (optional)"));
            return p;
        }

        private Panel DonePage()
        {
            var p = new Panel();
            p.Controls.Add(P("Need help later? Click the  ?  button in the top-right of the main screen for the full guide.", 10));
            p.Controls.Add(P("3.  Review each screen CargoSync shows you, then run the real Upload when you're happy."));
            p.Controls.Add(P("2.  Click Dry Run first — it checks everything and sends NOTHING, so you can practice safely."));
            p.Controls.Add(P("1.  Pick your client and Browse to an Excel/CSV file of organizations."));
            p.Controls.Add(P("Here's the safest way to do your first import:", 6));
            p.Controls.Add(H("You're all set ✓"));
            return p;
        }

        private static Panel Spacer(int h) => new() { Dock = DockStyle.Top, Height = h, BackColor = Color.Transparent };

        // ---------------- navigation ----------------

        private void RefreshConnStatus()
        {
            int n = _clientCount();
            if (n > 0)
            {
                _connStatus.ForeColor = AppleTheme.Success;
                _connStatus.Text = $"✓ {n} CargoWise connection(s) configured — you're good to go.";
            }
            else
            {
                _connStatus.ForeColor = AppleTheme.TextSecondary;
                _connStatus.Text = "No connection yet. You can also do this later via the Add Client button.";
            }
        }

        private void ShowPage(int page)
        {
            _page = Math.Max(0, Math.Min(_pages.Length - 1, page));
            for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _page;
            _stepLbl.Text = $"Step {_page + 1} of {_pages.Length}";
            _backBtn.Visible = _page > 0;
            _nextBtn.Text = _page == _pages.Length - 1 ? "Finish ✓" : "Next  →";
            if (_page == 1) RefreshConnStatus();
        }

        private void Next()
        {
            if (_page == _pages.Length - 1)
            {
                // Apply the AI choice (the page carries the consent wording, so seeing it = informed).
                try
                {
                    _aiSettings.Enabled = _aiCheck.Checked;
                    _aiSettings.Save();
                }
                catch (Exception ex) { Logging.AppLog.Warn("Wizard could not save AI choice", ex); }
                DialogResult = DialogResult.OK;
                Close();
                return;
            }
            ShowPage(_page + 1);
        }
    }
}
