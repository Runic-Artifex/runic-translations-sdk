using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Runic.Translations.Compiler;

internal static class Rmf2DecimalV5
{
    // An exact, portable decimal subset. Never route authored numbers through a
    // binary float or Decimal.Parse (which can round excessive fractional digits).
    private static readonly Regex Number = new(@"\A(?<sign>-?)(?<whole>0|[1-9][0-9]*)(?:\.(?<fraction>[0-9]+))?(?:[eE](?<exponent>[+-]?[0-9]+))?\z", RegexOptions.CultureInvariant);
    private const string MaximumCoefficient = "79228162514264337593543950335";

    internal static bool TryCanonicalize(string value, out string canonical)
    {
        canonical = string.Empty;
        if (value.Length > 4096) return false;
        var match = Number.Match(value);
        if (!match.Success) return false;
        int exponent = 0;
        if (match.Groups["exponent"].Success && !int.TryParse(match.Groups["exponent"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out exponent)) return false;
        string coefficient = (match.Groups["whole"].Value + match.Groups["fraction"].Value).TrimStart('0');
        if (coefficient.Length == 0) { canonical = "0"; return true; }
        long scale = (long)match.Groups["fraction"].Length - exponent;
        int trailing = coefficient.Length;
        while (trailing > 0 && coefficient[trailing - 1] == '0') { trailing--; scale--; }
        coefficient = coefficient.Substring(0, trailing);
        if (scale < 0)
        {
            if (coefficient.Length - scale > MaximumCoefficient.Length) return false;
            coefficient += new string('0', (int)-scale); scale = 0;
        }
        if (scale > 28 || coefficient.Length > MaximumCoefficient.Length ||
            (coefficient.Length == MaximumCoefficient.Length && string.CompareOrdinal(coefficient, MaximumCoefficient) > 0)) return false;
        canonical = scale == 0 ? coefficient : scale >= coefficient.Length
            ? "0." + new string('0', (int)scale - coefficient.Length) + coefficient
            : coefficient.Insert(coefficient.Length - (int)scale, ".");
        if (match.Groups["sign"].Length != 0) canonical = "-" + canonical;
        return true;
    }

    internal static bool IsInteger(string canonical) => !canonical.Contains('.', StringComparison.Ordinal);
}
