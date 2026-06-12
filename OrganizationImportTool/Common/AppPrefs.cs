using System;
using System.IO;
using System.Text.Json;

namespace OrganizationImportTool
{
    /// <summary>
    /// Small per-user UX preferences (first-run flags etc.) persisted as JSON in %AppData%.
    /// Distinct from AiSettings (AI config) and the SQLite data store (clients/users).
    /// </summary>
    public class AppPrefs
    {
        /// <summary>The first-run welcome wizard finished (or was explicitly skipped).</summary>
        public bool WelcomeWizardCompleted { get; set; }

        /// <summary>The user has seen the "AI sends data to a provider" notice.</summary>
        public bool AiConsentShown { get; set; }

        private static string FilePath => Path.Combine(AppPaths.DataDir, "app-prefs.json");

        public static AppPrefs Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var p = JsonSerializer.Deserialize<AppPrefs>(File.ReadAllText(FilePath));
                    if (p != null) return p;
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("App prefs unreadable - using defaults", ex); }
            return new AppPrefs();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Logging.AppLog.Warn("App prefs save failed", ex); }
        }
    }
}
