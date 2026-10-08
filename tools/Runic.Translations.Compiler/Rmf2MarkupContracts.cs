using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Runic.Translations.Compiler;

internal sealed class Rmf2MarkupRegistry
{
    private static readonly string[] ContractMembers = { "name", "kind", "placement", "options", "children", "interactive", "plainText" };
    private static readonly string[] OptionMembers = { "type", "values", "default", "literalOnly", "minimum", "maximum", "description" };
    // "structure" is reserved for a future per-message structure policy (W220-003 section 6).
    private static readonly string[] RegistryMembers = { "contracts", "aliases", "slots", "structure" };
    internal static readonly Regex Name = new("^[A-Za-z_][A-Za-z0-9_-]*(?::[A-Za-z_][A-Za-z0-9_-]*)?$", RegexOptions.CultureInvariant);
    internal readonly Dictionary<string, Contract> Contracts = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    internal JsonValue? SlotConstraints;
    internal Rmf2MarkupRegistry()
    {
        foreach (string name in new[] { "strong", "em", "bold", "italic", "code", "br", "link", "action", "icon" })
        {
            Contracts.Add("runic:" + name, new Contract("runic:" + name, name is "br" or "icon", name is "link" or "action",
                name == "br" ? "lineBreak" : name == "icon" ? "alternateText" : name == "action" ? "explicit" : "children", new Dictionary<string, Option>(StringComparer.Ordinal),
                "inline", name is "br" or "icon" ? "none" : "inline"));
            Aliases.Add(name, "runic:" + name);
        }
        // The document profile v1 vocabulary (specs/translations/rmf2-document-profile-v1.md) is
        // always on. Every option is literal-only and structural.
        var none = new Dictionary<string, Option>(StringComparer.Ordinal);
        AddBlock("p", "block", "inline", none);
        AddBlock("h", "block", "inline", new(StringComparer.Ordinal) { ["level"] = new("integer", Array.Empty<string>(), null, true, 1, 6) });
        AddBlock("ul", "block", "list-items", none);
        AddBlock("ol", "block", "list-items", new(StringComparer.Ordinal)
        {
            ["start"] = new("integer", Array.Empty<string>(), "1", true, 1, int.MaxValue),
            ["marker"] = new("enum", ["decimal", "lower-alpha", "upper-alpha", "lower-roman", "upper-roman"], "decimal", true),
        });
        AddBlock("li", "list-item", "inline", none);
        void AddBlock(string name, string placement, string children, Dictionary<string, Option> options)
        {
            Contracts.Add("runic:" + name, new Contract("runic:" + name, false, false, "children", new Dictionary<string, Option>(options, StringComparer.Ordinal), placement, children));
            Aliases.Add(name, "runic:" + name);
        }
    }
    internal static Rmf2MarkupRegistry Read(JsonProperty? property, TranslationSource source, DiagnosticBag diagnostics)
    {
        var registry = new Rmf2MarkupRegistry();
        if (property is null) return registry;
        JsonValue root = property.Value;
        if (root.Kind != JsonKind.Object) { Error("markup must be an object.", root); return registry; }
        Known(root, RegistryMembers);
        if (root.Property("structure") is { } structure) Error("markup.structure is reserved for a future structure policy; document structure is locked.", structure.Value);
        registry.SlotConstraints = root.Property("slots")?.Value;
        JsonProperty? declarations = root.Property("contracts");
        if (declarations is not null)
        {
            if (declarations.Value.Kind != JsonKind.Array) Error("markup.contracts must be an array.", declarations.Value);
            else foreach (JsonValue item in declarations.Value.Items)
            {
                if (item.Kind != JsonKind.Object) { Error("Markup declaration must be an object.", item); continue; }
                Known(item, ContractMembers);
                string name = String(item, "name"), kind = String(item, "kind"), plain = String(item, "plainText"), children = String(item, "children");
                string placement = item.Property("placement") is { } placed ? placed.Value.Text ?? "" : "inline";
                bool interactive = item.Property("interactive")?.Value.Kind == JsonKind.True;
                if (!Name.IsMatch(name) || !name.Contains(':', StringComparison.Ordinal) || name.StartsWith("runic:", StringComparison.Ordinal) || kind is not ("paired" or "standalone") ||
                    plain is not ("children" or "lineBreak" or "alternateText" or "explicit" or "omit") || children != (kind == "standalone" ? "none" : "inline"))
                { Error("Invalid custom markup name, kind, child model or plainText policy.", item); continue; }
                // Custom block contracts are designed (W220-003) but delivered later (W220-008).
                if (placement != "inline") { Error("Custom markup placement must be 'inline'; custom block contracts are not supported yet.", item.Property("placement")!.Value); continue; }
                var options = new Dictionary<string, Option>(StringComparer.Ordinal);
                JsonProperty? schemas = item.Property("options");
                if (schemas is not null)
                {
                    if (schemas.Value.Kind != JsonKind.Object) Error("Markup options must be an object.", schemas.Value);
                    else foreach (JsonProperty option in schemas.Value.Properties)
                    {
                        if (!Rmf2ResourceReader.Identifier.IsMatch(option.Name) || option.Value.Kind != JsonKind.Object) { Error("Invalid markup option schema.", option.Value); continue; }
                        Known(option.Value, OptionMembers);
                        string type = String(option.Value, "type");
                        string[] values = option.Value.Property("values")?.Value.Items.Select(v => v.Text ?? "").ToArray() ?? Array.Empty<string>();
                        string? fallback = option.Value.Property("default")?.Value.Text;
                        if (type is not ("string" or "number" or "integer" or "boolean" or "enum") || (type == "enum" && values.Length == 0)) Error("Unsupported markup option type.", option.Value);
                        long minimum = int.MinValue, maximum = int.MaxValue;
                        foreach (string bound in new[] { "minimum", "maximum" })
                        {
                            if (option.Value.Property(bound) is not { } member) continue;
                            if (type != "integer") { Error("Markup option '" + bound + "' is valid only on integer options.", member.Value); continue; }
                            if (member.Value.Kind != JsonKind.Number || !long.TryParse(member.Value.Text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long parsed) ||
                                parsed < int.MinValue || parsed > int.MaxValue) { Error("Integer option bounds must be integers in the signed 32-bit range.", member.Value); continue; }
                            if (bound == "minimum") minimum = parsed; else maximum = parsed;
                        }
                        if (minimum > maximum) Error("Integer option minimum must not exceed maximum.", option.Value);
                        options.Add(option.Name, new Option(type, values, fallback, option.Value.Property("literalOnly")?.Value.Kind == JsonKind.True, minimum, maximum));
                    }
                }
                if (!registry.Contracts.TryAdd(name, new Contract(name, kind == "standalone", interactive, plain, options, placement, children))) Error("Duplicate markup contract.", item);
            }
        }
        JsonProperty? aliases = root.Property("aliases");
        if (aliases is not null)
        {
            if (aliases.Value.Kind != JsonKind.Object) Error("markup.aliases must be an object.", aliases.Value);
            else foreach (JsonProperty alias in aliases.Value.Properties)
                if (!Name.IsMatch(alias.Name) || alias.Name.Contains(':', StringComparison.Ordinal) || alias.Value.Kind != JsonKind.String ||
                    !registry.Contracts.ContainsKey(alias.Value.Text!) || !registry.Aliases.TryAdd(alias.Name, alias.Value.Text!)) Error("Alias is ambiguous, shadows a default, or names an unknown contract.", alias.Value);
        }
        return registry;
        void Error(string message, JsonValue value) => diagnostics.Add("RTR0060", TranslationDiagnosticSeverity.Error, message, source, value.Span);
        void Known(JsonValue value, string[] names)
        { foreach (JsonProperty member in value.Properties) if (!names.Contains(member.Name, StringComparer.Ordinal)) Error("Unknown markup contract member '" + member.Name + "'.", member.Value); }
        static string String(JsonValue value, string key) => value.Property(key)?.Value.Text ?? "";
    }

