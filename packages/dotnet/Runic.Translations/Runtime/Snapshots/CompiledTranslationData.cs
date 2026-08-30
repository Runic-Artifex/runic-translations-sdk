using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>Describes one closed placeholder contract in a compiled resource.</summary>
public readonly record struct TranslationPlaceholderDescriptor(
    string Name,
    TextArgumentType Type,
    TextArgumentFormat Format);

/// <summary>Describes one canonical resource key and its placeholder contract.</summary>
public sealed class CompiledTranslationDefinition
{
    private readonly TranslationPlaceholderDescriptor[] _placeholders;

    /// <summary>Creates a compiled resource definition.</summary>
    public CompiledTranslationDefinition(
        string name,
        IReadOnlyList<TranslationPlaceholderDescriptor> placeholders,
        bool isCanonical = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(placeholders);

        if (!TranslationDataValidation.IsResourceName(name))
        {
            throw new ArgumentException(
                "Resource names must be dot-separated ASCII identifiers.",
                nameof(name));
        }

        if (placeholders.Count > 32)
        {
            throw new ArgumentException("A resource cannot declare more than 32 placeholders.", nameof(placeholders));
        }

        _placeholders = new TranslationPlaceholderDescriptor[placeholders.Count];
        string? previous = null;
        for (int i = 0; i < placeholders.Count; i++)
        {
            TranslationPlaceholderDescriptor descriptor = placeholders[i];
            if (!TranslationDataValidation.IsIdentifier(descriptor.Name))
            {
                throw new ArgumentException(
                    "Placeholder names must match [A-Za-z_][A-Za-z0-9_]*.",
                    nameof(placeholders));
            }

            if (previous is not null && string.CompareOrdinal(previous, descriptor.Name) >= 0)
            {
                throw new ArgumentException(
                    "Placeholder descriptors must be unique and ordered by ordinal name.",
                    nameof(placeholders));
            }

            if (!TranslationDataValidation.IsAllowedFormat(descriptor.Type, descriptor.Format))
            {
                throw new ArgumentException(
                    $"Placeholder '{descriptor.Name}' has an incompatible type and format.",
                    nameof(placeholders));
            }

            _placeholders[i] = descriptor;
            previous = descriptor.Name;
        }

        Name = name;
        IsCanonical = isCanonical;
    }

    /// <summary>The stable dotted resource name.</summary>
    public string Name { get; }

    /// <summary>
    /// Whether this definition belongs to the default locale's canonical generated key set.
    /// Non-canonical definitions are permitted locale extras and follow all canonical definitions.
    /// </summary>
    public bool IsCanonical { get; }

    /// <summary>The ordinal-ordered placeholder contracts.</summary>
    public ReadOnlyMemory<TranslationPlaceholderDescriptor> Placeholders =>
        (TranslationPlaceholderDescriptor[])_placeholders.Clone();

    internal TranslationPlaceholderDescriptor[] PlaceholderArray => _placeholders;
}

/// <summary>Associates one canonical key identifier with a compatibility pattern and optional precompiled AST.</summary>
public readonly record struct CompiledTranslationValue(int Id, string Pattern)
{
    /// <summary>Creates generated data that bypasses runtime pattern parsing.</summary>
    public CompiledTranslationValue(int id, string pattern, CompiledTextMessage message)
        : this(id, pattern)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }

    /// <summary>The compiler-produced message, or null for compatibility/runtime pack inputs.</summary>
    public CompiledTextMessage? Message { get; init; }
}

/// <summary>Contains the direct compiled values and fallback edge for one declared locale.</summary>
public sealed class CompiledTranslationLocale
{
    private readonly CompiledTranslationValue[] _values;

