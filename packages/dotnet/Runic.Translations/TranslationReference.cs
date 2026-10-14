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
    /// <remarks>Version 2 tags each argument with its type and adds list and message arguments. Readers also accept version 1.</remarks>
    public const int Version = 2;
    /// <summary>The oldest transport version that readers accept.</summary>
    public const int MinimumReadVersion = 1;
    /// <summary>The maximum number of arguments in one reference.</summary>
    public const int MaximumArguments = 32;
    /// <summary>The maximum stable key length.</summary>
    public const int MaximumKeyLength = 512;
    /// <summary>The maximum canonical value length of one argument; the items of a list argument share it.</summary>
    public const int MaximumArgumentLength = 16 * 1024;
    /// <summary>The maximum optional fallback-text length.</summary>
    public const int MaximumFallbackLength = 64 * 1024;
    /// <summary>The maximum number of items in one list argument.</summary>
    public const int MaximumListItems = 256;
    /// <summary>The maximum nesting depth of message arguments, counting the outermost reference as 1.</summary>
    public const int MaximumDepth = 8;
    /// <summary>The maximum number of references in one reference tree, the outermost reference included.</summary>
    public const int MaximumReferences = 32;

    internal static string WireType(TextArgumentType type) => type switch
    {
        TextArgumentType.String => "string",
        TextArgumentType.Int => "int64",
        TextArgumentType.Number => "decimal",
        TextArgumentType.Bool => "boolean",
        TextArgumentType.Date => "date",
        TextArgumentType.Time => "time",
        TextArgumentType.DateTime => "datetime",
        TextArgumentType.Guid => "guid",
        TextArgumentType.List => "list",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    internal static bool TryParseWireType(string name, out TextArgumentType type)
    {
        switch (name)
        {
            case "string": type = TextArgumentType.String; return true;
            case "int64": type = TextArgumentType.Int; return true;
            case "decimal": type = TextArgumentType.Number; return true;
            case "boolean": type = TextArgumentType.Bool; return true;
            case "date": type = TextArgumentType.Date; return true;
            case "time": type = TextArgumentType.Time; return true;
            case "datetime": type = TextArgumentType.DateTime; return true;
            case "guid": type = TextArgumentType.Guid; return true;
            case "list": type = TextArgumentType.List; return true;
            default: type = default; return false;
        }
    }
}

