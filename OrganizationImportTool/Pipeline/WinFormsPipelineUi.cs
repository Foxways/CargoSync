using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Guna.UI2.WinForms;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Pipeline
{
    /// <summary>
    /// The real application's pipeline driver: shows the existing review dialogs modally and
    /// narrates progress into Form1's log box / counter / progress bar. Must be used from the
    /// UI thread (the pipeline awaits keep the message pump alive between dialogs).
    /// </summary>
    public sealed class WinFormsPipelineUi : IPipelineUi
    {
        private readonly Form _owner;
        private readonly RichTextBox _logBox;
        private readonly Label _status;
        private readonly Guna2ProgressBar _progress;
        private readonly Func<bool> _isPaused;
        private readonly AiRouter? _aiRouter;

        public WinFormsPipelineUi(Form owner, RichTextBox logBox, Label status, Guna2ProgressBar progress,
            Func<bool> isPaused, AiRouter? aiRouter)
        {
            _owner = owner;
            _logBox = logBox;
            _status = status;
            _progress = progress;
            _isPaused = isPaused;
            _aiRouter = aiRouter;
        }

        public Task<MappingResult?> ConfirmMappingAsync(FieldContract contract, SourceTable table,
            MappingResult suggested, string clientId, TemplateStore templates)
        {
            using var mapForm = new MappingForm(contract, table, suggested, clientId, templates, _aiRouter);
            Ui.StepBanner.Attach(mapForm, 1, 6, "Confirm the column mapping",
                "Check each column goes to the right CargoWise field. Cancel stops the whole import.", "step-mapping");
            return Task.FromResult(mapForm.ShowDialog(_owner) == DialogResult.OK ? mapForm.ConfirmedResult : null);
        }

        /// <summary>Back buttons on the review forms return DialogResult.Retry.</summary>
        private static GateNav ToNav(DialogResult r) => r switch
        {
            DialogResult.OK => GateNav.Proceed,
            DialogResult.Retry => GateNav.Back,
            _ => GateNav.Cancel
        };

        public Task<GateNav> ConfirmProfileAsync(ProfileReport report)
        {
            using var dash = new ProfileDashboardForm(report);
            Ui.StepBanner.Attach(dash, 2, 6, "Data health check",
                "← Back returns to the column mapping.", "step-profile");
            return Task.FromResult(ToNav(dash.ShowDialog(_owner)));
        }

        public Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups)
        {
            using var dlg = new DuplicateReviewForm(groups);
            Ui.StepBanner.Attach(dlg, 3, 6, "Review possible duplicates",
                "This step only appears when duplicates were found. ← Back returns to the data health check.", "step-duplicates");
            var nav = ToNav(dlg.ShowDialog(_owner));
            if (nav != GateNav.Proceed)
                return Task.FromResult(new DuplicateDecision { Cancelled = nav == GateNav.Cancel, Back = nav == GateNav.Back });
            return Task.FromResult(new DuplicateDecision
            {
                SkipDuplicates = dlg.SkipDuplicates,
                RowsToSkip = dlg.SkipDuplicates ? dlg.RowsToSkip : new HashSet<int>()
            });
        }

        public Task<GateNav> ReviewCleaningAsync(List<CleaningChange> changes)
        {
            using var dlg = new DataCleaningForm(changes);
            Ui.StepBanner.Attach(dlg, 4, 6, "Review data cleaning",
                "Only ticked fixes are applied — your data is sent as-is by default. ← Back returns to the previous step.", "step-cleaning");
            return Task.FromResult(ToNav(dlg.ShowDialog(_owner)));
        }

        public Task<GateNav> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions)
        {
            using var dlg = new EnrichmentReviewForm(suggestions);
            Ui.StepBanner.Attach(dlg, 5, 6, "Fill empty fields",
                "Only empty fields are ever filled, and only the ones you tick. ← Back returns to the previous step.", "step-enrichment");
            return Task.FromResult(ToNav(dlg.ShowDialog(_owner)));
        }

        public Task<GateNav> ConfirmSendAsync(int rowsToSend, int totalRows, bool dryRun, string environment, string clientName)
        {
            using var dlg = new ConfirmSendForm(rowsToSend, totalRows, dryRun, environment, clientName);
            return Task.FromResult(ToNav(dlg.ShowDialog(_owner)));
        }

        public Task<ResumeChoice> ConfirmResumeAsync(int alreadyImported, int totalRows, string? crashedRunDescription)
        {
            string message =
                (crashedRunDescription != null ? crashedRunDescription + "\n\n" : "") +
                $"{alreadyImported} of {totalRows} row(s) in this file were already imported to CargoWise successfully.\n\n" +
                "YES — Skip them (recommended): only the remaining rows are sent.\n" +
                "NO — Re-send everything: existing organizations are updated by code\n" +
                "         (if CargoWise auto-generates codes this can create duplicates).\n" +
                "CANCEL — Stop, nothing is sent.";
            var answer = MessageBox.Show(_owner, message, "Some rows were already imported",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1);
            return Task.FromResult(answer switch
            {
                DialogResult.Yes => ResumeChoice.SkipAlreadyImported,
                DialogResult.No => ResumeChoice.ResendAll,
                _ => ResumeChoice.Cancel
            });
        }

        public void Log(string line)
        {
            if (_logBox.InvokeRequired)
                _logBox.BeginInvoke((Action)(() => _logBox.AppendText(line + "\r\n")));
            else
                _logBox.AppendText(line + "\r\n");
        }

        public void Status(string text)
        {
            if (_status.InvokeRequired)
                _status.BeginInvoke((Action)(() => _status.Text = text));
            else
                _status.Text = text;
        }

        public void Progress(int current, int total)
        {
            if (total <= 0) return;
            _progress.Value = Math.Min(_progress.Maximum, (int)(((double)current / total) * 100));
        }

        public async Task WaitIfPausedAsync(int processed, int total, CancellationToken ct)
        {
            while (_isPaused() && !ct.IsCancellationRequested)
            {
                _status.Text = $"Paused at {processed}/{total} — click Resume to continue";
                await Task.Delay(200, CancellationToken.None);
            }
        }
    }
}
