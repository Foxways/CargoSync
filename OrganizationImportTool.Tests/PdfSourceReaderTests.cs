using System.Text;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class PdfSourceReaderTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cargosync-tests-" + Guid.NewGuid().ToString("N"));

        public PdfSourceReaderTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [Fact]
        public void Digital_pdf_table_is_extracted_from_the_text_layer()
        {
            string path = Path.Combine(_dir, "orgs.pdf");
            File.WriteAllBytes(path, BuildPdf(new[]
            {
                ("Code", "Name", "City"),
                ("ORG1", "Acme", "Sydney"),
                ("ORG2", "Beta", "Perth"),
                ("ORG3", "Gamma", "Brisbane"),
            }));

            var t = new PdfSourceReader().Read(path);

            Assert.Equal(3, t.RowCount);
            Assert.Equal(new[] { "Code", "Name", "City" }, t.Headers);
            Assert.Equal("Acme", t.Rows[0]["Name"]);
            Assert.Equal("Brisbane", t.Rows[2]["City"]);
            Assert.Contains("text layer", t.SourceName);
        }

        [Fact]
        public void Corrupt_pdf_gives_friendly_error()
        {
            string path = Path.Combine(_dir, "broken.pdf");
            File.WriteAllText(path, "%PDF-1.7 this is not really a pdf");
            var ex = Assert.ThrowsAny<Exception>(() => new PdfSourceReader().Read(path));
            Assert.Contains("pdf", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Assemble a minimal valid single-page PDF with one Helvetica text row per tuple, three fixed columns.</summary>
        private static byte[] BuildPdf(IEnumerable<(string c1, string c2, string c3)> rows)
        {
            var content = new StringBuilder("BT /F1 12 Tf\n");
            int y = 700;
            foreach (var (c1, c2, c3) in rows)
            {
                content.Append($"1 0 0 1 50 {y} Tm ({c1}) Tj\n");
                content.Append($"1 0 0 1 200 {y} Tm ({c2}) Tj\n");
                content.Append($"1 0 0 1 380 {y} Tm ({c3}) Tj\n");
                y -= 20;
            }
            content.Append("ET");

            var objects = new[]
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"
            };

            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new long[objects.Length];
            for (int i = 0; i < objects.Length; i++)
            {
                offsets[i] = pdf.Length;
                pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            long xrefAt = pdf.Length;
            pdf.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (long off in offsets)
                pdf.Append($"{off:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xrefAt}\n%%EOF");

            return Encoding.ASCII.GetBytes(pdf.ToString());
        }
    }
}
