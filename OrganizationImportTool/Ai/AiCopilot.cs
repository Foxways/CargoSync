using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Ai
{
    /// <summary>
    /// The in-app AI assistant ("CargoSync Copilot"). Answers operator questions about the loaded
    /// file, the current mapping, risks and rules - grounded with a compact context block so the
    /// model talks about THIS import, not generic advice.
    /// </summary>
    public class AiCopilot
    {
        private readonly AiRouter _router;
        public AiCopilot(AiRouter router) => _router = router;

        public const string SystemPrompt =
            "You are CargoSync Copilot, an expert assistant embedded in an app that imports organizations " +
            "into CargoWise via eAdaptor. Help the operator understand and fix their column mapping, validation, " +
            "duplicates, data cleaning and IF-THEN rules. Be concise, concrete and friendly - a few sentences or a " +
            "short list, not essays. When asked which CargoWise field a column belongs to, reply with the exact field " +
            "PATH from the provided list. When suggesting a rule, write it as: If <column> <condition> <value> then set " +
            "<field path> = <value>. Only ever reference field paths that appear in the provided context. If something " +
            "isn't answerable from the context, say so briefly.";

        /// <summary>Build the grounding context from the current mapping screen state.</summary>
        public static string BuildContext(FieldContract contract, SourceTable table, MappingResult result)
        {
            var first = table.Rows.FirstOrDefault();
            var sb = new StringBuilder();
            sb.AppendLine($"FILE: {table.SourceName} — {table.RowCount} rows, {table.ColumnCount} columns.");
            sb.AppendLine("YOUR COLUMNS (header = first sample value):");
            foreach (var h in table.Headers)
                sb.AppendLine($"- {h} = {Trunc(first != null ? first[h] : "", 40)}");

            sb.AppendLine();
            sb.AppendLine("CURRENT MAPPING (your column -> CargoWise field [confidence/source]):");
            foreach (var c in result.Columns.Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath)))
                sb.AppendLine($"- {c.SourceHeader} -> {c.TargetPath} [{c.Confidence}/{c.Source}]");
            var ignored = result.Columns.Where(c => !c.Include || string.IsNullOrEmpty(c.TargetPath)).Select(c => c.SourceHeader).ToList();
            if (ignored.Count > 0) sb.AppendLine("UNMAPPED/IGNORED columns: " + string.Join(", ", ignored));

            sb.AppendLine("UNMAPPED REQUIRED FIELDS: " +
                (result.UnmappedRequired.Count == 0 ? "none" : string.Join(", ", result.UnmappedRequired.Select(f => f.Label))));
            if (result.Constants.Count > 0)
                sb.AppendLine("CONSTANTS: " + string.Join(", ", result.Constants.Select(kv => $"{kv.Key}={kv.Value}")));
            if (result.Rules.Count > 0)
                sb.AppendLine("RULES: " + string.Join(" | ", result.Rules.Select(r => r.Describe())));

            sb.AppendLine();
            sb.AppendLine("AVAILABLE CARGOWISE FIELDS (path = label):");
            foreach (var f in contract.MappableFields)
                sb.AppendLine($"- {f.Path} = {f.DisplayName}{(f.Required ? " (required)" : "")}");

            return sb.ToString();
        }

        /// <summary>Ask a question; <paramref name="history"/> is prior (operator, copilot) turns.</summary>
        public async Task<string> AskAsync(string question, string context,
            IReadOnlyList<(string role, string text)> history, CancellationToken ct = default)
        {
            if (!_router.IsConfigured) return "AI isn't configured yet — add a provider in AI Settings.";

            var sb = new StringBuilder();
            sb.AppendLine("CONTEXT FOR THIS IMPORT:");
            sb.AppendLine(context);
            sb.AppendLine();
            if (history.Count > 0)
            {
                sb.AppendLine("CONVERSATION SO FAR:");
                foreach (var (role, text) in history.TakeLast(6))
                    sb.AppendLine($"{(role == "user" ? "Operator" : "Copilot")}: {text}");
                sb.AppendLine();
            }
            sb.AppendLine($"Operator: {question}");
            sb.AppendLine("Copilot:");

            try
            {
                var resp = await _router.CompleteAsync(new AiRequest
                {
                    System = SystemPrompt,
                    Prompt = sb.ToString(),
                    Operation = "copilot-chat",
                    MaxTokensOverride = 700
                }, ct).ConfigureAwait(false);

                if (!resp.Success || string.IsNullOrWhiteSpace(resp.Text))
                    return "Sorry — I couldn't reach the AI just now. " + (resp.Error ?? "");
                return resp.Text.Trim();
            }
            catch (Exception ex)
            {
                return "Sorry — something went wrong: " + ex.Message;
            }
        }

        private static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
    }
}
