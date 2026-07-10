using System.Text;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class FileSnifferTests
    {
        private static FileKind Sniff(byte[] bytes) => FileSniffer.SniffBytes(bytes, bytes.Length);
        private static FileKind SniffText(string text) => Sniff(Encoding.UTF8.GetBytes(text));

        [Fact]
        public void Zip_signature_is_zip_based()
            => Assert.Equal(FileKind.ZipBased, Sniff(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 }));

        [Fact]
        public void Ole2_signature_is_legacy_excel()
            => Assert.Equal(FileKind.LegacyOle2, Sniff(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00 }));

        [Fact]
        public void Pdf_marker_is_pdf()
            => Assert.Equal(FileKind.Pdf, SniffText("%PDF-1.7 something"));

        [Fact]
        public void Png_signature_is_image()
            => Assert.Equal(FileKind.Image, Sniff(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }));

        [Fact]
        public void Jpeg_signature_is_image()
            => Assert.Equal(FileKind.Image, Sniff(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }));

        [Theory]
        [InlineData("{ \"a\": 1 }")]
        [InlineData("  [ { \"a\": 1 } ]")]
        [InlineData("\t{\"rows\":[]}")]
        public void Json_text_is_json(string text)
            => Assert.Equal(FileKind.Json, SniffText(text));

        [Theory]
        [InlineData("<?xml version=\"1.0\"?><root/>")]
        [InlineData("  <organizations><org/></organizations>")]
        public void Xml_text_is_xml(string text)
            => Assert.Equal(FileKind.Xml, SniffText(text));

        [Fact]
        public void Csv_text_is_delimited()
            => Assert.Equal(FileKind.DelimitedText, SniffText("Code,Name,Country\nORG1,Acme,AU\n"));

        [Fact]
        public void Utf8_bom_json_is_json()
        {
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            var body = Encoding.UTF8.GetBytes("{\"a\":1}");
            var bytes = new byte[bom.Length + body.Length];
            bom.CopyTo(bytes, 0);
            body.CopyTo(bytes, bom.Length);
            Assert.Equal(FileKind.Json, Sniff(bytes));
        }

        [Fact]
        public void Text_starting_with_BM_is_not_mistaken_for_bitmap()
            => Assert.Equal(FileKind.DelimitedText, SniffText("BM,code,name\n1,ORG1,Acme\n"));

        [Fact]
        public void Binary_with_nulls_is_unknown()
            => Assert.Equal(FileKind.Unknown, Sniff(new byte[] { 0x01, 0x00, 0x02, 0x00, 0x7F, 0x00 }));

        [Fact]
        public void Empty_is_unknown()
            => Assert.Equal(FileKind.Unknown, Sniff(System.Array.Empty<byte>()));
    }
}
