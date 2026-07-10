using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>The cadence a scheduled job runs on.</summary>
    public enum ScheduleKind
    {
        /// <summary>No automatic trigger - the job only runs when launched by hand.</summary>
        Manual = 0,
        /// <summary>Every N minutes, around the clock.</summary>
        EveryNMinutes = 1,
        /// <summary>Once an hour, at a fixed minute past the hour.</summary>
        Hourly = 2,
        /// <summary>Once a day, at a fixed time.</summary>
        Daily = 3,
        /// <summary>On chosen weekdays, at a fixed time.</summary>
        Weekly = 4
    }

    /// <summary>
    /// A client-configurable run cadence. Deliberately mirrors the trigger shapes Windows Task
    /// Scheduler understands (interval / daily / weekly at a time), so a spec maps 1:1 onto a real
    /// scheduled task. <see cref="NextRunAfter"/> is a pure function used for "next run" display and
    /// is fully unit-tested; the OS owns the actual firing once a task is created.
    /// </summary>
    public sealed class ScheduleSpec
    {
        public ScheduleKind Kind { get; set; } = ScheduleKind.Manual;

        /// <summary>Interval for <see cref="ScheduleKind.EveryNMinutes"/> (clamped to >= 1).</summary>
        public int IntervalMinutes { get; set; } = 15;

        /// <summary>Time of day for Hourly (minute only), Daily and Weekly. Stored as HH:mm in JSON.</summary>
        public TimeSpan TimeOfDay { get; set; } = new TimeSpan(2, 0, 0);

        /// <summary>Weekdays the job runs on for <see cref="ScheduleKind.Weekly"/>.</summary>
        public List<DayOfWeek> DaysOfWeek { get; set; } = new();

        /// <summary>The next fire time strictly after <paramref name="after"/>, or null for Manual.</summary>
        public DateTime? NextRunAfter(DateTime after)
        {
            switch (Kind)
            {
                case ScheduleKind.Manual:
                    return null;

                case ScheduleKind.EveryNMinutes:
                    return after.AddMinutes(Math.Max(1, IntervalMinutes));

                case ScheduleKind.Hourly:
                {
                    // Next instant whose minute == TimeOfDay.Minutes, strictly after 'after'.
                    int minute = ClampMinute(TimeOfDay.Minutes);
                    var candidate = new DateTime(after.Year, after.Month, after.Day, after.Hour, minute, 0, after.Kind);
                    if (candidate <= after) candidate = candidate.AddHours(1);
                    return candidate;
                }

                case ScheduleKind.Daily:
                {
                    var candidate = OnDate(after.Date);
                    if (candidate <= after) candidate = OnDate(after.Date.AddDays(1));
                    return candidate;
                }

                case ScheduleKind.Weekly:
                {
                    var days = NormalisedDays();
                    if (days.Count == 0) return null; // weekly with no day selected never fires
                    // Scan today + the next 7 days for the first selected weekday at TimeOfDay.
                    for (int offset = 0; offset <= 7; offset++)
                    {
                        var date = after.Date.AddDays(offset);
                        if (!days.Contains(date.DayOfWeek)) continue;
                        var candidate = OnDate(date);
                        if (candidate > after) return candidate;
                    }
                    return null; // unreachable for a valid day set, but keeps the compiler happy
                }

                default:
                    return null;
            }
        }

        /// <summary>True when the schedule is configured well enough to create a task from.</summary>
        public bool IsValid(out string error)
        {
            switch (Kind)
            {
                case ScheduleKind.EveryNMinutes when IntervalMinutes < 1:
                    error = "Interval must be at least 1 minute.";
                    return false;
                case ScheduleKind.Weekly when NormalisedDays().Count == 0:
                    error = "Pick at least one weekday for a weekly schedule.";
                    return false;
                default:
                    error = string.Empty;
                    return true;
            }
        }

        /// <summary>Human-readable summary, e.g. "Daily at 02:00" or "Weekly on Mon, Wed at 06:30".</summary>
        public string Describe() => Kind switch
        {
            ScheduleKind.Manual => "Manual (no automatic schedule)",
            ScheduleKind.EveryNMinutes => $"Every {Math.Max(1, IntervalMinutes)} minute(s)",
            ScheduleKind.Hourly => $"Hourly at {ClampMinute(TimeOfDay.Minutes):00} past the hour",
            ScheduleKind.Daily => $"Daily at {Hhmm(TimeOfDay)}",
            ScheduleKind.Weekly => NormalisedDays().Count == 0
                ? "Weekly (no weekday selected)"
                : $"Weekly on {string.Join(", ", NormalisedDays().Select(ShortDay))} at {Hhmm(TimeOfDay)}",
            _ => Kind.ToString()
        };

        private DateTime OnDate(DateTime date) =>
            new DateTime(date.Year, date.Month, date.Day,
                ClampHour(TimeOfDay.Hours), ClampMinute(TimeOfDay.Minutes), 0, date.Kind);

        /// <summary>Distinct, weekday-ordered selection (so Describe/scan are deterministic).</summary>
        private List<DayOfWeek> NormalisedDays() =>
            DaysOfWeek.Distinct().OrderBy(d => (int)d).ToList();

        private static int ClampHour(int h) => Math.Min(23, Math.Max(0, h));
        private static int ClampMinute(int m) => Math.Min(59, Math.Max(0, m));
        private static string Hhmm(TimeSpan t) => $"{ClampHour(t.Hours):00}:{ClampMinute(t.Minutes):00}";
        private static string ShortDay(DayOfWeek d) => d switch
        {
            DayOfWeek.Monday => "Mon", DayOfWeek.Tuesday => "Tue", DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday => "Thu", DayOfWeek.Friday => "Fri", DayOfWeek.Saturday => "Sat",
            _ => "Sun"
        };
    }
}
