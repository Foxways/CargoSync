using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class JobRunnerTests : IDisposable
    {
        private readonly string _root;
        private static readonly DateTime Now = new(2026, 6, 19, 2, 0, 0, DateTimeKind.Utc);

        public JobRunnerTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "oit_jobrun_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private string MakeFile(string name)
        {
            string p = Path.Combine(_root, name);
            File.WriteAllText(p, "dummy");
            return p;
        }

        private ScheduledJob Job() => new()
        {
            Name = "Test", ClientId = "C1", ClientName = "Acme",
            SourceFolder = _root, FilePattern = "*.csv",
            PostProcess = PostProcessAction.Move
        };

        private static PipelineResult CleanResult()
        {
            var r = new PipelineResult();
            r.Outcomes.Add(new OrgSendOutcome { RowNumber = 1, Response = new EadaptorResponse { TransportOk = true, Status = "PRS" } });
            return r;
        }

        private static PipelineResult BlockedResult()
        {
            var r = new PipelineResult();
            r.Outcomes.Add(new OrgSendOutcome { RowNumber = 1, Response = EadaptorResponse.ValidationFailed("Code required") });
            return r;
        }

        [Fact]
        public async Task Clean_files_move_to_processed()
        {
            MakeFile("a.csv");
            MakeFile("b.csv");
            var job = Job();

            var summary = await JobRunner.RunJobAsync(job,
                (f, ct) => Task.FromResult(CleanResult()), _root, null, null, Now);

            Assert.Equal(2, summary.FileCount);
            Assert.Equal(0, summary.ExitCode);
            Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "processed")).Length);
            Assert.Empty(Directory.GetFiles(_root, "*.csv")); // originals moved out
        }

        [Fact]
        public async Task Run_level_exception_moves_file_to_failed()
        {
            MakeFile("a.csv");
            var job = Job();

            var summary = await JobRunner.RunJobAsync(job,
                (f, ct) => throw new InvalidOperationException("boom"), _root, null, null, Now);

            Assert.Equal(2, summary.ExitCode);               // files needed attention
            Assert.True(summary.Files[0].MovedToFailed);
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "failed")));
            Assert.Contains("boom", summary.Files[0].Report.Error);
        }

        [Fact]
        public async Task Blocked_rows_write_attention_csv_and_move_to_failed()
        {
            MakeFile("a.csv");
            var job = Job();
            job.AttachFailedRowsCsv = true;

            var summary = await JobRunner.RunJobAsync(job,
                (f, ct) => Task.FromResult(BlockedResult()), _root, null, null, Now);

            Assert.Equal(2, summary.ExitCode);                       // not clean (a blocked row)
            Assert.True(summary.Files[0].MovedToFailed);             // partial import -> failed subfolder
            Assert.Single(Directory.GetFiles(Path.Combine(_root, "failed")));
            Assert.NotNull(summary.Files[0].Report.FailedRowsCsvPath);
            Assert.True(File.Exists(summary.Files[0].Report.FailedRowsCsvPath!));
        }

        [Fact]
        public async Task Delete_policy_removes_clean_file()
        {
            MakeFile("a.csv");
            var job = Job();
            job.PostProcess = PostProcessAction.Delete;

            await JobRunner.RunJobAsync(job, (f, ct) => Task.FromResult(CleanResult()), _root, null, null, Now);

            Assert.Empty(Directory.GetFiles(_root, "*.csv"));
            Assert.False(Directory.Exists(Path.Combine(_root, "processed")));
        }

        [Fact]
        public async Task Leave_policy_keeps_file_in_place()
        {
            MakeFile("a.csv");
            var job = Job();
            job.PostProcess = PostProcessAction.Leave;

            await JobRunner.RunJobAsync(job, (f, ct) => Task.FromResult(CleanResult()), _root, null, null, Now);

            Assert.Single(Directory.GetFiles(_root, "*.csv"));
        }

        [Fact]
        public async Task Missing_source_folder_is_a_job_error()
        {
            var job = Job();
            job.SourceFolder = Path.Combine(_root, "does-not-exist");

            var summary = await JobRunner.RunJobAsync(job, (f, ct) => Task.FromResult(CleanResult()), _root, null, null, Now);

            Assert.True(summary.JobError);
            Assert.Equal(4, summary.ExitCode);
        }

        [Fact]
        public void EnumerateFiles_excludes_processed_and_failed_subfolders()
        {
            MakeFile("a.csv");
            Directory.CreateDirectory(Path.Combine(_root, "processed"));
            File.WriteAllText(Path.Combine(_root, "processed", "old.csv"), "x");
            Directory.CreateDirectory(Path.Combine(_root, "failed"));
            File.WriteAllText(Path.Combine(_root, "failed", "bad.csv"), "x");

            var job = Job();
            job.Recursive = true;

            var files = JobRunner.EnumerateFiles(job);
            Assert.Single(files);
            Assert.EndsWith("a.csv", files[0]);
        }

        [Fact]
        public async Task Pattern_filters_by_extension()
        {
            MakeFile("a.csv");
            MakeFile("b.xlsx");
            var job = Job(); // *.csv only

            var summary = await JobRunner.RunJobAsync(job, (f, ct) => Task.FromResult(CleanResult()), _root, null, null, Now);

            Assert.Equal(1, summary.FileCount);
            Assert.True(File.Exists(Path.Combine(_root, "b.xlsx"))); // untouched
        }
    }
}
