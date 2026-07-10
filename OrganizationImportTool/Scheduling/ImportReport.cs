using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Pipeline;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>One blocked/rejected row, summarised for the notification email.</summary>
    public sealed class ReportFailure
    {
        public int RowNumber { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// A provider-agnostic summary of one file's import, built from a <see cref="PipelineResult"/>.
    /// Holds everything the notification email needs so the mail layer never touches pipeline types.
    /// </summary>
    public sealed class ImportReport
    {
        public string JobName { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public bool DryRun { get; set; }

        public int Total { get; set; }
        public int Ok { get; set; }
        public int WouldSend { get; set; }
        public int Warnings { get; set; }
        public int Blocked { get; set; }
        public int SkippedDuplicates { get; set; }
        public int AlreadyImported { get; set; }

        public TimeSpan Elapsed { get; set; }

        /// <summary>Set when the whole run threw before/while processing (vs. per-row blocks).</summary>
        public string? Error { get; set; }

        public List<ReportFailure> Failures { get; } = new();

        public string? ImportLogPath { get; set; }
        public string? FailedRowsCsvPath { get; set; }

        /// <summary>True when nothing went wrong: no run-level error and no blocked rows.</summary>
        public bool IsClean => string.IsNullOrEmpty(Error) && Blocked == 0;

        /// <summary>Build a report from a completed pipeline run.</summary>
        public static ImportReport FromResult(string jobName, string clientName, string fileName, PipelineResult result, bool dryRun)
        {
            var r = new ImportReport
            {
                JobName = jobName,
                ClientName = clientName,
                FileName = fileName,
                DryRun = dryRun,
                Total = result.Outcomes.Count,
                Ok = result.Ok,
                WouldSend = result.WouldSend,
                Warnings = result.WarningCount,
                SkippedDuplicates = result.Outcomes.Count(o => o.Response.IsDuplicate),
                AlreadyImported = result.Outcomes.Count(o => o.Response.IsAlreadyImported),
                Elapsed = result.Elapsed,
                ImportLogPath = result.ImportLogPath
            };

            foreach (var o in result.Outcomes.Where(NeedsAttention))
                r.Failures.Add(new ReportFailure
                {
                    RowNumber = o.RowNumber,
                    Code = o.SentCode,
                    Reason = ReasonFor(o.Response)
                });
            r.Blocked = r.Failures.Count;
            return r;
        }

        /// <summary>
        /// A row needs attention when it's not a success, warning, dry-run-ok, or a skip
        /// (duplicate / already-imported). Shared by the report and the failed-rows CSV export.
        /// </summary>
        public static bool NeedsAttention(OrgSendOutcome o)
        {
            var resp = o.Response;
            return !resp.IsSuccess && !resp.IsWarning && !resp.IsSimulatedOk
                   && !resp.IsDuplicate && !resp.IsAlreadyImported;
        }

        private static string ReasonFor(EadaptorResponse resp)
        {
            if (!string.IsNullOrWhiteSpace(resp.Error)) return resp.Error!.Trim();
            if (!string.IsNullOrWhiteSpace(resp.ProcessingLog)) return resp.ProcessingLog.Trim();
            return resp.Outcome;
        }
    }
}
