using System;
using System.Collections.Generic;
using System.Text;

namespace WebUIToolkit.TextResources;

/// <summary>An immutable snapshot over validated generated catalog data.</summary>
public sealed class CompiledTextResourceSnapshot : ITextResourceSnapshot
{
    private readonly CompiledTextResourceCatalog _catalog;
    private readonly CompiledTextResourceDefinition[] _definitions;
    private readonly string?[] _patterns;
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
        _noArgumentText = BuildNoArgumentText(_patterns, _definitions);
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
        Array.Copy(_patterns, replacedPatterns, _patterns.Length);
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
            if (!TextResourceDataValidation.PatternMatches(
                value.Pattern,
                _definitions[value.Id].PlaceholderArray))
            {
                throw new ArgumentException(
                    $"Replacement pattern for key '{_definitions[value.Id].Name}' does not match its placeholder contract.",
                    nameof(replacementValues));
            }

            replacedPatterns[value.Id] = value.Pattern;
            previousId = value.Id;
        }

        _patterns = replacedPatterns;
        _noArgumentText = BuildNoArgumentText(replacedPatterns, _definitions);
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
        if (!TryGetKeyIndex(key, out int index) || _patterns[index] is not string pattern)
        {
            return Missing(key);
        }

        if (arguments.IsEmpty && _definitions[index].PlaceholderArray.Length == 0)
        {
            return _noArgumentText[index]!;
        }

        ValidateArguments(_definitions[index], arguments);
        return TextPatternFormatter.Format(pattern, arguments, Locale, _valueFormatter);
    }

    private static string?[] BuildNoArgumentText(
        string?[] patterns,
        CompiledTextResourceDefinition[] definitions)
    {
        var result = new string?[patterns.Length];
        for (int i = 0; i < patterns.Length; i++)
        {
            string? pattern = patterns[i];
            if (pattern is not null && definitions[i].PlaceholderArray.Length == 0)
            {
                result[i] = RenderLiteralPattern(pattern);
            }
        }

        return result;
    }

    private static string RenderLiteralPattern(string pattern)
    {
        int escape = pattern.IndexOf("{{", StringComparison.Ordinal);
        if (escape < 0)
        {
            escape = pattern.IndexOf("}}", StringComparison.Ordinal);
        }

        if (escape < 0)
        {
            return pattern;
        }

        var builder = new StringBuilder(pattern.Length - 1);
        for (int i = 0; i < pattern.Length; i++)
        {
            char character = pattern[i];
            builder.Append(character);
            if ((character == '{' || character == '}') && i + 1 < pattern.Length && pattern[i + 1] == character)
            {
                i++;
            }
        }

        return builder.ToString();
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
