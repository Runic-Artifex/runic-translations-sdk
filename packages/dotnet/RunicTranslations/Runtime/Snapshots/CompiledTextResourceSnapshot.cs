using System;
using System.Collections.Generic;

namespace RunicTranslations;

/// <summary>An immutable snapshot over validated generated catalog data.</summary>
public sealed class CompiledTextResourceSnapshot : ITextResourceSnapshot
{
    private readonly CompiledTextResourceCatalog _catalog;
    private readonly CompiledTextResourceDefinition[] _definitions;
    private readonly string?[] _patterns;
    private readonly CompiledTextMessage?[] _messages;
    private readonly string?[] _noArgumentText;
    private readonly ITextValueFormatter _valueFormatter;

    /// <summary>Creates a snapshot for one declared canonical locale.</summary>
    public CompiledTextResourceSnapshot(
        CompiledTextResourceCatalog catalog,
        string canonicalLocale,
        ITextValueFormatter? valueFormatter = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalLocale);

        string resolvedLocale = catalog.ResolveRequestedLocale(canonicalLocale);
        if (!string.Equals(canonicalLocale, resolvedLocale, StringComparison.Ordinal))
        {
            throw new ArgumentException("A snapshot locale must be a declared canonical locale.", nameof(canonicalLocale));
        }

