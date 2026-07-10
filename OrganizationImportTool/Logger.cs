using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using System;
using System.IO;

namespace OrganizationImportTool
{
    public static class Logger
    {
        private static ILog SuccessLog { get; set; }
        private static ILog FailedLog { get; set; }

        // Track only the appenders THIS class owns so we can swap them per-run without
        // touching the root hierarchy or any appenders registered by AppLog.
        private static RollingFileAppender? _prevSuccess;
        private static RollingFileAppender? _prevFailed;

        public static void Setup(string logFolderPath)
        {
            // Ensure the log folder exists
            if (!Directory.Exists(logFolderPath))
                Directory.CreateDirectory(logFolderPath);

            var hierarchy = (Hierarchy)LogManager.GetRepository();
            // DO NOT call hierarchy.ResetConfiguration() — it destroys every appender in the
            // global repository, including AppLog's own appenders, silencing all diagnostics
            // for the rest of the session. Instead, remove only our previously registered
            // appenders from their named loggers before installing the new ones.

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

            // Define a standard pattern layout
            var patternLayout = new PatternLayout
            {
                ConversionPattern = "%date{dd MMM yyyy HH:mm:ss,fff} [%thread] %level %logger - %message%newline"
            };
            patternLayout.ActivateOptions();

            // === SUCCESS LOGGER ===
            var successAppender = new RollingFileAppender
            {
                Name = "SuccessAppender",
                File = Path.Combine(logFolderPath, $"SUCCESS_{timestamp}.txt"),
                AppendToFile = true,
                Layout = patternLayout,
                MaxSizeRollBackups = 2,
                MaximumFileSize = "1000MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true,
                LockingModel = new FileAppender.MinimalLock()
            };
            successAppender.ActivateOptions();

            var successLogger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger("SuccessLogger");
            successLogger.Additivity = false; // Prevent log bubbling to root
            successLogger.Level = Level.Info;
            if (_prevSuccess != null) { successLogger.RemoveAppender(_prevSuccess); _prevSuccess.Close(); }
            successLogger.AddAppender(successAppender);
            _prevSuccess = successAppender;

            // === FAILED LOGGER ===
            var failedAppender = new RollingFileAppender
            {
                Name = "FailedAppender",
                File = Path.Combine(logFolderPath, $"FAILED_{timestamp}.txt"),
                AppendToFile = true,
                Layout = patternLayout,
                MaxSizeRollBackups = 2,
                MaximumFileSize = "1000MB",
                RollingStyle = RollingFileAppender.RollingMode.Size,
                StaticLogFileName = true,
                LockingModel = new FileAppender.MinimalLock()
            };
            failedAppender.ActivateOptions();

            var failedLogger = (log4net.Repository.Hierarchy.Logger)hierarchy.GetLogger("FailedLogger");
            failedLogger.Additivity = false;
            failedLogger.Level = Level.Info;
            if (_prevFailed != null) { failedLogger.RemoveAppender(_prevFailed); _prevFailed.Close(); }
            failedLogger.AddAppender(failedAppender);
            _prevFailed = failedAppender;

            hierarchy.Configured = true;

            // Assign to ILog instances
            SuccessLog = LogManager.GetLogger("SuccessLogger");
            FailedLog = LogManager.GetLogger("FailedLogger");
        }

        public static void LogSuccess(string message)
        {
            SuccessLog?.Info(message);
        }

        public static void LogFailure(string message)
        {
            FailedLog?.Error(message);
        }

        public static void LogFailure(string message, Exception ex)
        {
            FailedLog?.Error(message, ex);
        }
    }
}
