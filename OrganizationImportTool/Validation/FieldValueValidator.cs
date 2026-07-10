using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Validation
{
    /// <summary>
    /// Checks a single value destined for a CargoWise field against the contract (enum membership,
    /// length, and basic type). Used to flag bad constants and rule outputs inline in the mapping
    /// screen, before the send gate. Returns null when the value is acceptable, otherwise a short
    /// human-readable problem description.
    /// </summary>
    public static class FieldValueValidator
    {
        public static string? Check(FieldContract contract, string? path, string? value)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(value)) return null;

            var field = contract.FindByPath(path)
                        ?? contract.FindByPath(Regex.Replace(path, @"\[\d+\]", "[]"));
            if (field == null) return null;

            string v = value.Trim();

            var allowed = contract.AllowedEnumCodes(field);
            if (allowed is { Count: > 0 } && !allowed.Contains(v, StringComparer.OrdinalIgnoreCase))
                return $"'{v}' is not a known code ({string.Join("/", allowed)})";

            if (field.MaxLength is int max && v.Length > max)
                return $"longer than {max} chars (will be truncated)";

            switch ((field.Type ?? "string").ToLowerInvariant())
            {
                case "bool":
                case "boolean":
                    if (!IsBoolish(v)) return "expected true/false (or yes/no, 1/0)";
                    break;
                case "int":
                case "integer":
                    if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        return "expected a whole number";
                    break;
                case "decimal":
                case "number":
                case "double":
                    if (!IsNumeric(v)) return "expected a number";
                    break;
                case "date":
                case "datetime":
                    if (!DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
                        !DateTime.TryParse(v, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                        return "expected a date";
                    break;
            }
            return null;
        }

        private static bool IsBoolish(string v) => v.ToLowerInvariant() is
            "1" or "true" or "yes" or "y" or "t" or "x" or "on" or
            "0" or "false" or "no" or "n" or "f" or "off";

        private static bool IsNumeric(string v) =>
            decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out _) ||
            decimal.TryParse(v, NumberStyles.Number, CultureInfo.CurrentCulture, out _);
    }
}
