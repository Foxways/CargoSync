using System;
using System.Collections.Generic;

namespace OrganizationImportTool.Mapping
{
    /// <summary>One field assignment in a template: from a source column, a constant, or both with a value-map.</summary>
    public class TemplateEntry
    {
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>Source column header to read from (null/empty when this entry is a constant).</summary>
        public string? SourceHeader { get; set; }

        /// <summary>Fixed value applied to every row (used when SourceHeader is empty).</summary>
        public string? ConstantValue { get; set; }

        /// <summary>Optional source-value -> output-value translation (client code maps / simple conditionals).</summary>
        public Dictionary<string, string>? ValueMap { get; set; }

        public bool IsConstant => string.IsNullOrEmpty(SourceHeader) && ConstantValue != null;
    }

    /// <summary>
    /// A saved, reusable mapping for a client's file layout. Global (ClientId empty) or
    /// scoped to one client. Captures column-&gt;field assignments plus constants and value-maps,
    /// so the second file from the same client is one click.
    /// </summary>
    public class MappingTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Untitled";

        /// <summary>Empty/null = global template (available to all clients).</summary>
        public string? ClientId { get; set; }

        /// <summary>True for the per-client auto-learned memory (hidden from the manual picker).</summary>
        public bool IsAuto { get; set; }

        public string Description { get; set; } = string.Empty;
        public string SavedUtc { get; set; } = string.Empty; // ISO timestamp, stamped on save
        public List<TemplateEntry> Entries { get; set; } = new List<TemplateEntry>();

        /// <summary>No-code IF-THEN rules saved with this template.</summary>
        public List<TransformRule> Rules { get; set; } = new List<TransformRule>();

        public bool IsGlobal => string.IsNullOrEmpty(ClientId);
        public string ScopeLabel => IsGlobal ? "Global" : "Client";
    }
}
