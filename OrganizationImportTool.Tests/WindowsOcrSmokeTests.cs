using System.Drawing;
using System.Drawing.Imaging;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    /// <summary>
    /// Real-engine smoke test of the offline (no-AI) extraction path: render a table to a
    /// bitmap, OCR it with the in-box Windows engine, reconstruct via geometry. Skips
    /// silently on machines without an OCR language pack (e.g. stripped-down CI images).
    /// </summary>
    public class WindowsOcrSmokeTests
    {
        [Fact]
        public void Rendered_table_image_is_read_back_without_ai()
        {
            if (!WindowsOcr.IsAvailable) return; // environment-dependent - skip, don't fail

            using var bmp = new Bitmap(900, 260);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using var font = new Font("Arial", 20);
                void Row(string c1, string c2, string c3, int y)
                {
                    g.DrawString(c1, font, Brushes.Black, 60, y);
                    g.DrawString(c2, font, Brushes.Black, 350, y);
                    g.DrawString(c3, font, Brushes.Black, 640, y);
                }
                // No digits/ambiguous glyphs (1 vs I, 0 vs O): this verifies the pipeline,
                // not the OCR engine's glyph accuracy - that's what the AI verify pass is for.
                Row("Code", "Name", "City", 40);
                Row("Alpha", "Acme", "Sydney", 100);
                Row("Bravo", "Beta", "Perth", 160);
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);

            var words = WindowsOcr.RecognizeImageFile(ms.ToArray());
            Assert.True(words.Count >= 9, $"OCR returned only {words.Count} words");

            var table = GeometryTableBuilder.Build(new[] { words }, "smoke.png", "smoke");
            Assert.NotNull(table);
            Assert.Equal(3, table!.ColumnCount);
            Assert.Equal(2, table.RowCount);
            Assert.Equal("Alpha", table.Rows[0][table.Headers[0]], ignoreCase: true);
            Assert.Equal("Perth", table.Rows[1][table.Headers[2]], ignoreCase: true);
        }
    }
}
