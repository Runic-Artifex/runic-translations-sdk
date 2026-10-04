using System;
using System.Globalization;

namespace Runic.Translations;

/// <summary>
/// The CLDR plural operands (UTS #35 Part 3, "Plural Operand Meanings") of a
/// visible decimal. CLDR's n operand is not stored: n equals i whenever t is zero,
/// and generated rules only compare n with integers.
/// </summary>
internal readonly struct PluralOperands
{
    private PluralOperands(UInt128 integer, int visibleDigits, UInt128 fraction, int exponent)
    {
        I = integer; V = visibleDigits; F = fraction; E = exponent;
        UInt128 trimmed = fraction; int digits = visibleDigits;
        while (digits > 0 && trimmed % 10 == 0) { trimmed /= 10; digits--; }
        T = trimmed; W = trimmed == 0 ? 0 : digits;
    }

    /// <summary>i: the integer digits of the absolute value.</summary>
    internal UInt128 I { get; }
    /// <summary>v: the number of visible fraction digits, including trailing zeros.</summary>
    internal int V { get; }
    /// <summary>w: the number of visible fraction digits, without trailing zeros.</summary>
    internal int W { get; }
    /// <summary>f: the visible fraction digits as an integer, including trailing zeros.</summary>
    internal UInt128 F { get; }
    /// <summary>t: the visible fraction digits as an integer, without trailing zeros.</summary>
    internal UInt128 T { get; }
    /// <summary>e (synonym c): the compact decimal exponent. Runic never formats compact notation, so runtime values use 0.</summary>
    internal int E { get; }

    /// <summary>Gets the operands of <paramref name="coefficient"/> / 10^<paramref name="scale"/>.</summary>
    internal static PluralOperands FromVisible(UInt128 coefficient, int scale, int exponent = 0)
    {
        UInt128 divisor = VisibleDecimal.Pow10(scale);
        return new(coefficient / divisor, scale, coefficient % divisor, exponent);
    }
}

/// <summary>
/// The number a numeric formatter displays, before localization. Plural and
/// ordinal selection, and exact numeric key matching, use this value instead of
/// the typed value: percent style multiplies by 100, the value is rounded
/// halfExpand to the maximum fraction digits, and trailing zeros are removed down
/// to the minimum fraction digits or padded up to it.
/// </summary>
internal readonly struct VisibleDecimal
{
    private const int MaximumScale = 28;

    private VisibleDecimal(bool negative, UInt128 coefficient, int scale)
    {
        Negative = negative && coefficient != 0; Coefficient = coefficient; Scale = scale;
    }

    internal bool Negative { get; }
    internal UInt128 Coefficient { get; }
    internal int Scale { get; }
    internal PluralOperands Operands => PluralOperands.FromVisible(Coefficient, Scale);

    /// <summary>The visible digits of <c>:number</c> with the resolved style and fraction digit options.</summary>
    internal static VisibleDecimal FromNumber(decimal value, bool percent, int minimumFractionDigits, int maximumFractionDigits)
    {
        if (minimumFractionDigits < 0 || minimumFractionDigits > maximumFractionDigits || maximumFractionDigits > MaximumScale)
            throw new ArgumentOutOfRangeException(nameof(maximumFractionDigits));
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        UInt128 coefficient = new((uint)bits[2], ((ulong)(uint)bits[1] << 32) | (uint)bits[0]);
        int scale = (bits[3] >> 16) & 0xFF;
        // decimal.MaxValue * 100 still fits: the coefficient stays below 2^96 * 10^8.
        if (percent) scale -= 2;
        if (scale < 0) { coefficient *= Pow10(-scale); scale = 0; }
        if (scale > maximumFractionDigits)
        {
            UInt128 divisor = Pow10(scale - maximumFractionDigits), remainder = coefficient % divisor;
            coefficient /= divisor;
            if (remainder * 2 >= divisor) coefficient++; // halfExpand on the absolute value
            scale = maximumFractionDigits;
        }
        while (scale > minimumFractionDigits && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        while (scale < minimumFractionDigits) { coefficient *= 10; scale++; }
        return new(value < 0, coefficient, scale);
    }

    /// <summary>The canonical value: no rounding, and trailing fraction zeros are not visible.</summary>
    internal static VisibleDecimal Canonical(decimal value) => FromNumber(value, false, 0, MaximumScale);

    /// <summary>The digits of <see cref="decimal.ToString(IFormatProvider)"/>, which keeps the value's scale.</summary>
    internal static VisibleDecimal Displayed(decimal value)
    {
        int scale = value.Scale;
        return FromNumber(value, false, scale, scale);
    }

    internal static VisibleDecimal FromInteger(long value) =>
        new(value < 0, value < 0 ? (UInt128)(-(Int128)value) : (ulong)value, 0);

    /// <summary>The canonical decimal spelling of this numeric value, as stored for exact variant keys.</summary>
    internal string ToCanonicalString()
    {
        UInt128 coefficient = Coefficient; int scale = Scale;
        while (scale > 0 && coefficient % 10 == 0) { coefficient /= 10; scale--; }
        string digits = coefficient.ToString(CultureInfo.InvariantCulture);
        string unsigned = scale == 0 ? digits : scale >= digits.Length
            ? "0." + new string('0', scale - digits.Length) + digits
            : digits.Insert(digits.Length - scale, ".");
        return Negative ? "-" + unsigned : unsigned;
    }

    internal static UInt128 Pow10(int exponent)
    {
        UInt128 result = 1;
        for (int index = 0; index < exponent; index++) result *= 10;
        return result;
    }
}