/// <summary>A portable, canonical argument carried by a <see cref="TranslationReference"/>.</summary>
/// <remarks>
/// An argument is a scalar with a canonical invariant <see cref="Value"/>, a list of strings (<see cref="Items"/>), or another
/// message (<see cref="Message"/>), which fills a string input with that message's text when the reference is resolved.
/// </remarks>
public readonly record struct TranslationReferenceArgument
{
    /// <summary>Creates a typed scalar argument from its canonical invariant wire value.</summary>
    /// <remarks>
    /// Integers and decimals use invariant digits without a plus sign, exponent or negative zero; dates use <c>yyyy-MM-dd</c>,
    /// times <c>HH:mm:ss</c>, date-times UTC <c>yyyy-MM-ddTHH:mm:ssZ</c> and GUIDs the lowercase <c>D</c> format.
    /// Use the list constructor for <see cref="TextArgumentType.List"/>.
    /// </remarks>
    public TranslationReferenceArgument(TextArgumentType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > TranslationTransport.MaximumArgumentLength)
            throw new ArgumentOutOfRangeException(nameof(value), "The canonical argument value is too long.");
        if (type == TextArgumentType.List) throw new ArgumentException("Create a list argument from its items.", nameof(type));
        ValidateCanonical(type, value);
        Type = type;
        Value = value;
    }

    /// <summary>Creates a list argument from a snapshot of <paramref name="items"/>.</summary>
    public TranslationReferenceArgument(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count > TranslationTransport.MaximumListItems) throw new ArgumentOutOfRangeException(nameof(items), "The list has too many items.");
        var copy = new string[items.Count];
        int length = 0;
        for (int index = 0; index < copy.Length; index++)
        {
            copy[index] = items[index] ?? throw new ArgumentException("List items cannot be null.", nameof(items));
            length += copy[index].Length;
            if (length > TranslationTransport.MaximumArgumentLength) throw new ArgumentOutOfRangeException(nameof(items), "The list items are too long.");
        }
        Type = TextArgumentType.List;
        Value = string.Empty;
        Items = Array.AsReadOnly(copy);
    }

    /// <summary>Creates an argument whose text is another message of the same catalog.</summary>
    /// <remarks>The referenced message fills a string input. It must use the catalog and contract fingerprint of the reference that carries it.</remarks>
    public TranslationReferenceArgument(TranslationReference message)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Version != TranslationTransport.Version) throw new ArgumentException("A version 1 reference cannot be a message argument.", nameof(message));
        Type = TextArgumentType.String;
        Value = string.Empty;
        Message = message;
    }

    /// <summary>The portable argument type. A message argument has the type <see cref="TextArgumentType.String"/>.</summary>
    public TextArgumentType Type { get; }
    /// <summary>The canonical invariant wire value of a scalar; empty for a list or message argument.</summary>
    public string Value { get; }
    /// <summary>The items of a list argument; otherwise <see langword="null"/>.</summary>
    public IReadOnlyList<string>? Items { get; }
    /// <summary>The referenced message of a message argument; otherwise <see langword="null"/>.</summary>
    public TranslationReference? Message { get; }

    private static void ValidateCanonical(TextArgumentType type, string value)
    {
        bool valid = type switch
        {
            TextArgumentType.String => true,
            TextArgumentType.Bool => value is "true" or "false",
            TextArgumentType.Int => long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer) &&
                value == integer.ToString(CultureInfo.InvariantCulture),
            TextArgumentType.Number => IsCanonicalDecimal(value),
            TextArgumentType.Date => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TextArgumentType.Time => TimeOnly.TryParseExact(value, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            TextArgumentType.DateTime => value.Length == 20 && value[^1] == 'Z' &&
                DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _),
            TextArgumentType.Guid => Guid.TryParseExact(value, "D", out Guid guid) && value == guid.ToString("D", CultureInfo.InvariantCulture),
            _ => false,
        };
        if (!valid) throw new ArgumentException("The value is not canonical for the declared portable type.", nameof(value));
    }

    // Plain invariant digits with an optional fraction; no sign on zero, so every value has one spelling apart from its scale.
    private static bool IsCanonicalDecimal(string value)
    {
        if (!decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal number) ||
            value != number.ToString(CultureInfo.InvariantCulture))
            return false;
        return !(value[0] == '-' && number == 0m);
    }

    internal bool IsScalar => Items is null && Message is null;
}

/// <summary>A stable key-and-arguments envelope for localization by another process or at a later time.</summary>
/// <remarks>
/// See <see cref="TranslationReferenceResolver"/> for how a reference is displayed after the catalog has changed.
/// A version 1 reference can only be read from JSON; new references are version 2.
/// </remarks>
[JsonConverter(typeof(TranslationReferenceJsonConverter))]
public sealed class TranslationReference
{
    private readonly Dictionary<string, TranslationReferenceArgument> _arguments;
    private readonly ReadOnlyDictionary<string, TranslationReferenceArgument> _readOnlyArguments;

    /// <summary>Creates an immutable version 2 text reference.</summary>
    public TranslationReference(
        string catalog,
        string contractFingerprint,
        string key,
        IReadOnlyDictionary<string, TranslationReferenceArgument>? arguments = null,
        string? fallbackText = null)
        : this(TranslationTransport.Version, catalog, contractFingerprint, key, arguments, fallbackText)
    {
    }

