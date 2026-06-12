using System.Drawing;
using System.Windows.Forms;

namespace OrganizationImportTool.Ui
{
    /// <summary>
    /// "Step 3 of 6 — Review possible duplicates" strip pinned to the top of each import review
    /// dialog, so the operator always knows where they are in the flow and what Cancel does.
    /// </summary>
    public static class StepBanner
    {
        /// <summary>Attach a step strip to the top of a dialog (call AFTER the form's UI is built).</summary>
        public static void Attach(Form form, int step, int total, string title, string? hint = null, string? helpTopicId = null)
        {
            hint ??= "Cancel stops the whole import — nothing has been sent yet.";

            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = form.LogicalToDeviceUnits(34),
                BackColor = Color.FromArgb(28, 32, 44), // subtly bluer than the canvas
                Padding = new Padding(20, 0, 8, 0)
            };

            var text = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppleTheme.Body,
                ForeColor = AppleTheme.TextSecondary
            };
            // Two-tone text via a second label for the step part keeps it simple and theme-safe.
            var stepLbl = new Label
            {
                Dock = DockStyle.Left,
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppleTheme.Font(10f, FontStyle.Bold),
                ForeColor = AppleTheme.Accent,
                Text = $"Step {step} of {total} — {title}",
                Padding = new Padding(0, form.LogicalToDeviceUnits(8), 12, 0)
            };
            text.Text = hint;
            text.Padding = new Padding(stepLbl.PreferredWidth + 8, 0, 0, 0);

            if (helpTopicId != null)
            {
                var helpBtn = GunaUi.Button("?", primary: false);
                helpBtn.Size = new Size(form.LogicalToDeviceUnits(26), form.LogicalToDeviceUnits(26));
                helpBtn.Dock = DockStyle.Right;
                helpBtn.Margin = new Padding(0, 4, 0, 4);
                helpBtn.Click += (s, e) => Help.HelpForm.Open(form, helpTopicId);
                var helpHost = new Panel { Dock = DockStyle.Right, Width = form.LogicalToDeviceUnits(34), BackColor = Color.Transparent, Padding = new Padding(4) };
                helpHost.Controls.Add(helpBtn);
                helpBtn.Dock = DockStyle.Fill;
                bar.Controls.Add(helpHost);
            }

            bar.Controls.Add(text);
            bar.Controls.Add(stepLbl);

            // Added LAST to the form = docked FIRST: the bar takes the top strip and the
            // existing Fill-docked root keeps the remainder.
            form.Controls.Add(bar);
        }
    }
}
