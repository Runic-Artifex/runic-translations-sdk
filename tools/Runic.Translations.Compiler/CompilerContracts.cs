using System;
namespace Runic.Translations.Compiler;

/// <summary>The severity of a <see cref="TranslationDiagnostic"/>. The set is open: a later release may add a level.</summary>
public enum TranslationDiagnosticSeverity
{
    /// <summary>Information only; never fails a build.</summary>
    Information = 0,
    /// <summary>A problem that does not stop compilation.</summary>
    Warning = 1,
    /// <summary>A problem that stops compilation.</summary>
    Error = 2,
}

internal enum TranslationVisibility
{
    Public,
    Internal,
}

internal enum TranslationPolicy
{
    Allow,
    Warning,
    Error,
}

internal enum TranslationUnsupportedLocalePolicy
{
    Exact,
    ParentsThenDefault,
    Default,
}

internal enum TranslationMissingKeyPolicy
{
    Throw,
    ReturnKey,
    ReturnMarker,
}

internal enum TranslationArgumentType
{
    String,
    Int,
    Number,
    Boolean,
    Date,
    Time,
    DateTime,
    Guid,
}

public sealed class TranslationSource
{
    private readonly byte[] _utf8Bytes;

    public TranslationSource(string path, byte[] utf8Bytes)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(utf8Bytes);
        Path = NormalizePath(path);
        _utf8Bytes = (byte[])utf8Bytes.Clone();
    }

    public string Path { get; }

    /// <summary>Returns a copy of the UTF-8 source, a new array on every call.</summary>
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();

    internal byte[] Bytes => _utf8Bytes;

    private static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
        return normalized.Length == 0 ? "." : normalized;
    }
}

/// <summary>Compiler limits. Every limit must be positive; a later release may add limits with their own defaults.</summary>
public sealed class TranslationCompilerOptions
{
    /// <summary>The largest accepted source document, in bytes. Defaults to 8 MiB.</summary>
    public int MaximumDocumentBytes { get; init => field = Positive(value); } = 8 * 1024 * 1024;
    /// <summary>The deepest accepted JSON or message nesting. Defaults to 64.</summary>
    public int MaximumDepth { get; init => field = Positive(value); } = 64;
    /// <summary>The most message keys in one catalog. Defaults to 50,000.</summary>
    public int MaximumKeysPerCatalog { get; init => field = Positive(value); } = 50_000;
    /// <summary>The largest accepted message value, in bytes. Defaults to 64 KiB.</summary>
    public int MaximumValueBytes { get; init => field = Positive(value); } = 64 * 1024;
    /// <summary>The most placeholders in one message value. Defaults to 32.</summary>
    public int MaximumPlaceholdersPerValue { get; init => field = Positive(value); } = 32;
    /// <summary>The most locales in one catalog. Defaults to 256.</summary>
    public int MaximumLocalesPerCatalog { get; init => field = Positive(value); } = 256;

    private static int Positive(int value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, name);
        return value;
    }
}

public sealed class TextSourceLocation
{
    public TextSourceLocation(string path, int startByte, int lengthBytes, int line, int column, int endLine, int endColumn)
    {
        Path = path;
        StartByte = startByte;
        LengthBytes = lengthBytes;
        Line = line;
        Column = column;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public string Path { get; }
    public int StartByte { get; }
    public int LengthBytes { get; }
    public int Line { get; }
    public int Column { get; }
    public int EndLine { get; }
    public int EndColumn { get; }

    public override string ToString() => Path + "(" + Line + "," + Column + ")";
}

public sealed class TranslationDiagnostic
{
    public TranslationDiagnostic(string id, TranslationDiagnosticSeverity severity, string message, TextSourceLocation location)
    {
        Id = id;
        Severity = severity;
        Message = message;
        Location = location;
    }

    public string Id { get; }
    public TranslationDiagnosticSeverity Severity { get; }
    public string Message { get; }
    public TextSourceLocation Location { get; }
}
