using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;

namespace OrganizationImportTool.Transform
{
    /// <summary>
    /// The app's "decision brain" for filling in values the client left out but CargoWise needs.
    /// Currently derives a missing ClosestPort UN/LOCODE (required when Org Code Generation is on)
    /// from the address's related port, the city, or the country - deterministically first, then
    /// via AI when configured. Results are cached so large files don't re-ask for the same city.
    /// </summary>
    public static class SmartDefaults
    {
        private const string ClosestPortKey = "orgHeader.closestPort.code";

        private static readonly Dictionary<string, string> _aiCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _cacheLock = new();

        /// <summary>Fill missing intelligent defaults into <paramref name="values"/>. Returns notes describing what was filled.</summary>
        public static async Task<List<string>> FillMissingAsync(IDictionary<string, string> values, AiRouter? router, bool aiEnabled)
        {
            var notes = new List<string>();
            await FillClosestPortAsync(values, router, aiEnabled, notes);
            return notes;
        }

        private static async Task FillClosestPortAsync(IDictionary<string, string> values, AiRouter? router, bool aiEnabled, List<string> notes)
        {
            if (NonEmpty(values, ClosestPortKey)) return; // already provided

            string related = Get(values, "orgAddressCollection[].relatedPortCode.code");
            string city = Get(values, "orgAddressCollection[].city");
            string country = Get(values, "orgAddressCollection[].countryCode.code");

            string? unloco = null;
            string source = "";

            if (LooksLikeUnloco(related)) { unloco = related.Trim().ToUpperInvariant(); source = "address related port"; }
            else if (city.Length > 0 && CityUnloco.TryGetValue(city.Trim().ToLowerInvariant(), out var byCity)) { unloco = byCity; source = $"city '{city}'"; }
            else if (country.Length > 0 && CountryDefaultPort.TryGetValue(country.Trim().ToUpperInvariant(), out var byCountry)) { unloco = byCountry; source = $"country '{country}'"; }
            else if (aiEnabled && router != null && router.IsConfigured && (city.Length > 0 || country.Length > 0))
            {
                unloco = await AskAiAsync(router, city, country);
                if (unloco != null) source = "AI";
            }

            if (!string.IsNullOrEmpty(unloco))
            {
                values[ClosestPortKey] = unloco;
                notes.Add($"ClosestPort = {unloco} (derived from {source})");
            }
        }

        private static async Task<string?> AskAiAsync(AiRouter router, string city, string country)
        {
            string key = $"{city}|{country}";
            lock (_cacheLock) { if (_aiCache.TryGetValue(key, out var cached)) return cached; }

            try
            {
                var resp = await router.CompleteAsync(new AiRequest
                {
                    System = "You return UN/LOCODE port codes. Reply with ONLY the 5-character UN/LOCODE " +
                             "(e.g. AUSYD) for the nearest major sea or air port to the given location, or NONE if unsure.",
                    Prompt = $"City: {city}\nCountry: {country}",
                    MaxTokensOverride = 16,
                    Operation = "derive-unloco"
                });

                string? result = null;
                if (resp.Success && !string.IsNullOrWhiteSpace(resp.Text))
                {
                    var m = Regex.Match(resp.Text.ToUpperInvariant(), @"\b[A-Z]{2}[A-Z0-9]{3}\b");
                    if (m.Success && !resp.Text.ToUpperInvariant().Contains("NONE")) result = m.Value;
                }
                lock (_cacheLock) { _aiCache[key] = result; }
                return result;
            }
            catch { return null; }
        }

        private static bool LooksLikeUnloco(string s)
            => !string.IsNullOrWhiteSpace(s) && Regex.IsMatch(s.Trim().ToUpperInvariant(), @"^[A-Z]{2}[A-Z0-9]{3}$");

        private static string Get(IDictionary<string, string> v, string key) => v.TryGetValue(key, out var s) ? (s ?? "") : "";
        private static bool NonEmpty(IDictionary<string, string> v, string key) => v.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s);

