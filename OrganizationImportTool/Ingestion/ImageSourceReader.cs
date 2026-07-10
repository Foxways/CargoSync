using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Logging;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads an image of a table (photo/screenshot/scan) into a generic SourceTable:
    /// AI vision extraction when a provider is configured (verified, layout-aware),
    /// otherwise the OCR engine built into Windows + geometric table reconstruction.
    /// TIFF/BMP are re-encoded to PNG so every AI provider can accept them.
    /// </summary>
    public class ImageSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tif", ".tiff" };

        private const long MaxAiBytes = 15 * 1024 * 1024;

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            byte[] bytes = File.ReadAllBytes(filePath);
            string fileName = Path.GetFileName(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            // 1) AI vision extraction (preferred - understands table layout, runs the verify pass).
            var router = AiRouter.Active;
            if (router != null && router.IsConfigured && bytes.LongLength <= MaxAiBytes)
            {
                try
                {
                    var (data, mediaType) = NormalizeForAi(bytes, ext);
                    var table = new AiTableExtractor(router).ExtractAsync(new AiAttachment
                    {
                        Kind = AiAttachmentKind.Image,
                        MediaType = mediaType,
                        Base64Data = Convert.ToBase64String(data)
                    }, filePath, fileName).GetAwaiter().GetResult();
                    return table;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"AI extraction failed for '{fileName}'; trying Windows OCR", ex);
                }
            }

            // 2) Offline fallback: Windows' built-in OCR + geometry reconstruction.
            if (!WindowsOcr.IsAvailable)
                throw new NotSupportedException(
                    "Reading images needs either an AI provider (Settings -> AI) or Windows OCR " +
                    "(install a language pack in Windows Settings). Alternatively supply the data as Excel/CSV.");

            List<WordBox> words;
            try
            {
                words = WindowsOcr.RecognizeImageFile(bytes);
            }
            catch (Exception ex) when (ex is not NotSupportedException && ex is not InvalidDataException)
            {
                throw new InvalidDataException($"'{fileName}' could not be decoded as an image ({ex.Message}).");
            }

            var ocrTable = GeometryTableBuilder.Build(new[] { words }, filePath, $"{fileName} (OCR)");
            return ocrTable ?? throw new InvalidDataException(
                $"No table could be recognised in '{fileName}'. If it is a poor-quality photo or scan, " +
                "try a clearer image, configure an AI provider, or supply the data as Excel/CSV.");
        }

        /// <summary>AI providers accept png/jpeg/webp/gif; re-encode TIFF/BMP to PNG.</summary>
        private static (byte[] data, string mediaType) NormalizeForAi(byte[] bytes, string ext) => ext switch
        {
            ".png" => (bytes, "image/png"),
            ".jpg" or ".jpeg" => (bytes, "image/jpeg"),
            ".webp" => (bytes, "image/webp"),
            ".gif" => (bytes, "image/gif"),
            _ => (ReencodeAsPng(bytes), "image/png")
        };

        private static byte[] ReencodeAsPng(byte[] bytes)
        {
            using var src = new MemoryStream(bytes);
            using var bmp = new Bitmap(src);
            using var dst = new MemoryStream();
            bmp.Save(dst, ImageFormat.Png);
            return dst.ToArray();
        }
    }
}
