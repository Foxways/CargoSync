using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Offline OCR via the engine built into Windows 10/11 (Windows.Media.Ocr) - the no-AI
    /// fallback for scanned PDFs and images. No cloud, no API key, no extra binaries: the
    /// words come back with bounding boxes, which feed the same GeometryTableBuilder the
    /// PDF text-layer path uses.
    /// </summary>
    public static class WindowsOcr
    {
        public static bool IsAvailable
        {
            get
            {
                try { return OcrEngine.TryCreateFromUserProfileLanguages() != null; }
                catch { return false; }
            }
        }

        /// <summary>OCR an encoded image (PNG/JPEG/...) into positioned words.</summary>
        public static List<WordBox> RecognizeImageFile(byte[] imageBytes)
            => Run(() => RecognizeEncodedAsync(imageBytes));

        /// <summary>OCR raw BGRA pixels (a rendered PDF page) into positioned words.</summary>
        public static List<WordBox> RecognizeBgra(byte[] bgra, int width, int height)
            => Run(() => RecognizeBitmapAsync(BgraToBitmap(bgra, width, height)));

        // WinRT awaits must not be blocked on a UI thread; Task.Run keeps the sync ISourceReader
        // contract safe regardless of where Read() is called from.
        private static List<WordBox> Run(Func<Task<List<WordBox>>> work)
            => Task.Run(work).GetAwaiter().GetResult();

        private static async Task<List<WordBox>> RecognizeEncodedAsync(byte[] imageBytes)
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(imageBytes.AsBuffer());
            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            return await RecognizeBitmapAsync(bitmap);
        }

        private static async Task<List<WordBox>> RecognizeBitmapAsync(SoftwareBitmap bitmap)
        {
            using (bitmap)
            {
                var engine = OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new NotSupportedException(
                        "Windows OCR is not available (no OCR language pack installed). " +
                        "Install a language pack in Windows Settings, configure an AI provider, " +
                        "or supply the data as Excel/CSV.");

                var working = bitmap;
                if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
                    throw new InvalidDataException(
                        $"The image is too large for Windows OCR (max {OcrEngine.MaxImageDimension}px per side). " +
                        "Resize it, configure an AI provider, or supply the data as Excel/CSV.");

                var result = await engine.RecognizeAsync(working);
                var words = new List<WordBox>();
                foreach (var line in result.Lines)
                    foreach (var word in line.Words)
                        words.Add(new WordBox
                        {
                            Text = word.Text,
                            X = word.BoundingRect.X,
                            Y = word.BoundingRect.Y,
                            Width = word.BoundingRect.Width,
                            Height = word.BoundingRect.Height
                        });
                return words;
            }
        }

        private static SoftwareBitmap BgraToBitmap(byte[] bgra, int width, int height)
        {
            var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(bgra.AsBuffer());
            return bitmap;
        }
    }
}
