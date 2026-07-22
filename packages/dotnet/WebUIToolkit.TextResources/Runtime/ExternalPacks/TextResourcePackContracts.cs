using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.TextResources;

/// <summary>Performs caller-defined integrity verification before an external pack is parsed.</summary>
/// <param name="content">The complete caller-owned pack bytes.</param>
/// <param name="cancellationToken">Cancels integrity verification.</param>
/// <returns><see langword="true"/> when the bytes are trusted enough to parse.</returns>
public delegate ValueTask<bool> TextResourcePackIntegrityVerifier(
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

/// <summary>Bounds untrusted external pack input.</summary>
public sealed class TextResourcePackLimits
{
    /// <summary>The maximum supported external pack size.</summary>
    public const int DefaultMaximumDocumentBytes = 8 * 1024 * 1024;
    /// <summary>The maximum supported JSON nesting depth.</summary>
    public const int DefaultMaximumDepth = 64;
    /// <summary>The maximum supported messages per pack.</summary>
    public const int DefaultMaximumMessages = 50_000;
    /// <summary>The maximum supported UTF-8 bytes in one pattern.</summary>
    public const int DefaultMaximumPatternBytes = 64 * 1024;
    /// <summary>The maximum supported arguments in one message.</summary>
    public const int DefaultMaximumArgumentsPerMessage = 32;

    /// <summary>Creates the default runtime limits.</summary>
    public TextResourcePackLimits()
        : this(DefaultMaximumDocumentBytes, DefaultMaximumDepth, DefaultMaximumMessages,
            DefaultMaximumPatternBytes, DefaultMaximumArgumentsPerMessage)
    {
    }

    /// <summary>Creates limits no less restrictive than the runtime defaults.</summary>
    public TextResourcePackLimits(
        int maximumDocumentBytes,
        int maximumDepth,
        int maximumMessages,
        int maximumPatternBytes,
        int maximumArgumentsPerMessage)
    {
        MaximumDocumentBytes = Tightened(maximumDocumentBytes, DefaultMaximumDocumentBytes, nameof(maximumDocumentBytes));
        MaximumDepth = Tightened(maximumDepth, DefaultMaximumDepth, nameof(maximumDepth));
        MaximumMessages = Tightened(maximumMessages, DefaultMaximumMessages, nameof(maximumMessages));
        MaximumPatternBytes = Tightened(maximumPatternBytes, DefaultMaximumPatternBytes, nameof(maximumPatternBytes));
        MaximumArgumentsPerMessage = Tightened(maximumArgumentsPerMessage, DefaultMaximumArgumentsPerMessage, nameof(maximumArgumentsPerMessage));
    }

    /// <summary>The maximum complete document size.</summary>
    public int MaximumDocumentBytes { get; }
    /// <summary>The maximum JSON nesting depth.</summary>
    public int MaximumDepth { get; }
    /// <summary>The maximum number of message entries.</summary>
    public int MaximumMessages { get; }
    /// <summary>The maximum UTF-8 byte length of one decoded pattern.</summary>
    public int MaximumPatternBytes { get; }
    /// <summary>The maximum arguments in one message.</summary>
    public int MaximumArgumentsPerMessage { get; }

    private static int Tightened(int value, int maximum, string parameterName)
    {
        if (value <= 0 || value > maximum)
        {
            throw new ArgumentOutOfRangeException(parameterName, value,
                "External pack limits must be positive and cannot exceed the runtime default.");
        }

        return value;
    }
}

/// <summary>Describes one generated placeholder contract.</summary>
public readonly record struct TextResourcePackArgumentContract(
    string Name,
    TextArgumentType Type,
    TextArgumentFormat Format);

/// <summary>Describes one generated key and its locale-independent placeholder contract.</summary>
public sealed class TextResourcePackMessageContract
{
    private readonly ReadOnlyCollection<TextResourcePackArgumentContract> _arguments;

    /// <summary>Creates a generated message contract.</summary>
    public TextResourcePackMessageContract(
        TextResourceKey key,
        IReadOnlyList<TextResourcePackArgumentContract>? arguments = null)
    {
        if (string.IsNullOrEmpty(key.Catalog)) throw new ArgumentException("A key catalog is required.", nameof(key));
        if (key.Id < 0) throw new ArgumentOutOfRangeException(nameof(key), "A key identifier cannot be negative.");
        if (!TextResourcePackValidation.IsResourceKey(key.Name))
            throw new ArgumentException("The key name is not a valid dotted resource key.", nameof(key));

        Key = key;
        var copy = new TextResourcePackArgumentContract[arguments?.Count ?? 0];
        string? previousName = null;
        for (int i = 0; i < copy.Length; i++)
        {
            TextResourcePackArgumentContract argument = arguments![i];
            if (!TextResourcePackValidation.IsIdentifier(argument.Name))
                throw new ArgumentException("An argument name is invalid.", nameof(arguments));
            if (previousName is not null && string.CompareOrdinal(previousName, argument.Name) >= 0)
                throw new ArgumentException("Argument contracts must be unique and ordinal-sorted.", nameof(arguments));
            if (!TextResourcePackValidation.IsFormatAllowed(argument.Type, argument.Format))
                throw new ArgumentException("An argument type and format combination is invalid.", nameof(arguments));
            copy[i] = argument;
            previousName = argument.Name;
        }

        _arguments = Array.AsReadOnly(copy);
    }

    /// <summary>The generated key.</summary>
    public TextResourceKey Key { get; }
    /// <summary>The ordinal-sorted placeholder contract.</summary>
    public IReadOnlyList<TextResourcePackArgumentContract> Arguments => _arguments;
}

/// <summary>The generated compatibility contract used to validate one locale pack.</summary>
public sealed class TextResourcePackContract
{
    private readonly ReadOnlyCollection<TextResourcePackMessageContract> _messages;
    private readonly Dictionary<string, TextResourcePackMessageContract> _messagesByName;

    /// <summary>Creates a contract for one catalog and canonical locale.</summary>
    public TextResourcePackContract(
        string catalog,
        string locale,
        string contractFingerprint,
        IReadOnlyList<TextResourcePackMessageContract> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!TextResourcePackValidation.IsCatalog(catalog))
            throw new ArgumentException("The catalog identifier is invalid.", nameof(catalog));
        if (!TextResourcePackValidation.IsCanonicalLocale(locale))
            throw new ArgumentException("The locale must be a canonical structural BCP 47 tag.", nameof(locale));
        if (!TextResourcePackValidation.IsFingerprint(contractFingerprint))
            throw new ArgumentException("The fingerprint must be lowercase sha256 hexadecimal text.", nameof(contractFingerprint));

        Catalog = catalog;
        Locale = locale;
        ContractFingerprint = contractFingerprint;
        var copy = new TextResourcePackMessageContract[messages.Count];
        _messagesByName = new Dictionary<string, TextResourcePackMessageContract>(messages.Count, StringComparer.Ordinal);
        string? previousKey = null;
        for (int i = 0; i < copy.Length; i++)
        {
            TextResourcePackMessageContract message = messages[i]
                ?? throw new ArgumentException("Message contracts cannot contain null.", nameof(messages));
            if (!string.Equals(message.Key.Catalog, catalog, StringComparison.Ordinal))
                throw new ArgumentException("Every message key must belong to the contract catalog.", nameof(messages));
            if (previousKey is not null && string.CompareOrdinal(previousKey, message.Key.Name) >= 0)
                throw new ArgumentException("Message contracts must be unique and ordinal-sorted.", nameof(messages));
            copy[i] = message;
            _messagesByName.Add(message.Key.Name, message);
            previousKey = message.Key.Name;
        }

        _messages = Array.AsReadOnly(copy);
    }

    /// <summary>The stable catalog identifier.</summary>
    public string Catalog { get; }
    /// <summary>The canonical locale expected in the pack.</summary>
    public string Locale { get; }
    /// <summary>The generated catalog contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The ordinal-sorted known message contracts.</summary>
    public IReadOnlyList<TextResourcePackMessageContract> Messages => _messages;

    internal bool TryGetMessage(string name, out TextResourcePackMessageContract contract) =>
        _messagesByName.TryGetValue(name, out contract!);
}

