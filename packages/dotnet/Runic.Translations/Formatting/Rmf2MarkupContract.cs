using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

using Runic.Translations.Generated;

namespace Runic.Translations;

/// <summary>
/// A compiler-exported RMF2 markup contract, parsed and validated once. Inline and document
/// renderers built from the same linked contract share it without parsing the JSON again.
/// </summary>
public sealed class Rmf2MarkupContract
{
    // Markup contract v2 (W220-003): explicit placement and child models plus integer options.
    internal const int Version = 2;
    // Markup contract v3 (D077.16): v2 without the message entries of the default shape, and the catalog's locales instead.
    internal const int CompactVersion = 3;
    // Document profile v1 (specs/translations/rmf2-document-profile-v1.md).
    internal const int MaximumDepth = 16;
    internal const int MaximumNodes = 4096;

    // The linked tags and messages, swapped as one object when Hot Reload changes the source (Relink).
    private Data _data;
    // The generated contract to read again after a Hot Reload update, and the JSON linked last; null for a fixed contract.
    private readonly Func<string>? _source;
    private string? _linkedJson;
    private int _linkVersion;

    private Rmf2MarkupContract(Data data, Func<string>? source = null, string? linkedJson = null)
    {
        _data = data;
        _source = source;
        _linkedJson = linkedJson;
    }

    internal Dictionary<string, Tag> Tags => Volatile.Read(ref _data).Tags;

    /// <summary>
    /// The contract of a message of the catalog: its entry, or in markup contract v3 the default entry when the contract
    /// does not list it. Callers check that the key is the catalog's; v3 does not list the default-shaped keys.
    /// </summary>
    internal bool TryGetMessage(string key, [NotNullWhen(true)] out Message? message)
    {
        Data data = Volatile.Read(ref _data);
        if (data.Messages.TryGetValue(key, out message)) return true;
        message = data.DefaultMessage;
        return message is not null;
    }

    /// <summary>How many times Hot Reload linked a changed contract into this object; caches built from it compare it.</summary>
    internal int LinkVersion => Volatile.Read(ref _linkVersion);

    /// <summary>Parses and validates the compiler-exported RMF2 markup contract JSON.</summary>
    /// <param name="contractJson">The generated <c>Rmf2MarkupContract</c> constant or the <c>markupContract</c> of a locale artifact.</param>
    /// <exception cref="ArgumentException">The contract is not a supported markup contract v2.</exception>
    public static Rmf2MarkupContract Link(string contractJson)
    {
        ArgumentNullException.ThrowIfNull(contractJson);
        return new Rmf2MarkupContract(Parse(contractJson, nameof(contractJson)));
    }

    /// <summary>
    /// Parses and validates the generated markup contract that <paramref name="contractJson"/> returns, and follows it under
    /// .NET Hot Reload: after an update that changes the generated <c>XCatalog.Rmf2MarkupContract</c> (a translation added,
    /// a document's structure edited), the contract is linked again, so renderers and overlays built from this object check
    /// against the new one. Without Hot Reload it is the same as <see cref="Link(string)"/>.
    /// </summary>
    /// <param name="contractJson">Returns the contract, for example <c>() =&gt; XCatalog.Rmf2MarkupContract</c>.</param>
    /// <exception cref="ArgumentException">The contract is not a supported markup contract v2.</exception>
    public static Rmf2MarkupContract Link(Func<string> contractJson)
    {
        ArgumentNullException.ThrowIfNull(contractJson);
        string json = contractJson() ?? throw new ArgumentException("The markup contract source returned null.", nameof(contractJson));
        Data data = Parse(json, nameof(contractJson));
        if (!TranslationHotReload.IsSupported) return new Rmf2MarkupContract(data);
        var linked = new Rmf2MarkupContract(data, contractJson, json);
        TranslationHotReload.Track(linked);
        return linked;
    }

    /// <summary>Links the source again when its JSON changed (Hot Reload). A contract that fails to link keeps the previous one.</summary>
    internal void Relink()
    {
        if (_source is null) return;
        string? json = _source();
        if (json is null || string.Equals(json, Volatile.Read(ref _linkedJson), StringComparison.Ordinal)) return;
        Data data = Parse(json, "contractJson");
        Volatile.Write(ref _data, data);
        Volatile.Write(ref _linkedJson, json);
        Interlocked.Increment(ref _linkVersion);
    }

