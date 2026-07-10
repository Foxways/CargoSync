using System;
using System.Collections.Generic;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Collects flattened records (header -> value) from a tree-shaped file and builds the
    /// final <see cref="SourceTable"/>: header order is first-seen across all records, and
    /// in-record header collisions are de-duplicated the same way the Excel reader does
    /// ("Code", "Code (2)", ...) so every value stays addressable.
    /// </summary>
    internal sealed class TableAccumulator
    {
        private readonly List<string> _headers = new();
        private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Dictionary<string, string>> _records = new();

        public Dictionary<string, string> NewRecord()
        {
            var rec = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _records.Add(rec);
            return rec;
        }

        public void Set(Dictionary<string, string> record, string header, string value)
        {
            if (string.IsNullOrWhiteSpace(header)) header = "Value";

            string final = header;
            int n = 2;
            while (record.ContainsKey(final)) final = $"{header} ({n++})";

            record[final] = value ?? string.Empty;
            if (_seen.Add(final)) _headers.Add(final);
        }

        public SourceTable Build(string sourcePath, string sourceName)
        {
            var table = new SourceTable
            {
                SourcePath = sourcePath,
                SourceName = sourceName,
                HeaderRowIndex = 1
            };
            table.Headers.AddRange(_headers);

            int rowNum = 0;
            foreach (var rec in _records)
            {
                var row = new SourceRow { RowNumber = ++rowNum };
                foreach (var h in _headers)
                    row[h] = rec.TryGetValue(h, out var v) ? v : string.Empty;
                if (!row.IsEmpty)
                    table.Rows.Add(row);
                else
                    rowNum--; // keep numbering contiguous when a record flattens to nothing
            }
            return table;
        }
    }
}
