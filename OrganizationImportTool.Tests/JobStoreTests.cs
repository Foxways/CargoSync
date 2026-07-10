using System;
using System.IO;
using System.Linq;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class JobStoreTests : IDisposable
    {
        private readonly string _dir;

        public JobStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "oit_jobstore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private static ScheduledJob Sample(string client = "C1") => new()
        {
            Name = "Nightly import",
            ClientId = client,
            ClientName = "Acme",
            SourceFolder = @"\\share\acme\inbound",
            FilePattern = "*.xlsx",
            TemplateId = "tmpl-123",
            Schedule = new ScheduleSpec
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeSpan(6, 30, 0),
                DaysOfWeek = { DayOfWeek.Monday, DayOfWeek.Wednesday }
            },
            NotifyOn = NotifyTrigger.Always,
            NotifyClientEmails = "ops@acme.example"
        };

        [Fact]
        public void Save_then_Get_round_trips_all_fields()
        {
            var store = new JobStore(_dir);
            var job = Sample();
            store.Save(job, "2026-06-18T00:00:00Z");

            var back = store.Get(job.Id);
            Assert.NotNull(back);
            Assert.Equal("Nightly import", back!.Name);
            Assert.Equal("tmpl-123", back.TemplateId);
            Assert.Equal(ScheduleKind.Weekly, back.Schedule.Kind);
            Assert.Equal(new TimeSpan(6, 30, 0), back.Schedule.TimeOfDay);
            Assert.Contains(DayOfWeek.Monday, back.Schedule.DaysOfWeek);
            Assert.Contains(DayOfWeek.Wednesday, back.Schedule.DaysOfWeek);
            Assert.Equal(NotifyTrigger.Always, back.NotifyOn);
        }

        [Fact]
        public void Save_stamps_CreatedUtc_only_once()
        {
            var store = new JobStore(_dir);
            var job = Sample();
            store.Save(job, "2026-06-18T00:00:00Z");
            store.Save(job, "2026-07-01T00:00:00Z");

            Assert.Equal("2026-06-18T00:00:00Z", store.Get(job.Id)!.CreatedUtc);
        }

        [Fact]
        public void ForClient_isolates_by_client()
        {
            var store = new JobStore(_dir);
            store.Save(Sample("C1"), "2026-06-18T00:00:00Z");
            store.Save(Sample("C1"), "2026-06-18T00:00:00Z");
            store.Save(Sample("C2"), "2026-06-18T00:00:00Z");

            Assert.Equal(2, store.ForClient("C1").Count);
            Assert.Single(store.ForClient("C2"));
            Assert.Equal(3, store.LoadAll().Count);
        }

        [Fact]
        public void Delete_removes_the_job()
        {
            var store = new JobStore(_dir);
            var job = Sample();
            store.Save(job, "2026-06-18T00:00:00Z");
            store.Delete(job.Id);

            Assert.Null(store.Get(job.Id));
            Assert.Empty(store.LoadAll());
        }

        [Fact]
        public void Corrupt_file_is_skipped_not_fatal()
        {
            var store = new JobStore(_dir);
            store.Save(Sample(), "2026-06-18T00:00:00Z");
            File.WriteAllText(Path.Combine(_dir, "garbage.json"), "{ not valid json ");

            Assert.Single(store.LoadAll()); // the good one survives, the bad one is ignored
        }
    }
}