    /// <summary>Creates immutable compiled locale data.</summary>
    public CompiledTranslationLocale(
        string locale,
        string? fallbackLocale,
        IReadOnlyList<CompiledTranslationValue> values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        ArgumentNullException.ThrowIfNull(values);

        if (!LocaleTag.TryCanonicalize(locale, out string canonical) ||
            !string.Equals(locale, canonical, StringComparison.Ordinal))
        {
            throw new ArgumentException("The locale must be a canonical BCP 47 tag.", nameof(locale));
        }

        if (fallbackLocale is not null &&
            (!LocaleTag.TryCanonicalize(fallbackLocale, out string canonicalFallback) ||
             !string.Equals(fallbackLocale, canonicalFallback, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The fallback locale must be a canonical BCP 47 tag.", nameof(fallbackLocale));
        }

        _values = new CompiledTranslationValue[values.Count];
        int previousId = -1;
        for (int i = 0; i < values.Count; i++)
        {
            CompiledTranslationValue value = values[i];
            if (value.Id < 0)
            {
                throw new ArgumentException("Compiled value identifiers cannot be negative.", nameof(values));
            }

            ArgumentNullException.ThrowIfNull(value.Pattern);
            if (value.Id <= previousId)
            {
                throw new ArgumentException(
                    "Compiled values must be unique and ordered by ascending identifier.",
                    nameof(values));
            }

            _values[i] = value;
            previousId = value.Id;
        }

        Locale = locale;
        FallbackLocale = fallbackLocale;
    }

    /// <summary>The canonical declared locale.</summary>
    public string Locale { get; }

    /// <summary>The explicit declared fallback, or <see langword="null"/> for the default locale.</summary>
    public string? FallbackLocale { get; }

    /// <summary>The direct values ordered by ascending canonical key identifier.</summary>
    public ReadOnlyMemory<CompiledTranslationValue> Values =>
        (CompiledTranslationValue[])_values.Clone();

    internal CompiledTranslationValue[] ValueArray => _values;
}

internal static class TranslationDataValidation
{
    internal static bool IsCatalog(string value)
    {
        if (value.Length == 0 || value[0] < 'a' || value[0] > 'z')
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if ((character < 'a' || character > 'z') &&
                (character < '0' || character > '9') &&
                character is not '.' and not '-')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsResourceName(string value)
    {
        string[] parts = value.Split('.');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!IsIdentifier(parts[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || (!IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            return false;
        }

        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if (!IsAsciiLetter(character) &&
                (character < '0' || character > '9') &&
                character != '_')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsAllowedFormat(TextArgumentType type, TextArgumentFormat format) => type switch
    {
        TextArgumentType.String => format == TextArgumentFormat.None,
        TextArgumentType.Int => format is TextArgumentFormat.Plain or TextArgumentFormat.Grouped,
        TextArgumentType.Number => format is >= TextArgumentFormat.Plain and <= TextArgumentFormat.Percent4,
        TextArgumentType.Bool => format == TextArgumentFormat.Lower,
        TextArgumentType.Date => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium or TextArgumentFormat.Long,
        TextArgumentType.Time => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium,
        TextArgumentType.DateTime => format is TextArgumentFormat.Iso or TextArgumentFormat.Short or TextArgumentFormat.Medium or TextArgumentFormat.Long,
        TextArgumentType.Guid => format is TextArgumentFormat.D or TextArgumentFormat.N,
        _ => false,
    };

    internal static bool PatternMatches(
        string pattern,
        TranslationPlaceholderDescriptor[] descriptors)
    {
        var used = new bool[descriptors.Length];
        for (int position = 0; position < pattern.Length; position++)
        {
            char character = pattern[position];
            if (character == '{')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '{')
                {
                    position++;
                    continue;
                }

                int close = pattern.IndexOf('}', position + 1);
                if (close < 0 || pattern.IndexOf('{', position + 1, close - position - 1) >= 0)
                {
                    return false;
                }

                string name = pattern.Substring(position + 1, close - position - 1);
                int descriptorIndex = FindDescriptor(descriptors, name);
                if (descriptorIndex < 0)
                {
                    return false;
                }

                used[descriptorIndex] = true;
                position = close;
            }
            else if (character == '}')
            {
                if (position + 1 >= pattern.Length || pattern[position + 1] != '}')
                {
                    return false;
                }

                position++;
            }
        }

        for (int i = 0; i < used.Length; i++)
        {
            if (!used[i])
            {
                return false;
            }
        }

        return true;
    }

    private static int FindDescriptor(TranslationPlaceholderDescriptor[] descriptors, string name)
    {
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

    private static bool IsAsciiLetter(char character) =>
        (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');
}
