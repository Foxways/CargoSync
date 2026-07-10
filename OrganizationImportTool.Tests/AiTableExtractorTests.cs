using OrganizationImportTool.Ai;
using OrganizationImportTool.Ingestion;

namespace OrganizationImportTool.Tests
{
    public class AiTableExtractorTests
    {
        private static AiAttachment Attachment() => new()
        {
            Kind = AiAttachmentKind.Image, MediaType = "image/png", Base64Data = "AAAA"
        };

        private static AiResponse Ok(string text) => new() { Success = true, Text = text };

        /// <summary>Fake completer that answers extract and verify calls from canned scripts.</summary>
        private static AiTableExtractor Fake(Queue<string> extractReplies, string? verifyReply = null)
            => new((req, ct) =>
            {
                if (req.Operation == "verify-extraction")
                    return Task.FromResult(verifyReply == null ? AiResponse.Fail("no verify") : Ok(verifyReply));
                return Task.FromResult(extractReplies.Count > 0 ? Ok(extractReplies.Dequeue()) : AiResponse.Fail("exhausted"));
            });

        [Fact]
        public async Task Valid_json_becomes_a_table()
        {
            var x = Fake(new Queue<string>(new[]
            {
                """{"headers":["Code","Name"],"rows":[["ORG1","Acme"],["ORG2","Beta"]]}"""
            }));
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal(2, t.RowCount);
            Assert.Equal("Acme", t.Rows[0]["Name"]);
            Assert.Contains("AI extracted", t.SourceName);
        }

        [Fact]
        public async Task Markdown_fenced_json_is_tolerated()
        {
            var x = Fake(new Queue<string>(new[]
            {
                "```json\n{\"headers\":[\"Code\"],\"rows\":[[\"ORG1\"]]}\n```"
            }));
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal(1, t.RowCount);
        }

        [Fact]
        public async Task Malformed_reply_is_retried_once()
        {
            var x = Fake(new Queue<string>(new[]
            {
                "Sure! Here is the table you asked for: Code, Name...",
                """{"headers":["Code"],"rows":[["ORG1"]]}"""
            }));
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal("ORG1", t.Rows[0]["Code"]);
        }

        [Fact]
        public async Task Verify_pass_corrections_are_applied()
        {
            var x = Fake(
                new Queue<string>(new[] { """{"headers":["Code","Name"],"rows":[["0RG1","Acme"]]}""" }),
                """{"row_count_ok":true,"corrections":[{"row":1,"header":"Code","value":"ORG1"}]}""");
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal("ORG1", t.Rows[0]["Code"]); // 0RG1 -> ORG1 (classic vision digit/letter swap)
        }

        [Fact]
        public async Task Failed_verify_keeps_the_extraction()
        {
            var x = Fake(new Queue<string>(new[] { """{"headers":["Code"],"rows":[["ORG1"]]}""" }));
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal(1, t.RowCount);
        }

        [Fact]
        public async Task Exhausted_chain_throws_invalid_data()
        {
            var x = new AiTableExtractor((req, ct) => Task.FromResult(AiResponse.Fail("all providers down")));
            await Assert.ThrowsAsync<InvalidDataException>(() => x.ExtractAsync(Attachment(), "scan.png", "scan.png"));
        }

        [Fact]
        public async Task Ragged_rows_are_padded_not_crashed()
        {
            var x = Fake(new Queue<string>(new[]
            {
                """{"headers":["Code","Name"],"rows":[["ORG1"],["ORG2","Beta","extra"]]}"""
            }));
            var t = await x.ExtractAsync(Attachment(), "scan.png", "scan.png");
            Assert.Equal(string.Empty, t.Rows[0]["Name"]);
            Assert.Equal("Beta", t.Rows[1]["Name"]);
        }
    }
}
