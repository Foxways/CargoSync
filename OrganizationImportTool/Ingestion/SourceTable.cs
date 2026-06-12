using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Structure-agnostic, in-memory representation of whatever file a client provides.
    /// No required headers - this just captures the columns and rows exactly as they appear,
    /// so the mapping engine (not the reader) decides how they relate to CargoWise fields.
    /// </summary>
    public class SourceTable
    {
        /// <summary>Original file path the data was read from.</summary>
        public string SourcePath { get; set; } = string.Empty;

        /// <summary>Sheet name (Excel) or file name (CSV) the data came from.</summary>
        public string SourceName { get; set; } = string.Empty;

        /// <summary>Column headers in their original order, exactly as found in the file.</summary>
        public List<string> Headers { get; set; } = new List<string>();

        /// <summary>
        /// Data rows. Each row maps header -> cell value (string). Header order is preserved
        /// via <see cref="Headers"/>; lookups are case-insensitive.
        /// </summary>
        public List<SourceRow> Rows { get; set; } = new List<SourceRow>();

        /// <summary>1-based index of the row the headers were detected on (for diagnostics).</summary>
        public int HeaderRowIndex { get; set; } = 1;

        public int RowCount => Rows.Count;
        public int ColumnCount => Headers.Count;

        /// <summary>A small sample of rows - handy for AI prompts and mapping previews.</summary>
        public IEnumerable<SourceRow> Sample(int count = 5) => Rows.Take(Math.Max(0, count));
    }

    /// <summary>A single data row: case-insensitive header -> raw string value.</summary>
    public class SourceRow
    {
        private readonly Dictionary<string, string> _values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>1-based source row number in the original file (header excluded).</summary>
        public int RowNumber { get; set; }

        public string this[string header]
        {
            get => _values.TryGetValue(header, out var v) ? v : string.Empty;
            set => _values[header] = value ?? string.Empty;
        }

        public bool Has(string header) => _values.ContainsKey(header);

        public IReadOnlyDictionary<string, string> Values => _values;

        /// <summary>True when every cell in the row is blank (used to stop / skip empty rows).</summary>
        public bool IsEmpty => _values.Values.All(string.IsNullOrWhiteSpace);
    }
}
