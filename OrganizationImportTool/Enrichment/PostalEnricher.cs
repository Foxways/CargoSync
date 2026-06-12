using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Enrichment
{
    /// <summary>
    /// Fills a missing city/state from a country + postcode using the free, keyless Zippopotam.us
    /// public API (https://api.zippopotam.us/{country}/{postcode}). Best-effort and network-bounded:
    /// any failure just yields no suggestion - the import is never blocked by enrichment.
    /// </summary>
    public class PostalEnricher : IEnrichmentProvider
    {
        public string Name => "Postal API";
        public bool IsAvailable => true;   // no key required

        private const int MaxDistinctLookups = 80;
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };

        public async Task<List<EnrichmentSuggestion>> EnrichAsync(IReadOnlyList<RowValues> rows, FieldContract contract, CancellationToken ct = default)
        {
            var suggestions = new List<EnrichmentSuggestion>();

            // Distinct (country, postcode) lookups for rows missing a city.
            var needed = new Dictionary<(string c, string p), (string place, string state)?>();
            foreach (var rv in rows)
            {
                string country = AddressPaths.Get(rv.Values, AddressPaths.Country);
                string postcode = AddressPaths.Get(rv.Values, AddressPaths.PostCode);
                bool needCity = AddressPaths.Empty(rv.Values, AddressPaths.City);
                bool needState = AddressPaths.Empty(rv.Values, AddressPaths.State);
                if (!(needCity || needState)) continue;
                if (country.Length != 2 || postcode.Length == 0) continue;
                needed[(country.ToLowerInvariant(), postcode)] = null;
            }

            int done = 0;
            foreach (var key in needed.Keys.ToList())
            {
                if (done++ >= MaxDistinctLookups) break;
                needed[key] = await LookupAsync(key.c, key.p, ct).ConfigureAwait(false);
            }

            foreach (var rv in rows)
            {
                string country = AddressPaths.Get(rv.Values, AddressPaths.Country);
                string postcode = AddressPaths.Get(rv.Values, AddressPaths.PostCode);
                if (country.Length != 2 || postcode.Length == 0) continue;
                if (!needed.TryGetValue((country.ToLowerInvariant(), postcode), out var hit) || hit == null) continue;

                if (AddressPaths.Empty(rv.Values, AddressPaths.City) && !string.IsNullOrWhiteSpace(hit.Value.place))
                    suggestions.Add(new EnrichmentSuggestion
                    {
                        RowNumber = rv.RowNumber, Path = AddressPaths.City, FieldLabel = Label(contract, AddressPaths.City),
                        Value = hit.Value.place, Source = Name, Basis = $"postcode {postcode}, {country.ToUpperInvariant()}"
                    });
                if (AddressPaths.Empty(rv.Values, AddressPaths.State) && !string.IsNullOrWhiteSpace(hit.Value.state))
                    suggestions.Add(new EnrichmentSuggestion
                    {
                        RowNumber = rv.RowNumber, Path = AddressPaths.State, FieldLabel = Label(contract, AddressPaths.State),
                        Value = hit.Value.state, Source = Name, Basis = $"postcode {postcode}, {country.ToUpperInvariant()}"
                    });
            }

            return suggestions;
        }

        private static async Task<(string place, string state)?> LookupAsync(string country, string postcode, CancellationToken ct)
        {
            try
            {
                string url = $"https://api.zippopotam.us/{country}/{Uri.EscapeDataString(postcode)}";
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("places", out var places) || places.ValueKind != JsonValueKind.Array || places.GetArrayLength() == 0)
                    return null;
                var p0 = places[0];
                string place = p0.TryGetProperty("place name", out var pn) ? pn.GetString() ?? "" : "";
                string state = p0.TryGetProperty("state abbreviation", out var sa) ? sa.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(state) && p0.TryGetProperty("state", out var st)) state = st.GetString() ?? "";
                return (place, state);
            }
            catch { return null; }
        }

        private static string Label(FieldContract contract, string path) =>
            contract.FindByPath(path)?.DisplayName ?? path;
    }
}
