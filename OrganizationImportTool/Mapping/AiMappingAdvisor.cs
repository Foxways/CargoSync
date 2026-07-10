using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// Optional AI pass over the deterministic mapping: asks the configured AI (through the
    /// fallback <see cref="AiRouter"/>) to resolve only the columns the fuzzy matcher was unsure
    /// about (Low / Unmapped). Deterministic High/Medium matches are left untouched, so AI is
    /// used sparingly (and only when enabled).
    /// </summary>
    public class AiMappingAdvisor : IMappingAdvisor
    {
        private readonly AiRouter _router;
        private readonly bool _lowConfidenceOnly;

        public AiMappingAdvisor(AiRouter router, bool lowConfidenceOnly = true)
        {
            _router = router;
            _lowConfidenceOnly = lowConfidenceOnly;
        }

        /// <summary>Number of columns the last refine pass changed (for UI/logging).</summary>
        public int LastChangedCount { get; private set; }

        public async Task<MappingResult> RefineAsync(SourceTable table, MappingResult result, FieldContract contract, CancellationToken ct = default)
        {
            LastChangedCount = 0;
            if (!_router.IsConfigured) return result;

            var targets = result.Columns
                .Where(c => !_lowConfidenceOnly
                            || c.Confidence == MappingConfidence.Low
                            || c.Confidence == MappingConfidence.Unmapped)
                .ToList();
            if (targets.Count == 0) return result;

            var sample = table.Rows.FirstOrDefault();
            string prompt = BuildPrompt(targets, sample, contract);

            var resp = await _router.CompleteAsync(new AiRequest
            {
                System = "You map messy spreadsheet column headers to a fixed list of CargoWise organization field paths. " +
                         "Reply ONLY with compact JSON: {\"mappings\":[{\"header\":\"<source header>\",\"path\":\"<exact field path or empty>\"}]}. " +
                         "Use empty path if no field fits. Never invent a path that is not in the provided list.",
                Prompt = prompt,
                Operation = "mapping-suggest",
                MaxTokensOverride = 1200
            }, ct).ConfigureAwait(false);

            if (!resp.Success || string.IsNullOrWhiteSpace(resp.Text)) return result;

            var valid = new HashSet<string>(contract.MappableFields.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

            // Track already-assigned paths so AI cannot map two source columns to the same target.
            var usedTargets = new HashSet<string>(
                result.Columns.Where(c => !string.IsNullOrEmpty(c.TargetPath)).Select(c => c.TargetPath!),
                StringComparer.OrdinalIgnoreCase);

            foreach (var (header, path) in ParseMappings(resp.Text))
            {
                if (string.IsNullOrEmpty(path) || !valid.Contains(path)) continue;
                if (usedTargets.Contains(path)) continue;  // another column already owns this target
                var col = result.Columns.FirstOrDefault(c =>
                    string.Equals(c.SourceHeader, header, StringComparison.OrdinalIgnoreCase));
                if (col == null) continue;
                if (string.Equals(col.TargetPath, path, StringComparison.OrdinalIgnoreCase)) continue;

                col.TargetPath = path;
                col.Source = MappingSource.Ai;
                col.Confidence = MappingConfidence.Medium; // AI-proposed - operator still confirms
                col.Score = 0.7;
                col.Approved = false;                      // AI matches require explicit human approval
                if (!col.Alternatives.Contains(path)) col.Alternatives.Insert(0, path);
                usedTargets.Add(path);

                // Explainability: the deterministic matcher was unsure, so AI resolved it.
                string label = contract.MappableFields.FirstOrDefault(f =>
                    string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? path;
                col.Rationale = $"AI ({_router.Current?.ProviderName ?? "model"}) read your column \"{col.SourceHeader}\"" +
                                (string.IsNullOrWhiteSpace(col.SampleValue) ? "" : $" (e.g. \"{Trunc(col.SampleValue, 30)}\")") +
                                $" and proposed {label} — the fuzzy matcher was unsure. Please confirm.";
                col.Candidates = new List<MappingCandidate>
                {
                    new MappingCandidate { Path = path, Label = label, Score = 0.7, MatchedOn = "AI inference", Chosen = true }
                };
                LastChangedCount++;
            }

            new MappingSuggester(contract).RecomputeUnmappedRequired(result);
            return result;
        }

        private static string BuildPrompt(List<ColumnMapping> targets, SourceRow? sample, FieldContract contract)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SOURCE COLUMNS to map (header => sample value):");
            foreach (var c in targets)
            {
                string s = sample != null ? sample[c.SourceHeader] : string.Empty;
                sb.AppendLine($"- {c.SourceHeader} => {Trunc(s, 40)}");
            }
            sb.AppendLine();
            sb.AppendLine("ALLOWED CargoWise field paths (path = label):");
            foreach (var f in contract.MappableFields)
                sb.AppendLine($"- {f.Path} = {f.Label}");
            return sb.ToString();
        }

        private static IEnumerable<(string header, string path)> ParseMappings(string text)
        {
            string json = ExtractJson(text);
            if (json.Length == 0) yield break;
            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch { yield break; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("mappings", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    yield break;
                foreach (var m in arr.EnumerateArray())
                {
                    string h = m.TryGetProperty("header", out var hv) ? hv.GetString() ?? "" : "";
                    string p = m.TryGetProperty("path", out var pv) ? pv.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(h)) yield return (h, p.Trim());
                }
            }
        }

        private static string ExtractJson(string text)
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            return (start >= 0 && end > start) ? text.Substring(start, end - start + 1) : string.Empty;
        }

        private static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
