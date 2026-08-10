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
public enum MissingTextResourcePolicy
{
    /// <summary>Throw <see cref="TextResourceNotFoundException"/>.</summary>
    Throw,
    /// <summary>Return the stable dotted key.</summary>
    ReturnKey,
    /// <summary>Return a visible marker containing the stable key.</summary>
    ReturnMarker,
}

/// <summary>Runtime options consumed by generated providers and managers.</summary>
public sealed class TextResourceOptions
{
    /// <summary>Gets or sets unsupported-locale behavior.</summary>
    public UnsupportedLocalePolicy UnsupportedLocale { get; set; } = UnsupportedLocalePolicy.ParentsThenDefault;
    /// <summary>Gets or sets missing-key behavior.</summary>
    public MissingTextResourcePolicy MissingKey { get; set; } = MissingTextResourcePolicy.Throw;
}

/// <summary>Thrown when a resource cannot be resolved under the missing-key policy.</summary>
public sealed class TextResourceNotFoundException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TextResourceNotFoundException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TextResourceNotFoundException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when runtime arguments do not match a compiled message contract.</summary>
public sealed class TextResourceFormatException : FormatException
{
    /// <summary>Creates the exception.</summary>
    public TextResourceFormatException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TextResourceFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when an external pack is malformed, incompatible, or rejected.</summary>
public sealed class TextResourcePackException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TextResourcePackException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TextResourcePackException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Thrown when a transported text reference is incompatible with the receiver.</summary>
public sealed class TextResourceContractException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TextResourceContractException(string message) : base(message) { }
    /// <summary>Creates the exception with an inner cause.</summary>
    public TextResourceContractException(string message, Exception innerException) : base(message, innerException) { }
}
