using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OrganizationImportTool.Logging
{
    /// <summary>
    /// Global last-line-of-defence exception handling. Installed first thing in Main so an
    /// unexpected error anywhere (UI thread, background task, app domain) is written to the
    /// crash log and shown to the user as a friendly dialog instead of a silent process death.
    /// </summary>
    public static class CrashHandler
    {
        private static int _showing; // reentrancy guard: never stack crash dialogs

        /// <summary>True when running a CLI/self-test mode - report to the console, not a dialog.</summary>
        public static bool Headless { get; set; }

        public static void Install()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Handle(e.Exception, "UI thread");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Handle(e.ExceptionObject as Exception, "AppDomain", e.IsTerminating);
            TaskScheduler.UnobservedTaskException += (_, e) => { Handle(e.Exception, "Background task"); e.SetObserved(); };
        }

        public static void Handle(Exception? ex, string source, bool fatal = false)
        {
            ex ??= new Exception("Unknown error (no exception object).");
            AppLog.Crash(source, ex);

            if (Headless)
            {
                Console.WriteLine($"UNHANDLED ({source}): {ex}");
                return;
            }

            if (Interlocked.Exchange(ref _showing, 1) == 1) return;
            try
            {
                string message =
                    "CargoSync hit an unexpected error." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Details were saved to:" + Environment.NewLine + AppLog.CrashLogPath +
                    (fatal ? Environment.NewLine + Environment.NewLine + "The application has to close." : string.Empty);
                MessageBox.Show(message, "CargoSync - Unexpected Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { /* even the dialog must not throw */ }
            finally { Interlocked.Exchange(ref _showing, 0); }
        }
    }
}
