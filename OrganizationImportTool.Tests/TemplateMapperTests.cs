using OrganizationImportTool.Mapping;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class TemplateMapperTests
    {
        [Fact]
        public void LearnFrom_then_ApplyLearned_recalls_manual_fixes()
        {
            var suggester = new MappingSuggester(TestData.Contract);
            var table = TestData.Table(
                new[] { "AcctRef", "Company Name" },
                new[] { "ACME001", "Acme Imports" });

            // Operator manually fixes the cryptic column, then confirms.
            var confirmed = suggester.Suggest(table);
            var acct = confirmed.Columns.First(c => c.SourceHeader == "AcctRef");
            acct.TargetPath = "orgHeader.code";
            acct.Source = MappingSource.Manual;
            acct.Approved = true;
            acct.Include = true;

            var template = TemplateMapper.LearnFrom(confirmed, existing: null, clientId: "C1");

            // Next upload: fresh suggestion + learned overlay.
            var fresh = suggester.Suggest(table);
            int recalled = TemplateMapper.ApplyLearned(template, table, TestData.Contract, fresh);

            Assert.True(recalled >= 1);
            var recalledCol = fresh.Columns.First(c => c.SourceHeader == "AcctRef");
            Assert.Equal("orgHeader.code", recalledCol.TargetPath);
            Assert.Equal(MappingConfidence.High, recalledCol.Confidence);
            Assert.Equal(MappingSource.Template, recalledCol.Source);
            Assert.True(recalledCol.Approved);
        }

        [Fact]
        public void LearnFrom_merges_into_existing_memory()
        {
            var suggester = new MappingSuggester(TestData.Contract);

            var table1 = TestData.Table(new[] { "AcctRef" }, new[] { "A1" });
            var r1 = suggester.Suggest(table1);
            r1.Columns[0].TargetPath = "orgHeader.code";
            var memory = TemplateMapper.LearnFrom(r1, null, "C1");

            var table2 = TestData.Table(new[] { "LegalEntity" }, new[] { "Acme" });
            var r2 = suggester.Suggest(table2);
            r2.Columns[0].TargetPath = "orgHeader.fullName";
            memory = TemplateMapper.LearnFrom(r2, memory, "C1");

            // Headers from the earlier file must survive the merge.
            Assert.Contains(memory.Entries, e => e.SourceHeader == "AcctRef" && e.TargetPath == "orgHeader.code");
            Assert.Contains(memory.Entries, e => e.SourceHeader == "LegalEntity" && e.TargetPath == "orgHeader.fullName");
        }

        [Fact]
        public void TemplateStore_auto_template_is_hidden_from_manual_picker()
        {
            using var dir = TestData.TempFolder();
            var store = new TemplateStore(dir.Path);

            var auto = new MappingTemplate { Id = TemplateStore.AutoId("C1"), ClientId = "C1", Name = "auto", IsAuto = true };
            store.SaveAuto(auto, DateTime.UtcNow.ToString("o"));

            var manual = new MappingTemplate { ClientId = "C1", Name = "My manual template" };
            store.Save(manual, DateTime.UtcNow.ToString("o"));

            Assert.NotNull(store.GetAuto("C1"));
            var picker = store.ForClient("C1");
            Assert.Contains(picker, t => t.Name == "My manual template");
            Assert.DoesNotContain(picker, t => t.IsAuto);
        }

        [Fact]
        public void Template_roundtrip_preserves_constants_and_rules()
        {
            var suggester = new MappingSuggester(TestData.Contract);
            var table = TestData.Table(new[] { "Company Name", "Type" }, new[] { "Acme", "IMP" });

            var result = suggester.Suggest(table);
            result.Constants["orgHeader.code"] = "FIXED01";
            result.Rules.Add(new TransformRule
            {
                Enabled = true,
                WhenColumn = "Type",
                Op = RuleOp.Contains,
                WhenValue = "IMP",
                ThenField = "orgHeader.isConsignee",
                ThenValue = "true",
            });

            var template = TemplateMapper.ToTemplate(result, "round trip", "C1");
            var into = suggester.Suggest(table);
            TemplateMapper.Apply(template, table, TestData.Contract, into);

            Assert.Equal("FIXED01", into.Constants["orgHeader.code"]);
            Assert.Single(into.Rules);
            Assert.Equal("orgHeader.isConsignee", into.Rules[0].ThenField);
        }
    }
}
