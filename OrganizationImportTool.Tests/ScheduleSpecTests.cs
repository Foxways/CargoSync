using System;
using System.Collections.Generic;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class ScheduleSpecTests
    {
        private static DateTime At(int y, int mo, int d, int h, int mi) => new(y, mo, d, h, mi, 0, DateTimeKind.Local);

        [Fact]
        public void Manual_never_fires()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.Manual };
            Assert.Null(s.NextRunAfter(At(2026, 6, 18, 9, 0)));
        }

        [Fact]
        public void EveryNMinutes_adds_interval()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.EveryNMinutes, IntervalMinutes = 15 };
            Assert.Equal(At(2026, 6, 18, 9, 15), s.NextRunAfter(At(2026, 6, 18, 9, 0)));
        }

        [Fact]
        public void EveryNMinutes_clamps_below_one()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.EveryNMinutes, IntervalMinutes = 0 };
            Assert.Equal(At(2026, 6, 18, 9, 1), s.NextRunAfter(At(2026, 6, 18, 9, 0)));
        }

        [Fact]
        public void Hourly_picks_next_minute_past_the_hour()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.Hourly, TimeOfDay = new TimeSpan(0, 30, 0) };
            // 9:05 -> 9:30 (same hour, still ahead)
            Assert.Equal(At(2026, 6, 18, 9, 30), s.NextRunAfter(At(2026, 6, 18, 9, 5)));
            // 9:30 -> 10:30 (strictly after, so roll to next hour)
            Assert.Equal(At(2026, 6, 18, 10, 30), s.NextRunAfter(At(2026, 6, 18, 9, 30)));
            // 9:45 -> 10:30 (already past :30 this hour)
            Assert.Equal(At(2026, 6, 18, 10, 30), s.NextRunAfter(At(2026, 6, 18, 9, 45)));
        }

        [Fact]
        public void Daily_today_if_time_still_ahead_else_tomorrow()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(2, 0, 0) };
            // before 02:00 -> today 02:00
            Assert.Equal(At(2026, 6, 18, 2, 0), s.NextRunAfter(At(2026, 6, 18, 1, 0)));
            // after 02:00 -> tomorrow 02:00
            Assert.Equal(At(2026, 6, 19, 2, 0), s.NextRunAfter(At(2026, 6, 18, 9, 0)));
            // exactly at 02:00 -> next day (strictly after)
            Assert.Equal(At(2026, 6, 19, 2, 0), s.NextRunAfter(At(2026, 6, 18, 2, 0)));
        }

        [Fact]
        public void Weekly_finds_next_selected_weekday()
        {
            // 2026-06-18 is a Thursday. Run Mondays & Wednesdays at 06:30.
            var s = new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeSpan(6, 30, 0),
                DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Wednesday }
            };
            // From Thursday -> next Monday (2026-06-22) 06:30
            Assert.Equal(At(2026, 6, 22, 6, 30), s.NextRunAfter(At(2026, 6, 18, 9, 0)));
        }

        [Fact]
        public void Weekly_same_day_before_time_fires_today()
        {
            // 2026-06-22 is a Monday.
            var s = new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeSpan(6, 30, 0),
                DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday }
            };
            Assert.Equal(At(2026, 6, 22, 6, 30), s.NextRunAfter(At(2026, 6, 22, 5, 0)));
            // after the time on the matching day -> following week
            Assert.Equal(At(2026, 6, 29, 6, 30), s.NextRunAfter(At(2026, 6, 22, 7, 0)));
        }

        [Fact]
        public void Weekly_with_no_day_never_fires_and_is_invalid()
        {
            var s = new ScheduleSpec { Kind = ScheduleKind.Weekly, DaysOfWeek = new() };
            Assert.Null(s.NextRunAfter(At(2026, 6, 18, 9, 0)));
            Assert.False(s.IsValid(out _));
        }

        [Fact]
        public void Describe_is_human_readable()
        {
            Assert.Equal("Every 15 minute(s)", new ScheduleSpec { Kind = ScheduleKind.EveryNMinutes, IntervalMinutes = 15 }.Describe());
            Assert.Equal("Daily at 02:00", new ScheduleSpec { Kind = ScheduleKind.Daily, TimeOfDay = new TimeSpan(2, 0, 0) }.Describe());
            var weekly = new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeSpan(6, 30, 0),
                DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Wednesday, DayOfWeek.Monday }
            };
            Assert.Equal("Weekly on Mon, Wed at 06:30", weekly.Describe());
        }
    }
}
