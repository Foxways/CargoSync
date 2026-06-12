using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Resolves the right <see cref="ISourceReader"/> for a file and loads it.
    /// Single entry point the UI calls - "give me this file as a SourceTable",
    /// regardless of format. New formats are added by registering another reader.
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
            var reader = _readers.FirstOrDefault(r => r.SupportedExtensions.Contains(ext))
                ?? throw new NotSupportedException(
                    $"No reader registered for '{ext}'. Supported: " +
                    string.Join(", ", _readers.SelectMany(r => r.SupportedExtensions).Distinct()));
            return reader.Read(filePath);
        }
    }
}