        _catalog = catalog;
        _definitions = catalog.DefinitionArray;
        _patterns = catalog.GetResolvedPatterns(canonicalLocale);
        _messages = catalog.GetResolvedMessages(canonicalLocale);
        _noArgumentText = BuildNoArgumentText(_messages, _definitions);
        _valueFormatter = valueFormatter ?? DefaultTextValueFormatter.Shared;
        Catalog = catalog.Catalog;
        Locale = canonicalLocale;
    }

    /// <summary>
    /// Creates a snapshot with fully verified replacement patterns layered over compiled fallback values.
    /// </summary>
    public CompiledTextResourceSnapshot(
        CompiledTextResourceCatalog catalog,
        string canonicalLocale,
        IReadOnlyList<CompiledTextResourceValue> replacementValues,
        ITextValueFormatter? valueFormatter = null)
        : this(catalog, canonicalLocale, valueFormatter)
    {
        ArgumentNullException.ThrowIfNull(replacementValues);
        if (replacementValues.Count == 0)
        {
            return;
        }

        var replacedPatterns = new string?[_patterns.Length];
        var replacedMessages = new CompiledTextMessage?[_messages.Length];
        Array.Copy(_patterns, replacedPatterns, _patterns.Length);
        Array.Copy(_messages, replacedMessages, _messages.Length);
        int previousId = -1;
        for (int i = 0; i < replacementValues.Count; i++)
        {
            CompiledTextResourceValue value = replacementValues[i];
            if (value.Id < 0 || value.Id >= _definitions.Length)
            {
                throw new ArgumentException("A replacement contains an unknown key identifier.", nameof(replacementValues));
            }

            if (value.Id <= previousId)
            {
                throw new ArgumentException(
                    "Replacement values must be unique and ordered by ascending identifier.",
                    nameof(replacementValues));
            }

            ArgumentNullException.ThrowIfNull(value.Pattern);
            CompiledTextMessage message;
            try
            {
                message = value.Message ?? CompiledTextMessageRuntime.ParseVersion1(value.Pattern);
            }
            catch (TextResourceFormatException)
            {
                throw new ArgumentException(
                    $"Replacement pattern for key '{_definitions[value.Id].Name}' is malformed.",
                    nameof(replacementValues));
            }
            if (!CompiledTextMessageRuntime.MatchesContract(message, _definitions[value.Id].PlaceholderArray))
            {
                throw new ArgumentException(
                    $"Replacement pattern for key '{_definitions[value.Id].Name}' does not match its placeholder contract.",
                    nameof(replacementValues));
            }

            replacedPatterns[value.Id] = value.Pattern;
            replacedMessages[value.Id] = message;
            previousId = value.Id;
        }

        _patterns = replacedPatterns;
        _messages = replacedMessages;
        _noArgumentText = BuildNoArgumentText(replacedMessages, _definitions);
    }

    /// <inheritdoc />
    public string Catalog { get; }

    /// <inheritdoc />
    public string Locale { get; }

    /// <inheritdoc />
    public bool TryGet(TextResourceKey key, out string pattern)
    {
        if (TryGetKeyIndex(key, out int index) && _patterns[index] is string resolved)
        {
            pattern = resolved;
            return true;
        }

        pattern = string.Empty;
        return false;
    }

    /// <inheritdoc />
    public string Get(TextResourceKey key)
    {
        if (TryGet(key, out string pattern))
        {
            return pattern;
        }

        return Missing(key);
    }

    /// <inheritdoc />
    public string Format(TextResourceKey key, ReadOnlySpan<TextArgument> arguments)
    {
        if (!TryGetKeyIndex(key, out int index) || _messages[index] is not CompiledTextMessage message)
        {
            return Missing(key);
        }

        if (arguments.IsEmpty && _definitions[index].PlaceholderArray.Length == 0)
        {
            return _noArgumentText[index]!;
        }

        ValidateArguments(_definitions[index], arguments);
        return CompiledTextMessageRuntime.Format(message, arguments, Locale, _valueFormatter);
    }

    /// <inheritdoc />
    public LocalizedTextContent FormatContent(TextResourceKey key, ReadOnlySpan<TextArgument> arguments)
    {
        if (!TryGetKeyIndex(key, out int index) || _messages[index] is not CompiledTextMessage message)
            throw new TextResourceNotFoundException("Structured text resource was not found.");
        if (!message.HasMarkup) throw new TextResourceFormatException("The resource does not produce structured content.");
        ValidateArguments(_definitions[index], arguments);
        return CompiledTextMessageRuntime.FormatContent(message, arguments, Locale, _valueFormatter);
    }

    private static string?[] BuildNoArgumentText(
        CompiledTextMessage?[] messages,
        CompiledTextResourceDefinition[] definitions)
    {
        var result = new string?[messages.Length];
        for (int i = 0; i < messages.Length; i++)
        {
            CompiledTextMessage? message = messages[i];
            if (message is not null && message.VariantArray.Length == 0 && definitions[i].PlaceholderArray.Length == 0)
            {
                result[i] = CompiledTextMessageRuntime.RenderLiteral(message);
            }
        }

        return result;
    }

    private static void ValidateArguments(
        CompiledTextResourceDefinition definition,
        ReadOnlySpan<TextArgument> arguments)
    {
        TextResourcePlaceholderDescriptor[] expected = definition.PlaceholderArray;
        if (arguments.Length != expected.Length)
        {
            throw new TextResourceFormatException(
                $"Resource '{definition.Name}' requires {expected.Length} arguments, but {arguments.Length} were supplied.");
        }

        for (int argumentIndex = 0; argumentIndex < arguments.Length; argumentIndex++)
        {
            TextArgument argument = arguments[argumentIndex];
            int descriptorIndex = FindDescriptor(expected, argument.Name);
            if (descriptorIndex < 0)
            {
                throw new TextResourceFormatException(
                    $"Resource '{definition.Name}' does not declare argument '{argument.Name ?? "<invalid>"}'.");
            }

            for (int prior = 0; prior < argumentIndex; prior++)
            {
                if (string.Equals(arguments[prior].Name, argument.Name, StringComparison.Ordinal))
                {
                    throw new TextResourceFormatException(
                        $"Argument '{argument.Name}' was supplied more than once for resource '{definition.Name}'.");
                }
            }

            TextResourcePlaceholderDescriptor descriptor = expected[descriptorIndex];
            if (argument.Type != descriptor.Type || argument.Format != descriptor.Format)
            {
                throw new TextResourceFormatException(
                    $"Argument '{descriptor.Name}' does not match the compiled type and format for resource '{definition.Name}'.");
            }
        }
    }

    private static int FindDescriptor(TextResourcePlaceholderDescriptor[] descriptors, string? name)
    {
        if (name is null)
        {
            return -1;
        }

        int low = 0;
        int high = descriptors.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            int comparison = string.CompareOrdinal(descriptors[middle].Name, name);
            if (comparison == 0)
            {
                return middle;
            }

            if (comparison < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return -1;
    }

    private bool TryGetKeyIndex(TextResourceKey key, out int index)
    {
        return _catalog.TryResolveKey(key, out index);
    }

    private string Missing(TextResourceKey key)
    {
        string name = string.IsNullOrEmpty(key.Name) ? "<invalid>" : key.Name;
        return _catalog.MissingKey switch
        {
            MissingTextResourcePolicy.ReturnKey => name,
            MissingTextResourcePolicy.ReturnMarker => "⟦" + name + "⟧",
            _ => throw new TextResourceNotFoundException(
                $"Text resource '{name}' was not found in catalog '{Catalog}' for locale '{Locale}'."),
        };
    }
}
