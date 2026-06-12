using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// Deterministic (offline, no-cost) auto-mapping: matches each source column to the
    /// best CargoWise field by comparing the normalized header against every field's
    /// label and aliases (exact match, token-set overlap, and Levenshtein similarity).
    ///
    /// This is the default "brain". A future AI advisor can refine the Low/Unmapped
    /// columns via the <see cref="IMappingAdvisor"/> seam without changing this class.
    /// </summary>
    public class MappingSuggester
    {
        private readonly FieldContract _contract;

        // Confidence tier thresholds on the 0..1 score.
        private const double HighThreshold = 0.90;
        private const double MediumThreshold = 0.65;
        private const double LowThreshold = 0.45;

        public MappingSuggester(FieldContract contract)
        {
            _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        }

        public MappingResult Suggest(SourceTable table)
        {
            var result = new MappingResult();
            var firstRow = table.Rows.FirstOrDefault();
            var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pre-compute the searchable index of mappable fields once.
            var index = _contract.MappableFields
                .Select(f => new FieldIndex(f, Normalize(f.Label), f.Aliases.Select(Normalize).ToList()))
                .ToList();

            foreach (var header in table.Headers)
            {
                string normHeader = Normalize(header);
                var ranked = RankFields(normHeader, index);

                var mapping = new ColumnMapping
                {
                    SourceHeader = header,
                    SampleValue = firstRow != null ? firstRow[header] : string.Empty,
                    Alternatives = ranked.Take(6).Select(r => r.Field.Path).ToList()
                };

                // Pick the best candidate above threshold whose target isn't already taken,
                // so two source columns never silently map to the same CargoWise field.
                var best = ranked.FirstOrDefault(r => r.Score >= LowThreshold && !usedTargets.Contains(r.Field.Path));
                if (best != null)
                {
                    mapping.TargetPath = best.Field.Path;
                    mapping.Score = best.Score;
                    mapping.Confidence = ToTier(best.Score);
                    mapping.Source = best.Score >= HighThreshold && best.WasExact
                        ? MappingSource.ExactAlias
                        : MappingSource.Fuzzy;
                    usedTargets.Add(best.Field.Path);
                }
                else
                {
                    mapping.Confidence = MappingConfidence.Unmapped;
                    mapping.Source = MappingSource.None;
                }

                // Explainability: record the top fields the matcher weighed and a plain-language reason.
                mapping.Candidates = ranked.Take(5)
                    .Select(r => new MappingCandidate
                    {
                        Path = r.Field.Path,
                        Label = r.Field.DisplayName,
                        Score = r.Score,
                        MatchedOn = r.MatchedTerm,
                        Chosen = best != null && r.Field.Path == best.Field.Path
                    })
                    .ToList();
                mapping.Rationale = BuildRationale(header, best);

                // Only strong (High) matches are auto-approved. Medium/Low matches are below the
                // certainty bar, so the operator must review and approve them before they're used.
                mapping.Approved = mapping.TargetPath == null || mapping.Confidence == MappingConfidence.High;

                result.Columns.Add(mapping);
            }

            RecomputeUnmappedRequired(result);
            return result;
        }

        /// <summary>Recalculate which required fields are still unmapped (call after any operator edit).</summary>
        public void RecomputeUnmappedRequired(MappingResult result)
        {
            var mapped = result.Columns
                .Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath))
                .Select(c => c.TargetPath!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // A required field can also be satisfied by a constant/default value.
            foreach (var k in result.Constants.Keys) mapped.Add(k);

            result.UnmappedRequired = _contract.RequiredFields
                .Where(f => !mapped.Contains(f.Path))
                .ToList();
        }

        private List<Ranked> RankFields(string normHeader, List<FieldIndex> index)
        {
            var scored = new List<Ranked>();
            foreach (var fi in index)
            {
                bool exact = false;
                double score = Similarity(normHeader, fi.NormLabel);
                string matchedTerm = fi.NormLabel; // the label/alias that produced the current best score

                // Aliases get a strong say - an exact alias hit is treated as near-certain.
                foreach (var alias in fi.NormAliases)
                {
                    if (alias.Length == 0) continue;
                    if (alias == normHeader) { score = 1.0; matchedTerm = alias; exact = true; break; }
                    double aScore = Similarity(normHeader, alias);
                    if (aScore > score) { score = aScore; matchedTerm = alias; }
                }
                if (fi.NormLabel == normHeader) { score = 1.0; matchedTerm = fi.NormLabel; exact = true; }

                if (score > 0)
                    scored.Add(new Ranked(fi.Field, score, exact, matchedTerm));
            }
            return scored.OrderByDescending(s => s.Score).ThenBy(s => s.Field.Path).ToList();
        }

        /// <summary>Plain-language explanation of why the chosen target won (or why nothing did).</summary>
        private static string BuildRationale(string header, Ranked? best)
        {
            if (best == null)
                return $"No CargoWise field scored above the match threshold for \"{header}\". " +
                       "Pick the right field from the dropdown, or untick to ignore this column.";

            string label = best.Field.DisplayName;
            string term = string.IsNullOrWhiteSpace(best.MatchedTerm) ? header : best.MatchedTerm;

            if (best.WasExact)
                return $"Exact match: your column \"{header}\" matches the known name \"{term}\" for {label} (100%).";

            string tier = best.Score >= MediumThreshold ? "Medium" : "Low";
            return $"Closest match: \"{header}\" ≈ \"{term}\" for {label} " +
                   $"({best.Score:P0} similar) → {tier} confidence. Review before confirming.";
        }

        private static MappingConfidence ToTier(double score) =>
            score >= HighThreshold ? MappingConfidence.High :
            score >= MediumThreshold ? MappingConfidence.Medium :
            MappingConfidence.Low;

        /// <summary>Blend of token-set overlap and Levenshtein ratio - robust to word order and typos.</summary>
        private static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0) return 0;
            if (a == b) return 1.0;

            double token = TokenSetRatio(a, b);
            double edit = LevenshteinRatio(a, b);
            double containment = (a.Contains(b) || b.Contains(a)) ? 0.85 : 0;
            return Math.Max(containment, (token * 0.6) + (edit * 0.4));
        }

        /// <summary>Jaccard-style overlap of word tokens (handles "company name" vs "name company").</summary>
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

        /// <summary>Lower-case, strip punctuation/underscores, collapse whitespace - so "INV_TERMS" ~ "inv terms".</summary>
        internal static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            char prev = '\0';
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                char ch = char.IsLetterOrDigit(c) ? c : ' ';
                if (ch == ' ' && prev == ' ') continue;
                sb.Append(ch);
                prev = ch;
            }
            return sb.ToString().Trim();
        }

        private sealed class FieldIndex
        {
            public ContractField Field { get; }
            public string NormLabel { get; }
            public List<string> NormAliases { get; }
            public FieldIndex(ContractField field, string normLabel, List<string> normAliases)
            { Field = field; NormLabel = normLabel; NormAliases = normAliases; }
        }

        private sealed class Ranked
        {
            public ContractField Field { get; }
            public double Score { get; }
            public bool WasExact { get; }
            public string MatchedTerm { get; }
            public Ranked(ContractField field, double score, bool wasExact, string matchedTerm = "")
            { Field = field; Score = score; WasExact = wasExact; MatchedTerm = matchedTerm; }
        }
    }

    /// <summary>
    /// Seam for an optional AI pass that refines low-confidence / unmapped columns.
    /// The AI subsystem implements this; the deterministic mapping does not depend on it.
    /// </summary>
    public interface IMappingAdvisor
    {
        /// <summary>Refine the deterministic result (improving Low/Unmapped columns). Mutates and returns it.</summary>
        Task<MappingResult> RefineAsync(SourceTable table, MappingResult deterministic, FieldContract contract, CancellationToken ct = default);
    }
}
