using OrganizationImportTool.Ai;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Profiling;
using OrganizationImportTool.Sync;
using OrganizationImportTool.Transform;
using Xunit;

namespace OrganizationImportTool.Tests
{
    /// <summary>Scriptable pipeline driver: approves every gate unless told to cancel one.</summary>
    internal sealed class FakePipelineUi : IPipelineUi
    {
        public string? CancelAtStage;            // "mapping" | "profile" | "duplicates" | "cleaning" | "enrichment"
        public bool SkipDuplicates = true;
        public List<string> LogLines { get; } = new();

        public Task<MappingResult?> ConfirmMappingAsync(FieldContract contract, SourceTable table,
            MappingResult suggested, string clientId, TemplateStore templates)
        {
            if (CancelAtStage == "mapping") return Task.FromResult<MappingResult?>(null);
            foreach (var c in suggested.Columns) c.Approved = true;
            return Task.FromResult<MappingResult?>(suggested);
        }

        public Task<bool> ConfirmProfileAsync(ProfileReport report) => Task.FromResult(CancelAtStage != "profile");

        public Task<DuplicateDecision> ReviewDuplicatesAsync(List<DuplicateGroup> groups)
        {
            if (CancelAtStage == "duplicates") return Task.FromResult(new DuplicateDecision { Cancelled = true });
            var skip = new HashSet<int>();
            if (SkipDuplicates)
                foreach (var g in groups) foreach (var ex in g.Extras) skip.Add(ex.RowNumber);
            return Task.FromResult(new DuplicateDecision { SkipDuplicates = SkipDuplicates, RowsToSkip = skip });
        }

        public Task<bool> ReviewCleaningAsync(List<CleaningChange> changes)
        {
            if (CancelAtStage == "cleaning") return Task.FromResult(false);
            foreach (var c in changes) c.Accept = true;
            return Task.FromResult(true);
        }

        public Task<bool> ReviewEnrichmentAsync(List<EnrichmentSuggestion> suggestions)
        {
            if (CancelAtStage == "enrichment") return Task.FromResult(false);
            foreach (var s in suggestions) s.Accept = true;
            return Task.FromResult(true);
        }

        public ResumeChoice ResumeAnswer = ResumeChoice.SkipAlreadyImported;
        public string? LastCrashDescription;

        public Task<ResumeChoice> ConfirmResumeAsync(int alreadyImported, int totalRows, string? crashedRunDescription)
        {
            LastCrashDescription = crashedRunDescription;
            return Task.FromResult(CancelAtStage == "resume" ? ResumeChoice.Cancel : ResumeAnswer);
        }

        public void Log(string line) => LogLines.Add(line);
        public void Status(string text) { }
        public void Progress(int current, int total) { }
        public Task WaitIfPausedAsync(int processed, int total, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>In-memory CargoWise: every send succeeds (PRS) unless the code is scripted to fail.</summary>
    internal sealed class FakeEadaptorClient : IEadaptorClient
    {
        public List<string> SentXml { get; } = new();
        public HashSet<string> FailCodes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<EadaptorResponse> SendAsync(string nativeXml, CancellationToken ct = default)
        {
            SentXml.Add(nativeXml);
            string code = ExtractCode(nativeXml);
            if (FailCodes.Contains(code))
                return Task.FromResult(new EadaptorResponse { TransportOk = true, HttpStatus = 200, Status = "ERR", Error = "scripted failure" });
            return Task.FromResult(new EadaptorResponse
            {
                TransportOk = true, HttpStatus = 200, Status = "PRS",
                ExternalCode = code, LocalCode = code, EntityPk = Guid.NewGuid().ToString(),
                EntityName = "OrgHeader", MessageNumber = "MSG1"
            });
        }

        private static string ExtractCode(string xml)
        {
            var doc = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XNamespace ns = "http://www.cargowise.com/Schemas/Native/2011/11";
            return doc.Descendants(ns + "Code").FirstOrDefault()?.Value ?? "";
        }
    }

    public class ImportPipelineTests : IDisposable
    {
        private readonly TempDir _templateDir = new();
        private readonly TempFile _db = new(".db");
        private readonly TempFile _csv = new(".csv");

        public void Dispose() { _templateDir.Dispose(); _db.Dispose(); _csv.Dispose(); }

        private void WriteCsv(params string[] lines)
            => File.WriteAllLines(_csv.Path, new[] { "Account Code,Company Name,Street,Town,Country" }.Concat(lines));

        private (ImportPipeline pipeline, FakePipelineUi ui, FakeEadaptorClient client, FeedbackStore feedback, TemplateStore templates)
            Build(AiRouter? ai = null, AiSettings? aiSettings = null)
        {
            var ui = new FakePipelineUi();
            var client = new FakeEadaptorClient();
            var feedback = new FeedbackStore(_db.Path);
            var templates = new TemplateStore(_templateDir.Path);
            var pipeline = new ImportPipeline(TestData.Contract, new SourceReaderFactory(), templates,
                feedback, ai, aiSettings ?? new AiSettings(), client, ui);
            return (pipeline, ui, client, feedback, templates);
        }

        private static PipelineRequest Request(string file, bool dryRun = false) => new()
        {
            FilePath = file, ClientId = "TESTCLIENT", ClientName = "Test Client",
            Username = "tester", OwnerCode = "CENGLOBAL", DryRun = dryRun, LearnMapping = !dryRun, LogDir = null
        };

        [Fact]
        public async Task Happy_path_sends_every_row_and_records_ledger()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "GLOBE001,Globe Trading,2 Dock Rd,Melbourne,AU");
            var (pipeline, _, client, feedback, templates) = Build();

            var result = await pipeline.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.False(result.Cancelled);
            Assert.Equal(2, result.Outcomes.Count);
            Assert.Equal(2, result.Ok);
            Assert.Equal(2, client.SentXml.Count);
            Assert.Equal(2, feedback.CountForClient("TESTCLIENT"));
            Assert.NotNull(templates.GetAuto("TESTCLIENT")); // confirmed mapping was learned
        }

