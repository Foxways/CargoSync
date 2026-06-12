using System;
using System.Collections.Generic;

namespace OrganizationImportTool.Transform
{
    /// <summary>
    /// Resolves human-friendly values into CargoWise reference codes for the common lookup
    /// tables - e.g. country "Australia" -> "AU". Unknown tables / values pass through unchanged
    /// (CargoWise then validates them). Extendable: load client-specific code maps here later.
    /// </summary>
    public static class ReferenceLookup
    {
        public static string Resolve(string? refTable, string value)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0 || string.IsNullOrEmpty(refTable)) return value;

            switch (refTable)
            {
                case "RefCountry":
                    // already a 2-letter ISO code?
                    if (value.Length == 2 && IsAlpha(value)) return value.ToUpperInvariant();
                    return Countries.TryGetValue(value.ToLowerInvariant(), out var c) ? c : value;
                case "RefCurrency":
                    if (value.Length == 3 && IsAlpha(value)) return value.ToUpperInvariant();
                    return Currencies.TryGetValue(value.ToLowerInvariant(), out var cur) ? cur : value;
                default:
                    return value; // UNLOCO, groups, companies, etc. - pass through as-is
            }
        }

        private static bool IsAlpha(string s)
        {
            foreach (char ch in s) if (!char.IsLetter(ch)) return false;
            return true;
        }

        // A pragmatic subset; extend as needed. Keys are lower-cased.
        private static readonly Dictionary<string, string> Countries = new(StringComparer.OrdinalIgnoreCase)
        {
            ["australia"] = "AU", ["new zealand"] = "NZ", ["united states"] = "US",
            ["united states of america"] = "US", ["usa"] = "US", ["united kingdom"] = "GB",
            ["uk"] = "GB", ["great britain"] = "GB", ["france"] = "FR", ["germany"] = "DE",
            ["spain"] = "ES", ["italy"] = "IT", ["netherlands"] = "NL", ["belgium"] = "BE",
            ["china"] = "CN", ["hong kong"] = "HK", ["singapore"] = "SG", ["japan"] = "JP",
            ["south korea"] = "KR", ["korea"] = "KR", ["india"] = "IN", ["canada"] = "CA",
            ["mexico"] = "MX", ["brazil"] = "BR", ["united arab emirates"] = "AE",
            ["uae"] = "AE", ["saudi arabia"] = "SA", ["south africa"] = "ZA",
            ["philippines"] = "PH", ["indonesia"] = "ID", ["malaysia"] = "MY",
            ["thailand"] = "TH", ["vietnam"] = "VN", ["ireland"] = "IE", ["switzerland"] = "CH",
        };

        private static readonly Dictionary<string, string> Currencies = new(StringComparer.OrdinalIgnoreCase)
        {
            ["australian dollar"] = "AUD", ["us dollar"] = "USD", ["euro"] = "EUR",
            ["pound"] = "GBP", ["british pound"] = "GBP", ["new zealand dollar"] = "NZD",
            ["singapore dollar"] = "SGD", ["japanese yen"] = "JPY", ["yen"] = "JPY",
            ["chinese yuan"] = "CNY", ["yuan"] = "CNY", ["philippine peso"] = "PHP",
        };
    }
}
