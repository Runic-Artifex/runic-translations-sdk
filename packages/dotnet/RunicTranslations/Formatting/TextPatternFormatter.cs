using System;
using System.Globalization;
using System.Text;

namespace RunicTranslations;

/// <summary>Renders the reflection-free version 1 named-placeholder message grammar.</summary>
public static class TextPatternFormatter
{
    /// <summary>The maximum argument count admitted by the version 1 grammar.</summary>
    public const int MaximumArguments = 32;

    /// <summary>The default maximum rendered length, measured in UTF-16 code units.</summary>
    public const int DefaultMaximumOutputLength = 1024 * 1024;

    /// <summary>Formats a compiled version 1 pattern with ordinally matched named arguments.</summary>
    /// <param name="pattern">A version 1 message pattern.</param>
    /// <param name="arguments">The complete argument set. Order is insignificant.</param>
    /// <param name="resourceLocale">The resource locale used for locale-sensitive values.</param>
    /// <param name="valueFormatter">An optional closed-value formatter.</param>
    /// <param name="maximumOutputLength">The maximum result length in UTF-16 code units.</param>
    /// <exception cref="TextResourceFormatException">
    /// The pattern is invalid, arguments are missing, duplicated or unknown, or the output limit is exceeded.
    /// </exception>
    public static string Format(
        string pattern,
        ReadOnlySpan<TextArgument> arguments,
        string resourceLocale,
        ITextValueFormatter? valueFormatter = null,
        int maximumOutputLength = DefaultMaximumOutputLength)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceLocale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputLength);

        if (arguments.Length > MaximumArguments)
        {
            throw new TextResourceFormatException(
                "Argument count exceeds the version 1 limit of " +
                MaximumArguments.ToString(CultureInfo.InvariantCulture) + ".");
        }

        ValidateArgumentSet(arguments);
        ValidatePatternArguments(pattern, arguments);

        var builder = new StringBuilder(Math.Min(pattern.Length, maximumOutputLength));
        ITextValueFormatter formatter = valueFormatter ?? DefaultTextValueFormatter.Shared;

        for (int position = 0; position < pattern.Length; position++)
        {
            char character = pattern[position];
            if (character == '{')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '{')
                {
                    Append(builder, '{', maximumOutputLength);
                    position++;
                    continue;
                }

                int nameStart = position + 1;
                int close = FindPlaceholderClose(pattern, nameStart);
                ReadOnlySpan<char> name = pattern.AsSpan(nameStart, close - nameStart);
                int argumentIndex = FindArgument(arguments, name);
                string formatted = formatter.Format(in arguments[argumentIndex], resourceLocale);
                if (formatted is null)
                {
                    throw new TextResourceFormatException(
                        "The value formatter returned null for argument '" + name.ToString() + "'.");
                }

                Append(builder, formatted, maximumOutputLength);
                position = close;
                continue;
            }

            if (character == '}')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '}')
                {
                    Append(builder, '}', maximumOutputLength);
                    position++;
                    continue;
                }

                // The validation pass guarantees that a single close brace cannot occur here.
                throw InvalidPattern("unmatched '}'", position);
            }

            Append(builder, character, maximumOutputLength);
        }

        return builder.ToString();
    }

    private static void ValidatePatternArguments(string pattern, ReadOnlySpan<TextArgument> arguments)
    {
        Span<bool> used = stackalloc bool[arguments.Length];
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

                int nameStart = position + 1;
                int close = FindPlaceholderClose(pattern, nameStart);
                if (close < 0)
                {
                    throw InvalidPattern("invalid nested or unmatched '{'", position);
                }

                ReadOnlySpan<char> name = pattern.AsSpan(nameStart, close - nameStart);
                if (!IsPlaceholderName(name))
                {
                    throw InvalidPattern("invalid placeholder", position);
                }

                int argumentIndex = FindArgument(arguments, name);
                if (argumentIndex < 0)
                {
                    throw new TextResourceFormatException(
                        "Required argument '" + name.ToString() + "' was not supplied.");
                }

                used[argumentIndex] = true;
                position = close;
                continue;
            }

            if (character == '}')
            {
                if (position + 1 < pattern.Length && pattern[position + 1] == '}')
                {
                    position++;
                    continue;
                }

                throw InvalidPattern("unmatched '}'", position);
            }
        }

        for (int index = 0; index < arguments.Length; index++)
        {
            if (!used[index])
            {
                throw new TextResourceFormatException(
                    "Unknown argument '" + arguments[index].Name + "' was supplied.");
            }
        }
    }

    private static void ValidateArgumentSet(ReadOnlySpan<TextArgument> arguments)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            string? name = arguments[index].Name;
            if (string.IsNullOrEmpty(name) || !IsPlaceholderName(name.AsSpan()))
            {
                throw new TextResourceFormatException(
                    "Argument at index " + index.ToString(CultureInfo.InvariantCulture) + " has an invalid name.");
            }

            for (int prior = 0; prior < index; prior++)
            {
                if (string.Equals(arguments[prior].Name, name, StringComparison.Ordinal))
                {
                    throw new TextResourceFormatException("Argument '" + name + "' was supplied more than once.");
                }
            }
        }
    }

    private static int FindArgument(ReadOnlySpan<TextArgument> arguments, ReadOnlySpan<char> name)
    {
        for (int index = 0; index < arguments.Length; index++)
        {
            if (name.Equals(arguments[index].Name.AsSpan(), StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindPlaceholderClose(string pattern, int nameStart)
    {
        for (int index = nameStart; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '}')
            {
                return index;
            }

            if (character == '{')
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool IsPlaceholderName(ReadOnlySpan<char> name)
    {
        if (name.IsEmpty || (!IsAsciiLetter(name[0]) && name[0] != '_'))
        {
            return false;
        }

        for (int index = 1; index < name.Length; index++)
        {
            char character = name[index];
            if (!IsAsciiLetter(character) && (character < '0' || character > '9') && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');

    private static void Append(StringBuilder builder, char value, int maximumOutputLength)
    {
        if (builder.Length >= maximumOutputLength)
        {
            throw OutputTooLong(maximumOutputLength);
        }

        builder.Append(value);
    }

    private static void Append(StringBuilder builder, string value, int maximumOutputLength)
    {
        if (value.Length > maximumOutputLength - builder.Length)
        {
            throw OutputTooLong(maximumOutputLength);
        }

        builder.Append(value);
    }

    private static TextResourceFormatException InvalidPattern(string reason, int position) =>
        new("Invalid version 1 message pattern (" + reason + ") at character " +
            position.ToString(CultureInfo.InvariantCulture) + ".");

    private static TextResourceFormatException OutputTooLong(int maximumOutputLength) =>
        new("Formatted text exceeds the configured output limit of " +
            maximumOutputLength.ToString(CultureInfo.InvariantCulture) +
            " UTF-16 code units.");
}
