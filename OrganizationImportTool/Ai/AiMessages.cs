using System.Collections.Generic;

namespace OrganizationImportTool.Ai
{
    /// <summary>What kind of binary content an attachment carries.</summary>
    public enum AiAttachmentKind { Image, Pdf }

    /// <summary>A binary attachment (image or PDF) sent alongside a prompt for vision extraction.</summary>
    public class AiAttachment
    {
        public AiAttachmentKind Kind { get; set; } = AiAttachmentKind.Image;
        /// <summary>MIME type, e.g. "image/png", "application/pdf".</summary>
        public string MediaType { get; set; } = "image/png";
        public string Base64Data { get; set; } = string.Empty;
    }

    /// <summary>A single AI completion request (provider-agnostic).</summary>
    public class AiRequest
    {
        public string System { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public int? MaxTokensOverride { get; set; }
        public double? TemperatureOverride { get; set; }

        /// <summary>
        /// Optional binary attachments (vision). Empty for normal text requests - all existing
        /// call sites are unaffected. Providers that cannot carry an attachment kind are skipped
        /// by the router (see <see cref="IAiClient.Supports"/>).
        /// </summary>
        public List<AiAttachment> Attachments { get; set; } = new List<AiAttachment>();

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
