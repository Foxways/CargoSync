using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace OrganizationImportTool.Sync
{
    /// <summary>One organization's reconciliation record: what we sent vs what CargoWise stored.</summary>
    public class CwSyncEntry
    {
        public long Id { get; set; }
        public string ClientId { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string SentCode { get; set; } = string.Empty;
        public string StoredCode { get; set; } = string.Empty;   // code CargoWise actually stored (may be generated)
        public string EntityPk { get; set; } = string.Empty;     // CargoWise primary key (GUID)
        public string EntityName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;       // PRS / WRN / ERR
        public string MessageNumber { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public DateTime SyncedUtc { get; set; }

        /// <summary>The import run this record belongs to (links to the ImportRuns journal).</summary>
        public string RunId { get; set; } = string.Empty;

        public bool IsSuccess => string.Equals(Status, "PRS", StringComparison.OrdinalIgnoreCase);
        public string Outcome => IsSuccess ? "Created/Updated" :
            string.Equals(Status, "WRN", StringComparison.OrdinalIgnoreCase) ? "Warning" : "Rejected";
    }

    /// <summary>
    /// Persists the feedback CargoWise returns for every transmitted organization (sent code →
    /// stored code, primary key, status), so the tool has a permanent ledger of what it has put
    /// into CargoWise. Later imports read this back to flag rows already synced for a client.
    /// </summary>
    public class FeedbackStore
    {
        private readonly string _connStr;

        public FeedbackStore(string? dbPath = null)
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
CREATE TABLE IF NOT EXISTS CwSync (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ClientId TEXT,
    ClientName TEXT,
    SentCode TEXT,
    StoredCode TEXT,
    EntityPk TEXT,
    EntityName TEXT,
    Status TEXT,
    MessageNumber TEXT,
    Username TEXT,
    SyncedUtc TEXT
);
CREATE INDEX IF NOT EXISTS IX_CwSync_Client_Sent ON CwSync (ClientId, SentCode);
CREATE TABLE IF NOT EXISTS ImportRuns (
    RunId TEXT PRIMARY KEY,
    ClientId TEXT,
    FileName TEXT,
    FileHash TEXT,
    TotalRows INTEGER,
    StartedUtc TEXT,
    CompletedUtc TEXT NULL,
    Username TEXT
);";
                cmd.ExecuteNonQuery();

                // PRAGMA-guarded column add for ledgers created before the run journal existed.
                bool hasRunId = false;
                using (var info = new SQLiteCommand("PRAGMA table_info(CwSync)", conn))
                using (var r = info.ExecuteReader())
                    while (r.Read()) if (string.Equals(r["name"]?.ToString(), "RunId", StringComparison.OrdinalIgnoreCase)) hasRunId = true;
                if (!hasRunId)
                {
                    using var alter = new SQLiteCommand("ALTER TABLE CwSync ADD COLUMN RunId TEXT", conn);
                    alter.ExecuteNonQuery();
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("Sync ledger schema creation failed", ex); /* must never break an import */ }
        }

        // ---------------- per-run journal (crash/resume detection) ----------------

        /// <summary>Open a run journal entry; returns the run id. CompletedUtc stays NULL until CompleteRun.</summary>
        public string BeginRun(string clientId, string fileName, string fileHash, int totalRows, string username)
        {
            string runId = Guid.NewGuid().ToString("N");
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "INSERT INTO ImportRuns (RunId, ClientId, FileName, FileHash, TotalRows, StartedUtc, Username) " +
                    "VALUES (@r,@c,@f,@h,@t,@s,@u)", conn);
                cmd.Parameters.AddWithValue("@r", runId);
                cmd.Parameters.AddWithValue("@c", clientId ?? "");
                cmd.Parameters.AddWithValue("@f", fileName ?? "");
                cmd.Parameters.AddWithValue("@h", fileHash ?? "");
                cmd.Parameters.AddWithValue("@t", totalRows);
                cmd.Parameters.AddWithValue("@s", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@u", username ?? "");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Run journal BeginRun failed", ex); }
            return runId;
        }

        /// <summary>Mark a run as cleanly finished (crash detection = runs that never reach this).</summary>
        public void CompleteRun(string runId)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand("UPDATE ImportRuns SET CompletedUtc=@w WHERE RunId=@r", conn);
                cmd.Parameters.AddWithValue("@w", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("@r", runId ?? "");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Run journal CompleteRun failed", ex); }
        }

        public sealed class ImportRunInfo
        {
            public string RunId { get; set; } = string.Empty;
            public string FileName { get; set; } = string.Empty;
            public int TotalRows { get; set; }
            public DateTime StartedUtc { get; set; }
            public int RowsRecorded { get; set; }
        }

        /// <summary>Newest run of this exact file for this client that never completed (crash/kill), if any.</summary>
        public ImportRunInfo? FindIncompleteRun(string clientId, string fileHash)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT RunId, FileName, TotalRows, StartedUtc, " +
                    "  (SELECT COUNT(*) FROM CwSync WHERE CwSync.RunId = ImportRuns.RunId) AS Recorded " +
                    "FROM ImportRuns WHERE ClientId=@c AND FileHash=@h AND CompletedUtc IS NULL " +
                    "ORDER BY StartedUtc DESC LIMIT 1", conn);
                cmd.Parameters.AddWithValue("@c", clientId ?? "");
                cmd.Parameters.AddWithValue("@h", fileHash ?? "");
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;
                DateTime.TryParse(r["StartedUtc"]?.ToString(), out var started);
                return new ImportRunInfo
                {
                    RunId = r["RunId"]?.ToString() ?? "",
                    FileName = r["FileName"]?.ToString() ?? "",
                    TotalRows = Convert.ToInt32(r["TotalRows"] ?? 0),
                    StartedUtc = started,
                    RowsRecorded = Convert.ToInt32(r["Recorded"] ?? 0)
                };
            }
            catch (Exception ex) { Logging.AppLog.Warn("Run journal FindIncompleteRun failed", ex); return null; }
        }

        public void Record(CwSyncEntry e)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "INSERT INTO CwSync (ClientId, ClientName, SentCode, StoredCode, EntityPk, EntityName, Status, MessageNumber, Username, SyncedUtc, RunId) " +
                    "VALUES (@ci,@cn,@sc,@stc,@pk,@en,@st,@mn,@un,@w,@run)", conn);
                cmd.Parameters.AddWithValue("@ci", e.ClientId ?? "");
                cmd.Parameters.AddWithValue("@cn", e.ClientName ?? "");
                cmd.Parameters.AddWithValue("@sc", e.SentCode ?? "");
                cmd.Parameters.AddWithValue("@stc", e.StoredCode ?? "");
                cmd.Parameters.AddWithValue("@pk", e.EntityPk ?? "");
                cmd.Parameters.AddWithValue("@en", e.EntityName ?? "");
                cmd.Parameters.AddWithValue("@st", e.Status ?? "");
                cmd.Parameters.AddWithValue("@mn", e.MessageNumber ?? "");
                cmd.Parameters.AddWithValue("@un", e.Username ?? "");
                cmd.Parameters.AddWithValue("@w", (e.SyncedUtc == default ? DateTime.UtcNow : e.SyncedUtc).ToString("o"));
                cmd.Parameters.AddWithValue("@run", e.RunId ?? "");
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn($"Sync ledger write failed for '{e.SentCode}'", ex); }
        }

        /// <summary>The set of codes already successfully synced to CargoWise for a client (for re-import detection).</summary>
        public HashSet<string> SyncedCodes(string clientId)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT SentCode, StoredCode FROM CwSync WHERE ClientId=@ci AND Status='PRS'", conn);
                cmd.Parameters.AddWithValue("@ci", clientId ?? "");
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string sc = r["SentCode"]?.ToString() ?? "";
                    string stc = r["StoredCode"]?.ToString() ?? "";
                    if (sc.Length > 0) set.Add(sc);
                    if (stc.Length > 0) set.Add(stc);
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("Sync ledger read (SyncedCodes) failed", ex); }
            return set;
        }

        /// <summary>Full sync ledger for a client, most recent first.</summary>
        public List<CwSyncEntry> ForClient(string clientId, int max = 1000)
        {
            var list = new List<CwSyncEntry>();
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT * FROM CwSync WHERE ClientId=@ci ORDER BY Id DESC LIMIT @m", conn);
                cmd.Parameters.AddWithValue("@ci", clientId ?? "");
                cmd.Parameters.AddWithValue("@m", max);
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(Read(r));
            }
            catch (Exception ex) { Logging.AppLog.Warn("Sync ledger read (ForClient) failed", ex); }
            return list;
        }

        public int CountForClient(string clientId)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand("SELECT COUNT(*) FROM CwSync WHERE ClientId=@ci AND Status='PRS'", conn);
                cmd.Parameters.AddWithValue("@ci", clientId ?? "");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }

        private static CwSyncEntry Read(SQLiteDataReader r)
        {
            DateTime.TryParse(r["SyncedUtc"]?.ToString(), out var when);
            return new CwSyncEntry
            {
                Id = Convert.ToInt64(r["Id"]),
                ClientId = r["ClientId"]?.ToString() ?? "",
                ClientName = r["ClientName"]?.ToString() ?? "",
                SentCode = r["SentCode"]?.ToString() ?? "",
                StoredCode = r["StoredCode"]?.ToString() ?? "",
                EntityPk = r["EntityPk"]?.ToString() ?? "",
                EntityName = r["EntityName"]?.ToString() ?? "",
                Status = r["Status"]?.ToString() ?? "",
                MessageNumber = r["MessageNumber"]?.ToString() ?? "",
                Username = r["Username"]?.ToString() ?? "",
                SyncedUtc = when
            };
        }
    }
}
