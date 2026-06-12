using OrganizationImportTool.Ai;
using Xunit;

namespace OrganizationImportTool.Tests
{
    public class AiRouterTests
    {
        private static AiSettings BrokenSettings()
        {
            // No BaseUrl/Model -> the client guard fails instantly, no network involved.
            var s = new AiSettings { Enabled = true };
            s.Providers.Add(new AiProviderProfile { Name = "Broken", Enabled = true });
            return s;
        }

        private static AiRequest Request() => new() { Prompt = "hi", Operation = "test" };

        [Fact]
        public async Task Disabled_settings_fail_fast()
        {
            var router = new AiRouter(new AiSettings { Enabled = false }, new TokenUsageStore { Persist = false });
            var resp = await router.CompleteAsync(Request());
            Assert.False(resp.Success);
            Assert.False(router.IsConfigured);
        }

        [Fact]
        public async Task Circuit_breaker_trips_after_repeated_failures_and_announces_offline_once()
        {
            var router = new AiRouter(BrokenSettings(), new TokenUsageStore { Persist = false });
            var phases = new List<AiPhase>();
            router.StatusChanged += s => phases.Add(s.Phase);

            await router.CompleteAsync(Request()); // failure 1
            Assert.False(router.AllProvidersDown);
            await router.CompleteAsync(Request()); // failure 2 -> breaker trips
            Assert.True(router.AllProvidersDown);

            // Tripped chain short-circuits: no Trying phase emitted, instant failure.
            phases.Clear();
            var resp = await router.CompleteAsync(Request());
            Assert.False(resp.Success);
            Assert.Contains("offline", resp.Error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(AiPhase.Trying, phases);
        }

        [Fact]
        public async Task ResetCircuits_re_arms_the_breaker()
        {
            var router = new AiRouter(BrokenSettings(), new TokenUsageStore { Persist = false });
            await router.CompleteAsync(Request());
            await router.CompleteAsync(Request());
            Assert.True(router.AllProvidersDown);

            router.ResetCircuits();
            Assert.False(router.AllProvidersDown);

            var phases = new List<AiPhase>();
            router.StatusChanged += s => phases.Add(s.Phase);
            await router.CompleteAsync(Request());
            Assert.Contains(AiPhase.Trying, phases); // providers are attempted again
        }

        [Fact]
        public async Task Offline_phase_is_emitted_exactly_once_per_run()
        {
            var router = new AiRouter(BrokenSettings(), new TokenUsageStore { Persist = false });
            var offlineCount = 0;
            router.StatusChanged += s => { if (s.Phase == AiPhase.Offline) offlineCount++; };

            for (int i = 0; i < 5; i++) await router.CompleteAsync(Request());
            Assert.Equal(1, offlineCount);
        }
    }
}
