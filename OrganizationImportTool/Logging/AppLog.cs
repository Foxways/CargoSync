using System;
using System.IO;
using System.Reflection;

namespace OrganizationImportTool.Logging
{
    /// <summary>
    /// Minimal application-wide logger that never throws and needs no setup, so it is safe to
    /// call from crash handlers, store catch blocks and code that runs before the per-client
    /// log4net logger is configured. Writes daily files to %AppData%\OrganizationImportTool\Logs.
    /// </summary>
    public static class AppLog
    {
        private static readonly object Gate = new();

        /// <summary>Today's application log file (warnings/errors).</summary>
        public static string LogPath => Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");

        /// <summary>Today's crash log file (unhandled exceptions, full detail).</summary>
        public static string CrashLogPath => Path.Combine(AppPaths.LogsDir, $"crash-{DateTime.Now:yyyyMMdd}.log");

        public static void Warn(string context, Exception? ex = null) => Write("WARN", context, ex, LogPath);
        public static void Error(string context, Exception? ex = null) => Write("ERROR", context, ex, LogPath);

        /// <summary>Record an unhandled exception with full detail and environment info.</summary>
        public static void Crash(string source, Exception ex)
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            Write("CRASH", $"{source} (CargoSync {version}, user {Environment.UserName})", ex, CrashLogPath);
        }

        private static void Write(string level, string context, Exception? ex, string path)
        {
            try
            {
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {context}";
                if (ex != null) line += Environment.NewLine + "    " + ex.ToString().Replace(Environment.NewLine, Environment.NewLine + "    ");
                lock (Gate) File.AppendAllText(path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down (disk full, locked file, ...).
            }
        }
    }
}
