using OrganizationImportTool.Transform;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class DataCleanerTests
    {
        private static async Task<List<CleaningChange>> AnalyzeAsync(params RowValues[] rows)
            => await new DataCleaner().AnalyzeAsync(rows.ToList(), TestData.Contract, router: null, aiEnabled: false);

        private static RowValues MessyRow() => new()
        {
            RowNumber = 1,
            Values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["orgHeader.code"] = "  ZZACE  001 ",
                ["orgHeader.fullName"] = "Acme   Imports   Pty Ltd ",
                ["orgAddressCollection[].countryCode.code"] = "Australia",
                ["orgHeader.isConsignee"] = "Yes",
                ["orgHeader.closestPort.code"] = "ausyd",
                ["orgAddressCollection[].orgAddressCapabilityCollection[].addressType"] = "ofc",
            }
        };

        [Fact]
        public async Task Deterministic_pass_fixes_all_known_messes()
        {
            var changes = await AnalyzeAsync(MessyRow());
            string Cleaned(string path) => changes.FirstOrDefault(c => c.Path == path)?.Cleaned ?? "(no change)";

            Assert.Equal("ZZACE 001", Cleaned("orgHeader.code"));
            Assert.Equal("Acme Imports Pty Ltd", Cleaned("orgHeader.fullName"));
            Assert.Equal("AU", Cleaned("orgAddressCollection[].countryCode.code"));
            Assert.Equal("true", Cleaned("orgHeader.isConsignee"));
            Assert.Equal("AUSYD", Cleaned("orgHeader.closestPort.code"));
            Assert.Equal("OFC", Cleaned("orgAddressCollection[].orgAddressCapabilityCollection[].addressType"));
        }

        [Fact]
        public async Task Clean_row_produces_no_changes()
        {
            var clean = new RowValues
            {
                RowNumber = 2,
                Values = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["orgHeader.code"] = "ACME001",
                    ["orgHeader.fullName"] = "Acme Imports Pty Ltd",
                    ["orgAddressCollection[].countryCode.code"] = "AU",
                }
            };
            Assert.Empty(await AnalyzeAsync(clean));
        }

        [Fact]
        public async Task AcceptedOverrides_only_includes_accepted_changes()
        {
            var changes = await AnalyzeAsync(MessyRow());
            Assert.NotEmpty(changes);
            foreach (var c in changes) c.Accept = false;
            changes[0].Accept = true;

            var overrides = DataCleaner.AcceptedOverrides(changes);
            Assert.Single(overrides);                       // one row
            Assert.Single(overrides[changes[0].RowNumber]); // one path
            Assert.Equal(changes[0].Cleaned, overrides[changes[0].RowNumber][changes[0].Path]);
        }

        [Fact]
        public async Task Indexed_collection_paths_are_normalized_for_field_lookup()
        {
            var row = new RowValues
            {
                RowNumber = 3,
                Values = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["orgAddressCollection[1].countryCode.code"] = "Australia",
                }
            };
            var changes = await AnalyzeAsync(row);
            Assert.Contains(changes, c => c.Path == "orgAddressCollection[1].countryCode.code" && c.Cleaned == "AU");
        }
    }
}
