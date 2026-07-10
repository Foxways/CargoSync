using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using OrganizationImportTool.Security;
using Renci.SshNet;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>Tracks one remote file that was downloaded to a local temp path.</summary>
    public sealed class RemoteDownloadResult
    {
        public string RemotePath  { get; init; } = string.Empty;
        public string LocalPath   { get; init; } = string.Empty;
        /// <summary>Set after the file has been processed through the pipeline.</summary>
        public bool   ProcessedOk { get; set; }
    }

    /// <summary>
    /// Downloads files from SFTP or FTP servers into a local temp directory, and applies the job's
    /// remote post-process policy (leave / delete / move-to-subfolder) after the run.
    /// Uses <see cref="Renci.SshNet"/> for SFTP and <see cref="FluentFTP"/> for FTP/FTPS.
    /// </summary>
    public static class RemoteSourceDownloader
    {
        // ── Public API ──────────────────────────────────────────────────────────

        /// <summary>Download all matching remote files to <paramref name="localTempDir"/>.</summary>
        public static async Task<List<RemoteDownloadResult>> DownloadAsync(
            ScheduledJob job, string localTempDir, Action<string> log, CancellationToken ct)
        {
            Directory.CreateDirectory(localTempDir);
            return job.SourceKind == SourceKind.Sftp
                ? await DownloadSftpAsync(job, localTempDir, log, ct)
                : await DownloadFtpAsync(job, localTempDir, log, ct);
        }

        /// <summary>
        /// Apply the job's <see cref="ScheduledJob.RemotePostProcess"/> policy to each file:
        /// delete successful files, move to processed/failed subfolder, or leave untouched.
        /// Best-effort: individual failures are logged, not thrown.
        /// </summary>
        public static async Task PostProcessAsync(
            ScheduledJob job, IReadOnlyList<RemoteDownloadResult> results,
            Action<string> log, CancellationToken ct)
        {
            if (job.RemotePostProcess == RemotePostProcessAction.Leave || results.Count == 0) return;

            log($"  [remote] applying post-process policy: {job.RemotePostProcess}");
            try
            {
                if (job.SourceKind == SourceKind.Sftp)
                    await PostProcessSftpAsync(job, results, log, ct);
                else
                    await PostProcessFtpAsync(job, results, log, ct);
            }
            catch (Exception ex)
            {
                log($"  [remote] post-process error: {ex.Message}");
                Logging.AppLog.Warn("Remote post-process failed", ex);
            }
        }

        /// <summary>Quick connection test: connect, list the remote folder, disconnect. Returns ok + message.</summary>
        public static async Task<(bool ok, string message)> TestConnectionAsync(ScheduledJob job)
        {
            try
            {
                if (job.SourceKind == SourceKind.Sftp)
                    return await TestSftpAsync(job);
                else
                    return await TestFtpAsync(job);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ── SFTP ────────────────────────────────────────────────────────────────

        private static async Task<List<RemoteDownloadResult>> DownloadSftpAsync(
            ScheduledJob job, string localDir, Action<string> log, CancellationToken ct)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);
            var patterns = ParsePatterns(job.FilePattern);
            var results  = new List<RemoteDownloadResult>();

            await Task.Run(() =>
            {
                using var client = BuildSftpClient(job, password);
                client.Connect();
                log($"  [sftp] connected to {job.RemoteHost}:{job.RemotePort}");

                var excludeDirs = new HashSet<string>(
                    new[] { job.ProcessedSubfolder, job.FailedSubfolder }
                        .Where(s => !string.IsNullOrEmpty(s)),
                    StringComparer.OrdinalIgnoreCase);
                var remoteFiles = ListSftpFiles(client, job.RemoteFolder, job.Recursive, patterns, excludeDirs);
                log($"  [sftp] {remoteFiles.Count} file(s) matched {job.FilePattern}");

                foreach (var remotePath in remoteFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    string localPath = UniquePath(Path.Combine(localDir, Path.GetFileName(remotePath)));
                    try
                    {
                        using var fs = File.Create(localPath);
                        client.DownloadFile(remotePath, fs);
                        log($"  [sftp] ↓ {Path.GetFileName(remotePath)}");
                        results.Add(new RemoteDownloadResult { RemotePath = remotePath, LocalPath = localPath });
                    }
                    catch (Exception ex)
                    {
                        log($"  [sftp] download failed for '{remotePath}': {ex.Message}");
                        Logging.AppLog.Warn($"SFTP download failed: {remotePath}", ex);
                        try { File.Delete(localPath); } catch { }
                    }
                }

                client.Disconnect();
            }, ct);

            return results;
        }

        private static async Task PostProcessSftpAsync(
            ScheduledJob job, IReadOnlyList<RemoteDownloadResult> results,
            Action<string> log, CancellationToken ct)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);

            await Task.Run(() =>
            {
                using var client = BuildSftpClient(job, password);
                client.Connect();

                foreach (var r in results)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (job.RemotePostProcess == RemotePostProcessAction.DeleteOnSuccess)
                        {
                            if (r.ProcessedOk)
                            {
                                client.DeleteFile(r.RemotePath);
                                log($"  [sftp] deleted {Path.GetFileName(r.RemotePath)}");
                            }
                        }
                        else if (job.RemotePostProcess == RemotePostProcessAction.MoveToSubfolder)
                        {
                            string subfolder = r.ProcessedOk ? job.ProcessedSubfolder : job.FailedSubfolder;
                            string destDir   = JoinRemotePath(job.RemoteFolder, subfolder);
                            EnsureSftpDir(client, destDir);
                            string dest = JoinRemotePath(destDir, Path.GetFileName(r.RemotePath));
                            client.RenameFile(r.RemotePath, dest);
                            log($"  [sftp] → {subfolder}/{Path.GetFileName(r.RemotePath)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        log($"  [sftp] post-process failed for '{r.RemotePath}': {ex.Message}");
                    }
                }

                client.Disconnect();
            }, ct);
        }

        private static async Task<(bool ok, string message)> TestSftpAsync(ScheduledJob job)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);
            return await Task.Run(() =>
            {
                using var client = BuildSftpClient(job, password);
                client.Connect();
                var entries = client.ListDirectory(job.RemoteFolder).Take(5).ToList();
                client.Disconnect();
                int fileCount = entries.Count(f => f.IsRegularFile);
                return (true, $"Connected. {fileCount} file(s) in '{job.RemoteFolder}' (first 5 entries checked).");
            });
        }

        private static SftpClient BuildSftpClient(ScheduledJob job, string password)
        {
            var client = new SftpClient(job.RemoteHost, job.RemotePort, job.RemoteUser, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            client.OperationTimeout = TimeSpan.FromMinutes(10); // prevent stalled downloads from hanging forever
            return client;
        }

        private static List<string> ListSftpFiles(
            SftpClient client, string folder, bool recursive, string[] patterns,
            HashSet<string>? excludeDirs = null)
        {
            var files = new List<string>();
            try
            {
                foreach (var entry in client.ListDirectory(folder))
                {
                    if (entry.Name == "." || entry.Name == "..") continue;
                    if (entry.IsDirectory && recursive)
                    {
                        // Skip processed/failed subfolders so already-handled files are never re-downloaded
                        if (excludeDirs != null && excludeDirs.Contains(entry.Name)) continue;
                        files.AddRange(ListSftpFiles(client, entry.FullName, recursive, patterns, excludeDirs));
                    }
                    else if (entry.IsRegularFile && MatchesAny(entry.Name, patterns))
                        files.Add(entry.FullName);
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn($"SFTP list failed for '{folder}'", ex); }
            return files;
        }

        private static void EnsureSftpDir(SftpClient client, string path)
        {
            if (!client.Exists(path)) client.CreateDirectory(path);
        }

        // ── FTP ─────────────────────────────────────────────────────────────────

        private static async Task<List<RemoteDownloadResult>> DownloadFtpAsync(
            ScheduledJob job, string localDir, Action<string> log, CancellationToken ct)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);
            var patterns = ParsePatterns(job.FilePattern);
            var results  = new List<RemoteDownloadResult>();

            using var client = BuildFtpClient(job, password);
            await client.Connect(ct);
            log($"  [ftp] connected to {job.RemoteHost}:{job.RemotePort}");

            var listing = await client.GetListing(job.RemoteFolder,
                job.Recursive ? FtpListOption.Recursive : FtpListOption.Auto, ct);

            var excludeDirNames = new HashSet<string>(
                new[] { job.ProcessedSubfolder, job.FailedSubfolder }
                    .Where(s => !string.IsNullOrEmpty(s)),
                StringComparer.OrdinalIgnoreCase);

            var matchedFiles = listing
                .Where(i => i.Type == FtpObjectType.File
                         && MatchesAny(i.Name, patterns)
                         && !IsInExcludedDir(i.FullName, job.RemoteFolder, excludeDirNames))
                .ToList();
            log($"  [ftp] {matchedFiles.Count} file(s) matched {job.FilePattern}");

            foreach (var item in matchedFiles)
            {
                ct.ThrowIfCancellationRequested();
                string localPath = UniquePath(Path.Combine(localDir, Path.GetFileName(item.FullName)));
                try
                {
                    var status = await client.DownloadFile(localPath, item.FullName,
                        FtpLocalExists.Overwrite, FtpVerify.None, null, ct);
                    if (status == FtpStatus.Success)
                    {
                        log($"  [ftp] ↓ {Path.GetFileName(item.FullName)}");
                        results.Add(new RemoteDownloadResult { RemotePath = item.FullName, LocalPath = localPath });
                    }
                }
                catch (Exception ex)
                {
                    log($"  [ftp] download failed for '{item.FullName}': {ex.Message}");
                    try { File.Delete(localPath); } catch { }
                }
            }

            await client.Disconnect(ct);
            return results;
        }

        private static async Task PostProcessFtpAsync(
            ScheduledJob job, IReadOnlyList<RemoteDownloadResult> results,
            Action<string> log, CancellationToken ct)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);
            using var client = BuildFtpClient(job, password);
            await client.Connect(ct);

            foreach (var r in results)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (job.RemotePostProcess == RemotePostProcessAction.DeleteOnSuccess)
                    {
                        if (r.ProcessedOk)
                        {
                            await client.DeleteFile(r.RemotePath, ct);
                            log($"  [ftp] deleted {Path.GetFileName(r.RemotePath)}");
                        }
                    }
                    else if (job.RemotePostProcess == RemotePostProcessAction.MoveToSubfolder)
                    {
                        string subfolder = r.ProcessedOk ? job.ProcessedSubfolder : job.FailedSubfolder;
                        string destDir   = JoinRemotePath(job.RemoteFolder, subfolder);
                        await client.CreateDirectory(destDir, ct);
                        string dest = JoinRemotePath(destDir, Path.GetFileName(r.RemotePath));
                        await client.MoveFile(r.RemotePath, dest, FtpRemoteExists.Overwrite, ct);
                        log($"  [ftp] → {subfolder}/{Path.GetFileName(r.RemotePath)}");
                    }
                }
                catch (Exception ex)
                {
                    log($"  [ftp] post-process failed for '{r.RemotePath}': {ex.Message}");
                }
            }

            await client.Disconnect(ct);
        }

        private static async Task<(bool ok, string message)> TestFtpAsync(ScheduledJob job)
        {
            string password = SecretProtector.Unprotect(job.RemotePasswordProtected);
            using var client = BuildFtpClient(job, password);
            await client.Connect();
            var listing = await client.GetListing(job.RemoteFolder);
            await client.Disconnect();
            int fileCount = listing.Count(i => i.Type == FtpObjectType.File);
            return (true, $"Connected. {fileCount} file(s) in '{job.RemoteFolder}'.");
        }

        private static AsyncFtpClient BuildFtpClient(ScheduledJob job, string password)
        {
            var client = new AsyncFtpClient(job.RemoteHost, job.RemoteUser, password, job.RemotePort,
                new FtpConfig
                {
                    EncryptionMode = job.FtpUseTls ? FtpEncryptionMode.Explicit : FtpEncryptionMode.None,
                    ConnectTimeout = 30_000,
                    ReadTimeout    = 60_000
                });
            // Log cert issues but accept them for backward compat with self-signed FTP servers.
            // Operators should see warnings in AppLog and install a proper CA cert when possible.
            client.ValidateCertificate += (_, e) =>
            {
                if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None)
                    Logging.AppLog.Warn($"FTP TLS cert issue for {job.RemoteHost}: {e.PolicyErrors}");
                e.Accept = true;
            };
            return client;
        }

        // ── Shared helpers ───────────────────────────────────────────────────────

        private static string[] ParsePatterns(string raw) =>
            raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
               .Select(p => p.Trim())
               .Where(p => p.Length > 0)
               .DefaultIfEmpty("*")
               .ToArray();

        private static bool MatchesAny(string fileName, string[] patterns) =>
            patterns.Any(p => GlobMatch(p, fileName));

        private static bool GlobMatch(string glob, string name)
        {
            string pattern = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(name, pattern, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir  = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext  = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, $"{stem}({i}){ext}");
                if (!File.Exists(candidate)) return candidate;
            }
            throw new IOException($"Cannot create a unique local path for '{Path.GetFileName(path)}' — 999 candidates already exist in '{dir}'.");
        }

        private static string JoinRemotePath(string parent, string child) =>
            parent.TrimEnd('/') + "/" + child.TrimStart('/');

        /// <summary>
        /// Returns true when <paramref name="fullPath"/> is inside one of the excluded directories
        /// (e.g. processed/ or failed/) relative to <paramref name="baseFolder"/>.
        /// </summary>
        private static bool IsInExcludedDir(string fullPath, string baseFolder, HashSet<string> excludeDirNames)
        {
            if (excludeDirNames.Count == 0) return false;
            string rel = fullPath.StartsWith(baseFolder, StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(baseFolder.Length).TrimStart('/')
                : fullPath.TrimStart('/');
            // Check every path segment except the last (filename) against the excluded set
            var segments = rel.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
                if (excludeDirNames.Contains(segments[i])) return true;
            return false;
        }
    }
}
