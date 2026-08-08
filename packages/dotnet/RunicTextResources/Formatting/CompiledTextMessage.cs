using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RunicTextResources;

/// <summary>The closed node kinds admitted by the portable compiled message ABI.</summary>
public enum CompiledTextMessageNodeKind
{
    /// <summary>Literal plain text.</summary>
    Text,
    /// <summary>A typed external input reference.</summary>
    Input,
    /// <summary>A typed input formatted with a node-specific scalar format.</summary>
    Format,
    /// <summary>A numeric input formatted as a locale-sensitive relative duration.</summary>
    RelativeTime,
    /// <summary>Starts a semantic markup element; it is never interpreted as HTML.</summary>
    MarkupStart,
    /// <summary>Ends the most recently started semantic markup element.</summary>
    MarkupEnd,
}

/// <summary>One immutable semantic markup attribute.</summary>
public readonly record struct CompiledTextMarkupProperty(string Name, string Value);

/// <summary>One immutable node in a compiled message pattern.</summary>
public sealed class CompiledTextMessageNode
{
    private readonly CompiledTextMarkupProperty[] _attributes;

    /// <summary>Creates a compiled node.</summary>
    public CompiledTextMessageNode(
        CompiledTextMessageNodeKind kind,
        string value,
        TextArgumentFormat format = TextArgumentFormat.None,
        string? unit = null,
        string? numeric = null,
        IReadOnlyList<CompiledTextMarkupProperty>? attributes = null)
    {
        Kind = kind;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Format = format;
        Unit = unit;
        Numeric = numeric;
        _attributes = attributes is null ? Array.Empty<CompiledTextMarkupProperty>() : CopyAttributes(attributes);
    }

    /// <summary>The closed node kind.</summary>
    public CompiledTextMessageNodeKind Kind { get; }
    /// <summary>Literal text, input name, or semantic element name depending on <see cref="Kind"/>.</summary>
    public string Value { get; }
    /// <summary>The scalar format override for a format node.</summary>
    public TextArgumentFormat Format { get; }
    /// <summary>The relative-time unit.</summary>
    public string? Unit { get; }
    /// <summary>The relative-time numeric mode.</summary>
    public string? Numeric { get; }
    /// <summary>Semantic attributes for a markup-start node.</summary>
    public ReadOnlyMemory<CompiledTextMarkupProperty> Attributes => (CompiledTextMarkupProperty[])_attributes.Clone();
    internal CompiledTextMarkupProperty[] AttributeArray => _attributes;

    private static CompiledTextMarkupProperty[] CopyAttributes(IReadOnlyList<CompiledTextMarkupProperty> attributes)
    {
        var result = new CompiledTextMarkupProperty[attributes.Count];
        string? previous = null;
        for (int index = 0; index < attributes.Count; index++)
        {
            CompiledTextMarkupProperty attribute = attributes[index];
            if (!TextResourceDataValidation.IsIdentifier(attribute.Name) || attribute.Value is null ||
                (previous is not null && string.CompareOrdinal(previous, attribute.Name) >= 0))
                throw new ArgumentException("Markup attributes must be unique ordinally ordered identifiers with non-null values.", nameof(attributes));
            result[index] = attribute;
            previous = attribute.Name;
        }
        return result;
    }
}

/// <summary>The closed selector functions admitted by the portable compiled message ABI.</summary>
public enum CompiledTextMessageSelectorKind
{
    /// <summary>Ordinal string equality over the canonical input representation.</summary>
    Literal,
    /// <summary>CLDR cardinal plural category selection.</summary>
    CardinalPlural,
    /// <summary>CLDR ordinal plural category selection.</summary>
    OrdinalPlural,
}

/// <summary>One named selector evaluated before ordered variant matching.</summary>
public readonly record struct CompiledTextMessageSelector(
    string Name,
    string Input,
    CompiledTextMessageSelectorKind Kind);

