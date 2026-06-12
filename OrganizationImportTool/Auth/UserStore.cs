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
            MigrateUsersTable(conn);
        }

        /// <summary>Idempotent column additions for existing databases (PRAGMA-guarded).</summary>
        private static void MigrateUsersTable(SQLiteConnection conn)
        {
            var existing = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var info = new SQLiteCommand("PRAGMA table_info(Users)", conn))
            using (var r = info.ExecuteReader())
                while (r.Read()) existing.Add(r["name"]?.ToString() ?? "");

            void AddColumn(string ddl, string name)
            {
                if (existing.Contains(name)) return;
                using var alter = new SQLiteCommand($"ALTER TABLE Users ADD COLUMN {ddl}", conn);
                alter.ExecuteNonQuery();
            }
            AddColumn("IsAdmin INTEGER NOT NULL DEFAULT 0", "IsAdmin");
            AddColumn("FailedLogins INTEGER NOT NULL DEFAULT 0", "FailedLogins");
            AddColumn("LockoutUntilUtc TEXT NULL", "LockoutUntilUtc");

            // First-created user becomes the administrator if none exists yet (simplest sane
            // model for a small desktop tool: the person who installed it administers it).
            using var fix = new SQLiteCommand(
                "UPDATE Users SET IsAdmin=1 WHERE Id=(SELECT MIN(Id) FROM Users) " +
                "AND NOT EXISTS (SELECT 1 FROM Users WHERE IsAdmin=1)", conn);
            fix.ExecuteNonQuery();
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

            bool firstUser = !AnyUsers(); // the very first account administers this installation

            var (hash, salt) = PasswordHasher.Hash(password!);
            using var conn = Open();
            using var cmd = new SQLiteCommand(
                "INSERT INTO Users (Username, PasswordHash, PasswordSalt, PasswordHint, CreatedUtc, IsAdmin) VALUES (@u,@h,@s,@hint,@c,@a)", conn);
            cmd.Parameters.AddWithValue("@u", username);
            cmd.Parameters.AddWithValue("@h", hash);
            cmd.Parameters.AddWithValue("@s", salt);
            cmd.Parameters.AddWithValue("@hint", (object?)hint?.Trim() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@c", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@a", firstUser ? 1 : 0);
            cmd.ExecuteNonQuery();
            return null;
        }

        private const int MaxFailedLogins = 5;
        private static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(15);

        /// <summary>Returns the user on correct credentials, otherwise null (no lockout detail).</summary>
        public User? Authenticate(string username, string password) => AuthenticateDetailed(username, password).User;

        /// <summary>
        /// Credential check with brute-force protection: 5 wrong passwords lock the account for
        /// 15 minutes (persisted, so restarting the app doesn't bypass it).
        /// </summary>
        public AuthResult AuthenticateDetailed(string username, string password)
        {
            username = (username ?? string.Empty).Trim();
            using var conn = Open();
            using var cmd = new SQLiteCommand(
                "SELECT Id, PasswordHash, PasswordSalt, IsAdmin, FailedLogins, LockoutUntilUtc FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", username);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return new AuthResult(); // unknown user: same generic failure as wrong password

            int id = r.GetInt32(0);
            string hash = r.GetString(1), salt = r.GetString(2);
            bool isAdmin = !r.IsDBNull(3) && r.GetInt32(3) == 1;
            int failed = r.IsDBNull(4) ? 0 : r.GetInt32(4);
            DateTime? lockedUntil = !r.IsDBNull(5) && DateTime.TryParse(r.GetString(5), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var lu) ? lu : null;
            r.Close();

            if (lockedUntil.HasValue && lockedUntil.Value > DateTime.UtcNow)
                return new AuthResult { Locked = true, LockRemaining = lockedUntil.Value - DateTime.UtcNow };

            if (!PasswordHasher.Verify(password ?? string.Empty, hash, salt))
            {
                failed++;
                bool lockNow = failed >= MaxFailedLogins;
                using var fail = new SQLiteCommand(
                    "UPDATE Users SET FailedLogins=@f, LockoutUntilUtc=@l WHERE Id=@id", conn);
                fail.Parameters.AddWithValue("@f", lockNow ? 0 : failed);
                fail.Parameters.AddWithValue("@l", lockNow ? (object)DateTime.UtcNow.Add(LockoutWindow).ToString("o") : DBNull.Value);
                fail.Parameters.AddWithValue("@id", id);
                fail.ExecuteNonQuery();
                if (lockNow) Logging.AppLog.Warn($"Account '{username}' locked for {LockoutWindow.TotalMinutes:0} minutes after {MaxFailedLogins} failed sign-ins");
                return lockNow
                    ? new AuthResult { Locked = true, LockRemaining = LockoutWindow }
                    : new AuthResult { FailedCount = failed, AttemptsRemaining = MaxFailedLogins - failed };
            }

            // Success: clear the counters.
            using var ok = new SQLiteCommand("UPDATE Users SET FailedLogins=0, LockoutUntilUtc=NULL WHERE Id=@id", conn);
            ok.Parameters.AddWithValue("@id", id);
            ok.ExecuteNonQuery();
            return new AuthResult { User = new User { Id = id, Username = username, IsAdmin = isAdmin } };
        }

        /// <summary>Change a password by proving knowledge of the current one. Null = success.</summary>
        public string? ChangePassword(string username, string currentPassword, string newPassword)
        {
            var auth = AuthenticateDetailed(username, currentPassword);
            if (auth.Locked) return "This account is temporarily locked. Try again later.";
            if (auth.User == null) return "Current password is incorrect.";
            return ResetPasswordUnchecked(username, newPassword);
        }

        /// <summary>Reset another user's password with an administrator's approval. Null = success.</summary>
        public string? AdminResetPassword(string adminUsername, string adminPassword, string targetUsername, string newPassword)
        {
            var admin = AuthenticateDetailed(adminUsername, adminPassword);
            if (admin.Locked) return "The administrator account is temporarily locked.";
            if (admin.User == null) return "Administrator sign-in failed.";
            if (!admin.User.IsAdmin) return $"'{adminUsername}' is not an administrator.";
            if (!UserExists(targetUsername)) return "No such user.";
            Logging.AppLog.Warn($"Password for '{targetUsername}' reset by administrator '{adminUsername}'.");
            return ResetPasswordUnchecked(targetUsername, newPassword);
        }

        public string? GetHint(string username)
        {
            using var conn = Open();
            using var cmd = new SQLiteCommand("SELECT PasswordHint FROM Users WHERE Username=@u", conn);
            cmd.Parameters.AddWithValue("@u", (username ?? string.Empty).Trim());
            var v = cmd.ExecuteScalar();
            return v == null || v == DBNull.Value ? null : v.ToString();
        }

        /// <summary>
        /// Set a new password WITHOUT verifying identity. Private on purpose: every caller must
        /// go through ChangePassword (knows the old password) or AdminResetPassword (admin-approved).
        /// </summary>
        private string? ResetPasswordUnchecked(string username, string newPassword)
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
