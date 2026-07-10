using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Pipeline
{
    /// <summary>
    /// Console pipeline driver for the CLI verification harness (--pipeline): runs the IDENTICAL
    /// stages as the app but auto-approves every review gate, printing what each stage decided.
    /// </summary>
    public sealed class HeadlessPipelineUi : IPipelineUi
    {
        public Task<MappingResult?> ConfirmMappingAsync(FieldContract contract, SourceTable table,
            MappingResult suggested, string clientId, TemplateStore templates)
        {
            var included = suggested.Columns.Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath)).ToList();
            foreach (var c in included)
                Console.WriteLine($"  {c.SourceHeader,-16} -> {c.TargetPath,-42} [{c.Confidence}/{c.Source}]{(c.Approved ? "" : "  (needs approval)")}");
            int needAppr = included.Count(c => !c.Approved);
            Console.WriteLine($"  [headless: auto-approving {needAppr} AI/low-confidence mapping(s)]");
            foreach (var c in suggested.Columns) c.Approved = true;
            return Task.FromResult<MappingResult?>(suggested);
        }

        public Task<GateNav> ConfirmProfileAsync(ProfileReport report)
        {
            Console.WriteLine($"  Risk: {report.Level} (score {report.Score}/100)  |  blocking {report.BlockingRows}, duplicates {report.DuplicateRows}, warnings {report.WarningRows}");
            foreach (var f in report.Factors) Console.WriteLine("    • " + f);
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups)
        {
            var skip = new HashSet<int>();
            foreach (var g in groups)
            {
                Console.WriteLine($"  rows [{g.RowList}] ({g.Confidence:P0}) {g.Reason}  ->  keep row {g.Rows[0].RowNumber}");
                foreach (var ex in g.Extras) skip.Add(ex.RowNumber);
            }
            Console.WriteLine($"  [headless: auto-skipping {skip.Count} duplicate row(s)]");
            return Task.FromResult(new DuplicateDecision { SkipDuplicates = true, RowsToSkip = skip });
        }

        public Task<GateNav> ReviewCleaningAsync(List<CleaningChange> changes)
        {
            foreach (var ch in changes)
                Console.WriteLine($"  row{ch.RowNumber} {ch.Path}: \"{ch.Original}\" -> \"{ch.Cleaned}\"  [{ch.Reason}/{ch.Source}]");
            Console.WriteLine($"  [headless: auto-accepting {changes.Count} fix(es)]");
            foreach (var ch in changes) ch.Accept = true;
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<GateNav> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions)
        {
            foreach (var s in suggestions)
                Console.WriteLine($"  row{s.RowNumber} {s.Path}: \"{s.Value}\"  [{s.Source}]");
            Console.WriteLine($"  [headless: auto-accepting {suggestions.Count} enrichment(s)]");
            foreach (var s in suggestions) s.Accept = true;
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<GateNav> ConfirmSendAsync(int rowsToSend, int totalRows, bool dryRun, string environment, string clientName)
        {
            Console.WriteLine($"  [headless: confirming send - {rowsToSend}/{totalRows} row(s), {(dryRun ? "DRY RUN" : "LIVE")} to {environment}]");
            return Task.FromResult(GateNav.Proceed);
        }

        public Task<ResumeChoice> ConfirmResumeAsync(int alreadyImported, int totalRows, string? crashedRunDescription)
        {
            if (crashedRunDescription != null) Console.WriteLine("  " + crashedRunDescription);
            Console.WriteLine($"  [headless: {alreadyImported}/{totalRows} row(s) already imported - re-sending all (MERGE updates)]");
            return Task.FromResult(ResumeChoice.ResendAll); // matches the harness's historical behavior
        }

        public void Log(string line) => Console.WriteLine(line);

        public void Status(string text) { /* the per-line log already narrates progress */ }

        public void Progress(int current, int total) { }

        public Task WaitIfPausedAsync(int processed, int total, CancellationToken ct) => Task.CompletedTask;
    }
}
