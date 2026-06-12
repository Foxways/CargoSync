using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OrganizationImportTool.Dedup
{
    /// <summary>The dedup-relevant fields pulled from one mapped row.</summary>
    public class OrgKey
    {
        public int RowNumber { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;

        /// <summary>Best human label for the row (name, falling back to code).</summary>
        public string Display => !string.IsNullOrWhiteSpace(Name) ? Name : (!string.IsNullOrWhiteSpace(Code) ? Code : $"(row {RowNumber})");
    }

    /// <summary>A set of rows in the same file that look like the same organization.</summary>
    public class DuplicateGroup
    {
        public string Reason { get; set; } = string.Empty;
        public double Confidence { get; set; }          // 0..1
        public List<OrgKey> Rows { get; set; } = new List<OrgKey>();

        public string RowList => string.Join(", ", Rows.Select(r => r.RowNumber));
        public string Names => string.Join("   ·   ", Rows.Select(r => r.Display));

        /// <summary>Rows that should be skipped if the operator de-duplicates (all but the first).</summary>
        public IEnumerable<OrgKey> Extras => Rows.Skip(1);
    }

    /// <summary>
    /// Finds likely-duplicate organizations WITHIN a single file before import, so the operator
    /// doesn't unknowingly create/merge the same org twice. Two signals:
    ///   1. identical organization code (definite duplicate), and
    ///   2. a near-identical company name in the same country, after stripping legal suffixes
    ///      (Ltd / LLC / Pty / FZE / GmbH ...), which is a likely duplicate the operator should review.
    /// Deterministic and offline - no AI, no CargoWise round-trip.
    /// </summary>
    public class DuplicateScanner
    {
        private const double NameThreshold = 0.88;

        /// <summary>Above this row count, fuzzy NAME matching is skipped (it is O(n²)); exact-code dedup still runs.</summary>
        public int MaxFuzzyRows { get; set; } = 10000;

        /// <summary>Set after Scan: true if the file was too large for fuzzy name matching (code-only dedup was used).</summary>
        public bool NameMatchingLimited { get; private set; }

        // Pure legal-entity suffixes only - deliberately NOT industry words (freight, logistics,
        // shipping) so "Acme Freight" and "Acme Shipping" are never wrongly merged.
        private static readonly HashSet<string> LegalSuffixes = new(StringComparer.OrdinalIgnoreCase)
        {
            "ltd","limited","llc","inc","incorporated","pty","fze","fzc","fzco","gmbh","co","corp",
            "corporation","plc","bv","srl","ag","sa","sas","nv","oy","oyj","ab","as","aps","sl","spa",
            "llp","lp","pllc","pvt","pte","bhd","sdn","kg","kk","ulc"
        };

        public List<DuplicateGroup> Scan(IReadOnlyList<OrgKey> keys)
        {
            int n = keys.Count;
            var norm = keys.Select(k => NormName(k.Name)).ToArray();
            var code = keys.Select(k => NormCode(k.Code)).ToArray();
            var uf = new UnionFind(n);

            // Edges: same code, OR near-identical name in the same country. Union-find then yields
            // connected components, so A~B and B~C cluster together and a row can join a code group
            // by name (and vice-versa).
            var codeLinked = new bool[n];
            var nameScore = new double[n]; // best name-edge score touching each row (for confidence)

            // 1) Exact-code edges via a dictionary — O(n), not O(n²).
            var firstByCode = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < n; i++)
            {
                if (code[i].Length == 0) continue;
                if (firstByCode.TryGetValue(code[i], out var first)) { uf.Union(first, i); codeLinked[first] = codeLinked[i] = true; }
                else firstByCode[code[i]] = i;
            }

            // 2) Fuzzy name edges — O(n²), so skip for very large files (exact-code dedup above still runs).
            NameMatchingLimited = n > MaxFuzzyRows;
            if (!NameMatchingLimited)
            {
                // Pre-tokenize once; a cheap token/containment pre-filter avoids the costly Levenshtein
                // for the ~99% of pairs that mathematically cannot reach the 0.88 threshold.
                var tokens = new HashSet<string>[n];
                for (int i = 0; i < n; i++)
                    tokens[i] = norm[i].Length == 0 ? null
                        : new HashSet<string>(norm[i].Split(' ', StringSplitOptions.RemoveEmptyEntries));

                for (int i = 0; i < n; i++)
                {
                    if (norm[i].Length == 0) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (norm[j].Length == 0) continue;
                        if (!SameCountry(keys[i].Country, keys[j].Country)) continue;
                        if (uf.Find(i) == uf.Find(j)) continue;          // already linked (e.g. by code)
                        if (!Candidate(norm[i], norm[j], tokens[i], tokens[j])) continue;
                        double s = Similarity(norm[i], norm[j]);
                        if (s >= NameThreshold)
                        {
                            uf.Union(i, j);
                            nameScore[i] = Math.Max(nameScore[i], s);
                            nameScore[j] = Math.Max(nameScore[j], s);
                        }
                    }
                }
            }

            // Collect components of size > 1, preserving each row's file order; keep earliest row first.
            var comps = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = uf.Find(i);
                if (!comps.TryGetValue(r, out var list)) comps[r] = list = new List<int>();
                list.Add(i);
            }

            var groups = new List<DuplicateGroup>();
            foreach (var members in comps.Values.Where(m => m.Count > 1))
            {
                var ordered = members.OrderBy(i => keys[i].RowNumber).ToList();
                bool byCode = ordered.Any(i => codeLinked[i]);
                var g = new DuplicateGroup();
                foreach (int i in ordered) g.Rows.Add(keys[i]);

                if (byCode)
                {
                    g.Confidence = 1.0;
                    g.Reason = "Identical organization code";
                }
                else
                {
                    double score = ordered.Where(i => nameScore[i] > 0).Select(i => nameScore[i]).DefaultIfEmpty(NameThreshold).Min();
                    g.Confidence = score;
                    string country = ordered.Select(i => keys[i].Country).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                    string where = string.IsNullOrWhiteSpace(country) ? "" : $", same country {country.ToUpperInvariant()}";
                    g.Reason = $"Similar company name ({score:P0}{where})";
                }
                groups.Add(g);
            }

            return groups.OrderByDescending(g => g.Confidence).ThenBy(g => g.Rows[0].RowNumber).ToList();
        }

        /// <summary>Minimal union-find (disjoint set) for clustering duplicate rows.</summary>
        private sealed class UnionFind
        {
            private readonly int[] _p;
            public UnionFind(int n) { _p = Enumerable.Range(0, n).ToArray(); }
            public int Find(int x) { while (_p[x] != x) { _p[x] = _p[_p[x]]; x = _p[x]; } return x; }
            public void Union(int a, int b) { int ra = Find(a), rb = Find(b); if (ra != rb) _p[ra] = rb; }
        }

        /// <summary>
        /// Cheap gate before the expensive Levenshtein: a pair can only reach the 0.88 blended
        /// threshold via containment (one name inside the other) OR a token overlap ≥ ~0.7
        /// (since the score is 0.5·token + 0.5·edit and edit ≤ 1). Everything else is skipped.
        /// </summary>
        private static bool Candidate(string a, string b, HashSet<string>? ta, HashSet<string>? tb)
        {
            // containment is itself a strong match basis (Similarity returns 0.9)
            if (a.Length >= b.Length ? a.Contains(b) : b.Contains(a)) return true;
            if (ta == null || tb == null || ta.Count == 0 || tb.Count == 0) return false;
            var small = ta.Count <= tb.Count ? ta : tb;
            var big = ta.Count <= tb.Count ? tb : ta;
            int inter = 0;
            foreach (var t in small) if (big.Contains(t)) inter++;
            int union = ta.Count + tb.Count - inter;
            return union > 0 && (double)inter / union >= 0.70;
        }

        private static bool SameCountry(string a, string b)
        {
            a = a?.Trim() ?? ""; b = b?.Trim() ?? "";
            if (a.Length == 0 || b.Length == 0) return true; // unknown country shouldn't block a name match
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormCode(string s) =>
            string.IsNullOrWhiteSpace(s) ? string.Empty
            : new string(s.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// <summary>Lower-case, strip punctuation, drop trailing/standalone legal-entity suffix tokens.</summary>
        private static string NormName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                sb.Append(char.IsLetterOrDigit(c) ? c : ' ');
            var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                          .Where(t => !LegalSuffixes.Contains(t))
                          .ToList();
            return string.Join(" ", tokens);
        }

        /// <summary>Blend of token-set overlap and Levenshtein ratio, with a containment boost.</summary>
        private static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1.0;
            double token = TokenSetRatio(a, b);
            double edit = LevenshteinRatio(a, b);
            double containment = (a.Contains(b) || b.Contains(a)) ? 0.9 : 0;
            return Math.Max(containment, (token * 0.5) + (edit * 0.5));
        }

        private static double TokenSetRatio(string a, string b)
        {
            var sa = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var sb = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            if (sa.Count == 0 || sb.Count == 0) return 0;
            int inter = sa.Count(t => sb.Contains(t));
            int union = sa.Union(sb).Count();
            return union == 0 ? 0 : (double)inter / union;
        }

        private static double LevenshteinRatio(string a, string b)
        {
            int dist = Levenshtein(a, b);
            int max = Math.Max(a.Length, b.Length);
            return max == 0 ? 1.0 : 1.0 - ((double)dist / max);
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            return d[a.Length, b.Length];
        }
    }
}
