using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Help
{
    /// <summary>Product, version and support information.</summary>
    public sealed class AboutForm : Form
    {
        public AboutForm()
        {
            Text = "About CargoSync";
            ClientSize = new Size(440, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            AppleTheme.ApplyWindow(this);

            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

            var card = GunaUi.Card();
            card.Dock = DockStyle.Fill;
            card.Padding = new Padding(28, 22, 28, 18);

            var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Color.Transparent };

            var logo = new LogoBadge { Size = new Size(64, 64), Margin = new Padding(0, 4, 0, 10) };
            Label L(string text, Font font, Color color, int padBottom = 4) => new()
            { Text = text, AutoSize = true, Font = font, ForeColor = color, Margin = new Padding(0, 0, 0, padBottom) };

            stack.Controls.Add(logo);
            stack.Controls.Add(L("CargoSync", AppleTheme.Title, AppleTheme.TextPrimary));
            stack.Controls.Add(L($"Version {version}", AppleTheme.Body, AppleTheme.TextSecondary, 12));
            stack.Controls.Add(L("CargoWise Organization Importer", AppleTheme.Headline, AppleTheme.TextPrimary));
            stack.Controls.Add(L("Built and owned by Kishan Manohar", AppleTheme.Body, AppleTheme.TextSecondary));
            stack.Controls.Add(L("Support: kishanmanohar@gmail.com", AppleTheme.Body, AppleTheme.TextSecondary, 12));
            stack.Controls.Add(L("Released under the MIT License.", AppleTheme.Caption, AppleTheme.TextSecondary, 14));

            var guideBtn = GunaUi.Button("Open the user guide", primary: true);
            guideBtn.Size = new Size(190, 40);
            guideBtn.Margin = new Padding(0, 0, 0, 8);
            guideBtn.Click += (s, e) => { HelpForm.Open(Owner ?? (IWin32Window)this); Close(); };

            var closeBtn = GunaUi.Button("Close", primary: false);
            closeBtn.Size = new Size(110, 36);
            closeBtn.DialogResult = DialogResult.Cancel;
            CancelButton = closeBtn;

            stack.Controls.Add(guideBtn);
            stack.Controls.Add(closeBtn);
            card.Controls.Add(stack);
            Controls.Add(card);
            FormAnimator.FadeIn(this);
        }
    }
}
