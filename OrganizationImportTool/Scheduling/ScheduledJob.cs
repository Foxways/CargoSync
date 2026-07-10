using System;

namespace OrganizationImportTool.Scheduling
{
    public enum SourceKind
    {
        Local = 0,  // local path or UNC share
        Sftp  = 1,  // SSH File Transfer Protocol
        Ftp   = 2   // FTP or FTPS
    }

    public enum RemotePostProcessAction
    {
        Leave           = 0,  // leave the remote file as-is
        DeleteOnSuccess = 1,  // delete from remote after a clean run; leave on failure
        MoveToSubfolder = 2   // move into processed/ or failed/ subfolder on the remote server
    }

    /// <summary>What to do with a source file once it has been processed.</summary>
    public enum PostProcessAction
    {
        /// <summary>Move the file into the processed / failed sub-folders (default, audit-friendly).</summary>
        Move = 0,
        /// <summary>Delete the file after a fully successful run (still moved to failed on error).</summary>
        Delete = 1,
        /// <summary>Leave the file in place (relies on the sync ledger to avoid re-sending rows).</summary>
        Leave = 2
    }

    /// <summary>When the client/ops notification email is sent.</summary>
    public enum NotifyTrigger
    {
        Never = 0,
        OnFailure = 1,
        Always = 2
    }

    /// <summary>
    /// A fully-configurable, per-client unattended import job: where files come from, which mapping
    /// template to apply, when to run, what to do afterwards, and who to email. Persisted as one JSON
    /// file by <see cref="JobStore"/> (mirroring the template-store convention).
    /// </summary>
    public sealed class ScheduledJob
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Untitled job";
        public bool Enabled { get; set; } = true;

        // ---- Client / destination ----
        /// <summary>Client whose eAdaptor credentials + learned memory this job uses.</summary>
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;

        // ---- Source (Phase 1: a watched local/UNC folder) ----
        public string SourceFolder { get; set; } = string.Empty;
        /// <summary>Glob filter for files to pick up, e.g. "*.xlsx" or "*.csv". Comma-separate for several.</summary>
        public string FilePattern { get; set; } = "*.xlsx";
        public bool Recursive { get; set; }

        public PostProcessAction PostProcess { get; set; } = PostProcessAction.Move;
        /// <summary>Sub-folder name (under SourceFolder) for successfully processed files.</summary>
        public string ProcessedSubfolder { get; set; } = "processed";
        /// <summary>Sub-folder name (under SourceFolder) for files that errored or had blocked rows.</summary>
        public string FailedSubfolder { get; set; } = "failed";

        // ---- Remote source (SFTP / FTP) ----
        public SourceKind SourceKind { get; set; } = SourceKind.Local;
        public string RemoteHost { get; set; } = string.Empty;
        public int RemotePort { get; set; } = 22;
        public string RemoteUser { get; set; } = string.Empty;
        /// <summary>DPAPI-protected (via SecretProtector) remote password.</summary>
        public string RemotePasswordProtected { get; set; } = string.Empty;
        public string RemoteFolder { get; set; } = "/";
        public bool FtpUseTls { get; set; } = true;
        public RemotePostProcessAction RemotePostProcess { get; set; } = RemotePostProcessAction.DeleteOnSuccess;

        // ---- Mapping / policy ----
        /// <summary>Bound mapping template id. Empty = rely on the client's learned memory + fuzzy/AI.</summary>
        public string? TemplateId { get; set; }
        /// <summary>Build &amp; validate only, never transmit (for safe scheduled rehearsals).</summary>
        public bool DryRun { get; set; }
        /// <summary>Skip rows already imported for this client (uses the sync ledger). Recommended on.</summary>
        public bool SkipDuplicates { get; set; } = true;
        /// <summary>Auto-apply deterministic data-cleaning fixes (no operator at the gate).</summary>
        public bool AutoApplyCleaning { get; set; } = true;
        /// <summary>Auto-apply enrichment suggestions (postal codes, country codes) in unattended runs.</summary>
        public bool AutoApplyEnrichment { get; set; } = true;

        // ---- Schedule ----
        public ScheduleSpec Schedule { get; set; } = new ScheduleSpec();

        // ---- Notification ----
        public NotifyTrigger NotifyOn { get; set; } = NotifyTrigger.OnFailure;
        /// <summary>Client-facing recipients (comma/semicolon-separated).</summary>
        public string NotifyClientEmails { get; set; } = string.Empty;
        /// <summary>Internal ops recipients (comma/semicolon-separated).</summary>
        public string NotifyInternalEmails { get; set; } = string.Empty;
        public bool AttachImportLog { get; set; } = true;
        public bool AttachFailedRowsCsv { get; set; } = true;

        // ---- Windows task ----
        /// <summary>Windows account the scheduled task runs as (username only — WTS stores credentials securely).</summary>
        public string RunAsUser { get; set; } = string.Empty;

        // ---- Audit ----
        public string CreatedUtc { get; set; } = string.Empty; // ISO, stamped on first save
        public string LastRunUtc { get; set; } = string.Empty; // ISO, stamped after each run
        /// <summary>One-line outcome of the most recent run, for the job list.</summary>
        public string LastResult { get; set; } = string.Empty;

        public bool HasSource => !string.IsNullOrWhiteSpace(SourceFolder);
        public bool HasRemoteSource => SourceKind != SourceKind.Local;
    }
}
