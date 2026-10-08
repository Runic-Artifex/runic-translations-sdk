using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text;

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
    IReadOnlyDictionary<string, string> Options, InlineMarkupBinding? Binding, bool Standalone)
{
    /// <summary>
    /// The stable occurrence key of an element run in document content: the enclosing block's path,
    /// <c>/</c>, then the dot-separated zero-based child positions within the block, for example
    /// <c>ul[1]/li[2]/1.0</c>. <see langword="null"/> for text runs and for inline messages.
    /// </summary>
    public string? Occurrence { get; init; }
}

/// <summary>Links versioned language-neutral contracts once, without loading application code.</summary>
public sealed class Rmf2InlineRenderer
{
    private readonly Rmf2MarkupContract _contract;

    /// <summary>Links the compiler-exported RMF2 markup contract. The caller owns UI implementations.</summary>
    public Rmf2InlineRenderer(string contractJson) : this(Rmf2MarkupContract.Link(contractJson)) { }

    /// <summary>Creates a renderer over an already linked markup contract.</summary>
    public Rmf2InlineRenderer(Rmf2MarkupContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _contract = contract;
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

    private IReadOnlyList<InlineMarkupRun> RenderCore<TBinding>(string key, LocalizedTextContent content,
        IReadOnlyDictionary<string, TBinding> slots) where TBinding : MarkupBinding
    {
        Rmf2MarkupContract.Message message = _contract.Get(key);
        if (message.Document) throw new TranslationFormatException("'" + key + "' is a document message; render it with Rmf2DocumentRenderer.");
        var reader = new Rmf2RunReader<TBinding>(_contract, message, content.Nodes.ToArray(), slots);
        IReadOnlyList<InlineMarkupRun> runs = reader.ReadInline(null, false, 0, null);
        reader.Finish();
        return runs;
    }

    /// <summary>Explicit projection; action labels require opt-in, custom explicit/alternate-text policies require an adapter, and meaningful icons require alternate text in the effective locale.</summary>
    public string ToPlainText(string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding>? slots = null,
        bool allowActionLabels = false, bool annotateLinkDestinations = false) =>
        ToPlainText(key, content, slots, new Rmf2PlainTextOptions { AllowActionLabels = allowActionLabels, AnnotateLinkDestinations = annotateLinkDestinations });

    /// <summary>Explicit plain-text projection of typed bound content, with the same policies as the string-key overload.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overloads take unrelated first parameters (string key vs. bound content), so calls cannot become ambiguous.")]
    [SuppressMessage("ApiDesign", "RS0027:API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "The longer overloads take a string key or Rmf2PlainTextOptions, so no existing call binds differently.")]
    public string ToPlainText(BoundLocalizedTextContent content, bool allowActionLabels = false, bool annotateLinkDestinations = false) =>
        ToPlainText(content, new Rmf2PlainTextOptions { AllowActionLabels = allowActionLabels, AnnotateLinkDestinations = annotateLinkDestinations });

    /// <summary>Explicit plain-text projection with the given options, including custom element projections.</summary>
    public string ToPlainText(string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding>? slots, Rmf2PlainTextOptions? options)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Rmf2PlainText.Inline(_contract, Render(key, content, slots), content.Locale, options ?? Rmf2PlainTextOptions.Default);
    }

    /// <summary>Explicit plain-text projection of typed bound content with the given options.</summary>
    public string ToPlainText(BoundLocalizedTextContent content, Rmf2PlainTextOptions? options)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Rmf2PlainText.Inline(_contract, Render(content), content.Content.Locale, options ?? Rmf2PlainTextOptions.Default);
    }

    internal static bool AcceptsInteger(string text, long minimum, long maximum) => Rmf2MarkupContract.AcceptsInteger(text, minimum, maximum);
}

/// <summary>Plain-text projection options shared by the inline and document renderers.</summary>
public sealed class Rmf2PlainTextOptions
{
    internal static readonly Rmf2PlainTextOptions Default = new();
    private readonly string _listMarker = "- ";

    /// <summary>Projects action labels; without it an action makes the projection fail.</summary>
    public bool AllowActionLabels { get; init; }

