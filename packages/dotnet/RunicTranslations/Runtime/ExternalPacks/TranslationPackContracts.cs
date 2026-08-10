using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTranslations;

/// <summary>Performs caller-defined integrity verification before an external pack is parsed.</summary>
/// <param name="content">The complete caller-owned pack bytes.</param>
/// <param name="cancellationToken">Cancels integrity verification.</param>
/// <returns><see langword="true"/> when the bytes are trusted enough to parse.</returns>
public delegate ValueTask<bool> TranslationPackIntegrityVerifier(
    ReadOnlyMemory<byte> content,
    CancellationToken cancellationToken);

/// <summary>Bounds untrusted external pack input.</summary>
public sealed class TranslationPackLimits
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
    public TranslationPackLimits()
        : this(DefaultMaximumDocumentBytes, DefaultMaximumDepth, DefaultMaximumMessages,
            DefaultMaximumPatternBytes, DefaultMaximumArgumentsPerMessage)
    {
    }

    /// <summary>Creates limits no less restrictive than the runtime defaults.</summary>
    public TranslationPackLimits(
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
public readonly record struct TranslationPackArgumentContract(
    string Name,
    TextArgumentType Type,
    TextArgumentFormat Format);

/// <summary>Describes one generated key and its locale-independent placeholder contract.</summary>
public sealed class TranslationPackMessageContract
{
    private readonly ReadOnlyCollection<TranslationPackArgumentContract> _arguments;

    /// <summary>Creates a generated message contract.</summary>
    public TranslationPackMessageContract(
        TranslationKey key,
        IReadOnlyList<TranslationPackArgumentContract>? arguments = null)
    {
        if (string.IsNullOrEmpty(key.Catalog)) throw new ArgumentException("A key catalog is required.", nameof(key));
        if (key.Id < 0) throw new ArgumentOutOfRangeException(nameof(key), "A key identifier cannot be negative.");
        if (!TranslationPackValidation.IsResourceKey(key.Name))
            throw new ArgumentException("The key name is not a valid dotted resource key.", nameof(key));

        Key = key;
        var copy = new TranslationPackArgumentContract[arguments?.Count ?? 0];
        string? previousName = null;
        for (int i = 0; i < copy.Length; i++)
        {
            TranslationPackArgumentContract argument = arguments![i];
            if (!TranslationPackValidation.IsIdentifier(argument.Name))
                throw new ArgumentException("An argument name is invalid.", nameof(arguments));
            if (previousName is not null && string.CompareOrdinal(previousName, argument.Name) >= 0)
                throw new ArgumentException("Argument contracts must be unique and ordinal-sorted.", nameof(arguments));
            if (!TranslationPackValidation.IsFormatAllowed(argument.Type, argument.Format))
                throw new ArgumentException("An argument type and format combination is invalid.", nameof(arguments));
            copy[i] = argument;
            previousName = argument.Name;
        }

        _arguments = Array.AsReadOnly(copy);
    }

    /// <summary>The generated key.</summary>
    public TranslationKey Key { get; }
    /// <summary>The ordinal-sorted placeholder contract.</summary>
    public IReadOnlyList<TranslationPackArgumentContract> Arguments => _arguments;
}

/// <summary>The generated compatibility contract used to validate one locale pack.</summary>
public sealed class TranslationPackContract
{
    private readonly ReadOnlyCollection<TranslationPackMessageContract> _messages;
    private readonly Dictionary<string, TranslationPackMessageContract> _messagesByName;

    /// <summary>Creates a contract for one catalog and canonical locale.</summary>
    public TranslationPackContract(
        string catalog,
        string locale,
        string contractFingerprint,
        IReadOnlyList<TranslationPackMessageContract> messages,
        int messageGrammarVersion = 1)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (!TranslationPackValidation.IsCatalog(catalog))
            throw new ArgumentException("The catalog identifier is invalid.", nameof(catalog));
        if (!TranslationPackValidation.IsCanonicalLocale(locale))
            throw new ArgumentException("The locale must be a canonical structural BCP 47 tag.", nameof(locale));
        if (!TranslationPackValidation.IsFingerprint(contractFingerprint))
            throw new ArgumentException("The fingerprint must be lowercase sha256 hexadecimal text.", nameof(contractFingerprint));
        if (messageGrammarVersion is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(messageGrammarVersion));

        Catalog = catalog;
        Locale = locale;
        ContractFingerprint = contractFingerprint;
        MessageGrammarVersion = messageGrammarVersion;
        var copy = new TranslationPackMessageContract[messages.Count];
        _messagesByName = new Dictionary<string, TranslationPackMessageContract>(messages.Count, StringComparer.Ordinal);
        string? previousKey = null;
        for (int i = 0; i < copy.Length; i++)
        {
            TranslationPackMessageContract message = messages[i]
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
    /// <summary>The message grammar expected in a matching locale artifact.</summary>
    public int MessageGrammarVersion { get; }
    /// <summary>The ordinal-sorted known message contracts.</summary>
    public IReadOnlyList<TranslationPackMessageContract> Messages => _messages;

    internal bool TryGetMessage(string name, out TranslationPackMessageContract contract) =>
        _messagesByName.TryGetValue(name, out contract!);
}

/// <summary>One fully verified external message value.</summary>
public sealed class VerifiedTranslationPackMessage
{
    internal VerifiedTranslationPackMessage(TranslationKey key, string pattern, CompiledTextMessage? message = null)
    { Key = key; Pattern = pattern; Message = message; }

    /// <summary>The generated known key.</summary>
    public TranslationKey Key { get; }
    /// <summary>The validated plain-text message pattern.</summary>
    public string Pattern { get; }
    /// <summary>The verified normalized message for grammar v2, or null for grammar v1.</summary>
    public CompiledTextMessage? Message { get; }
}

/// <summary>Immutable external pack data that passed integrity, shape, and compatibility validation.</summary>
public sealed class VerifiedExternalTranslationPack
{
    private readonly ReadOnlyCollection<VerifiedTranslationPackMessage> _messages;
    private readonly Dictionary<TranslationKey, string> _patterns;

    internal VerifiedExternalTranslationPack(
        string catalog,
        string locale,
        string contractFingerprint,
        VerifiedTranslationPackMessage[] messages)
    {
        Catalog = catalog;
        Locale = locale;
        ContractFingerprint = contractFingerprint;
        _messages = Array.AsReadOnly(messages);
        _patterns = new Dictionary<TranslationKey, string>(messages.Length);
        for (int i = 0; i < messages.Length; i++) _patterns.Add(messages[i].Key, messages[i].Pattern);
    }

    /// <summary>The verified catalog identifier.</summary>
    public string Catalog { get; }
    /// <summary>The verified canonical locale.</summary>
    public string Locale { get; }
    /// <summary>The verified generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The verified messages in ordinal key order.</summary>
    public IReadOnlyList<VerifiedTranslationPackMessage> Messages => _messages;

    /// <summary>Attempts to obtain a verified replacement pattern for a generated key.</summary>
    public bool TryGetPattern(TranslationKey key, out string pattern) => _patterns.TryGetValue(key, out pattern!);
}
