using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Drives <see cref="ImportPipeline"/> for an unattended scheduled run. Unlike the verification
    /// harness it applies the job's <b>bound template</b>, honours the job's duplicate/cleaning policy,
    /// and (critically for production) <b>skips already-imported rows</b> on resume so a re-run never
    /// re-creates organizations. All narration is captured for the per-run log.
    /// </summary>
    public sealed class JobPipelineUi : IPipelineUi
    {
        private readonly ScheduledJob _job;
        private readonly TemplateStore _templates;
        private readonly Action<string>? _sink;

        public List<string> Lines { get; } = new();

        public JobPipelineUi(ScheduledJob job, TemplateStore templates, Action<string>? sink = null)
        {
            _job = job;
            _templates = templates;
            _sink = sink;
        }

        public Task<MappingResult?> ConfirmMappingAsync(FieldContract contract, SourceTable table,
            MappingResult suggested, string clientId, TemplateStore templates)
        {
            if (!string.IsNullOrWhiteSpace(_job.TemplateId))
            {
                var tmpl = _templates.LoadAll().FirstOrDefault(t =>
                    string.Equals(t.Id, _job.TemplateId, StringComparison.OrdinalIgnoreCase));
                if (tmpl != null)
                {
                    TemplateMapper.Apply(tmpl, table, contract, suggested);
                    Log($"  [job: applied bound template \"{tmpl.Name}\"]");
                }
                else
                {
                    Log($"  [job: bound template '{_job.TemplateId}' not found - using auto-mapping]");
                }
            }

            foreach (var c in suggested.Columns) c.Approved = true; // no human to approve; policy is the template/auto-map
            return Task.FromResult<MappingResult?>(suggested);
        }

        public Task<GateNav> ConfirmProfileAsync(ProfileReport report)
        {
            Log($"  [job: profile risk {report.Level} (score {report.Score}/100), {report.BlockingRows} blocking]");
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups)
        {
            if (!_job.SkipDuplicates)
            {
                Log("  [job: duplicate-skipping off - importing all rows]");
                return Task.FromResult(new DuplicateDecision { SkipDuplicates = false });
            }
            var skip = new HashSet<int>();
            foreach (var g in groups)
                foreach (var ex in g.Extras) skip.Add(ex.RowNumber);
            Log($"  [job: auto-skipping {skip.Count} duplicate row(s)]");
            return Task.FromResult(new DuplicateDecision { SkipDuplicates = true, RowsToSkip = skip });
        }

        public Task<GateNav> ReviewCleaningAsync(List<CleaningChange> changes)
        {
            // CleaningChange.Accept defaults to true, so "off" must explicitly clear it - otherwise an
            // unattended run would apply fixes the operator's policy says to leave alone.
            foreach (var ch in changes) ch.Accept = _job.AutoApplyCleaning;
            Log(_job.AutoApplyCleaning
                ? $"  [job: auto-accepting {changes.Count} cleaning fix(es)]"
                : $"  [job: auto-cleaning off - {changes.Count} fix(es) left unapplied]");
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<GateNav> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions)
        {
            // EnrichmentSuggestion.Accept defaults to true - clear it when the policy is off.
            // Uses AutoApplyEnrichment, not AutoApplyCleaning — they are independent settings.
            foreach (var s in suggestions) s.Accept = _job.AutoApplyEnrichment;
            Log(_job.AutoApplyEnrichment
                ? $"  [job: auto-accepting {suggestions.Count} enrichment(s)]"
                : $"  [job: auto-enrichment off - {suggestions.Count} suggestion(s) left unapplied]");
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<GateNav> ConfirmSendAsync(int rowsToSend, int totalRows, bool dryRun, string environment, string clientName)
        {
            Log($"  [job: {(dryRun ? "DRY RUN" : "SEND")} {rowsToSend}/{totalRows} to {environment}]");
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<ResumeChoice> ConfirmResumeAsync(int alreadyImported, int totalRows, string? crashedRunDescription)
        {
            if (crashedRunDescription != null) Log("  " + crashedRunDescription);
            Log($"  [job: {alreadyImported}/{totalRows} already imported - skipping them]");
            return Task.FromResult(ResumeChoice.SkipAlreadyImported); // never re-send unattended
        }

        public void Log(string line) { Lines.Add(line); _sink?.Invoke(line); }
        public void Status(string text) { }
        public void Progress(int current, int total) { }
        public Task WaitIfPausedAsync(int processed, int total, CancellationToken ct) => Task.CompletedTask;
    }
}
