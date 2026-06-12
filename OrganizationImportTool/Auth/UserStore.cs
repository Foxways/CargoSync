using System;
using System.Data.SQLite;
using System.IO;

namespace OrganizationImportTool.Auth
{
    /// <summary>
    /// SQLite-backed user accounts (in the app's data.db). Stores PBKDF2 password hashes + salts
    /// and a password hint, and creates the activity-tracking table. No plaintext passwords.
    /// </summary>
    public class UserStore
    {
        private readonly string _connStr;

        public UserStore(string? dbPath = null)
        {
            dbPath ??= AppPaths.DbPath;
            _connStr = $"Data Source={dbPath};Version=3;";
        }

        private SQLiteConnection Open()
        {
            var c = new SQLiteConnection(_connStr);
            c.Open();
            return c;
        }

        public void EnsureSchema()
        {
            using var conn = Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE COLLATE NOCASE,
    PasswordHash TEXT NOT NULL,
    PasswordSalt TEXT NOT NULL,
    PasswordHint TEXT,
    CreatedUtc TEXT
);
CREATE TABLE IF NOT EXISTS ImportActivity (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UserId INTEGER,
    Username TEXT,
    ClientName TEXT,
    FileName TEXT,
    Total INTEGER,
    Succeeded INTEGER,
    Failed INTEGER,
    NotSent INTEGER,
    WhenUtc TEXT
);";
            cmd.ExecuteNonQuery();
        }

        public bool AnyUsers()
        {
            using var conn = Open();
            using var cmd = new SQLiteCommand("SELECT COUNT(*) FROM Users", conn);
            return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
        }

        public bool UserExists(string username)
        {
            using var conn = Open();
            using var cmd = new SQLiteCommand("SELECT 1 FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", username.Trim());
            return cmd.ExecuteScalar() != null;
        }

        /// <summary>Create a user. Returns null on success, or an error message.</summary>
        public string? CreateUser(string username, string password, string? hint)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length < 3) return "Username must be at least 3 characters.";
            if ((password ?? string.Empty).Length < 4) return "Password must be at least 4 characters.";
            if (UserExists(username)) return "That username already exists.";

            var (hash, salt) = PasswordHasher.Hash(password!);
            using var conn = Open();
            using var cmd = new SQLiteCommand(
                "INSERT INTO Users (Username, PasswordHash, PasswordSalt, PasswordHint, CreatedUtc) VALUES (@u,@h,@s,@hint,@c)", conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@hint", (object?)hint?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
            return null;
        }

        /// <summary>Returns the user on correct credentials, otherwise null.</summary>
        public User? Authenticate(string username, string password)
        {
            using var conn = Open();
            using var cmd = new SQLiteCommand("SELECT Id, PasswordHash, PasswordSalt FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", (username ?? string.Empty).Trim());
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            string hash = r.GetString(1), salt = r.GetString(2);
            if (!PasswordHasher.Verify(password ?? string.Empty, hash, salt)) return null;
            return new User { Id = r.GetInt32(0), Username = username!.Trim() };
        }

        public string? GetHint(string username)
        {
            using var conn = Open();
            using var cmd = new SQLiteCommand("SELECT PasswordHint FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", (username ?? string.Empty).Trim());
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? null : v.ToString();
        }

        /// <summary>Set a new password (used by forgot-password reset). Returns null on success or an error.</summary>
        public string? ResetPassword(string username, string newPassword)
        {
            if ((newPassword ?? string.Empty).Length < 4) return "Password must be at least 4 characters.";
            if (!UserExists(username)) return "No such user.";
            var (hash, salt) = PasswordHasher.Hash(newPassword!);
            using var conn = Open();
            using var cmd = new SQLiteCommand("UPDATE Users SET PasswordHash=@h, PasswordSalt=@s WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.ExecuteNonQuery();
            return null;
        }
    }
}
