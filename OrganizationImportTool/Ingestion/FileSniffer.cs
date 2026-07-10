using System;
using System.IO;
using System.Text;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>What a file actually contains, regardless of its extension.</summary>
    public enum FileKind
    {
        Unknown,
        /// <summary>Zip container - OOXML Excel (.xlsx/.xlsm) and friends.</summary>
        ZipBased,
        /// <summary>OLE2 compound document - legacy Excel 97-2003 (.xls).</summary>
        LegacyOle2,
        Pdf,
        Image,
        Json,
        Xml,
        DelimitedText
    }

    /// <summary>
    /// Identifies a file by its content (magic numbers / leading text), not its extension.
    /// Lets the reader factory route mislabeled files correctly - e.g. JSON saved as .txt,
    /// or an OOXML workbook a client renamed to .xls - and reject formats we can't read
    /// (legacy .xls, PDF, images) with a friendly message instead of a parser crash.
    /// </summary>
    public static class FileSniffer
    {
        private const int ProbeLength = 4096;

        public static FileKind Sniff(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            // FileShare.ReadWrite for the same reason as the readers: the file may be open in Excel / OneDrive.
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[ProbeLength];
            int n = fs.Read(head, 0, head.Length);
            return SniffBytes(head, n);
        }

        public static FileKind SniffBytes(byte[] head, int length)
        {
            if (length <= 0) return FileKind.Unknown;

            // ---- binary signatures ----
            if (StartsWith(head, length, 0x50, 0x4B)) return FileKind.ZipBased;                               // "PK"
            if (StartsWith(head, length, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1)) return FileKind.LegacyOle2;
            if (IndexOfAscii(head, length, "%PDF-", 1024) >= 0) return FileKind.Pdf;                          // spec allows junk before the marker
            if (StartsWith(head, length, 0x89, 0x50, 0x4E, 0x47)) return FileKind.Image;                      // PNG
            if (StartsWith(head, length, 0xFF, 0xD8, 0xFF)) return FileKind.Image;                            // JPEG
            if (StartsWith(head, length, (byte)'G', (byte)'I', (byte)'F', (byte)'8')) return FileKind.Image;  // GIF
            if (StartsWith(head, length, 0x49, 0x49, 0x2A, 0x00)) return FileKind.Image;                      // TIFF LE
            if (StartsWith(head, length, 0x4D, 0x4D, 0x00, 0x2A)) return FileKind.Image;                      // TIFF BE
            if (length >= 12 && StartsWith(head, length, (byte)'R', (byte)'I', (byte)'F', (byte)'F')
                && head[8] == 'W' && head[9] == 'E' && head[10] == 'B' && head[11] == 'P') return FileKind.Image;
            // BMP: "BM" can also start a text file, so require the reserved bytes (6-9) to be zero.
            if (length >= 10 && head[0] == 'B' && head[1] == 'M'
                && head[6] == 0 && head[7] == 0 && head[8] == 0 && head[9] == 0) return FileKind.Image;

            // ---- text classification ----
            string text = DecodeHead(head, length);
            if (text.Length == 0) return FileKind.Unknown;

            // Binary that matched no signature: NUL bytes never appear in legitimate text data.
            if (text.IndexOf('\0') >= 0) return FileKind.Unknown;

            string trimmed = text.TrimStart();
            if (trimmed.Length == 0) return FileKind.Unknown;
            char first = trimmed[0];
            if (first == '{' || first == '[') return FileKind.Json;
            if (first == '<') return FileKind.Xml;

            return MostlyPrintable(text) ? FileKind.DelimitedText : FileKind.Unknown;
        }

        private static bool StartsWith(byte[] head, int length, params byte[] sig)
        {
            if (length < sig.Length) return false;
            for (int i = 0; i < sig.Length; i++)
                if (head[i] != sig[i]) return false;
            return true;
        }

        private static int IndexOfAscii(byte[] head, int length, string marker, int within)
        {
            int limit = Math.Min(length, within) - marker.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool hit = true;
                for (int j = 0; j < marker.Length; j++)
                    if (head[i + j] != (byte)marker[j]) { hit = false; break; }
                if (hit) return i;
            }
            return -1;
        }

        /// <summary>Decode the probe respecting a BOM; tolerate a multi-byte char cut off at the end.</summary>
        private static string DecodeHead(byte[] head, int length)
        {
            Encoding enc = Encoding.UTF8;
            int offset = 0;
            if (length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) { offset = 3; }
            else if (length >= 2 && head[0] == 0xFF && head[1] == 0xFE) { enc = Encoding.Unicode; offset = 2; }
            else if (length >= 2 && head[0] == 0xFE && head[1] == 0xFF) { enc = Encoding.BigEndianUnicode; offset = 2; }

            try
            {
                var lenient = (Encoding)enc.Clone();
                lenient.DecoderFallback = new DecoderReplacementFallback(" ");
                return lenient.GetString(head, offset, length - offset);
            }
            catch { return string.Empty; }
        }

        private static bool MostlyPrintable(string text)
        {
            int control = 0;
            foreach (char c in text)
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') control++;
            return control <= text.Length / 10;
        }
    }
}
