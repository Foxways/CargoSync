using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using OrganizationImportTool.Mapping;
using OrganizationImportTool.Transform;

namespace OrganizationImportTool.Eadaptor
{
    /// <summary>
    /// Builds a CargoWise Native &lt;Organization&gt; document from a confirmed, flat
    /// path-&gt;value mapping for a single source row. Contract-driven: knows which paths are
    /// reference lookups (TableName + Code), which are booleans/dates/numbers (coerced via
    /// <see cref="ValueTransformer"/>), and supports multiple items per collection using an
    /// index in the path, e.g. orgAddressCollection[0]/[1] for main + postal addresses.
    /// </summary>
    public class OrganizationXmlBuilder
    {
        private static readonly XNamespace Ns = "http://www.cargowise.com/Schemas/Native/2011/11";
        private static readonly Regex IndexRx = new(@"\[\d+\]", RegexOptions.Compiled);
        private readonly FieldContract _contract;

        public OrganizationXmlBuilder(FieldContract contract) => _contract = contract;

        public string Build(IDictionary<string, string> values, string ownerCode, bool enableCodeMapping)
        {
            var orgHeader = new XElement(Ns + "OrgHeader", new XAttribute("Action", "MERGE"));

            // Per-collection item buckets, keyed by item index.
            var addresses = new SortedDictionary<int, AddressItem>();
            var contacts = new SortedDictionary<int, XElement>();
            var registrations = new SortedDictionary<int, XElement>();
            var companies = new SortedDictionary<int, XElement>();

            foreach (var kv in OrderForHeader(values))
            {
                string path = kv.Key;
                string raw = kv.Value;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (path.EndsWith(".action", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith("header.", StringComparison.OrdinalIgnoreCase)) continue; // envelope-level

                var field = FindField(path);

                if (path.StartsWith("orgHeader.", StringComparison.OrdinalIgnoreCase))
                {
                    EmitInto(orgHeader, path.Substring("orgHeader.".Length), raw, field);
                }
                else if (TryCollection(path, "orgAddressCollection", out int ai, out string arest))
                {
                    var item = addresses.TryGetValue(ai, out var ex) ? ex : (addresses[ai] = new AddressItem(ai));
                    if (TryCollection(arest, "orgAddressCapabilityCollection", out _, out string crest))
                    {
                        if (string.IsNullOrEmpty(crest)) continue; // path ends at collection boundary — no field to emit
                        item.Capability ??= new XElement(Ns + "OrgAddressCapability", new XAttribute("Action", "MERGE"));
                        EmitInto(item.Capability, crest, raw, field);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(arest)) continue;
                        EmitInto(item.Address, arest, raw, field);
                    }
                }
                else if (TryCollection(path, "orgContactCollection", out int ci, out string crest2))
                {
                    if (string.IsNullOrEmpty(crest2)) continue;
                    var c = contacts.TryGetValue(ci, out var ex) ? ex : (contacts[ci] = NewItem("OrgContact"));
                    EmitInto(c, crest2, raw, field);
                }
                else if (TryCollection(path, "orgRegistrationNumberCollection", out int ri, out string rrest))
                {
                    if (string.IsNullOrEmpty(rrest)) continue;
                    var rEl = registrations.TryGetValue(ri, out var ex) ? ex : (registrations[ri] = new XElement(Ns + "RegistrationNumber"));
                    EmitInto(rEl, rrest, raw, field);
                }
                else if (TryCollection(path, "orgCompanyDataCollection", out int coi, out string corest))
                {
                    if (string.IsNullOrEmpty(corest)) continue;
                    var co = companies.TryGetValue(coi, out var ex) ? ex : (companies[coi] = NewItem("OrgCompanyData"));
                    EmitInto(co, corest, raw, field);
                }
            }

            // Assemble addresses (each gets a capability - default it if none was mapped).
            if (addresses.Count > 0)
            {
                var coll = new XElement(Ns + "OrgAddressCollection");
                foreach (var item in addresses.Values)
                {
                    var cap = item.Capability ?? DefaultCapability(item.Index);
                    item.Address.Add(new XElement(Ns + "OrgAddressCapabilityCollection", cap));
                    coll.Add(item.Address);
                }
                orgHeader.Add(coll);
            }
            if (contacts.Count > 0)
                orgHeader.Add(new XElement(Ns + "OrgContactCollection", contacts.Values));
            if (registrations.Count > 0)
                orgHeader.Add(new XElement(Ns + "RegistrationNumberCollection", registrations.Values));
            if (companies.Count > 0)
                orgHeader.Add(new XElement(Ns + "OrgCompanyDataCollection", companies.Values));

            var header = new XElement(Ns + "Header", new XElement(Ns + "OwnerCode", ownerCode));
            if (enableCodeMapping)
                header.Add(new XElement(Ns + "EnableCodeMapping", "true"));

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement(Ns + "Native", new XAttribute("version", "2.0"),
                    header,
                    new XElement(Ns + "Body",
                        new XElement(Ns + "Organization", new XAttribute("version", "2.0"), orgHeader))));

            return doc.Declaration + Environment.NewLine + doc.ToString();
        }

        private XElement DefaultCapability(int index)
        {
            string type = FindField($"orgAddressCollection[{index}].orgAddressCapabilityCollection[].addressType")?.Default
                          ?? (index == 0 ? "OFC" : "PAD");
            return new XElement(Ns + "OrgAddressCapability", new XAttribute("Action", "MERGE"),
                new XElement(Ns + "AddressType", type),
                new XElement(Ns + "IsMainAddress", index == 0 ? "true" : "false"));
        }

