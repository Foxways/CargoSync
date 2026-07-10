using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads JSON (.json) and JSON Lines (.jsonl) into a generic SourceTable.
    /// Auto-locates the record array anywhere in the document (root array, wrapped
    /// "data.organizations" style, etc.) by scoring every array of similar objects,
    /// then flattens each record: nested objects become word-spaced headers the
    /// mapping engine can fuzzy-match ("address.city" -> "address city"), nested
    /// object arrays become indexed columns ("contacts 1 email"), and scalar arrays
    /// are joined into one cell. No particular schema is required.
    /// </summary>
    public class JsonSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".json", ".jsonl" };

        /// <summary>Max items expanded per nested object array (contacts, addresses, ...).</summary>
        private const int MaxRepeats = 5;

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            string text = File.ReadAllText(filePath); // BOM-aware, defaults to UTF-8
            string fileName = Path.GetFileName(filePath);

            List<JsonElement> records;
            string recordPath;
            JsonDocument? doc = null;
            try
            {
                try
                {
                    doc = JsonDocument.Parse(text, new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip
                    });
                    (records, recordPath) = LocateRecords(doc.RootElement);
                }
                catch (JsonException) when (LooksLikeJsonLines(text))
                {
                    records = ParseJsonLines(text);
                    recordPath = "lines";
                }

                var acc = new TableAccumulator();
                foreach (var rec in records)
                {
                    var row = acc.NewRecord();
                    Flatten(rec, string.Empty, acc, row);
                }

                string name = string.IsNullOrEmpty(recordPath) ? fileName : $"{fileName} ({recordPath})";
                var table = acc.Build(filePath, name);
                return table;
            }
            finally
            {
                doc?.Dispose();
            }
        }

        // ---------------- record location ----------------

        private sealed class Candidate
        {
            public string Path = string.Empty;
            public List<JsonElement> Items = new();
            public double Consistency;
            public double Score => Items.Count * Consistency;
        }

        /// <summary>
        /// Find the array of objects that most plausibly holds the records: largest count x
        /// key-consistency, ignoring arrays nested inside another candidate's items (a per-record
        /// "contacts" array must never beat the organizations array that contains it).
        /// </summary>
        private static (List<JsonElement>, string) LocateRecords(JsonElement root)
        {
            var candidates = new List<Candidate>();
            Collect(root, string.Empty, candidates);

            // An array nested inside another candidate's items is per-record data (contacts inside
            // an organization), not the record set - unless it dwarfs its container, which is the
            // "wrapper array around the real records" shape ({"results":[{"items":[...500...]}]}).
            var best = candidates
                .Where(c => !candidates.Any(o => o != c &&
                                                 c.Path.StartsWith(o.Path + "[]", StringComparison.Ordinal) &&
                                                 (o.Items.Count >= 2 || c.Score < o.Score * 5)))
                .OrderByDescending(c => c.Score)
                .FirstOrDefault();

            if (best != null)
                return (best.Items, best.Path);

            // No array of objects anywhere: a single top-level object is one record.
            if (root.ValueKind == JsonValueKind.Object)
                return (new List<JsonElement> { root }, string.Empty);

            return (new List<JsonElement>(), string.Empty);
        }

        private static void Collect(JsonElement el, string path, List<Candidate> found)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                        Collect(prop.Value, path.Length == 0 ? prop.Name : path + "." + prop.Name, found);
                    break;

                case JsonValueKind.Array:
                    var objs = new List<JsonElement>();
                    int total = 0;
                    foreach (var item in el.EnumerateArray())
                    {
                        total++;
                        if (item.ValueKind == JsonValueKind.Object) objs.Add(item);
                    }
                    // record-like: at least one object, and objects dominate the array
                    if (objs.Count >= 1 && objs.Count * 2 > total)
                        found.Add(new Candidate { Path = path, Items = objs, Consistency = KeyConsistency(objs) });

                    foreach (var item in el.EnumerateArray())
                        Collect(item, path + "[]", found);
                    break;
            }
        }

        /// <summary>1.0 when every object has the same keys; approaches 0 as shapes diverge.</summary>
        private static double KeyConsistency(List<JsonElement> objs)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sum = 0;
            foreach (var o in objs)
            {
                foreach (var p in o.EnumerateObject()) { union.Add(p.Name); sum++; }
            }
            if (union.Count == 0) return 0.1; // empty objects - still records, just weak ones
            return (double)sum / (objs.Count * union.Count);
        }

        // ---------------- JSON Lines ----------------

        private static bool LooksLikeJsonLines(string text)
        {
            foreach (var line in EnumerateLines(text))
            {
                string t = line.Trim();
                if (t.Length == 0) continue;
                return t.StartsWith("{", StringComparison.Ordinal);
            }
            return false;
        }

        private static List<JsonElement> ParseJsonLines(string text)
        {
            var records = new List<JsonElement>();
            int lineNo = 0;
            foreach (var line in EnumerateLines(text))
            {
                lineNo++;
                string t = line.Trim();
                if (t.Length == 0) continue;
                try
                {
                    var doc = JsonDocument.Parse(t);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                        records.Add(doc.RootElement.Clone());
                    doc.Dispose();
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException($"Line {lineNo} is not valid JSON: {ex.Message}");
                }
            }
            return records;
        }

        private static IEnumerable<string> EnumerateLines(string text)
        {
            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
                yield return line;
        }

        // ---------------- flattening ----------------

        private static void Flatten(JsonElement el, string prefix, TableAccumulator acc, Dictionary<string, string> row)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in el.EnumerateObject())
                        Flatten(prop.Value, HeaderText.Join(prefix, HeaderText.Humanize(prop.Name)), acc, row);
                    break;

                case JsonValueKind.Array:
                    var scalars = new List<string>();
                    int objIndex = 0;
                    foreach (var item in el.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object || item.ValueKind == JsonValueKind.Array)
                        {
                            objIndex++;
                            if (objIndex <= MaxRepeats)
                                Flatten(item, HeaderText.Join(prefix, objIndex.ToString()), acc, row);
                        }
                        else
                        {
                            string s = ScalarToString(item);
                            if (s.Length > 0) scalars.Add(s);
                        }
                    }
                    if (scalars.Count > 0)
                        acc.Set(row, prefix, string.Join(", ", scalars));
                    break;

                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    break; // empty cell - omit; SourceRow returns "" for missing headers

                default:
                    acc.Set(row, prefix, ScalarToString(el));
                    break;
            }
        }

        private static string ScalarToString(JsonElement el) => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }
}
