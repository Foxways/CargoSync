using OrganizationImportTool.Mapping;
using OrganizationImportTool.Validation;

namespace OrganizationImportTool.Tests
{
    public class FieldValueValidatorTests
    {
        private static readonly FieldContract Contract = FieldContract.Parse(@"
        {
          ""enums"": { ""addressType"": [""OFC (Office)"", ""PAD (Postal)""] },
          ""fields"": [
            { ""path"": ""orgHeader.code"", ""label"": ""Code"", ""type"": ""string"", ""maxLength"": 10 },
            { ""path"": ""orgHeader.isConsignee"", ""label"": ""Is Consignee"", ""type"": ""bool"" },
            { ""path"": ""orgHeader.count"", ""label"": ""Count"", ""type"": ""int"" },
            { ""path"": ""orgAddressCollection[].orgAddressCapabilityCollection[].addressType"", ""label"": ""Address Type"", ""enum"": ""addressType"" }
          ]
        }");

        private static string? Check(string path, string value) => FieldValueValidator.Check(Contract, path, value);

        [Fact]
        public void Empty_or_unknown_path_is_fine()
        {
            Assert.Null(Check("orgHeader.code", ""));
            Assert.Null(Check("no.such.field", "anything"));
        }

        [Fact]
        public void Enum_membership_is_enforced()
        {
            Assert.Null(Check("orgAddressCollection[].orgAddressCapabilityCollection[].addressType", "OFC"));
            Assert.Null(Check("orgAddressCollection[].orgAddressCapabilityCollection[].addressType", "ofc")); // case-insensitive
            Assert.Contains("not a known code", Check("orgAddressCollection[].orgAddressCapabilityCollection[].addressType", "ZZZ")!);
        }

        [Fact]
        public void Enum_resolves_through_indexed_collection_path()
        {
            // a concrete indexed path normalises to the contract's []-path
            Assert.Null(Check("orgAddressCollection[0].orgAddressCapabilityCollection[1].addressType", "PAD"));
            Assert.Contains("not a known code", Check("orgAddressCollection[0].orgAddressCapabilityCollection[1].addressType", "BAD")!);
        }

        [Fact]
        public void Max_length_is_flagged()
        {
            Assert.Null(Check("orgHeader.code", "1234567890"));        // exactly 10
            Assert.Contains("longer than 10", Check("orgHeader.code", "12345678901")!);
        }

        [Fact]
        public void Bool_type_is_checked()
        {
            Assert.Null(Check("orgHeader.isConsignee", "true"));
            Assert.Null(Check("orgHeader.isConsignee", "Yes"));
            Assert.Null(Check("orgHeader.isConsignee", "0"));
            Assert.Contains("true/false", Check("orgHeader.isConsignee", "maybe")!);
        }

        [Fact]
        public void Int_type_is_checked()
        {
            Assert.Null(Check("orgHeader.count", "42"));
            Assert.Contains("whole number", Check("orgHeader.count", "4.5")!);
            Assert.Contains("whole number", Check("orgHeader.count", "abc")!);
        }
    }
}
