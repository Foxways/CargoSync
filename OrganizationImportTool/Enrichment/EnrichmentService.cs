using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Enrichment
{
    /// <summary>
    /// Runs the enrichment providers in priority order and merges their suggestions, keeping the
    /// first (highest-priority) value per empty cell. External reference data (Postal API) wins
    /// over AI inference for the same field.
    /// </summary>
    public class EnrichmentService
    {
        private readonly List<IEnrichmentProvider> _providers;

        public EnrichmentService(IEnumerable<IEnrichmentProvider> providers) => _providers = providers.ToList();

        /// <summary>Default stack: free Postal API first, then AI inference.</summary>
        public EnrichmentService(AiRouter? router, bool aiEnabled)
            : this(new IEnrichmentProvider[] { new PostalEnricher(), new AiEnricher(router, aiEnabled) }) { }

        public async Task<List<EnrichmentSuggestion>> RunAsync(IReadOnlyList<RowValues> rows, FieldContract contract, CancellationToken ct = default)
        {
            var seen = new HashSet<(int, string)>();
            var merged = new List<EnrichmentSuggestion>();
            foreach (var p in _providers.Where(p => p.IsAvailable))
            {
                List<EnrichmentSuggestion> got;
                try { got = await p.EnrichAsync(rows, contract, ct); }
                catch (Exception ex) { Logging.AppLog.Warn($"Enrichment provider '{p.Name}' threw unexpectedly — skipping", ex); continue; }
                foreach (var s in got)
                    if (!string.IsNullOrWhiteSpace(s.Value) && seen.Add((s.RowNumber, s.Path)))
                        merged.Add(s);
            }
            return merged.OrderBy(s => s.RowNumber).ThenBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Collapse accepted suggestions into per-row overrides to overlay onto the values.</summary>
        public static Dictionary<int, Dictionary<string, string>> AcceptedOverrides(IEnumerable<EnrichmentSuggestion> suggestions)
        {
            var byRow = new Dictionary<int, Dictionary<string, string>>();
            foreach (var s in suggestions.Where(s => s.Accept))
            {
                if (!byRow.TryGetValue(s.RowNumber, out var d))
                    byRow[s.RowNumber] = d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                d[s.Path] = s.Value;
            }
            return byRow;
        }
    }
}
