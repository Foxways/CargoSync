using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Mapping
{
    /// <summary>How a rule condition compares the source column's value.</summary>
    public enum RuleOp
    {
        Equals = 0,
        NotEquals = 1,
        Contains = 2,
        StartsWith = 3,
        IsEmpty = 4,
        IsNotEmpty = 5
    }

    /// <summary>
    /// One no-code transformation rule: WHEN a source column matches a condition,
    /// THEN set a CargoWise field to a value. Evaluated per row before validation/send.
    /// </summary>
    public class TransformRule
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Source column header the condition reads.</summary>
        public string WhenColumn { get; set; } = string.Empty;
        public RuleOp Op { get; set; } = RuleOp.Equals;

        /// <summary>Comparison value (ignored for IsEmpty / IsNotEmpty).</summary>
        public string WhenValue { get; set; } = string.Empty;

        /// <summary>Target field path the action writes.</summary>
        public string ThenField { get; set; } = string.Empty;

        /// <summary>Value to set when the condition matches.</summary>
        public string ThenValue { get; set; } = string.Empty;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(WhenColumn) && !string.IsNullOrWhiteSpace(ThenField) &&
            (Op == RuleOp.IsEmpty || Op == RuleOp.IsNotEmpty || !string.IsNullOrWhiteSpace(WhenValue));

        public static string OpText(RuleOp op) => op switch
        {
            RuleOp.Equals => "is",
            RuleOp.NotEquals => "is not",
            RuleOp.Contains => "contains",
            RuleOp.StartsWith => "starts with",
            RuleOp.IsEmpty => "is empty",
            RuleOp.IsNotEmpty => "is not empty",
            _ => op.ToString()
        };

        public string Describe()
        {
            string cond = Op is RuleOp.IsEmpty or RuleOp.IsNotEmpty
                ? $"\"{WhenColumn}\" {OpText(Op)}"
                : $"\"{WhenColumn}\" {OpText(Op)} \"{WhenValue}\"";
            return $"If {cond} → set {ThenField} = \"{ThenValue}\"";
        }
    }

    /// <summary>Applies no-code rules to a row's mapped values, reading raw cells from the source row.</summary>
    public static class RuleEngine
    {
        /// <summary>Run every enabled, complete rule; mutate <paramref name="values"/>; return what fired.</summary>
        public static List<string> Apply(IEnumerable<TransformRule> rules, SourceRow row, IDictionary<string, string> values)
        {
            var applied = new List<string>();
            foreach (var r in rules.Where(r => r.Enabled && r.IsComplete))
            {
                string cell = row[r.WhenColumn] ?? string.Empty;
                if (Matches(r, cell))
                {
                    values[r.ThenField] = r.ThenValue ?? string.Empty;
                    applied.Add(r.Describe());
                }
            }
            return applied;
        }

        private static bool Matches(TransformRule r, string cell)
        {
            cell = (cell ?? string.Empty).Trim();
            string val = (r.WhenValue ?? string.Empty).Trim();
            return r.Op switch
            {
                RuleOp.Equals => string.Equals(cell, val, StringComparison.OrdinalIgnoreCase),
                RuleOp.NotEquals => !string.Equals(cell, val, StringComparison.OrdinalIgnoreCase),
                RuleOp.Contains => val.Length > 0 && cell.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0,
                RuleOp.StartsWith => val.Length > 0 && cell.StartsWith(val, StringComparison.OrdinalIgnoreCase),
                RuleOp.IsEmpty => cell.Length == 0,
                RuleOp.IsNotEmpty => cell.Length > 0,
                _ => false
            };
        }
    }
}
