using OrganizationImportTool.Sync;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class FeedbackStoreTests
    {
        private static CwSyncEntry Entry(string client, string sent, string stored, string status) => new()
        {
            ClientId = client,
            ClientName = "Test Client",
            SentCode = sent,
            StoredCode = stored,
            EntityPk = Guid.NewGuid().ToString(),
            EntityName = "OrgHeader",
            Status = status,
            Username = "tester",
        };

        [Fact]
        public void SyncedCodes_returns_sent_and_stored_codes_for_prs_only()
        {
            using var db = TestData.TempDb();
            var store = new FeedbackStore(db.Path);

            store.Record(Entry("C1", "ZZ1", "ZZ1A", "PRS")); // CW renamed the code
            store.Record(Entry("C1", "ZZBAD", "", "ERR"));

            var codes = store.SyncedCodes("C1");
            Assert.Contains("ZZ1", codes);
            Assert.Contains("ZZ1A", codes);   // renamed code must also count
            Assert.DoesNotContain("ZZBAD", codes);
        }

        [Fact]
        public void Clients_are_isolated()
        {
            using var db = TestData.TempDb();
            var store = new FeedbackStore(db.Path);
            store.Record(Entry("C1", "ZZ1", "ZZ1", "PRS"));

            Assert.Empty(store.SyncedCodes("OTHER"));
            Assert.Equal(0, store.CountForClient("OTHER"));
            Assert.Equal(1, store.CountForClient("C1"));
        }

        [Fact]
        public void ForClient_returns_ledger_most_recent_first()
        {
            using var db = TestData.TempDb();
            var store = new FeedbackStore(db.Path);
            store.Record(Entry("C1", "FIRST", "FIRST", "PRS"));
            store.Record(Entry("C1", "SECOND", "SECOND", "WRN"));

            var ledger = store.ForClient("C1");
            Assert.Equal(2, ledger.Count);
            Assert.Equal("SECOND", ledger[0].SentCode);
            Assert.Equal("Warning", ledger[0].Outcome);
            Assert.True(ledger[1].IsSuccess);
        }
    }
}
