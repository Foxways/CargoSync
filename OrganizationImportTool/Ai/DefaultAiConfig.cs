using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OrganizationImportTool.Ai
{
    /// <summary>
    /// Seeds a working OpenRouter configuration the very first time the app runs on a machine,
    /// so AI features (smart mapping, cleaning, copilot, enrichment) work out-of-the-box for every
    /// user without any manual setup.
    ///
    /// Behaviour:
    ///   * Only runs when no ai-settings.json exists yet (first launch). It NEVER overwrites a
    ///     user's own configuration - once a user opens AI Settings and saves their own key/model,
    ///     that file exists and this seeder stays out of the way forever.
    ///   * The default key is stored base64-encoded (not as a raw plaintext literal). This is light
    ///     obfuscation only - it is NOT real security. Anyone with the installed files can recover
    ///     it, so this should always be a free-tier / spend-capped OpenRouter key.
    /// </summary>
    internal static class DefaultAiConfig
    {
        // Base64 of the default OpenRouter API key (sk-or-v1-...). Decoded at runtime.
        // To change the bundled default, replace this string with the base64 of a new key.
        private const string DefaultKeyB64 =
            "KEY-REMOVED-FROM-HISTORY";

        private const string DefaultModel = "openai/gpt-oss-120b:free";

        private static string DecodeKey()
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(DefaultKeyB64)); }
            catch { return string.Empty; }
        }

        /// <summary>
        /// If the user has no AI settings file yet, create one with AI enabled and OpenRouter
        /// pre-configured (key + model + headers). Safe to call on every startup - it is a no-op
        /// once the settings file exists. Never throws.
        /// </summary>
        public static void SeedDefaultsIfMissing()
        {
            try
            {
                string path = Path.Combine(AppPaths.DataDir, "ai-settings.json");
                if (File.Exists(path)) return; // user already has settings - leave them untouched

                string key = DecodeKey();
                if (string.IsNullOrEmpty(key)) return;

                var openRouter = AiProviderProfile.OpenRouterTemplate();
                openRouter.Model = DefaultModel;
                openRouter.Enabled = true;
                openRouter.ApiKey = key; // setter DPAPI-encrypts it for this user on save

                var settings = new AiSettings
                {
                    Enabled = true,
                    UseAiForLowConfidenceOnly = true,
                    Providers = { openRouter }
                };
                settings.Save(path);
            }
            catch (Exception ex) { Logging.AppLog.Warn("Default AI config seeding failed - AI can be set up manually", ex); }
        }
    }
}
