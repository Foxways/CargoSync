using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads .xlsx / .xlsm / .xls (where supported by ClosedXML) into a generic SourceTable.
    /// Auto-detects the header row (the first row that looks like labels rather than data)
    /// and reads every column - it does NOT require any specific headers.
    /// </summary>
    public class ExcelSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".xlsx", ".xlsm", ".xls" };

        /// <summary>How many leading rows to scan when looking for the header row.</summary>
        private const int HeaderScanDepth = 15;

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            // FileShare.ReadWrite so we can open workbooks that are open in Excel / syncing in OneDrive.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(fs);
            var worksheet = workbook.Worksheets.FirstOrDefault(w => w.RangeUsed() != null)
                            ?? workbook.Worksheet(1);

            var used = worksheet.RangeUsed();
            var table = new SourceTable
            {
                SourcePath = filePath,
                SourceName = worksheet.Name
            };
            if (used == null)
                return table; // empty sheet

            int firstCol = used.FirstColumn().ColumnNumber();
            int lastCol = used.LastColumn().ColumnNumber();
            int firstRow = used.FirstRow().RowNumber();
            int lastRow = used.LastRow().RowNumber();

            int headerRowNum = DetectHeaderRow(worksheet, firstRow, lastRow, firstCol, lastCol);
            table.HeaderRowIndex = headerRowNum;

            // Build headers, de-duplicating blanks / collisions so every column is addressable.
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var colHeaders = new List<(int col, string header)>();
            for (int col = firstCol; col <= lastCol; col++)
            {
                string raw = worksheet.Cell(headerRowNum, col).GetString().Trim();
                if (string.IsNullOrEmpty(raw))
                    raw = $"Column{col}";

                string header = raw;
                if (seen.TryGetValue(raw, out int n))
                {
                    seen[raw] = n + 1;
                    header = $"{raw} ({n + 1})";
                }
                else
                {
                    seen[raw] = 1;
                }
                colHeaders.Add((col, header));
                table.Headers.Add(header);
            }

            // Read data rows below the header row; skip fully-empty rows.
            int outRowNum = 0;
            for (int r = headerRowNum + 1; r <= lastRow; r++)
            {
                var row = new SourceRow();
                bool any = false;
                foreach (var (col, header) in colHeaders)
                {
                    string value = worksheet.Cell(r, col).GetString().Trim();
                    row[header] = value;
                    if (!string.IsNullOrEmpty(value)) any = true;
                }
                if (!any) continue;
                row.RowNumber = ++outRowNum;
                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>
        /// Picks the most likely header row: the row (within the first few) with the most
        /// non-empty, mostly-textual cells. Falls back to the first used row.
        /// </summary>
        private static int DetectHeaderRow(IXLWorksheet ws, int firstRow, int lastRow, int firstCol, int lastCol)
        {
            int bestRow = firstRow;
            double bestScore = -1;
            int scanTo = Math.Min(lastRow, firstRow + HeaderScanDepth);

            for (int r = firstRow; r <= scanTo; r++)
            {
                int nonEmpty = 0, textual = 0;
                for (int c = firstCol; c <= lastCol; c++)
                {
                    string v = ws.Cell(r, c).GetString().Trim();
                    if (string.IsNullOrEmpty(v)) continue;
                    nonEmpty++;
                    // Header cells are usually short labels, not pure numbers.
                    if (!double.TryParse(v, out _)) textual++;
                }
                if (nonEmpty == 0) continue;

                // Reward filled, textual rows; this favours a real header over sparse title rows.
                double score = nonEmpty + textual;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRow = r;
                }
            }
            return bestRow;
        }
    }
}