    internal IReadOnlyDictionary<string, string> Validate(Mf2ParsedMessage message, Rmf2ResourceNode resource, DiagnosticBag diagnostics)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        var variants = message.Message.IsVariant ? message.Message.Variants.Select(v => v.Pattern.Nodes) : new[] { message.Message.Nodes };
        foreach (IReadOnlyList<CompiledMessageNode> nodes in variants) Visit(nodes, false, 0);
        return slots;
        void Error(string text) => diagnostics.Add("RTR0061", TranslationDiagnosticSeverity.Error, text, resource.NameLocation);
        void Visit(IReadOnlyList<CompiledMessageNode> nodes, bool interactiveParent, int depth)
        {
            if (depth > 16) { Error("Inline markup nesting exceeds 16 levels."); return; }
            foreach (CompiledMessageMarkup tag in nodes.OfType<CompiledMessageMarkup>())
            {
                tag.Rmf2 = true;
                string canonical = Aliases.TryGetValue(tag.Name, out string? alias) ? alias : tag.Name;
                if (!Contracts.TryGetValue(canonical, out Contract? contract)) { Error("Unknown markup '" + tag.Name + "'. Register its language-neutral contract."); continue; }
                tag.Name = canonical;
                if (tag.Standalone != contract.Standalone) Error("Markup '" + canonical + "' requires " + (contract.Standalone ? "standalone" : "paired") + " syntax.");
                if (contract.Interactive && interactiveParent) Error("Interactive markup cannot be nested inside another interactive element.");
                var options = new SortedDictionary<string, string>(tag.Attributes.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);
                bool functional = canonical is "runic:link" or "runic:action" or "runic:icon";
                if (functional)
                {
                    string slot = options.TryGetValue("ref", out string? value) ? value : canonical == "runic:icon" ? "" : canonical.Substring(6);
                    if (!Name.IsMatch(slot) || tag.VariableOptions.Contains("ref")) Error("Functional markup ref must be a static slot ID; icon requires an explicit ref.");
                    else if (slots.TryGetValue(slot, out string? kind) && kind != canonical) Error("Slot '" + slot + "' has conflicting kinds.");
                    else slots[slot] = canonical;
                    options["ref"] = slot;
                }
                foreach (var option in options)
                {
                    if (functional && option.Key == "ref") continue;
                    if (!contract.Options.TryGetValue(option.Key, out Option? schema)) { Error("Unknown option '" + option.Key + "' on '" + canonical + "'."); continue; }
                    if (tag.VariableOptions.Contains(option.Key))
                    {
                        string input = option.Value;
                        PlaceholderModel? descriptor = Array.Find(message.Placeholders, p => p.Name == input);
                        if (schema.LiteralOnly || descriptor is null || !schema.AcceptsType(descriptor.Type)) Error("Markup option '" + option.Key + "' has an incompatible variable input.");
                    }
                    else if (!schema.Accepts(option.Value)) Error("Invalid literal value for markup option '" + option.Key + "'.");
                }
                foreach (var option in contract.Options)
                    if (!options.ContainsKey(option.Key))
                    { if (option.Value.Default is null) Error("Missing required markup option '" + option.Key + "'."); else if (!option.Value.Accepts(option.Value.Default)) Error("Invalid markup option default."); else options[option.Key] = option.Value.Default; }
                tag.Attributes = options;
                Visit(tag.Children, interactiveParent || contract.Interactive, depth + 1);
            }
        }
    }

    internal sealed record Contract(string Name, bool Standalone, bool Interactive, string PlainText, Dictionary<string, Option> Options,
        string Placement, string Children);
    internal sealed record Option(string Type, string[] Values, string? Default, bool LiteralOnly, long Minimum = int.MinValue, long Maximum = int.MaxValue)
    {
        internal bool Accepts(string value) => Type switch
        {
            "enum" => Values.Contains(value, StringComparer.Ordinal),
            "number" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number),
            "integer" => Rmf2IntegerOption.Accepts(value, Minimum, Maximum),
            "boolean" => value is "true" or "false",
            _ => true,
        };
        internal bool AcceptsType(TranslationArgumentType type) => Type switch { "number" => type is TranslationArgumentType.Int or TranslationArgumentType.Number, "integer" => type == TranslationArgumentType.Int, "boolean" => type == TranslationArgumentType.Boolean, _ => type == TranslationArgumentType.String };
    }
}

// The markup-contract v2 integer option rule, shared with the .NET runtime and the ESM
// markupLiteral: canonical decimal text only (no sign on zero, no leading zeros, no
// fraction or exponent), within the declared signed 32-bit bounds.
internal static class Rmf2IntegerOption
{
    internal static bool Accepts(string text, long minimum, long maximum)
    {
        if (text.Length == 0 || text.Length > 11) return false;
        int start = text[0] == '-' ? 1 : 0;
        if (start == text.Length || (text[start] == '0' && (text.Length != 1))) return false;
        for (int index = start; index < text.Length; index++) if (!char.IsAsciiDigit(text[index])) return false;
        return long.TryParse(text, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out long value) &&
            value >= minimum && value <= maximum;
    }
}
