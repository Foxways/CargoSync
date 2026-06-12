using System.Collections.Generic;

namespace OrganizationImportTool.Mapping
{
    public enum MappingConfidence
    {
        /// <summary>No reasonable target found - operator must map manually.</summary>
        Unmapped = 0,
        /// <summary>Weak guess - review carefully.</summary>
        Low = 1,
        /// <summary>Plausible - quick confirm.</summary>
        Medium = 2,
        /// <summary>Strong (exact label/alias) - likely correct.</summary>
        High = 3
    }

    /// <summary>How a suggestion was produced (so the UI can badge AI vs deterministic).</summary>
    public enum MappingSource
    {
        None = 0,
        ExactAlias = 1,
        Fuzzy = 2,
        Ai = 3,
        Manual = 4,
        Template = 5
    }

    /// <summary>One source column and the CargoWise field it is (proposed to be) mapped to.</summary>
    public class ColumnMapping
    {
        /// <summary>The header from the client's file.</summary>
        public string SourceHeader { get; set; } = string.Empty;

        /// <summary>A sample value from the first data row (helps the operator decide).</summary>
        public string SampleValue { get; set; } = string.Empty;

        /// <summary>Chosen target field path (null = leave unmapped / ignore this column).</summary>
        public string? TargetPath { get; set; }

        public MappingConfidence Confidence { get; set; } = MappingConfidence.Unmapped;
        public MappingSource Source { get; set; } = MappingSource.None;

        /// <summary>0..1 raw score behind the confidence tier (for sorting / display).</summary>
        public double Score { get; set; }

        /// <summary>Whether this column is included in the import (operator can untick noise columns).</summary>
        public bool Include { get; set; } = true;

        /// <summary>
        /// Human sign-off on an AI-proposed mapping. Deterministic/learned/manual matches are
        /// auto-approved; AI suggestions start false and must be approved by the operator.
        /// </summary>
        public bool Approved { get; set; } = true;

        /// <summary>Ranked alternative target paths, best first - feeds the override dropdown.</summary>
        public List<string> Alternatives { get; set; } = new List<string>();

        /// <summary>Plain-language reason this target was chosen (for the explainability panel).</summary>
        public string Rationale { get; set; } = string.Empty;

        /// <summary>Ranked fields the engine actually considered, with scores - the "why this and not that".</summary>
        public List<MappingCandidate> Candidates { get; set; } = new List<MappingCandidate>();
    }

    /// <summary>One field the matcher weighed for a column, with the score and what it matched on.</summary>
    public class MappingCandidate
    {
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>0..1 similarity behind this candidate.</summary>
        public double Score { get; set; }

        /// <summary>The label/alias text that produced the score (e.g. the alias "company name").</summary>
        public string MatchedOn { get; set; } = string.Empty;

        /// <summary>True for the candidate that became the chosen target.</summary>
        public bool Chosen { get; set; }
    }

    /// <summary>The full proposed mapping for a file, ready for the validation screen.</summary>
    public class MappingResult
    {
        public List<ColumnMapping> Columns { get; set; } = new List<ColumnMapping>();

        /// <summary>Required contract fields that no column currently maps to (blocks send until resolved).</summary>
        public List<ContractField> UnmappedRequired { get; set; } = new List<ContractField>();

        /// <summary>Fixed values applied to a target field for every row (target path -> constant).</summary>
        public Dictionary<string, string> Constants { get; set; }
            = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>Per-target value translations (target path -> (source value -> output value)), e.g. client code maps.</summary>
        public Dictionary<string, Dictionary<string, string>> ValueMaps { get; set; }
            = new Dictionary<string, Dictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);

        /// <summary>No-code IF-THEN rules applied per row before validation/send.</summary>
        public List<TransformRule> Rules { get; set; } = new List<TransformRule>();

        /// <summary>Apply a target's value-map (if any) to a raw cell value; pass-through when no map/match.</summary>
        public string ApplyValueMap(string targetPath, string raw)
        {
            if (ValueMaps.TryGetValue(targetPath, out var map) && map.TryGetValue(raw.Trim(), out var mapped))
                return mapped;
            return raw;
        }
    }
}
