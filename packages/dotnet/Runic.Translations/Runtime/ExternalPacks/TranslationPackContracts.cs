using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations;

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
        : this(key, arguments, false)
    {
    }

    /// <summary>Creates a v5 message contract from NFC RMF2 caller inputs.</summary>
    public static TranslationPackMessageContract FromRmf2Inputs(
        TranslationKey key,
        IReadOnlyList<CompiledRmf2Input> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var arguments = new TranslationPackArgumentContract[inputs.Count];
        for (int i = 0; i < arguments.Length; i++)
        {
            CompiledRmf2Input input = inputs[i]
                ?? throw new ArgumentException("RMF2 caller inputs cannot contain null.", nameof(inputs));
            arguments[i] = new TranslationPackArgumentContract(input.Name, input.Type, DefaultFormat(input.Type));
        }
        return new TranslationPackMessageContract(key, arguments, true);
    }

    private TranslationPackMessageContract(
        TranslationKey key,
        IReadOnlyList<TranslationPackArgumentContract>? arguments,
        bool rmf2)
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
            if (rmf2 ? !TranslationPackValidation.IsRmf2Name(argument.Name) : !TranslationPackValidation.IsIdentifier(argument.Name))
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

    private static TextArgumentFormat DefaultFormat(TextArgumentType type) => type switch
    {
        TextArgumentType.String => TextArgumentFormat.None,
        TextArgumentType.Int or TextArgumentType.Number => TextArgumentFormat.Plain,
        TextArgumentType.Bool => TextArgumentFormat.Lower,
        TextArgumentType.Guid => TextArgumentFormat.D,
        TextArgumentType.Date or TextArgumentType.Time or TextArgumentType.DateTime => TextArgumentFormat.Iso,
        _ => throw new ArgumentException("Unknown RMF2 caller input type.", nameof(type)),
    };

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

    /// <summary>Creates an RMF2 execution-v2 contract for one resolved locale artifact v5.</summary>
    public static TranslationPackContract CreateRmf2V5(string catalog, string locale, string contractFingerprint,
        IReadOnlyList<TranslationPackMessageContract> messages, string rmf2MarkupContract) =>
        new(catalog, locale, contractFingerprint, messages, 5,
            rmf2MarkupContract ?? throw new ArgumentNullException(nameof(rmf2MarkupContract)), "rmf2-execution-v2");

    private TranslationPackContract(string catalog, string locale, string contractFingerprint,
        IReadOnlyList<TranslationPackMessageContract> messages, int messageGrammarVersion, string? rmf2MarkupContract, string? profile)
    {
        if (messageGrammarVersion != 5 || profile != "rmf2-execution-v2" || rmf2MarkupContract is null)
            throw new ArgumentException("External translation packs require the RMF2 v5 execution profile.", nameof(profile));
        Rmf2MarkupContract = rmf2MarkupContract;
        Profile = profile;
        ArgumentNullException.ThrowIfNull(messages);
        if (!TranslationPackValidation.IsCatalog(catalog))
            throw new ArgumentException("The catalog identifier is invalid.", nameof(catalog));
        if (!TranslationPackValidation.IsCanonicalLocale(locale))
            throw new ArgumentException("The locale must be a canonical structural BCP 47 tag.", nameof(locale));
        if (!TranslationPackValidation.IsFingerprint(contractFingerprint))
            throw new ArgumentException("The fingerprint must be lowercase sha256 hexadecimal text.", nameof(contractFingerprint));
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
    /// <summary>The required RMF2 execution profile.</summary>
    public string? Profile { get; }
    /// <summary>The trusted language-neutral manifest required before RMF2 pack activation.</summary>
    public string? Rmf2MarkupContract { get; }
    /// <summary>The ordinal-sorted known message contracts.</summary>
    public IReadOnlyList<TranslationPackMessageContract> Messages => _messages;

    internal bool TryGetMessage(string name, out TranslationPackMessageContract contract) =>
        _messagesByName.TryGetValue(name, out contract!);
}

/// <summary>One fully verified external message value.</summary>
public sealed class VerifiedTranslationPackMessage
{
    internal VerifiedTranslationPackMessage(TranslationKey key, CompiledTextMessage message)
    { Key = key; Message = message; }

    /// <summary>The generated known key.</summary>
    public TranslationKey Key { get; }
    /// <summary>The verified normalized RMF2 v5 message.</summary>
    public CompiledTextMessage Message { get; }
}

/// <summary>Immutable external pack data that passed integrity, shape, and compatibility validation.</summary>
public sealed class VerifiedExternalTranslationPack
{
    private readonly ReadOnlyCollection<VerifiedTranslationPackMessage> _messages;

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
    }

    /// <summary>The verified catalog identifier.</summary>
    public string Catalog { get; }
    /// <summary>The verified canonical locale.</summary>
    public string Locale { get; }
    /// <summary>The verified generated contract fingerprint.</summary>
    public string ContractFingerprint { get; }
    /// <summary>The verified messages in ordinal key order.</summary>
    public IReadOnlyList<VerifiedTranslationPackMessage> Messages => _messages;
}
