using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Security;
using OrganizationImportTool.Sync;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Wires a <see cref="ScheduledJob"/> to the real import pipeline and CargoWise eAdaptor for an
    /// unattended run: resolves the client's credentials from data.db, builds a per-file run delegate,
    /// and hands off to the (unit-tested) <see cref="JobRunner"/>. This is the class the
    /// <c>--run-job</c> CLI entry point and the Windows scheduled task invoke.
    /// </summary>
    public static class ScheduledImportService
    {
        private sealed record ClientEAdaptor(
            string Environment, string Url, string SenderId, string Password,
            string LogPath, string CompanyCode, string EnterpriseId);

        /// <summary>CLI entry: <c>CargoSync.exe --run-job &lt;jobId&gt;</c>. Returns the process exit code.</summary>
        public static async Task<int> RunFromCliAsync(string[] args)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.WriteLine("Usage: CargoSync.exe --run-job <jobId>");
                return 1;
            }

            string jobId = args[1];
            var store = new JobStore();
            var job = store.Get(jobId);
            if (job == null)
            {
                Console.WriteLine($"Job '{jobId}' not found.");
                return 3;
            }
            if (!job.Enabled)
            {
                Console.WriteLine($"Job '{job.Name}' is disabled - nothing to do.");
                return 0;
            }

            // Cross-process mutex: if a previous instance of this exact job is still running
            // (e.g. a short-interval task that takes longer than its period), skip rather than
            // racing — avoids duplicate sends to CargoWise from two concurrent processes.
            using var jobMutex = new Mutex(true, $"Global\\CargoSync_Job_{jobId}", out bool acquiredMutex);
            if (!acquiredMutex)
            {
                Console.WriteLine($"Job '{job.Name}' ({jobId}) is already running in another process — skipping this instance.");
                Logging.AppLog.Warn($"Skipped overlapping run of job '{job.Name}' ({jobId}).");
                return 0;
            }

            var now = DateTime.UtcNow;
            string logDir = AppPaths.LogsDir; // resolved after cfg load below; updated there

            // Per-run log file: captures ALL operational output so nothing is lost when running
            // as a headless Windows scheduled task (no console attached).
            var safeName = new string(job.Name.Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray());
            string runLogPath = Path.Combine(AppPaths.LogsDir, $"job-{safeName}-{now:yyyyMMdd-HHmmss}.log");
            var runLogLines = new System.Collections.Generic.List<string>();

            void Log(string msg)
            {
                Console.WriteLine(msg);
                runLogLines.Add($"{DateTime.UtcNow:HH:mm:ss.fff}  {msg}");
            }

            void FlushLog()
            {
                try { File.WriteAllLines(runLogPath, runLogLines, System.Text.Encoding.UTF8); }
                catch (Exception ex) { Logging.AppLog.Warn("Failed to write run log", ex); }
            }

            Log($"=== Scheduled job: {job.Name} (client {job.ClientName}) ===");

            var cfg = LoadClientConfig(job.ClientId);
            if (cfg == null)
            {
                Log($"No eAdaptor configuration found for client '{job.ClientId}'.");
                job.LastRunUtc = now.ToString("o");
                job.LastResult = "Failed: client eAdaptor config missing.";
                store.Save(job, job.CreatedUtc);
                FlushLog();
                return 4;
            }

            logDir = Directory.Exists(cfg.LogPath) ? cfg.LogPath : AppPaths.LogsDir;
            string sourceDesc = job.HasRemoteSource
                ? $"{job.SourceKind} {job.RemoteHost}:{job.RemotePort}{job.RemoteFolder}"
                : job.SourceFolder;
            Log($"Source: {sourceDesc}  pattern: {job.FilePattern}  {(job.DryRun ? "[DRY RUN]" : "")}");

            var contract = FieldContract.Load();
            var aiSettings = AiSettings.Load();
            AiRouter? router = aiSettings.Enabled && aiSettings.FallbackChain.Any()
                ? new AiRouter(aiSettings, new TokenUsageStore())
                : null;
            var templates = new TemplateStore();
            var feedback = new FeedbackStore();
            var reader = new SourceReaderFactory();
            string ownerCode = !string.IsNullOrWhiteSpace(cfg.CompanyCode) ? cfg.CompanyCode.Trim() : contract.OwnerCodeDefault;

            JobRunner.RunFileAsync runFile = async (file, ct) =>
            {
                var ui = new JobPipelineUi(job, templates, Log);
                var pipeline = new ImportPipeline(
                    contract, reader, templates, feedback, router, aiSettings,
                    new EadaptorClient(cfg.Url, cfg.SenderId, cfg.Password), ui);

                var request = new PipelineRequest
                {
                    FilePath = file,
                    ClientId = job.ClientId,
                    ClientName = job.ClientName,
                    Username = "scheduler",
                    OwnerCode = ownerCode,
                    DryRun = job.DryRun,
                    LearnMapping = false,           // unattended runs must not mutate learned memory
                    LogDir = logDir,
                    Environment = cfg.Environment,
                    Url = cfg.Url,
                    SenderId = cfg.SenderId
                };
                Log($"--- {Path.GetFileName(file)} ---");
                return await pipeline.RunAsync(request, ct);
            };

            var smtp = SmtpSettings.Load();
            var notifier = new Notifier();

            // ---- Remote source download (SFTP / FTP) ----
            List<RemoteDownloadResult>? remoteFiles = null;
            string? tempDir = null;

            if (job.HasRemoteSource)
            {
                tempDir = Path.Combine(Path.GetTempPath(), $"CargoSync-{job.Id}-{now:yyyyMMdd-HHmmss}");
                try
                {
                    Log($"Downloading from {job.SourceKind} {job.RemoteHost}:{job.RemotePort}{job.RemoteFolder}");
                    remoteFiles = await RemoteSourceDownloader.DownloadAsync(job, tempDir, Log, CancellationToken.None);
                    Log($"Downloaded {remoteFiles.Count} file(s) to temp dir.");
                }
                catch (Exception ex)
                {
                    Log($"Remote download failed: {ex.Message}");
                    job.LastRunUtc = now.ToString("o");
                    job.LastResult = "Failed: remote download error — " + ex.Message;
                    store.Save(job, job.CreatedUtc);
                    FlushLog();
                    return 4;
                }
            }

            JobRunSummary summary;
            try
            {
                summary = await JobRunner.RunJobAsync(job, runFile, logDir, smtp, notifier, now, CancellationToken.None,
                    preEnumeratedFiles: remoteFiles?.Select(r => r.LocalPath).ToList());
            }
            catch (Exception ex)
            {
                Log("JOB EXCEPTION: " + ex);
                job.LastRunUtc = now.ToString("o");
                job.LastResult = "Failed: " + ex.Message;
                store.Save(job, job.CreatedUtc);
                FlushLog();
                return 4;
            }
            finally
            {
                // Always clean up temp dir regardless of outcome
                if (tempDir != null)
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
            }

            // ---- Remote post-process: mark success/failure then apply server-side policy ----
            if (remoteFiles != null && remoteFiles.Count > 0)
            {
                foreach (var rf in remoteFiles)
                {
                    var fileResult = summary.Files.FirstOrDefault(f =>
                        string.Equals(Path.GetFileName(f.SourceFile), Path.GetFileName(rf.LocalPath), StringComparison.OrdinalIgnoreCase));
                    rf.ProcessedOk = fileResult != null && !fileResult.MovedToFailed && string.IsNullOrEmpty(fileResult.Report.Error);
                }
                await RemoteSourceDownloader.PostProcessAsync(job, remoteFiles, Log, CancellationToken.None);
            }

            store.Save(job, job.CreatedUtc); // persist LastRunUtc / LastResult
            Log("=== " + summary.Message + " ===");
            FlushLog();
            return summary.ExitCode;
        }

        private static ClientEAdaptor? LoadClientConfig(string clientId)
        {
            try
            {
                using var conn = new SQLiteConnection($"Data Source={AppPaths.DbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT Environment, URL, SenderID, Password, LogPath, CompanyCode, EnterpriseID " +
                    "FROM EAdaptors WHERE ClientId=@c", conn);
                cmd.Parameters.AddWithValue("@c", clientId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                return new ClientEAdaptor(
                    Environment: r["Environment"]?.ToString() ?? string.Empty,
                    Url: r["URL"]?.ToString() ?? string.Empty,
                    SenderId: r["SenderID"]?.ToString() ?? string.Empty,
                    Password: SecretProtector.Unprotect(r["Password"]?.ToString()),
                    LogPath: r["LogPath"]?.ToString() ?? string.Empty,
                    CompanyCode: r["CompanyCode"]?.ToString() ?? string.Empty,
                    EnterpriseId: r["EnterpriseID"]?.ToString() ?? string.Empty);
            }
            catch (Exception ex)
            {
                Logging.AppLog.Error($"Loading eAdaptor config for client '{clientId}' failed", ex);
                return null;
            }
        }
    }
}
