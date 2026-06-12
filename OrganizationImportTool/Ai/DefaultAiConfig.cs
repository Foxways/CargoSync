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
    ///   * The default key is NOT stored in source (this repo is public). It is read at runtime
    ///     from the CARGOSYNC_DEFAULT_OPENROUTER_KEY environment variable or from an
    ///     "openrouter-default.key" file next to the executable - the build/installer machine
    ///     drops that file in locally (it is gitignored). No file = no seeding; users simply
    ///     configure AI themselves in AI Settings. Always use a free-tier / spend-capped key.
    /// </summary>
    internal static class DefaultAiConfig
    {
        private const string KeyFileName = "openrouter-default.key";
        private const string KeyEnvVar = "CARGOSYNC_DEFAULT_OPENROUTER_KEY";

        private const string DefaultModel = "openai/gpt-oss-120b:free";

        private static string DecodeKey()
        {
            try
            {
                string env = Environment.GetEnvironmentVariable(KeyEnvVar) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(env)) return env.Trim();

                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, KeyFileName);
                if (File.Exists(path)) return File.ReadAllText(path).Trim();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Default AI key lookup failed", ex); }
            return string.Empty;
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