        private static XElement NewItem(string name) => new XElement(Ns + name, new XAttribute("Action", "MERGE"));

        /// <summary>Emit one leaf (relative path within its container) - reference lookup or plain element.</summary>
        private void EmitInto(XElement parent, string rel, string value, ContractField? field)
        {
            bool isLookup = field?.RefTable != null || rel.EndsWith(".code", StringComparison.OrdinalIgnoreCase);
            if (isLookup)
            {
                string parentSeg = rel.Contains('.') ? rel.Substring(0, rel.LastIndexOf('.')) : rel;
                string elemName = ElementName(parentSeg);
                string code = ReferenceLookup.Resolve(field?.RefTable, value);
                var lookup = new XElement(Ns + elemName, new XElement(Ns + "Code", code));
                if (field?.RefTable != null && !elemName.Equals("GlbCompany", StringComparison.Ordinal))
                    lookup.SetAttributeValue("TableName", field.RefTable);
                parent.Add(lookup);
                return;
            }

            string name = ElementName(rel);
            string outVal = ValueTransformer.Coerce(field, value);
            parent.Add(new XElement(Ns + name, outVal));
        }

        /// <summary>Find the contract field for a path, falling back to the index-normalised ([n] -> []) form.</summary>
        private ContractField? FindField(string path)
            => _contract.FindByPath(path) ?? _contract.FindByPath(IndexRx.Replace(path, "[]"));

        /// <summary>Match "{collection}[idx]." at the start of <paramref name="path"/>; idx defaults to 0 for "[]".</summary>
        private static bool TryCollection(string path, string collection, out int index, out string rest)
        {
            index = 0; rest = string.Empty;
            string prefix = collection + "[";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            int close = path.IndexOf(']', prefix.Length - 1);
            if (close < 0) return false;
            string idxStr = path.Substring(prefix.Length, close - prefix.Length);
            index = string.IsNullOrEmpty(idxStr) ? 0 : (int.TryParse(idxStr, out var n) ? n : 0);
            // skip "]."
            rest = close + 2 <= path.Length ? path.Substring(close + 2) : string.Empty;
            return true;
        }

        private static string ElementName(string segment)
        {
            if (Overrides.TryGetValue(segment, out var name)) return name;
            if (string.IsNullOrEmpty(segment)) return segment;
            return char.ToUpperInvariant(segment[0]) + segment.Substring(1);
        }

        private static IEnumerable<KeyValuePair<string, string>> OrderForHeader(IDictionary<string, string> values)
        {
            int Rank(string p) => p switch
            {
                "orgHeader.code" => 0,
                "orgHeader.fullName" => 1,
                "orgHeader.isActive" => 2,
                _ => 5
            };
            return values.OrderBy(kv => Rank(kv.Key));
        }

        private sealed class AddressItem
        {
            public int Index { get; }
            public XElement Address { get; }
            public XElement? Capability { get; set; }
            public AddressItem(int index)
            {
                Index = index;
                Address = new XElement(Ns + "OrgAddress", new XAttribute("Action", "MERGE"));
            }
        }

        /// <summary>Element-name overrides where naive PascalCase is wrong (AR/AP/IM/EX/FW/GL prefixes, etc.).</summary>
        private static readonly Dictionary<string, string> Overrides = new(StringComparer.Ordinal)
        {
            ["isAirLine"] = "IsAirLine",
            ["isSeaCTO"] = "IsSeaCTO",
            ["isAirCTO"] = "IsAirCTO",
            ["isVGMContractor"] = "IsVGMContractor",
            ["countryCode"] = "CountryCode",
            ["relatedPortCode"] = "RelatedPortCode",
            ["closestPort"] = "ClosestPort",
            ["shippingLine"] = "ShippingLine",
            ["nationality"] = "Nationality",
            ["countryOfIssue"] = "CountryOfIssue",
            ["glbCompany"] = "GlbCompany",
            ["isDebtor"] = "IsDebtor",
            ["isCreditor"] = "IsCreditor",
            ["arDebtorGroup"] = "ARDebtorGroup",
            ["apCreditorGroup"] = "APCreditorGroup",
            ["arExternalDebtorCode"] = "ARExternalDebtorCode",
            ["apExternalCreditorCode"] = "APExternalCreditorCode",
            ["arClientNumber"] = "ARClientNumber",
            ["arCreditLimit"] = "ARCreditLimit",
            ["arCreditApproved"] = "ARCreditApproved",
            ["arCreditRating"] = "ARCreditRating",
            ["arOnCreditHold"] = "AROnCreditHold",
            ["arInvoiceTerms"] = "ARInvoiceTerms",
            ["arInvoiceTermDays"] = "ARInvoiceTermDays",
            ["apPaymentTerms"] = "APPaymentTerms",
            ["apPaymentTermDays"] = "APPaymentTermDays",
            ["apDefltCurrency"] = "APDefltCurrency",
            ["arDDefltCurrency"] = "ARDDefltCurrency",
            ["imImporterCategory"] = "IMImporterCategory",
            ["exExporterCategory"] = "EXExporterCategory",
            ["fwAgentCategory"] = "FWAgentCategory",
            ["imDefaultINCOTerm"] = "IMDefaultINCOTerm",
            ["exDefaultIncoTerm"] = "EXDefaultIncoTerm",
            ["exDefaultCntryOfOrigin"] = "EXDefaultCntryOfOrigin",
            ["fwDefCurrency"] = "FWDefCurrency",
        };
    }
}
