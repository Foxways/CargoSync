using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Docnet.Core;
using Docnet.Core.Models;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Logging;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads PDFs into a generic SourceTable using the best mechanism the file allows:
    /// 1. Text layer (digital PDFs) - words with coordinates via PDFium, reconstructed into a
    ///    table deterministically. Exact, offline, free.
    /// 2. AI extraction - when the text layer is missing/poor and an AI provider is configured,
    ///    the PDF goes through the verified AI table extractor.
    /// 3. Windows OCR - offline fallback: pages are rendered via PDFium and read by the OCR
    ///    engine built into Windows, words feeding the same geometry reconstruction.
    /// </summary>
    public class PdfSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

        private const int MaxPages = 50;
        private const double GoodConfidence = 0.6;
        /// <summary>Render scale for OCR (PDF points -> pixels; 2.0 ≈ 144 DPI).</summary>
        private const double OcrRenderScale = 2.0;
        private const long MaxAiBytes = 20 * 1024 * 1024;

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            byte[] bytes = File.ReadAllBytes(filePath);
            string fileName = Path.GetFileName(filePath);

            List<List<WordBox>> textPages;
            int pageCount;
            try
            {
                (textPages, pageCount) = ExtractTextWords(bytes);
            }
            catch (Exception ex) when (ex is not NotSupportedException)
            {
                throw new InvalidDataException(
                    $"'{fileName}' could not be read as a PDF ({ex.Message}). The file may be corrupt or password-protected.");
            }

            if (pageCount > MaxPages)
                throw new NotSupportedException(
                    $"'{fileName}' has {pageCount} pages (limit {MaxPages}). Split the file, or export the data as Excel/CSV.");

            // 1) Digital PDF: deterministic reconstruction from the text layer.
            int wordCount = textPages.Sum(p => p.Count);
            if (wordCount >= 10)
            {
                var table = GeometryTableBuilder.Build(textPages, filePath, $"{fileName} (text layer)");
                if (table != null && GeometryTableBuilder.Confidence(table) >= GoodConfidence)
                    return table;

                // Poor table shape: prefer AI (it understands layout); keep the deterministic
                // result as the no-AI answer rather than failing.
                var ai = TryAiExtract(bytes, filePath, fileName);
                if (ai != null) return ai;
                if (table != null)
                {
                    AppLog.Warn($"PDF '{fileName}': low-confidence text-layer reconstruction used (no AI available).");
                    return table;
                }
            }

            // 2) Scanned PDF: AI first, then offline Windows OCR.
            var aiScan = TryAiExtract(bytes, filePath, fileName);
            if (aiScan != null) return aiScan;

            var ocr = OcrPages(bytes, filePath, fileName);
            if (ocr != null) return ocr;

            throw new InvalidDataException(
                $"No table could be extracted from '{fileName}'. If it is a poor-quality scan, " +
                "try a clearer copy, configure an AI provider, or supply the data as Excel/CSV.");
        }

        // ---------------- text layer (PDFium characters -> words) ----------------

        private static (List<List<WordBox>>, int) ExtractTextWords(byte[] bytes)
        {
            var pages = new List<List<WordBox>>();
            using var doc = DocLib.Instance.GetDocReader(bytes, new PageDimensions(1.0));
            int count = doc.GetPageCount();
            for (int i = 0; i < count && i < MaxPages; i++)
            {
                using var page = doc.GetPageReader(i);
                pages.Add(CharactersToWords(page.GetCharacters()));
            }
            return (pages, count);
        }

        /// <summary>Assemble PDFium per-character boxes into words, splitting on whitespace or a gap wider than ~half a char.</summary>
        private static List<WordBox> CharactersToWords(IEnumerable<Docnet.Core.Models.Character> chars)
        {
            var words = new List<WordBox>();
            var current = new List<Docnet.Core.Models.Character>();

            void Flush()
            {
                if (current.Count == 0) return;
                double left = current.Min(c => c.Box.Left);
                double top = current.Min(c => c.Box.Top);
                double right = current.Max(c => c.Box.Right);
                double bottom = current.Max(c => c.Box.Bottom);
                string text = string.Concat(current.Select(c => c.Char)).Trim();
                if (text.Length > 0)
                    words.Add(new WordBox { Text = text, X = left, Y = top, Width = right - left, Height = bottom - top });
                current.Clear();
            }

            foreach (var ch in chars)
            {
                if (char.IsWhiteSpace(ch.Char)) { Flush(); continue; }
                if (current.Count > 0)
                {
                    // Real spaces arrive as whitespace characters (handled above), so this only
                    // needs to catch repositioning jumps (new column/line) - size the threshold
                    // on char height, not glyph width, or narrow letters like "i" split words.
                    var prev = current[^1];
                    double charHeight = Math.Max(3.0, prev.Box.Bottom - prev.Box.Top);
                    bool gap = ch.Box.Left - prev.Box.Right > charHeight;
                    bool newLine = Math.Abs(ch.Box.Top - prev.Box.Top) > charHeight;
                    if (gap || newLine) Flush();
                }
                current.Add(ch);
            }
            Flush();
            return words;
        }

        // ---------------- AI fallback ----------------

        private static SourceTable? TryAiExtract(byte[] bytes, string filePath, string fileName)
        {
            var router = AiRouter.Active;
            if (router == null || !router.IsConfigured) return null;
            if (bytes.LongLength > MaxAiBytes)
            {
                AppLog.Warn($"PDF '{fileName}' exceeds the AI extraction size limit; skipping AI.");
                return null;
            }

            try
            {
                var attachment = new AiAttachment
                {
                    Kind = AiAttachmentKind.Pdf,
                    MediaType = "application/pdf",
                    Base64Data = Convert.ToBase64String(bytes)
                };
                return new AiTableExtractor(router)
                    .ExtractAsync(attachment, filePath, fileName).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"AI extraction failed for '{fileName}'; falling back", ex);
                return null;
            }
        }

        // ---------------- offline OCR fallback ----------------

        private static SourceTable? OcrPages(byte[] bytes, string filePath, string fileName)
        {
            if (!WindowsOcr.IsAvailable) return null;

            try
            {
                var pages = new List<List<WordBox>>();
                using var doc = DocLib.Instance.GetDocReader(bytes, new PageDimensions(OcrRenderScale));
                int count = Math.Min(doc.GetPageCount(), MaxPages);
                for (int i = 0; i < count; i++)
                {
                    using var page = doc.GetPageReader(i);
                    pages.Add(WindowsOcr.RecognizeBgra(page.GetImage(), page.GetPageWidth(), page.GetPageHeight()));
                }
                return GeometryTableBuilder.Build(pages, filePath, $"{fileName} (OCR)");
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Windows OCR failed for '{fileName}'", ex);
                return null;
            }
        }
    }
}
