using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class XmlSourceReaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cargosync-tests-" + Guid.NewGuid().ToString("N"));

        public XmlSourceReaderTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private SourceTable Read(string xml)
        {
            string path = Path.Combine(_dir, "data.xml");
            File.WriteAllText(path, xml);
            return new XmlSourceReader().Read(path);
        }

        [Fact]
        public void Repeated_elements_become_records()
        {
            var t = Read("""
                <organizations>
                  <organization><code>ORG1</code><name>Acme</name></organization>
                  <organization><code>ORG2</code><name>Beta</name></organization>
                </organizations>
                """);
            Assert.Equal(2, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
            Assert.Equal("Beta", t.Rows[1]["name"]);
            Assert.Contains("organization", t.SourceName);
        }

        [Fact]
        public void Attributes_are_captured_as_columns()
        {
            var t = Read("""
                <orgs>
                  <org code="ORG1" isActive="true"><name>Acme</name></org>
                  <org code="ORG2" isActive="false"><name>Beta</name></org>
                </orgs>
                """);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
            Assert.Equal("false", t.Rows[1]["is Active"]);
        }

        [Fact]
        public void Nested_elements_flatten_to_spaced_headers()
        {
            var t = Read("""
                <orgs>
                  <org><code>ORG1</code><address><city>Sydney</city><countryCode>AU</countryCode></address></org>
                  <org><code>ORG2</code><address><city>Perth</city><countryCode>AU</countryCode></address></org>
                </orgs>
                """);
            Assert.Equal("Sydney", t.Rows[0]["address city"]);
            Assert.Equal("AU", t.Rows[1]["address country Code"]);
        }

        [Fact]
        public void Repeated_children_get_indexed_columns()
        {
            var t = Read("""
                <orgs>
                  <org><code>ORG1</code><contact><email>a@x.com</email></contact><contact><email>b@x.com</email></contact></org>
                  <org><code>ORG2</code><contact><email>c@x.com</email></contact><contact><email>d@x.com</email></contact></org>
                </orgs>
                """);
            Assert.Equal("a@x.com", t.Rows[0]["contact 1 email"]);
            Assert.Equal("d@x.com", t.Rows[1]["contact 2 email"]);
        }

        [Fact]
        public void Cargowise_native_export_uses_orgheader_records()
        {
            var t = Read("""
                <Native xmlns="http://www.cargowise.com/Schemas/Native/2011/11" version="2.0">
                  <Body>
                    <Organization version="2.0">
                      <OrgHeader>
                        <Code>ORG1</Code><FullName>Acme Pty Ltd</FullName>
                        <ClosestPort TableName="RefUNLOCO"><Code>AUSYD</Code></ClosestPort>
                      </OrgHeader>
                    </Organization>
                    <Organization version="2.0">
                      <OrgHeader>
                        <Code>ORG2</Code><FullName>Beta Ltd</FullName>
                        <ClosestPort TableName="RefUNLOCO"><Code>AUMEL</Code></ClosestPort>
                      </OrgHeader>
                    </Organization>
                  </Body>
                </Native>
                """);
            Assert.Equal(2, t.RowCount);
            Assert.Contains("OrgHeader", t.SourceName);
            Assert.Equal("Acme Pty Ltd", t.Rows[0]["Full Name"]);
            Assert.Equal("AUMEL", t.Rows[1]["Closest Port Code"]);
        }

        [Fact]
        public void Collection_wrappers_are_flattened_through()
        {
            var t = Read("""
                <orgs>
                  <org><code>ORG1</code>
                    <addressCollection>
                      <address><city>Sydney</city></address>
                      <address><city>Perth</city></address>
                    </addressCollection>
                  </org>
                  <org><code>ORG2</code>
                    <addressCollection>
                      <address><city>Auckland</city></address>
                      <address><city>Wellington</city></address>
                    </addressCollection>
                  </org>
                </orgs>
                """);
            Assert.Equal(2, t.RowCount);
            Assert.Equal("Sydney", t.Rows[0]["address 1 city"]);
            Assert.Equal("Wellington", t.Rows[1]["address 2 city"]);
        }

        [Fact]
        public void Document_without_repeats_is_a_single_record()
        {
            var t = Read("<org><code>ORG1</code><name>Acme</name></org>");
            Assert.Equal(1, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
        }
    }
}
