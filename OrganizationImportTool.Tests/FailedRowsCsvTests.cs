using System;
using System.IO;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Scheduling;

namespace OrganizationImportTool.Tests
{
    public class FailedRowsCsvTests : IDisposable
    {
        private readonly string _dir;

        public FailedRowsCsvTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "oit_csv_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        private static SourceRow Row(int n, string code, string name)
        {
            var r = new SourceRow { RowNumber = n };
            r["Code"] = code;
            r["Name"] = name;
            return r;
        }

        [Fact]
        public void Writes_attention_rows_in_original_columns()
        {
            var result = new PipelineResult { SourceHeaders = new[] { "Code", "Name" } };
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 1, SentCode = "OK1", SourceRow = Row(1, "OK1", "Good Co"),
                Response = new EadaptorResponse { TransportOk = true, Status = "PRS" }
            });
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 2, SentCode = "BAD2", SourceRow = Row(2, "BAD2", "Bad, Inc \"quoted\""),
                Response = EadaptorResponse.ValidationFailed("Code required")
            });

            string path = Path.Combine(_dir, "out.csv");
            var written = FailedRowsCsv.Write(path, result);

            Assert.Equal(path, written);
            var text = File.ReadAllText(path);
            Assert.Contains("Code,Name,Status,Reason", text);
            Assert.Contains("Code required", text);
            Assert.Contains("\"Bad, Inc \"\"quoted\"\"\"", text); // RFC-4180 escaping
            Assert.DoesNotContain("Good Co", text);                // the OK row is excluded
        }

        [Fact]
        public void Returns_null_when_no_attention_rows()
        {
            var result = new PipelineResult { SourceHeaders = new[] { "Code" } };
            result.Outcomes.Add(new OrgSendOutcome
            {
                RowNumber = 1, Response = new EadaptorResponse { TransportOk = true, Status = "PRS" }
            });

            Assert.Null(FailedRowsCsv.Write(Path.Combine(_dir, "none.csv"), result));
        }
    }
}