        [Fact]
        public async Task Cancel_at_mapping_sends_nothing()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU");
            var (pipeline, ui, client, feedback, _) = Build();
            ui.CancelAtStage = "mapping";

            var result = await pipeline.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.True(result.Cancelled);
            Assert.Equal("mapping", result.CancelledAtStage);
            Assert.Empty(result.Outcomes);
            Assert.Empty(client.SentXml);
            Assert.Equal(0, feedback.CountForClient("TESTCLIENT"));
        }

        [Fact]
        public async Task Cancel_at_profile_sends_nothing()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU");
            var (pipeline, ui, client, _, _) = Build();
            ui.CancelAtStage = "profile";

            var result = await pipeline.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.True(result.Cancelled);
            Assert.Equal("profile", result.CancelledAtStage);
            Assert.Empty(client.SentXml);
        }

        [Fact]
        public async Task Dry_run_never_transmits_records_or_learns()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU");
            var (pipeline, _, client, feedback, templates) = Build();

            var result = await pipeline.RunAsync(Request(_csv.Path, dryRun: true), CancellationToken.None);

            Assert.False(result.Cancelled);
            Assert.Single(result.Outcomes);
            Assert.True(result.Outcomes[0].Response.IsSimulatedOk);
            Assert.False(string.IsNullOrEmpty(result.Outcomes[0].SentXml)); // would-be XML is previewable
            Assert.Empty(client.SentXml);
            Assert.Equal(0, feedback.CountForClient("TESTCLIENT"));
            Assert.Null(templates.GetAuto("TESTCLIENT")); // a preview must not mutate learned memory
        }

        [Fact]
        public async Task Validation_blocked_row_is_not_sent()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     ",NoCode Company,9 Empty Rd,Sydney,AU"); // missing required org code
            var (pipeline, _, client, _, _) = Build();

            var result = await pipeline.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.Equal(2, result.Outcomes.Count);
            Assert.Equal(1, result.Ok);
            Assert.Single(client.SentXml); // only the valid row reached CargoWise
            var blocked = result.Outcomes.Single(o => o.Response.NotSent);
            Assert.Contains("required", blocked.Response.Error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Duplicate_rows_are_skipped_keeping_the_first()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "ACME001,Acme Imports Pty,1 Test St,Sydney,AU"); // identical code
            var (pipeline, _, client, _, _) = Build();

            var result = await pipeline.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.Equal(2, result.Outcomes.Count);
            Assert.Single(client.SentXml);
            var dup = result.Outcomes.Single(o => o.Response.IsDuplicate);
            Assert.Equal(2, dup.RowNumber); // the earlier row was kept
        }

        [Fact]
        public async Task Failing_ai_provider_changes_nothing_vs_no_ai()
        {
            // A provider with no BaseUrl/Model fails instantly inside the client guard, so this
            // proves the "AI enabled but unavailable" run is identical to the "AI off" run.
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "GLOBE001,Globe Trading,2 Dock Rd,Melbourne,AU");

            var (noAiPipeline, _, noAiClient, _, _) = Build();
            var noAi = await noAiPipeline.RunAsync(Request(_csv.Path, dryRun: true), CancellationToken.None);

            var brokenSettings = new AiSettings { Enabled = true };
            brokenSettings.Providers.Add(new AiProviderProfile { Name = "Broken", Enabled = true }); // no BaseUrl/Model
            var router = new AiRouter(brokenSettings, new TokenUsageStore { Persist = false });
            var (aiPipeline, _, aiClient, _, _) = Build(router, brokenSettings);
            var withBrokenAi = await aiPipeline.RunAsync(Request(_csv.Path, dryRun: true), CancellationToken.None);

            Assert.Equal(noAi.Outcomes.Count, withBrokenAi.Outcomes.Count);
            Assert.Equal(noAi.WouldSend, withBrokenAi.WouldSend);
            Assert.Equal(
                noAi.Outcomes.Select(o => (o.RowNumber, o.SentCode, o.Response.Outcome)),
                withBrokenAi.Outcomes.Select(o => (o.RowNumber, o.SentCode, o.Response.Outcome)));
            Assert.Empty(noAiClient.SentXml);
            Assert.Empty(aiClient.SentXml);
        }

        [Fact]
        public async Task Second_upload_offers_resume_and_skips_already_imported_rows()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "GLOBE001,Globe Trading,2 Dock Rd,Melbourne,AU");
            var (p1, _, c1, feedback, _) = Build();
            var r1 = await p1.RunAsync(Request(_csv.Path), CancellationToken.None);
            Assert.Equal(2, r1.Ok);

            // Same file again: the resume gate fires; default fake answer = skip.
            var (p2, ui2, c2, _, _) = Build();
            var r2 = await p2.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.Equal(2, r2.Outcomes.Count);
            Assert.All(r2.Outcomes, o => Assert.True(o.Response.IsAlreadyImported));
            Assert.Empty(c2.SentXml); // nothing re-sent
        }

        [Fact]
        public async Task Resend_all_choice_sends_everything_again()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU");
            var (p1, _, _, _, _) = Build();
            await p1.RunAsync(Request(_csv.Path), CancellationToken.None);

            var (p2, ui2, c2, _, _) = Build();
            ui2.ResumeAnswer = ResumeChoice.ResendAll;
            var r2 = await p2.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.Equal(1, r2.Ok);
            Assert.Single(c2.SentXml);
        }

        [Fact]
        public async Task Crashed_run_is_detected_on_the_next_upload_of_the_same_file()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "GLOBE001,Globe Trading,2 Dock Rd,Melbourne,AU");

            // Run 1 "crashes": cancel after the loop starts so the run journal never completes.
            var (p1, ui1, _, feedback, _) = Build();
            using (var cts = new CancellationTokenSource())
            {
                var firstSent = false;
                ui1.LogLines.Clear();
                // cancel as soon as the first row's outcome is logged -> journal left open
                var run1 = p1.RunAsync(Request(_csv.Path), cts.Token);
                // crude but deterministic: wait for completion of row 1 then cancel can't be timed
                // reliably here, so instead simulate the crash by completing run1 fully and
                // re-opening a journal entry manually:
                await run1;
            }
            string hash;
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var fs = File.OpenRead(_csv.Path))
                hash = Convert.ToHexString(sha.ComputeHash(fs));
            feedback.BeginRun("TESTCLIENT", Path.GetFileName(_csv.Path), hash, 2, "tester"); // never completed

            var (p2, ui2, _, _, _) = Build();
            await p2.RunAsync(Request(_csv.Path), CancellationToken.None);

            Assert.NotNull(ui2.LastCrashDescription);
            Assert.Contains("stopped part-way", ui2.LastCrashDescription);
        }

        [Fact]
        public async Task Dry_run_never_triggers_the_resume_gate()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU");
            var (p1, _, _, _, _) = Build();
            await p1.RunAsync(Request(_csv.Path), CancellationToken.None);

            var (p2, ui2, c2, _, _) = Build();
            ui2.ResumeAnswer = ResumeChoice.Cancel; // would cancel IF the gate fired
            var r2 = await p2.RunAsync(Request(_csv.Path, dryRun: true), CancellationToken.None);

            Assert.False(r2.Cancelled);
            Assert.Single(r2.Outcomes);
            Assert.True(r2.Outcomes[0].Response.IsSimulatedOk);
        }

        [Fact]
        public async Task Stop_mid_run_keeps_partial_outcomes_without_cancelled_flag()
        {
            WriteCsv("ACME001,Acme Imports,1 Test St,Sydney,AU",
                     "GLOBE001,Globe Trading,2 Dock Rd,Melbourne,AU");
            var (pipeline, _, client, _, _) = Build();

            using var cts = new CancellationTokenSource();
            cts.Cancel(); // token already cancelled when the send loop starts

            var result = await pipeline.RunAsync(Request(_csv.Path), cts.Token);

            Assert.False(result.Cancelled);   // Stop is not a gate-cancel: partial results are kept
            Assert.Empty(result.Outcomes);    // stopped before any row
            Assert.Empty(client.SentXml);
        }
    }
}
