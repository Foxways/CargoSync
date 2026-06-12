namespace OrganizationImportTool.Ai
{
    /// <summary>A single AI completion request (provider-agnostic).</summary>
    public class AiRequest
    {
        public string System { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public int? MaxTokensOverride { get; set; }
        public double? TemperatureOverride { get; set; }

        /// <summary>Short tag describing what this call is for (e.g. "mapping-suggest") - stored in usage history.</summary>
        public string Operation { get; set; } = "general";
    }

    /// <summary>The result of a completion, including token usage for tracking.</summary>
    public class AiResponse
    {
        public bool Success { get; set; }
        public string Text { get; set; } = string.Empty;
        public string? Error { get; set; }

        public string ProviderId { get; set; } = string.Empty;
        public string ProviderName { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;

        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens => InputTokens + OutputTokens;

        public long ElapsedMs { get; set; }

        public static AiResponse Fail(string error, string providerId = "", string providerName = "", string model = "")
            => new AiResponse { Success = false, Error = error, ProviderId = providerId, ProviderName = providerName, Model = model };
    }
}
