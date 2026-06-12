using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OrganizationImportTool.Mapping
{
    /// <summary>
    /// One CargoWise target field as described by CargoWiseOrganizationFields.json.
    /// The mapping engine, validation UI and XML generator all read from these.
    /// </summary>
    public class ContractField
    {
        public string Path { get; set; } = string.Empty;
        public string Group { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Type { get; set; } = "string";
        public bool Required { get; set; }
        public int? MaxLength { get; set; }
        public string? Enum { get; set; }
        public string? RefTable { get; set; }
        public string? Default { get; set; }

        /// <summary>True when this field is normally populated from the client's file (so it's auto-suggested).</summary>
        public bool MapFromFile { get; set; } = true;

        public List<string> Aliases { get; set; } = new List<string>();

        /// <summary>Field sits inside a repeating collection (path contains "[]").</summary>
        public bool IsCollectionItem => Path.Contains("[]");

        /// <summary>Human-friendly "Group ▸ Label" used in the mapping dropdown.</summary>
        public string DisplayName => string.IsNullOrEmpty(Group) ? Label : $"{Group} ▸ {Label}";
    }

    public class ContractGroup
    {
        public string Key { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public int Order { get; set; }
    }

    /// <summary>
    /// The full, data-driven CargoWise organization field contract loaded from JSON.
    /// Adding a field is a JSON edit - no code change.
    /// </summary>
    public class FieldContract
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string OwnerCodeDefault { get; set; } = "CENGLOBAL";
        public List<ContractGroup> Groups { get; set; } = new List<ContractGroup>();
        public List<ContractField> Fields { get; set; } = new List<ContractField>();

        /// <summary>Enum name -> allowed CargoWise codes (parsed from the leading token of each option).</summary>
        public Dictionary<string, List<string>> Enums { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Allowed codes for a field's enum, or null if the field is not an enum.</summary>
        public List<string>? AllowedEnumCodes(ContractField field)
            => field.Enum != null && Enums.TryGetValue(field.Enum, out var codes) ? codes : null;

        /// <summary>Fields the operator can map data into (excludes fixed/system defaults).</summary>
        public IEnumerable<ContractField> MappableFields => Fields.Where(f => f.MapFromFile);

        public IEnumerable<ContractField> RequiredFields => Fields.Where(f => f.Required);

        public ContractField? FindByPath(string path) =>
            Fields.FirstOrDefault(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));

        /// <summary>Default location of the contract JSON next to the executable.</summary>
        public static string DefaultPath =>
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mapping", "CargoWiseOrganizationFields.json");

        public static FieldContract Load(string? path = null)
        {
            path ??= DefaultPath;
            if (!File.Exists(path))
                throw new FileNotFoundException("CargoWise field contract not found.", path);

            string json = File.ReadAllText(path);
            return Parse(json);
        }

        /// <summary>
        /// Tolerant parse: we only read the keys we care about, so unknown keys
        /// (envelope, notes, enum option lists, etc.) are simply ignored.
        /// </summary>
        public static FieldContract Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var contract = new FieldContract();

            if (root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.String)
                contract.SchemaVersion = sv.GetString() ?? string.Empty;
            if (root.TryGetProperty("ownerCodeDefault", out var oc) && oc.ValueKind == JsonValueKind.String)
                contract.OwnerCodeDefault = oc.GetString() ?? "CENGLOBAL";

            if (root.TryGetProperty("enums", out var enums) && enums.ValueKind == JsonValueKind.Object)
            {
                foreach (var e in enums.EnumerateObject())
                {
                    if (e.Value.ValueKind != JsonValueKind.Array) continue;
                    var codes = new List<string>();
                    foreach (var opt in e.Value.EnumerateArray())
                    {
                        if (opt.ValueKind != JsonValueKind.String) continue;
                        // option strings look like "OFC (Office/Main)" - take the leading code token.
                        string s = (opt.GetString() ?? string.Empty).Trim();
                        int sp = s.IndexOfAny(new[] { ' ', '(' });
                        codes.Add(sp > 0 ? s.Substring(0, sp) : s);
                    }
                    contract.Enums[e.Name] = codes;
                }
            }

            if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groups.EnumerateArray())
                {
                    contract.Groups.Add(new ContractGroup
                    {
                        Key = GetString(g, "key"),
                        Label = GetString(g, "label"),
                        Order = GetInt(g, "order") ?? 0
                    });
                }
            }

            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fields.EnumerateArray())
                {
                    var field = new ContractField
                    {
                        Path = GetString(f, "path"),
                        Group = GetString(f, "group"),
                        Label = GetString(f, "label"),
                        Type = GetString(f, "type", "string"),
                        Required = GetBool(f, "required"),
                        MaxLength = GetInt(f, "maxLength"),
                        Enum = GetNullableString(f, "enum"),
                        RefTable = GetNullableString(f, "refTable"),
                        Default = GetDefaultAsString(f),
                        MapFromFile = GetBool(f, "mapFromFile", defaultValue: true),
                        Aliases = GetStringList(f, "aliases")
                    };
                    if (string.IsNullOrWhiteSpace(field.Label) && !string.IsNullOrWhiteSpace(field.Path))
                        field.Label = field.Path;
                    contract.Fields.Add(field);
                }
            }

            return contract;
        }

        // ---- small JSON helpers (defensive against missing / wrong-typed keys) ----

        private static string GetString(JsonElement e, string name, string fallback = "")
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? fallback) : fallback;

        private static string? GetNullableString(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool GetBool(JsonElement e, string name, bool defaultValue = false)
        {
            if (!e.TryGetProperty(name, out var v)) return defaultValue;
            return v.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => defaultValue
            };
        }

        private static int? GetInt(JsonElement e, string name)
            => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : (int?)null;

        private static List<string> GetStringList(JsonElement e, string name)
        {
            var list = new List<string>();
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var item in v.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.String)
                        list.Add(item.GetString() ?? string.Empty);
            return list;
        }

        /// <summary>Default may be a string, bool or number in JSON; normalise to a string for XML emission.</summary>
        private static string? GetDefaultAsString(JsonElement e)
        {
            if (!e.TryGetProperty("default", out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            };
        }
    }
}
