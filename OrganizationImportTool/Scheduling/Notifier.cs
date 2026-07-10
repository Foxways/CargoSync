using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>The result of attempting to send a notification email.</summary>
    public sealed class NotifyOutcome
    {
        public bool Sent { get; init; }
        public bool Skipped { get; init; }
        public string? Error { get; init; }

        public static NotifyOutcome Ok() => new() { Sent = true };
        public static NotifyOutcome Skip(string why) => new() { Skipped = true, Error = why };
        public static NotifyOutcome Fail(string why) => new() { Error = why };
    }

    /// <summary>
    /// Sends scheduled-import notification emails via Outlook / Office 365 SMTP. Message construction
    /// (<see cref="BuildMessage"/>) is pure and unit-tested; the network send supports both app-password
    /// (Basic) auth and O365 OAuth2 client-credentials. Notification failures are returned, never thrown,
    /// so a mail problem can't fail an otherwise-successful import.
    /// </summary>
    public sealed class Notifier
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>True when the job's trigger says this report warrants an email.</summary>
        public static bool ShouldSend(ScheduledJob job, ImportReport report) => job.NotifyOn switch
        {
            NotifyTrigger.Always => true,
            NotifyTrigger.OnFailure => !report.IsClean,
            _ => false
        };

        /// <summary>Split a comma/semicolon/whitespace-separated list into distinct valid addresses.</summary>
        public static List<string> ParseRecipients(params string?[] lists)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var raw in lists)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                foreach (var part in raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var addr = part.Trim();
                    // Require an @ with non-empty local + domain: MimeKit otherwise accepts a bare
                    // local-part ("not-an-email") as a valid mailbox.
                    int at = addr.IndexOf('@');
                    bool hasDomain = at > 0 && at < addr.Length - 1;
                    if (hasDomain && MailboxAddress.TryParse(addr, out _) && seen.Add(addr))
                        result.Add(addr);
                }
            }
            return result;
        }

        /// <summary>Build the notification message (no network). Returns null if there are no recipients.</summary>
        public static MimeMessage? BuildMessage(SmtpSettings smtp, ScheduledJob job, ImportReport report)
        {
            var recipients = ParseRecipients(job.NotifyClientEmails, job.NotifyInternalEmails);
            if (recipients.Count == 0) return null;

            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(smtp.FromDisplayName ?? "CargoSync", smtp.FromAddress));
            foreach (var to in recipients) msg.To.Add(MailboxAddress.Parse(to));
            msg.Subject = Subject(report);

            var body = new BodyBuilder { HtmlBody = HtmlBody(job, report), TextBody = TextBody(job, report) };

            if (job.AttachImportLog && Exists(report.ImportLogPath))
                body.Attachments.Add(report.ImportLogPath!);
            if (job.AttachFailedRowsCsv && Exists(report.FailedRowsCsvPath))
                body.Attachments.Add(report.FailedRowsCsvPath!);

            msg.Body = body.ToMessageBody();
            return msg;
        }

        /// <summary>Build (if warranted) and send the report. Never throws.</summary>
        public async Task<NotifyOutcome> SendReportAsync(SmtpSettings smtp, ScheduledJob job, ImportReport report, CancellationToken ct = default)
        {
            try
            {
                if (!ShouldSend(job, report)) return NotifyOutcome.Skip("Trigger condition not met.");
                if (!smtp.IsConfigured(out var cfgErr)) return NotifyOutcome.Skip(cfgErr);

                var msg = BuildMessage(smtp, job, report);
                if (msg == null) return NotifyOutcome.Skip("No recipients configured.");

                await SendAsync(smtp, msg, ct).ConfigureAwait(false);
                return NotifyOutcome.Ok();
            }
            catch (Exception ex)
            {
                Logging.AppLog.Warn($"Notification email failed for job '{job.Name}'", ex);
                return NotifyOutcome.Fail(ex.Message);
            }
        }

        /// <summary>Send a one-line test email (used by the SMTP settings screen).</summary>
        public async Task<NotifyOutcome> SendTestAsync(SmtpSettings smtp, string toAddress, CancellationToken ct = default)
        {
            try
            {
                if (!smtp.IsConfigured(out var cfgErr)) return NotifyOutcome.Fail(cfgErr);
                if (!MailboxAddress.TryParse(toAddress, out _)) return NotifyOutcome.Fail("Invalid test recipient address.");

                var msg = new MimeMessage();
                msg.From.Add(new MailboxAddress(smtp.FromDisplayName ?? "CargoSync", smtp.FromAddress));
                msg.To.Add(MailboxAddress.Parse(toAddress));
                msg.Subject = "[CargoSync] Test email";
                msg.Body = new TextPart("plain") { Text = "This is a test from CargoSync. Your email notification settings work." };

                await SendAsync(smtp, msg, ct).ConfigureAwait(false);
                return NotifyOutcome.Ok();
            }
            catch (Exception ex)
            {
                return NotifyOutcome.Fail(ex.Message);
            }
        }

        private async Task SendAsync(SmtpSettings smtp, MimeMessage msg, CancellationToken ct)
        {
            using var client = new SmtpClient();
            var socket = smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
            await client.ConnectAsync(smtp.Host, smtp.Port, socket, ct).ConfigureAwait(false);

            if (smtp.AuthMode == SmtpAuthMode.OAuth2)
            {
                string token = await AcquireOAuth2TokenAsync(smtp, ct).ConfigureAwait(false);
                var user = string.IsNullOrWhiteSpace(smtp.Username) ? smtp.FromAddress : smtp.Username;
                await client.AuthenticateAsync(new SaslMechanismOAuth2(user, token), ct).ConfigureAwait(false);
            }
            else
            {
                await client.AuthenticateAsync(smtp.Username, smtp.Password, ct).ConfigureAwait(false);
            }

            await client.SendAsync(msg, ct).ConfigureAwait(false);
            await client.DisconnectAsync(true, ct).ConfigureAwait(false);
        }

        /// <summary>Office 365 client-credentials token for SMTP (scope outlook.office365.com/.default).</summary>
        private static async Task<string> AcquireOAuth2TokenAsync(SmtpSettings smtp, CancellationToken ct)
        {
            string url = $"https://login.microsoftonline.com/{smtp.TenantId}/oauth2/v2.0/token";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", smtp.ClientIdOAuth),
                new KeyValuePair<string, string>("client_secret", smtp.ClientSecret),
                new KeyValuePair<string, string>("scope", "https://outlook.office365.com/.default"),
            });
            using var resp = await Http.PostAsync(url, content, ct).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"OAuth2 token request failed (HTTP {(int)resp.StatusCode}): {json}");

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tok) && tok.GetString() is { Length: > 0 } t)
                return t;
            throw new InvalidOperationException("OAuth2 token response did not contain an access_token.");
        }

        // ---- message text ----

        public static string Subject(ImportReport r)
        {
            string scope = $"{r.ClientName}: {r.FileName}";
            if (!string.IsNullOrEmpty(r.Error)) return $"[CargoSync] FAILED — {scope}";
            if (r.DryRun) return $"[CargoSync] Dry run — {scope} ({r.WouldSend} would send, {r.Blocked} blocked)";
            return $"[CargoSync] {scope} — {r.Ok} sent, {r.Blocked} blocked, {r.SkippedDuplicates} duplicate";
        }

        private static string HtmlBody(ScheduledJob job, ImportReport r)
        {
            var sb = new StringBuilder();
            sb.Append("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#222\">");
            sb.Append($"<h2 style=\"margin:0 0 4px\">{Enc(r.ClientName)} — import {(r.IsClean ? "completed" : "needs attention")}</h2>");
            sb.Append($"<p style=\"margin:0 0 12px;color:#666\">Job “{Enc(job.Name)}” · file <b>{Enc(r.FileName)}</b>{(r.DryRun ? " · DRY RUN" : "")}</p>");

            if (!string.IsNullOrEmpty(r.Error))
                sb.Append($"<p style=\"color:#b00020\"><b>Run error:</b> {Enc(r.Error)}</p>");

            sb.Append("<table cellpadding=\"6\" style=\"border-collapse:collapse;margin:8px 0\">");
            Row(sb, "Rows processed", r.Total.ToString());
            Row(sb, r.DryRun ? "Would send" : "Sent OK", (r.DryRun ? r.WouldSend : r.Ok).ToString());
            Row(sb, "Blocked (validation/rejected)", r.Blocked.ToString(), r.Blocked > 0);
            Row(sb, "Skipped (duplicate)", r.SkippedDuplicates.ToString());
            Row(sb, "Skipped (already imported)", r.AlreadyImported.ToString());
            if (r.Warnings > 0) Row(sb, "Warnings", r.Warnings.ToString());
            Row(sb, "Duration", $"{r.Elapsed.TotalSeconds:0.0}s");
            sb.Append("</table>");

            if (r.Failures.Count > 0)
            {
                sb.Append("<h3 style=\"margin:14px 0 4px\">Rows that need attention</h3>");
                sb.Append("<table cellpadding=\"6\" style=\"border-collapse:collapse;border:1px solid #ddd\">");
                sb.Append("<tr style=\"background:#f4f4f4\"><th align=\"left\">Row</th><th align=\"left\">Code</th><th align=\"left\">Reason</th></tr>");
                foreach (var f in r.Failures.Take(50))
                    sb.Append($"<tr><td>{f.RowNumber}</td><td>{Enc(f.Code)}</td><td>{Enc(f.Reason)}</td></tr>");
                sb.Append("</table>");
                if (r.Failures.Count > 50)
                    sb.Append($"<p style=\"color:#666\">…and {r.Failures.Count - 50} more (see the attached log / CSV).</p>");
            }

            sb.Append("<p style=\"color:#999;font-size:12px;margin-top:16px\">Sent automatically by CargoSync.</p>");
            sb.Append("</div>");
            return sb.ToString();
        }

        private static void Row(StringBuilder sb, string label, string value, bool warn = false)
        {
            string colour = warn ? "#b00020" : "#222";
            sb.Append($"<tr><td style=\"color:#666\">{Enc(label)}</td><td style=\"color:{colour};font-weight:600\">{Enc(value)}</td></tr>");
        }

        private static string TextBody(ScheduledJob job, ImportReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"{r.ClientName} — import {(r.IsClean ? "completed" : "needs attention")}");
            sb.AppendLine($"Job: {job.Name}");
            sb.AppendLine($"File: {r.FileName}{(r.DryRun ? " (DRY RUN)" : "")}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(r.Error)) sb.AppendLine($"RUN ERROR: {r.Error}").AppendLine();
            sb.AppendLine($"Rows processed : {r.Total}");
            sb.AppendLine($"{(r.DryRun ? "Would send" : "Sent OK")}     : {(r.DryRun ? r.WouldSend : r.Ok)}");
            sb.AppendLine($"Blocked        : {r.Blocked}");
            sb.AppendLine($"Duplicate skip : {r.SkippedDuplicates}");
            sb.AppendLine($"Already import : {r.AlreadyImported}");
            if (r.Warnings > 0) sb.AppendLine($"Warnings       : {r.Warnings}");
            sb.AppendLine($"Duration       : {r.Elapsed.TotalSeconds:0.0}s");
            if (r.Failures.Count > 0)
            {
                sb.AppendLine().AppendLine("Rows that need attention:");
                foreach (var f in r.Failures.Take(50))
                    sb.AppendLine($"  row {f.RowNumber} [{f.Code}]: {f.Reason}");
                if (r.Failures.Count > 50) sb.AppendLine($"  ...and {r.Failures.Count - 50} more.");
            }
            sb.AppendLine().AppendLine("Sent automatically by CargoSync.");
            return sb.ToString();
        }

        private static bool Exists(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        private static string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? string.Empty);
    }
}
