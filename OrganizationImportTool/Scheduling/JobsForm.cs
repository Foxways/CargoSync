using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Security;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Manage scheduled import jobs — list on the left, tabbed editor on the right.
    /// Tabs: General | Source | Schedule | Notifications | Advanced.
    /// </summary>
    public sealed class JobsForm : Form
    {
        private readonly JobStore           _store   = new();
        private readonly List<ClientRef>    _clients = ClientDirectory.List();
        private readonly Guna2DataGridView  _grid    = new();
        private string? _editingId;

        // ── General tab ──────────────────────────────────────────────────────
        private readonly Guna2TextBox   _name    = GunaUi.TextBox("e.g. Acme nightly import");
        private readonly Guna2CheckBox  _enabled = GunaUi.Check("Enabled");
        private readonly Guna2ComboBox  _client  = GunaUi.Combo();

        // ── Source tab ───────────────────────────────────────────────────────
        private readonly Guna2ComboBox      _sourceKind         = GunaUi.Combo();
        private readonly Guna2TextBox       _source             = GunaUi.TextBox(@"\\server\share\inbound");
        private readonly Guna2TextBox       _remoteHost         = GunaUi.TextBox("sftp.example.com");
        private readonly Guna2NumericUpDown _remotePort         = GunaUi.Numeric();
        private readonly Guna2TextBox       _remoteUser         = GunaUi.TextBox("username");
        private readonly Guna2TextBox       _remotePass         = GunaUi.TextBox("password");
        private readonly Guna2TextBox       _remoteFolder       = GunaUi.TextBox("/inbound");
        private readonly Guna2CheckBox      _remoteTls          = GunaUi.Check("Use FTPS (TLS encryption)");
        private readonly Guna2ComboBox      _remotePost         = GunaUi.Combo();
        private readonly Guna2TextBox       _pattern            = GunaUi.TextBox("*.xlsx, *.csv");
        private readonly Guna2CheckBox      _recursive          = GunaUi.Check("Include sub-folders");
        private readonly Guna2ComboBox      _template           = GunaUi.Combo();
        private readonly Guna2CheckBox      _dryRun             = GunaUi.Check("Dry run");
        private readonly Guna2CheckBox      _skipDup            = GunaUi.Check("Skip duplicates");
        private readonly Guna2CheckBox      _autoClean          = GunaUi.Check("Auto-apply data cleaning");
        private readonly Guna2ComboBox      _post               = GunaUi.Combo();
        private readonly Guna2TextBox       _processedSubfolder = GunaUi.TextBox("processed");
        private readonly Guna2TextBox       _failedSubfolder    = GunaUi.TextBox("failed");

        // Source-tab row tracking for show/hide
        private TableLayoutPanel? _sourceTlp;
        private int _rowLocalFolder  = -1;
        private int _rowRemoteHost   = -1;
        private int _rowRemotePort   = -1;
        private int _rowRemoteUser   = -1;
        private int _rowRemotePass   = -1;
        private int _rowRemoteFolder = -1;
        private int _rowRemoteTls    = -1;
        private int _rowRemotePost   = -1;
        private int _rowLocalPost    = -1;
        private int _rowProcessed    = -1;
        private int _rowFailed       = -1;

        // ── Schedule tab ─────────────────────────────────────────────────────
        private readonly Guna2ComboBox      _schedKind = GunaUi.Combo();
        private readonly Guna2NumericUpDown _interval  = GunaUi.Numeric();
        private readonly Guna2NumericUpDown _hour      = GunaUi.Numeric();
        private readonly Guna2NumericUpDown _minute    = GunaUi.Numeric();
        private readonly Guna2CheckBox[]    _days      = new Guna2CheckBox[7];
        private readonly Label _timeLabel    = new() { TextAlign = ContentAlignment.MiddleLeft, Text = "Time of day",
                                                        ForeColor = AppleTheme.TextSecondary };
        private readonly Label _nextRunLabel = new() { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                                                        ForeColor = AppleTheme.TextPrimary };

        // ── Notifications tab ────────────────────────────────────────────────
        private readonly Guna2ComboBox  _notify         = GunaUi.Combo();
        private readonly Guna2TextBox   _clientEmails   = GunaUi.TextBox("client@company.com, ops@company.com");
        private readonly Guna2TextBox   _internalEmails = GunaUi.TextBox("team@yourcompany.com");
        private readonly Guna2CheckBox  _attachLog      = GunaUi.Check("Import log");
        private readonly Guna2CheckBox  _attachCsv      = GunaUi.Check("Failed-rows CSV");

        // ── Advanced / status ─────────────────────────────────────────────────
        private readonly Guna2TextBox  _runAsUser  = GunaUi.TextBox(@"DOMAIN\user  (blank = current user)");
        private readonly Guna2TextBox  _runAsPass  = GunaUi.TextBox("password");
        private readonly Label _lastRunLabel   = new() { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                                                           ForeColor = AppleTheme.TextPrimary };
        private readonly Label _taskStatusLabel = new() { TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
                                                            ForeColor = AppleTheme.TextPrimary };

        // ── Tab navigation ────────────────────────────────────────────────────
        private static readonly string[] TabTitles = { "General", "Source", "Schedule", "Notifications", "Advanced" };
        private readonly GunaUi.GlossButton[] _tabBtns = new GunaUi.GlossButton[5];
        private readonly Panel[]       _tabPages = new Panel[5];
        private int _activeTab = 0;

        private static readonly string[] DayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        private static readonly DayOfWeek[] DayOrder =
            { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
              DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };

        // ─────────────────────────────────────────────────────────────────────

        public JobsForm()
        {
            Text          = "CargoSync — Scheduled Imports";
            StartPosition = FormStartPosition.CenterScreen;
            Size          = new Size(1280, 780);
            MinimumSize   = new Size(1260, 640);  // strip = 1260−381−36 = 843px > lfW(280)+rfW(534) = 814px → no button overlap
            BackColor     = AppleTheme.Canvas;
            ForeColor     = AppleTheme.TextPrimary;
            Font          = AppleTheme.Body;
            AppleTheme.ApplyWindow(this);

            _runAsPass.UseSystemPasswordChar  = true;
            _remotePass.UseSystemPasswordChar = true;
            _interval.Minimum = 1;   _interval.Maximum = 1440; _interval.Value = 15;
            _hour.Minimum     = 0;   _hour.Maximum     = 23;   _hour.Value    = 2;
            _minute.Minimum   = 0;   _minute.Maximum   = 59;   _minute.Value  = 0;
            _remotePort.Minimum = 1; _remotePort.Maximum = 65535; _remotePort.Value = 22;

            _sourceKind.Items.AddRange(new object[] { "Local / UNC path", "SFTP", "FTP / FTPS" });
            _remotePost.Items.AddRange(new object[] { "Leave on server", "Delete after success", "Move to sub-folder" });
            _post.Items.AddRange(new object[] { "Move to processed / failed folders", "Delete after success", "Leave in place" });
            _schedKind.Items.AddRange(new object[] { "Manual (on demand)", "Every N minutes", "Hourly", "Daily", "Weekly" });
            _notify.Items.AddRange(new object[] { "Never", "On failure only", "Always" });
            _client.Items.AddRange(_clients.Cast<object>().ToArray());

            _client.SelectedIndexChanged     += (_, __) => ReloadTemplates();
            _schedKind.SelectedIndexChanged  += (_, __) => ToggleScheduleRows();
            _sourceKind.SelectedIndexChanged += (_, __) => ToggleSourceRows();
            _post.SelectedIndexChanged       += (_, __) => TogglePostRows();

            Controls.Add(BuildEditor());
            Controls.Add(BuildSidebar());

            RefreshList();
            NewJob();
            FormAnimator.FadeIn(this);
        }

        // ════════════════════════════ LAYOUT ══════════════════════════════════

        private Control BuildSidebar()
        {
            _grid.Dock                = DockStyle.Fill;
            _grid.ReadOnly            = true;
            _grid.AllowUserToAddRows  = false;
            _grid.RowHeadersVisible   = false;
            _grid.MultiSelect         = false;
            _grid.SelectionMode       = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Name",     HeaderText = "Job",      FillWeight = 42 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Schedule", HeaderText = "Schedule", FillWeight = 30 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "NextRun",  HeaderText = "Next Run", FillWeight = 28 });
            AppleTheme.StyleGrid(_grid);
            _grid.SelectionChanged += (_, __) => LoadSelected();

            var newBtn = GunaUi.Button("+ New",   primary: true);
            var delBtn = GunaUi.Button("Delete",  primary: false);
            var runBtn = GunaUi.Button("Run Now", primary: false);
            newBtn.Click += (_, __) => NewJob();
            delBtn.Click += (_, __) => DeleteSelected();
            runBtn.Click += (_, __) => RunNow();

            // Three equal-width Percent columns separated by 1px Hairline dividers — like a
            // segmented control. Every button fills its column; Margin (0,11,0,11) → 56-22=34px height → centred.
            newBtn.Dock = delBtn.Dock = runBtn.Dock = DockStyle.Fill;
            newBtn.Margin = delBtn.Margin = runBtn.Margin = new Padding(6, 11, 6, 11);  // 6px horizontal inset → visible gap from separator edges

            var btnBar = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom, Height = 56, BackColor = AppleTheme.Canvas,
                ColumnCount = 5, RowCount = 1, Padding = new Padding(0), Margin = new Padding(0)
            };
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));  // New
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));          // separator
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));  // Delete
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));          // separator
            btnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));  // Run Now
            btnBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            btnBar.Controls.Add(newBtn,                                                         0, 0);
            btnBar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Hairline }, 1, 0);
            btnBar.Controls.Add(delBtn,                                                         2, 0);
            btnBar.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Hairline }, 3, 0);
            btnBar.Controls.Add(runBtn,                                                         4, 0);

            // 1 px hairline above the button bar
            var barLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = AppleTheme.Hairline };
            var bar = btnBar;

            var hairline = new Panel { Dock = DockStyle.Right, Width = 1, BackColor = AppleTheme.Hairline };
            var panel    = new Panel { Dock = DockStyle.Left, Width = 380, Padding = new Padding(12, 12, 0, 0), BackColor = AppleTheme.Canvas };
            // Dock order (last added = first processed):
            //   hairline → barLine → bar → _grid
            panel.Controls.Add(_grid);
            panel.Controls.Add(bar);
            panel.Controls.Add(barLine);
            panel.Controls.Add(hairline);
            return panel;
        }

        private Control BuildEditor()
        {
            // Tab strip
            // strip height = 6(pad-top) + 34(btn) + 6(pad-bottom) = 46 → buttons perfectly centred
            var strip = new Panel { Dock = DockStyle.Top, Height = 46, BackColor = AppleTheme.Canvas, Padding = new Padding(20, 6, 0, 6) };
            var flow  = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = Color.Transparent };

            for (int i = 0; i < TabTitles.Length; i++)
            {
                int tabW = TextRenderer.MeasureText(TabTitles[i], AppleTheme.Body).Width + 60;
                var btn = new GunaUi.GlossButton
                {
                    Text         = TabTitles[i],
                    Font         = AppleTheme.Body,
                    Size         = new Size(tabW, 34), // 34 = natural Guna2Button height; width from text+40 so never clips
                    BorderRadius = 8,
                    FillColor    = AppleTheme.Surface,
                    ForeColor    = AppleTheme.TextSecondary,
                    TextAlign    = HorizontalAlignment.Center,
                    Margin       = new Padding(0, 0, 6, 0)
                };
                btn.HoverState.FillColor = AppleTheme.Hairline;
                int captured = i;
                btn.Click += (_, __) => SwitchTab(captured);
                _tabBtns[i] = btn;
                flow.Controls.Add(btn);
            }
            strip.Controls.Add(flow);

            var sep = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = AppleTheme.Hairline };

            _tabPages[0] = BuildGeneralPage();
            _tabPages[1] = BuildSourcePage();
            _tabPages[2] = BuildSchedulePage();
            _tabPages[3] = BuildNotificationsPage();
            _tabPages[4] = BuildAdvancedPage();

            var content = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };
            foreach (var p in _tabPages) { p.Dock = DockStyle.Fill; p.Visible = false; content.Controls.Add(p); }

            var editor = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };
            // Button bar added last → processed first by dock layout → sits at bottom of editor only
            // (not under the sidebar).  strip/sep are processed after, anchoring to the top.
            editor.Controls.Add(content);
            editor.Controls.Add(sep);
            editor.Controls.Add(strip);
            editor.Controls.Add(BuildButtonBar());

            SwitchTab(0);
            return editor;
        }

        private void SwitchTab(int idx)
        {
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                bool on = i == idx;
                _tabBtns[i].FillColor = on ? AppleTheme.Accent : AppleTheme.Surface;
                _tabBtns[i].ForeColor = on ? Color.White : AppleTheme.TextSecondary;
                _tabPages[i].Visible  = on;
            }
            _activeTab = idx;
        }

        // ──────────────────────────── TAB PAGES ──────────────────────────────

        private Panel BuildGeneralPage()
        {
            var tlp = MakeTlp();
            AddSection(tlp, "Job details");
            AddRow(tlp, "Job name", _name);
            AddFullSpan(tlp, _enabled);
            AddRow(tlp, "Client", _client);
            AddSection(tlp, "Run status");
            AddRow(tlp, "Last run", _lastRunLabel);
            AddRow(tlp, "Task status", _taskStatusLabel);
            return Scroll(tlp);
        }

        private Panel BuildSourcePage()
        {
            var tlp = MakeTlp();
            _sourceTlp = tlp;

            AddSection(tlp, "Source");
            AddRow(tlp, "Source type",   _sourceKind);
            _rowLocalFolder  = AddRowTracked(tlp, "Source folder",  WithBrowse(_source));
            _rowRemoteHost   = AddRowTracked(tlp, "Host",           _remoteHost);
            _rowRemotePort   = AddRowTracked(tlp, "Port",           _remotePort);
            _rowRemoteUser   = AddRowTracked(tlp, "Username",       _remoteUser);
            _rowRemotePass   = AddRowTracked(tlp, "Password",       _remotePass);
            _rowRemoteFolder = AddRowTracked(tlp, "Remote folder",  WithTestConnection(_remoteFolder));
            _rowRemoteTls    = AddRowTracked(tlp, "FTPS",           _remoteTls);
            _rowRemotePost   = AddRowTracked(tlp, "After download", _remotePost);

            AddSection(tlp, "File selection");
            AddRow(tlp, "Pattern",  WithRecursive(_pattern));
            AddRow(tlp, "Template", WithTemplateManager(_template));

            AddSection(tlp, "Import policy");
            AddFullSpan(tlp, PolicyRow());
            _rowLocalPost = AddRowTracked(tlp, "After import",     _post);
            _rowProcessed = AddRowTracked(tlp, "Processed folder", _processedSubfolder);
            _rowFailed    = AddRowTracked(tlp, "Failed folder",    _failedSubfolder);

            return Scroll(tlp);
        }

        private Panel BuildSchedulePage()
        {
            var tlp = MakeTlp();
            AddSection(tlp, "Cadence");
            AddRow(tlp, "Schedule",       _schedKind);
            AddRow(tlp, "Interval (min)", _interval);
            AddRow(tlp, _timeLabel,       TimeRow());
            AddRow(tlp, "Days of week",   DaysRow());
            AddSection(tlp, "Next run");
            AddRow(tlp, "Predicted at",   _nextRunLabel);
            return Scroll(tlp);
        }

        private Panel BuildNotificationsPage()
        {
            var tlp = MakeTlp();
            AddSection(tlp, "Email notifications");
            AddRow(tlp, "Send on",             _notify);
            AddRow(tlp, "Client recipients",   _clientEmails);
            AddRow(tlp, "Internal recipients", _internalEmails);
            AddRow(tlp, "Attach",              AttachRow());

            var hint = new Label
            {
                Text = "Configure the outbound mailbox via \"Email settings…\" in the button bar below.",
                ForeColor = AppleTheme.TextSecondary,
                Font = new Font(AppleTheme.Body.FontFamily, 8.5f),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(0, 8, 0, 0)
            };
            int r = tlp.RowCount++;
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            tlp.Controls.Add(hint, 0, r); tlp.SetColumnSpan(hint, 2);

            return Scroll(tlp);
        }

        private Panel BuildAdvancedPage()
        {
            var tlp = MakeTlp();
            AddSection(tlp, "Windows Task Scheduler");
            AddRow(tlp, "Run as user",      _runAsUser);
            AddRow(tlp, "Account password", _runAsPass);

            var hint = new Label
            {
                Text = "Leave both blank to run as the current Windows user (recommended for single-user installs).",
                ForeColor = AppleTheme.TextSecondary,
                Font = new Font(AppleTheme.Body.FontFamily, 8.5f),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
            };
            int r = tlp.RowCount++;
            tlp.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            tlp.Controls.Add(hint, 0, r); tlp.SetColumnSpan(hint, 2);

            return Scroll(tlp);
        }

        // ─────────────────────── COMPOSITE CONTROLS ──────────────────────────

        // Shared layout: [input fills | 8 px gap | button centred].
        // AddRow gives this composite 38px height (Margin 0,5,0,5 on a 48px row).
        // AnchorStyles.None centres the 34px button vertically: (38-34)/2 = 2px top/bottom.
        private static TableLayoutPanel InputWithButton(Control input, Guna2Button btn)
        {
            int btnW   = TextRenderer.MeasureText(btn.Text, btn.Font).Width + 60;
            btn.Size   = new Size(btnW, 34);
            btn.Anchor = AnchorStyles.None;
            input.Dock = DockStyle.Fill;

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = AppleTheme.Canvas, Margin = new Padding(0), Padding = new Padding(0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, btnW));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.Controls.Add(input,                                     0, 0);
            t.Controls.Add(new Panel { BackColor = AppleTheme.Canvas }, 1, 0);
            t.Controls.Add(btn,                                       2, 0);
            return t;
        }

        private Control WithBrowse(Control box)
        {
            var browse = GunaUi.Button("Browse", primary: false);
            browse.Click += (_, __) =>
            {
                using var dlg = new FolderBrowserDialog();
                if (dlg.ShowDialog(this) == DialogResult.OK) _source.Text = dlg.SelectedPath;
            };
            return InputWithButton(box, browse);
        }

        private Control WithRecursive(Control patternBox)
        {
            _recursive.AutoSize = true;
            _recursive.Anchor   = AnchorStyles.None;   // vertically centres the checkbox in the row

            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                BackColor = AppleTheme.Canvas, Margin = new Padding(0), Padding = new Padding(0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            patternBox.Dock = DockStyle.Fill;
            t.Controls.Add(patternBox,                                  0, 0);
            t.Controls.Add(new Panel { BackColor = AppleTheme.Canvas }, 1, 0);
            t.Controls.Add(_recursive,                                  2, 0);
            return t;
        }

        private Control WithTemplateManager(Control combo)
        {
            var btn = GunaUi.Button("Manage", primary: false);
            btn.Click += (_, __) =>
            {
                using var f = new TemplateManagerForm();
                f.ShowDialog(this);
                ReloadTemplates();
            };
            return InputWithButton(combo, btn);
        }

        private Control WithTestConnection(Control folderBox)
        {
            var testBtn = GunaUi.Button("Test", primary: false);
            testBtn.Click += async (_, __) =>
            {
                testBtn.Enabled = false; testBtn.Text = "…";
                try
                {
                    var (ok, msg) = await RemoteSourceDownloader.TestConnectionAsync(BuildJobFromEditor());
                    MessageBox.Show(this, msg, ok ? "Connection OK" : "Connection failed",
                        MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Connection failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { testBtn.Enabled = true; testBtn.Text = "Test…"; }
            };
            return InputWithButton(folderBox, testBtn);
        }

        private Control PolicyRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                BackColor = AppleTheme.Canvas, Padding = new Padding(0, 4, 0, 0) };
            _dryRun.Margin  = new Padding(0, 0, 22, 0);
            _skipDup.Margin = new Padding(0, 0, 22, 0);
            p.Controls.AddRange(new Control[] { _dryRun, _skipDup, _autoClean });
            return p;
        }

        private Control TimeRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, BackColor = AppleTheme.Canvas };
            _hour.Size = new Size(62, 34); _minute.Size = new Size(62, 34);
            var colon = new Label { Text = " : ", ForeColor = AppleTheme.TextSecondary, AutoSize = true, Padding = new Padding(2, 10, 2, 0) };
            var hint  = new Label { Text = "(24h)", ForeColor = AppleTheme.TextSecondary, AutoSize = true, Padding = new Padding(6, 7, 0, 0) };
            p.Controls.AddRange(new Control[] { _hour, colon, _minute, hint });
            return p;
        }

        private Control DaysRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                BackColor = AppleTheme.Canvas, Padding = new Padding(0, 3, 0, 0) };
            for (int i = 0; i < 7; i++)
            {
                _days[i] = GunaUi.Check(DayNames[i]);
                _days[i].Margin = new Padding(0, 0, 10, 0);
                p.Controls.Add(_days[i]);
            }
            return p;
        }

        private Control AttachRow()
        {
            var p = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false,
                BackColor = AppleTheme.Canvas, Padding = new Padding(0, 4, 0, 0) };
            _attachLog.Margin = new Padding(0, 0, 22, 0);
            p.Controls.AddRange(new Control[] { _attachLog, _attachCsv });
            return p;
        }

        private Control BuildButtonBar()
        {
            static Size Sz(Control b) => new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + 60, 34);

            var email     = GunaUi.Button("Email",      primary: false);
            email.Click += (_, __) => { using var f = new SmtpSettingsForm(SmtpSettings.Load()); f.ShowDialog(this); };
            var templates = GunaUi.Button("Templates",  primary: false);
            templates.Click += (_, __) => { using var f = new TemplateManagerForm(); f.ShowDialog(this); ReloadTemplates(); };
            var save      = GunaUi.Button("Save",       primary: true);  save.Click      += (_, __) => Save(false);
            var saveSched = GunaUi.Button("Schedule",   primary: true);  saveSched.Click += (_, __) => Save(true);
            var unsched   = GunaUi.Button("Unschedule", primary: false); unsched.Click   += (_, __) => RemoveSchedule();
            var close     = GunaUi.Button("Close",      primary: false); close.Click     += (_, __) => Close();

            var allBtns = new[] { email, templates, save, saveSched, unsched, close };
            foreach (var b in allBtns) b.Size = Sz(b);

            // DockStyle.Left / DockStyle.Right groups (GunaUi.ButtonBar pattern).
            // MinimumSize is 1200px so left + right groups never overlap.
            // 8px margin per side = 16px between siblings within each group.
            return GunaUi.ButtonBar(
                right: new Control[] { close, unsched, saveSched, save },
                left:  new Control[] { email, templates });
        }

        // ══════════════════════════ DATA BINDING ══════════════════════════════

        private void RefreshList()
        {
            _grid.Rows.Clear();
            var now = DateTime.Now;
            foreach (var j in _store.LoadAll())
            {
                string next = !j.Enabled ? "Disabled"
                    : j.Schedule.Kind == ScheduleKind.Manual ? "Manual"
                    : (j.Schedule.NextRunAfter(now)?.ToString("ddd HH:mm") ?? "—");
                int row = _grid.Rows.Add(
                    $"{(j.Enabled ? "" : "⏸ ")}{j.Name}",
                    j.Schedule.Describe(), next);
                _grid.Rows[row].Tag = j.Id;
                if (!j.Enabled) _grid.Rows[row].DefaultCellStyle.ForeColor = AppleTheme.TextSecondary;
            }
        }

        private void LoadSelected()
        {
            if (_grid.SelectedRows.Count == 0) return;
            if (_grid.SelectedRows[0].Tag is not string id) return;
            var job = _store.Get(id);
            if (job != null) LoadJob(job);
        }

        private void NewJob() { _editingId = null; LoadJob(new ScheduledJob()); _grid.ClearSelection(); }

        private void LoadJob(ScheduledJob j)
        {
            _editingId = string.IsNullOrEmpty(j.CreatedUtc) ? null : j.Id;

            // General
            _name.Text       = j.Name == "Untitled job" ? "" : j.Name;
            _enabled.Checked = j.Enabled;
            SelectClient(j.ClientId);

            // Source
            _sourceKind.SelectedIndex = (int)j.SourceKind;
            _source.Text      = j.SourceFolder;
            _remoteHost.Text  = j.RemoteHost;
            _remotePort.Value = Math.Min(_remotePort.Maximum, Math.Max(_remotePort.Minimum, j.RemotePort));
            _remoteUser.Text  = j.RemoteUser;
            try { _remotePass.Text = string.IsNullOrEmpty(j.RemotePasswordProtected) ? "" : SecretProtector.Unprotect(j.RemotePasswordProtected); }
            catch { _remotePass.Text = ""; }
            _remoteFolder.Text        = string.IsNullOrWhiteSpace(j.RemoteFolder) ? "/" : j.RemoteFolder;
            _remoteTls.Checked        = j.FtpUseTls;
            _remotePost.SelectedIndex = (int)j.RemotePostProcess;
            ToggleSourceRows();

            _pattern.Text      = j.FilePattern;
            _recursive.Checked = j.Recursive;
            ReloadTemplates();
            SelectTemplate(j.TemplateId);
            _dryRun.Checked    = j.DryRun;
            _skipDup.Checked   = j.SkipDuplicates;
            _autoClean.Checked = j.AutoApplyCleaning;
            _post.SelectedIndex = (int)j.PostProcess;
            _processedSubfolder.Text = j.ProcessedSubfolder;
            _failedSubfolder.Text    = j.FailedSubfolder;
            TogglePostRows();

            // Schedule
            _schedKind.SelectedIndex = (int)j.Schedule.Kind;
            _interval.Value = Math.Min(_interval.Maximum, Math.Max(_interval.Minimum, j.Schedule.IntervalMinutes));
            _hour.Value     = Math.Min(23, Math.Max(0, j.Schedule.TimeOfDay.Hours));
            _minute.Value   = Math.Min(59, Math.Max(0, j.Schedule.TimeOfDay.Minutes));
            for (int i = 0; i < 7; i++) _days[i].Checked = j.Schedule.DaysOfWeek.Contains(DayOrder[i]);
            ToggleScheduleRows();
            UpdateNextRunDisplay(j);

            // Notifications
            _notify.SelectedIndex    = (int)j.NotifyOn;
            _clientEmails.Text       = j.NotifyClientEmails;
            _internalEmails.Text     = j.NotifyInternalEmails;
            _attachLog.Checked       = j.AttachImportLog;
            _attachCsv.Checked       = j.AttachFailedRowsCsv;

            // Advanced / status
            _runAsUser.Text = j.RunAsUser;
            UpdateLastRunDisplay(j);
            UpdateTaskStatus(j);
        }

        private ScheduledJob BuildJobFromEditor()
        {
            var job = (_editingId != null ? _store.Get(_editingId) : null) ?? new ScheduledJob();

            job.Name       = string.IsNullOrWhiteSpace(_name.Text) ? "Untitled job" : _name.Text.Trim();
            job.Enabled    = _enabled.Checked;
            job.ClientId   = (_client.SelectedItem as ClientRef)?.Id   ?? string.Empty;
            job.ClientName = (_client.SelectedItem as ClientRef)?.Name ?? string.Empty;

            job.SourceFolder      = _source.Text.Trim();
            job.SourceKind        = (SourceKind)Math.Max(0, _sourceKind.SelectedIndex);
            job.RemoteHost        = _remoteHost.Text.Trim();
            job.RemotePort        = (int)_remotePort.Value;
            job.RemoteUser        = _remoteUser.Text.Trim();
            job.RemotePasswordProtected = string.IsNullOrEmpty(_remotePass.Text)
                ? string.Empty : SecretProtector.Protect(_remotePass.Text);
            job.RemoteFolder      = string.IsNullOrWhiteSpace(_remoteFolder.Text) ? "/" : _remoteFolder.Text.Trim();
            job.FtpUseTls         = _remoteTls.Checked;
            job.RemotePostProcess = (RemotePostProcessAction)Math.Max(0, _remotePost.SelectedIndex);

            job.FilePattern       = string.IsNullOrWhiteSpace(_pattern.Text) ? "*.*" : _pattern.Text.Trim();
            job.Recursive         = _recursive.Checked;
            job.TemplateId        = (_template.SelectedItem as TemplateRef)?.Id;
            job.DryRun            = _dryRun.Checked;
            job.SkipDuplicates    = _skipDup.Checked;
            job.AutoApplyCleaning = _autoClean.Checked;
            job.PostProcess       = (PostProcessAction)Math.Max(0, _post.SelectedIndex);
            job.ProcessedSubfolder = string.IsNullOrWhiteSpace(_processedSubfolder.Text) ? "processed" : _processedSubfolder.Text.Trim();
            job.FailedSubfolder   = string.IsNullOrWhiteSpace(_failedSubfolder.Text)    ? "failed"    : _failedSubfolder.Text.Trim();

            job.Schedule = new ScheduleSpec
            {
                Kind            = (ScheduleKind)Math.Max(0, _schedKind.SelectedIndex),
                IntervalMinutes = (int)_interval.Value,
                TimeOfDay       = new TimeSpan((int)_hour.Value, (int)_minute.Value, 0),
                DaysOfWeek      = Enumerable.Range(0, 7).Where(i => _days[i].Checked).Select(i => DayOrder[i]).ToList()
            };

            job.NotifyOn             = (NotifyTrigger)Math.Max(0, _notify.SelectedIndex);
            job.NotifyClientEmails   = _clientEmails.Text.Trim();
            job.NotifyInternalEmails = _internalEmails.Text.Trim();
            job.AttachImportLog      = _attachLog.Checked;
            job.AttachFailedRowsCsv  = _attachCsv.Checked;
            job.RunAsUser            = NullIfBlank(_runAsUser.Text) ?? string.Empty;
            return job;
        }

        // ═══════════════════════════ ACTIONS ══════════════════════════════════

        private bool Save(bool schedule)
        {
            var job = BuildJobFromEditor();

            if (string.IsNullOrWhiteSpace(job.ClientId))
            { Warn("Choose a client on the General tab."); SwitchTab(0); return false; }
            if (!job.HasRemoteSource && string.IsNullOrWhiteSpace(job.SourceFolder))
            { Warn("Enter a source folder on the Source tab."); SwitchTab(1); return false; }
            if (job.HasRemoteSource && string.IsNullOrWhiteSpace(job.RemoteHost))
            { Warn("Enter the remote host on the Source tab."); SwitchTab(1); return false; }
            if (job.HasRemoteSource && string.IsNullOrWhiteSpace(job.RemoteUser))
            { Warn("Enter the remote username on the Source tab."); SwitchTab(1); return false; }
            if (!job.Schedule.IsValid(out var schedErr))
            { Warn(schedErr); SwitchTab(2); return false; }

            _store.Save(job, DateTime.UtcNow.ToString("o"));
            _editingId = job.Id;

            if (schedule)
            {
                if (!OperatingSystem.IsWindows())
                    Warn("Scheduling requires Windows.");
                else
                {
                    var (ok, output) = WindowsTaskScheduler.CreateOrUpdate(
                        job, NullIfBlank(_runAsUser.Text), NullIfBlank(_runAsPass.Text));
                    MessageBox.Show(this, ok
                        ? (job.Schedule.Kind == ScheduleKind.Manual
                            ? "Saved. (Manual cadence — no Windows task created.)"
                            : $"Saved and scheduled: {job.Schedule.Describe()}.")
                        : "Saved, but the Windows task could not be created:\n" + output,
                        "CargoSync", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show(this, "Job saved.", "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            RefreshList();
            UpdateNextRunDisplay(job);
            UpdateLastRunDisplay(job);
            if (schedule) UpdateTaskStatus(job);
            return true;
        }

        private void RemoveSchedule()
        {
            if (_editingId == null) { Warn("Save the job first."); return; }
            var job = _store.Get(_editingId);
            if (job == null) return;
            if (!OperatingSystem.IsWindows()) { Warn("Scheduling requires Windows."); return; }
            var (ok, output) = WindowsTaskScheduler.Delete(job);
            MessageBox.Show(this,
                ok ? "Windows schedule removed (the job definition is kept)."
                   : "Could not remove the task:\n" + output,
                "CargoSync", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            UpdateTaskStatus(job);
        }

        private void DeleteSelected()
        {
            if (_editingId == null) { NewJob(); return; }
            var job = _store.Get(_editingId);
            if (job == null) return;
            if (MessageBox.Show(this, $"Delete job '{job.Name}'? This also removes its Windows scheduled task.",
                    "CargoSync", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (OperatingSystem.IsWindows()) WindowsTaskScheduler.Delete(job);
            _store.Delete(job.Id);
            RefreshList();
            NewJob();
        }

        private void RunNow()
        {
            if (!Save(false)) return;
            if (_editingId == null) return;
            try
            {
                string exe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName!;
                var psi = new ProcessStartInfo(exe) { UseShellExecute = true };
                psi.ArgumentList.Add("--run-job");
                psi.ArgumentList.Add(_editingId);
                Process.Start(psi);
                MessageBox.Show(this,
                    "Job started in a separate window. Re-open this screen after it finishes to see the result.",
                    "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex) { Warn("Could not start the job: " + ex.Message); }
        }

        // ════════════════════════ HELPERS ═════════════════════════════════════

        private void ReloadTemplates()
        {
            var refs = new List<TemplateRef> { new() { Id = null, Name = "(Auto / learned memory)" } };
            try
            {
                // Show every non-auto template regardless of client scope so the user can always
                // see all their saved templates. The scope label ("Client" / "Global") in the name
                // helps distinguish them.
                refs.AddRange(new TemplateStore().LoadAll()
                    .Where(t => !t.IsAuto)
                    .Select(t => new TemplateRef { Id = t.Id, Name = $"{t.Name} ({t.ScopeLabel})" }));
            }
            catch (Exception ex) { Logging.AppLog.Warn("Loading templates for job editor failed", ex); }
            var keep = (_template.SelectedItem as TemplateRef)?.Id;
            _template.Items.Clear();
            _template.Items.AddRange(refs.Cast<object>().ToArray());
            SelectTemplate(keep);
        }

        private void SelectClient(string id)
        {
            for (int i = 0; i < _client.Items.Count; i++)
                if (_client.Items[i] is ClientRef c && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                { _client.SelectedIndex = i; return; }
            _client.SelectedIndex = -1;
        }

        private void SelectTemplate(string? id)
        {
            for (int i = 0; i < _template.Items.Count; i++)
                if (_template.Items[i] is TemplateRef t && string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
                { _template.SelectedIndex = i; return; }
            if (_template.Items.Count > 0) _template.SelectedIndex = 0;
        }

        private void ToggleScheduleRows()
        {
            var kind = (ScheduleKind)Math.Max(0, _schedKind.SelectedIndex);
            _interval.Enabled = kind == ScheduleKind.EveryNMinutes;
            bool usesTime = kind is ScheduleKind.Hourly or ScheduleKind.Daily or ScheduleKind.Weekly;
            _hour.Enabled   = usesTime && kind != ScheduleKind.Hourly;
            _minute.Enabled = usesTime;
            bool weekly = kind == ScheduleKind.Weekly;
            foreach (var d in _days)
            {
                d.Enabled   = weekly;
                d.ForeColor = weekly ? AppleTheme.TextPrimary : AppleTheme.TextSecondary;
            }
            _timeLabel.Text = kind == ScheduleKind.Hourly ? "Minute past hour" : "Time of day";
        }

        private void ToggleSourceRows()
        {
            if (_sourceTlp == null) return;
            bool isRemote = _sourceKind.SelectedIndex > 0;
            bool isFtp    = _sourceKind.SelectedIndex == 2;
            if (_sourceKind.SelectedIndex == 1 && _remotePort.Value == 21) _remotePort.Value = 22;
            if (_sourceKind.SelectedIndex == 2 && _remotePort.Value == 22) _remotePort.Value = 21;

            SetRowVisible(_sourceTlp, _rowLocalFolder,  !isRemote);
            SetRowVisible(_sourceTlp, _rowRemoteHost,    isRemote);
            SetRowVisible(_sourceTlp, _rowRemotePort,    isRemote);
            SetRowVisible(_sourceTlp, _rowRemoteUser,    isRemote);
            SetRowVisible(_sourceTlp, _rowRemotePass,    isRemote);
            SetRowVisible(_sourceTlp, _rowRemoteFolder,  isRemote);
            SetRowVisible(_sourceTlp, _rowRemoteTls,     isFtp);
            SetRowVisible(_sourceTlp, _rowRemotePost,    isRemote);
            SetRowVisible(_sourceTlp, _rowLocalPost,    !isRemote);

            bool showSub = !isRemote && (PostProcessAction)Math.Max(0, _post.SelectedIndex) == PostProcessAction.Move;
            SetRowVisible(_sourceTlp, _rowProcessed, showSub);
            SetRowVisible(_sourceTlp, _rowFailed,    showSub);
        }

        private void TogglePostRows()
        {
            if (_sourceTlp == null) return;
            bool move = _sourceKind.SelectedIndex == 0
                     && (PostProcessAction)Math.Max(0, _post.SelectedIndex) == PostProcessAction.Move;
            SetRowVisible(_sourceTlp, _rowProcessed, move);
            SetRowVisible(_sourceTlp, _rowFailed,    move);
        }

        private static void SetRowVisible(TableLayoutPanel t, int rowIdx, bool visible, int normalHeight = 40)
        {
            if (rowIdx < 0 || rowIdx >= t.RowStyles.Count) return;
            t.RowStyles[rowIdx] = new RowStyle(SizeType.Absolute, visible ? normalHeight : 0);
            foreach (Control c in t.Controls)
                if (t.GetRow(c) == rowIdx) c.Visible = visible;
            t.PerformLayout();
        }

        private void UpdateNextRunDisplay(ScheduledJob j)
        {
            if (j.Schedule.Kind == ScheduleKind.Manual)
            { _nextRunLabel.Text = "Manual — triggered on demand only"; _nextRunLabel.ForeColor = AppleTheme.TextSecondary; return; }
            var next = j.Schedule.NextRunAfter(DateTime.Now);
            _nextRunLabel.Text      = next.HasValue ? next.Value.ToString("ddd d MMM yyyy  HH:mm") : "—";
            _nextRunLabel.ForeColor = AppleTheme.TextPrimary;
        }

        private void UpdateLastRunDisplay(ScheduledJob j)
        {
            if (string.IsNullOrEmpty(j.LastRunUtc))
            { _lastRunLabel.Text = "Never run"; _lastRunLabel.ForeColor = AppleTheme.TextSecondary; return; }
            string timeText = DateTime.TryParse(j.LastRunUtc, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToLocalTime().ToString("ddd d MMM yyyy  HH:mm") : j.LastRunUtc;
            bool fail = !string.IsNullOrEmpty(j.LastResult)
                     && j.LastResult.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);
            _lastRunLabel.Text      = string.IsNullOrEmpty(j.LastResult) ? timeText : $"{timeText}  ·  {j.LastResult}";
            _lastRunLabel.ForeColor = fail ? AppleTheme.Warning : AppleTheme.TextPrimary;
        }

        private void UpdateTaskStatus(ScheduledJob j)
        {
            if (j.Schedule.Kind == ScheduleKind.Manual)
            { _taskStatusLabel.Text = "Manual schedule — no Windows task needed"; _taskStatusLabel.ForeColor = AppleTheme.TextSecondary; return; }
            if (string.IsNullOrEmpty(j.CreatedUtc))
            { _taskStatusLabel.Text = "Save the job first"; _taskStatusLabel.ForeColor = AppleTheme.TextSecondary; return; }
            if (!OperatingSystem.IsWindows())
            { _taskStatusLabel.Text = "Windows only"; _taskStatusLabel.ForeColor = AppleTheme.TextSecondary; return; }
            try
            {
                bool exists = WindowsTaskScheduler.Exists(j);
                _taskStatusLabel.Text      = exists ? "✓  Windows task registered" : "⚠  Not scheduled — use Save & schedule";
                _taskStatusLabel.ForeColor = exists ? AppleTheme.Success : AppleTheme.Warning;
            }
            catch { _taskStatusLabel.Text = "Could not query Task Scheduler"; _taskStatusLabel.ForeColor = AppleTheme.TextSecondary; }
        }

        private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        private void Warn(string m) => MessageBox.Show(this, m, "CargoSync", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        // ═══════════════════ TableLayoutPanel HELPERS ═════════════════════════

        private static TableLayoutPanel MakeTlp()
        {
            var t = new TableLayoutPanel
            {
                ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(40, 28, 40, 24), BackColor = AppleTheme.Canvas
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return t;
        }

        private static Panel Scroll(TableLayoutPanel tlp)
        {
            var p = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, AutoScroll = true };
            tlp.Dock = DockStyle.Top;
            p.Controls.Add(tlp);
            p.SizeChanged += (_, __) => { if (tlp.Width != p.ClientSize.Width) tlp.Width = p.ClientSize.Width; };
            return p;
        }

        private void AddSection(TableLayoutPanel t, string text)
        {
            // The first section in each tab already gets spacing from MakeTlp()'s top padding.
            // Only add a spacer between consecutive sections so sections don't look double-padded.
            if (t.RowCount > 0)
            {
                var sp = new Panel { BackColor = AppleTheme.Canvas };
                int spRow = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                t.Controls.Add(sp, 0, spRow); t.SetColumnSpan(sp, 2);
            }

            var lbl = new Label { Text = text, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Headline,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, Padding = new Padding(0, 0, 0, 4) };
            int lblRow = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            t.Controls.Add(lbl, 0, lblRow); t.SetColumnSpan(lbl, 2);

            var hr = new Panel { BackColor = AppleTheme.Hairline };
            int hrRow = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            t.Controls.Add(hr, 0, hrRow); t.SetColumnSpan(hr, 2);

            // Gap below the divider so the first field in each section has breathing room.
            var gap = new Panel { BackColor = AppleTheme.Canvas };
            int gapRow = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
            t.Controls.Add(gap, 0, gapRow); t.SetColumnSpan(gap, 2);
        }

        private void AddFullSpan(TableLayoutPanel t, Control control)
        {
            control.Margin = new Padding(0, 8, 0, 8);
            int row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            t.Controls.Add(control, 0, row); t.SetColumnSpan(control, 2);
        }

        private void AddRow(TableLayoutPanel t, string labelText, Control control)
        {
            var lbl = new Label { Text = labelText, ForeColor = AppleTheme.TextSecondary,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 5, 0, 5);
            int row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            t.Controls.Add(lbl, 0, row); t.Controls.Add(control, 1, row);
        }

        private void AddRow(TableLayoutPanel t, Label label, Control control)
        {
            label.ForeColor = AppleTheme.TextSecondary; label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleLeft;
            control.Dock = DockStyle.Fill; control.Margin = new Padding(0, 5, 0, 5);  // 38px content — same as string overload; 7px was clipping NumericUpDown
            int row = t.RowCount++; t.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            t.Controls.Add(label, 0, row); t.Controls.Add(control, 1, row);
        }

        private int AddRowTracked(TableLayoutPanel t, string labelText, Control control)
        {
            AddRow(t, labelText, control);
            return t.RowCount - 1;
        }

        private sealed class TemplateRef
        {
            public string? Id   { get; init; }
            public string  Name { get; init; } = string.Empty;
            public override string ToString() => Name;
        }
    }
}