/// <summary>An ordered match and pattern in a compiled variant message.</summary>
public sealed class CompiledTextMessageVariant
{
    private readonly string[] _matches;
    private readonly CompiledTextMessageNode[] _nodes;

    /// <summary>Creates an immutable variant. Matches are in selector order.</summary>
    public CompiledTextMessageVariant(
        IReadOnlyList<string> matches,
        IReadOnlyList<CompiledTextMessageNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(nodes);
        _matches = CopyMatches(matches);
        _nodes = CompiledTextMessage.CopyNodes(nodes, nameof(nodes));
    }

    /// <summary>Literal category matches or <c>*</c>, in selector order.</summary>
    public ReadOnlyMemory<string> Matches => (string[])_matches.Clone();

    /// <summary>The selected plain-text pattern nodes.</summary>
    public ReadOnlyMemory<CompiledTextMessageNode> Nodes => (CompiledTextMessageNode[])_nodes.Clone();

    internal string[] MatchArray => _matches;
    internal CompiledTextMessageNode[] NodeArray => _nodes;

    private static string[] CopyMatches(IReadOnlyList<string> matches)
    {
        var result = new string[matches.Count];
        for (int index = 0; index < matches.Count; index++)
        {
            string match = matches[index] ?? throw new ArgumentException("Variant matches cannot contain null.", nameof(matches));
            if (match.Length == 0) throw new ArgumentException("Variant matches cannot be empty.", nameof(matches));
            result[index] = match;
        }
        return result;
    }
}

/// <summary>A validated, immutable portable message consumed directly by the .NET runtime.</summary>
public sealed class CompiledTextMessage
{
    private readonly CompiledTextMessageNode[] _nodes;
    private readonly CompiledTextMessageSelector[] _selectors;
    private readonly CompiledTextMessageVariant[] _variants;

    /// <summary>Creates a simple compiled pattern.</summary>
    public CompiledTextMessage(IReadOnlyList<CompiledTextMessageNode> nodes)
        : this(nodes, Array.Empty<CompiledTextMessageSelector>(), Array.Empty<CompiledTextMessageVariant>())
    {
    }

