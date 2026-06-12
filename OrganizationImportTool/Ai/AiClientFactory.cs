using System;

namespace OrganizationImportTool.Ai
{
    /// <summary>Builds the right <see cref="IAiClient"/> for a provider profile's wire format.</summary>
    public static class AiClientFactory
    {
        public static IAiClient Create(AiProviderProfile profile) => profile.Kind switch
        {
            AiProviderKind.Anthropic => new AnthropicAiClient(profile),
            AiProviderKind.OpenAiCompatible => new OpenAiCompatibleClient(profile),
            _ => throw new NotSupportedException($"Unknown provider kind: {profile.Kind}")
        };
    }
}