    /// <summary>Appends <c> (destination)</c> after each link label.</summary>
    public bool AnnotateLinkDestinations { get; init; }

    /// <summary>The marker written before each <c>ul</c> item. Ordered items use <c>{n}. </c>.</summary>
    public string ListMarker { get => _listMarker; init => _listMarker = value ?? throw new ArgumentNullException(nameof(value)); }

    /// <summary>
    /// Projections for custom elements whose plain-text policy is <c>explicit</c> or <c>alternateText</c>,
    /// keyed ordinally by canonical contract name. A missing projection makes the projection fail.
    /// </summary>
    public IReadOnlyDictionary<string, Func<Rmf2PlainTextElement, string>>? Custom { get; init; }
}

/// <summary>A custom element passed to a <see cref="Rmf2PlainTextOptions.Custom"/> projection.</summary>
/// <param name="Name">The canonical contract name.</param>
/// <param name="Options">The resolved element options.</param>
/// <param name="Locale">The effective content locale.</param>
/// <param name="Text">The projected text of the element's children.</param>
public sealed record Rmf2PlainTextElement(string Name, IReadOnlyDictionary<string, string> Options, string Locale, string Text);

// Reads the balanced semantic node stream of formatted content into inline runs, checking
// the linked contract: registered tags, kinds, interactive nesting, options, slot bindings and
// multiplicities, nesting depth and the node limit. Shared by the inline and document renderers.
internal sealed class Rmf2RunReader<TBinding> where TBinding : MarkupBinding
{
    private static readonly IReadOnlyDictionary<string, string> EmptyOptions = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    private readonly Rmf2MarkupContract _contract;
    private readonly Rmf2MarkupContract.Message _message;
    private readonly LocalizedTextContentNode[] _nodes;
    private readonly IReadOnlyDictionary<string, TBinding> _slots;
    private readonly Dictionary<string, int> _occurrences = new(StringComparer.Ordinal);
    private int _at, _count;

    internal Rmf2RunReader(Rmf2MarkupContract contract, Rmf2MarkupContract.Message message, LocalizedTextContentNode[] nodes, IReadOnlyDictionary<string, TBinding> slots)
    {
        _contract = contract; _message = message; _nodes = nodes; _slots = slots;
        foreach (var slot in message.Slots)
            if (!slots.TryGetValue(slot.Key, out TBinding? binding) || !Matches(slot.Value, binding))
                throw new TranslationFormatException("Missing or incompatible binding for slot '" + slot.Key + "'.");
    }

    internal bool AtEnd => _at >= _nodes.Length;

    internal LocalizedTextContentNode Next()
    {
        if (++_count > Rmf2MarkupContract.MaximumNodes) throw new TranslationFormatException("RMF2 content node limit exceeded.");
        return _nodes[_at++];
    }

    internal Rmf2MarkupContract.Tag Tag(LocalizedTextContentNode node) =>
        _contract.Tags.TryGetValue(node.Value, out Rmf2MarkupContract.Tag? tag) ? tag : throw new TranslationFormatException("Unregistered tag '" + node.Value + "'.");