    /// <summary>Creates a simple or selector-driven compiled message.</summary>
    public CompiledTextMessage(
        IReadOnlyList<CompiledTextMessageNode> nodes,
        IReadOnlyList<CompiledTextMessageSelector> selectors,
        IReadOnlyList<CompiledTextMessageVariant> variants)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(selectors);
        ArgumentNullException.ThrowIfNull(variants);
        _nodes = CopyNodes(nodes, nameof(nodes));
        _selectors = CopySelectors(selectors);
        _variants = CopyVariants(variants, _selectors.Length);
        ValidateMarkup(_nodes, nameof(nodes));
        for (int index = 0; index < _variants.Length; index++) ValidateMarkup(_variants[index].NodeArray, nameof(variants));
        if (_variants.Length != 0 && _nodes.Length != 0)
            throw new ArgumentException("A message cannot contain both a simple pattern and variants.", nameof(nodes));
        if (_selectors.Length != 0 && _variants.Length == 0)
            throw new ArgumentException("Selectors require variants.", nameof(selectors));
    }

    /// <summary>The simple pattern nodes, empty for a variant message.</summary>
    public ReadOnlyMemory<CompiledTextMessageNode> Nodes => (CompiledTextMessageNode[])_nodes.Clone();

    /// <summary>The selectors in deterministic evaluation order.</summary>
    public ReadOnlyMemory<CompiledTextMessageSelector> Selectors => (CompiledTextMessageSelector[])_selectors.Clone();

    /// <summary>The variants in authored match order.</summary>
    public ReadOnlyMemory<CompiledTextMessageVariant> Variants => (CompiledTextMessageVariant[])_variants.Clone();

    internal CompiledTextMessageNode[] NodeArray => _nodes;
    internal CompiledTextMessageSelector[] SelectorArray => _selectors;
    internal CompiledTextMessageVariant[] VariantArray => _variants;
    internal bool HasMarkup
    {
        get
        {
            if (ContainsMarkup(_nodes)) return true;
            for (int index = 0; index < _variants.Length; index++) if (ContainsMarkup(_variants[index].NodeArray)) return true;
            return false;
        }
    }

    internal static CompiledTextMessageNode[] CopyNodes(IReadOnlyList<CompiledTextMessageNode> nodes, string parameterName)
    {
        var result = new CompiledTextMessageNode[nodes.Count];
        for (int index = 0; index < nodes.Count; index++)
        {
            CompiledTextMessageNode node = nodes[index] ?? throw new ArgumentException("Compiled message nodes cannot contain null.", parameterName);
            if (!Enum.IsDefined(node.Kind)) throw new ArgumentException("Unknown compiled message node kind.", parameterName);
            if (node.Kind != CompiledTextMessageNodeKind.Text && !TextResourceDataValidation.IsIdentifier(node.Value))
                throw new ArgumentException("Compiled message input names must be identifiers.", parameterName);
            if (node.Kind == CompiledTextMessageNodeKind.RelativeTime &&
                (node.Unit is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year") ||
                 node.Numeric is not ("always" or "auto")))
                throw new ArgumentException("A relative-time node has invalid options.", parameterName);
            result[index] = new CompiledTextMessageNode(node.Kind, node.Value, node.Format, node.Unit, node.Numeric, node.AttributeArray);
        }
        return result;
    }

    private static CompiledTextMessageSelector[] CopySelectors(IReadOnlyList<CompiledTextMessageSelector> selectors)
    {
        var result = new CompiledTextMessageSelector[selectors.Count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < selectors.Count; index++)
        {
            CompiledTextMessageSelector selector = selectors[index];
            if (!TextResourceDataValidation.IsIdentifier(selector.Name) || !names.Add(selector.Name))
                throw new ArgumentException("Selector names must be unique identifiers.", nameof(selectors));
            if (!TextResourceDataValidation.IsIdentifier(selector.Input) || !Enum.IsDefined(selector.Kind))
                throw new ArgumentException("A selector contains an invalid input or kind.", nameof(selectors));
            result[index] = selector;
        }
        return result;
    }

    private static CompiledTextMessageVariant[] CopyVariants(IReadOnlyList<CompiledTextMessageVariant> variants, int selectorCount)
    {
        var result = new CompiledTextMessageVariant[variants.Count];
        bool catchAll = false;
        for (int index = 0; index < variants.Count; index++)
        {
            CompiledTextMessageVariant variant = variants[index] ?? throw new ArgumentException("Variants cannot contain null.", nameof(variants));
            if (variant.MatchArray.Length != selectorCount) throw new ArgumentException("Every variant must match every selector.", nameof(variants));
            bool all = true;
            for (int match = 0; match < selectorCount; match++) all &= variant.MatchArray[match] == "*";
            catchAll |= all;
            result[index] = variant;
        }
        if (variants.Count != 0 && !catchAll) throw new ArgumentException("Variant messages require an all-'*' catch-all.", nameof(variants));
        return result;
    }

    private static void ValidateMarkup(CompiledTextMessageNode[] nodes, string parameterName)
    {
        var names = new Stack<string>();
        for (int index = 0; index < nodes.Length; index++)
        {
            if (nodes[index].Kind == CompiledTextMessageNodeKind.MarkupStart) names.Push(nodes[index].Value);
            else if (nodes[index].Kind == CompiledTextMessageNodeKind.MarkupEnd &&
                (names.Count == 0 || names.Pop() != nodes[index].Value))
                throw new ArgumentException("Compiled markup nodes must be balanced and properly nested.", parameterName);
        }
        if (names.Count != 0) throw new ArgumentException("Compiled markup nodes must be balanced and properly nested.", parameterName);
    }

    private static bool ContainsMarkup(CompiledTextMessageNode[] nodes)
    {
        for (int index = 0; index < nodes.Length; index++)
            if (nodes[index].Kind is CompiledTextMessageNodeKind.MarkupStart or CompiledTextMessageNodeKind.MarkupEnd) return true;
        return false;
    }
}

