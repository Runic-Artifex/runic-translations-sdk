using System;
using System.Collections.Generic;

namespace RunicTextResources;

/// <summary>The independently versioned wire contract for backend-originated localizable text.</summary>
public static class TextResourceTransport
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

/// <summary>A portable, canonical argument carried by a <see cref="TextResourceReference"/>.</summary>
public readonly record struct TextResourceReferenceArgument
{
    /// <summary>Creates a typed argument from its canonical invariant wire value.</summary>
    public TextResourceReferenceArgument(TextArgumentType type, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > TextResourceTransport.MaximumArgumentLength)
            throw new ArgumentOutOfRangeException(nameof(value), "The canonical argument value is too long.");
        Type = type;
        Value = value;
    }

    /// <summary>The portable argument type.</summary>
    public TextArgumentType Type { get; }
    /// <summary>The canonical invariant wire value.</summary>
    public string Value { get; }
}

/// <summary>A stable key-and-arguments envelope for localization by another process.</summary>
public sealed class TextResourceReference
{
    private readonly Dictionary<string, TextResourceReferenceArgument> _arguments;

    /// <summary>Creates an immutable version 1 text reference.</summary>
    public TextResourceReference(
        string catalog,
        string contractFingerprint,
        string key,
        IReadOnlyDictionary<string, TextResourceReferenceArgument>? arguments = null,
        string? fallbackText = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > TextResourceTransport.MaximumKeyLength) throw new ArgumentOutOfRangeException(nameof(key));
        if (fallbackText?.Length > TextResourceTransport.MaximumFallbackLength) throw new ArgumentOutOfRangeException(nameof(fallbackText));
        if (!contractFingerprint.StartsWith("sha256:", StringComparison.Ordinal) || contractFingerprint.Length != 71)
            throw new ArgumentException("A lowercase sha256: contract fingerprint is required.", nameof(contractFingerprint));

        Catalog = catalog;
        Version = TextResourceTransport.Version;
        ContractFingerprint = contractFingerprint;
        Key = key;
        FallbackText = fallbackText;
        _arguments = new Dictionary<string, TextResourceReferenceArgument>(StringComparer.Ordinal);
        if (arguments is null) return;
        if (arguments.Count > TextResourceTransport.MaximumArguments) throw new ArgumentOutOfRangeException(nameof(arguments));
        foreach (KeyValuePair<string, TextResourceReferenceArgument> pair in arguments)
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
    public IReadOnlyDictionary<string, TextResourceReferenceArgument> Arguments => _arguments;
    /// <summary>Optional already-resolved plain text for version skew and inaccessible clients.</summary>
    public string? FallbackText { get; }

    /// <summary>Fails deterministically when a receiver has a different generated catalog contract.</summary>
    public void ValidateCatalog(string expectedCatalog, string expectedContractFingerprint)
    {
        if (!string.Equals(Catalog, expectedCatalog, StringComparison.Ordinal))
            throw new TextResourceContractException($"Text reference catalog '{Catalog}' does not match '{expectedCatalog}'.");
        if (!string.Equals(ContractFingerprint, expectedContractFingerprint, StringComparison.Ordinal))
            throw new TextResourceContractException("Text reference contract fingerprint does not match the receiver.");
    }
}
