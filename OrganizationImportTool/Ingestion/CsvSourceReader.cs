using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads delimited text (.csv / .tsv / .txt) into a generic SourceTable.
    /// Auto-detects encoding (BOM) and delimiter (comma / semicolon / tab / pipe),
    /// and parses RFC-4180 quoted fields (embedded delimiters, quotes, and newlines).
    /// </summary>
    public class CsvSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".csv", ".tsv", ".txt" };

        private static readonly char[] CandidateDelimiters = { ',', ';', '\t', '|' };

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            string text = ReadAllTextDetectEncoding(filePath);
            char delimiter = DetectDelimiter(text);

            var records = ParseDelimited(text, delimiter);
            var table = new SourceTable
            {
                SourcePath = filePath,
                SourceName = Path.GetFileName(filePath),
                HeaderRowIndex = 1
            };
            if (records.Count == 0)
                return table;

            // First non-empty record is the header.
            int headerIdx = records.FindIndex(r => r.Any(c => !string.IsNullOrWhiteSpace(c)));
            if (headerIdx < 0)
                return table;

            var headerFields = records[headerIdx];
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw0 in headerFields)
            {
                string raw = (raw0 ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(raw)) raw = $"Column{table.Headers.Count + 1}";
                string header = raw;
                if (seen.TryGetValue(raw, out int n)) { seen[raw] = n + 1; header = $"{raw} ({n + 1})"; }
                else seen[raw] = 1;
                table.Headers.Add(header);
            }

            int outRowNum = 0;
            for (int i = headerIdx + 1; i < records.Count; i++)
            {
                var fields = records[i];
                if (fields.All(string.IsNullOrWhiteSpace)) continue;

                var row = new SourceRow();
                for (int c = 0; c < table.Headers.Count; c++)
                {
                    string value = c < fields.Count ? (fields[c] ?? string.Empty).Trim() : string.Empty;
                    row[table.Headers[c]] = value;
                }
                row.RowNumber = ++outRowNum;
                table.Rows.Add(row);
            }

            return table;
        }

        private static string ReadAllTextDetectEncoding(string path)
        {
            // FileShare.ReadWrite so we can read files that are open in Excel / syncing in OneDrive.
            // detectEncodingFromByteOrderMarks honours UTF-8/16/32 BOMs, defaulting to UTF-8.
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        /// <summary>Choose the delimiter that yields the most consistent column count across the first rows.</summary>
        private static char DetectDelimiter(string text)
        {
            var sampleLines = text.Replace("\r\n", "\n").Replace('\r', '\n')
                                  .Split('\n')
                                  .Where(l => !string.IsNullOrWhiteSpace(l))
                                  .Take(10)
                                  .ToList();
            if (sampleLines.Count == 0) return ',';

            char best = ',';
            double bestScore = -1;
            foreach (char d in CandidateDelimiters)
            {
                // Count delimiters outside quoted sections only — embedded commas inside "..." would
                // otherwise skew detection toward comma even for tab- or pipe-delimited files.
                var counts = sampleLines.Select(l => StripQuotedSections(l).Count(ch => ch == d)).ToList();
                double avg = counts.Average();
                if (avg <= 0) continue;
                // Prefer delimiters that appear often and consistently (low variance).
                double variance = counts.Select(c => (c - avg) * (c - avg)).Average();
                double score = avg - variance;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            return best;
        }

        /// <summary>
        /// Replaces all content inside RFC-4180 quoted fields with spaces so delimiter
        /// counts are not skewed by embedded delimiters (e.g. commas inside "New York, NY").
        /// </summary>
        private static string StripQuotedSections(string line)
        {
            var sb = new StringBuilder(line.Length);
            bool inQuotes = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (inQuotes)
                {
                    if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                        i++; // skip RFC-4180 escaped quote ("")
                    else if (ch == '"')
                        inQuotes = false;
                    sb.Append(' '); // mask everything inside quotes
                }
                else
                {
                    if (ch == '"') inQuotes = true;
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }

        /// <summary>RFC-4180 parser: handles quoted fields with embedded delimiters, quotes ("") and newlines.</summary>
        private static List<List<string>> ParseDelimited(string text, char delimiter)
        {
            var records = new List<List<string>>();
            var field = new StringBuilder();
            var record = new List<string>();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];

                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(ch);
                    continue;
                }

                if (ch == '"') { inQuotes = true; }
                else if (ch == delimiter) { record.Add(field.ToString()); field.Clear(); }
                else if (ch == '\r')
                {
                    // swallow; handle the line break on the following \n (or treat lone \r as newline)
                    if (i + 1 < text.Length && text[i + 1] == '\n') { /* wait for \n */ }
                    else { record.Add(field.ToString()); field.Clear(); records.Add(record); record = new List<string>(); }
                }
                else if (ch == '\n') { record.Add(field.ToString()); field.Clear(); records.Add(record); record = new List<string>(); }
                else field.Append(ch);
            }

            // flush trailing field/record
            if (field.Length > 0 || record.Count > 0)
            {
                record.Add(field.ToString());
                records.Add(record);
            }
            return records;
        }
    }
}
