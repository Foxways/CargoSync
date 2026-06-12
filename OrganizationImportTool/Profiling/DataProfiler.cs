using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;
using OrganizationImportTool.Validation;

namespace OrganizationImportTool.Profiling
{
    public enum RiskLevel { Low, Medium, High }

    /// <summary>Profile of one mapped target field across all rows.</summary>
    public class FieldProfile
    {
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Filled { get; set; }
        public int Total { get; set; }
        public double FillRate => Total == 0 ? 0 : (double)Filled / Total;
        public int Distinct { get; set; }
        public string Sample { get; set; } = string.Empty;
        public int MaxLen { get; set; }
        public bool Required { get; set; }
        public string Note { get; set; } = string.Empty;
    }

    /// <summary>The pre-flight data-health report shown before an import.</summary>
    public class ProfileReport
    {
        public int RowCount { get; set; }
        public int MappedFieldCount { get; set; }
        public List<FieldProfile> Fields { get; set; } = new();

        public int BlockingRows { get; set; }   // rows missing a required field (will not send)
        public int DuplicateRows { get; set; }
        public int WarningRows { get; set; }
        public int CleaningFixes { get; set; }
        public int AlreadySynced { get; set; }   // rows whose code is already in CargoWise for this client

        public RiskLevel Level { get; set; }
        public int Score { get; set; }          // 0 (clean) .. 100 (high risk)
        public List<string> Factors { get; set; } = new();
    }

    /// <summary>
    /// Builds a pre-flight profile + risk score from the mapped values: per-field fill rates and
    /// distinct counts, plus an overall risk grade driven by missing required fields, duplicates,
    /// validation warnings and pending cleaning fixes. Read-only - it informs, it doesn't change data.
    /// </summary>
    public class DataProfiler
    {
        public ProfileReport Profile(IReadOnlyList<RowValues> rows, FieldContract contract,
                                     int duplicateRows, int cleaningFixes, int alreadySynced = 0)
        {
            int total = rows.Count;
            var report = new ProfileReport
            {
                RowCount = total,
                DuplicateRows = duplicateRows,
                CleaningFixes = cleaningFixes,
                AlreadySynced = alreadySynced
            };

            // Every target path present anywhere in the data.
            var paths = rows.SelectMany(r => r.Values.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            report.MappedFieldCount = paths.Count;

            foreach (var path in paths)
            {
                var field = contract.FindByPath(path)
                            ?? contract.FindByPath(System.Text.RegularExpressions.Regex.Replace(path, @"\[\d+\]", "[]"));
                var vals = rows.Select(r => r.Values.TryGetValue(path, out var v) ? v : "")
                               .Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
                var fp = new FieldProfile
                {
                    Path = path,
                    Label = field?.DisplayName ?? path,
                    Total = total,
                    Filled = vals.Count,
                    Distinct = vals.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    Sample = vals.FirstOrDefault() ?? "",
                    MaxLen = vals.Count == 0 ? 0 : vals.Max(v => v.Trim().Length),
                    Required = field?.Required ?? false
                };
                var notes = new List<string>();
                if (fp.Required && fp.Filled < total) notes.Add($"{total - fp.Filled} missing (required)");
                if (field?.MaxLength is int max && fp.MaxLen > max) notes.Add($"exceeds {max} chars");
                if (!fp.Required && total > 0 && fp.FillRate < 0.5) notes.Add("sparsely filled");
                fp.Note = string.Join("; ", notes);
                report.Fields.Add(fp);
            }

            report.Fields = report.Fields
                .OrderByDescending(f => f.Required)
                .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Rows blocked by a missing required field.
            var blocking = new HashSet<int>();
            foreach (var rf in contract.RequiredFields)
                foreach (var r in rows)
                    if (!r.Values.TryGetValue(rf.Path, out var v) || string.IsNullOrWhiteSpace(v))
                        blocking.Add(r.RowNumber);
            report.BlockingRows = blocking.Count;

            // Rows that would carry a validation warning (length / enum / no-address).
            var validator = new OrgValidator(contract);
            report.WarningRows = rows.Count(r => validator.Validate(r.Values).Warnings.Any());

            // ---- risk scoring ----
            double Frac(int n) => total == 0 ? 0 : (double)n / total;
            double score = Frac(report.BlockingRows) * 60
                         + Frac(report.DuplicateRows) * 20
                         + Frac(report.WarningRows) * 12
                         + (cleaningFixes > 0 ? Math.Min(8, Frac(cleaningFixes) * 8) : 0);
            report.Score = (int)Math.Round(Math.Min(100, score));

            report.Level =
                report.BlockingRows > 0 ? RiskLevel.High :
                (report.DuplicateRows > 0 || report.WarningRows > 0 || report.AlreadySynced > 0 || report.Score >= 20) ? RiskLevel.Medium :
                RiskLevel.Low;

            // ---- top risk factors (human-readable) ----
            var factors = new List<string>();
            if (report.BlockingRows > 0)
                factors.Add($"{report.BlockingRows} row(s) missing a required field — these will NOT be sent.");
            foreach (var rf in contract.RequiredFields)
            {
                int miss = rows.Count(r => !r.Values.TryGetValue(rf.Path, out var v) || string.IsNullOrWhiteSpace(v));
                if (miss > 0) factors.Add($"{rf.Label}: empty on {miss} of {total} row(s).");
            }
            if (report.AlreadySynced > 0) factors.Add($"{report.AlreadySynced} row(s) already imported to CargoWise before — re-import will update them.");
            if (report.DuplicateRows > 0) factors.Add($"{report.DuplicateRows} likely duplicate row(s) detected.");
            if (report.CleaningFixes > 0) factors.Add($"{report.CleaningFixes} value(s) need cleaning before send.");
            if (report.WarningRows > 0) factors.Add($"{report.WarningRows} row(s) have length/enum warnings.");
            foreach (var fp in report.Fields.Where(f => !f.Required && f.FillRate < 0.5).Take(4))
                factors.Add($"{fp.Label}: only {fp.FillRate:P0} filled.");
            if (factors.Count == 0) factors.Add("No significant risks detected — this file looks clean.");
            report.Factors = factors;

            return report;
        }
    }
}
