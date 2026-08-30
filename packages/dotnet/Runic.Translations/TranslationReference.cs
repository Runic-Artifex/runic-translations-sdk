using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Runic.Translations;

/// <summary>The independently versioned wire contract for backend-originated localizable text.</summary>
public static class TranslationTransport
{
    /// <summary>The current transport writer version.</summary>
    public const int Version = 1;
    /// <summary>The maximum number of arguments in one reference.</summary>
    public const int MaximumArguments = 32;
    /// <summary>The maximum stable key length.</summary>
    public const int MaximumKeyLength = 512;
    /// <summary>The maximum canonical value length of one argument.</summary>
    public const int MaximumArgumentLength = 16 * 1024;
    /// <summary>The maximum optional fallback-text length.</summary>
    public const int MaximumFallbackLength = 64 * 1024;
}

/// <summary>A portable, canonical argument carried by a <see cref="TranslationReference"/>.</summary>
public readonly record struct TranslationReferenceArgument
{
    /// <summary>Creates a typed argument from its canonical invariant wire value.</summary>
    public TranslationReferenceArgument(TextArgumentType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > TranslationTransport.MaximumArgumentLength)
            throw new ArgumentOutOfRangeException(nameof(value), "The canonical argument value is too long.");
        ValidateCanonical(type, value);
        Type = type;
        Value = value;
    }

    /// <summary>The portable argument type.</summary>
    public TextArgumentType Type { get; }
    /// <summary>The canonical invariant wire value.</summary>
    public string Value { get; }

    private static void ValidateCanonical(TextArgumentType type, string value)
    {
        bool valid = type switch
        {
            TextArgumentType.String => true,
            TextArgumentType.Bool => value is "true" or "false",
            TextArgumentType.Int => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer) &&
                value == integer.ToString(CultureInfo.InvariantCulture),
            TextArgumentType.Number => decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal number) && value == number.ToString(CultureInfo.InvariantCulture),
            TextArgumentType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TextArgumentType.Time => TimeOnly.TryParseExact(value, ["HH:mm:ss", "HH:mm:ss.FFFFFFF"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TextArgumentType.DateTime => DateTimeOffset.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm:ssK", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _),
            TextArgumentType.Guid => Guid.TryParseExact(value, "D", out Guid guid) && value == guid.ToString("D", CultureInfo.InvariantCulture),
            _ => false,
        };
        if (!valid) throw new ArgumentException("The value is not canonical for the declared portable type.", nameof(value));
    }
}

/// <summary>A stable key-and-arguments envelope for localization by another process.</summary>
[JsonConverter(typeof(TranslationReferenceJsonConverter))]
public sealed class TranslationReference
{
    private readonly Dictionary<string, TranslationReferenceArgument> _arguments;
    private readonly ReadOnlyDictionary<string, TranslationReferenceArgument> _readOnlyArguments;

    /// <summary>Creates an immutable version 1 text reference.</summary>
    public TranslationReference(
        string catalog,
        string contractFingerprint,
        string key,
        IReadOnlyDictionary<string, TranslationReferenceArgument>? arguments = null,
        string? fallbackText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > TranslationTransport.MaximumKeyLength) throw new ArgumentOutOfRangeException(nameof(key));
        if (fallbackText?.Length > TranslationTransport.MaximumFallbackLength) throw new ArgumentOutOfRangeException(nameof(fallbackText));
        if (!IsSha256Fingerprint(contractFingerprint))
            throw new ArgumentException("A lowercase sha256: contract fingerprint is required.", nameof(contractFingerprint));

        Catalog = catalog;
        Version = TranslationTransport.Version;
        ContractFingerprint = contractFingerprint;
        Key = key;
        FallbackText = fallbackText;
        _arguments = new Dictionary<string, TranslationReferenceArgument>(StringComparer.Ordinal);
        _readOnlyArguments = new ReadOnlyDictionary<string, TranslationReferenceArgument>(_arguments);
        if (arguments is null) return;
        if (arguments.Count > TranslationTransport.MaximumArguments) throw new ArgumentOutOfRangeException(nameof(arguments));
        foreach (KeyValuePair<string, TranslationReferenceArgument> pair in arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            if (!_arguments.TryAdd(pair.Key, pair.Value)) throw new ArgumentException("Argument names must be unique.", nameof(arguments));
        }
    }

    /// <summary>The transport contract version.</summary>
    public int Version { get; }
    /// <summary>The stable catalog ID.</summary>
    public string Catalog { get; }
    /// <summary>The sender's generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The stable dotted key, never a process-local integer ID.</summary>
    public string Key { get; }
    /// <summary>The canonical typed arguments.</summary>
    public IReadOnlyDictionary<string, TranslationReferenceArgument> Arguments => _readOnlyArguments;
    /// <summary>Optional already-resolved plain text for version skew and inaccessible clients.</summary>
    public string? FallbackText { get; }

    /// <summary>Fails deterministically when a receiver has a different generated catalog contract.</summary>
    public void ValidateCatalog(string expectedCatalog, string expectedContractFingerprint)
    {
        if (!string.Equals(Catalog, expectedCatalog, StringComparison.Ordinal))
            throw new TranslationContractException($"Text reference catalog '{Catalog}' does not match '{expectedCatalog}'.");
        if (!string.Equals(ContractFingerprint, expectedContractFingerprint, StringComparison.Ordinal))
            throw new TranslationContractException("Text reference contract fingerprint does not match the receiver.");
    }

    private static bool IsSha256Fingerprint(string value)
    {
        if (!value.StartsWith("sha256:", StringComparison.Ordinal) || value.Length != 71) return false;
        for (int index = 7; index < value.Length; index++)
        {
            char character = value[index];
            if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))) return false;
        }
        return true;
    }
}
