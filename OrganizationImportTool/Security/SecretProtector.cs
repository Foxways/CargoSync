using System;
using System.Security.Cryptography;
using System.Text;
using OrganizationImportTool.Logging;

namespace OrganizationImportTool.Security
{
    /// <summary>
    /// Encrypts secrets (API keys, passwords) at rest using Windows DPAPI, scoped to the
    /// current user. Protected values are tagged with a prefix so we can detect and migrate
    /// legacy plaintext values. Never log the unprotected value.
    /// </summary>
    public static class SecretProtector
    {
        private const string Prefix = "enc:v1:";
        private const string PlainPrefix = "plain:v1:"; // DPAPI failed - stored un-encrypted but tagged

        private static bool _plainWarned; // log the legacy/plain warning once per process, not per read

        /// <summary>
        /// Raised when a secret could NOT be encrypted and was stored un-encrypted (tagged).
        /// Save dialogs subscribe to surface a visible warning to the user.
        /// </summary>
        public static event Action<string>? ProtectFailed;

        public static string Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(enc);
            }
            catch (Exception ex)
            {
                // DPAPI unavailable: keep the value recoverable (tagged, base64-wrapped) but make
                // the failure LOUD - log it and tell the UI so the user knows it is un-encrypted.
                AppLog.Error("DPAPI encryption failed - secret stored UN-ENCRYPTED (tagged plain:v1)", ex);
                ProtectFailed?.Invoke(
                    "Windows could not encrypt this secret, so it was saved un-encrypted on this machine. " +
                    "It still works, but anyone with access to your Windows profile can read it.");
                return PlainPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
            }
        }

        public static string Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;

            if (stored.StartsWith(PlainPrefix, StringComparison.Ordinal))
            {
                WarnPlainOnce("A stored secret is un-encrypted (DPAPI was unavailable when it was saved).");
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(PlainPrefix.Length))); }
                catch (Exception ex) { AppLog.Error("Corrupt plain-tagged secret could not be decoded", ex); return string.Empty; }
            }

            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                WarnPlainOnce("A legacy plaintext secret was read from storage (it will be encrypted on next save).");
                return stored; // legacy / unencrypted value
            }

            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] bytes = ProtectedData.Unprotect(enc, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch (Exception ex)
            {
                // A silently-empty password looks like an auth failure with no clue - log why.
                AppLog.Error("DPAPI decryption failed (different Windows user/machine, or corrupt value) - returning empty secret", ex);
                return string.Empty;
            }
        }

        public static bool IsProtected(string? stored) =>
            !string.IsNullOrEmpty(stored) && stored!.StartsWith(Prefix, StringComparison.Ordinal);

        private static void WarnPlainOnce(string message)
        {
            if (_plainWarned) return;
            _plainWarned = true;
            AppLog.Warn(message);
        }
    }
}
