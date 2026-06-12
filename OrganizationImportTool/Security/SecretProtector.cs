using System;
using System.Security.Cryptography;
using System.Text;

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

        public static string Protect(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plain);
                byte[] enc = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Prefix + Convert.ToBase64String(enc);
            }
            catch
            {
                // If DPAPI is unavailable, fall back to storing as-is rather than losing the value.
                return plain;
            }
        }

        public static string Unprotect(string? stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
                return stored; // legacy / unencrypted value
            try
            {
                byte[] enc = Convert.FromBase64String(stored.Substring(Prefix.Length));
                byte[] bytes = ProtectedData.Unprotect(enc, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsProtected(string? stored) =>
            !string.IsNullOrEmpty(stored) && stored!.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
