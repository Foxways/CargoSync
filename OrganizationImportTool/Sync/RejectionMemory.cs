using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Text;
using System.Text.RegularExpressions;

namespace OrganizationImportTool.Sync
{
    /// <summary>One recurring CargoWise rejection reason for a client.</summary>
    public class RejectionLesson
    {
        public string Signature { get; set; } = string.Empty;   // normalized reason (groups similar messages)
        public string SampleMessage { get; set; } = string.Empty; // a real message, verbatim, for display
        public string SampleCode { get; set; } = string.Empty;    // an org code it happened to
        public int Count { get; set; }
        public DateTime LastSeenUtc { get; set; }
    }

    /// <summary>
    /// The app's "learn from mistakes" memory: every organization CargoWise REJECTS is recorded
    /// with a normalized reason signature, per client. Before the next import, the pipeline reads
    /// these lessons back and warns the operator - in the pre-flight dashboard, not a nag popup -
    /// when the new file looks like it will hit the same rejection again.
    /// </summary>
    public class RejectionMemory
    {
        private readonly string _connStr;

        public RejectionMemory(string? dbPath = null)
        {
            dbPath ??= AppPaths.DbPath;
            _connStr = $"Data Source={dbPath};Version=3;";
            EnsureSchema();
        }

        private void EnsureSchema()
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS CwRejections (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId TEXT,
    Signature TEXT,
    SampleMessage TEXT,
    SampleCode TEXT,
    Count INTEGER NOT NULL DEFAULT 1,
    LastSeenUtc TEXT
);
CREATE UNIQUE INDEX IF NOT EXISTS IX_CwRejections_Client_Sig ON CwRejections (ClientId, Signature);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Rejection memory schema creation failed", ex); }
        }

        /// <summary>Record one CargoWise rejection (upserts by normalized signature).</summary>
        public void Record(string clientId, string? message, string orgCode)
        {
            string sig = Normalize(message);
            if (sig.Length == 0) return;
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(@"
INSERT INTO CwRejections (ClientId, Signature, SampleMessage, SampleCode, Count, LastSeenUtc)
VALUES (@c, @s, @m, @o, 1, @w)
ON CONFLICT(ClientId, Signature)
DO UPDATE SET Count = Count + 1, SampleMessage = @m, SampleCode = @o, LastSeenUtc = @w", conn);
                cmd.Parameters.AddWithValue("@c", clientId ?? "");
                cmd.Parameters.AddWithValue("@s", sig);
                cmd.Parameters.AddWithValue("@m", (message ?? "").Trim());
                cmd.Parameters.AddWithValue("@o", orgCode ?? "");
                cmd.Parameters.AddWithValue("@w", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Rejection memory record failed", ex); }
        }

        /// <summary>The client's recurring rejection reasons, most frequent first.</summary>
        public List<RejectionLesson> ForClient(string clientId, int max = 5)
        {
            var list = new List<RejectionLesson>();
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT Signature, SampleMessage, SampleCode, Count, LastSeenUtc FROM CwRejections " +
                    "WHERE ClientId=@c ORDER BY Count DESC, LastSeenUtc DESC LIMIT @m", conn);
                cmd.Parameters.AddWithValue("@c", clientId ?? "");
                cmd.Parameters.AddWithValue("@m", max);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime.TryParse(r["LastSeenUtc"]?.ToString(), out var seen);
                    list.Add(new RejectionLesson
                    {
                        Signature = r["Signature"]?.ToString() ?? "",
                        SampleMessage = r["SampleMessage"]?.ToString() ?? "",
                        SampleCode = r["SampleCode"]?.ToString() ?? "",
                        Count = Convert.ToInt32(r["Count"] ?? 0),
                        LastSeenUtc = seen
                    });
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("Rejection memory read failed", ex); }
            return list;
        }

        public int CountForClient(string clientId)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand("SELECT COUNT(*) FROM CwRejections WHERE ClientId=@c", conn);
                cmd.Parameters.AddWithValue("@c", clientId ?? "");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        /// <summary>
        /// Normalize a CargoWise error into a stable signature so "Code 'ABC1' too long" and
        /// "Code 'XYZ99' too long" count as the SAME lesson: first line, lowercased, quoted
        /// values and numbers replaced by placeholders.
        /// </summary>
        public static string Normalize(string? message)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;
            string line = message.Replace("\r", "").Split('\n')[0].Trim();
            line = line.ToLowerInvariant();
            line = Regex.Replace(line, "'[^']*'", "'*'");      // 'ACME001' -> '*'
            line = Regex.Replace(line, "\"[^\"]*\"", "\"*\"");
            line = Regex.Replace(line, @"\d+", "#");            // row/line numbers, lengths
            line = Regex.Replace(line, @"\s+", " ");
            return line.Length > 200 ? line.Substring(0, 200) : line;
        }
    }
}