    internal TranslationReference(
        int version,
        string catalog,
        string contractFingerprint,
        string key,
        IReadOnlyDictionary<string, TranslationReferenceArgument>? arguments,
        string? fallbackText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > TranslationTransport.MaximumKeyLength) throw new ArgumentOutOfRangeException(nameof(key));
        if (fallbackText?.Length > TranslationTransport.MaximumFallbackLength) throw new ArgumentOutOfRangeException(nameof(fallbackText));
        if (!IsSha256Fingerprint(contractFingerprint))
            throw new ArgumentException("A lowercase sha256: contract fingerprint is required.", nameof(contractFingerprint));

        Catalog = catalog;
        Version = version;
        ContractFingerprint = contractFingerprint;
        Key = key;
        FallbackText = fallbackText;
        Depth = 1;
        ReferenceCount = 1;
        _arguments = new Dictionary<string, TranslationReferenceArgument>(StringComparer.Ordinal);
        _readOnlyArguments = new ReadOnlyDictionary<string, TranslationReferenceArgument>(_arguments);
        if (arguments is null) return;
        if (arguments.Count > TranslationTransport.MaximumArguments) throw new ArgumentOutOfRangeException(nameof(arguments));
        foreach (KeyValuePair<string, TranslationReferenceArgument> pair in arguments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            TranslationReferenceArgument argument = pair.Value;
            if (argument.Value is null) throw new ArgumentException("An argument has no value.", nameof(arguments));
            if (version == 1 && !argument.IsScalar)
                throw new ArgumentException("A version 1 reference carries only scalar arguments.", nameof(arguments));
            if (argument.Message is { } message)
            {
                if (!string.Equals(message.Catalog, catalog, StringComparison.Ordinal) ||
                    !string.Equals(message.ContractFingerprint, contractFingerprint, StringComparison.Ordinal))
                    throw new ArgumentException("A message argument must use the catalog and contract fingerprint of its reference.", nameof(arguments));
                Depth = Math.Max(Depth, message.Depth + 1);
                ReferenceCount += message.ReferenceCount;
                if (Depth > TranslationTransport.MaximumDepth)
                    throw new ArgumentOutOfRangeException(nameof(arguments), "Message arguments are nested too deeply.");
                if (ReferenceCount > TranslationTransport.MaximumReferences)
                    throw new ArgumentOutOfRangeException(nameof(arguments), "The reference contains too many message arguments.");
            }
            if (!_arguments.TryAdd(pair.Key, argument)) throw new ArgumentException("Argument names must be unique.", nameof(arguments));
        }
    }

    /// <summary>The transport contract version: 2 for a new reference, or 1 for a reference read from version 1 JSON.</summary>
    public int Version { get; }
    /// <summary>The stable catalog ID.</summary>
    public string Catalog { get; }
    /// <summary>The sender's generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The stable dotted key, never a process-local integer ID.</summary>
    public string Key { get; }
    /// <summary>The canonical typed arguments.</summary>
    public IReadOnlyDictionary<string, TranslationReferenceArgument> Arguments => _readOnlyArguments;
    /// <summary>Optional already-resolved plain text, shown when the message cannot be translated.</summary>
    public string? FallbackText { get; }

    internal int Depth { get; }
    internal int ReferenceCount { get; }

    /// <summary>Fails deterministically when a receiver has a different generated catalog contract.</summary>
    /// <remarks>
    /// Use it where sender and receiver must be built from the same catalog, such as live messages between processes of one release.
    /// To display a stored reference after the catalog has changed, use <see cref="TranslationReferenceResolver.Resolve"/>.
    /// </remarks>
    public void ValidateCatalog(string expectedCatalog, string expectedContractFingerprint)
    {
        if (!string.Equals(Catalog, expectedCatalog, StringComparison.Ordinal))
            throw new TranslationContractException($"Text reference catalog '{Catalog}' does not match '{expectedCatalog}'.");
        if (!string.Equals(ContractFingerprint, expectedContractFingerprint, StringComparison.Ordinal))
            throw new TranslationContractException("Text reference contract fingerprint does not match the receiver.");
    }

    internal static bool IsSha256Fingerprint(string value)
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
