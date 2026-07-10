using System;
using System.Linq;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class WindowsTaskSchedulerTests
    {
        private static ScheduledJob Job(ScheduleSpec spec) => new()
        {
            Id = "abc123", Name = "Nightly", Schedule = spec
        };

        private static string Args(ScheduleSpec spec, string exe = @"C:\Tools\CargoSync.exe")
            => string.Join(" ", WindowsTaskScheduler.BuildCreateArguments(Job(spec), exe, null, null)!);

        [Fact]
        public void TaskName_is_folder_scoped_by_id()
            => Assert.Equal(@"CargoSync\abc123", WindowsTaskScheduler.TaskName(Job(new ScheduleSpec())));

        [Fact]
        public void ActionCommand_quotes_exe_path_with_spaces()
        {
            var job = Job(new ScheduleSpec());
            Assert.Equal("\"C:\\Program Files\\CargoSync.exe\" --run-job abc123",
                WindowsTaskScheduler.ActionCommand(job, @"C:\Program Files\CargoSync.exe"));
            Assert.Equal("C:\\Tools\\CargoSync.exe --run-job abc123",
                WindowsTaskScheduler.ActionCommand(job, @"C:\Tools\CargoSync.exe"));
        }

        [Fact]
        public void Manual_schedule_creates_no_task()
            => Assert.Null(WindowsTaskScheduler.BuildCreateArguments(Job(new ScheduleSpec { Kind = ScheduleKind.Manual }), "x.exe", null, null));

        [Fact]
        public void EveryNMinutes_maps_to_minute_schedule()
        {
            var s = Args(new ScheduleSpec { Kind = ScheduleKind.EveryNMinutes, IntervalMinutes = 15 });
            Assert.Contains("/SC MINUTE", s);
            Assert.Contains("/MO 15", s);
            Assert.Contains("--run-job abc123", s);
        }

        [Fact]
        public void Daily_maps_to_daily_at_time()
        {
            var s = Args(new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(2, 5, 0) });
            Assert.Contains("/SC DAILY", s);
            Assert.Contains("/ST 02:05", s);
        }

        [Fact]
        public void Weekly_maps_days_and_time()
        {
            var s = Args(new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeSpan(6, 30, 0),
                DaysOfWeek = { DayOfWeek.Wednesday, DayOfWeek.Monday }
            });
            Assert.Contains("/SC WEEKLY", s);
            Assert.Contains("/D MON,WED", s); // normalised weekday order
            Assert.Contains("/ST 06:30", s);
        }

        [Fact]
        public void Hourly_uses_minute_offset()
        {
            var s = Args(new ScheduleSpec { Kind = ScheduleKind.Hourly, TimeOfDay = new TimeSpan(0, 45, 0) });
            Assert.Contains("/SC HOURLY", s);
            Assert.Contains("/ST 00:45", s);
        }

        [Fact]
        public void RunAs_user_and_password_are_included()
        {
            var args = WindowsTaskScheduler.BuildCreateArguments(
                Job(new ScheduleSpec { Kind = ScheduleKind.Daily }), "x.exe", @"DOMAIN\svc", "pw");
            Assert.NotNull(args);
            int ru = args!.IndexOf("/RU");
            Assert.True(ru >= 0 && args[ru + 1] == @"DOMAIN\svc");
            int rp = args.IndexOf("/RP");
            Assert.True(rp >= 0 && args[rp + 1] == "pw");
        }

        [Fact]
        public void Delete_and_query_target_the_task_name()
        {
            var job = Job(new ScheduleSpec());
            Assert.Equal(new[] { "/Delete", "/F", "/TN", @"CargoSync\abc123" }, WindowsTaskScheduler.BuildDeleteArguments(job));
            Assert.Equal(new[] { "/Query", "/TN", @"CargoSync\abc123" }, WindowsTaskScheduler.BuildQueryArguments(job));
        }
    }
}
