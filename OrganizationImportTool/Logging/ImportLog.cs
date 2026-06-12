using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OrganizationImportTool.Eadaptor;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Logging
{
    /// <summary>
    /// Writes one detailed, human-readable audit log per import run into the client's configured
    /// log folder. Captures the run context, the confirmed mapping, every organization's outcome
    /// (created / updated / rejected / not-sent) with CargoWise's processing log and message
    /// number, validation warnings, and a final summary - a complete record of what happened.
    /// </summary>
    public sealed class ImportLog : IDisposable
    {
        private readonly StreamWriter? _w;
        public string FilePath { get; } = string.Empty;
        public bool Ok => _w != null;

        public ImportLog(string logDir, string clientName)
        {
            try
            {
                Directory.CreateDirectory(logDir);
                string ts = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string safeClient = string.Concat((clientName ?? "client").Split(Path.GetInvalidFileNameChars()));
                FilePath = Path.Combine(logDir, $"IMPORT_{safeClient}_{ts}.log");
                _w = new StreamWriter(FilePath, append: false) { AutoFlush = true };
            }
            catch { _w = null; }
        }

        private void Line(string s = "") => _w?.WriteLine(s);
        private void Rule() => Line(new string('=', 78));

        public void Header(string client, string environment, string url, string senderId, string filePath, int rowCount, string performedBy = "")
        {
            Rule();
            Line("  CARGOWISE ORGANIZATION IMPORT - DETAILED LOG");
            Rule();
            Line($"  Started      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Line($"  Performed by : {performedBy}");
            Line($"  Client       : {client}");
            Line($"  Environment  : {environment}");
            Line($"  eAdaptor URL : {url}");
            Line($"  Sender/User  : {senderId}");
            Line($"  Source file  : {filePath}");
            Line($"  Rows to send : {rowCount}");
            Rule();
            Line();
        }

        public void Mapping(IEnumerable<ColumnMapping> columns, IReadOnlyDictionary<string, string> constants)
        {
            Line("CONFIRMED FIELD MAPPING");
            Line(new string('-', 78));
            foreach (var c in columns.Where(c => c.Include && !string.IsNullOrEmpty(c.TargetPath)))
                Line($"  {c.SourceHeader,-28} ->  {c.TargetPath}   [{c.Confidence}/{c.Source}]");
            if (constants.Count > 0)
            {
                Line("  -- constants / defaults --");
                foreach (var kv in constants)
                    Line($"  (constant){"",-19} ->  {kv.Key} = {kv.Value}");
            }
            Line();
            Line("PER-ORGANIZATION RESULTS");
            Line(new string('-', 78));
        }

        public void Row(int index, OrgSendOutcome o, IEnumerable<string> warnings)
        {
            var r = o.Response;
            string verdict = r.NotSent ? "NOT SENT (validation)"
                : r.IsSuccess ? $"OK - {r.Outcome}"
                : r.IsWarning ? "WARNING"
                : "REJECTED";

            Line($"[{index}] {o.SentCode}   =>   {verdict}");
            if (!string.IsNullOrEmpty(r.LocalCode) && r.LocalCode != o.SentCode)
                Line($"      CargoWise code : {r.LocalCode}");
            if (!string.IsNullOrEmpty(r.MessageNumber))
                Line($"      Message number : {r.MessageNumber}");
            if (r.HttpStatus != 0)
                Line($"      HTTP status    : {r.HttpStatus}");

            foreach (var w in warnings)
                Line($"      ! warning      : {w}");

            if (!string.IsNullOrWhiteSpace(r.ProcessingLog))
            {
                Line("      Processing log :");
                foreach (var ln in r.ProcessingLog.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(ln)) Line($"        {ln.Trim()}");
            }
            if (!r.IsSuccess && !string.IsNullOrEmpty(r.Error))
                Line($"      Error          : {r.Error}");

            // Include the exact XML we sent for any non-success row, so failures are reproducible.
            if (!r.IsSuccess && !string.IsNullOrEmpty(o.SentXml))
            {
                Line("      Sent XML       :");
                foreach (var ln in o.SentXml.Split('\n'))
                    Line($"        {ln.TrimEnd()}");
            }
            Line();
        }

        public void Summary(int total, int ok, int warnings, int notSent, int rejected, TimeSpan elapsed)
        {
            Rule();
            Line("  SUMMARY");
            Rule();
            Line($"  Total organizations : {total}");
            Line($"  Succeeded (PRS)     : {ok}");
            Line($"  Warnings (WRN)      : {warnings}");
            Line($"  Rejected (ERR)      : {rejected}");
            Line($"  Not sent (invalid)  : {notSent}");
            Line($"  Duration            : {elapsed:hh\\:mm\\:ss}");
            Line($"  Finished            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Rule();
        }

        public void Note(string message) => Line(message);

        public void Dispose() => _w?.Dispose();
    }
}
