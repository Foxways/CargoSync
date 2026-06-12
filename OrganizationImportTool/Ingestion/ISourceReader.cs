using System.Collections.Generic;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads a client-supplied data file into a generic <see cref="SourceTable"/>.
    /// Implementations must NOT enforce any particular set of headers - they load
    /// whatever structure the file has. Validation against CargoWise happens later.
    /// </summary>
    public interface ISourceReader
    {
        /// <summary>File extensions this reader handles, lower-case, with leading dot (e.g. ".xlsx").</summary>
        IEnumerable<string> SupportedExtensions { get; }

        /// <summary>Reads the file into a SourceTable. Throws on unreadable/corrupt files.</summary>
        SourceTable Read(string filePath);
    }
}
