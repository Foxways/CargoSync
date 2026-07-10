using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OrganizationImportTool.Pipeline;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>
    /// Writes the rows that need attention (blocked / rejected) back out in their ORIGINAL source
    /// columns plus Status + Reason, so the client can fix them and re-drop the file - the learned
    /// mapping picks the unchanged columns straight back up. Headless equivalent of the
    /// "export rows needing attention" button on the results screen.
    /// </summary>
    public static class FailedRowsCsv
    {
        /// <summary>Write the attention rows; returns the path, or null when there were none.</summary>
        public static string? Write(string path, PipelineResult result)
        {
            var rows = result.Outcomes.Where(ImportReport.NeedsAttention).ToList();
            if (rows.Count == 0) return null;

            var headers = result.SourceHeaders?.ToList() ?? new List<string>();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(Csv).Append(Csv("Status")).Append(Csv("Reason"))));

            foreach (var o in rows)
            {
                var cells = headers.Select(h => Csv(o.SourceRow != null ? o.SourceRow[h] : string.Empty)).ToList();
                cells.Add(Csv(o.Response.Outcome));
                cells.Add(Csv(o.Response.Error ?? o.Response.ProcessingLog));
                sb.AppendLine(string.Join(",", cells));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            return path;
        }

        /// <summary>RFC-4180 field: wrap in quotes when it contains a comma, quote or newline.</summary>
        private static string Csv(string? value)
        {
            string v = value ?? string.Empty;
            if (v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return v;
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        }
    }
}
