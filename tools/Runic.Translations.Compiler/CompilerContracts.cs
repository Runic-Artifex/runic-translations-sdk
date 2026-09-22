using System;
namespace Runic.Translations.Compiler;

public enum TranslationDiagnosticSeverity
{
    Warning,
    Error,
}

public enum TranslationVisibility
{
    Public,
    Internal,
}

public enum TranslationPolicy
{
    Allow,
    Warning,
    Error,
}

public enum TranslationUnsupportedLocalePolicy
{
    Exact,
    ParentsThenDefault,
    Default,
}

public enum TranslationMissingKeyPolicy
{
    Throw,
    ReturnKey,
    ReturnMarker,
}

public enum TranslationArgumentType
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

    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();

    internal byte[] Bytes => _utf8Bytes;

    private static string NormalizePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
        return normalized.Length == 0 ? "." : normalized;
    }
}

public sealed class TranslationCompilerOptions
{
    public TranslationCompilerOptions(
        int maximumDocumentBytes = 8 * 1024 * 1024,
        int maximumDepth = 64,
        int maximumKeysPerCatalog = 50_000,
        int maximumValueBytes = 64 * 1024,
        int maximumPlaceholdersPerValue = 32,
        int maximumLocalesPerCatalog = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDocumentBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumKeysPerCatalog);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumValueBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPlaceholdersPerValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLocalesPerCatalog);
        MaximumDocumentBytes = maximumDocumentBytes;
        MaximumDepth = maximumDepth;
        MaximumKeysPerCatalog = maximumKeysPerCatalog;
        MaximumValueBytes = maximumValueBytes;
        MaximumPlaceholdersPerValue = maximumPlaceholdersPerValue;
        MaximumLocalesPerCatalog = maximumLocalesPerCatalog;
    }

    public int MaximumDocumentBytes { get; }
    public int MaximumDepth { get; }
    public int MaximumKeysPerCatalog { get; }
    public int MaximumValueBytes { get; }
    public int MaximumPlaceholdersPerValue { get; }
    public int MaximumLocalesPerCatalog { get; }
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
