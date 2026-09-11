using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Runic.Translations.Compiler;

internal sealed class Rmf2MarkupRegistry
{
    private static readonly string[] ContractMembers = { "name", "kind", "options", "children", "interactive", "plainText" };
    private static readonly string[] OptionMembers = { "type", "values", "default", "literalOnly", "description" };
    private static readonly string[] RegistryMembers = { "contracts", "aliases", "slots" };
    internal static readonly Regex Name = new("^[A-Za-z_][A-Za-z0-9_-]*(?::[A-Za-z_][A-Za-z0-9_-]*)?$", RegexOptions.CultureInvariant);
    internal readonly Dictionary<string, Contract> Contracts = new(StringComparer.Ordinal);
    internal readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal);
    internal JsonValue? SlotConstraints;
    internal Rmf2MarkupRegistry()
    {
        foreach (string name in new[] { "strong", "em", "bold", "italic", "code", "br", "link", "action", "icon" })
        {
            Contracts.Add("runic:" + name, new Contract("runic:" + name, name is "br" or "icon", name is "link" or "action",
                name == "br" ? "lineBreak" : name == "icon" ? "alternateText" : name == "action" ? "explicit" : "children", new Dictionary<string, Option>(StringComparer.Ordinal)));
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
                bool interactive = item.Property("interactive")?.Value.Kind == JsonKind.True;
                if (!Name.IsMatch(name) || !name.Contains(':', StringComparison.Ordinal) || name.StartsWith("runic:", StringComparison.Ordinal) || kind is not ("paired" or "standalone") ||
                    plain is not ("children" or "lineBreak" or "alternateText" or "explicit" or "omit") || children != (kind == "standalone" ? "none" : "inline"))
                { Error("Invalid custom markup name, kind, child model or plainText policy.", item); continue; }
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
                        if (type is not ("string" or "number" or "boolean" or "enum") || (type == "enum" && values.Length == 0)) Error("Unsupported markup option type.", option.Value);
                        options.Add(option.Name, new Option(type, values, fallback, option.Value.Property("literalOnly")?.Value.Kind == JsonKind.True));
                    }
                }
                if (!registry.Contracts.TryAdd(name, new Contract(name, kind == "standalone", interactive, plain, options))) Error("Duplicate markup contract.", item);
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

    internal sealed record Contract(string Name, bool Standalone, bool Interactive, string PlainText, Dictionary<string, Option> Options);
    internal sealed record Option(string Type, string[] Values, string? Default, bool LiteralOnly)
    {
        internal bool Accepts(string value) => Type switch
        { "enum" => Values.Contains(value, StringComparer.Ordinal), "number" => double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number) && double.IsFinite(number), "boolean" => value is "true" or "false", _ => true };
        internal bool AcceptsType(TranslationArgumentType type) => Type switch { "number" => type is TranslationArgumentType.Int or TranslationArgumentType.Number, "boolean" => type == TranslationArgumentType.Boolean, _ => type == TranslationArgumentType.String };
    }
}
