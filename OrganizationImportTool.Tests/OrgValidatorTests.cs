using OrganizationImportTool.Validation;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class OrgValidatorTests
    {
        private static OrgValidator Validator => new(TestData.Contract);

        private static Dictionary<string, string> ValidRow() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["orgHeader.code"] = "ACME001",
            ["orgHeader.fullName"] = "Acme Imports Pty Ltd",
            ["orgAddressCollection[].address1"] = "1 Test Street",
            ["orgAddressCollection[].city"] = "Sydney",
        };

        [Fact]
        public void Complete_row_has_no_errors()
        {
            var report = Validator.Validate(ValidRow());
            Assert.False(report.HasErrors, report.ErrorText);
        }

        [Fact]
        public void Missing_required_code_is_an_error()
        {
            var row = ValidRow();
            row["orgHeader.code"] = "";
            var report = Validator.Validate(row);
            Assert.True(report.HasErrors);
            Assert.Contains(report.Errors, i => i.Message.Contains("required"));
        }

        [Fact]
        public void Missing_required_fullname_is_an_error()
        {
            var row = ValidRow();
            row.Remove("orgHeader.fullName");
            Assert.True(Validator.Validate(row).HasErrors);
        }

        [Fact]
        public void Row_without_address_gets_warning_not_error()
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["orgHeader.code"] = "ACME001",
                ["orgHeader.fullName"] = "Acme Imports",
            };
            var report = Validator.Validate(row);
            Assert.False(report.HasErrors);
            Assert.Contains(report.Warnings, i => i.Label == "Address");
        }

        [Fact]
        public void Unknown_enum_code_is_warning_not_error()
        {
            var row = ValidRow();
            row["orgAddressCollection[].orgAddressCapabilityCollection[].addressType"] = "ZZZ";
            var report = Validator.Validate(row);
            Assert.False(report.HasErrors);
            Assert.Contains(report.Warnings, i => i.Message.Contains("not a known code"));
        }

        [Fact]
        public void Indexed_collection_paths_resolve_to_contract_fields()
        {
            // Validator must treat orgAddressCollection[1].city like orgAddressCollection[].city.
            var row = ValidRow();
            row["orgAddressCollection[1].orgAddressCapabilityCollection[].addressType"] = "ZZZ";
            var report = Validator.Validate(row);
            Assert.Contains(report.Warnings, i => i.Message.Contains("not a known code"));
        }
    }
}
