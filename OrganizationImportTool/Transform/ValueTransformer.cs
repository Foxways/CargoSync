using System;
using System.Globalization;
using System.Linq;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Transform
{
    /// <summary>
    /// Coerces raw cell text into the form CargoWise expects for a field's type:
    /// booleans to true/false, dates to ISO (yyyy-MM-dd), numbers cleaned, and strings
    /// trimmed/length-capped. Pure and deterministic - safe to run on every value.
    /// </summary>
    public static class ValueTransformer
    {
        public static string Coerce(ContractField? field, string raw)
        {
            raw = (raw ?? string.Empty).Trim();
            if (raw.Length == 0) return raw;
            if (field == null) return raw;

            switch (field.Type)
            {
                case "bool": return Bool(raw);
                case "date": return Date(raw) ?? raw;
                case "int": return Integer(raw) ?? raw;
                case "decimal": return Decimal(raw) ?? raw;
                default:
                    if (field.MaxLength is int max && raw.Length > max)
                        raw = raw.Substring(0, max);
                    return raw;
            }
        }

        public static string Bool(string raw)
        {
            string v = raw.Trim().ToLowerInvariant();
            return v is "1" or "true" or "yes" or "y" or "t" or "x" or "on" ? "true" : "false";
        }

        /// <summary>Parse a wide range of date formats into ISO yyyy-MM-dd; null if unrecognised.</summary>
        public static string? Date(string raw)
        {
            string[] formats =
            {
                "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "M/d/yyyy",
                "dd-MM-yyyy", "dd.MM.yyyy", "yyyy/MM/dd", "dd MMM yyyy", "d MMM yyyy",
                "dd-MMM-yyyy", "yyyyMMdd", "MMM d, yyyy", "MMMM d, yyyy"
            };
            if (DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
                return exact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var any))
                return any.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return null;
        }

        public static string? Integer(string raw)
        {
            string cleaned = NormalizeNumber(raw);
            if (!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                return null;
            // An int field must receive an integral value; "1.5" passes through untouched
            // (Coerce falls back to the raw text) rather than silently becoming 15.
            return d == decimal.Truncate(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : null;
        }

        public static string? Decimal(string raw)
        {
            string cleaned = NormalizeNumber(raw);
            return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)
                ? d.ToString(CultureInfo.InvariantCulture) : null;
        }

        /// <summary>
        /// Make "1,5", "1.234,56", "1,234.56" and "1.234.567" all parse correctly regardless of
        /// the file's locale: when both separators appear the RIGHTMOST one is the decimal point;
        /// a lone comma with 1-2 trailing digits is a decimal comma, otherwise a thousands group;
        /// repeated dots are thousands groups.
        /// </summary>
        private static string NormalizeNumber(string raw)
        {
            string s = new string(raw.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-').ToArray());
            bool hasDot = s.Contains('.'), hasComma = s.Contains(',');

            if (hasDot && hasComma)
            {
                char dec = s.LastIndexOf('.') > s.LastIndexOf(',') ? '.' : ',';
                char thousands = dec == '.' ? ',' : '.';
                s = s.Replace(thousands.ToString(), string.Empty);
                if (dec == ',') s = s.Replace(',', '.');
            }
            else if (hasComma)
            {
                int last = s.LastIndexOf(',');
                bool single = s.IndexOf(',') == last;
                int trailing = s.Length - last - 1;
                s = single && trailing is >= 1 and <= 2
                    ? s.Replace(',', '.')                  // European decimal comma: 1,5 -> 1.5
                    : s.Replace(",", string.Empty);        // thousands: 1,000 -> 1000
            }
            else if (hasDot && s.IndexOf('.') != s.LastIndexOf('.'))
            {
                s = s.Replace(".", string.Empty);          // repeated dots = thousands: 1.234.567
            }
            return s;
        }
    }
}
