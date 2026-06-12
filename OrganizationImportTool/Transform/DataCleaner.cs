using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Transform
{
    public enum CleanSource { Auto, Ai }

    /// <summary>One proposed fix to a single cell, shown to the operator before it is applied.</summary>
    public class CleaningChange
    {
        public int RowNumber { get; set; }
        public string Path { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string Original { get; set; } = string.Empty;
        public string Cleaned { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public CleanSource Source { get; set; } = CleanSource.Auto;

        /// <summary>Operator's decision - applied only when true.</summary>
        public bool Accept { get; set; } = true;
    }

    /// <summary>One row's mapped target-path values, ready for cleaning.</summary>
    public class RowValues
    {
        public int RowNumber { get; set; }
        public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Inspects the mapped values and proposes fixes BEFORE sending: deterministic normalisation
    /// (whitespace, casing, country/currency → code, boolean, enum-case, length) plus an optional
    /// AI pass that resolves values the rules can't (unknown country names, mis-typed enum codes).
    /// Every change is surfaced for the operator to accept or reject - nothing is silently altered.
    /// </summary>
    public class DataCleaner
    {
        private static readonly Regex IndexedSegment = new(@"\[\d+\]", RegexOptions.Compiled);

        public async Task<List<CleaningChange>> AnalyzeAsync(
            IReadOnlyList<RowValues> rows, FieldContract contract,
            AiRouter? router, bool aiEnabled, CancellationToken ct = default)
        {
            var changes = new List<CleaningChange>();

            // Unresolved values that AI may be able to fix: (kind, enumName, allowed) -> distinct raw values.
            var aiCountry = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aiCurrency = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var aiEnum = new Dictionary<string, (List<string> allowed, HashSet<string> values)>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                foreach (var path in row.Values.Keys.ToList())
                {
                    string original = row.Values[path];
                    if (string.IsNullOrEmpty(original)) continue;
                    var field = contract.FindByPath(NormalizePath(path));
                    string cleaned = original;
                    var reasons = new List<string>();

                    // 1) whitespace + control characters
                    string ws = CleanWhitespace(cleaned);
                    if (ws != cleaned) { cleaned = ws; reasons.Add("trimmed/collapsed whitespace"); }
                    if (cleaned.Length == 0) continue;

                    if (field != null)
                    {
                        // 2) boolean → true/false
                        if (field.Type == "bool")
                        {
                            string b = ValueTransformer.Bool(cleaned);
                            if (!string.Equals(b, cleaned, StringComparison.Ordinal)) { cleaned = b; reasons.Add("normalised to true/false"); }
                        }
                        // 3) country / currency reference codes
                        else if (field.RefTable == "RefCountry")
                        {
                            string r = ReferenceLookup.Resolve("RefCountry", cleaned);
                            if (!string.Equals(r, cleaned, StringComparison.Ordinal)) { cleaned = r; reasons.Add("country name → ISO code"); }
                            if (!IsCode(cleaned, 2) && aiEnabled) aiCountry.Add(cleaned);
                        }
                        else if (field.RefTable == "RefCurrency")
                        {
                            string r = ReferenceLookup.Resolve("RefCurrency", cleaned);
                            if (!string.Equals(r, cleaned, StringComparison.Ordinal)) { cleaned = r; reasons.Add("currency name → ISO code"); }
                            if (!IsCode(cleaned, 3) && aiEnabled) aiCurrency.Add(cleaned);
                        }
                        // 4) UN/LOCODE & port codes are upper-case
                        else if (field.RefTable == "RefUNLOCO" || path.EndsWith("closestPort.code", StringComparison.OrdinalIgnoreCase))
                        {
                            string up = cleaned.ToUpperInvariant();
                            if (up != cleaned) { cleaned = up; reasons.Add("upper-cased code"); }
                        }
                        // 5) enum: fix case to the allowed code, or queue for AI
                        else if (field.Enum != null)
                        {
                            var allowed = contract.AllowedEnumCodes(field);
                            if (allowed != null && allowed.Count > 0)
                            {
                                var exact = allowed.FirstOrDefault(c => string.Equals(c, cleaned, StringComparison.OrdinalIgnoreCase));
                                if (exact != null)
                                {
                                    if (!string.Equals(exact, cleaned, StringComparison.Ordinal)) { cleaned = exact; reasons.Add("matched the allowed code"); }
                                }
                                else if (aiEnabled)
                                {
                                    if (!aiEnum.TryGetValue(field.Enum, out var bucket))
                                        aiEnum[field.Enum] = bucket = (allowed, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                                    bucket.values.Add(cleaned);
                                }
                            }
                        }

                        // 6) length cap (lossy - always surfaced for review)
                        if (field.MaxLength is int max && cleaned.Length > max)
                        {
                            reasons.Add($"truncated to {max} chars (was {cleaned.Length})");
                            cleaned = cleaned.Substring(0, max);
                        }
                    }

                    if (!string.Equals(cleaned, original, StringComparison.Ordinal))
                        changes.Add(new CleaningChange
                        {
                            RowNumber = row.RowNumber, Path = path, FieldLabel = field?.DisplayName ?? path,
                            Original = original, Cleaned = cleaned, Reason = string.Join("; ", reasons), Source = CleanSource.Auto
                        });
                }
            }

            // ---- AI Auto-Fix: resolve the values the rules couldn't ----
            if (aiEnabled && router?.IsConfigured == true)
            {
                var countryMap = await AiResolveCodesAsync(router, aiCountry,
                    "Map each value to its ISO 3166-1 alpha-2 country code (2 letters).", ct);
                var currencyMap = await AiResolveCodesAsync(router, aiCurrency,
                    "Map each value to its ISO 4217 currency code (3 letters).", ct);

                var enumMaps = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in aiEnum)
                    enumMaps[kv.Key] = await AiResolveCodesAsync(router, kv.Value.values,
                        $"Map each value to the single best-matching code from this allowed list: {string.Join(", ", kv.Value.allowed)}. " +
                        "If none fits, return an empty string for that value.", ct, allowed: kv.Value.allowed);

                // Apply AI resolutions as additional per-cell changes.
                foreach (var row in rows)
                    foreach (var path in row.Values.Keys.ToList())
                    {
                        string original = row.Values[path];
                        if (string.IsNullOrWhiteSpace(original)) continue;
                        // start from the whitespace-cleaned value so we compare apples to apples
                        string baseVal = CleanWhitespace(original);
                        var field = contract.FindByPath(NormalizePath(path));
                        if (field == null) continue;

                        string? fixedVal = null;
                        if (field.RefTable == "RefCountry" && !IsCode(ReferenceLookup.Resolve("RefCountry", baseVal), 2))
                            countryMap.TryGetValue(baseVal, out fixedVal);
                        else if (field.RefTable == "RefCurrency" && !IsCode(ReferenceLookup.Resolve("RefCurrency", baseVal), 3))
                            currencyMap.TryGetValue(baseVal, out fixedVal);
                        else if (field.Enum != null && enumMaps.TryGetValue(field.Enum, out var em))
                        {
                            var allowed = contract.AllowedEnumCodes(field);
                            if (allowed != null && !allowed.Any(c => string.Equals(c, baseVal, StringComparison.OrdinalIgnoreCase)))
                                em.TryGetValue(baseVal, out fixedVal);
                        }

                        if (!string.IsNullOrWhiteSpace(fixedVal) && !string.Equals(fixedVal, original, StringComparison.Ordinal))
                        {
                            // replace any earlier Auto change for this cell - the AI fix supersedes it
                            changes.RemoveAll(c => c.RowNumber == row.RowNumber && c.Path == path);
                            changes.Add(new CleaningChange
                            {
                                RowNumber = row.RowNumber, Path = path, FieldLabel = field.DisplayName,
                                Original = original, Cleaned = fixedVal!, Reason = "AI corrected", Source = CleanSource.Ai
                            });
                        }
                    }
            }

            return changes.OrderBy(c => c.RowNumber).ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Ask the AI to map a distinct set of raw values to codes; returns value→code (validated).</summary>
        private static async Task<Dictionary<string, string>> AiResolveCodesAsync(
            AiRouter router, HashSet<string> values, string instruction, CancellationToken ct, List<string>? allowed = null)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (values.Count == 0) return map;

            var sb = new StringBuilder();
            sb.AppendLine(instruction);
            sb.AppendLine("Reply ONLY with compact JSON: {\"results\":[{\"value\":\"<input>\",\"code\":\"<code or empty>\"}]}.");
            sb.AppendLine("VALUES:");
            foreach (var v in values) sb.AppendLine("- " + v);

            try
            {
                var resp = await router.CompleteAsync(new AiRequest
                {
                    System = "You normalise messy reference data to exact codes. Never invent codes outside the allowed set.",
                    Prompt = sb.ToString(),
                    Operation = "data-clean",
                    MaxTokensOverride = 800
                }, ct).ConfigureAwait(false);

                if (!resp.Success || string.IsNullOrWhiteSpace(resp.Text)) return map;
                int s = resp.Text.IndexOf('{'), e = resp.Text.LastIndexOf('}');
                if (s < 0 || e <= s) return map;
                using var doc = JsonDocument.Parse(resp.Text.Substring(s, e - s + 1));
                if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) return map;
                foreach (var item in arr.EnumerateArray())
                {
                    string val = item.TryGetProperty("value", out var vv) ? vv.GetString() ?? "" : "";
                    string code = item.TryGetProperty("code", out var cv) ? (cv.GetString() ?? "").Trim() : "";
                    if (val.Length == 0 || code.Length == 0) continue;
                    if (allowed != null && !allowed.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase))) continue;
                    if (allowed == null && code.Length > 3) continue; // a country/currency code is short
                    map[val] = code;
                }
            }
            catch { /* AI unavailable - deterministic results still stand */ }
            return map;
        }

        /// <summary>Collapse the value-set of a finished analysis into accepted per-row overrides.</summary>
        public static Dictionary<int, Dictionary<string, string>> AcceptedOverrides(IEnumerable<CleaningChange> changes)
        {
            var byRow = new Dictionary<int, Dictionary<string, string>>();
            foreach (var c in changes.Where(c => c.Accept))
            {
                if (!byRow.TryGetValue(c.RowNumber, out var d))
                    byRow[c.RowNumber] = d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                d[c.Path] = c.Cleaned;
            }
            return byRow;
        }

        private static string NormalizePath(string path) => IndexedSegment.Replace(path, "[]");

        private static bool IsCode(string s, int len) =>
            s.Length == len && s.All(char.IsLetter) && s == s.ToUpperInvariant();

        private static string CleanWhitespace(string s)
        {
            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c))
                {
                    if (!prevSpace) { sb.Append(' '); prevSpace = true; }
                }
                else { sb.Append(c); prevSpace = false; }
            }
            return sb.ToString().Trim();
        }
    }
}