    // Reads inline runs up to the closing tag. In document content prefix is the occurrence
    // prefix of the children ("<blockPath>/" in a leaf, "<occurrence>." below an element).
    internal IReadOnlyList<InlineMarkupRun> ReadInline(string? closing, bool interactiveParent, int depth, string? prefix)
    {
        if (depth > Rmf2MarkupContract.MaximumDepth) throw new TranslationFormatException("RMF2 nesting limit exceeded.");
        var result = new List<InlineMarkupRun>();
        while (!AtEnd)
        {
            LocalizedTextContentNode node = Next();
            if (node.Kind == LocalizedTextContentNodeKind.ElementEnd)
            { if (node.Value != closing) throw new TranslationFormatException("Unbalanced inline content."); return result.AsReadOnly(); }
            if (node.Kind == LocalizedTextContentNodeKind.Text)
            { result.Add(new InlineMarkupRun("text", node.Value, Array.Empty<InlineMarkupRun>(), EmptyOptions, null, true)); continue; }
            Rmf2MarkupContract.Tag tag = Tag(node);
            if (tag.Placement != "inline") throw new TranslationFormatException("Block element '" + node.Value + "' cannot appear inside inline content.");
            bool standalone = node.Kind == LocalizedTextContentNodeKind.ElementStandalone;
            if (standalone != tag.Standalone || (tag.Interactive && interactiveParent)) throw new TranslationFormatException("Invalid inline kind or interactive nesting.");
            IReadOnlyDictionary<string, string> options = Options(node, tag, out InlineMarkupBinding? binding);
            if (binding is InlineIconBinding icon && (icon.Asset is null || (!icon.Decorative && icon.AccessibleName is null))) throw new TranslationFormatException("A meaningful icon requires localized alternate text.");
            string? occurrence = prefix is null ? null : prefix + result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var children = standalone ? Array.Empty<InlineMarkupRun>() : ReadInline(node.Value, interactiveParent || tag.Interactive, depth + 1, occurrence is null ? null : occurrence + ".");
            result.Add(new InlineMarkupRun(node.Value, null, children, options, binding, standalone) { Occurrence = occurrence });
        }
        if (closing is not null) throw new TranslationFormatException("Unclosed inline content.");
        return result.AsReadOnly();
    }

    internal IReadOnlyDictionary<string, string> Options(LocalizedTextContentNode node, Rmf2MarkupContract.Tag tag, out InlineMarkupBinding? binding)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        binding = null;
        bool functional = node.Value is "runic:link" or "runic:action" or "runic:icon";
        foreach (CompiledTextMarkupProperty option in node.Attributes.Span)
        {
            if (option.IsAnnotation) continue;
            if (option.Name == "ref" && functional)
            {
                if (!_message.Slots.TryGetValue(option.Value, out string? kind) || !_message.Bounds.ContainsKey(option.Value) ||
                    kind != node.Value || !_slots.TryGetValue(option.Value, out TBinding? bound) || !Matches(kind, bound))
                    throw new TranslationFormatException("Invalid functional slot '" + option.Value + "'.");
                binding = (InlineMarkupBinding)(MarkupBinding)bound;
                _occurrences[option.Value] = _occurrences.GetValueOrDefault(option.Value) + 1;
            }
            else if (!tag.Options.TryGetValue(option.Name, out Rmf2MarkupContract.Option? schema) || !schema.Accepts(option.Value))
                throw new TranslationFormatException("Invalid resolved markup option '" + option.Name + "'.");
            if (!options.TryAdd(option.Name, option.Value)) throw new TranslationFormatException("Duplicate markup option.");
        }
        if (functional && binding is null) throw new TranslationFormatException("Missing functional slot ref.");
        foreach (string option in tag.Options.Keys) if (!options.ContainsKey(option)) throw new TranslationFormatException("Missing markup option '" + option + "'.");
        return new ReadOnlyDictionary<string, string>(options);
    }

    internal void Finish()
    {
        foreach (var slot in _message.Bounds)
        {
            int count = _occurrences.GetValueOrDefault(slot.Key);
            if (count < slot.Value.Min || count > slot.Value.Max)
                throw new TranslationFormatException("Functional slot multiplicity mismatch for '" + slot.Key + "'.");
        }
    }

    private static bool Matches(string kind, MarkupBinding? binding) => kind switch
    { "runic:link" => binding is InlineLinkBinding { Destination: not null } link && (!link.Destination.IsAbsoluteUri || link.Destination.Scheme is "http" or "https" or "mailto" or "tel"), "runic:action" => binding is InlineActionBinding { Activate: not null }, "runic:icon" => binding is InlineIconBinding, _ => false };
}

