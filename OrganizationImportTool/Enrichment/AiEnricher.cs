using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Enrichment
{
    /// <summary>
    /// AI-backed enrichment: fills a missing country code by inferring it from the city. Distinct
    /// cities are resolved in a single batched call, so cost stays low. Only fills empty fields.
    /// </summary>
    public class AiEnricher : IEnrichmentProvider
    {
        private readonly AiRouter? _router;
        private readonly bool _enabled;

        public AiEnricher(AiRouter? router, bool enabled) { _router = router; _enabled = enabled; }

        public string Name => "AI";
        public bool IsAvailable => _enabled && _router?.IsConfigured == true;

        public async Task<List<EnrichmentSuggestion>> EnrichAsync(IReadOnlyList<RowValues> rows, FieldContract contract, CancellationToken ct = default)
        {
            var suggestions = new List<EnrichmentSuggestion>();
            if (!IsAvailable) return suggestions;

            // Rows with a city but no country → infer the country code from the city.
            var cities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rv in rows)
            {
                string city = AddressPaths.Get(rv.Values, AddressPaths.City);
                if (city.Length > 0 && AddressPaths.Empty(rv.Values, AddressPaths.Country))
                    cities.Add(city);
            }
            if (cities.Count == 0) return suggestions;

            var map = await ResolveAsync(cities,
                "For each city, give its ISO 3166-1 alpha-2 country code (2 letters). If unsure, return empty.", ct);

            foreach (var rv in rows)
            {
                string city = AddressPaths.Get(rv.Values, AddressPaths.City);
                if (city.Length == 0 || !AddressPaths.Empty(rv.Values, AddressPaths.Country)) continue;
                if (map.TryGetValue(city, out var code) && code.Length == 2)
                    suggestions.Add(new EnrichmentSuggestion
                    {
                        RowNumber = rv.RowNumber, Path = AddressPaths.Country,
                        FieldLabel = contract.FindByPath(AddressPaths.Country)?.DisplayName ?? AddressPaths.Country,
                        Value = code, Source = Name, Basis = $"inferred from city \"{city}\""
                    });
            }
            return suggestions;
        }

        private async Task<Dictionary<string, string>> ResolveAsync(HashSet<string> values, string instruction, CancellationToken ct)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            sb.AppendLine(instruction);
            sb.AppendLine("Reply ONLY with compact JSON: {\"results\":[{\"value\":\"<input>\",\"code\":\"<code or empty>\"}]}.");
            sb.AppendLine("VALUES:");
            foreach (var v in values) sb.AppendLine("- " + v);

            try
            {
                var resp = await _router!.CompleteAsync(new AiRequest
                {
                    System = "You enrich missing reference data with exact codes. Never guess wildly; return empty if unsure.",
                    Prompt = sb.ToString(),
                    Operation = "enrich",
                    MaxTokensOverride = 600
                }, ct).ConfigureAwait(false);

                if (!resp.Success || string.IsNullOrWhiteSpace(resp.Text)) return map;
                int s = resp.Text.IndexOf('{'), e = resp.Text.LastIndexOf('}');
                if (s < 0 || e <= s) return map;
                using var doc = JsonDocument.Parse(resp.Text.Substring(s, e - s + 1));
                if (!doc.RootElement.TryGetProperty("results", out var arr) || arr.ValueKind != JsonValueKind.Array) return map;
                foreach (var item in arr.EnumerateArray())
                {
                    string val = item.TryGetProperty("value", out var vv) ? vv.GetString() ?? "" : "";
                    string code = item.TryGetProperty("code", out var cv) ? (cv.GetString() ?? "").Trim().ToUpperInvariant() : "";
                    if (val.Length > 0 && code.Length == 2) map[val] = code;
                }
            }
            catch { }
            return map;
        }
    }
}