internal static class CompiledTextMessageRuntime
{
    internal static CompiledTextMessage ParseVersion1(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var nodes = new List<CompiledTextMessageNode>();
        var text = new StringBuilder();
        for (int position = 0; position < pattern.Length; position++)
        {
            char character = pattern[position];
            if (character == '{')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '{') { text.Append('{'); position++; continue; }
                int close = pattern.IndexOf('}', position + 1);
                if (close < 0) throw InvalidPattern(position);
                string name = pattern.Substring(position + 1, close - position - 1);
                if (!TextResourceDataValidation.IsIdentifier(name)) throw InvalidPattern(position);
                Flush(nodes, text);
                nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.Input, name));
                position = close;
                continue;
            }
            if (character == '}')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '}') { text.Append('}'); position++; continue; }
                throw InvalidPattern(position);
            }
            text.Append(character);
        }
        Flush(nodes, text);
        return new CompiledTextMessage(nodes);
    }

    internal static bool MatchesContract(CompiledTextMessage message, TextResourcePlaceholderDescriptor[] descriptors)
    {
        var used = new bool[descriptors.Length];
        if (!Mark(message.NodeArray, descriptors, used)) return false;
        CompiledTextMessageSelector[] selectors = message.SelectorArray;
        for (int index = 0; index < selectors.Length; index++)
        {
            int descriptor = FindDescriptor(descriptors, selectors[index].Input);
            if (descriptor < 0) return false;
            TextArgumentType type = descriptors[descriptor].Type;
            if (selectors[index].Kind is CompiledTextMessageSelectorKind.CardinalPlural or CompiledTextMessageSelectorKind.OrdinalPlural &&
                type is not (TextArgumentType.Int or TextArgumentType.Number)) return false;
            used[descriptor] = true;
        }
        CompiledTextMessageVariant[] variants = message.VariantArray;
        for (int index = 0; index < variants.Length; index++) if (!Mark(variants[index].NodeArray, descriptors, used)) return false;
        for (int index = 0; index < used.Length; index++) if (!used[index]) return false;
        return true;
    }

    internal static string Format(CompiledTextMessage message, ReadOnlySpan<TextArgument> arguments, string locale,
        ITextValueFormatter formatter, int maximumOutputLength = TextPatternFormatter.DefaultMaximumOutputLength)
    {
        CompiledTextMessageNode[] nodes = message.NodeArray;
        if (message.VariantArray.Length != 0) nodes = SelectVariant(message, arguments, locale);
        if (message.HasMarkup) throw new TextResourceFormatException("Structured localized content must be requested through FormatContent.");
        var builder = new StringBuilder();
        for (int index = 0; index < nodes.Length; index++)
        {
            CompiledTextMessageNode node = nodes[index];
            if (node.Kind == CompiledTextMessageNodeKind.Text) Append(builder, node.Value, maximumOutputLength);
            else
            {
                int argument = FindArgument(arguments, node.Value);
                if (argument < 0) throw new TextResourceFormatException("Required argument '" + node.Value + "' was not supplied.");
                TextArgument value = arguments[argument];
                string? formatted;
                if (node.Kind == CompiledTextMessageNodeKind.Format)
                {
                    TextArgument formattedValue = WithFormat(value, node.Format);
                    formatted = formatter.Format(in formattedValue, locale);
                }
                else if (node.Kind == CompiledTextMessageNodeKind.RelativeTime)
                    formatted = TextRelativeTimeFormatter.Format(DecimalValue(value), node.Unit!, node.Numeric!, locale);
                else formatted = formatter.Format(in value, locale);
                if (formatted is null)
                    throw new TextResourceFormatException("The value formatter returned null for argument '" + node.Value + "'.");
                Append(builder, formatted, maximumOutputLength);
            }
        }
        return builder.ToString();
    }

    internal static LocalizedTextContent FormatContent(CompiledTextMessage message, ReadOnlySpan<TextArgument> arguments,
        string locale, ITextValueFormatter formatter, int maximumOutputLength = TextPatternFormatter.DefaultMaximumOutputLength)
    {
        CompiledTextMessageNode[] nodes = message.VariantArray.Length == 0 ? message.NodeArray : SelectVariant(message, arguments, locale);
        var result = new List<LocalizedTextContentNode>();
        int outputLength = 0;
        for (int index = 0; index < nodes.Length; index++)
        {
            CompiledTextMessageNode node = nodes[index];
            if (node.Kind == CompiledTextMessageNodeKind.MarkupStart)
            {
                result.Add(new LocalizedTextContentNode(LocalizedTextContentNodeKind.ElementStart, node.Value, node.AttributeArray));
                continue;
            }
            if (node.Kind == CompiledTextMessageNodeKind.MarkupEnd)
            {
                result.Add(new LocalizedTextContentNode(LocalizedTextContentNodeKind.ElementEnd, node.Value));
                continue;
            }
            string value;
            if (node.Kind == CompiledTextMessageNodeKind.Text) value = node.Value;
            else
            {
                int argumentIndex = FindArgument(arguments, node.Value);
                if (argumentIndex < 0) throw new TextResourceFormatException("Required argument '" + node.Value + "' was not supplied.");
                TextArgument argument = arguments[argumentIndex];
                if (node.Kind == CompiledTextMessageNodeKind.Format)
                {
                    TextArgument formatted = WithFormat(argument, node.Format);
                    value = formatter.Format(in formatted, locale);
                }
                else if (node.Kind == CompiledTextMessageNodeKind.RelativeTime)
                    value = TextRelativeTimeFormatter.Format(DecimalValue(argument), node.Unit!, node.Numeric!, locale);
                else value = formatter.Format(in argument, locale);
                if (value is null) throw new TextResourceFormatException("The value formatter returned null.");
            }
            outputLength += value.Length;
            if (outputLength > maximumOutputLength) throw new TextResourceFormatException("Formatted text exceeds the configured output limit.");
            result.Add(new LocalizedTextContentNode(LocalizedTextContentNodeKind.Text, value));
        }
        return new LocalizedTextContent(result);
    }

    internal static string RenderLiteral(CompiledTextMessage message)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < message.NodeArray.Length; index++) builder.Append(message.NodeArray[index].Value);
        return builder.ToString();
    }

    private static CompiledTextMessageNode[] SelectVariant(CompiledTextMessage message, ReadOnlySpan<TextArgument> arguments, string locale)
    {
        CompiledTextMessageSelector[] selectors = message.SelectorArray;
        var selected = new string[selectors.Length];
        for (int index = 0; index < selectors.Length; index++)
        {
            int argumentIndex = FindArgument(arguments, selectors[index].Input);
            if (argumentIndex < 0) throw new TextResourceFormatException("Selector input '" + selectors[index].Input + "' was not supplied.");
            TextArgument argument = arguments[argumentIndex];
            selected[index] = selectors[index].Kind switch
            {
                CompiledTextMessageSelectorKind.CardinalPlural => TextMessageSelector.SelectPlural(DecimalValue(argument), locale, false),
                CompiledTextMessageSelectorKind.OrdinalPlural => TextMessageSelector.SelectPlural(DecimalValue(argument), locale, true),
                _ => CanonicalValue(argument),
            };
        }
        CompiledTextMessageVariant[] variants = message.VariantArray;
        for (int variantIndex = 0; variantIndex < variants.Length; variantIndex++)
        {
            bool matches = true;
            for (int selector = 0; selector < selectors.Length; selector++)
            {
                string match = variants[variantIndex].MatchArray[selector];
                matches &= match == "*" || match == selected[selector];
            }
            if (matches) return variants[variantIndex].NodeArray;
        }
        throw new TextResourceFormatException("Compiled message variant selection has no catch-all.");
    }

    private static decimal DecimalValue(TextArgument argument)
    {
        if (argument.TryGetValue(out long integer)) return integer;
        if (argument.TryGetValue(out decimal number)) return number;
        throw new TextResourceFormatException("Plural selector input must be numeric.");
    }

    private static string CanonicalValue(TextArgument argument) => argument.Type switch
    {
        TextArgumentType.String when argument.TryGetValue(out string? text) => text!,
        TextArgumentType.Bool when argument.TryGetValue(out bool boolean) => boolean ? "true" : "false",
        TextArgumentType.Int when argument.TryGetValue(out long integer) => integer.ToString(CultureInfo.InvariantCulture),
        TextArgumentType.Number when argument.TryGetValue(out decimal number) => number.ToString(CultureInfo.InvariantCulture),
        _ => DefaultTextValueFormatter.Shared.Format(in argument, "en"),
    };

    private static bool Mark(CompiledTextMessageNode[] nodes, TextResourcePlaceholderDescriptor[] descriptors, bool[] used)
    {
        for (int index = 0; index < nodes.Length; index++)
        {
            if (nodes[index].Kind is CompiledTextMessageNodeKind.Text or CompiledTextMessageNodeKind.MarkupStart or CompiledTextMessageNodeKind.MarkupEnd) continue;
            int descriptor = FindDescriptor(descriptors, nodes[index].Value);
            if (descriptor < 0) return false;
            used[descriptor] = true;
        }
        return true;
    }

    private static int FindDescriptor(TextResourcePlaceholderDescriptor[] descriptors, string name)
    {
        for (int index = 0; index < descriptors.Length; index++) if (descriptors[index].Name == name) return index;
        return -1;
    }

    private static int FindArgument(ReadOnlySpan<TextArgument> arguments, string name)
    {
        for (int index = 0; index < arguments.Length; index++) if (arguments[index].Name == name) return index;
        return -1;
    }

    private static TextArgument WithFormat(TextArgument argument, TextArgumentFormat format)
    {
        if (argument.TryGetValue(out string? text)) return new TextArgument(argument.Name, text!);
        if (argument.TryGetValue(out long integer)) return new TextArgument(argument.Name, integer, format);
        if (argument.TryGetValue(out decimal number)) return new TextArgument(argument.Name, number, format);
        if (argument.TryGetValue(out bool boolean)) return new TextArgument(argument.Name, boolean, format);
        if (argument.TryGetValue(out DateOnly date)) return new TextArgument(argument.Name, date, format);
        if (argument.TryGetValue(out TimeOnly time)) return new TextArgument(argument.Name, time, format);
        if (argument.TryGetValue(out DateTimeOffset instant)) return new TextArgument(argument.Name, instant, format);
        if (argument.TryGetValue(out Guid guid)) return new TextArgument(argument.Name, guid, format);
        throw new TextResourceFormatException("Compiled format node has an incompatible input.");
    }

    private static void Flush(List<CompiledTextMessageNode> nodes, StringBuilder text)
    {
        if (text.Length == 0) return;
        nodes.Add(new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, text.ToString()));
        text.Clear();
    }

    private static void Append(StringBuilder builder, string value, int maximum)
    {
        if (value.Length > maximum - builder.Length) throw new TextResourceFormatException("Formatted text exceeds the configured output limit.");
        builder.Append(value);
    }

    private static TextResourceFormatException InvalidPattern(int position) =>
        new("Invalid version 1 message pattern at character " + position.ToString(CultureInfo.InvariantCulture) + ".");
}
