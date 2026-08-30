using System;
using System.Runtime.CompilerServices;

namespace Runic.Translations;

/// <summary>A stable machine-readable reason for an external pack failure.</summary>
public enum TranslationPackFailureReason
{
    /// <summary>The exception was not created by the external pack loader or has no registered reason.</summary>
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
}

/// <summary>Reads stable machine-classifiable metadata from external pack exceptions.</summary>
public static class TranslationPackFailure
{
    /// <summary>The reserved diagnostic identity for external pack incompatibility or rejection.</summary>
    public const string DiagnosticId = "RTR0023";

    /// <summary>The stable prefix used by .NET and generated ESM locale-pack-v2 decoders.</summary>
    public const string RejectionIdPrefix = "RTR0023/";

    private static readonly ConditionalWeakTable<TranslationPackException, FailureReasonHolder> Reasons = new();

    /// <summary>
    /// Returns the registered reason, or <see cref="TranslationPackFailureReason.Unknown"/> for
    /// exceptions created outside the external pack loader.
    /// </summary>
    public static TranslationPackFailureReason GetReason(TranslationPackException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return Reasons.TryGetValue(exception, out FailureReasonHolder? holder)
            ? holder.Reason
            : TranslationPackFailureReason.Unknown;
    }

    /// <summary>
    /// Returns the stable location-free diagnostic identity for an external pack failure.
    /// Runtime pack failures do not manufacture a source span.
    /// </summary>
    public static string GetDiagnosticId(TranslationPackException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return DiagnosticId;
    }

    /// <summary>Returns the normalized, location-free locale-pack rejection ID.</summary>
    public static string GetRejectionId(TranslationPackException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return RejectionIdPrefix + RejectionName(GetReason(exception));
    }

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
            TranslationPackException packException => GetReason(packException),
            _ => TranslationPackFailureReason.Unknown,
        };
    }

    internal static TranslationPackException Create(string message, TranslationPackFailureReason reason)
    {
        var exception = new TranslationPackException(message);
        Reasons.Add(exception, new FailureReasonHolder(reason));
        return exception;
    }

    private static string RejectionName(TranslationPackFailureReason reason) => reason switch
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
        _ => "unknown",
    };

    private sealed class FailureReasonHolder
    {
        internal FailureReasonHolder(TranslationPackFailureReason reason) => Reason = reason;
        internal TranslationPackFailureReason Reason { get; }
    }
}