    private static Data Parse(string contractJson, string parameterName)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(contractJson, new JsonDocumentOptions { MaxDepth = 32 });
            return Read(json.RootElement);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException(exception.Message, parameterName, exception);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ArgumentException("The RMF2 markup contract is malformed.", parameterName, exception);
        }
    }

    private sealed class Data
    {
        internal Dictionary<string, Tag> Tags { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Message> Messages { get; } = new(StringComparer.Ordinal);
        // Markup contract v3: the entry of every message it does not list; null for v2.
        internal Message? DefaultMessage { get; set; }
    }

    private static Data Read(JsonElement root)
    {
        var data = new Data();
        int version = root.GetProperty("version").GetInt32();
        if (version is not (Version or CompactVersion)) throw new FormatException("Unsupported RMF2 contract version.");
        if (version == CompactVersion)
        {
            // The default entry: no slots, not structured, inline without skeletons, each locale its own content locale.
            var locales = new Dictionary<string, string>(StringComparer.Ordinal);
            string? previous = null;
            foreach (JsonElement locale in root.GetProperty("locales").EnumerateArray())
            {
                string tag = locale.GetString()!;
                if (!LocaleTag.TryCanonicalize(tag, out string canonical) || canonical != tag || (previous is not null && string.CompareOrdinal(previous, tag) >= 0))
                    throw new FormatException("The locales of an RMF2 markup contract v3 must be canonical tags in ordinal order.");
                locales.Add(tag, tag);
                previous = tag;
            }
            data.DefaultMessage = new Message([], [], locales, false, [], false);
        }
        else if (root.TryGetProperty("locales", out _)) throw new FormatException("An RMF2 markup contract v2 has no locales.");
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
            var tag = new Tag(standalone, placement, children, item.Value.GetProperty("interactive").GetBoolean(), plainText, options);
            if (item.Name.StartsWith("runic:", StringComparison.Ordinal) && !(BuiltIns.TryGetValue(item.Name, out Tag? builtIn) && builtIn.SameShape(tag)))
                throw new FormatException("RMF2 built-in markup '" + item.Name + "' does not have the built-in contract shape.");
            data.Tags.Add(item.Name, tag);
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
            // Markup contract v2 requires the content kind, the source skeletons and the content locales.
            var locales = message.Value.GetProperty("contentLocales").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
            string content = message.Value.GetProperty("content").GetString()!;
            if (content is not ("inline" or "document")) throw new FormatException("Unsupported RMF2 message content kind.");
            var skeletons = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement skeleton in message.Value.GetProperty("skeletons").EnumerateArray()) skeletons.Add(skeleton.GetString()!);
            if (content == "inline" && skeletons.Count != 0) throw new FormatException("An inline RMF2 message cannot declare document skeletons.");
            // Whether any locale of the message uses markup; a contract without the member allows markup.
            bool structured = !message.Value.TryGetProperty("structured", out JsonElement structuredValue) || structuredValue.GetBoolean();
            data.Messages.Add(message.Name, new Message(slots, bounds, locales, content == "document", skeletons, structured));
            // v3 lists only entries that differ from the default; a default one there is malformed, so each contract has one form.
            if (data.DefaultMessage is not null && IsDefault(data.Messages[message.Name], data.DefaultMessage))
                throw new FormatException("An RMF2 markup contract v3 lists a message of the default shape.");
        }
        return data;
    }

    /// <summary>
    /// Reports whether the linked contract declares the markup with this canonical name, for example a custom
    /// <c>shop:badge</c>. Adapters use it to check application-owned factories when they are linked. Built-in
    /// <c>runic:</c> names are reported as declared when the contract lists them.
    /// </summary>
    /// <param name="name">The canonical markup contract name, compared ordinally.</param>
    /// <returns><see langword="true"/> when the contract declares <paramref name="name"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public bool DeclaresMarkup(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Tags.ContainsKey(name);
    }

    internal Message Get(string key) =>
        TryGetMessage(key, out Message? message) ? message : throw new TranslationFormatException("Unknown RMF2 message contract '" + key + "'.");

    // Validates one loaded external-pack message against the linked contract: markup kinds,
    // options and slots, and for document messages the block rules, the normalized leaf
    // invariants and the locked skeleton (document profile v1, sections 3 to 6).
    internal void ValidatePlanV5(TranslationPackMessageContract contract, CompiledRmf2Message message) =>
        ValidatePlanV5(contract.Key.Name, message);

    internal void ValidatePlanV5(string key, CompiledRmf2Message message)
    {
        if (!TryGetMessage(key, out Message? linked))
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

    private static bool IsDefault(Message message, Message defaults) =>
        message.Slots.Count == 0 && !message.Structured && !message.Document && message.Skeletons.Count == 0 &&
        message.ContentLocales.Count == defaults.ContentLocales.Count &&
        message.ContentLocales.All(pair => defaults.ContentLocales.ContainsKey(pair.Key) && pair.Key == pair.Value);

    /// <summary>
    /// Whether two markup contracts of one catalog are the same contract, in version 2 or 3: equal text, or equal once each
    /// version 3 contract is written in version 2 with the default entry of every one of <paramref name="keys"/> (the
    /// catalog's messages) that it does not list.
    /// </summary>
    internal static bool SameContract(string left, string right, IEnumerable<string> keys)
    {
        if (string.Equals(left, right, StringComparison.Ordinal)) return true;
        string[] all = keys.ToArray();
        // A pack's contract is untrusted: one that cannot be expanded is not the same contract.
        try { return string.Equals(Expanded(left, all), Expanded(right, all), StringComparison.Ordinal); }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException) { return false; }
    }

    // The version 2 form of a contract, as the compiler writes it; a version 2 contract as it is.
    private static string Expanded(string contract, IReadOnlyList<string> keys)
    {
        using JsonDocument document = JsonDocument.Parse(contract, new JsonDocumentOptions { MaxDepth = 32 });
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int number) || number != CompactVersion)
            return contract;
        string[] locales = root.GetProperty("locales").EnumerateArray().Select(static item => item.GetString()!).ToArray();
        JsonElement listed = root.GetProperty("messages");
        var all = new SortedSet<string>(keys, StringComparer.Ordinal);
        foreach (JsonProperty message in listed.EnumerateObject()) all.Add(message.Name);
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WritePropertyName("contracts");
            root.GetProperty("contracts").WriteTo(writer);
            writer.WriteStartObject("messages");
            foreach (string key in all)
            {
                writer.WritePropertyName(key);
                if (listed.TryGetProperty(key, out JsonElement entry)) { entry.WriteTo(writer); continue; }
                writer.WriteStartObject();
                writer.WriteStartObject("slots");
                writer.WriteEndObject();
                writer.WriteBoolean("structured", false);
                writer.WriteStartObject("contentLocales");
                foreach (string locale in locales) writer.WriteString(locale, locale);
                writer.WriteEndObject();
                writer.WriteString("content", "inline");
                writer.WriteStartArray("skeletons");
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    internal sealed record Message(Dictionary<string, string> Slots, Dictionary<string, (int Min, int Max)> Bounds,
        Dictionary<string, string> ContentLocales, bool Document, HashSet<string> Skeletons, bool Structured);

    internal sealed record Tag(bool Standalone, string Placement, string Children, bool Interactive, string PlainText, Dictionary<string, Option> Options)
    {
        internal bool SameShape(Tag other) =>
            Standalone == other.Standalone && Placement == other.Placement && Children == other.Children && Interactive == other.Interactive && PlainText == other.PlainText &&
            Options.Count == other.Options.Count && Options.All(option => other.Options.TryGetValue(option.Key, out Option? actual) && option.Value.SameShape(actual));
    }

    // The built-in vocabulary the compiler exports (markup contract v2, document profile v1). A
    // linked contract carries only the built-ins its messages use, each with exactly this shape.
    private static readonly Dictionary<string, Tag> BuiltIns = CreateBuiltIns();

    private static Dictionary<string, Tag> CreateBuiltIns()
    {
        var none = new Dictionary<string, Option>(StringComparer.Ordinal);
        var tags = new Dictionary<string, Tag>(StringComparer.Ordinal)
        {
            ["runic:strong"] = new(false, "inline", "inline", false, "children", none),
            ["runic:em"] = new(false, "inline", "inline", false, "children", none),
            ["runic:bold"] = new(false, "inline", "inline", false, "children", none),
            ["runic:italic"] = new(false, "inline", "inline", false, "children", none),
            ["runic:code"] = new(false, "inline", "inline", false, "children", none),
            ["runic:br"] = new(true, "inline", "none", false, "lineBreak", none),
            ["runic:link"] = new(false, "inline", "inline", true, "children", none),
            ["runic:action"] = new(false, "inline", "inline", true, "explicit", none),
            ["runic:icon"] = new(true, "inline", "none", false, "alternateText", none),
            ["runic:p"] = new(false, "block", "inline", false, "children", none),
            ["runic:h"] = new(false, "block", "inline", false, "children", new(StringComparer.Ordinal) { ["level"] = new("integer", [], true, 1, 6) }),
            ["runic:ul"] = new(false, "block", "list-items", false, "children", none),
            ["runic:ol"] = new(false, "block", "list-items", false, "children", new(StringComparer.Ordinal)
            {
                ["start"] = new("integer", [], true, 1, int.MaxValue),
                ["marker"] = new("enum", ["decimal", "lower-alpha", "upper-alpha", "lower-roman", "upper-roman"], true, int.MinValue, int.MaxValue),
            }),
            ["runic:li"] = new(false, "list-item", "inline", false, "children", none),
        };
        return tags;
    }

    internal sealed record Option(string Type, string[] Values, bool LiteralOnly, long Minimum, long Maximum)
    {
        internal bool SameShape(Option other) =>
            Type == other.Type && LiteralOnly == other.LiteralOnly && Minimum == other.Minimum && Maximum == other.Maximum && Values.Order(StringComparer.Ordinal).SequenceEqual(other.Values.Order(StringComparer.Ordinal), StringComparer.Ordinal);
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
