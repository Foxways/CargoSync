using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Persists <see cref="ScheduledJob"/> definitions as one JSON file each under
    /// %AppData%\OrganizationImportTool\Jobs (mirroring <c>TemplateStore</c>). A corrupt file is
    /// skipped, never fatal, so one bad job can't hide the others.
    /// </summary>
    public sealed class JobStore
    {
        private readonly string _dir;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public JobStore(string? dir = null)
        {
            _dir = dir ?? Path.Combine(AppPaths.DataDir, "Jobs");
            Directory.CreateDirectory(_dir);
        }

        /// <summary>All defined jobs, ordered by name.</summary>
        public List<ScheduledJob> LoadAll()
        {
            var list = new List<ScheduledJob>();
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                try
                {
                    var j = JsonSerializer.Deserialize<ScheduledJob>(File.ReadAllText(file));
                    if (j != null) list.Add(j);
                }
                catch (Exception ex) { Logging.AppLog.Warn($"Skipping corrupt job file '{Path.GetFileName(file)}'", ex); }
            }
            return list.OrderBy(j => j.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The jobs defined for one client.</summary>
        public List<ScheduledJob> ForClient(string clientId) =>
            LoadAll().Where(j => string.Equals(j.ClientId, clientId, StringComparison.OrdinalIgnoreCase)).ToList();

        public ScheduledJob? Get(string id)
        {
            string path = PathFor(id);
            if (!File.Exists(path)) return null;
            try { return JsonSerializer.Deserialize<ScheduledJob>(File.ReadAllText(path)); }
            catch (Exception ex) { Logging.AppLog.Warn($"Job '{id}' could not be read", ex); return null; }
        }

        /// <summary>Create or overwrite a job. Stamps CreatedUtc on first save.</summary>
        public void Save(ScheduledJob job, string nowUtcIso)
        {
            if (string.IsNullOrWhiteSpace(job.CreatedUtc)) job.CreatedUtc = nowUtcIso;
            // Atomic write: write to a .tmp file then rename so a concurrent reader never
            // sees a partial or zero-byte file (File.WriteAllText is not atomic on Windows).
            string path = PathFor(job.Id);
            string tmp  = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(job, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }

        public void Delete(string id)
        {
            string path = PathFor(id);
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Logging.AppLog.Warn($"Job delete failed for '{id}'", ex); }
        }

        private string PathFor(string id)
        {
            // ids are GUID "N" strings, but sanitise defensively so a bad id can't escape the folder.
            var safe = new string((id ?? string.Empty)
                .Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
            if (string.IsNullOrEmpty(safe)) safe = "job";
            return Path.Combine(_dir, safe + ".json");
        }
    }
}
