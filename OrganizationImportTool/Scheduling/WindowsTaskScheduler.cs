using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Registers a scheduled job as a real Windows scheduled task (via schtasks.exe) that runs
    /// <c>CargoSync.exe --run-job &lt;id&gt;</c> on the job's cadence. The argument construction is a pure,
    /// unit-tested function; the executor is a thin schtasks wrapper. Using schtasks keeps this
    /// dependency-free and maps a <see cref="ScheduleSpec"/> straight onto native triggers.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WindowsTaskScheduler
    {
        /// <summary>Task Scheduler folder all CargoSync jobs live under.</summary>
        public const string Folder = "CargoSync";

        /// <summary>Stable task name (keyed by job id, so renaming the job keeps the same task).</summary>
        public static string TaskName(ScheduledJob job) => $"{Folder}\\{job.Id}";

        /// <summary>The command schtasks will run for this job.</summary>
        public static string ActionCommand(ScheduledJob job, string exePath) =>
            exePath.Contains(' ') ? $"\"{exePath}\" --run-job {job.Id}" : $"{exePath} --run-job {job.Id}";

        /// <summary>
        /// Build the schtasks /Create argument list for a job, or null for a Manual schedule (no task).
        /// Password is included only when a run-as user is supplied (schtasks needs it on the command
        /// line to store credentials for "run whether logged on or not").
        /// </summary>
        public static List<string>? BuildCreateArguments(ScheduledJob job, string exePath, string? runAsUser, string? password)
        {
            var spec = job.Schedule;
            if (spec.Kind == ScheduleKind.Manual) return null;

            var a = new List<string>
            {
                "/Create", "/F",
                "/TN", TaskName(job),
                "/TR", ActionCommand(job, exePath)
            };

            switch (spec.Kind)
            {
                case ScheduleKind.EveryNMinutes:
                    a.AddRange(new[] { "/SC", "MINUTE", "/MO", Math.Max(1, spec.IntervalMinutes).ToString() });
                    break;
                case ScheduleKind.Hourly:
                    a.AddRange(new[] { "/SC", "HOURLY", "/MO", "1", "/ST", $"00:{Clamp(spec.TimeOfDay.Minutes):00}" });
                    break;
                case ScheduleKind.Daily:
                    a.AddRange(new[] { "/SC", "DAILY", "/ST", Hhmm(spec) });
                    break;
                case ScheduleKind.Weekly:
                    a.AddRange(new[] { "/SC", "WEEKLY", "/D", DaysList(spec), "/ST", Hhmm(spec) });
                    break;
            }

            if (!string.IsNullOrWhiteSpace(runAsUser))
            {
                a.Add("/RU"); a.Add(runAsUser!);
                if (!string.IsNullOrEmpty(password)) { a.Add("/RP"); a.Add(password!); }
            }
            return a;
        }

        public static List<string> BuildDeleteArguments(ScheduledJob job) =>
            new() { "/Delete", "/F", "/TN", TaskName(job) };

        public static List<string> BuildQueryArguments(ScheduledJob job) =>
            new() { "/Query", "/TN", TaskName(job) };

        /// <summary>Create/replace the Windows task for this job (Manual schedule removes any task).</summary>
        public static (bool ok, string output) CreateOrUpdate(ScheduledJob job, string? runAsUser = null, string? password = null, string? exePath = null)
        {
            exePath ??= Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "CargoSync.exe";
            var args = BuildCreateArguments(job, exePath, runAsUser, password);
            if (args == null) return Delete(job); // Manual: ensure no lingering task
            return RunSchtasks(args, redactAfter: "/RP");
        }

        public static (bool ok, string output) Delete(ScheduledJob job) => RunSchtasks(BuildDeleteArguments(job));

        public static bool Exists(ScheduledJob job) => RunSchtasks(BuildQueryArguments(job)).ok;

        private static (bool ok, string output) RunSchtasks(List<string> args, string? redactAfter = null)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var arg in args) psi.ArgumentList.Add(arg);

                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit();

                if (p.ExitCode != 0)
                    Logging.AppLog.Warn($"schtasks {Redact(args, redactAfter)} exited {p.ExitCode}: {output.Trim()}");
                return (p.ExitCode == 0, output.Trim());
            }
            catch (Exception ex)
            {
                Logging.AppLog.Error("schtasks invocation failed", ex);
                return (false, ex.Message);
            }
        }

        /// <summary>Render the args for logging, hiding the value following a sensitive flag (e.g. /RP).</summary>
        private static string Redact(List<string> args, string? sensitiveFlag)
        {
            if (sensitiveFlag == null) return string.Join(' ', args);
            var copy = new List<string>(args);
            int i = copy.FindIndex(x => string.Equals(x, sensitiveFlag, StringComparison.OrdinalIgnoreCase));
            if (i >= 0 && i + 1 < copy.Count) copy[i + 1] = "***";
            return string.Join(' ', copy);
        }

        private static string Hhmm(ScheduleSpec s) => $"{Clamp(s.TimeOfDay.Hours, 23):00}:{Clamp(s.TimeOfDay.Minutes):00}";
        private static int Clamp(int v, int max = 59) => Math.Min(max, Math.Max(0, v));

        private static string DaysList(ScheduleSpec s) =>
            string.Join(",", s.DaysOfWeek.Distinct().OrderBy(d => (int)d).Select(Abbrev));

        private static string Abbrev(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "MON", DayOfWeek.Tuesday => "TUE", DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU", DayOfWeek.Friday => "FRI", DayOfWeek.Saturday => "SAT",
            _ => "SUN"
        };
    }
}
