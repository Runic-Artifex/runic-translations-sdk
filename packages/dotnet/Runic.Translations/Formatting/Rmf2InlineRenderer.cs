using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>A typed application-owned binding for a functional RMF2 markup slot.</summary>
public abstract record MarkupBinding;
/// <summary>A typed application-owned functional inline binding.</summary>
public abstract record InlineMarkupBinding : MarkupBinding;
/// <summary>Application-owned navigation destination.</summary>
public sealed record InlineLinkBinding(Uri Destination) : InlineMarkupBinding;
/// <summary>Application-owned activation callback; rendering never invokes it.</summary>
public sealed record InlineActionBinding(Action Activate) : InlineMarkupBinding;
/// <summary>An application-owned asset with explicit accessibility semantics.</summary>
public sealed record InlineIconBinding(object Asset, bool Decorative, Func<string, string>? AccessibleName = null) : InlineMarkupBinding;

/// <summary>A semantic native/UI run. Factories explicitly map options to their toolkit.</summary>
public sealed record InlineMarkupRun(string Name, string? Text, IReadOnlyList<InlineMarkupRun> Children,
    IReadOnlyDictionary<string, string> Options, InlineMarkupBinding? Binding, bool Standalone);

/// <summary>Links versioned language-neutral contracts once, without loading application code.</summary>
public sealed class Rmf2InlineRenderer
{
    private readonly Dictionary<string, Tag> _tags = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _contentLocales = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, (int Min, int Max)>> _bounds = new(StringComparer.Ordinal);
    private static readonly IReadOnlyDictionary<string, string> EmptyOptions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    // Markup contract v2 (W220-003): explicit placement and child models plus integer options.
    internal const int MarkupContractVersion = 2;

