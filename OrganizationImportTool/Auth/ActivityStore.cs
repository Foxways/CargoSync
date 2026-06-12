using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;

namespace OrganizationImportTool.Auth
{
    public class ImportActivity
    {
        public string Username { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public int Total { get; set; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public int NotSent { get; set; }
        public DateTime WhenUtc { get; set; }
    }

    /// <summary>
    /// Records who imported what, for which client, and the outcome counts - so there's a clear
    /// audit trail of every import run by every user.
    /// </summary>
    public class ActivityStore
    {
        private readonly string _connStr;

        public ActivityStore(string? dbPath = null)
        {
            dbPath ??= AppPaths.DbPath;
            _connStr = $"Data Source={dbPath};Version=3;";
        }

        public void Record(int userId, string username, string clientName, string fileName,
                           int total, int succeeded, int failed, int notSent)
        {
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "INSERT INTO ImportActivity (UserId, Username, ClientName, FileName, Total, Succeeded, Failed, NotSent, WhenUtc) " +
                    "VALUES (@uid,@un,@c,@f,@t,@ok,@fail,@ns,@w)", conn);
                cmd.Parameters.AddWithValue("@uid", userId);
                cmd.Parameters.AddWithValue("@un", username);
                cmd.Parameters.AddWithValue("@c", clientName);
                cmd.Parameters.AddWithValue("@f", fileName);
                cmd.Parameters.AddWithValue("@t", total);
                cmd.Parameters.AddWithValue("@ok", succeeded);
                cmd.Parameters.AddWithValue("@fail", failed);
                cmd.Parameters.AddWithValue("@ns", notSent);
                cmd.Parameters.AddWithValue("@w", DateTime.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex) { Logging.AppLog.Warn("Activity record failed", ex); /* must never break an import */ }
        }

        public List<ImportActivity> Recent(int max = 200)
        {
            var list = new List<ImportActivity>();
            try
            {
                using var conn = new SQLiteConnection(_connStr);
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT Username, ClientName, FileName, Total, Succeeded, Failed, NotSent, WhenUtc " +
                    "FROM ImportActivity ORDER BY Id DESC LIMIT @m", conn);
                cmd.Parameters.AddWithValue("@m", max);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    DateTime.TryParse(r["WhenUtc"]?.ToString(), out var when);
                    list.Add(new ImportActivity
                    {
                        Username = r["Username"]?.ToString() ?? "",
                        ClientName = r["ClientName"]?.ToString() ?? "",
                        FileName = r["FileName"]?.ToString() ?? "",
                        Total = ToInt(r["Total"]), Succeeded = ToInt(r["Succeeded"]),
                        Failed = ToInt(r["Failed"]), NotSent = ToInt(r["NotSent"]),
                        WhenUtc = when
                    });
                }
            }
            catch (Exception ex) { Logging.AppLog.Warn("Activity history read failed", ex); }
            return list;
        }

        private static int ToInt(object? o) => o == null || o == DBNull.Value ? 0 : Convert.ToInt32(o);
    }
}
