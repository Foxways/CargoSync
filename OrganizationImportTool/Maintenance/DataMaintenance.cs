using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Maintenance
{
    /// <summary>One clearable category of accumulated data shown on the Data &amp; Maintenance screen.</summary>
    public sealed class CleanupCategory
    {
        public string Key { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        /// <summary>True = hand-made / hard-to-recover data that warrants a stronger confirmation.</summary>
        public bool Destructive { get; init; }

        /// <summary>Current footprint: an item count and an approximate size in bytes (0 when unknown).</summary>
        public Func<(int count, long bytes)> Measure { get; init; } = () => (0, 0);

        /// <summary>Remove everything in this category. Must be best-effort and never throw.</summary>
        public Action Clear { get; init; } = () => { };
    }

    /// <summary>
    /// Inventory + clear operations for everything the app accumulates locally (logs, learned mapping
    /// memory, AI usage / activity history, dedup &amp; sync ledgers, saved templates). Lets the user
    /// reclaim space and wipe history WITHOUT ever touching user accounts (the Users table), AI keys,
    /// or preferences. Every operation is best-effort and swallows IO/DB errors so the screen can't
    /// leave the app in a broken state.
    /// </summary>
    public static class DataMaintenance
    {
        private static string AiUsagePath => Path.Combine(AppPaths.DataDir, "ai-usage-history.jsonl");
        private static string TemplatesDir => Path.Combine(AppPaths.DataDir, "Templates");

        public static List<CleanupCategory> Categories() => new()
        {
            new CleanupCategory
            {
                Key = "logs",
                Title = "Application & import logs",
                Description = "Diagnostic and import log files. Safe to clear at any time.",
                Measure = () => FolderStats(AppPaths.LogsDir),
                Clear   = () => ClearFolder(AppPaths.LogsDir)
            },
            new CleanupCategory
            {
                Key = "learned",
                Title = "Learned mapping memory",
                Description = "Per-client auto-learned column mappings recalled to pre-fill future uploads.",
                Measure = () => TemplateStats(auto: true),
                Clear   = () => ClearTemplates(auto: true)
            },
            new CleanupCategory
            {
                Key = "aiusage",
                Title = "AI usage & import activity history",
                Description = "AI token-usage history plus the per-import audit trail (who imported what, and the outcome).",
                Measure = () =>
                {
                    var f = FileStats(AiUsagePath);
                    return (f.count + TableCount("ImportActivity"), f.bytes);
                },
                Clear = () =>
                {
                    TryDelete(AiUsagePath);
                    TruncateTable("ImportActivity");
                }
            },
            new CleanupCategory
            {
                Key = "dedup",
                Title = "Dedup memory & sync feedback",
                Description = "Recurring-rejection lessons and the CargoWise sync ledger used for already-synced detection.",
                Measure = () => (TableCount("CwRejections") + TableCount("CwSync") + TableCount("ImportRuns"), 0),
                Clear = () =>
                {
                    TruncateTable("CwRejections");
                    TruncateTable("CwSync");
                    TruncateTable("ImportRuns");
                }
            },
            new CleanupCategory
            {
                Key = "templates",
                Title = "Saved mapping templates",
                Description = "Your hand-made reusable templates. These are NOT regenerated — clear only if you no longer need them.",
                Destructive = true,
                Measure = () => TemplateStats(auto: false),
                Clear   = () => ClearTemplates(auto: false)
            },
            new CleanupCategory
            {
                Key = "dbreset",
                Title = "Reset local history database",
                Description = "Wipe ALL operational history in data.db (sync ledger, rejections, activity, run journal). Keeps your user accounts and settings.",
                Destructive = true,
                Measure = () => (TableCount("CwSync") + TableCount("CwRejections") + TableCount("ImportActivity") + TableCount("ImportRuns"), 0),
                Clear = () =>
                {
                    TruncateTable("CwSync");
                    TruncateTable("CwRejections");
                    TruncateTable("ImportActivity");
                    TruncateTable("ImportRuns");
                    Vacuum();
                }
            }
        };

        /// <summary>Human-readable byte size (e.g. "2.4 MB").</summary>
        public static string Human(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.#} {u[i]}";
        }

        // ───────────── measurement helpers ─────────────

        private static (int count, long bytes) FolderStats(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return (0, 0);
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                long bytes = 0;
                foreach (var f in files) { try { bytes += new FileInfo(f).Length; } catch { } }
                return (files.Length, bytes);
            }
            catch { return (0, 0); }
        }

        private static (int count, long bytes) FileStats(string path)
        {
            try
            {
                if (!File.Exists(path)) return (0, 0);
                long len = new FileInfo(path).Length;
                int lines = 0;
                foreach (var _ in File.ReadLines(path)) lines++;
                return (lines, len);
            }
            catch { return (0, 0); }
        }

        private static (int count, long bytes) TemplateStats(bool auto)
        {
            try
            {
                if (!Directory.Exists(TemplatesDir)) return (0, 0);
                var subset = new TemplateStore().LoadAll().Where(t => t.IsAuto == auto).ToList();
                long bytes = subset.Sum(t =>
                {
                    try { return new FileInfo(Path.Combine(TemplatesDir, t.Id + ".json")).Length; }
                    catch { return 0L; }
                });
                return (subset.Count, bytes);
            }
            catch { return (0, 0); }
        }

        // ───────────── clear helpers ─────────────

        private static void ClearFolder(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    TryDelete(f);
            }
            catch (Exception ex) { Logging.AppLog.Warn($"Clearing folder '{dir}' failed", ex); }
        }

        private static void ClearTemplates(bool auto)
        {
            try
            {
                var store = new TemplateStore();
                foreach (var t in store.LoadAll().Where(t => t.IsAuto == auto).ToList())
                    store.Delete(t.Id);
            }
            catch (Exception ex) { Logging.AppLog.Warn("Clearing templates failed", ex); }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Logging.AppLog.Warn($"Could not delete '{path}' (still in use?)", ex); }
        }

        // ───────────── local-DB helpers (table names are a fixed whitelist — never user input) ─────────────

        private static int TableCount(string table)
        {
            try
            {
                using var conn = DB.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }   // table not created yet → nothing to count
        }

        private static void TruncateTable(string table)
        {
            try
            {
                using var conn = DB.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $"DELETE FROM {table}";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn($"Clearing table '{table}' failed", ex); }
        }

        private static void Vacuum()
        {
            try
            {
                using var conn = DB.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "VACUUM";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Database VACUUM failed", ex); }
        }
    }
}
