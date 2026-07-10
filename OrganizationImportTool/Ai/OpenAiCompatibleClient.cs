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
    /// <summary>
    /// Talks to any OpenAI-compatible /chat/completions endpoint: OpenAI, OpenRouter, Groq,
    /// Together, Azure-OpenAI-style, and local servers (LM Studio / Ollama).
    /// </summary>
    public class OpenAiCompatibleClient : IAiClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        public AiProviderProfile Profile { get; }

        public OpenAiCompatibleClient(AiProviderProfile profile) => Profile = profile;

        public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(Profile.BaseUrl) || string.IsNullOrWhiteSpace(Profile.Model))
                return Fail("Provider not configured (Base URL and Model are required).", 0);

            var sw = Stopwatch.StartNew();
            try
            {
                string url = Profile.BaseUrl.TrimEnd('/') + "/chat/completions";
                var payload = new
                {
                    model = Profile.Model,
                    max_tokens = request.MaxTokensOverride ?? Profile.MaxTokens,
                    temperature = request.TemperatureOverride ?? Profile.Temperature,
                    messages = new object[]
                    {
                        new { role = "system", content = request.System },
                        new { role = "user", content = BuildUserContent(request) }
                    }
                };

                using var msg = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                if (!string.IsNullOrEmpty(Profile.ApiKey))
                    msg.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Profile.ApiKey);
                foreach (var kv in Profile.ExtraHeaders)
                    msg.Headers.TryAddWithoutValidation(kv.Key, kv.Value);

                using var resp = await Http.SendAsync(msg, ct).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                sw.Stop();

                if (!resp.IsSuccessStatusCode)
                    return Fail($"HTTP {(int)resp.StatusCode}: {Trim(body)}", sw.ElapsedMilliseconds);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string text = string.Empty;
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c))
                        text = c.GetString() ?? string.Empty;
                }

                int input = 0, output = 0;
                if (root.TryGetProperty("usage", out var usage))
                {
                    input = GetInt(usage, "prompt_tokens");
                    output = GetInt(usage, "completion_tokens");
                }

                return new AiResponse
                {
                    Success = true,
                    Text = text,
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

        /// <summary>PDF "document" blocks are an Anthropic wire-format feature; images work everywhere.</summary>
        public bool Supports(AiRequest request)
        {
            foreach (var att in request.Attachments)
                if (att.Kind == AiAttachmentKind.Pdf) return false;
            return true;
        }

        /// <summary>
        /// Plain string for text-only requests (unchanged behaviour); multimodal content-part
        /// array (text + data-URI image_url parts) when image attachments are present.
        /// </summary>
        private static object BuildUserContent(AiRequest request)
        {
            if (request.Attachments.Count == 0) return request.Prompt;

            var parts = new List<object>();
            foreach (var att in request.Attachments)
                parts.Add(new
                {
                    type = "image_url",
                    image_url = new { url = $"data:{att.MediaType};base64,{att.Base64Data}" }
                });
            parts.Add(new { type = "text", text = request.Prompt });
            return parts;
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
