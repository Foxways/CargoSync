using System;
using System.IO;

namespace OrganizationImportTool
{
    /// <summary>Centralized, per-user application data locations.</summary>
    public static class AppPaths
    {
        /// <summary>%AppData%\OrganizationImportTool - settings, AI config, usage history.</summary>
        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OrganizationImportTool");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>Default folder for application logs (overridable per client).</summary>
        public static string LogsDir
        {
            get
            {
                string dir = Path.Combine(DataDir, "Logs");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// The application database lives in the per-user data folder
        /// (%AppData%\OrganizationImportTool\data.db) - a safe, writable location that does NOT
        /// depend on where the app was installed (Program Files is read-only for normal users) and
        /// that survives uninstalls / re-installs. On first run we copy the seed database shipped
        /// next to the executable into this location if one isn't already there.
        /// </summary>
        public static string DbPath
        {
            get
            {
                string target = Path.Combine(DataDir, "data.db");
                if (!File.Exists(target))
                {
                    try
                    {
                        string seed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data.db");
                        if (File.Exists(seed)) File.Copy(seed, target, overwrite: false);
                    }
                    catch { /* if seeding fails the stores create an empty schema on demand */ }
                }
                return target;
            }
        }
    }
}