// Plain-text projection of semantic runs and document blocks (W220-003 section 7).
internal static class Rmf2PlainText
{
    internal static string Inline(Rmf2MarkupContract contract, IReadOnlyList<InlineMarkupRun> runs, string locale, Rmf2PlainTextOptions options)
    {
        var text = new StringBuilder();
        foreach (InlineMarkupRun run in runs) Append(text, run);
        return text.ToString();

        void Append(StringBuilder target, InlineMarkupRun run)
        {
            if (run.Text is not null) { target.Append(run.Text); return; }
            Rmf2MarkupContract.Tag tag = contract.Tags[run.Name];
            bool custom = !run.Name.StartsWith("runic:", StringComparison.Ordinal);
            if (custom && tag.PlainText is "explicit" or "alternateText")
            {
                if (options.Custom is null || !options.Custom.TryGetValue(run.Name, out Func<Rmf2PlainTextElement, string>? project) || project is null)
                    throw new TranslationFormatException("Custom markup '" + run.Name + "' requires an explicit plain-text projection.");
                var children = new StringBuilder();
                foreach (InlineMarkupRun child in run.Children) Append(children, child);
                target.Append(project(new Rmf2PlainTextElement(run.Name, run.Options, locale, children.ToString()))
                    ?? throw new TranslationFormatException("A custom plain-text projection returned null."));
                return;
            }
            if (tag.PlainText == "explicit" && !options.AllowActionLabels) throw new TranslationFormatException("This markup requires an explicit label-only projection policy.");
            if (tag.PlainText == "lineBreak") { target.Append('\n'); return; }
            if (run.Binding is InlineIconBinding icon)
            {
                if (!icon.Decorative)
                {
                    string? label = icon.AccessibleName!(locale);
                    if (string.IsNullOrWhiteSpace(label)) throw new TranslationFormatException("Meaningful icon alternate text is empty.");
                    target.Append(label);
                }
                return;
            }
            if (tag.PlainText == "omit") return;
            foreach (InlineMarkupRun child in run.Children) Append(target, child);
            if (options.AnnotateLinkDestinations && run.Binding is InlineLinkBinding link) target.Append(" (").Append(link.Destination).Append(')');
        }
    }

    // Top-level blocks are separated by a blank line and list items by one line break. Each
    // further line of an item is indented two spaces; blank lines carry no spaces. Empty blocks
    // add no separator, so the projection never starts or ends with a line break.
    internal static string Document(Rmf2MarkupContract contract, IReadOnlyList<DocumentBlock> blocks, string locale, Rmf2PlainTextOptions options)
    {
        var parts = new List<string>(blocks.Count);
        foreach (DocumentBlock block in blocks)
        {
            if (contract.Tags[block.Name].Children != "list-items")
            {
                string text = Inline(contract, block.Inlines, locale, options);
                if (text.Length != 0) parts.Add(text);
                continue;
            }
            var items = new List<string>(block.Blocks.Count);
            long start = block.Name == "runic:ol" ? long.Parse(block.Options["start"], System.Globalization.CultureInfo.InvariantCulture) : 1;
            for (int index = 0; index < block.Blocks.Count; index++)
            {
                string marker = block.Name == "runic:ol" ? Number(start + index, block.Options["marker"]) + ". " : options.ListMarker;
                string[] lines = Inline(contract, block.Blocks[index].Inlines, locale, options).Split('\n');
                for (int line = 1; line < lines.Length; line++) if (lines[line].Length != 0) lines[line] = "  " + lines[line];
                items.Add(marker + string.Join('\n', lines));
            }
            if (items.Count != 0) parts.Add(string.Join('\n', items));
        }
        return string.Join("\n\n", parts);
    }

    // Fixed, locale-independent numbering: ASCII decimal, bijective base 26 letters, and
    // subtractive roman numerals for 1 to 3999 with a decimal fallback outside that range.
    internal static string Number(long value, string marker)
    {
        switch (marker)
        {
            case "lower-alpha" or "upper-alpha":
            {
                var letters = new StringBuilder();
                for (long rest = value; rest > 0; rest = (rest - 1) / 26) letters.Insert(0, (char)('a' + (rest - 1) % 26));
                return marker == "upper-alpha" ? letters.ToString().ToUpperInvariant() : letters.ToString();
            }
            case "lower-roman" or "upper-roman" when value is >= 1 and <= 3999:
            {
                ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
                string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
                var roman = new StringBuilder();
                long rest = value;
                for (int index = 0; index < values.Length; index++)
                    for (; rest >= values[index]; rest -= values[index]) roman.Append(symbols[index]);
                return marker == "lower-roman" ? roman.ToString().ToLowerInvariant() : roman.ToString();
            }
            default:
                return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
