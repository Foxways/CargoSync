using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace OrganizationImportTool.Scheduling
{
    /// <summary>A client that has eAdaptor credentials configured (selectable for a job).</summary>
    public sealed class ClientRef
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public override string ToString() => Name;
    }

    /// <summary>Lists the clients (with eAdaptor config) from data.db, for the job editor's picker.</summary>
    public static class ClientDirectory
    {
        public static List<ClientRef> List()
        {
            var list = new List<ClientRef>();
            try
            {
                using var conn = new SQLiteConnection($"Data Source={AppPaths.DbPath};Version=3;");
                conn.Open();
                using var cmd = new SQLiteCommand(
                    "SELECT DISTINCT C.Id, C.Name FROM Clients C JOIN EAdaptors E ON E.ClientId = C.Id ORDER BY C.Name", conn);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new ClientRef { Id = r["Id"]?.ToString() ?? string.Empty, Name = r["Name"]?.ToString() ?? string.Empty });
            }
            catch (Exception ex)
            {
                Logging.AppLog.Warn("Listing clients for the job editor failed", ex);
            }
            return list;
        }
    }
}
