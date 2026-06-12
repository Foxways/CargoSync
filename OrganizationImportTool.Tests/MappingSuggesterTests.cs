using OrganizationImportTool.Mapping;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class MappingSuggesterTests
    {
        private static MappingSuggester Suggester => new(TestData.Contract);

        [Fact]
        public void Standard_headers_map_high_confidence()
        {
            var table = TestData.Table(
                new[] { "Account Code", "Company Name", "Street", "Town", "Country", "Closest Port", "Consignee" },
                new[] { "ACME001", "Acme Imports Pty Ltd", "1 Test St", "Sydney", "AU", "AUSYD", "Yes" });

            var result = Suggester.Suggest(table);

            string? PathOf(string header) => result.Columns.First(c => c.SourceHeader == header).TargetPath;
            Assert.Equal("orgHeader.code", PathOf("Account Code"));
            Assert.Equal("orgHeader.fullName", PathOf("Company Name"));
            Assert.All(result.Columns, c => Assert.NotNull(c.TargetPath));
            Assert.Contains(result.Columns, c => c.Confidence == MappingConfidence.High);
        }

        [Fact]
        public void Two_columns_never_map_to_same_target()
        {
            var table = TestData.Table(
                new[] { "Company Name", "Organisation Name", "Full Name" },
                new[] { "Acme", "Acme", "Acme" });

            var result = Suggester.Suggest(table);
            var targets = result.Columns.Where(c => c.TargetPath != null).Select(c => c.TargetPath!).ToList();
            Assert.Equal(targets.Count, targets.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void Nonsense_header_stays_unmapped_with_rationale()
        {
            var table = TestData.Table(new[] { "Flibbertigibbet Quotient" }, new[] { "42" });
            var col = Suggester.Suggest(table).Columns.Single();
            Assert.Null(col.TargetPath);
            Assert.Equal(MappingConfidence.Unmapped, col.Confidence);
            Assert.False(string.IsNullOrWhiteSpace(col.Rationale));
        }

        [Fact]
        public void High_matches_are_auto_approved()
        {
            var table = TestData.Table(new[] { "Company Name" }, new[] { "Acme" });
            var col = Suggester.Suggest(table).Columns.Single();
            Assert.Equal(MappingConfidence.High, col.Confidence);
            Assert.True(col.Approved);
        }

        [Fact]
        public void Suggest_populates_explainability_candidates()
        {
            var table = TestData.Table(new[] { "Company Name" }, new[] { "Acme" });
            var col = Suggester.Suggest(table).Columns.Single();
            Assert.NotEmpty(col.Candidates);
            Assert.Contains(col.Candidates, k => k.Chosen);
        }

        [Fact]
        public void Unmapped_required_fields_are_reported()
        {
            var table = TestData.Table(new[] { "Some Random Notes" }, new[] { "hello" });
            var result = Suggester.Suggest(table);
            Assert.Contains(result.UnmappedRequired, f => f.Path == "orgHeader.code");
            Assert.Contains(result.UnmappedRequired, f => f.Path == "orgHeader.fullName");
        }

        [Fact]
        public void Constants_satisfy_required_fields()
        {
            var table = TestData.Table(new[] { "Company Name" }, new[] { "Acme" });
            var result = Suggester.Suggest(table);
            result.Constants["orgHeader.code"] = "FIXED01";
            Suggester.RecomputeUnmappedRequired(result);
            Assert.DoesNotContain(result.UnmappedRequired, f => f.Path == "orgHeader.code");
        }
    }
}
