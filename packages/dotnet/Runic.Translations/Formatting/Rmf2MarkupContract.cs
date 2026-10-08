using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>
/// A compiler-exported RMF2 markup contract, parsed and validated once. Inline and document
/// renderers built from the same linked contract share it without parsing the JSON again.
/// </summary>
public sealed class Rmf2MarkupContract
{
    // Markup contract v2 (W220-003): explicit placement and child models plus integer options.
    internal const int Version = 2;
    // Document profile v1 (specs/translations/rmf2-document-profile-v1.md).
    internal const int MaximumDepth = 16;
    internal const int MaximumNodes = 4096;

    private Rmf2MarkupContract() { }

    internal Dictionary<string, Tag> Tags { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Message> Messages { get; } = new(StringComparer.Ordinal);

    /// <summary>Parses and validates the compiler-exported RMF2 markup contract JSON.</summary>
    /// <param name="contractJson">The generated <c>Rmf2MarkupContract</c> constant or the <c>markupContract</c> of a locale artifact.</param>
    /// <exception cref="ArgumentException">The contract is not a supported markup contract v2.</exception>
    public static Rmf2MarkupContract Link(string contractJson)
    {
        ArgumentNullException.ThrowIfNull(contractJson);
        var linked = new Rmf2MarkupContract();
        try
        {
            using JsonDocument json = JsonDocument.Parse(contractJson, new JsonDocumentOptions { MaxDepth = 32 });
            linked.Read(json.RootElement);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(exception.Message, nameof(contractJson), exception);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ArgumentException("The RMF2 markup contract is malformed.", nameof(contractJson), exception);
        }
        return linked;
    }

    private void Read(JsonElement root)
    {
        if (root.GetProperty("version").GetInt32() != Version) throw new FormatException("Unsupported RMF2 contract version.");
        foreach (JsonProperty item in root.GetProperty("contracts").EnumerateObject())
        {
            var options = new Dictionary<string, Option>(StringComparer.Ordinal);
            foreach (JsonProperty option in item.Value.GetProperty("options").EnumerateObject())
            {
                string type = option.Value.GetProperty("type").GetString()!;
                if (type is not ("string" or "number" or "integer" or "boolean" or "enum")) throw new FormatException("Unsupported RMF2 markup option type.");
                long minimum = int.MinValue, maximum = int.MaxValue;
                if (type == "integer")
                {
                    minimum = option.Value.GetProperty("minimum").GetInt32();
                    maximum = option.Value.GetProperty("maximum").GetInt32();
                    if (minimum > maximum) throw new FormatException("Invalid RMF2 integer option bounds.");
                }
                options.Add(option.Name, new Option(type, option.Value.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray(), option.Value.GetProperty("literalOnly").GetBoolean(), minimum, maximum));
            }
            string kind = item.Value.GetProperty("kind").GetString()!;
            if (kind is not ("paired" or "standalone")) throw new FormatException("Unsupported RMF2 markup kind.");
            bool standalone = kind == "standalone";
            string children = item.Value.GetProperty("children").GetString()!;
            string placement = item.Value.GetProperty("placement").GetString()!;
            // Inline contracts keep their v1 child models. Block-level placements belong to the
            // built-in document vocabulary only; custom block contracts are a later profile.
            bool supported = placement switch
            {
                "inline" => children == (standalone ? "none" : "inline"),
                "block" => !standalone && children is "inline" or "list-items" && item.Name.StartsWith("runic:", StringComparison.Ordinal),
                "list-item" => !standalone && children == "inline" && item.Name.StartsWith("runic:", StringComparison.Ordinal),
                _ => false,
            };
            if (!supported) throw new FormatException("RMF2 markup placement or child model is not supported.");
            string plainText = item.Value.GetProperty("plainText").GetString()!;
            if (plainText is not ("children" or "lineBreak" or "alternateText" or "explicit" or "omit")) throw new FormatException("Unsupported RMF2 plain-text projection policy.");
            Tags.Add(item.Name, new Tag(standalone, placement, children, item.Value.GetProperty("interactive").GetBoolean(), plainText, options));
        }
        foreach (JsonProperty message in root.GetProperty("messages").EnumerateObject())
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            var bounds = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
            foreach (JsonProperty slot in message.Value.GetProperty("slots").EnumerateObject())
            {
                slots.Add(slot.Name, slot.Value.GetProperty("kind").GetString()!);
                bounds.Add(slot.Name, (slot.Value.GetProperty("min").GetInt32(), slot.Value.GetProperty("max").GetInt32()));
            }
            var locales = message.Value.TryGetProperty("contentLocales", out JsonElement contentLocales)
                ? contentLocales.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);
            string content = message.Value.TryGetProperty("content", out JsonElement contentKind) ? contentKind.GetString()! : "inline";
            if (content is not ("inline" or "document")) throw new FormatException("Unsupported RMF2 message content kind.");
            var skeletons = new HashSet<string>(StringComparer.Ordinal);
            if (message.Value.TryGetProperty("skeletons", out JsonElement skeletonValues))
                foreach (JsonElement skeleton in skeletonValues.EnumerateArray()) skeletons.Add(skeleton.GetString()!);
            Messages.Add(message.Name, new Message(slots, bounds, locales, content == "document", skeletons));
        }
    }

    internal Message Get(string key) =>
        Messages.TryGetValue(key, out Message? message) ? message : throw new TranslationFormatException("Unknown RMF2 message contract '" + key + "'.");

    // Validates one loaded external-pack message against the linked contract: markup kinds,
    // options and slots, and for document messages the block rules, the normalized leaf
    // invariants and the locked skeleton (document profile v1, sections 3 to 6).
    internal void ValidatePlanV5(TranslationPackMessageContract contract, CompiledRmf2Message message)
    {
        if (!Messages.TryGetValue(contract.Key.Name, out Message? linked))
            throw new TranslationFormatException("Unknown RMF2 message contract.");
        var types = message.InputArray.ToDictionary(input => input.Name, input => input.Type, StringComparer.Ordinal);
        foreach (CompiledRmf2Declaration declaration in message.DeclarationArray) types[declaration.Name] = declaration.Expression.ValueType;
        foreach (CompiledRmf2Variant variant in message.VariantArray)
        {
            var stack = new Stack<Tag>();
            var names = new Stack<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CompiledRmf2Node node in variant.NodeArray)
            {
                if (node.Kind != "markup") continue;
                if (node.MarkupKind == "close")
                {
                    if (!Tags.TryGetValue(node.Value, out Tag? closing) || closing.Standalone || stack.Count == 0 || names.Pop() != node.Value)
                        throw new TranslationFormatException("Unbalanced or invalid closing markup.");
                    if (node.OptionArray.Length != 0) throw new TranslationFormatException("Closing markup cannot declare options.");
                    stack.Pop();
                    continue;
                }
                if (!Tags.TryGetValue(node.Value, out Tag? tag) || tag.Standalone != (node.MarkupKind == "standalone") || tag.Interactive && stack.Any(parent => parent.Interactive))
                    throw new TranslationFormatException("Unknown tag, invalid kind or interactive nesting.");
                var present = new HashSet<string>(StringComparer.Ordinal);
                bool functional = node.Value is "runic:link" or "runic:action" or "runic:icon";
                foreach (CompiledRmf2Option option in node.OptionArray)
                {
                    present.Add(option.Name);
                    if (functional && option.Name == "ref")
                    {
                        if (option.Value.Kind != "string-literal" || !linked.Slots.TryGetValue(option.Value.Value, out string? slotKind) || slotKind != node.Value)
                            throw new TranslationFormatException("Unknown functional slot or changed slot kind.");
                        counts[option.Value.Value] = counts.GetValueOrDefault(option.Value.Value) + 1;
                        continue;
                    }
                    if (!tag.Options.TryGetValue(option.Name, out Option? schema) || !schema.Accepts(option.Value, types))
                        throw new TranslationFormatException("Unknown or incompatible typed markup option.");
                }
                if (functional && !present.Contains("ref")) throw new TranslationFormatException("Missing functional ref.");
                foreach (string option in tag.Options.Keys) if (!present.Contains(option)) throw new TranslationFormatException("Missing required markup option.");
                if (!tag.Standalone) { stack.Push(tag); names.Push(node.Value); }
                if (stack.Count > MaximumDepth) throw new TranslationFormatException("Markup depth limit exceeded.");
            }
            if (stack.Count != 0) throw new TranslationFormatException("Unclosed inline plan.");
            foreach (var slot in linked.Bounds)
                if (counts.GetValueOrDefault(slot.Key) < slot.Value.Min || counts.GetValueOrDefault(slot.Key) > slot.Value.Max)
                    throw new TranslationFormatException("Functional slot multiplicity mismatch.");
            if (linked.Document)
            {
                string skeleton = DocumentSkeleton(variant.NodeArray);
                if (!linked.Skeletons.Contains(skeleton))
                    throw new Rmf2DocumentPlanException("A translated document structure does not match any source structure.", structure: true);
            }
            else if (variant.NodeArray.Any(node => node.Kind == "markup" && Tags[node.Value].Placement != "inline"))
                throw new Rmf2DocumentPlanException("Block markup appears in an inline message.", structure: false);
        }
    }

    // Checks the block rules and the leaf invariants of one already balanced document variant
    // and returns its encoded skeleton. Violations are malformed patterns: the compiler never
    // writes them, so only a tampered or hand-written pack can contain them.
    private string DocumentSkeleton(CompiledRmf2Node[] nodes)
    {
        var builder = new StringBuilder();
        var frames = new Stack<(string Model, bool First)>();
        frames.Push(("root", true));
        var leaf = new List<CompiledRmf2Node>();
        int count = 0;
        foreach (CompiledRmf2Node node in nodes)
        {
            if (node.Kind != "markup" || node.MarkupKind != "close")
                if (++count > MaximumNodes) throw new TranslationFormatException("Document node limit exceeded.");
            (string model, bool first) = frames.Peek();
            Tag? tag = node.Kind == "markup" ? Tags[node.Value] : null;
            switch (model)
            {
                case "root" or "list":
                    if (tag is null) Fail("Text or a placeholder appears outside a block.");
                    if (node.MarkupKind == "close")
                    {
                        frames.Pop();
                        if (model == "list") builder.Append(')');
                        break;
                    }
                    bool expected = model == "root" ? tag!.Placement == "block" : tag!.Placement == "list-item";
                    if (!expected) Fail("Element '" + node.Value + "' is not allowed here.");
                    frames.Pop();
                    frames.Push((model, false));
                    if (!first) builder.Append(',');
                    builder.Append(SkeletonName(node.Value));
                    string options = EncodeOptions(node.OptionArray);
                    if (options.Length != 0) builder.Append('[').Append(options).Append(']');
                    if (tag.Children == "list-items") { builder.Append('('); frames.Push(("list", true)); }
                    else { frames.Push(("leaf", true)); leaf.Clear(); }
                    break;
                default:
                    // Inside a leaf block or its inline markup. The variant is already balanced,
                    // so a close in the leaf frame closes the block itself.
                    if (tag is not null && node.MarkupKind == "close")
                    {
                        if (model == "leaf") CheckLeaf(leaf);
                        frames.Pop();
                        break;
                    }
                    if (tag is not null && tag.Placement != "inline") Fail("Block element '" + node.Value + "' appears inside inline content.");
                    leaf.Add(node);
                    if (tag is not null && node.MarkupKind == "open") frames.Push(("inline", true));
                    break;
            }
        }
        if (frames.Count != 1) Fail("Unclosed document structure.");
        return builder.ToString();

        static void Fail(string message) => throw new Rmf2DocumentPlanException(message, structure: false);
    }

    // Leaf invariants (document profile v1, section 5): no CR or LF in leaf text, and no space
    // or tab at the start or end of the leaf, where paired inline tags are transparent and
    // placeholders and standalone markup are atoms.
    private static void CheckLeaf(List<CompiledRmf2Node> leaf)
    {
        var characters = new List<char?>();
        foreach (CompiledRmf2Node node in leaf)
        {
            if (node.Kind == "text")
            {
                foreach (char value in node.Value)
                {
                    if (value is '\r' or '\n') throw new Rmf2DocumentPlanException("Document text is not normalized.", structure: false);
                    characters.Add(value);
                }
            }
            else if (node.Kind == "expression" || node.MarkupKind == "standalone") characters.Add(null);
        }
        if (characters.Count != 0 && (characters[0] is ' ' or '\t' || characters[^1] is ' ' or '\t'))
            throw new Rmf2DocumentPlanException("Document text is not normalized.", structure: false);
    }

    private static string SkeletonName(string name) => name.StartsWith("runic:", StringComparison.Ordinal) ? name.Substring(6) : name;

    // The same encoding as the compiler: every option in ordinal key order, literals as their
    // canonical text with the skeleton metacharacters escaped, variables as $name.
    private static string EncodeOptions(CompiledRmf2Option[] options)
    {
        var builder = new StringBuilder();
        foreach (CompiledRmf2Option option in options.OrderBy(option => option.Name, StringComparer.Ordinal))
        {
            if (builder.Length != 0) builder.Append(';');
            builder.Append(option.Name).Append('=');
            if (option.Value.Kind is "input" or "local") { builder.Append('$').Append(option.Value.Value); continue; }
            string value = option.Value.Canonical ?? option.Value.Value;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current is '\\' or '[' or ']' or '(' or ')' or ',' or ';' or '=' || (index == 0 && current == '$')) builder.Append('\\');
                builder.Append(current);
            }
        }
        return builder.ToString();
    }

    // Canonical decimal text only (no sign on zero, no leading zeros, no fraction or exponent),
    // within the declared bounds; identical to the compiler and the ESM markupLiteral rule.
    internal static bool AcceptsInteger(string text, long minimum, long maximum)
    {
        if (text.Length == 0 || text.Length > 11) return false;
        int start = text[0] == '-' ? 1 : 0;
        if (start == text.Length || (text[start] == '0' && text.Length != 1)) return false;
        for (int index = start; index < text.Length; index++) if (!char.IsAsciiDigit(text[index])) return false;
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value) && value >= minimum && value <= maximum;
    }

    internal sealed record Message(Dictionary<string, string> Slots, Dictionary<string, (int Min, int Max)> Bounds,
        Dictionary<string, string> ContentLocales, bool Document, HashSet<string> Skeletons);

    internal sealed record Tag(bool Standalone, string Placement, string Children, bool Interactive, string PlainText, Dictionary<string, Option> Options);

    internal sealed record Option(string Type, string[] Values, bool LiteralOnly, long Minimum, long Maximum)
    {
        internal bool AcceptsType(TextArgumentType type) => Type switch { "number" => type is TextArgumentType.Int or TextArgumentType.Number, "integer" => type == TextArgumentType.Int, "boolean" => type == TextArgumentType.Bool, _ => type == TextArgumentType.String };
        internal bool Accepts(string value) => Type switch
        {
            "enum" => Values.Contains(value, StringComparer.Ordinal),
            "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number),
            "integer" => AcceptsInteger(value, Minimum, Maximum),
            "boolean" => value is "true" or "false",
            _ => true,
        };
        internal bool Accepts(CompiledRmf2Value value, Dictionary<string, TextArgumentType> types)
        {
            if (value.Kind is "input" or "local")
                return !LiteralOnly && types.TryGetValue(value.Value, out TextArgumentType type) && AcceptsType(type);
            return Type switch
            {
                "number" => value.Kind == "number-literal" && value.Canonical is not null,
                "integer" => (value.Kind == "string-literal" || value.Kind == "number-literal" && value.Canonical == value.Value) && AcceptsInteger(value.Value, Minimum, Maximum),
                "boolean" => value.Kind == "string-literal" && value.Value is "true" or "false",
                "enum" => value.Kind == "string-literal" && Values.Contains(value.Value, StringComparer.Ordinal),
                "string" => value.Kind == "string-literal",
                _ => false,
            };
        }
    }
}

// A document-profile violation in a loaded pack. Structure mismatches are a translated
// skeleton outside the source skeletons; everything else is a malformed pattern.
#pragma warning disable CA1032, CA1064 // Internal loader signal; never escapes the pack loader.
internal sealed class Rmf2DocumentPlanException : Exception
#pragma warning restore CA1032, CA1064
{
    internal Rmf2DocumentPlanException(string message, bool structure) : base(message) => Structure = structure;
    internal bool Structure { get; }
}
