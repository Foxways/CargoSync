using System;
using System.IO;
using System.Linq;

namespace OrganizationImportTool.Logging
{
    /// <summary>
    /// Deletes log files older than a retention window. Call once at startup (and after a
    /// settings change). A retention of 0 or less means "keep forever".
    /// </summary>
    public static class LogRetention
    {
        /// <summary>Removes files matching <paramref name="searchPattern"/> in <paramref name="directory"/>
        /// last written before now - retentionDays. Returns the number of files deleted.</summary>
        public static int Apply(string directory, int retentionDays, string searchPattern = "*.log", bool recursive = false)
        {
            if (retentionDays <= 0) return 0;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return 0;

            var cutoff = DateTime.Now.AddDays(-retentionDays);
            int deleted = 0;
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            string[] files;
            try { files = Directory.GetFiles(directory, searchPattern, option); }
            catch { return 0; }

            foreach (var file in files)
            {
                try
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
                catch { /* skip locked/inaccessible files */ }
            }
            return deleted;
        }

        /// <summary>Apply retention to the default app logs folder plus any extra client log folders.</summary>
        public static int ApplyAll(int retentionDays, params string[] extraDirectories)
        {
            int total = Apply(AppPaths.LogsDir, retentionDays);
            foreach (var dir in extraDirectories.Where(d => !string.IsNullOrWhiteSpace(d)).Distinct())
                total += Apply(dir, retentionDays, "*.log");
            return total;
        }
    }
}
