using System;

namespace Runic.Translations;

/// <summary>Behavior for an unsupported requested locale.</summary>
public enum UnsupportedLocalePolicy
{
    /// <summary>Accept only a declared locale.</summary>
    Exact,
    /// <summary>Strip BCP 47 subtags before using the declared graph and default.</summary>
    ParentsThenDefault,
    /// <summary>Map unsupported tags directly to the default locale.</summary>
    Default,
}

/// <summary>Behavior when a key is absent after fallback.</summary>
public enum MissingTranslationPolicy
{
    /// <summary>Throw <see cref="TranslationNotFoundException"/>.</summary>
    Throw,
    /// <summary>Return the stable dotted key.</summary>
    ReturnKey,
    /// <summary>Return a visible marker containing the stable key.</summary>
    ReturnMarker,
}

/// <summary>Runtime options consumed by generated providers and managers.</summary>
public sealed class TranslationOptions
{
    /// <summary>Unsupported-locale behavior.</summary>
    public UnsupportedLocalePolicy UnsupportedLocale { get; init; } = UnsupportedLocalePolicy.ParentsThenDefault;
    /// <summary>Missing-key behavior.</summary>
    public MissingTranslationPolicy MissingKey { get; init; } = MissingTranslationPolicy.Throw;
}

/// <summary>Thrown when a resource cannot be resolved under the missing-key policy.</summary>
public sealed class TranslationNotFoundException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TranslationNotFoundException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TranslationNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when runtime arguments do not match a compiled message contract.</summary>
public sealed class TranslationFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public TranslationFormatException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TranslationFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when an external pack is malformed, incompatible, or rejected.</summary>
public sealed class TranslationPackException : Exception
{
    /// <summary>The diagnostic identity of every external pack incompatibility or rejection.</summary>
    public const string DiagnosticId = "RTR0023";

    /// <summary>Creates the exception with an <see cref="TranslationPackFailureReason.Unknown"/> reason.</summary>
    public TranslationPackException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause and an <see cref="TranslationPackFailureReason.Unknown"/> reason.</summary>
    public TranslationPackException(string message, Exception innerException) : base(message, innerException) { }
    /// <summary>Creates the exception with a machine-readable reason, for example from a custom <see cref="IExternalTranslationSource"/>.</summary>
    public TranslationPackException(string message, TranslationPackFailureReason reason, Exception? innerException = null)
        : base(message, innerException)
    {
        if (!Enum.IsDefined(reason)) throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown pack failure reason.");
        Reason = reason;
    }

    /// <summary>Why the pack was rejected; <see cref="TranslationPackFailureReason.Unknown"/> when no reason was given.</summary>
    public TranslationPackFailureReason Reason { get; }

    /// <summary>The normalized, location-free rejection ID, such as <c>RTR0023/catalog-mismatch</c>.</summary>
    public string RejectionId => DiagnosticId + "/" + TranslationPackFailure.RejectionName(Reason);
}

/// <summary>Thrown when a transported text reference is incompatible with the receiver.</summary>
public sealed class TranslationContractException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TranslationContractException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TranslationContractException(string message, Exception innerException) : base(message, innerException) { }
}
