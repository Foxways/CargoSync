using System;
using System.Collections.Generic;
using System.Linq;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>A positioned word from a PDF text layer or an OCR result. Y grows downward.</summary>
    public sealed class WordBox
    {
        public string Text { get; init; } = string.Empty;
        public double X { get; init; }
        public double Y { get; init; }
        public double Width { get; init; }
        public double Height { get; init; }
        public double CenterY => Y + Height / 2;
        public double Right => X + Width;
    }

    /// <summary>
    /// Reconstructs a table from positioned words - the shared geometry brain behind the PDF
    /// text-layer reader and the offline OCR fallback. Words are clustered into lines by
    /// vertical overlap, column edges are inferred from the lines that agree on the modal
    /// word count, and every word is then assigned to its nearest column. The first line of
    /// the first page is the header; repeated headers on later pages are dropped.
    /// </summary>
    public static class GeometryTableBuilder
    {
        /// <summary>Build a table from per-page word lists. Returns null when no table emerges.</summary>
        public static SourceTable? Build(IReadOnlyList<IReadOnlyList<WordBox>> pages, string sourcePath, string sourceName)
        {
            var allLines = new List<List<WordBox>>();
            foreach (var page in pages)
                allLines.AddRange(ClusterLines(page));
            if (allLines.Count < 2) return null;

            // Modal cell count across multi-word lines drives the column model.
            var multi = allLines.Where(l => l.Count >= 2).ToList();
            if (multi.Count < 2) return null;
            int columns = multi.GroupBy(l => l.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
            if (columns < 2) return null;

            // Column left edges = median left edge of the k-th word across the agreeing lines.
            var anchors = multi.Where(l => l.Count == columns).ToList();
            var edges = new double[columns];
            for (int k = 0; k < columns; k++)
                edges[k] = Median(anchors.Select(l => l[k].X));

            // Assign each line's words to the nearest column; join multi-word cells left-to-right.
            var rows = new List<string[]>();
            foreach (var line in allLines)
            {
                if (line.Count == 1 && columns >= 3) continue; // titles / footers / page numbers
                var cells = new List<string>[columns];
                for (int k = 0; k < columns; k++) cells[k] = new List<string>();
                foreach (var w in line)
                    cells[NearestColumn(edges, w.X)].Add(w.Text);
                rows.Add(cells.Select(c => string.Join(" ", c)).ToArray());
            }
            if (rows.Count < 2) return null;

            var header = rows[0];
            var table = new SourceTable { SourcePath = sourcePath, SourceName = sourceName, HeaderRowIndex = 1 };
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int k = 0; k < columns; k++)
            {
                string raw = string.IsNullOrWhiteSpace(header[k]) ? $"Column{k + 1}" : header[k].Trim();
                string h = raw;
                if (seen.TryGetValue(raw, out int n)) { seen[raw] = n + 1; h = $"{raw} ({n + 1})"; }
                else seen[raw] = 1;
                table.Headers.Add(h);
            }

            int rowNum = 0;
            foreach (var cells in rows.Skip(1))
            {
                if (IsRepeatedHeader(cells, header)) continue;
                var row = new SourceRow { RowNumber = ++rowNum };
                for (int k = 0; k < columns; k++) row[table.Headers[k]] = cells[k].Trim();
                if (!row.IsEmpty) table.Rows.Add(row); else rowNum--;
            }
            return table.Rows.Count == 0 ? null : table;
        }

        /// <summary>How trustworthy the reconstruction looks (0..1) - drives the AI/OCR fallback decision.</summary>
        public static double Confidence(SourceTable table)
        {
            if (table.RowCount == 0 || table.ColumnCount < 2) return 0;
            int cells = 0, filled = 0;
            foreach (var row in table.Rows)
                foreach (var h in table.Headers)
                {
                    cells++;
                    if (!string.IsNullOrWhiteSpace(row[h])) filled++;
                }
            return cells == 0 ? 0 : (double)filled / cells;
        }

        /// <summary>Group words into reading lines: sort by vertical centre, split on centre gaps larger than half the word height.</summary>
        private static List<List<WordBox>> ClusterLines(IReadOnlyList<WordBox> words)
        {
            var lines = new List<List<WordBox>>();
            if (words.Count == 0) return lines;

            double tolerance = Math.Max(1.0, Median(words.Select(w => w.Height)) * 0.6);
            var sorted = words.OrderBy(w => w.CenterY).ThenBy(w => w.X).ToList();

            var current = new List<WordBox> { sorted[0] };
            double lineY = sorted[0].CenterY;
            for (int i = 1; i < sorted.Count; i++)
            {
                var w = sorted[i];
                if (Math.Abs(w.CenterY - lineY) <= tolerance)
                {
                    current.Add(w);
                    lineY = current.Average(x => x.CenterY);
                }
                else
                {
                    lines.Add(current.OrderBy(x => x.X).ToList());
                    current = new List<WordBox> { w };
                    lineY = w.CenterY;
                }
            }
            lines.Add(current.OrderBy(x => x.X).ToList());
            return lines;
        }

        private static int NearestColumn(double[] edges, double x)
        {
            int best = 0;
            double bestDist = Math.Abs(x - edges[0]);
            for (int k = 1; k < edges.Length; k++)
            {
                double d = Math.Abs(x - edges[k]);
                if (d < bestDist) { best = k; bestDist = d; }
            }
            return best;
        }

        private static bool IsRepeatedHeader(string[] cells, string[] header)
        {
            int matches = 0, nonEmpty = 0;
            for (int k = 0; k < cells.Length; k++)
            {
                if (string.IsNullOrWhiteSpace(cells[k])) continue;
                nonEmpty++;
                if (string.Equals(cells[k].Trim(), header[k].Trim(), StringComparison.OrdinalIgnoreCase)) matches++;
            }
            return nonEmpty > 0 && matches >= Math.Max(1, (int)(nonEmpty * 0.8));
        }

        private static double Median(IEnumerable<double> values)
        {
            var v = values.OrderBy(x => x).ToList();
            if (v.Count == 0) return 0;
            return v.Count % 2 == 1 ? v[v.Count / 2] : (v[v.Count / 2 - 1] + v[v.Count / 2]) / 2.0;
        }
    }
}
