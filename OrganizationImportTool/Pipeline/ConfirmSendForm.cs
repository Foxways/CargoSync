using System.Drawing;
using System.Windows.Forms;
using OrganizationImportTool.Ui;

namespace OrganizationImportTool.Pipeline
{
    /// <summary>
    /// The explicit final gate before anything is transmitted. Makes the moment of sending
    /// unmistakable: the primary button literally says "Send to CargoWise" (or "Start dry run"
    /// in simulation mode), with the row count and target environment spelled out.
    /// OK = send, Retry = back to the reviews, Cancel = abort.
    /// </summary>
    public class ConfirmSendForm : Form
    {
        public ConfirmSendForm(int rowsToSend, int totalRows, bool dryRun, string environment, string clientName)
        {
            Text = dryRun ? "Ready to simulate" : "Ready to send";
            ClientSize = new Size(580, 248);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false; MinimizeBox = false;
            AppleTheme.ApplyWindow(this);

            var card = GunaUi.Card(); card.Dock = DockStyle.Fill; card.Padding = new Padding(28, 22, 28, 18);

            var title = new Label
            {
                Text = dryRun ? "Ready to run the simulation" : "Ready to send to CargoWise",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(40),
                Font = AppleTheme.Title, ForeColor = AppleTheme.TextPrimary
            };

            int skipped = totalRows - rowsToSend;
            string detail = dryRun
                ? $"{rowsToSend} organization(s) will be rehearsed for \"{clientName}\" ({environment}). " +
                  "Nothing is transmitted in a dry run."
                : $"{rowsToSend} organization(s) will be sent to CargoWise for \"{clientName}\" ({environment})." +
                  (skipped > 0 ? $" {skipped} row(s) are skipped (duplicates / already imported)." : "");
            var body = new Label
            {
                Text = detail + "\n\nThis is the last step — nothing has been transmitted yet.",
                Dock = DockStyle.Top, Height = LogicalToDeviceUnits(96),
                Font = AppleTheme.Body, ForeColor = AppleTheme.TextSecondary
            };

            var send = GunaUi.Button(dryRun ? "Start dry run" : "Send to CargoWise", primary: true);
            send.Size = new Size(TextRenderer.MeasureText(send.Text, send.Font).Width + 60, 34); send.DialogResult = DialogResult.OK;
            var back = GunaUi.Button("← Back", primary: false);
            back.Size = new Size(TextRenderer.MeasureText(back.Text, back.Font).Width + 60, 34); back.DialogResult = DialogResult.Retry; back.Margin = new Padding(10, 0, 0, 0);
            var cancel = GunaUi.Button("Cancel import", primary: false);
            cancel.Size = new Size(TextRenderer.MeasureText(cancel.Text, cancel.Font).Width + 60, 34); cancel.DialogResult = DialogResult.Cancel; cancel.Margin = new Padding(10, 0, 0, 0);
            var bar = GunaUi.ButtonBar(new Control[] { send, cancel, back });

            card.Controls.Add(body);
            card.Controls.Add(title);
            Controls.Add(card);
            Controls.Add(bar);
            AcceptButton = send;
            CancelButton = cancel;
            FormAnimator.FadeIn(this);
        }
    }
}
