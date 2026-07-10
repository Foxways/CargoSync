using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrganizationImportTool.Security;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>How CargoSync authenticates to the outgoing mail server.</summary>
    public enum SmtpAuthMode
    {
        /// <summary>Username + password (use an Outlook/O365 app password where Basic SMTP is allowed).</summary>
        BasicOrAppPassword = 0,
        /// <summary>Microsoft 365 modern auth (OAuth2). Required where tenant has disabled Basic SMTP.</summary>
        OAuth2 = 1
    }

    /// <summary>
    /// Outgoing-mail configuration for scheduled-import notifications. Defaults target Microsoft
    /// Outlook / Office 365 (smtp.office365.com:587, STARTTLS). The password is stored DPAPI-encrypted
    /// via <see cref="SecretProtector"/> and never serialized in clear text. Persisted as
    /// %AppData%\OrganizationImportTool\smtp-settings.json (mirrors AiSettings).
    /// </summary>
    public sealed class SmtpSettings
    {
        public const string FileName = "smtp-settings.json";

        public bool Enabled { get; set; }

        public string Host { get; set; } = "smtp.office365.com";
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;

        /// <summary>The address notifications are sent from (configurable in-app).</summary>
        public string FromAddress { get; set; } = string.Empty;
        public string FromDisplayName { get; set; } = "CargoSync";

        public SmtpAuthMode AuthMode { get; set; } = SmtpAuthMode.BasicOrAppPassword;

        /// <summary>Login user (usually the same mailbox as FromAddress).</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>DPAPI-encrypted password/app-password as stored on disk. Use <see cref="Password"/> in code.</summary>
        public string PasswordProtected { get; set; } = string.Empty;

        // ---- OAuth2 (O365) — used when AuthMode == OAuth2 ----
        public string TenantId { get; set; } = string.Empty;
        public string ClientIdOAuth { get; set; } = string.Empty;
        /// <summary>DPAPI-encrypted client secret. Use <see cref="ClientSecret"/> in code.</summary>
        public string ClientSecretProtected { get; set; } = string.Empty;

        /// <summary>Plain password/app-password. Reads decrypt, writes encrypt — never persisted in clear.</summary>
        [JsonIgnore]
        public string Password
        {
            get => SecretProtector.Unprotect(PasswordProtected);
            set => PasswordProtected = SecretProtector.Protect(value ?? string.Empty);
        }

        /// <summary>Plain OAuth2 client secret. Reads decrypt, writes encrypt.</summary>
        [JsonIgnore]
        public string ClientSecret
        {
            get => SecretProtector.Unprotect(ClientSecretProtected);
            set => ClientSecretProtected = SecretProtector.Protect(value ?? string.Empty);
        }

        private static string FilePath => Path.Combine(AppPaths.DataDir, FileName);

        public static SmtpSettings Load()
        {
            try
            {
                string path = FilePath;
                if (File.Exists(path))
                {
                    var s = JsonSerializer.Deserialize<SmtpSettings>(File.ReadAllText(path));
                    if (s != null) return s;
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("SMTP settings load failed - using defaults", ex); }
            return new SmtpSettings();
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Logging.AppLog.Error("SMTP settings save failed", ex); }
        }

        /// <summary>True when enough is set to attempt sending.</summary>
        public bool IsConfigured(out string error)
        {
            if (!Enabled) { error = "Email notifications are disabled."; return false; }
            if (string.IsNullOrWhiteSpace(Host)) { error = "SMTP host is required."; return false; }
            if (string.IsNullOrWhiteSpace(FromAddress)) { error = "From address is required."; return false; }
            if (AuthMode == SmtpAuthMode.BasicOrAppPassword &&
                (string.IsNullOrWhiteSpace(Username) || string.IsNullOrEmpty(Password)))
            {
                error = "Username and password (or app password) are required for basic auth.";
                return false;
            }
            if (AuthMode == SmtpAuthMode.OAuth2 &&
                (string.IsNullOrWhiteSpace(TenantId) || string.IsNullOrWhiteSpace(ClientIdOAuth) || string.IsNullOrEmpty(ClientSecret)))
            {
                error = "Tenant ID, client ID and client secret are required for OAuth2.";
                return false;
            }
            error = string.Empty;
            return true;
        }
    }
}
