using System;

namespace RunicTranslations;

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
    /// <summary>Gets or sets unsupported-locale behavior.</summary>
    public UnsupportedLocalePolicy UnsupportedLocale { get; set; } = UnsupportedLocalePolicy.ParentsThenDefault;
    /// <summary>Gets or sets missing-key behavior.</summary>
    public MissingTranslationPolicy MissingKey { get; set; } = MissingTranslationPolicy.Throw;
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
    /// <summary>Creates the exception.</summary>
    public TranslationPackException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TranslationPackException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a transported text reference is incompatible with the receiver.</summary>
public sealed class TranslationContractException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TranslationContractException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TranslationContractException(string message, Exception innerException) : base(message, innerException) { }
}
