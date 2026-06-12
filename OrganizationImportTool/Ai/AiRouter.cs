using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Ai
{
    public enum AiPhase
    {
        Idle, Trying, Succeeded, FellBack, Exhausted, Disabled,
        /// <summary>A provider failed repeatedly and is skipped for the rest of the run.</summary>
        ProviderDown,
        /// <summary>Every provider is down - AI is offline for the rest of the run (emitted once).</summary>
        Offline
    }

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
            AiPhase.ProviderDown => $"⛔ {ProviderName} is unavailable — skipping it for the rest of this run",
            AiPhase.Offline => "AI unavailable — continuing without AI",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Runs an AI request through the configured fallback chain: tries each enabled provider in
    /// order until one succeeds, records token usage, and reports live status via <see cref="StatusChanged"/>.
    /// Resilience: each attempt is capped at <see cref="AiSettings.OperationTimeoutSeconds"/>, and a
    /// circuit breaker trips a provider after repeated consecutive failures so an unreachable
    /// provider can never stall an import — calls fail instantly until <see cref="ResetCircuits"/>.
    /// </summary>
    public class AiRouter
    {
        private readonly AiSettings _settings;
        private readonly TokenUsageStore _usage;

        // Circuit breaker: per-provider consecutive-failure count + the set of tripped providers.
        private readonly object _circuitLock = new();
        private readonly Dictionary<string, int> _consecutiveFailures = new();
        private readonly HashSet<string> _tripped = new();
        private bool _offlineAnnounced;

        /// <summary>Consecutive failures before a provider is skipped for the rest of the run.</summary>
        public int TripThreshold { get; set; } = 2;

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

        /// <summary>True when every enabled provider has tripped its breaker this run.</summary>
        public bool AllProvidersDown
        {
            get
            {
                var chain = _settings.FallbackChain.ToList();
                lock (_circuitLock) return chain.Count > 0 && chain.All(p => _tripped.Contains(p.Id));
            }
        }

        /// <summary>Re-arm all breakers (called at the start of each import run).</summary>
        public void ResetCircuits()
        {
            lock (_circuitLock)
            {
                _consecutiveFailures.Clear();
                _tripped.Clear();
                _offlineAnnounced = false;
            }
        }

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

            // Whole chain already tripped? Fail instantly - no HTTP, no waiting.
            if (AllProvidersDown)
                return AiResponse.Fail("AI offline (all providers unavailable this run).");

            int timeoutSeconds = Math.Max(5, _settings.OperationTimeoutSeconds);
            AiResponse last = AiResponse.Fail("No attempt made.");
            for (int i = 0; i < chain.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var profile = chain[i];

                // Skip providers whose breaker has tripped this run.
                lock (_circuitLock) { if (_tripped.Contains(profile.Id)) continue; }

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

                // Per-attempt timeout budget: an unreachable provider must never stall the pipeline
                // for the full HTTP timeout. The linked CTS preserves outer (user Stop) cancellation.
                using (var opCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    opCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                    try
                    {
                        last = await client.CompleteAsync(request, opCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        last = AiResponse.Fail($"{profile.Name} timed out after {timeoutSeconds}s.");
                    }
                }

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
                    lock (_circuitLock) _consecutiveFailures[profile.Id] = 0;
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

                // Failure: count it, maybe trip the breaker, then fall back to the next provider.
                bool trippedNow = false;
                lock (_circuitLock)
                {
                    int fails = _consecutiveFailures.TryGetValue(profile.Id, out var f) ? f + 1 : 1;
                    _consecutiveFailures[profile.Id] = fails;
                    if (fails >= TripThreshold && _tripped.Add(profile.Id)) trippedNow = true;
                }
                if (trippedNow)
                {
                    Emit(new AiStatus
                    {
                        Phase = AiPhase.ProviderDown,
                        ProviderId = profile.Id,
                        ProviderName = profile.Name,
                        Model = profile.Model,
                        Message = last.Error ?? "Repeated failures"
                    });
                    AnnounceOfflineIfAllDown(chain);
                }

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

        private void AnnounceOfflineIfAllDown(List<AiProviderProfile> chain)
        {
            bool announce;
            lock (_circuitLock)
            {
                announce = !_offlineAnnounced && chain.All(p => _tripped.Contains(p.Id));
                if (announce) _offlineAnnounced = true;
            }
            if (announce)
                Emit(new AiStatus
                {
                    Phase = AiPhase.Offline,
                    Message = "All AI providers are unavailable — the import continues without AI."
                });
        }

        private void Emit(AiStatus status)
        {
            Current = status;
            StatusChanged?.Invoke(status);
        }
    }
}
