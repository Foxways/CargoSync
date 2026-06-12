using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Ai
{
    /// <summary>
    /// Persisted AI + logging configuration. The order of <see cref="Providers"/> defines the
    /// fallback chain: the router tries each enabled provider top-to-bottom until one succeeds.
    /// </summary>
    public class AiSettings
    {
        /// <summary>Master switch - when off, the app uses deterministic fuzzy mapping only.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Providers in fallback priority order (index 0 = first tried).</summary>
        public List<AiProviderProfile> Providers { get; set; } = new List<AiProviderProfile>();

        /// <summary>Persist token-usage history to disk (off = session-only totals).</summary>
        public bool SaveTokenHistory { get; set; } = true;

        /// <summary>Delete application logs older than this many days (0 = keep forever).</summary>
        public int LogRetentionDays { get; set; } = 30;

        /// <summary>Delete AI token-usage history older than this many days (0 = keep forever).</summary>
        public int TokenHistoryRetentionDays { get; set; } = 90;

        /// <summary>Only map columns below this confidence with AI (so we don't pay for easy matches).</summary>
        public bool UseAiForLowConfidenceOnly { get; set; } = true;

        /// <summary>Enabled providers in fallback order.</summary>
        public IEnumerable<AiProviderProfile> FallbackChain => Providers.Where(p => p.Enabled);

        // ---- persistence ----
        private static string DefaultPath => Path.Combine(AppPaths.DataDir, "ai-settings.json");

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public static AiSettings Load(string? path = null)
        {
            path ??= DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    var s = JsonSerializer.Deserialize<AiSettings>(File.ReadAllText(path), JsonOpts);
                    if (s != null) return s;
                }
            }
            catch { /* fall through to defaults */ }
            return new AiSettings();
        }

        public void Save(string? path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
        }
    }
}
