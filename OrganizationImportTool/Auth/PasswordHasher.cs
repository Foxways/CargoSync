using System;
using System.Security.Cryptography;

namespace OrganizationImportTool.Auth
{
    /// <summary>
    /// PBKDF2 (SHA-256) password hashing with a per-user random salt - the standard, production
    /// approach. Passwords are never stored or recoverable; verification is constant-time.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 100_000;

        public static (string hash, string salt) Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
            return (Convert.ToBase64String(key), Convert.ToBase64String(salt));
        }

        public static bool Verify(string password, string hashBase64, string saltBase64)
        {
            try
            {
                byte[] salt = Convert.FromBase64String(saltBase64);
                byte[] expected = Convert.FromBase64String(hashBase64);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch { return false; }
        }
    }
}
