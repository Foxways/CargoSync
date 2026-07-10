using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Pipeline;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>The outcome of one file inside a job run.</summary>
    public sealed class JobFileResult
    {
        public string SourceFile { get; set; } = string.Empty;
        public ImportReport Report { get; set; } = new();
        public bool MovedToFailed { get; set; }
        public string? FinalLocation { get; set; }
    }

    /// <summary>Roll-up of a whole job run, used for the log line and the CLI exit code.</summary>
    public sealed class JobRunSummary
    {
        public List<JobFileResult> Files { get; } = new();
        public bool JobError { get; set; }
        public string Message { get; set; } = string.Empty;

        public int FileCount => Files.Count;
        public int FilesWithFailures => Files.Count(f => !f.Report.IsClean);
        public int TotalSent => Files.Sum(f => f.Report.Ok);
        public int TotalBlocked => Files.Sum(f => f.Report.Blocked);

        /// <summary>0 = all good, 2 = some files needed attention, 4 = the job itself failed.</summary>
        public int ExitCode => JobError ? 4 : (FilesWithFailures > 0 ? 2 : 0);
    }

    /// <summary>
    /// Executes a scheduled job over its watched folder. The per-file pipeline run is injected as a
    /// delegate so the orchestration (enumerate → run → report → notify → move) is fully unit-testable;
    /// the CLI wires in a real <see cref="ImportPipeline"/>-backed delegate.
    /// </summary>
    public static class JobRunner
    {
        public delegate Task<PipelineResult> RunFileAsync(string filePath, CancellationToken ct);

        public static async Task<JobRunSummary> RunJobAsync(
            ScheduledJob job, RunFileAsync runFile, string logDir,
            SmtpSettings? smtp, Notifier? notifier, DateTime nowUtc, CancellationToken ct = default,
            IReadOnlyList<string>? preEnumeratedFiles = null)
        {
            var summary = new JobRunSummary();
            job.LastRunUtc = nowUtc.ToString("o");

            if (!job.HasRemoteSource && (!job.HasSource || !Directory.Exists(job.SourceFolder)))
            {
                summary.JobError = true;
                summary.Message = $"Source folder not found: '{job.SourceFolder}'.";
                job.LastResult = summary.Message;
                return summary;
            }

            var files = preEnumeratedFiles != null
                ? new List<string>(preEnumeratedFiles)
                : EnumerateFiles(job);
            if (files.Count == 0)
            {
                summary.Message = "No matching files to process.";
                job.LastResult = summary.Message;
                return summary;
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var fileResult = new JobFileResult { SourceFile = file };
                string name = Path.GetFileName(file);

                try
                {
                    var result = await runFile(file, ct).ConfigureAwait(false);
                    var report = ImportReport.FromResult(job.Name, job.ClientName, name, result, job.DryRun);
                    report.ImportLogPath = result.ImportLogPath;

                    if (job.AttachFailedRowsCsv && report.Blocked > 0)
                    {
                        try
                        {
                            string csv = Path.Combine(logDir,
                                $"{Path.GetFileNameWithoutExtension(name)}-attention-{nowUtc:yyyyMMdd-HHmmss}.csv");
                            report.FailedRowsCsvPath = FailedRowsCsv.Write(csv, result);
                        }
                        catch (Exception ex) { Logging.AppLog.Warn("Attention CSV export failed", ex); }
                    }

                    fileResult.Report = report;
                }
                catch (Exception ex)
                {
                    Logging.AppLog.Error($"Job '{job.Name}' failed on file '{name}'", ex);
                    fileResult.Report = new ImportReport
                    {
                        JobName = job.Name, ClientName = job.ClientName, FileName = name, Error = ex.Message
                    };
                }

                // Notify (best-effort; never fails the run).
                if (notifier != null && smtp != null)
                    await notifier.SendReportAsync(smtp, job, fileResult.Report, ct).ConfigureAwait(false);

                // Move/delete the source file according to policy and outcome.
                // Treat both run-level errors AND partial imports (blocked rows) as "failed"
                // so incomplete files land in the failed subfolder rather than processed.
                bool failed = !string.IsNullOrEmpty(fileResult.Report.Error) || fileResult.Report.Blocked > 0;
                fileResult.MovedToFailed = failed;
                fileResult.FinalLocation = ApplyPostProcess(job, file, failed, nowUtc);

                summary.Files.Add(fileResult);
            }

            summary.Message = $"{summary.FileCount} file(s): {summary.TotalSent} sent, " +
                              $"{summary.TotalBlocked} blocked, {summary.FilesWithFailures} file(s) need attention.";
            job.LastResult = summary.Message;
            return summary;
        }

        /// <summary>Files matching the job's pattern(s), excluding the processed/failed sub-folders.</summary>
        public static List<string> EnumerateFiles(ScheduledJob job)
        {
            var option = job.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var patterns = job.FilePattern
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .DefaultIfEmpty("*.*");

            string processed = Path.Combine(job.SourceFolder, job.ProcessedSubfolder);
            string failed = Path.Combine(job.SourceFolder, job.FailedSubfolder);

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in patterns)
            {
                foreach (var f in Directory.EnumerateFiles(job.SourceFolder, pattern, option))
                {
                    string dir = Path.GetDirectoryName(f) ?? string.Empty;
                    if (IsUnder(dir, processed) || IsUnder(dir, failed)) continue;
                    set.Add(f);
                }
            }
            return set.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsUnder(string dir, string ancestor)
        {
            var d = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            var a = Path.GetFullPath(ancestor).TrimEnd(Path.DirectorySeparatorChar);
            return d.Equals(a, StringComparison.OrdinalIgnoreCase) ||
                   d.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Move (or delete) the processed file; returns its final location, or null if left/deleted.</summary>
        private static string? ApplyPostProcess(ScheduledJob job, string file, bool failed, DateTime nowUtc)
        {
            try
            {
                if (job.PostProcess == PostProcessAction.Leave) return file;

                if (job.PostProcess == PostProcessAction.Delete && !failed)
                {
                    File.Delete(file);
                    return null;
                }

                string subfolder = failed ? job.FailedSubfolder : job.ProcessedSubfolder;
                string destDir = Path.Combine(job.SourceFolder, subfolder);
                Directory.CreateDirectory(destDir);
                string dest = UniquePath(Path.Combine(destDir, Path.GetFileName(file)), nowUtc);
                File.Move(file, dest);
                return dest;
            }
            catch (Exception ex)
            {
                Logging.AppLog.Warn($"Post-process move failed for '{file}'", ex);
                return file; // leave it where it is rather than lose it
            }
        }

        private static string UniquePath(string desired, DateTime nowUtc)
        {
            if (!File.Exists(desired)) return desired;
            string dir = Path.GetDirectoryName(desired) ?? string.Empty;
            string stamp = nowUtc.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(desired)}-{stamp}{Path.GetExtension(desired)}");
        }
    }
}
