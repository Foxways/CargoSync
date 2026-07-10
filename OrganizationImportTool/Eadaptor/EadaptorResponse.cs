using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace OrganizationImportTool.Eadaptor
{
    /// <summary>
    /// Parsed result of one organization submission to eAdaptor. CargoWise replies with a
    /// &lt;UniversalResponse&gt; whose Status is PRS (processed), WRN (warning) or ERR (error),
    /// plus a ProcessingLog and (on success) an event describing the created/updated entity.
    /// </summary>
    public class EadaptorResponse
    {
        public bool TransportOk { get; set; }
        public int HttpStatus { get; set; }
        public string RawResponse { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;       // PRS / WRN / ERR
        public string ProcessingLog { get; set; } = string.Empty;
        public string MessageNumber { get; set; } = string.Empty;

        // From the response Event context (when processed)
        public string ExternalCode { get; set; } = string.Empty;  // the Code we sent
        public string LocalCode { get; set; } = string.Empty;     // the Code CargoWise stored (may be generated)
        public string EntityPk { get; set; } = string.Empty;
        public string EntityName { get; set; } = string.Empty;

        public string? Error { get; set; }

        /// <summary>True when the org was never sent (failed local validation before transport).</summary>
        public bool NotSent { get; set; }

        /// <summary>True for a dry-run preview row: built + validated locally but never transmitted.</summary>
        public bool Simulated { get; set; }

        public bool IsSuccess => TransportOk && !NotSent && Status.Equals("PRS", StringComparison.OrdinalIgnoreCase);
        public bool IsWarning => TransportOk && !NotSent && Status.Equals("WRN", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dry-run row that passed local validation and is ready to send.</summary>
        public bool IsSimulatedOk => Simulated && !NotSent;

        /// <summary>True when the row was skipped as a duplicate of another row in the same file.</summary>
        public bool IsDuplicate => Status.Equals("DUP", StringComparison.OrdinalIgnoreCase);

        /// <summary>One-line outcome for grids/logs.</summary>
        public string Outcome =>
            IsAlreadyImported ? "Skipped (already imported)" :
            IsDuplicate ? (Simulated ? "Would skip (duplicate)" : "Skipped (duplicate)") :
            Simulated ? (NotSent ? "Would NOT send (validation)" : "Would send ✓ (preview)") :
            NotSent ? "Not sent (validation)" :
            !TransportOk ? "Transport error" :
            IsSuccess ? (string.IsNullOrEmpty(LocalCode) || LocalCode == ExternalCode ? "Created/Updated" : $"Created as {LocalCode}") :
            IsWarning ? "Warning" : "Rejected";

        public static EadaptorResponse ValidationFailed(string error) =>
            new EadaptorResponse { NotSent = true, TransportOk = false, Status = "ERR", Error = error, ProcessingLog = error };

        /// <summary>A row skipped because it duplicates an earlier row in the same file.</summary>
        public static EadaptorResponse SkippedDuplicate(string reason) =>
            new EadaptorResponse { NotSent = true, TransportOk = false, Status = "DUP", Error = reason, ProcessingLog = reason };

        /// <summary>True when the row was skipped because it was already imported on a previous run.</summary>
        public bool IsAlreadyImported => Status.Equals("SKP", StringComparison.OrdinalIgnoreCase);

        /// <summary>A row skipped because the sync ledger shows it was already imported successfully.</summary>
        public static EadaptorResponse SkippedAlreadyImported(string reason) =>
            new EadaptorResponse { NotSent = true, TransportOk = false, Status = "SKP", Error = reason, ProcessingLog = reason };

        /// <summary>A dry-run row that validated cleanly and would be sent for real.</summary>
        public static EadaptorResponse SimulatedOk(string code) =>
            new EadaptorResponse
            {
                Simulated = true, TransportOk = false, Status = "SIM", ExternalCode = code,
                ProcessingLog = "Built and validated locally. Ready to send — dry run, nothing was transmitted."
            };

        public static EadaptorResponse FromXml(string xml, int httpStatus, bool transportOk)
        {
            var r = new EadaptorResponse { RawResponse = xml, HttpStatus = httpStatus, TransportOk = transportOk };
            if (string.IsNullOrWhiteSpace(xml)) { r.Error = "Empty response."; return r; }

            try
            {
                var doc = XDocument.Parse(xml);
                XNamespace u = "http://www.cargowise.com/Schemas/Universal/2011/11";

                r.Status = doc.Descendants(u + "Status").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                r.ProcessingLog = doc.Descendants(u + "ProcessingLog").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                r.MessageNumber = doc.Descendants(u + "MessageNumber").FirstOrDefault()?.Value?.Trim() ?? string.Empty;

                foreach (var ctx in doc.Descendants(u + "Context"))
                {
                    string type = ctx.Element(u + "Type")?.Value ?? string.Empty;
                    string val = ctx.Element(u + "Value")?.Value ?? string.Empty;
                    switch (type)
                    {
                        case "EntityExternalCode": r.ExternalCode = val; break;
                        case "EntityLocalCode": r.LocalCode = val; break;
                        case "EntityPrimaryKey": r.EntityPk = val; break;
                        case "NativeEntityName": r.EntityName = val; break;
                    }
                }

                if (!r.IsSuccess && string.IsNullOrEmpty(r.Error))
                    r.Error = FirstErrorLine(r.ProcessingLog);
            }
            catch (Exception ex)
            {
                r.Error = "Could not parse response: " + ex.Message;
            }
            return r;
        }

        private static string FirstErrorLine(string log)
        {
            if (string.IsNullOrEmpty(log)) return "Unknown error";
            var line = log.Split('\n').FirstOrDefault(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase));
            return (line ?? log).Trim();
        }
    }
}