    /// <summary>Links the compiler-exported RMF2 markup contract. The caller owns UI implementations.</summary>
    public Rmf2InlineRenderer(string contractJson)
    {
        ArgumentNullException.ThrowIfNull(contractJson);
        using JsonDocument json = JsonDocument.Parse(contractJson, new JsonDocumentOptions { MaxDepth = 32 });
        JsonElement root = json.RootElement;
        if (root.GetProperty("version").GetInt32() != MarkupContractVersion) throw new ArgumentException("Unsupported RMF2 contract version.", nameof(contractJson));
        foreach (JsonProperty item in root.GetProperty("contracts").EnumerateObject())
        {
            var options = new Dictionary<string, Option>(StringComparer.Ordinal);
            foreach (JsonProperty option in item.Value.GetProperty("options").EnumerateObject())
            {
                string type = option.Value.GetProperty("type").GetString()!;
                if (type is not ("string" or "number" or "integer" or "boolean" or "enum")) throw new ArgumentException("Unsupported RMF2 markup option type.", nameof(contractJson));
                long minimum = int.MinValue, maximum = int.MaxValue;
                if (type == "integer")
                {
                    minimum = option.Value.GetProperty("minimum").GetInt32();
                    maximum = option.Value.GetProperty("maximum").GetInt32();
                    if (minimum > maximum) throw new ArgumentException("Invalid RMF2 integer option bounds.", nameof(contractJson));
                }
                options.Add(option.Name, new Option(type, option.Value.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray(), option.Value.GetProperty("literalOnly").GetBoolean(), minimum, maximum));
            }
            string kind = item.Value.GetProperty("kind").GetString()!;
            if (kind is not ("paired" or "standalone")) throw new ArgumentException("Unsupported RMF2 markup kind.", nameof(contractJson));
            bool standalone = kind == "standalone";
            string children = item.Value.GetProperty("children").GetString()!;
            string placement = item.Value.GetProperty("placement").GetString()!;
            if (placement != "inline" || children != (standalone ? "none" : "inline")) throw new ArgumentException("RMF2 markup placement or child model is not supported.", nameof(contractJson));
            string plainText = item.Value.GetProperty("plainText").GetString()!;
            if (plainText is not ("children" or "lineBreak" or "alternateText" or "explicit" or "omit")) throw new ArgumentException("Unsupported RMF2 plain-text projection policy.", nameof(contractJson));
            _tags.Add(item.Name, new Tag(standalone, children, item.Value.GetProperty("interactive").GetBoolean(), plainText, options));
        }
        foreach (JsonProperty message in root.GetProperty("messages").EnumerateObject())
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            var bounds = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal);
            foreach (JsonProperty slot in message.Value.GetProperty("slots").EnumerateObject()) { slots.Add(slot.Name, slot.Value.GetProperty("kind").GetString()!); bounds.Add(slot.Name, (slot.Value.GetProperty("min").GetInt32(), slot.Value.GetProperty("max").GetInt32())); }
            _bounds.Add(message.Name, bounds);
            _slots.Add(message.Name, slots);
            _contentLocales.Add(message.Name, message.Value.GetProperty("contentLocales").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal));
        }
    }

    internal string ExpectedLocale(string key, string locale) => _contentLocales[key][locale];

    internal void ValidatePlan(TranslationPackMessageContract contract, CompiledTextMessage message)
    {
        if (!_slots.TryGetValue(contract.Key.Name, out var slots)) throw new TranslationFormatException("Unknown RMF2 message contract.");
        IEnumerable<CompiledTextMessageNode[]> variants = message.VariantArray.Length == 0 ? new[] { message.NodeArray } : message.VariantArray.Select(v => v.NodeArray);
        foreach (CompiledTextMessageNode[] nodes in variants)
        {
            var stack = new Stack<Tag>(); var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (CompiledTextMessageNode node in nodes)
            {
                if (node.Kind == CompiledTextMessageNodeKind.MarkupEnd) { if (stack.Count == 0) throw new TranslationFormatException("Unbalanced inline plan."); stack.Pop(); continue; }
                if (node.Kind is not (CompiledTextMessageNodeKind.MarkupStart or CompiledTextMessageNodeKind.MarkupStandalone)) continue;
                if (!_tags.TryGetValue(node.Value, out Tag? tag) || tag.Standalone != (node.Kind == CompiledTextMessageNodeKind.MarkupStandalone) || (tag.Interactive && stack.Any(t => t.Interactive))) throw new TranslationFormatException("Unknown tag, invalid kind or interactive nesting.");
                var present = new HashSet<string>(StringComparer.Ordinal);
                bool functional = node.Value is "runic:link" or "runic:action" or "runic:icon";
                foreach (CompiledTextMarkupProperty option in node.AttributeArray)
                {
                    if (option.IsAnnotation) continue;
                    present.Add(option.Name);
                    if (functional && option.Name == "ref")
                    {
                        if (option.IsVariable || !slots.TryGetValue(option.Value, out string? kind) || kind != node.Value) throw new TranslationFormatException("Unknown functional slot or changed slot kind.");
                        counts[option.Value] = counts.GetValueOrDefault(option.Value) + 1; continue;
                    }
                    if (!tag.Options.TryGetValue(option.Name, out Option? schema)) throw new TranslationFormatException("Unknown markup option.");
                    if (option.IsVariable)
                    {
                        TranslationPackArgumentContract input = contract.Arguments.FirstOrDefault(a => a.Name == option.Value);
                        if (schema.LiteralOnly || input.Name is null || !schema.AcceptsType(input.Type)) throw new TranslationFormatException("Dynamic markup option input type mismatch.");
                    }
                    else if (!schema.Accepts(option.Value)) throw new TranslationFormatException("Invalid literal markup option.");
                }
                if (functional && !present.Contains("ref")) throw new TranslationFormatException("Missing functional ref.");
                foreach (string option in tag.Options.Keys) if (!present.Contains(option)) throw new TranslationFormatException("Missing required markup option.");
                if (!tag.Standalone) stack.Push(tag);
                if (stack.Count > 16) throw new TranslationFormatException("Markup depth limit exceeded.");
            }
            foreach (var slot in _bounds[contract.Key.Name])
                if (counts.GetValueOrDefault(slot.Key) < slot.Value.Min || counts.GetValueOrDefault(slot.Key) > slot.Value.Max) throw new TranslationFormatException("Functional slot multiplicity mismatch.");
        }
    }

    internal void ValidatePlanV5(TranslationPackMessageContract contract, CompiledRmf2Message message)
    {
        if (!_slots.TryGetValue(contract.Key.Name, out var slots) || !_bounds.TryGetValue(contract.Key.Name, out var bounds))
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
                    if (!_tags.TryGetValue(node.Value, out Tag? closing) || closing.Standalone || stack.Count == 0 || names.Pop() != node.Value)
                        throw new TranslationFormatException("Unbalanced or invalid closing markup.");
                    if (node.OptionArray.Length != 0) throw new TranslationFormatException("Closing markup cannot declare options.");
                    stack.Pop();
                    continue;
                }
                if (!_tags.TryGetValue(node.Value, out Tag? tag) || tag.Standalone != (node.MarkupKind == "standalone") || tag.Interactive && stack.Any(parent => parent.Interactive))
                    throw new TranslationFormatException("Unknown tag, invalid kind or interactive nesting.");
                var present = new HashSet<string>(StringComparer.Ordinal);
                bool functional = node.Value is "runic:link" or "runic:action" or "runic:icon";
                foreach (CompiledRmf2Option option in node.OptionArray)
                {
                    present.Add(option.Name);
                    if (functional && option.Name == "ref")
                    {
                        if (option.Value.Kind != "string-literal" || !slots.TryGetValue(option.Value.Value, out string? slotKind) || slotKind != node.Value)
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
                if (stack.Count > 16) throw new TranslationFormatException("Markup depth limit exceeded.");
            }
            if (stack.Count != 0) throw new TranslationFormatException("Unclosed inline plan.");
            foreach (var slot in bounds)
                if (counts.GetValueOrDefault(slot.Key) < slot.Value.Min || counts.GetValueOrDefault(slot.Key) > slot.Value.Max)
                    throw new TranslationFormatException("Functional slot multiplicity mismatch.");
        }
    }

    /// <summary>Builds semantic inline runs for the selected variant, checking all potential slot bindings.</summary>
    public IReadOnlyList<InlineMarkupRun> Render(string key, LocalizedTextContent content,
        IReadOnlyDictionary<string, InlineMarkupBinding>? slots = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return RenderCore(key, content, slots ?? new Dictionary<string, InlineMarkupBinding>());
    }

    /// <summary>Builds semantic inline runs for typed bound content, applying the same contract checks as the string-key overload.</summary>
    public IReadOnlyList<InlineMarkupRun> Render(BoundLocalizedTextContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return RenderCore(content.Key, content.Content, content.Slots);
    }

    // Generic over the binding type because IReadOnlyDictionary is invariant; TBinding is a
    // reference type, so this is shared code without per-instantiation specialization.
    private IReadOnlyList<InlineMarkupRun> RenderCore<TBinding>(string key, LocalizedTextContent content,
        IReadOnlyDictionary<string, TBinding> slots) where TBinding : MarkupBinding
    {
        if (!_slots.TryGetValue(key, out Dictionary<string, string>? required) ||
            !_bounds.TryGetValue(key, out Dictionary<string, (int Min, int Max)>? bounds))
            throw new TranslationFormatException("Unknown RMF2 message contract '" + key + "'.");
        foreach (var slot in required)
            if (!slots.TryGetValue(slot.Key, out TBinding? binding) || !Matches(slot.Value, binding))
                throw new TranslationFormatException("Missing or incompatible binding for slot '" + slot.Key + "'.");
        LocalizedTextContentNode[] nodes = content.Nodes.ToArray(); int at = 0, count = 0;
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        IReadOnlyList<InlineMarkupRun> runs = Read(null, false, 0);
        foreach (var slot in bounds)
        {
            int occurrencesForSlot = occurrences.GetValueOrDefault(slot.Key);
            if (occurrencesForSlot < slot.Value.Min || occurrencesForSlot > slot.Value.Max)
                throw new TranslationFormatException("Functional slot multiplicity mismatch for '" + slot.Key + "'.");
        }
        return runs;
        IReadOnlyList<InlineMarkupRun> Read(string? closing, bool interactiveParent, int depth)
        {
            if (depth > 16) throw new TranslationFormatException("RMF2 inline nesting limit exceeded.");
            var result = new List<InlineMarkupRun>();
            while (at < nodes.Length)
            {
                LocalizedTextContentNode node = nodes[at++];
                if (++count > 4096) throw new TranslationFormatException("RMF2 inline node limit exceeded.");
                if (node.Kind == LocalizedTextContentNodeKind.ElementEnd)
                { if (node.Value != closing) throw new TranslationFormatException("Unbalanced inline content."); return result.AsReadOnly(); }
                if (node.Kind == LocalizedTextContentNodeKind.Text)
                { result.Add(new InlineMarkupRun("text", node.Value, Array.Empty<InlineMarkupRun>(), EmptyOptions, null, true)); continue; }
                if (!_tags.TryGetValue(node.Value, out Tag? tag)) throw new TranslationFormatException("Unregistered inline tag '" + node.Value + "'.");
                bool standalone = node.Kind == LocalizedTextContentNodeKind.ElementStandalone;
                if (standalone != tag.Standalone || (tag.Interactive && interactiveParent)) throw new TranslationFormatException("Invalid inline kind or interactive nesting.");
                var options = new Dictionary<string, string>(StringComparer.Ordinal);
                InlineMarkupBinding? binding = null;
                foreach (CompiledTextMarkupProperty option in node.Attributes.Span)
                {
                    if (option.IsAnnotation) continue;
                    if (option.Name == "ref" && node.Value is "runic:link" or "runic:action" or "runic:icon")
                    {
                        if (!required.TryGetValue(option.Value, out string? kind) || !bounds.ContainsKey(option.Value) ||
                            kind != node.Value || !slots.TryGetValue(option.Value, out TBinding? bound) || !Matches(kind, bound))
                            throw new TranslationFormatException("Invalid functional slot '" + option.Value + "'.");
                        binding = (InlineMarkupBinding)(MarkupBinding)bound;
                        occurrences[option.Value] = occurrences.GetValueOrDefault(option.Value) + 1;
                    }
                    else if (!tag.Options.TryGetValue(option.Name, out Option? schema) || !schema.Accepts(option.Value))
                        throw new TranslationFormatException("Invalid resolved markup option '" + option.Name + "'.");
                    if (!options.TryAdd(option.Name, option.Value)) throw new TranslationFormatException("Duplicate markup option.");
                }
                if (node.Value is "runic:link" or "runic:action" or "runic:icon" && binding is null) throw new TranslationFormatException("Missing functional slot ref.");
                foreach (string option in tag.Options.Keys) if (!options.ContainsKey(option)) throw new TranslationFormatException("Missing markup option '" + option + "'.");
                if (binding is InlineIconBinding icon && (icon.Asset is null || (!icon.Decorative && icon.AccessibleName is null))) throw new TranslationFormatException("A meaningful icon requires localized alternate text.");
                var children = standalone ? Array.Empty<InlineMarkupRun>() : Read(node.Value, interactiveParent || tag.Interactive, depth + 1);
                result.Add(new InlineMarkupRun(node.Value, null, children, new ReadOnlyDictionary<string, string>(options), binding, standalone));
            }
            if (closing is not null) throw new TranslationFormatException("Unclosed inline content.");
            return result.AsReadOnly();
        }
    }

    /// <summary>Explicit projection; action labels require opt-in, custom explicit/alternate-text policies require an adapter, and meaningful icons require alternate text in the effective locale.</summary>
    public string ToPlainText(string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding>? slots = null,
        bool allowActionLabels = false, bool annotateLinkDestinations = false) =>
        Project(Render(key, content, slots), content.Locale, allowActionLabels, annotateLinkDestinations);

    /// <summary>Explicit plain-text projection of typed bound content, with the same policies as the string-key overload.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overloads take unrelated first parameters (string key vs. bound content), so calls cannot become ambiguous.")]
    public string ToPlainText(BoundLocalizedTextContent content, bool allowActionLabels = false, bool annotateLinkDestinations = false) =>
        Project(Render(content), content.Content.Locale, allowActionLabels, annotateLinkDestinations);

    private string Project(IReadOnlyList<InlineMarkupRun> runs, string locale, bool allowActionLabels, bool annotateLinkDestinations)
    {
        var text = new StringBuilder();
        foreach (InlineMarkupRun run in runs) Append(run);
        return text.ToString();
        void Append(InlineMarkupRun run)
        {
            if (run.Text is not null) { text.Append(run.Text); return; }
            Tag tag = _tags[run.Name];
            if (tag.PlainText == "explicit" && run.Name != "runic:action") throw new TranslationFormatException("Custom markup requires an explicit plain-text adapter.");
            if (tag.PlainText == "explicit" && !allowActionLabels) throw new TranslationFormatException("This markup requires an explicit label-only projection policy.");
            if (tag.PlainText == "lineBreak") { text.Append('\n'); return; }
            if (run.Binding is InlineIconBinding icon)
            {
                if (!icon.Decorative)
                {
                    string? label = icon.AccessibleName!(locale);
                    if (string.IsNullOrWhiteSpace(label)) throw new TranslationFormatException("Meaningful icon alternate text is empty.");
                    text.Append(label);
                }
                return;
            }
            if (tag.PlainText == "alternateText") throw new TranslationFormatException("Custom meaningful markup needs an explicit alternate-text adapter.");
            if (tag.PlainText == "omit") return;
            foreach (InlineMarkupRun child in run.Children) Append(child);
            if (annotateLinkDestinations && run.Binding is InlineLinkBinding link) text.Append(" (").Append(link.Destination).Append(')');
        }
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
    private static bool Matches(string kind, MarkupBinding? binding) => kind switch
    { "runic:link" => binding is InlineLinkBinding { Destination: not null } link && (!link.Destination.IsAbsoluteUri || link.Destination.Scheme is "http" or "https" or "mailto" or "tel"), "runic:action" => binding is InlineActionBinding { Activate: not null }, "runic:icon" => binding is InlineIconBinding, _ => false };
    private sealed record Tag(bool Standalone, string Children, bool Interactive, string PlainText, Dictionary<string, Option> Options);
    private sealed record Option(string Type, string[] Values, bool LiteralOnly, long Minimum, long Maximum)
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
