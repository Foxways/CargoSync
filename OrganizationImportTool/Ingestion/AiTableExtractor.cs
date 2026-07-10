using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrganizationImportTool.Ai;
using OrganizationImportTool.Logging;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Extracts a table from a PDF or image using the configured AI chain, with accuracy
    /// engineered in rather than hoped for: strict JSON-only output at temperature 0, one
    /// automatic re-prompt on malformed JSON, then an independent verification pass where
    /// the model re-reads the document against its own extraction and returns corrections.
    /// Construction takes a completer delegate so tests can run it without a live provider.
    /// </summary>
    public class AiTableExtractor
    {
        private readonly Func<AiRequest, CancellationToken, Task<AiResponse>> _complete;

        private const string ExtractSystem =
            "You convert documents into tables. Reply with ONLY a JSON object, no markdown fence, no commentary: " +
            "{\"headers\":[...],\"rows\":[[...],[...]]}. Rules: transcribe values EXACTLY as printed - do not " +
            "correct spelling, do not infer or fill in missing values; use \"\" for unreadable or empty cells; " +
            "every row array must have the same length as headers; ignore page furniture (titles, footers, page numbers).";

        private const string VerifySystem =
            "You verify a previous table extraction against the original document. Reply with ONLY a JSON object: " +
            "{\"row_count_ok\":true|false,\"corrections\":[{\"row\":<1-based>,\"header\":\"...\",\"value\":\"...\"}]}. " +
            "Report a correction ONLY when the document clearly shows a different value. An empty corrections " +
            "array means the extraction is faithful.";

        public AiTableExtractor(AiRouter router)
            : this((req, ct) => router.CompleteAsync(req, ct)) { }

        public AiTableExtractor(Func<AiRequest, CancellationToken, Task<AiResponse>> complete)
            => _complete = complete ?? throw new ArgumentNullException(nameof(complete));

        /// <summary>Extract a table from the attached document. Throws InvalidDataException when the AI chain cannot produce one.</summary>
        public async Task<SourceTable> ExtractAsync(AiAttachment attachment, string sourcePath, string sourceName,
            CancellationToken ct = default)
        {
            var extracted = await AskForTableAsync(attachment, ct).ConfigureAwait(false)
                ?? throw new System.IO.InvalidDataException(
                    "AI could not extract a table from this document. If it is a poor-quality scan, " +
                    "try a clearer copy, or supply the data as Excel/CSV.");

            await VerifyAsync(attachment, extracted, ct).ConfigureAwait(false);

            var acc = new TableAccumulator();
            foreach (var cells in extracted.Rows)
            {
                var row = acc.NewRecord();
                for (int k = 0; k < extracted.Headers.Count; k++)
                    acc.Set(row, extracted.Headers[k], k < cells.Count ? cells[k] : string.Empty);
            }
            return acc.Build(sourcePath, sourceName + " (AI extracted)");
        }

        private sealed class Extracted
        {
            public List<string> Headers = new();
            public List<List<string>> Rows = new();
        }

        private async Task<Extracted?> AskForTableAsync(AiAttachment attachment, CancellationToken ct)
        {
            string prompt = "Extract the table of records from the attached document.";
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var resp = await _complete(new AiRequest
                {
                    System = ExtractSystem,
                    Prompt = prompt,
                    Attachments = new List<AiAttachment> { attachment },
                    TemperatureOverride = 0,
                    MaxTokensOverride = 8192,
                    Operation = "extract-table"
                }, ct).ConfigureAwait(false);

                if (!resp.Success)
                {
                    AppLog.Warn($"AI table extraction failed: {resp.Error}");
                    return null; // chain exhausted - no point re-prompting
                }

                var parsed = ParseExtraction(resp.Text);
                if (parsed != null) return parsed;

                AppLog.Warn("AI extraction reply was not the required JSON shape; re-prompting once.");
                prompt = "Your previous reply was not a valid JSON object of the required shape. " +
                         "Extract the table again and reply with ONLY {\"headers\":[...],\"rows\":[[...]]}.";
            }
            return null;
        }

        private async Task VerifyAsync(AiAttachment attachment, Extracted extracted, CancellationToken ct)
        {
            try
            {
                string snapshot = JsonSerializer.Serialize(new { headers = extracted.Headers, rows = extracted.Rows });
                if (snapshot.Length > 60_000) // keep the verify prompt sane for very large tables
                {
                    AppLog.Warn("AI extraction verify pass skipped (table too large to restate).");
                    return;
                }

                var resp = await _complete(new AiRequest
                {
                    System = VerifySystem,
                    Prompt = "Previous extraction:\n" + snapshot + "\nVerify it against the attached document.",
                    Attachments = new List<AiAttachment> { attachment },
                    TemperatureOverride = 0,
                    MaxTokensOverride = 4096,
                    Operation = "verify-extraction"
                }, ct).ConfigureAwait(false);

                if (!resp.Success) return; // verification is best-effort; the extraction stands

                using var doc = JsonDocument.Parse(StripFence(resp.Text));
                if (!doc.RootElement.TryGetProperty("corrections", out var corrections) ||
                    corrections.ValueKind != JsonValueKind.Array) return;

                int applied = 0;
                foreach (var c in corrections.EnumerateArray())
                {
                    if (!c.TryGetProperty("row", out var rowEl) || !rowEl.TryGetInt32(out int rowNum)) continue;
                    if (!c.TryGetProperty("header", out var hEl) || !c.TryGetProperty("value", out var vEl)) continue;
                    int col = extracted.Headers.FindIndex(h => string.Equals(h, hEl.GetString(), StringComparison.OrdinalIgnoreCase));
                    if (col < 0 || rowNum < 1 || rowNum > extracted.Rows.Count) continue;
                    string correctedValue = vEl.GetString();
                    if (string.IsNullOrEmpty(correctedValue)) continue; // skip null/empty — don't blank a correct cell
                    extracted.Rows[rowNum - 1][col] = correctedValue;
                    applied++;
                }
                if (applied > 0)
                    AppLog.Warn($"AI extraction verify pass corrected {applied} cell(s).");
            }
            catch (Exception ex)
            {
                AppLog.Warn("AI extraction verify pass failed (extraction kept as-is)", ex);
            }
        }

        private static Extracted? ParseExtraction(string text)
        {
            try
            {
                using var doc = JsonDocument.Parse(StripFence(text));
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                if (!root.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array) return null;
                if (!root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array) return null;

                var result = new Extracted();
                foreach (var h in headers.EnumerateArray())
                    result.Headers.Add(h.ValueKind == JsonValueKind.String ? (h.GetString() ?? "") : h.GetRawText());
                if (result.Headers.Count == 0) return null;

                foreach (var r in rows.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Array) return null;
                    var cells = new List<string>();
                    foreach (var c in r.EnumerateArray())
                        cells.Add(c.ValueKind switch
                        {
                            JsonValueKind.String => c.GetString() ?? "",
                            JsonValueKind.Null => "",
                            _ => c.GetRawText()
                        });
                    result.Rows.Add(cells);
                }
                return result.Rows.Count == 0 ? null : result;
            }
            catch (JsonException) { return null; }
        }

        /// <summary>Tolerate the one formatting slip models still make: a ```json fence around the object.</summary>
        private static string StripFence(string text)
        {
            string t = text.Trim();
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = t.IndexOf('\n');
                int lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                    t = t.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }
            return t;
        }
    }
}
