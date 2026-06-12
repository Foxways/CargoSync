using System;
using System.Collections.Generic;
using System.Linq;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Validation
{
    public class ValidationIssue
    {
        public string Label { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool IsError { get; set; }
        public override string ToString() => $"{(IsError ? "ERROR" : "WARN")}: {Label} - {Message}";
    }

    public class ValidationReport
    {
        public List<ValidationIssue> Issues { get; } = new();
        public bool HasErrors => Issues.Any(i => i.IsError);
        public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.IsError);
        public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => !i.IsError);

        public string ErrorText => string.Join("; ", Errors.Select(i => $"{i.Label}: {i.Message}"));
        public string AllText => string.Join("\n", Issues.Select(i => i.ToString()));
    }

    /// <summary>
    /// Validates one organization's mapped values against the contract BEFORE it is sent, so
    /// definitely-broken rows (missing required fields) are reported instead of being POSTed to
    /// CargoWise. Uncertain checks (enum/length) are warnings only - they never block a send.
    /// </summary>
    public class OrgValidator
    {
        private readonly FieldContract _contract;

        public OrgValidator(FieldContract contract) => _contract = contract;

        public ValidationReport Validate(IDictionary<string, string> values)
        {
            var report = new ValidationReport();

            // 1) Required fields must have a non-empty value (ERROR - blocks send).
            foreach (var rf in _contract.RequiredFields)
            {
                if (!values.TryGetValue(rf.Path, out var v) || string.IsNullOrWhiteSpace(v))
                    report.Issues.Add(new ValidationIssue { Label = rf.Label, Message = "required value is missing", IsError = true });
            }

            // 2) Per-value checks (WARNINGS - informational, never block).
            foreach (var kv in values)
            {
                if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                var field = _contract.FindByPath(kv.Key)
                            ?? _contract.FindByPath(System.Text.RegularExpressions.Regex.Replace(kv.Key, @"\[\d+\]", "[]"));
                if (field == null) continue;

                if (field.MaxLength is int max && kv.Value.Trim().Length > max)
                    report.Issues.Add(new ValidationIssue
                    {
                        Label = field.Label,
                        Message = $"value longer than {max} chars - will be truncated",
                        IsError = false
                    });

                var allowed = _contract.AllowedEnumCodes(field);
                if (allowed is { Count: > 0 } &&
                    !allowed.Contains(kv.Value.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    report.Issues.Add(new ValidationIssue
                    {
                        Label = field.Label,
                        Message = $"'{kv.Value.Trim()}' is not a known code ({string.Join("/", allowed)})",
                        IsError = false
                    });
                }
            }

            // 3) An organization should carry at least one address line (WARNING).
            bool hasAddress = values.Keys.Any(k =>
                k.StartsWith("orgAddressCollection", StringComparison.OrdinalIgnoreCase) &&
                (k.EndsWith(".address1", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".city", StringComparison.OrdinalIgnoreCase)));
            if (!hasAddress)
                report.Issues.Add(new ValidationIssue { Label = "Address", Message = "no address mapped", IsError = false });

            return report;
        }
    }
}
