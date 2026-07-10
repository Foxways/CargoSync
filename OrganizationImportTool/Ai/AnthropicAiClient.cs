using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Ai
{
    /// <summary>Talks to Anthropic's /v1/messages API (Claude models).</summary>
    public class AnthropicAiClient : IAiClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private const string AnthropicVersion = "2023-06-01";
        public AiProviderProfile Profile { get; }

        public AnthropicAiClient(AiProviderProfile profile) => Profile = profile;

        public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(Profile.Model) || string.IsNullOrWhiteSpace(Profile.ApiKey))
                return Fail("Anthropic provider not configured (Model and API key are required).", 0);

            var sw = Stopwatch.StartNew();
            try
            {
                string baseUrl = string.IsNullOrWhiteSpace(Profile.BaseUrl) ? "https://api.anthropic.com" : Profile.BaseUrl;
                string url = baseUrl.TrimEnd('/') + "/v1/messages";

                var payload = new
                {
                    model = Profile.Model,
                    max_tokens = request.MaxTokensOverride ?? Profile.MaxTokens,
                    temperature = request.TemperatureOverride ?? Profile.Temperature,
                    system = request.System,
                    messages = new object[]
                    {
                        new { role = "user", content = BuildContent(request) }
                    }
                };

                using var msg = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                msg.Headers.TryAddWithoutValidation("x-api-key", Profile.ApiKey);
                msg.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
                foreach (var kv in Profile.ExtraHeaders)
                    msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

                using var resp = await Http.SendAsync(msg, ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                    return Fail($"HTTP {(int)resp.StatusCode}: {Trim(body)}", sw.ElapsedMilliseconds);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var sb = new StringBuilder();
                if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    foreach (var block in content.EnumerateArray())
                        if (block.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                            block.TryGetProperty("text", out var txt))
                            sb.Append(txt.GetString());

                int input = 0, output = 0;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = GetInt(usage, "input_tokens");
                    output = GetInt(usage, "output_tokens");
                }

                return new AiResponse
                {
                    Success = true,
                    Text = sb.ToString(),
                    ProviderId = Profile.Id,
                    ProviderName = Profile.Name,
                    Model = Profile.Model,
                    InputTokens = input,
                    OutputTokens = output,
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }
            catch (OperationCanceledException)
            {
                return Fail("Cancelled.", sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                return Fail(ex.Message, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Plain string for text-only requests (unchanged behaviour); content-block array when
        /// attachments are present. PDFs ride as native "document" blocks - the model sees both
        /// the text layer and the rendered layout, so no local rasterisation is needed.
        /// </summary>
        private static object BuildContent(AiRequest request)
        {
            if (request.Attachments.Count == 0) return request.Prompt;

            var blocks = new List<object>();
            foreach (var att in request.Attachments)
            {
                blocks.Add(att.Kind == AiAttachmentKind.Pdf
                    ? new
                    {
                        type = "document",
                        source = new { type = "base64", media_type = "application/pdf", data = att.Base64Data }
                    }
                    : (object)new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = att.MediaType, data = att.Base64Data }
                    });
            }
            blocks.Add(new { type = "text", text = request.Prompt });
            return blocks;
        }

        private AiResponse Fail(string error, long ms)
        {
            var r = AiResponse.Fail(error, Profile.Id, Profile.Name, Profile.Model);
            r.ElapsedMs = ms;
            return r;
        }

        private static int GetInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

        private static string Trim(string s) => s.Length <= 300 ? s : s.Substring(0, 300) + "…";
    }
}
