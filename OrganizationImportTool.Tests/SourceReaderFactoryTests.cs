using ClosedXML.Excel;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class SourceReaderFactoryTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cargosync-tests-" + Guid.NewGuid().ToString("N"));
        private readonly SourceReaderFactory _factory = new();

        public SourceReaderFactoryTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        private string Write(string name, string content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private string WriteBytes(string name, byte[] content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllBytes(path, content);
            return path;
        }

        private string WriteWorkbook(string name)
        {
            string path = Path.Combine(_dir, name);
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Orgs");
            ws.Cell(1, 1).Value = "Code";
            ws.Cell(1, 2).Value = "Name";
            ws.Cell(2, 1).Value = "ORG1";
            ws.Cell(2, 2).Value = "Acme";
            wb.SaveAs(path);
            return path;
        }

        [Fact]
        public void Json_extension_routes_to_json_reader()
        {
            var t = _factory.Read(Write("orgs.json", """[{"code":"ORG1"},{"code":"ORG2"}]"""));
            Assert.Equal(2, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["code"]);
        }

        [Fact]
        public void Xml_extension_routes_to_xml_reader()
        {
            var t = _factory.Read(Write("orgs.xml", "<orgs><org><code>ORG1</code></org><org><code>ORG2</code></org></orgs>"));
            Assert.Equal(2, t.RowCount);
        }

        [Fact]
        public void Json_content_in_txt_is_auto_identified()
        {
            var t = _factory.Read(Write("export.txt", """[{"code":"ORG1","name":"Acme"}]"""));
            Assert.Equal(1, t.RowCount);
            Assert.Equal("Acme", t.Rows[0]["name"]);
        }

        [Fact]
        public void Xml_content_in_txt_is_auto_identified()
        {
            var t = _factory.Read(Write("export.txt", "<orgs><org><code>ORG1</code></org><org><code>ORG2</code></org></orgs>"));
            Assert.Equal(2, t.RowCount);
        }

        [Fact]
        public void Workbook_renamed_to_csv_is_read_as_excel()
        {
            string xlsx = WriteWorkbook("real.xlsx");
            string disguised = Path.Combine(_dir, "orgs.csv");
            File.Copy(xlsx, disguised);

            var t = _factory.Read(disguised);
            Assert.Equal(1, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["Code"]);
        }

        [Fact]
        public void Ooxml_workbook_named_xls_still_opens()
        {
            string xlsx = WriteWorkbook("real.xlsx");
            string disguised = Path.Combine(_dir, "orgs.xls");
            File.Copy(xlsx, disguised);

            var t = _factory.Read(disguised);
            Assert.Equal("Acme", t.Rows[0]["Name"]);
        }

        [Fact]
        public void Csv_starting_with_brace_stays_csv()
        {
            var t = _factory.Read(Write("orgs.csv", "{code},name\nORG1,Acme\n"));
            Assert.Equal(1, t.RowCount);
            Assert.Equal("ORG1", t.Rows[0]["{code}"]);
        }

        [Fact]
        public void Unknown_extension_with_json_content_routes_by_content()
        {
            var t = _factory.Read(Write("orgs.dat", """[{"code":"ORG1"}]"""));
            Assert.Equal(1, t.RowCount);
        }

        [Fact]
        public void Legacy_xls_gives_friendly_error()
        {
            var ole2 = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0 };
            var ex = Assert.Throws<NotSupportedException>(() => _factory.Read(WriteBytes("old.xls", ole2)));
            Assert.Contains(".xlsx", ex.Message);
        }

        [Fact]
        public void Unreadable_pdf_routes_to_pdf_reader_and_fails_with_guidance()
        {
            var ex = Assert.ThrowsAny<Exception>(() => _factory.Read(Write("orgs.pdf", "%PDF-1.7 ...")));
            Assert.Contains("pdf", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Undecodable_image_routes_to_image_reader_and_fails()
        {
            // A bare PNG signature with no image data: must reach the image reader and fail
            // there with a message, never crash a text parser.
            var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0 };
            Assert.ThrowsAny<Exception>(() => _factory.Read(WriteBytes("scan.png", png)));
        }

        [Fact]
        public void Dialog_filter_includes_pdf_and_images()
        {
            Assert.Contains("*.pdf", _factory.FileDialogFilter);
            Assert.Contains("*.png", _factory.FileDialogFilter);
        }

        [Fact]
        public void Plain_csv_still_reads_normally()
        {
            var t = _factory.Read(Write("orgs.csv", "Code,Name\nORG1,Acme\nORG2,Beta\n"));
            Assert.Equal(2, t.RowCount);
            Assert.Equal("Beta", t.Rows[1]["Name"]);
        }

        [Fact]
        public void Dialog_filter_includes_new_formats()
        {
            Assert.Contains("*.json", _factory.FileDialogFilter);
            Assert.Contains("*.xml", _factory.FileDialogFilter);
            Assert.Contains("*.xlsx", _factory.FileDialogFilter);
            Assert.Contains("*.csv", _factory.FileDialogFilter);
        }
    }
}
