using System;
using System.Diagnostics.CodeAnalysis;

namespace WebUIToolkit.TextResources;

/// <summary>Closed primitive types accepted by the portable message formatter.</summary>
public enum TextArgumentType
{
    /// <summary>Plain text.</summary>
    String,
    /// <summary>A signed 64-bit integer.</summary>
    Int,
    /// <summary>A decimal number.</summary>
    Number,
    /// <summary>A Boolean value.</summary>
    Bool,
    /// <summary>A calendar date.</summary>
    Date,
    /// <summary>A time of day.</summary>
    Time,
    /// <summary>An offset-aware instant.</summary>
    DateTime,
    /// <summary>A globally unique identifier.</summary>
    Guid,
}

/// <summary>Portable, closed formatting choices for text arguments.</summary>
public enum TextArgumentFormat
{
    /// <summary>No explicit format.</summary>
    None,
    /// <summary>Ungrouped numeric text.</summary>
    Plain,
    /// <summary>Grouped numeric text.</summary>
    Grouped,
    /// <summary>Fixed-point with zero fractional digits.</summary>
    Fixed0,
    /// <summary>Fixed-point with one fractional digit.</summary>
    Fixed1,
    /// <summary>Fixed-point with two fractional digits.</summary>
    Fixed2,
    /// <summary>Fixed-point with three fractional digits.</summary>
    Fixed3,
    /// <summary>Fixed-point with four fractional digits.</summary>
    Fixed4,
    /// <summary>Fixed-point with five fractional digits.</summary>
    Fixed5,
    /// <summary>Fixed-point with six fractional digits.</summary>
    Fixed6,
    /// <summary>Percent with zero fractional digits.</summary>
    Percent0,
    /// <summary>Percent with one fractional digit.</summary>
    Percent1,
    /// <summary>Percent with two fractional digits.</summary>
    Percent2,
    /// <summary>Percent with three fractional digits.</summary>
    Percent3,
    /// <summary>Percent with four fractional digits.</summary>
    Percent4,
    /// <summary>Lowercase Boolean text.</summary>
    Lower,
    /// <summary>ISO representation.</summary>
    Iso,
    /// <summary>Short locale-sensitive representation.</summary>
    Short,
    /// <summary>Medium locale-sensitive representation.</summary>
    Medium,
    /// <summary>Long locale-sensitive representation.</summary>
    Long,
    /// <summary>Hyphenated GUID representation.</summary>
    D,
    /// <summary>Compact GUID representation.</summary>
    N,
}

/// <summary>A named, typed primitive argument for a compiled resource pattern.</summary>
public readonly struct TextArgument
{
    private readonly object _value;

    /// <summary>Creates a string argument.</summary>
    public TextArgument(string name, string value)
        : this(name, TextArgumentType.String, TextArgumentFormat.None, value ?? throw new ArgumentNullException(nameof(value)))
    {
    }

    /// <summary>Creates an integer argument.</summary>
    public TextArgument(string name, long value, TextArgumentFormat format = TextArgumentFormat.Plain)
        : this(name, TextArgumentType.Int, format, value)
    {
    }

    /// <summary>Creates a number argument.</summary>
    public TextArgument(string name, decimal value, TextArgumentFormat format = TextArgumentFormat.Plain)
        : this(name, TextArgumentType.Number, format, value)
    {
    }

    /// <summary>Creates a Boolean argument.</summary>
    public TextArgument(string name, bool value, TextArgumentFormat format = TextArgumentFormat.Lower)
        : this(name, TextArgumentType.Bool, format, value)
    {
    }

    /// <summary>Creates a date argument.</summary>
    public TextArgument(string name, DateOnly value, TextArgumentFormat format = TextArgumentFormat.Medium)
        : this(name, TextArgumentType.Date, format, value)
    {
    }

    /// <summary>Creates a time argument.</summary>
    public TextArgument(string name, TimeOnly value, TextArgumentFormat format = TextArgumentFormat.Short)
        : this(name, TextArgumentType.Time, format, value)
    {
    }

    /// <summary>Creates a date-time argument.</summary>
    public TextArgument(string name, DateTimeOffset value, TextArgumentFormat format = TextArgumentFormat.Medium)
        : this(name, TextArgumentType.DateTime, format, value)
    {
    }

    /// <summary>Creates a GUID argument.</summary>
    public TextArgument(string name, Guid value, TextArgumentFormat format = TextArgumentFormat.D)
        : this(name, TextArgumentType.Guid, format, value)
    {
    }

    private TextArgument(string name, TextArgumentType type, TextArgumentFormat format, object value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!IsPlaceholderName(name))
        {
            throw new ArgumentException("Placeholder names must match [A-Za-z_][A-Za-z0-9_]*.", nameof(name));
        }

        if (!IsAllowedFormat(type, format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "The format is not valid for this argument type.");
        }

        Name = name;
        Type = type;
        Format = format;
        _value = value;
    }

    /// <summary>The declared placeholder name.</summary>
    public string Name { get; }

    /// <summary>The closed primitive type.</summary>
    public TextArgumentType Type { get; }

    /// <summary>The portable formatting choice.</summary>
    public TextArgumentFormat Format { get; }

    /// <summary>Attempts to read the closed value as <typeparamref name="T"/>.</summary>
    public bool TryGetValue<T>([MaybeNullWhen(false)] out T value)
    {
        if (_value is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsPlaceholderName(string name)
    {
        if (!IsAsciiLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            char character = name[i];
            if (!IsAsciiLetter(character) && (character < '0' || character > '9') && character != '_')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char character) =>
        (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z');

    private static bool IsAllowedFormat(TextArgumentType type, TextArgumentFormat format) => type switch
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
}
