using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// A portable bundle of the rules, constants and value-maps from a mapping. Export it to a JSON
    /// file to share between clients/operators (e.g. a master country-code value-map), and import it
    /// into another mapping - either replacing or merging. Keeps these reusable assets independent of
    /// a full column-mapping template.
    /// </summary>
    public sealed class MappingExtras
    {
        public string Name { get; set; } = "Shared rules & maps";
        public string SavedUtc { get; set; } = string.Empty;

        public List<TransformRule> Rules { get; set; } = new();
        public Dictionary<string, string> Constants { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> ValueMaps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static MappingExtras From(MappingResult m, string? name = null) => new()
        {
            Name = name ?? "Shared rules & maps",
            Rules = m.Rules.Select(Clone).ToList(),
            Constants = new Dictionary<string, string>(m.Constants, StringComparer.OrdinalIgnoreCase),
            ValueMaps = m.ValueMaps.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase)
        };

        /// <summary>Apply this bundle to a live mapping; <paramref name="replace"/> clears the targets first.</summary>
        public void ApplyTo(MappingResult m, bool replace)
        {
            if (replace) { m.Rules = new(); m.Constants.Clear(); m.ValueMaps.Clear(); }

            foreach (var r in Rules) m.Rules.Add(Clone(r));
            foreach (var kv in Constants) m.Constants[kv.Key] = kv.Value;

            foreach (var kv in ValueMaps)
            {
                if (!m.ValueMaps.TryGetValue(kv.Key, out var inner))
                    m.ValueMaps[kv.Key] = inner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in kv.Value) inner[pair.Key] = pair.Value;
            }
        }

        public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        public static MappingExtras FromJson(string json)
        {
            var x = JsonSerializer.Deserialize<MappingExtras>(json) ?? new MappingExtras();
            // System.Text.Json rebuilds dictionaries case-sensitive; restore the ignore-case comparers.
            x.Constants = new Dictionary<string, string>(x.Constants, StringComparer.OrdinalIgnoreCase);
            x.ValueMaps = x.ValueMaps.ToDictionary(
                kv => kv.Key,
                kv => new Dictionary<string, string>(kv.Value, StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);
            return x;
        }

        public void Save(string path) => File.WriteAllText(path, ToJson());
        public static MappingExtras Load(string path) => FromJson(File.ReadAllText(path));

        public bool IsEmpty => Rules.Count == 0 && Constants.Count == 0 && ValueMaps.Count == 0;

        private static TransformRule Clone(TransformRule r) => new()
        {
            Enabled = r.Enabled,
            WhenColumn = r.WhenColumn, Op = r.Op, WhenValue = r.WhenValue,
            ThenField = r.ThenField, ThenValue = r.ThenValue,
            Logic = r.Logic,
            Conditions = r.Conditions.Select(c => new RuleCondition { Column = c.Column, Op = c.Op, Value = c.Value }).ToList(),
            Actions = r.Actions.Select(a => new RuleAction { Field = a.Field, Value = a.Value }).ToList()
        };
    }
}
