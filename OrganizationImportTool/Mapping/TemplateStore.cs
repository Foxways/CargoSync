using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// Persists mapping templates as JSON files (one per template) under %AppData%, and converts
    /// between a <see cref="MappingTemplate"/> and the live <see cref="MappingResult"/>.
    /// </summary>
    public class TemplateStore
    {
        private readonly string _dir;
        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public TemplateStore(string? dir = null)
        {
            _dir = dir ?? Path.Combine(AppPaths.DataDir, "Templates");
            Directory.CreateDirectory(_dir);
        }

        public List<MappingTemplate> LoadAll()
        {
            var list = new List<MappingTemplate>();
            foreach (var file in Directory.GetFiles(_dir, "*.json"))
            {
                try
                {
                    var t = JsonSerializer.Deserialize<MappingTemplate>(File.ReadAllText(file));
                    if (t != null) list.Add(t);
                }
                catch { /* skip corrupt template files */ }
            }
            return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Templates a user can pick: the client's own + global ones, excluding the hidden auto-learned memory.</summary>
        public List<MappingTemplate> ForClient(string? clientId) =>
            LoadAll().Where(t => !t.IsAuto &&
                                 (t.IsGlobal || string.Equals(t.ClientId, clientId, StringComparison.OrdinalIgnoreCase)))
                     .ToList();

        /// <summary>Deterministic file id for a client's auto-learned memory (one per client).</summary>
        public static string AutoId(string? clientId)
        {
            string raw = "auto_" + (clientId ?? "global");
            var safe = new string(raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
            return safe;
        }

        /// <summary>The per-client self-learned mapping memory, or null if nothing learned yet.</summary>
        public MappingTemplate? GetAuto(string? clientId)
        {
            string path = Path.Combine(_dir, AutoId(clientId) + ".json");
            if (!File.Exists(path)) return null;
            try
            {
                var t = JsonSerializer.Deserialize<MappingTemplate>(File.ReadAllText(path));
                return (t != null && t.IsAuto) ? t : null;
            }
            catch { return null; }
        }

        /// <summary>Persist (overwrite) the client's auto-learned memory under its deterministic id.</summary>
        public void SaveAuto(MappingTemplate template, string nowUtcIso)
        {
            template.IsAuto = true;
            template.Id = AutoId(template.ClientId);
            Save(template, nowUtcIso);
        }

        public void Save(MappingTemplate template, string nowUtcIso)
        {
            template.SavedUtc = nowUtcIso;
            File.WriteAllText(Path.Combine(_dir, template.Id + ".json"),
                JsonSerializer.Serialize(template, JsonOpts));
        }

        public void Delete(string id)
        {
            string path = Path.Combine(_dir, id + ".json");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    /// <summary>Converts between the live mapping and a saved template.</summary>
    public static class TemplateMapper
    {
        /// <summary>Build a reusable template from the operator's confirmed mapping.</summary>
        public static MappingTemplate ToTemplate(MappingResult result, string name, string? clientId)
        {
            var t = new MappingTemplate { Name = name, ClientId = clientId };
            foreach (var c in result.Columns.Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath)))
            {
                var entry = new TemplateEntry { TargetPath = c.TargetPath!, SourceHeader = c.SourceHeader };
                if (result.ValueMaps.TryGetValue(c.TargetPath!, out var vm) && vm.Count > 0)
                    entry.ValueMap = new Dictionary<string, string>(vm);
                t.Entries.Add(entry);
            }
            foreach (var kv in result.Constants)
                t.Entries.Add(new TemplateEntry { TargetPath = kv.Key, ConstantValue = kv.Value });
            t.Rules = result.Rules.Select(CloneRule).ToList();
            return t;
        }

        private static TransformRule CloneRule(TransformRule r) => new TransformRule
        {
            Enabled = r.Enabled, WhenColumn = r.WhenColumn, Op = r.Op, WhenValue = r.WhenValue,
            ThenField = r.ThenField, ThenValue = r.ThenValue
        };

        /// <summary>
        /// Apply a template onto the current table's mapping result: re-point columns by header,
        /// load constants and value-maps. Columns whose header isn't in the file are skipped.
        /// </summary>
        public static void Apply(MappingTemplate template, SourceTable table, FieldContract contract, MappingResult into)
        {
            var headerLookup = new HashSet<string>(table.Headers, StringComparer.OrdinalIgnoreCase);

            // reset existing column targets first so the template fully defines the mapping
            foreach (var col in into.Columns)
            {
                col.TargetPath = null;
                col.Confidence = MappingConfidence.Unmapped;
                col.Source = MappingSource.None;
            }
            into.Constants.Clear();
            into.ValueMaps.Clear();
            into.Rules = template.Rules.Select(CloneRule).ToList();

            foreach (var e in template.Entries)
            {
                if (e.IsConstant)
                {
                    into.Constants[e.TargetPath] = e.ConstantValue ?? string.Empty;
                    if (e.ValueMap != null) into.ValueMaps[e.TargetPath] = new Dictionary<string, string>(e.ValueMap, StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (string.IsNullOrEmpty(e.SourceHeader) || !headerLookup.Contains(e.SourceHeader)) continue;
                var col = into.Columns.FirstOrDefault(c =>
                    string.Equals(c.SourceHeader, e.SourceHeader, StringComparison.OrdinalIgnoreCase));
                if (col == null) continue;

                col.TargetPath = e.TargetPath;
                col.Include = true;
                col.Source = MappingSource.Template;
                col.Confidence = MappingConfidence.High;
                col.Score = 1.0;
                col.Approved = true;                     // operator chose to load this template
                string aLabel = LabelFor(contract, e.TargetPath);
                col.Rationale = $"Loaded from template \"{template.Name}\" — \"{col.SourceHeader}\" → {aLabel}.";
                col.Candidates = new List<MappingCandidate>
                {
                    new MappingCandidate { Path = e.TargetPath, Label = aLabel, Score = 1.0, MatchedOn = $"template \"{template.Name}\"", Chosen = true }
                };
                if (e.ValueMap != null)
                    into.ValueMaps[e.TargetPath] = new Dictionary<string, string>(e.ValueMap, StringComparer.OrdinalIgnoreCase);
            }

            new MappingSuggester(contract).RecomputeUnmappedRequired(into);
        }

        private static string LabelFor(FieldContract contract, string path) =>
            contract.MappableFields.FirstOrDefault(f =>
                string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? path;

        /// <summary>
        /// Self-learning overlay: apply remembered assignments for headers this client has used before,
        /// WITHOUT wiping the fuzzy/AI suggestions for columns the memory doesn't know. Returns how many
        /// columns were filled from memory.
        /// </summary>
        public static int ApplyLearned(MappingTemplate template, SourceTable table, FieldContract contract, MappingResult into)
        {
            var headerLookup = new HashSet<string>(table.Headers, StringComparer.OrdinalIgnoreCase);
            int applied = 0;

            foreach (var e in template.Entries)
            {
                if (e.IsConstant)
                {
                    if (!into.Constants.ContainsKey(e.TargetPath))
                        into.Constants[e.TargetPath] = e.ConstantValue ?? string.Empty;
                    if (e.ValueMap != null && !into.ValueMaps.ContainsKey(e.TargetPath))
                        into.ValueMaps[e.TargetPath] = new Dictionary<string, string>(e.ValueMap, StringComparer.OrdinalIgnoreCase);
                    continue;
                }

                if (string.IsNullOrEmpty(e.SourceHeader) || !headerLookup.Contains(e.SourceHeader)) continue;
                var col = into.Columns.FirstOrDefault(c =>
                    string.Equals(c.SourceHeader, e.SourceHeader, StringComparison.OrdinalIgnoreCase));
                if (col == null) continue;

                col.TargetPath = e.TargetPath;
                col.Include = true;
                col.Source = MappingSource.Template;     // remembered = confirmed history, highest trust
                col.Confidence = MappingConfidence.High;
                col.Score = 1.0;
                col.Approved = true;                     // already confirmed on a prior upload
                string lLabel = LabelFor(contract, e.TargetPath);
                col.Rationale = $"Recalled from this client's history — \"{col.SourceHeader}\" was mapped to {lLabel} on a previous upload.";
                col.Candidates = new List<MappingCandidate>
                {
                    new MappingCandidate { Path = e.TargetPath, Label = lLabel, Score = 1.0, MatchedOn = "learned memory", Chosen = true }
                };
                if (e.ValueMap != null)
                    into.ValueMaps[e.TargetPath] = new Dictionary<string, string>(e.ValueMap, StringComparer.OrdinalIgnoreCase);
                applied++;
            }

            // Recall the client's saved rules (merge overlay runs before the form, so the suggested
            // result has no rules of its own yet).
            if (template.Rules.Count > 0) into.Rules = template.Rules.Select(CloneRule).ToList();

            new MappingSuggester(contract).RecomputeUnmappedRequired(into);
            return applied;
        }

        /// <summary>
        /// Fold the operator's confirmed mapping into the client's accumulating memory: existing remembered
        /// headers are updated, new ones added, and headers from previous files (not in this file) are kept.
        /// </summary>
        public static MappingTemplate LearnFrom(MappingResult confirmed, MappingTemplate? existing, string? clientId)
        {
            var t = existing ?? new MappingTemplate();
            t.IsAuto = true;
            t.ClientId = clientId;
            t.Id = TemplateStore.AutoId(clientId);
            if (string.IsNullOrWhiteSpace(t.Name) || t.Name == "Untitled")
                t.Name = "Auto-learned memory";

            // index existing entries so we upsert rather than duplicate
            var byHeader = t.Entries
                .Where(e => !e.IsConstant && !string.IsNullOrEmpty(e.SourceHeader))
                .ToDictionary(e => e.SourceHeader!, e => e, StringComparer.OrdinalIgnoreCase);

            foreach (var c in confirmed.Columns.Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath) && !string.IsNullOrEmpty(c.SourceHeader)))
            {
                if (!byHeader.TryGetValue(c.SourceHeader!, out var entry))
                {
                    entry = new TemplateEntry { SourceHeader = c.SourceHeader };
                    t.Entries.Add(entry);
                    byHeader[c.SourceHeader!] = entry;
                }
                entry.TargetPath = c.TargetPath!;
                entry.ConstantValue = null;
                if (confirmed.ValueMaps.TryGetValue(c.TargetPath!, out var vm) && vm.Count > 0)
                    entry.ValueMap = new Dictionary<string, string>(vm);
            }

            // refresh remembered constants (replace the whole constant set with the latest confirmation's)
            t.Entries.RemoveAll(e => e.IsConstant);
            foreach (var kv in confirmed.Constants)
                t.Entries.Add(new TemplateEntry { TargetPath = kv.Key, ConstantValue = kv.Value });

            // remember the operator's confirmed rules for next time
            t.Rules = confirmed.Rules.Select(CloneRule).ToList();

            return t;
        }
    }
}
