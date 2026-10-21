using System;

namespace Runic.Translations;

/// <summary>A stable machine-readable reason for an external pack failure.</summary>
public enum TranslationPackFailureReason
{
    /// <summary>No reason was given: the exception was not created by the pack loader or with a reason. The default.</summary>
    Unknown,
    /// <summary>The pack artifact version is not supported.</summary>
    ArtifactVersionMismatch,
    /// <summary>The pack message grammar version is not supported.</summary>
    MessageGrammarVersionMismatch,
    /// <summary>The pack catalog does not match the generated contract.</summary>
    CatalogMismatch,
    /// <summary>The pack locale does not match the requested canonical locale.</summary>
    LocaleMismatch,
    /// <summary>The pack fingerprint does not match the generated contract.</summary>
    ContractFingerprintMismatch,
    /// <summary>A configured document, depth, message, pattern, or argument limit was exceeded.</summary>
    LimitExceeded,
    /// <summary>The pack contains a message key outside the generated contract.</summary>
    UnknownKey,
    /// <summary>A message argument descriptor differs from the generated contract.</summary>
    ArgumentContractMismatch,
    /// <summary>A message pattern is malformed or differs from its generated argument contract.</summary>
    MalformedPattern,
    /// <summary>The pack contains an unknown root, message, or argument member.</summary>
    UnknownMember,
    /// <summary>The integrity policy rejected the pack or failed to verify it.</summary>
    IntegrityRejected,
    /// <summary>The explicit caller-provided pack source failed.</summary>
    SourceFailure,
    /// <summary>Pack loading or parsing was cancelled and no successful value may be cached.</summary>
    Cancelled,
    /// <summary>The pack is empty, malformed, invalid UTF-8, incomplete, or has an unsupported shape.</summary>
    Malformed,
    /// <summary>The pack was built for another RMF2 markup contract version and must be rebuilt with the current compiler.</summary>
    MarkupContractVersionMismatch,
    /// <summary>A document message variant has a block structure that is not one of the skeletons locked by the generated contract.</summary>
    DocumentStructureMismatch,
}

/// <summary>Classifies external pack failures, including cancellation.</summary>
public static class TranslationPackFailure
{
    /// <summary>
    /// Classifies a pack exception or cancellation without requiring callers to branch on
    /// exception type before reading the stable reason.
    /// </summary>
    public static TranslationPackFailureReason GetReason(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            OperationCanceledException => TranslationPackFailureReason.Cancelled,
            TranslationPackException packException => packException.Reason,
            _ => TranslationPackFailureReason.Unknown,
        };
    }

    internal static TranslationPackException Create(string message, TranslationPackFailureReason reason) => new(message, reason);

    internal static string RejectionName(TranslationPackFailureReason reason) => reason switch
    {
        TranslationPackFailureReason.ArtifactVersionMismatch => "artifact-version-mismatch",
        TranslationPackFailureReason.MessageGrammarVersionMismatch => "message-grammar-version-mismatch",
        TranslationPackFailureReason.CatalogMismatch => "catalog-mismatch",
        TranslationPackFailureReason.LocaleMismatch => "locale-mismatch",
        TranslationPackFailureReason.ContractFingerprintMismatch => "contract-fingerprint-mismatch",
        TranslationPackFailureReason.LimitExceeded => "limit-exceeded",
        TranslationPackFailureReason.UnknownKey => "unknown-key",
        TranslationPackFailureReason.ArgumentContractMismatch => "argument-contract-mismatch",
        TranslationPackFailureReason.MalformedPattern => "malformed-pattern",
        TranslationPackFailureReason.UnknownMember => "unknown-member",
        TranslationPackFailureReason.IntegrityRejected => "integrity-rejected",
        TranslationPackFailureReason.SourceFailure => "source-failure",
        TranslationPackFailureReason.Cancelled => "cancelled",
        TranslationPackFailureReason.Malformed => "malformed",
        TranslationPackFailureReason.MarkupContractVersionMismatch => "markup-contract-version-mismatch",
        TranslationPackFailureReason.DocumentStructureMismatch => "document-structure-mismatch",
        _ => "unknown",
    };
}
