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

    /// <summary>How multiple conditions combine.</summary>
    public enum RuleLogic { And = 0, Or = 1 }

    /// <summary>One WHEN test: a source column compared to a value.</summary>
    public sealed class RuleCondition
    {
        public string Column { get; set; } = string.Empty;
        public RuleOp Op { get; set; } = RuleOp.Equals;
        public string Value { get; set; } = string.Empty;

        public bool IsComplete =>
            !string.IsNullOrWhiteSpace(Column) &&
            (Op == RuleOp.IsEmpty || Op == RuleOp.IsNotEmpty || !string.IsNullOrWhiteSpace(Value));
    }

    /// <summary>One THEN action: set a CargoWise field to a value.</summary>
    public sealed class RuleAction
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsComplete => !string.IsNullOrWhiteSpace(Field);
    }

    /// <summary>
    /// A no-code transformation rule. The original single condition/action lives in the scalar
    /// <see cref="WhenColumn"/>/<see cref="ThenField"/> properties (so existing saved rules keep
    /// working unchanged); <see cref="Conditions"/>/<see cref="Actions"/> add further WHEN tests
    /// (combined per <see cref="Logic"/>) and further THEN actions. Evaluated per row before send.
    /// </summary>
    public class TransformRule
    {
        public bool Enabled { get; set; } = true;

        // ---- primary (scalar) condition - kept for backward compatibility ----
        public string WhenColumn { get; set; } = string.Empty;
        public RuleOp Op { get; set; } = RuleOp.Equals;
        public string WhenValue { get; set; } = string.Empty;

        // ---- primary (scalar) action ----
        public string ThenField { get; set; } = string.Empty;
        public string ThenValue { get; set; } = string.Empty;

        // ---- additional conditions / actions (multi-condition rules) ----
        public RuleLogic Logic { get; set; } = RuleLogic.And;
        public List<RuleCondition> Conditions { get; set; } = new();
        public List<RuleAction> Actions { get; set; } = new();

        /// <summary>All conditions (primary first), regardless of completeness.</summary>
        public IEnumerable<RuleCondition> EffectiveConditions()
        {
            if (!string.IsNullOrWhiteSpace(WhenColumn))
                yield return new RuleCondition { Column = WhenColumn, Op = Op, Value = WhenValue };
            foreach (var c in Conditions) yield return c;
        }

        /// <summary>All actions (primary first), regardless of completeness.</summary>
        public IEnumerable<RuleAction> EffectiveActions()
        {
            if (!string.IsNullOrWhiteSpace(ThenField))
                yield return new RuleAction { Field = ThenField, Value = ThenValue };
            foreach (var a in Actions) yield return a;
        }

        public bool IsComplete =>
            EffectiveConditions().Any(c => c.IsComplete) && EffectiveActions().Any(a => a.IsComplete);

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
            string Cond(RuleCondition c) => c.Op is RuleOp.IsEmpty or RuleOp.IsNotEmpty
                ? $"\"{c.Column}\" {OpText(c.Op)}"
                : $"\"{c.Column}\" {OpText(c.Op)} \"{c.Value}\"";

            string joiner = Logic == RuleLogic.Or ? " OR " : " AND ";
            string when = string.Join(joiner, EffectiveConditions().Where(c => c.IsComplete).Select(Cond));
            string then = string.Join(", ", EffectiveActions().Where(a => a.IsComplete).Select(a => $"{a.Field} = \"{a.Value}\""));
            return $"If {when} → set {then}";
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
                var conds = r.EffectiveConditions().Where(c => c.IsComplete).ToList();
                if (conds.Count == 0) continue;

                bool match = r.Logic == RuleLogic.Or
                    ? conds.Any(c => Matches(c, row[c.Column]))
                    : conds.All(c => Matches(c, row[c.Column]));

                if (!match) continue;
                foreach (var a in r.EffectiveActions().Where(a => a.IsComplete))
                    values[a.Field] = a.Value ?? string.Empty;
                applied.Add(r.Describe());
            }
            return applied;
        }

        private static bool Matches(RuleCondition c, string? cell)
        {
            string val = (cell ?? string.Empty).Trim();
            string cmp = (c.Value ?? string.Empty).Trim();
            return c.Op switch
            {
                RuleOp.Equals => string.Equals(val, cmp, StringComparison.OrdinalIgnoreCase),
                RuleOp.NotEquals => !string.Equals(val, cmp, StringComparison.OrdinalIgnoreCase),
                RuleOp.Contains => cmp.Length > 0 && val.IndexOf(cmp, StringComparison.OrdinalIgnoreCase) >= 0,
                RuleOp.StartsWith => cmp.Length > 0 && val.StartsWith(cmp, StringComparison.OrdinalIgnoreCase),
                RuleOp.IsEmpty => val.Length == 0,
                RuleOp.IsNotEmpty => val.Length > 0,
                _ => false
            };
        }
    }
}
