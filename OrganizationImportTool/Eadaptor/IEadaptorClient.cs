using System.Threading;
using System.Threading.Tasks;

namespace OrganizationImportTool.Eadaptor
{
    /// <summary>Transport seam for the import pipeline - lets tests fake CargoWise responses.</summary>
    public interface IEadaptorClient
    {
        Task<EadaptorResponse> SendAsync(string nativeXml, CancellationToken ct = default);
    }
}
