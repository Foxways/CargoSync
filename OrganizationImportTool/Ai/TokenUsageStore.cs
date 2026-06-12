using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Ai
{
    /// <summary>One recorded AI call - the unit of the token-usage history.</summary>
    public class AiUsageRecord
    {
        public DateTime TimestampUtc { get; set; }
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public bool Success { get; set; }
        public long ElapsedMs { get; set; }
        public int TotalTokens => InputTokens + OutputTokens;
    }

    /// <summary>Aggregated totals for one provider/model (for the usage summary view).</summary>
    public class AiUsageTotals
    {
        public string ProviderName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Calls { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens => InputTokens + OutputTokens;
    }

    /// <summary>
    /// Append-only token-usage history persisted as JSON Lines. Honours a "save tokens" toggle
    /// (when off, history is session-only and not written to disk) and a retention period.
    /// </summary>
    public class TokenUsageStore
    {
        private readonly string _path;
        private readonly List<AiUsageRecord> _records = new List<AiUsageRecord>();
        private readonly object _lock = new object();

        /// <summary>When false, records stay in memory only (never written to disk).</summary>
        public bool Persist { get; set; } = true;

        public TokenUsageStore(string? path = null)
        {
            _path = path ?? Path.Combine(AppPaths.DataDir, "ai-usage-history.jsonl");
            Load();
        }

        public void Record(AiUsageRecord record)
        {
            lock (_lock)
            {
                _records.Add(record);
                if (!Persist) return;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.AppendAllText(_path, JsonSerializer.Serialize(record) + Environment.NewLine);
                }
                catch { /* usage history is best-effort - never break the import over it */ }
            }
        }

        public IReadOnlyList<AiUsageRecord> All()
        {
            lock (_lock) return _records.ToList();
        }

        public int TotalTokens()
        {
            lock (_lock) return _records.Sum(r => r.TotalTokens);
        }

        public List<AiUsageTotals> TotalsByProviderModel()
        {
            lock (_lock)
            {
                return _records
                    .GroupBy(r => (r.ProviderName, r.Model))
                    .Select(g => new AiUsageTotals
                    {
                        ProviderName = g.Key.ProviderName,
                        Model = g.Key.Model,
                        Calls = g.Count(),
                        InputTokens = g.Sum(r => r.InputTokens),
                        OutputTokens = g.Sum(r => r.OutputTokens)
                    })
                    .OrderByDescending(t => t.TotalTokens)
                    .ToList();
            }
        }

        /// <summary>Drop records older than the retention window and rewrite the file. Returns count removed.</summary>
        public int PurgeOlderThan(int retentionDays)
        {
            if (retentionDays <= 0) return 0; // 0/negative = keep forever
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                int before = _records.Count;
                _records.RemoveAll(r => r.TimestampUtc < cutoff);
                int removed = before - _records.Count;
                if (removed > 0 && Persist) Rewrite();
                return removed;
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _records.Clear();
                try { if (File.Exists(_path)) File.Delete(_path); } catch { }
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (var line in File.ReadAllLines(_path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var rec = JsonSerializer.Deserialize<AiUsageRecord>(line);
                    if (rec != null) _records.Add(rec);
                }
            }
            catch { /* corrupt history shouldn't stop the app */ }
        }

        private void Rewrite()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllLines(_path, _records.Select(r => JsonSerializer.Serialize(r)));
            }
            catch { }
        }
    }
}
