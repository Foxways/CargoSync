using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Ai
{
    /// <summary>A client that can talk to one configured AI provider.</summary>
    public interface IAiClient
    {
        AiProviderProfile Profile { get; }

        /// <summary>
        /// Run a completion. Implementations should NOT throw for HTTP/API errors -
        /// return an <see cref="AiResponse"/> with Success=false and Error set, so the
        /// router can fall back to the next provider.
        /// </summary>
        Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default);

        /// <summary>
        /// Whether this provider's wire format can carry the request at all (e.g. PDF
        /// attachments are Anthropic-only). The router skips unsupported providers without
        /// counting a failure. A provider whose MODEL lacks vision still fails at the API
        /// and falls back through the chain as normal.
        /// </summary>
        bool Supports(AiRequest request) => true;
    }
}
