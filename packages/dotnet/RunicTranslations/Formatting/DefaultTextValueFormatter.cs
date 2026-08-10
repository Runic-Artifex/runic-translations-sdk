using System;
using System.Globalization;

namespace RunicTranslations;

/// <summary>Formats the closed version 1 text argument types without reflection or arbitrary format strings.</summary>
public sealed class DefaultTextValueFormatter : ITextValueFormatter
{
    /// <summary>A reusable stateless formatter instance.</summary>
    public static DefaultTextValueFormatter Shared { get; } = new();

    /// <inheritdoc />
    public string Format(in TextArgument value, string resourceLocale)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceLocale);

        try
        {
            return value.Type switch
            {
                TextArgumentType.String => FormatString(in value),
                TextArgumentType.Int => FormatInt(in value, resourceLocale),
                TextArgumentType.Number => FormatNumber(in value, resourceLocale),
                TextArgumentType.Bool => FormatBoolean(in value),
                TextArgumentType.Date => FormatDate(in value, resourceLocale),
                TextArgumentType.Time => FormatTime(in value, resourceLocale),
                TextArgumentType.DateTime => FormatDateTime(in value, resourceLocale),
                TextArgumentType.Guid => FormatGuid(in value),
                _ => throw InvalidDescriptor(in value),
            };
        }
        catch (TextResourceFormatException)
        {
            throw;
        }
        catch (FormatException exception)
        {
            throw new TextResourceFormatException(
                "Argument '" + (value.Name ?? "<unnamed>") + "' could not be formatted.", exception);
        }
    }

    private static string FormatString(in TextArgument argument)
    {
        EnsureFormat(in argument, TextArgumentFormat.None);
        return GetValue<string>(in argument);
    }

    private static string FormatInt(in TextArgument argument, string resourceLocale)
    {
        long value = GetValue<long>(in argument);
        return argument.Format switch
        {
            TextArgumentFormat.Plain => value.ToString(CultureInfo.InvariantCulture),
            TextArgumentFormat.Grouped => value.ToString("N0", GetCulture(resourceLocale)),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static string FormatNumber(in TextArgument argument, string resourceLocale)
    {
        decimal value = GetValue<decimal>(in argument);
        if (argument.Format == TextArgumentFormat.Plain)
        {
            return value.ToString("0.############################", CultureInfo.InvariantCulture);
        }

        CultureInfo culture = GetCulture(resourceLocale);
        return argument.Format switch
        {
            TextArgumentFormat.Grouped => value.ToString("#,0.############################", culture),
            TextArgumentFormat.Fixed0 => value.ToString("F0", culture),
            TextArgumentFormat.Fixed1 => value.ToString("F1", culture),
            TextArgumentFormat.Fixed2 => value.ToString("F2", culture),
            TextArgumentFormat.Fixed3 => value.ToString("F3", culture),
            TextArgumentFormat.Fixed4 => value.ToString("F4", culture),
            TextArgumentFormat.Fixed5 => value.ToString("F5", culture),
            TextArgumentFormat.Fixed6 => value.ToString("F6", culture),
            TextArgumentFormat.Percent0 => value.ToString("P0", culture),
            TextArgumentFormat.Percent1 => value.ToString("P1", culture),
            TextArgumentFormat.Percent2 => value.ToString("P2", culture),
            TextArgumentFormat.Percent3 => value.ToString("P3", culture),
            TextArgumentFormat.Percent4 => value.ToString("P4", culture),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static string FormatBoolean(in TextArgument argument)
    {
        EnsureFormat(in argument, TextArgumentFormat.Lower);
        return GetValue<bool>(in argument) ? "true" : "false";
    }

    private static string FormatDate(in TextArgument argument, string resourceLocale)
    {
        DateOnly value = GetValue<DateOnly>(in argument);
        if (argument.Format == TextArgumentFormat.Iso)
        {
            return value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        CultureInfo culture = GetCulture(resourceLocale);
        return argument.Format switch
        {
            TextArgumentFormat.Short => value.ToString("d", culture),
            TextArgumentFormat.Medium => value.ToString("d MMM yyyy", culture),
            TextArgumentFormat.Long => value.ToString("D", culture),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static string FormatTime(in TextArgument argument, string resourceLocale)
    {
        TimeOnly value = GetValue<TimeOnly>(in argument);
        if (argument.Format == TextArgumentFormat.Iso)
        {
            return FormatIsoTime(value);
        }

        CultureInfo culture = GetCulture(resourceLocale);
        return argument.Format switch
        {
            TextArgumentFormat.Short => value.ToString("t", culture),
            TextArgumentFormat.Medium => value.ToString("T", culture),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static string FormatDateTime(in TextArgument argument, string resourceLocale)
    {
        DateTimeOffset value = GetValue<DateTimeOffset>(in argument);
        if (argument.Format == TextArgumentFormat.Iso)
        {
            return value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        }

        CultureInfo culture = GetCulture(resourceLocale);
        return argument.Format switch
        {
            TextArgumentFormat.Short => value.ToString("g", culture),
            TextArgumentFormat.Medium => value.ToString("G", culture),
            TextArgumentFormat.Long => value.ToString("F", culture),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static string FormatGuid(in TextArgument argument)
    {
        Guid value = GetValue<Guid>(in argument);
        return argument.Format switch
        {
            TextArgumentFormat.D => value.ToString("D", CultureInfo.InvariantCulture),
            TextArgumentFormat.N => value.ToString("N", CultureInfo.InvariantCulture),
            _ => throw InvalidDescriptor(in argument),
        };
    }

    private static T GetValue<T>(in TextArgument argument)
    {
        if (argument.TryGetValue<T>(out T? value))
        {
            return value;
        }

        throw new TextResourceFormatException(
            "Argument '" + (argument.Name ?? "<unnamed>") + "' does not contain its declared " +
            argument.Type.ToString() + " value.");
    }

    private static CultureInfo GetCulture(string resourceLocale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(resourceLocale);
        }
        catch (CultureNotFoundException exception)
        {
            throw new TextResourceFormatException(
                "Resource locale '" + resourceLocale + "' is not available for locale-sensitive formatting.", exception);
        }
    }

    private static string FormatIsoTime(TimeOnly value) =>
        value.ToString("HH:mm:ss", CultureInfo.InvariantCulture) +
        FormatIsoFraction(value.Ticks % TimeSpan.TicksPerSecond);

    private static string FormatIsoFraction(long ticksWithinSecond)
    {
        if (ticksWithinSecond == 0)
        {
            return string.Empty;
        }

        return "." + ticksWithinSecond.ToString("D7", CultureInfo.InvariantCulture).TrimEnd('0');
    }

    private static void EnsureFormat(in TextArgument argument, TextArgumentFormat expected)
    {
        if (argument.Format != expected)
        {
            throw InvalidDescriptor(in argument);
        }
    }

    private static TextResourceFormatException InvalidDescriptor(in TextArgument argument) =>
        new("Argument '" + (argument.Name ?? "<unnamed>") + "' has invalid format '" +
            argument.Format.ToString() + "' for type '" + argument.Type.ToString() + "'.");
}
