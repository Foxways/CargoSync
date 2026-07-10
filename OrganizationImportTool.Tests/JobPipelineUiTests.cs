using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrganizationImportTool.Dedup;
using OrganizationImportTool.Enrichment;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Pipeline;
using OrganizationImportTool.Scheduling;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Tests
{
    public class JobPipelineUiTests : IDisposable
    {
        private readonly string _dir;
        private readonly TemplateStore _templates;

        public JobPipelineUiTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "oit_jobui_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _templates = new TemplateStore(_dir);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
        }

        [Fact]
        public async Task Resume_always_skips_already_imported()
        {
            var ui = new JobPipelineUi(new ScheduledJob(), _templates);
            var choice = await ui.ConfirmResumeAsync(5, 10, "a previous run crashed");
            Assert.Equal(ResumeChoice.SkipAlreadyImported, choice);
        }

        [Fact]
        public async Task SkipDuplicates_policy_is_honoured()
        {
            var on = new JobPipelineUi(new ScheduledJob { SkipDuplicates = true }, _templates);
            Assert.True((await on.ReviewDuplicatesAsync(new List<DuplicateGroup>())).SkipDuplicates);

            var off = new JobPipelineUi(new ScheduledJob { SkipDuplicates = false }, _templates);
            Assert.False((await off.ReviewDuplicatesAsync(new List<DuplicateGroup>())).SkipDuplicates);
        }

        [Fact]
        public async Task AutoApplyCleaning_toggles_accept_flags()
        {
            var changes = new List<CleaningChange> { new() { RowNumber = 1 }, new() { RowNumber = 2 } };

            var on = new JobPipelineUi(new ScheduledJob { AutoApplyCleaning = true }, _templates);
            await on.ReviewCleaningAsync(changes);
            Assert.All(changes, c => Assert.True(c.Accept));

            var changes2 = new List<CleaningChange> { new() { RowNumber = 1 } };
            var off = new JobPipelineUi(new ScheduledJob { AutoApplyCleaning = false }, _templates);
            await off.ReviewCleaningAsync(changes2);
            Assert.All(changes2, c => Assert.False(c.Accept));
        }

        [Fact]
        public async Task AutoApplyCleaning_toggles_enrichment_too()
        {
            var sugg = new List<EnrichmentSuggestion> { new() { RowNumber = 1, Path = "x", Value = "y" } };
            var on = new JobPipelineUi(new ScheduledJob { AutoApplyCleaning = true }, _templates);
            await on.ReviewEnrichmentAsync(sugg);
            Assert.All(sugg, s => Assert.True(s.Accept));
        }

        [Fact]
        public async Task Mapping_without_template_auto_approves()
        {
            var result = new MappingResult
            {
                Columns = { new ColumnMapping { SourceHeader = "Name", TargetPath = "orgHeader.fullName", Approved = false } }
            };
            var ui = new JobPipelineUi(new ScheduledJob { TemplateId = null }, _templates);

            var confirmed = await ui.ConfirmMappingAsync(null!, new Ingestion.SourceTable(), result, "C1", _templates);

            Assert.NotNull(confirmed);
            Assert.All(confirmed!.Columns, c => Assert.True(c.Approved));
        }

        [Fact]
        public async Task Missing_bound_template_falls_back_to_automap()
        {
            var result = new MappingResult
            {
                Columns = { new ColumnMapping { SourceHeader = "Name", TargetPath = "orgHeader.fullName" } }
            };
            var ui = new JobPipelineUi(new ScheduledJob { TemplateId = "no-such-template" }, _templates);

            var confirmed = await ui.ConfirmMappingAsync(null!, new Ingestion.SourceTable(), result, "C1", _templates);

            Assert.NotNull(confirmed);
            Assert.Contains(ui.Lines, l => l.Contains("not found"));
        }
    }
}
