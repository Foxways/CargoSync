using OrganizationImportTool.Ingestion;
using OrganizationImportTool.Mapping;

namespace OrganizationImportTool.Tests
{
    /// <summary>Shared fixtures: the real field contract + in-memory source tables.</summary>
    public static class TestData
    {
        private static FieldContract? _contract;

        /// <summary>
        /// The live CargoWise field contract. Resolved from the test output directory
        /// (copied transitively from the main project) with a fallback to the source tree.
        /// </summary>
        public static FieldContract Contract
        {
            get
            {
                if (_contract != null) return _contract;
                string outputCopy = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mapping", "CargoWiseOrganizationFields.json");
                if (File.Exists(outputCopy)) return _contract = FieldContract.Load(outputCopy);

                // Fallback: walk up from bin/<cfg>/<tfm> to the repo root and read the source JSON.
                var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "OrganizationImportTool", "Mapping", "CargoWiseOrganizationFields.json");
                    if (File.Exists(candidate)) return _contract = FieldContract.Load(candidate);
                    dir = dir.Parent;
                }
                throw new FileNotFoundException("CargoWiseOrganizationFields.json not found for tests.");
            }
        }

        /// <summary>Build an in-memory SourceTable from headers + rows of cell values.</summary>
        public static SourceTable Table(string[] headers, params string[][] rows)
        {
            var t = new SourceTable { SourceName = "test", Headers = headers.ToList() };
            int n = 1;
            foreach (var cells in rows)
            {
                var row = new SourceRow { RowNumber = n++ };
                for (int i = 0; i < headers.Length && i < cells.Length; i++)
                    row[headers[i]] = cells[i];
                t.Rows.Add(row);
            }
            return t;
        }

        /// <summary>A unique temp file path that is deleted when the returned scope is disposed.</summary>
        public static TempFile TempDb() => new TempFile(".db");
        public static TempDir TempFolder() => new TempDir();
    }

    public sealed class TempFile : IDisposable
    {
        public string Path { get; }
        public TempFile(string extension)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oit_test_" + Guid.NewGuid().ToString("N") + extension);
        }
        public void Dispose()
        {
            try { if (File.Exists(Path)) File.Delete(Path); } catch { /* best effort */ }
        }
    }

    public sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oit_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
