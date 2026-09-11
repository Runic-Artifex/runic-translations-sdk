using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations;

/// <summary>A typed application-owned functional inline binding.</summary>
public abstract record InlineMarkupBinding;
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

    /// <summary>Links the compiler-exported RMF2 markup contract. The caller owns UI implementations.</summary>
    public Rmf2InlineRenderer(string contractJson)
    {
        ArgumentNullException.ThrowIfNull(contractJson);
        using JsonDocument json = JsonDocument.Parse(contractJson, new JsonDocumentOptions { MaxDepth = 32 });
        JsonElement root = json.RootElement;
        if (root.GetProperty("version").GetInt32() != 1) throw new ArgumentException("Unsupported RMF2 contract version.", nameof(contractJson));
        foreach (JsonProperty item in root.GetProperty("contracts").EnumerateObject())
        {
            var options = new Dictionary<string, Option>(StringComparer.Ordinal);
            foreach (JsonProperty option in item.Value.GetProperty("options").EnumerateObject())
                options.Add(option.Name, new Option(option.Value.GetProperty("type").GetString()!, option.Value.GetProperty("values").EnumerateArray().Select(v => v.GetString()!).ToArray(), option.Value.GetProperty("literalOnly").GetBoolean()));
            _tags.Add(item.Name, new Tag(item.Value.GetProperty("kind").GetString() == "standalone",
                item.Value.GetProperty("interactive").GetBoolean(), item.Value.GetProperty("plainText").GetString()!, options));
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

    /// <summary>Builds semantic inline runs for the selected variant, checking all potential slot bindings.</summary>
    public IReadOnlyList<InlineMarkupRun> Render(string key, LocalizedTextContent content,
        IReadOnlyDictionary<string, InlineMarkupBinding>? slots = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        slots ??= new Dictionary<string, InlineMarkupBinding>();
        if (!_slots.TryGetValue(key, out Dictionary<string, string>? required)) throw new TranslationFormatException("Unknown RMF2 message contract '" + key + "'.");
        foreach (var slot in required)
            if (!slots.TryGetValue(slot.Key, out InlineMarkupBinding? binding) || !Matches(slot.Value, binding))
                throw new TranslationFormatException("Missing or incompatible binding for slot '" + slot.Key + "'.");
        LocalizedTextContentNode[] nodes = content.Nodes.ToArray(); int at = 0, count = 0;
        return Read(null, false, 0);
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
                        if (!required.TryGetValue(option.Value, out string? kind) || kind != node.Value || !slots.TryGetValue(option.Value, out binding) || !Matches(kind, binding))
                            throw new TranslationFormatException("Invalid functional slot '" + option.Value + "'.");
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

    /// <summary>Explicit projection; action labels require opt-in and meaningful icons require alternate text in the effective locale.</summary>
    public string ToPlainText(string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding>? slots = null,
        bool allowActionLabels = false, bool annotateLinkDestinations = false)
    {
        var text = new StringBuilder();
        foreach (InlineMarkupRun run in Render(key, content, slots)) Append(run);
        return text.ToString();
        void Append(InlineMarkupRun run)
        {
            if (run.Text is not null) { text.Append(run.Text); return; }
            Tag tag = _tags[run.Name];
            if (tag.PlainText == "explicit" && !allowActionLabels) throw new TranslationFormatException("This markup requires an explicit label-only projection policy.");
            if (tag.PlainText == "lineBreak") { text.Append('\n'); return; }
            if (run.Binding is InlineIconBinding icon)
            {
                if (!icon.Decorative)
                {
                    string? label = icon.AccessibleName!(content.Locale);
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
    private static bool Matches(string kind, InlineMarkupBinding binding) => kind switch
    { "runic:link" => binding is InlineLinkBinding { Destination: not null } link && (!link.Destination.IsAbsoluteUri || link.Destination.Scheme is "http" or "https" or "mailto" or "tel"), "runic:action" => binding is InlineActionBinding { Activate: not null }, "runic:icon" => binding is InlineIconBinding, _ => false };
    private sealed record Tag(bool Standalone, bool Interactive, string PlainText, Dictionary<string, Option> Options);
    private sealed record Option(string Type, string[] Values, bool LiteralOnly)
    {
        internal bool AcceptsType(TextArgumentType type) => Type switch { "number" => type is TextArgumentType.Int or TextArgumentType.Number, "boolean" => type == TextArgumentType.Bool, _ => type == TextArgumentType.String };
        internal bool Accepts(string value) => Type switch
        { "enum" => Values.Contains(value, StringComparer.Ordinal), "number" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number), "boolean" => value is "true" or "false", _ => true };
    }
}
