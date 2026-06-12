using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Ai
{
    public enum AiPhase { Idle, Trying, Succeeded, FellBack, Exhausted, Disabled }

    /// <summary>Live status snapshot the UI binds to (which provider is active, last result, tokens).</summary>
    public class AiStatus
    {
        public AiPhase Phase { get; set; } = AiPhase.Idle;
        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public int Attempt { get; set; }
        public int TotalProviders { get; set; }
        public string Message { get; set; } = string.Empty;
        public int LastCallTokens { get; set; }

        public string Describe() => Phase switch
        {
            AiPhase.Idle => "AI idle",
            AiPhase.Disabled => "AI disabled",
            AiPhase.Trying => $"Using {ProviderName} ({Model}) — attempt {Attempt}/{TotalProviders}…",
            AiPhase.Succeeded => $"✓ {ProviderName} ({Model}) — {LastCallTokens} tokens",
            AiPhase.FellBack => $"⤵ {ProviderName} failed, trying next…",
            AiPhase.Exhausted => "✗ All AI providers failed",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Runs an AI request through the configured fallback chain: tries each enabled provider in
    /// order until one succeeds, records token usage, and reports live status via <see cref="StatusChanged"/>.
    /// </summary>
    public class AiRouter
    {
        private readonly AiSettings _settings;
        private readonly TokenUsageStore _usage;

        /// <summary>Raised on every phase transition (marshal to the UI thread in the handler).</summary>
        public event Action<AiStatus>? StatusChanged;

        public AiStatus Current { get; private set; } = new AiStatus();

        public AiRouter(AiSettings settings, TokenUsageStore usage)
        {
            _settings = settings;
            _usage = usage;
            _usage.Persist = settings.SaveTokenHistory;
        }

        public bool IsConfigured => _settings.Enabled && _settings.FallbackChain.Any();

        public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
        {
            if (!_settings.Enabled)
            {
                Emit(new AiStatus { Phase = AiPhase.Disabled, Message = "AI is turned off in settings." });
                return AiResponse.Fail("AI disabled.");
            }

            var chain = _settings.FallbackChain.ToList();
            if (chain.Count == 0)
            {
                Emit(new AiStatus { Phase = AiPhase.Disabled, Message = "No AI providers configured." });
                return AiResponse.Fail("No AI providers configured.");
            }

            AiResponse last = AiResponse.Fail("No attempt made.");
            for (int i = 0; i < chain.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var profile = chain[i];

                Emit(new AiStatus
                {
                    Phase = AiPhase.Trying,
                    ProviderId = profile.Id,
                    ProviderName = profile.Name,
                    Model = profile.Model,
                    Attempt = i + 1,
                    TotalProviders = chain.Count
                });

                var client = AiClientFactory.Create(profile);
                last = await client.CompleteAsync(request, ct).ConfigureAwait(false);

                _usage.Record(new AiUsageRecord
                {
                    TimestampUtc = DateTime.UtcNow,
                    ProviderId = profile.Id,
                    ProviderName = profile.Name,
                    Model = profile.Model,
                    Operation = request.Operation,
                    InputTokens = last.InputTokens,
                    OutputTokens = last.OutputTokens,
                    Success = last.Success,
                    ElapsedMs = last.ElapsedMs
                });

                if (last.Success)
                {
                    Emit(new AiStatus
                    {
                        Phase = AiPhase.Succeeded,
                        ProviderId = profile.Id,
                        ProviderName = profile.Name,
                        Model = profile.Model,
                        Attempt = i + 1,
                        TotalProviders = chain.Count,
                        LastCallTokens = last.TotalTokens
                    });
                    return last;
                }

                // failed - report and fall back to the next provider (if any)
                Emit(new AiStatus
                {
                    Phase = i + 1 < chain.Count ? AiPhase.FellBack : AiPhase.Exhausted,
                    ProviderId = profile.Id,
                    ProviderName = profile.Name,
                    Model = profile.Model,
                    Attempt = i + 1,
                    TotalProviders = chain.Count,
                    Message = last.Error ?? "Unknown error"
                });
            }

            return last;
        }

        /// <summary>One-shot connectivity test for a single profile (used by the config screen's "Test").</summary>
        public static async Task<AiResponse> TestAsync(AiProviderProfile profile, CancellationToken ct = default)
        {
            var client = AiClientFactory.Create(profile);
            return await client.CompleteAsync(new AiRequest
            {
                System = "You are a connectivity test.",
                Prompt = "Reply with the single word: OK",
                MaxTokensOverride = 16,
                Operation = "connection-test"
            }, ct).ConfigureAwait(false);
        }

        private void Emit(AiStatus status)
        {
            Current = status;
            StatusChanged?.Invoke(status);
        }
    }
}