/// <summary>One fully verified external message value.</summary>
public sealed class VerifiedTextResourcePackMessage
{
    internal VerifiedTextResourcePackMessage(TextResourceKey key, string pattern) { Key = key; Pattern = pattern; }

    /// <summary>The generated known key.</summary>
    public TextResourceKey Key { get; }
    /// <summary>The validated plain-text message pattern.</summary>
    public string Pattern { get; }
}

/// <summary>Immutable external pack data that passed integrity, shape, and compatibility validation.</summary>
public sealed class VerifiedExternalTextResourcePack
{
    private readonly ReadOnlyCollection<VerifiedTextResourcePackMessage> _messages;
    private readonly Dictionary<TextResourceKey, string> _patterns;

    internal VerifiedExternalTextResourcePack(
        string catalog,
        string locale,
        string contractFingerprint,
        VerifiedTextResourcePackMessage[] messages)
    {
        Catalog = catalog;
        Locale = locale;
        ContractFingerprint = contractFingerprint;
        _messages = Array.AsReadOnly(messages);
        _patterns = new Dictionary<TextResourceKey, string>(messages.Length);
        for (int i = 0; i < messages.Length; i++) _patterns.Add(messages[i].Key, messages[i].Pattern);
    }

    /// <summary>The verified catalog identifier.</summary>
    public string Catalog { get; }
    /// <summary>The verified canonical locale.</summary>
    public string Locale { get; }
    /// <summary>The verified generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The verified messages in ordinal key order.</summary>
    public IReadOnlyList<VerifiedTextResourcePackMessage> Messages => _messages;

    /// <summary>Attempts to obtain a verified replacement pattern for a generated key.</summary>
    public bool TryGetPattern(TextResourceKey key, out string pattern) => _patterns.TryGetValue(key, out pattern!);
}
