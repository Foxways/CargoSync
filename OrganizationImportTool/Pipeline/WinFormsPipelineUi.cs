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
        private readonly TextBox _logBox;
        private readonly Label _status;
        private readonly Guna2ProgressBar _progress;
        private readonly Func<bool> _isPaused;
        private readonly AiRouter? _aiRouter;

        public WinFormsPipelineUi(Form owner, TextBox logBox, Label status, Guna2ProgressBar progress,
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
            return Task.FromResult(mapForm.ShowDialog(_owner) == DialogResult.OK ? mapForm.ConfirmedResult : null);
        }

        public Task<bool> ConfirmProfileAsync(ProfileReport report)
        {
            using var dash = new ProfileDashboardForm(report);
            return Task.FromResult(dash.ShowDialog(_owner) == DialogResult.OK);
        }

        public Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups)
        {
            using var dlg = new DuplicateReviewForm(groups);
            if (dlg.ShowDialog(_owner) != DialogResult.OK)
                return Task.FromResult(new DuplicateDecision { Cancelled = true });
            return Task.FromResult(new DuplicateDecision
            {
                SkipDuplicates = dlg.SkipDuplicates,
                RowsToSkip = dlg.SkipDuplicates ? dlg.RowsToSkip : new HashSet<int>()
            });
        }

        public Task<bool> ReviewCleaningAsync(List<CleaningChange> changes)
        {
            using var dlg = new DataCleaningForm(changes);
            return Task.FromResult(dlg.ShowDialog(_owner) == DialogResult.OK);
        }

        public Task<bool> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions)
        {
            using var dlg = new EnrichmentReviewForm(suggestions);
            return Task.FromResult(dlg.ShowDialog(_owner) == DialogResult.OK);
        }

        public void Log(string line) => _logBox.AppendText(line + "\r\n");

        public void Status(string text) => _status.Text = text;

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
