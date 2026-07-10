using System;
using System.Linq;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class NotifierTests
    {
        private static PipelineResult SampleResult()
        {
            var result = new PipelineResult { Elapsed = TimeSpan.FromSeconds(3) };
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 1, SentCode = "ZZACME001",
                Response = new EadaptorResponse { TransportOk = true, Status = "PRS" }
            });
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 2, SentCode = "ZZBAD002",
                Response = EadaptorResponse.ValidationFailed("Organization Code: required value is missing")
            });
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 3, SentCode = "ZZDUP003",
                Response = EadaptorResponse.SkippedDuplicate("duplicate of row 1")
            });
            return result;
        }

        [Fact]
        public void Report_classifies_rows_correctly()
        {
            var r = ImportReport.FromResult("Nightly", "Acme", "orders.xlsx", SampleResult(), dryRun: false);

            Assert.Equal(3, r.Total);
            Assert.Equal(1, r.Ok);
            Assert.Equal(1, r.Blocked);
            Assert.Equal(1, r.SkippedDuplicates);
            Assert.Single(r.Failures);
            Assert.Equal(2, r.Failures[0].RowNumber);
            Assert.Contains("required value is missing", r.Failures[0].Reason);
            Assert.False(r.IsClean);
        }

        [Fact]
        public void Clean_report_when_no_blocks()
        {
            var result = new PipelineResult();
            result.Outcomes.Add(new OrgSendOutcome { RowNumber = 1, Response = new EadaptorResponse { TransportOk = true, Status = "PRS" } });
            var r = ImportReport.FromResult("J", "Acme", "f.xlsx", result, dryRun: false);
            Assert.True(r.IsClean);
            Assert.Empty(r.Failures);
        }

        [Theory]
        [InlineData(NotifyTrigger.Never, false, false)]
        [InlineData(NotifyTrigger.Never, true, false)]
        [InlineData(NotifyTrigger.Always, true, true)]
        [InlineData(NotifyTrigger.Always, false, true)]
        [InlineData(NotifyTrigger.OnFailure, true, false)]  // clean -> no email
        [InlineData(NotifyTrigger.OnFailure, false, true)]  // has blocks -> email
        public void ShouldSend_respects_trigger(NotifyTrigger trigger, bool clean, bool expected)
        {
            var job = new ScheduledJob { NotifyOn = trigger };
            var report = new ImportReport { Blocked = clean ? 0 : 2 };
            Assert.Equal(expected, Notifier.ShouldSend(job, report));
        }

        [Fact]
        public void ParseRecipients_splits_dedupes_and_validates()
        {
            var list = Notifier.ParseRecipients("a@x.com, b@y.com; a@x.com", "ops@z.com\nnot-an-email");
            Assert.Equal(3, list.Count);
            Assert.Contains("a@x.com", list);
            Assert.Contains("b@y.com", list);
            Assert.Contains("ops@z.com", list);
            Assert.DoesNotContain("not-an-email", list);
        }

        [Fact]
        public void BuildMessage_sets_recipients_subject_and_body()
        {
            var smtp = new SmtpSettings { FromAddress = "noreply@acme.example", FromDisplayName = "CargoSync" };
            var job = new ScheduledJob
            {
                Name = "Nightly",
                NotifyClientEmails = "ops@acme.example, billing@acme.example",
                NotifyInternalEmails = "team@us.example"
            };
            var report = ImportReport.FromResult("Nightly", "Acme", "orders.xlsx", SampleResult(), dryRun: false);

            var msg = Notifier.BuildMessage(smtp, job, report);

            Assert.NotNull(msg);
            Assert.Equal(3, msg!.To.Count);
            Assert.Contains("Acme", msg.Subject);
            Assert.Contains("1 sent", msg.Subject);
            Assert.Contains("required value is missing", msg.HtmlBody);
            Assert.Contains("orders.xlsx", msg.TextBody);
        }

        [Fact]
        public void BuildMessage_is_null_without_recipients()
        {
            var smtp = new SmtpSettings { FromAddress = "noreply@acme.example" };
            var job = new ScheduledJob(); // no notify addresses
            var report = ImportReport.FromResult("J", "Acme", "f.xlsx", SampleResult(), dryRun: false);
            Assert.Null(Notifier.BuildMessage(smtp, job, report));
        }

        [Fact]
        public void Subject_flags_failure_and_dry_run()
        {
            Assert.Contains("FAILED", Notifier.Subject(new ImportReport { ClientName = "Acme", FileName = "f.xlsx", Error = "boom" }));
            Assert.Contains("Dry run", Notifier.Subject(new ImportReport { ClientName = "Acme", FileName = "f.xlsx", DryRun = true, WouldSend = 5 }));
        }
    }
}
