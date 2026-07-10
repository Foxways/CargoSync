using System.Text;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Turns machine-style keys from JSON/XML into the word-spaced headers the mapping
    /// engine matches best: "fullName" / "FullName" / "full_name" all become "full Name" /
    /// "Full Name" / "full name", and acronyms survive ("ARClientNumber" -> "AR Client Number").
    /// </summary>
    internal static class HeaderText
    {
        public static string Humanize(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;

            var sb = new StringBuilder(key.Length + 8);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c == '_' || c == '-' || c == '.') c = ' ';

                if (char.IsUpper(c) && sb.Length > 0 && i > 0)
                {
                    char prev = key[i - 1];
                    bool lowerToUpper = char.IsLower(prev) || char.IsDigit(prev);
                    bool acronymEnd = char.IsUpper(prev) && i + 1 < key.Length && char.IsLower(key[i + 1]);
                    if ((lowerToUpper || acronymEnd) && sb[sb.Length - 1] != ' ')
                        sb.Append(' ');
                }

                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>Join a parent prefix and a child part into one header.</summary>
        public static string Join(string prefix, string part)
            => string.IsNullOrEmpty(prefix) ? part : prefix + " " + part;
    }
}
