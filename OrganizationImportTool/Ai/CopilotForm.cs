using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Ai
{
    /// <summary>
    /// Chat window for the in-app AI assistant. Grounded with the current file + mapping context so
    /// answers are about THIS import. Multi-turn; suggested prompts for discoverability.
    /// </summary>
    public class CopilotForm : Form
    {
        private readonly AiCopilot _copilot;
        private readonly string _context;
        private readonly List<(string role, string text)> _history = new();

        private RichTextBox _transcript = null!;
        private Guna2TextBox _input = null!;
        private Guna2Button _send = null!;
        private Spinner _spinner = null!;
        private bool _busy;

        public CopilotForm(AiRouter router, FieldContract contract, SourceTable table, MappingResult result)
        {
            _copilot = new AiCopilot(router);
            _context = AiCopilot.BuildContext(contract, table, result);
            BuildUi();
            Greet(table);
        }

        private void BuildUi()
        {
            Text = "CargoSync Copilot";
            ClientSize = new Size(620, 680);
            MinimumSize = new Size(480, 520);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;      // a minimized owned modal "vanishes" behind the main window
            ShowInTaskbar = false;
            AppleTheme.ApplyWindow(this);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = AppleTheme.Canvas };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(80)));   // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // transcript
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(48)));   // suggestion chips (one row)
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(76)));   // input

            var header = new Panel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas };
            var title = new Label { Text = "CargoSync Copilot ✨", Dock = DockStyle.Top, Height = LogicalToDeviceUnits(52), Padding = new Padding(16, 6, 0, 2), Font = AppleTheme.Title, ForeColor = AppleTheme.Accent, TextAlign = ContentAlignment.MiddleLeft };
            var sub = new Label { Text = "Ask about your file, mapping, risks or rules", Dock = DockStyle.Top, Height = 22, Padding = new Padding(18, 0, 0, 0), Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary };
            header.Controls.Add(sub);
            header.Controls.Add(title);

            _transcript = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None,
                BackColor = AppleTheme.Surface, ForeColor = AppleTheme.TextPrimary,
                Font = new Font("Segoe UI", 10.5f), Margin = new Padding(14, 4, 14, 4), DetectUrls = false
            };

            // Short labels so all four chips sit on ONE aligned row.
            var chips = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = AppleTheme.Canvas, Padding = new Padding(14, 6, 14, 4), AutoScroll = false };
            foreach (var s in new[] { "Explain mapping", "What's risky?", "Suggest a rule", "Unmapped?" })
                chips.Controls.Add(Chip(s));

            // Input row as a 3-column table → text box | spinner | Send, with guaranteed gaps.
            var inputPanel = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = AppleTheme.Canvas, Padding = new Padding(14, 8, 14, 14), ColumnCount = 3, RowCount = 1 };
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
            inputPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            _input = GunaUi.TextBox("Ask the copilot…");
            _input.Dock = DockStyle.Fill; _input.Multiline = true; _input.Margin = new Padding(0, 0, 8, 0);
            _input.KeyDown += Input_KeyDown;
            _send = GunaUi.Button("Send", primary: true); _send.Dock = DockStyle.Fill; _send.Margin = new Padding(0); _send.Click += async (s, e) => await SendAsync();
            _spinner = new Spinner { Size = new Size(26, 26), Visible = false, Anchor = AnchorStyles.None };   // centered next to Send while the copilot thinks
            inputPanel.Controls.Add(_input, 0, 0);
            inputPanel.Controls.Add(_spinner, 1, 0);
            inputPanel.Controls.Add(_send, 2, 0);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(_transcript, 0, 1);
            root.Controls.Add(chips, 0, 2);
            root.Controls.Add(inputPanel, 0, 3);
            Controls.Add(root);
            FormAnimator.FadeIn(this);
        }

        private Guna2Button Chip(string text)
        {
            var b = new Guna2Button
            {
                Text = text, AutoSize = false, Size = new Size(Math.Max(86, TextRenderer.MeasureText(text, AppleTheme.Body).Width + 22), 32),
                FillColor = AppleTheme.SecondaryFill, ForeColor = AppleTheme.TextPrimary, Font = AppleTheme.Body,
                BorderRadius = 14, Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand
            };
            b.HoverState.FillColor = AppleTheme.Accent;
            b.Click += async (s, e) => { if (!_busy) { _input.Text = text; await SendAsync(); } };
            return b;
        }

        private void Greet(SourceTable table)
        {
            Append("Copilot", AppleTheme.Success,
                $"Hi! I can see your file \"{table.SourceName}\" ({table.RowCount} rows). Ask me anything about the mapping, " +
                "what's still unmapped, why something's risky, or how to write a rule.");
        }

        private void Input_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift)
            {
                e.SuppressKeyPress = true;
                _ = SendAsync();
            }
        }

        private async Task SendAsync()
        {
            if (_busy) return;
            string q = _input.Text.Trim();
            if (q.Length == 0) return;

            _input.Text = string.Empty;
            Append("You", AppleTheme.Accent, q);
            _history.Add(("user", q));
            SetBusy(true);

            string answer = await _copilot.AskAsync(q, _context, _history);

            _history.Add(("assistant", answer));
            Append("Copilot", AppleTheme.Success, answer);
            SetBusy(false);
            _input.Focus();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _send.Enabled = !busy;
            _send.Text = busy ? "…" : "Send";
            _spinner.Visible = busy;
            if (busy) _spinner.Start(); else _spinner.Stop();
        }

        private void Append(string who, Color whoColor, string text)
        {
            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.SelectionColor = whoColor;
            _transcript.SelectionFont = new Font("Segoe UI", 10.5f, FontStyle.Bold);
            _transcript.AppendText(who + "\n");
            _transcript.SelectionColor = AppleTheme.TextPrimary;
            _transcript.SelectionFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
            _transcript.AppendText(text + "\n\n");
            _transcript.SelectionStart = _transcript.TextLength;
            _transcript.ScrollToCaret();
        }
    }
}
