using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Resolves the right <see cref="ISourceReader"/> for a file and loads it.
    /// Single entry point the UI calls - "give me this file as a SourceTable",
    /// regardless of format. Dispatch is content-aware: the file is sniffed
    /// (<see cref="FileSniffer"/>) so mislabeled files (JSON saved as .txt, a
    /// workbook renamed .csv) still route to the right reader, and unreadable
    /// formats fail with a friendly message instead of a parser crash.
    /// New formats are added by registering another reader.
    /// </summary>
    public class SourceReaderFactory
    {
        private readonly List<ISourceReader> _readers;

        public SourceReaderFactory(IEnumerable<ISourceReader>? readers = null)
        {
            _readers = (readers ?? DefaultReaders()).ToList();
        }

        private static IEnumerable<ISourceReader> DefaultReaders() => new ISourceReader[]
        {
            new ExcelSourceReader(),
            new CsvSourceReader(),
            new JsonSourceReader(),
            new XmlSourceReader(),
            new PdfSourceReader(),
            new ImageSourceReader(),
        };

        /// <summary>File-dialog filter string covering every supported format.</summary>
        public string FileDialogFilter
        {
            get
            {
                var exts = _readers.SelectMany(r => r.SupportedExtensions)
                                   .Select(e => "*" + e)
                                   .Distinct();
                return $"Supported files|{string.Join(";", exts)}|All files|*.*";
            }
        }

        public bool CanRead(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return _readers.Any(r => r.SupportedExtensions.Contains(ext));
        }

        public SourceTable Read(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            FileKind kind = FileSniffer.Sniff(filePath);

            // The one format we recognise but cannot read - fail with guidance, not a parser crash.
            if (kind == FileKind.LegacyOle2)
                throw new NotSupportedException(
                    "This is a legacy Excel 97-2003 (.xls) file. Open it in Excel, save it as .xlsx, and import that instead.");

            var byExt = FindByExtension(ext);
            var bySniff = kind switch
            {
                FileKind.ZipBased => FindByExtension(".xlsx"),
                FileKind.Json => FindByExtension(".json"),
                FileKind.Xml => FindByExtension(".xml"),
                FileKind.DelimitedText => FindByExtension(".csv"),
                FileKind.Pdf => FindByExtension(".pdf"),
                FileKind.Image => FindByExtension(".png"),
                _ => null
            };

            var reader = Choose(byExt, bySniff, kind, ext)
                ?? throw new NotSupportedException(
                    $"No reader registered for '{ext}'. Supported: " +
                    string.Join(", ", _readers.SelectMany(r => r.SupportedExtensions).Distinct()));
            return reader.Read(filePath);
        }

        private ISourceReader? FindByExtension(string ext)
            => _readers.FirstOrDefault(r => r.SupportedExtensions.Contains(ext));

        /// <summary>
        /// Pick between the extension's reader and the content's reader. The extension wins by
        /// default (a .csv whose first cell starts with '{' is still a CSV); content wins only
        /// when the extension reader would definitely fail (binary workbook with a text extension,
        /// text content with an Excel extension) or the extension is the catch-all ".txt".
        /// </summary>
        private static ISourceReader? Choose(ISourceReader? byExt, ISourceReader? bySniff, FileKind kind, string ext)
        {
            if (byExt == null) return bySniff;
            if (bySniff == null || ReferenceEquals(byExt, bySniff)) return byExt;

            bool extIsExcel = byExt.SupportedExtensions.Contains(".xlsx");
            if (extIsExcel) return bySniff;                 // text/JSON/XML content named .xls(x)
            bool binaryContent = kind == FileKind.ZipBased || kind == FileKind.Pdf || kind == FileKind.Image;
            if (binaryContent) return bySniff;              // real workbook/PDF/image named .csv/.txt/.json
            if (ext == ".txt" && (kind == FileKind.Json || kind == FileKind.Xml)) return bySniff;

            return byExt;
        }
    }
}