        // ---- built-in knowledge: major city -> UN/LOCODE ----
        private static readonly Dictionary<string, string> CityUnloco = new(StringComparer.OrdinalIgnoreCase)
        {
            ["sydney"] = "AUSYD", ["melbourne"] = "AUMEL", ["brisbane"] = "AUBNE", ["perth"] = "AUPER",
            ["adelaide"] = "AUADL", ["fremantle"] = "AUFRE", ["auckland"] = "NZAKL", ["wellington"] = "NZWLG",
            ["singapore"] = "SGSIN", ["hong kong"] = "HKHKG", ["shanghai"] = "CNSHA", ["shenzhen"] = "CNSZX",
            ["ningbo"] = "CNNGB", ["guangzhou"] = "CNCAN", ["qingdao"] = "CNTAO", ["beijing"] = "CNBJS",
            ["tokyo"] = "JPTYO", ["yokohama"] = "JPYOK", ["osaka"] = "JPOSA", ["kobe"] = "JPUKB",
            ["busan"] = "KRPUS", ["seoul"] = "KRSEL", ["mumbai"] = "INBOM", ["nhava sheva"] = "INNSA",
            ["chennai"] = "INMAA", ["delhi"] = "INDEL", ["new delhi"] = "INDEL", ["kolkata"] = "INCCU",
            ["dubai"] = "AEDXB", ["jebel ali"] = "AEJEA", ["abu dhabi"] = "AEAUH", ["london"] = "GBLON",
            ["felixstowe"] = "GBFXT", ["southampton"] = "GBSOU", ["liverpool"] = "GBLIV", ["rotterdam"] = "NLRTM",
            ["amsterdam"] = "NLAMS", ["antwerp"] = "BEANR", ["hamburg"] = "DEHAM", ["bremerhaven"] = "DEBRV",
            ["le havre"] = "FRLEH", ["paris"] = "FRPAR", ["marseille"] = "FRMRS", ["barcelona"] = "ESBCN",
            ["valencia"] = "ESVLC", ["algeciras"] = "ESALG", ["genoa"] = "ITGOA", ["la spezia"] = "ITSPE",
            ["new york"] = "USNYC", ["newark"] = "USEWR", ["los angeles"] = "USLAX", ["long beach"] = "USLGB",
            ["savannah"] = "USSAV", ["houston"] = "USHOU", ["chicago"] = "USCHI", ["miami"] = "USMIA",
            ["seattle"] = "USSEA", ["vancouver"] = "CAVAN", ["montreal"] = "CAMTR", ["toronto"] = "CATOR",
            ["santos"] = "BRSSZ", ["durban"] = "ZADUR", ["cape town"] = "ZACPT", ["manila"] = "PHMNL",
            ["jakarta"] = "IDJKT", ["surabaya"] = "IDSUB", ["port klang"] = "MYPKG", ["tanjung pelepas"] = "MYTPP",
            ["bangkok"] = "THBKK", ["laem chabang"] = "THLCH", ["ho chi minh"] = "VNSGN", ["ho chi minh city"] = "VNSGN",
            ["haiphong"] = "VNHPH", ["colombo"] = "LKCMB", ["karachi"] = "PKKHI",
        };

        // ---- fallback: country ISO-2 -> a major gateway port ----
        private static readonly Dictionary<string, string> CountryDefaultPort = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AU"] = "AUSYD", ["NZ"] = "NZAKL", ["SG"] = "SGSIN", ["HK"] = "HKHKG", ["CN"] = "CNSHA",
            ["JP"] = "JPTYO", ["KR"] = "KRPUS", ["IN"] = "INBOM", ["AE"] = "AEDXB", ["GB"] = "GBLON",
            ["NL"] = "NLRTM", ["BE"] = "BEANR", ["DE"] = "DEHAM", ["FR"] = "FRLEH", ["ES"] = "ESBCN",
            ["IT"] = "ITGOA", ["US"] = "USNYC", ["CA"] = "CAVAN", ["BR"] = "BRSSZ", ["ZA"] = "ZADUR",
            ["PH"] = "PHMNL", ["ID"] = "IDJKT", ["MY"] = "MYPKG", ["TH"] = "THBKK", ["VN"] = "VNSGN",
            ["LK"] = "LKCMB", ["PK"] = "PKKHI",
        };
    }
}
