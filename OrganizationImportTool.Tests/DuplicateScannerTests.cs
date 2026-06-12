using OrganizationImportTool.Dedup;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class DuplicateScannerTests
    {
        private static List<OrgKey> SampleKeys() => new()
        {
            new OrgKey { RowNumber = 1, Code = "ZZACME001", Name = "Acme Imports Pty Ltd", Country = "AU" },
            new OrgKey { RowNumber = 2, Code = "GLOBE1", Name = "Globe Trading", Country = "AU" },
            new OrgKey { RowNumber = 3, Code = "ZZACME001", Name = "Acme Imports", Country = "AU" },     // same code as row 1
            new OrgKey { RowNumber = 4, Code = "ZZACME9", Name = "ACME IMPORTS", Country = "AU" },        // joins by name
            new OrgKey { RowNumber = 5, Code = "GLOBE2", Name = "Globe Trading", Country = "US" },        // same name, other country
            new OrgKey { RowNumber = 6, Code = "UNIQ1", Name = "Unique Logistics", Country = "SG" },
        };

        [Fact]
        public void Code_and_name_signals_cluster_transitively()
        {
            var groups = new DuplicateScanner().Scan(SampleKeys());
            var acme = groups.SingleOrDefault(g => g.Rows.Any(r => r.RowNumber == 1));
            Assert.NotNull(acme);
            Assert.Equal(new[] { 1, 3, 4 }, acme!.Rows.Select(r => r.RowNumber).OrderBy(n => n));
        }

        [Fact]
        public void Same_name_in_different_countries_does_not_merge()
        {
            var groups = new DuplicateScanner().Scan(SampleKeys());
            Assert.DoesNotContain(groups, g =>
                g.Rows.Any(r => r.RowNumber == 2) && g.Rows.Any(r => r.RowNumber == 5));
        }

        [Fact]
        public void Unique_rows_are_not_flagged()
        {
            var groups = new DuplicateScanner().Scan(SampleKeys());
            Assert.DoesNotContain(groups, g => g.Rows.Any(r => r.RowNumber == 6));
        }

        [Fact]
        public void Extras_keep_earliest_row_first()
        {
            var groups = new DuplicateScanner().Scan(SampleKeys());
            var acme = groups.Single(g => g.Rows.Any(r => r.RowNumber == 1));
            Assert.Equal(1, acme.Rows.First().RowNumber);
            Assert.Equal(new[] { 3, 4 }, acme.Extras.Select(r => r.RowNumber).OrderBy(n => n));
        }

        [Fact]
        public void Large_files_skip_fuzzy_names_but_keep_code_dedup()
        {
            var scanner = new DuplicateScanner { MaxFuzzyRows = 3 };
            var groups = scanner.Scan(SampleKeys());
            Assert.True(scanner.NameMatchingLimited);
            // Exact-code group (rows 1+3) must survive; the name-only join (row 4) is skipped.
            var acme = groups.Single(g => g.Rows.Any(r => r.RowNumber == 1));
            Assert.Equal(new[] { 1, 3 }, acme.Rows.Select(r => r.RowNumber).OrderBy(n => n));
        }

        [Fact]
        public void Empty_input_yields_no_groups()
        {
            Assert.Empty(new DuplicateScanner().Scan(new List<OrgKey>()));
        }
    }
}
