using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using OrganizationImportTool.Security;

namespace OrganizationImportTool.Ai
{
    /// <summary>API wire-format family a provider speaks.</summary>
    public enum AiProviderKind
    {
        /// <summary>OpenAI /chat/completions schema - covers OpenAI, OpenRouter, Groq, Together, local LM Studio/Ollama, etc.</summary>
        OpenAiCompatible = 0,
        /// <summary>Anthropic /v1/messages schema.</summary>
        Anthropic = 1
    }

    /// <summary>
    /// One configured AI provider. The app can hold many; the enabled ones form an ordered
    /// fallback chain (see <see cref="AiSettings"/>). Any provider reachable by an API key
    /// and base URL works - including OpenRouter.
    /// </summary>
    public class AiProviderProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New Provider";
        public AiProviderKind Kind { get; set; } = AiProviderKind.OpenAiCompatible;

        /// <summary>Base URL without the path (e.g. https://openrouter.ai/api/v1, https://api.anthropic.com).</summary>
        public string BaseUrl { get; set; } = string.Empty;

        public string Model { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int MaxTokens { get; set; } = 1024;
        public double Temperature { get; set; } = 0.0;

        /// <summary>Extra HTTP headers (e.g. OpenRouter HTTP-Referer / X-Title).</summary>
        public Dictionary<string, string> ExtraHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>Encrypted-at-rest API key (DPAPI). This is what gets serialized.</summary>
        public string ApiKeyEncrypted { get; set; } = string.Empty;

        /// <summary>Plain API key for runtime use - never serialized; transparently (de)protected.</summary>
        [JsonIgnore]
        public string ApiKey
        {
            get => SecretProtector.Unprotect(ApiKeyEncrypted);
            set => ApiKeyEncrypted = SecretProtector.Protect(value);
        }

        public AiProviderProfile Clone() => new AiProviderProfile
        {
            Id = Id, Name = Name, Kind = Kind, BaseUrl = BaseUrl, Model = Model,
            Enabled = Enabled, MaxTokens = MaxTokens, Temperature = Temperature,
            ApiKeyEncrypted = ApiKeyEncrypted,
            ExtraHeaders = new Dictionary<string, string>(ExtraHeaders)
        };

        // ---- Quick-start templates for the config screen ----
        public static AiProviderProfile OpenAiTemplate() => new AiProviderProfile
        {
            Name = "OpenAI", Kind = AiProviderKind.OpenAiCompatible,
            BaseUrl = "https://api.openai.com/v1", Model = "gpt-4o-mini"
        };

        public static AiProviderProfile OpenRouterTemplate() => new AiProviderProfile
        {
            Name = "OpenRouter", Kind = AiProviderKind.OpenAiCompatible,
            BaseUrl = "https://openrouter.ai/api/v1", Model = "openai/gpt-4o-mini",
            ExtraHeaders = new Dictionary<string, string>
            {
                ["HTTP-Referer"] = "https://github.com/kishanmanohar/cargosync",
                ["X-Title"] = "CargoSync"
            }
        };

        public static AiProviderProfile AnthropicTemplate() => new AiProviderProfile
        {
            Name = "Anthropic (Claude)", Kind = AiProviderKind.Anthropic,
            BaseUrl = "https://api.anthropic.com", Model = "claude-sonnet-4-6"
        };
    }
}
