using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Enrichment
{
    /// <summary>One proposed value to FILL a currently-empty field, fetched from an external source.</summary>
    public class EnrichmentSuggestion
    {
        public int RowNumber { get; set; }
        public string Path { get; set; } = string.Empty;
        public string FieldLabel { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;

        /// <summary>Provider that produced it, e.g. "Postal API" or "AI".</summary>
        public string Source { get; set; } = string.Empty;

        /// <summary>What it was derived from, e.g. "postcode 2000, AU".</summary>
        public string Basis { get; set; } = string.Empty;

        public bool Accept { get; set; } = true;
    }

    /// <summary>
    /// A pluggable source of enrichment values. Implementations only ever suggest values for fields
    /// that are currently EMPTY - enrichment fills gaps, it never overwrites the operator's data.
    /// </summary>
    public interface IEnrichmentProvider
    {
        string Name { get; }
        bool IsAvailable { get; }
        Task<List<EnrichmentSuggestion>> EnrichAsync(IReadOnlyList<RowValues> rows, FieldContract contract, CancellationToken ct = default);
    }

    /// <summary>Well-known address field paths used by the enrichers (index-0 collection item).</summary>
    public static class AddressPaths
    {
        public const string City = "orgAddressCollection[].city";
        public const string State = "orgAddressCollection[].state";
        public const string Country = "orgAddressCollection[].countryCode.code";
        public const string PostCode = "orgAddressCollection[].postCode";

        public static bool Empty(IDictionary<string, string> v, string path) =>
            !v.TryGetValue(path, out var s) || string.IsNullOrWhiteSpace(s);

        public static string Get(IDictionary<string, string> v, string path) =>
            v.TryGetValue(path, out var s) ? s.Trim() : string.Empty;
    }
}
