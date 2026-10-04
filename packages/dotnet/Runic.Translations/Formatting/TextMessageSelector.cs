using System;

namespace Runic.Translations;

/// <summary>Portable selector primitives used by generated schema version 2 accessors.</summary>
public static class TextMessageSelector
{
    /// <summary>Selects a CLDR plural category for the currently supported built-in locale families.</summary>
    /// <remarks>
    /// The pinned CLDR rules are applied to the canonical value: the decimal's scale
    /// is not significant, so <c>1.0m</c> selects like <c>1m</c>. RMF2 v5 messages
    /// instead select on the digits their number formatter displays. Generated
    /// compilers reject locale families outside this registry before using plural
    /// messages; other locales select <c>other</c>.
    /// </remarks>
    public static string SelectPlural(decimal value, string locale, bool ordinal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locale);
        return Select(VisibleDecimal.Canonical(value), locale, ordinal);
    }

    internal static string Select(VisibleDecimal value, string locale, bool ordinal)
    {
        PluralOperands operands = value.Operands;
        return GeneratedLocaleData.SelectPlural(GeneratedLocaleData.Language(locale), ordinal, in operands) ?? "other";
    }
}
