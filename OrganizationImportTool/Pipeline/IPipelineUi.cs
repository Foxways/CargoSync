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

    /// <summary>The operator's decision at the duplicate-review gate.</summary>
    public sealed class DuplicateDecision
    {
        public bool Cancelled { get; set; }
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

        /// <summary>Data-health dashboard. False = operator cancelled.</summary>
        Task<bool> ConfirmProfileAsync(ProfileReport report);

        /// <summary>Duplicate review (only called when groups exist).</summary>
        Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups);

        /// <summary>Cleaning review - implementations mutate each change's Accept flag. False = cancelled.</summary>
        Task<bool> ReviewCleaningAsync(List<CleaningChange> changes);

        /// <summary>Enrichment review - implementations mutate each suggestion's Accept flag. False = cancelled.</summary>
        Task<bool> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions);

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
