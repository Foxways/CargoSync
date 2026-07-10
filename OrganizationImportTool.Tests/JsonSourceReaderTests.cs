using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class JsonSourceReaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cargosync-tests-" + Guid.NewGuid().ToString("N"));

        public JsonSourceReaderTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private SourceTable Read(string json, string name = "data.json")
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, json);
            return new JsonSourceReader().Read(path);
        }

        [Fact]
        public void Root_array_reads_rows_and_headers()
        {
            var t = Read("""[{"code":"ORG1","name":"Acme"},{"code":"ORG2","name":"Beta"}]""");
            Assert.Equal(2, t.RowCount);
            Assert.Contains("code", t.Headers);
            Assert.Contains("name", t.Headers);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
            Assert.Equal("Beta", t.Rows[1]["name"]);
        }

        [Fact]
        public void Wrapped_array_is_located_automatically()
        {
            var t = Read("""
                { "meta": { "exported": "2026-01-01", "by": "erp" },
                  "data": { "organizations": [
                      {"code":"ORG1","city":"Sydney"},
                      {"code":"ORG2","city":"Perth"},
                      {"code":"ORG3","city":"Brisbane"} ] } }
                """);
            Assert.Equal(3, t.RowCount);
            Assert.Equal("Sydney", t.Rows[0]["city"]);
            Assert.Contains("data.organizations", t.SourceName);
        }

        [Fact]
        public void Nested_objects_flatten_to_spaced_headers()
        {
            var t = Read("""[{"code":"ORG1","address":{"city":"Sydney","countryCode":"AU"}}]""");
            Assert.Equal("Sydney", t.Rows[0]["address city"]);
            Assert.Equal("AU", t.Rows[0]["address country Code"]);
        }

        [Fact]
        public void CamelCase_keys_become_spaced_headers()
        {
            var t = Read("""[{"fullName":"Acme Pty Ltd"}]""");
            Assert.Contains("full Name", t.Headers);
            Assert.Equal("Acme Pty Ltd", t.Rows[0]["full name"]); // row lookup is case-insensitive
        }

        [Fact]
        public void Scalar_arrays_join_into_one_cell()
        {
            var t = Read("""[{"code":"ORG1","tags":["importer","vip"]}]""");
            Assert.Equal("importer, vip", t.Rows[0]["tags"]);
        }

        [Fact]
        public void Object_arrays_become_indexed_columns()
        {
            var t = Read("""[{"code":"ORG1","contacts":[{"email":"a@x.com"},{"email":"b@x.com"}]}]""");
            Assert.Equal("a@x.com", t.Rows[0]["contacts 1 email"]);
            Assert.Equal("b@x.com", t.Rows[0]["contacts 2 email"]);
        }

        [Fact]
        public void Per_record_arrays_do_not_beat_the_record_array()
        {
            // 2 orgs, each with 4 contacts - the orgs array must win even though contacts repeat more.
            var t = Read("""
                { "organizations": [
                    {"code":"ORG1","contacts":[{"e":"1"},{"e":"2"},{"e":"3"},{"e":"4"}]},
                    {"code":"ORG2","contacts":[{"e":"5"},{"e":"6"},{"e":"7"},{"e":"8"}]} ] }
                """);
            Assert.Equal(2, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
        }

        [Fact]
        public void Json_lines_are_read_as_records()
        {
            var t = Read("{\"code\":\"ORG1\"}\n{\"code\":\"ORG2\"}\n{\"code\":\"ORG3\"}\n", "data.jsonl");
            Assert.Equal(3, t.RowCount);
            Assert.Equal("ORG2", t.Rows[1]["code"]);
        }

        [Fact]
        public void Single_object_is_one_record()
        {
            var t = Read("""{"code":"ORG1","name":"Acme"}""");
            Assert.Equal(1, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
        }

        [Fact]
        public void Null_values_become_empty_cells()
        {
            var t = Read("""[{"code":"ORG1","fax":null},{"code":"ORG2","fax":"123"}]""");
            Assert.Equal(string.Empty, t.Rows[0]["fax"]);
            Assert.Equal("123", t.Rows[1]["fax"]);
        }

        [Fact]
        public void Numbers_and_booleans_become_text()
        {
            var t = Read("""[{"code":"ORG1","creditLimit":50000.5,"isActive":true}]""");
            Assert.Equal("50000.5", t.Rows[0]["credit Limit"]);
            Assert.Equal("true", t.Rows[0]["is Active"]);
        }
    }
}
