using System.Collections.Generic;
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
    /// <summary>What to do with rows the sync ledger says were already imported successfully.</summary>
    public enum ResumeChoice
    {
        /// <summary>Skip the already-imported rows (recommended; avoids duplicate orgs under code-generation).</summary>
        SkipAlreadyImported,
        /// <summary>Send everything again (CargoWise MERGEs by code; regenerated codes may duplicate).</summary>
        ResendAll,
        Cancel
    }

    /// <summary>How the operator left a review step: continue, go back one step, or abort the run.</summary>
    public enum GateNav { Proceed, Back, Cancel }

    /// <summary>The operator's decision at the duplicate-review gate.</summary>
    public sealed class DuplicateDecision
    {
        public bool Cancelled { get; set; }
        /// <summary>Operator chose to go back to the previous step.</summary>
        public bool Back { get; set; }
        public bool SkipDuplicates { get; set; }
        public HashSet<int> RowsToSkip { get; set; } = new();
    }

    /// <summary>
    /// The seam between the import pipeline (pure orchestration) and whoever is driving it:
    /// the WinForms app shows the real review dialogs, the CLI harness auto-approves, and
    /// tests script the answers. Gate methods return null/false/Cancelled to abort the run.
    /// </summary>
    public interface IPipelineUi
    {
        /// <summary>The mandatory human mapping gate. Null = operator cancelled.</summary>
        Task<MappingResult?> ConfirmMappingAsync(FieldContract contract, SourceTable table,
            MappingResult suggested, string clientId, TemplateStore templates);

        /// <summary>Data-health dashboard. Back returns to the mapping step.</summary>
        Task<GateNav> ConfirmProfileAsync(ProfileReport report);

        /// <summary>Duplicate review (only called when groups exist).</summary>
        Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups);

        /// <summary>Cleaning review - implementations mutate each change's Accept flag.</summary>
        Task<GateNav> ReviewCleaningAsync(List<CleaningChange> changes);

        /// <summary>Enrichment review - implementations mutate each suggestion's Accept flag.</summary>
        Task<GateNav> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions);

        /// <summary>
        /// The explicit final gate before anything is transmitted: the operator sees how many
        /// organizations will be sent (and where) and must press "Send to CargoWise" - or run
        /// the dry-run simulation - to proceed. Back returns to the last review step.
        /// </summary>
        Task<GateNav> ConfirmSendAsync(int rowsToSend, int totalRows, bool dryRun, string environment, string clientName);

        /// <summary>
        /// Some rows were already imported successfully on a previous run (and/or a previous run
        /// of this exact file crashed mid-way). Skip them, re-send everything, or cancel?
        /// </summary>
        Task<ResumeChoice> ConfirmResumeAsync(int alreadyImported, int totalRows, string? crashedRunDescription);

        /// <summary>One line of operator-visible progress narration.</summary>
        void Log(string line);

        /// <summary>Short live status (the counter label in the UI).</summary>
        void Status(string text);

        /// <summary>Row-level progress through the send loop.</summary>
        void Progress(int current, int total);

        /// <summary>Hold here while the operator has paused the run.</summary>
        Task WaitIfPausedAsync(int processed, int total, CancellationToken ct);
    }
}
