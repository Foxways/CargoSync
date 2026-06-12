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
    }
}
