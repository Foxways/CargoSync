using System.Xml.Linq;
using OrganizationImportTool.Eadaptor;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class OrganizationXmlBuilderTests
    {
        private static readonly XNamespace Ns = "http://www.cargowise.com/Schemas/Native/2011/11";

        private static XDocument Build(Dictionary<string, string> values, string owner = "CENGLOBAL", bool codeMapping = false)
            => XDocument.Parse(new OrganizationXmlBuilder(TestData.Contract).Build(values, owner, codeMapping));

        private static Dictionary<string, string> BasicOrg() => new(StringComparer.OrdinalIgnoreCase)
        {
            ["orgHeader.code"] = "ACME001",
            ["orgHeader.fullName"] = "Acme Imports Pty Ltd",
            ["orgHeader.isConsignee"] = "Yes",
            ["orgAddressCollection[].address1"] = "1 Test Street",
            ["orgAddressCollection[].city"] = "Sydney",
            ["orgAddressCollection[].countryCode.code"] = "AU",
        };

        [Fact]
        public void Root_is_native_envelope_with_owner_code()
        {
            var doc = Build(BasicOrg());
            Assert.Equal(Ns + "Native", doc.Root!.Name);
            Assert.Equal("CENGLOBAL", doc.Descendants(Ns + "OwnerCode").Single().Value);
        }

        [Fact]
        public void OrgHeader_carries_merge_action_and_values()
        {
            var doc = Build(BasicOrg());
            var header = doc.Descendants(Ns + "OrgHeader").Single();
            Assert.Equal("MERGE", header.Attribute("Action")!.Value);
            Assert.Equal("ACME001", header.Element(Ns + "Code")!.Value);
            Assert.Equal("Acme Imports Pty Ltd", header.Element(Ns + "FullName")!.Value);
        }

        [Fact]
        public void Boolean_values_are_coerced()
        {
            var doc = Build(BasicOrg());
            Assert.Equal("true", doc.Descendants(Ns + "IsConsignee").Single().Value);
        }

        [Fact]
        public void Address_lands_in_address_collection()
        {
            var doc = Build(BasicOrg());
            var addr = doc.Descendants(Ns + "OrgAddress").Single();
            Assert.Equal("1 Test Street", addr.Element(Ns + "Address1")!.Value);
            Assert.Equal("Sydney", addr.Element(Ns + "City")!.Value);
        }

        [Fact]
        public void Indexed_paths_create_multiple_collection_items()
        {
            var values = BasicOrg();
            values["orgAddressCollection[1].address1"] = "PO Box 99";
            values["orgAddressCollection[1].orgAddressCapabilityCollection[].addressType"] = "PAD";
            var doc = Build(values);
            Assert.Equal(2, doc.Descendants(Ns + "OrgAddress").Count());
        }

        [Fact]
        public void Special_characters_are_xml_escaped()
        {
            var values = BasicOrg();
            values["orgHeader.fullName"] = "Smith & Sons <Imports> \"Pty\"";
            var doc = Build(values); // XDocument.Parse throws if escaping were broken
            Assert.Equal("Smith & Sons <Imports> \"Pty\"", doc.Descendants(Ns + "FullName").Single().Value);
        }

        [Fact]
        public void Empty_values_are_omitted()
        {
            var values = BasicOrg();
            values["orgHeader.vatNumber"] = "   ";
            var doc = Build(values);
            Assert.Empty(doc.Descendants(Ns + "VATNumber"));
        }
    }
}
