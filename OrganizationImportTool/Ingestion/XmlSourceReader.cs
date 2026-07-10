using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace OrganizationImportTool.Ingestion
{
    /// <summary>
    /// Reads XML (.xml) into a generic SourceTable. Auto-detects the repeating "record"
    /// element (most-repeated element with a consistent shape, preferring outer over nested
    /// repeats), with a special case for CargoWise Native exports where records are the
    /// OrgHeader elements. Flattening mirrors the JSON reader: nested elements become
    /// word-spaced headers, "...Collection" wrappers are skipped, repeated children get
    /// indexed columns, and attributes are captured alongside child elements.
    /// </summary>
    public class XmlSourceReader : ISourceReader
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".xml" };

        private const int MaxRepeats = 5;
        private const string CargoWiseNativeNs = "http://www.cargowise.com/Schemas/Native";

        public SourceTable Read(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Source file not found.", filePath);

            XDocument doc;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = XmlReader.Create(fs, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit, // no XXE / external entities
                    XmlResolver = null
                });
                doc = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException(
                    $"The file looks like XML but could not be parsed ({ex.Message}). " +
                    "If this came from a web page or report export, try saving the data as CSV or Excel instead.");
            }

            var root = doc.Root ?? throw new InvalidDataException("The XML file has no root element.");

            var (records, recordName) = LocateRecords(root);

            var acc = new TableAccumulator();
            foreach (var rec in records)
            {
                var row = acc.NewRecord();
                FlattenElement(rec, string.Empty, acc, row);
            }

            string fileName = Path.GetFileName(filePath);
            string name = string.IsNullOrEmpty(recordName) ? fileName : $"{fileName} ({recordName})";
            return acc.Build(filePath, name);
        }

        // ---------------- record location ----------------

        private static (List<XElement>, string) LocateRecords(XElement root)
        {
            // CargoWise Native export: records are the OrgHeader elements.
            if (root.Name.NamespaceName.StartsWith(CargoWiseNativeNs, StringComparison.OrdinalIgnoreCase))
            {
                var orgs = root.Descendants().Where(e => e.Name.LocalName == "OrgHeader").ToList();
                if (orgs.Count > 0) return (orgs, "OrgHeader");
            }

            // Generic: score every repeated element name (count x shape-consistency), skipping
            // groups nested inside another repeated group (contacts inside organizations).
            var groups = root.Descendants()
                .Where(IsRecordLike)
                .GroupBy(e => e.Name)
                .Where(g => g.Count() >= 2)
                .Select(g => new { Name = g.Key, Items = g.ToList(), Score = g.Count() * ShapeConsistency(g.ToList()) })
                .ToList();

            var repeatedNames = new HashSet<XName>(groups.Select(g => g.Name));
            var best = groups
                .Where(g => !g.Items[0].Ancestors().Any(a => repeatedNames.Contains(a.Name) && a.Name != g.Name))
                .OrderByDescending(g => g.Score)
                .FirstOrDefault();

            if (best != null)
                return (best.Items, best.Name.LocalName);

            // Nothing repeats: treat the whole document as a single record.
            return (new List<XElement> { root }, string.Empty);
        }

        /// <summary>A record carries structure - child elements or attributes - not just a text value.</summary>
        private static bool IsRecordLike(XElement e)
            => e.HasElements || e.Attributes().Any(a => !a.IsNamespaceDeclaration);

        /// <summary>1.0 when every item has the same child-element names; lower as shapes diverge.</summary>
        private static double ShapeConsistency(List<XElement> items)
        {
            var union = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sum = 0;
            foreach (var item in items)
            {
                foreach (var c in item.Elements()) { union.Add(c.Name.LocalName); sum++; }
            }
            if (union.Count == 0) return 0.1;
            return (double)sum / (items.Count * union.Count);
        }

        // ---------------- flattening ----------------

        private static void FlattenElement(XElement el, string prefix, TableAccumulator acc, Dictionary<string, string> row)
        {
            foreach (var attr in el.Attributes())
            {
                if (attr.IsNamespaceDeclaration) continue;
                acc.Set(row, HeaderText.Join(prefix, HeaderText.Humanize(attr.Name.LocalName)), attr.Value.Trim());
            }

            if (!el.HasElements)
            {
                string text = el.Value.Trim();
                if (text.Length > 0)
                    acc.Set(row, string.IsNullOrEmpty(prefix) ? HeaderText.Humanize(el.Name.LocalName) : prefix, text);
                return;
            }

            foreach (var group in el.Elements().GroupBy(c => c.Name.LocalName))
            {
                var items = group.ToList();
                if (items.Count == 1)
                {
                    var child = items[0];
                    // "...Collection" wrappers add noise, not meaning - flatten through them so
                    // OrgAddressCollection/OrgAddress[2]/City becomes "Org Address 2 City".
                    if (child.Name.LocalName.EndsWith("Collection", StringComparison.OrdinalIgnoreCase) && child.HasElements)
                        FlattenElement(child, prefix, acc, row);
                    else
                        FlattenElement(child, HeaderText.Join(prefix, HeaderText.Humanize(child.Name.LocalName)), acc, row);
                }
                else
                {
                    string groupHeader = HeaderText.Join(prefix, HeaderText.Humanize(group.Key));
                    for (int i = 0; i < items.Count && i < MaxRepeats; i++)
                        FlattenElement(items[i], $"{groupHeader} {i + 1}", acc, row);
                }
            }
        }
    }
}
