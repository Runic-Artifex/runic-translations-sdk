using System;
using System.Collections.Generic;

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

public sealed class TranslationCompilation
{
    internal TranslationCompilation(IReadOnlyList<CompiledTextCatalog> catalogs, IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        Catalogs = catalogs;
        Diagnostics = diagnostics;
    }

    public IReadOnlyList<CompiledTextCatalog> Catalogs { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public bool Success
    {
        get
        {
            for (int i = 0; i < Diagnostics.Count; i++)
                if (Diagnostics[i].Severity == TranslationDiagnosticSeverity.Error) return false;
            return true;
        }
    }
}

public sealed class CompiledTextCatalog
{
    internal CompiledTextCatalog(string id, string codeNamespace, string className, TranslationVisibility visibility,
        string defaultLocale, IReadOnlyList<CompiledTextLayer> layers, IReadOnlyList<CompiledTextLocale> locales,
        IReadOnlyList<CompiledTranslation> canonicalResources, TranslationUnsupportedLocalePolicy unsupportedLocale,
        TranslationMissingKeyPolicy missingKey, string fingerprint, int schemaVersion = 1, int messageGrammarVersion = 1)
    {
        Id = id;
        CodeNamespace = codeNamespace;
        ClassName = className;
        Visibility = visibility;
        DefaultLocale = defaultLocale;
        Layers = layers;
        Locales = locales;
        CanonicalResources = canonicalResources;
        UnsupportedLocale = unsupportedLocale;
        MissingKey = missingKey;
        Fingerprint = fingerprint;
        SchemaVersion = schemaVersion;
        MessageGrammarVersion = messageGrammarVersion;
    }

    public int SchemaVersion { get; }
    public int MessageGrammarVersion { get; }
    public string Id { get; }
    public string CodeNamespace { get; }
    public string ClassName { get; }
    public TranslationVisibility Visibility { get; }
    public string DefaultLocale { get; }
    public IReadOnlyList<CompiledTextLayer> Layers { get; }
    public IReadOnlyList<CompiledTextLocale> Locales { get; }
    public IReadOnlyList<CompiledTranslation> CanonicalResources { get; }
    public TranslationUnsupportedLocalePolicy UnsupportedLocale { get; }
    public TranslationMissingKeyPolicy MissingKey { get; }
    public string Fingerprint { get; }
}

public sealed class CompiledTextLayer
{
    internal CompiledTextLayer(string name, int priority) { Name = name; Priority = priority; }
    public string Name { get; }
    public int Priority { get; }
}

public sealed class CompiledTextLocale
{
    internal CompiledTextLocale(string tag, string? fallbackTag, IReadOnlyList<CompiledTranslation> directResources,
        IReadOnlyList<CompiledTranslation> resolvedResources)
    {
        Tag = tag;
        FallbackTag = fallbackTag;
        DirectResources = directResources;
        ResolvedResources = resolvedResources;
    }

    public string Tag { get; }
    public string? FallbackTag { get; }
    public IReadOnlyList<CompiledTranslation> DirectResources { get; }
    public IReadOnlyList<CompiledTranslation> ResolvedResources { get; }
}

public sealed class CompiledTranslation
{
    internal CompiledTranslation(int id, string key, string pattern, string? description, string? since,
        string? deprecatedReason, IReadOnlyList<string> tags, IReadOnlyList<CompiledTextPlaceholder> placeholders,
        TextSourceLocation sourceLocation, CompiledMessagePattern message)
    {
        Id = id;
        Key = key;
        Pattern = pattern;
        Description = description;
        Since = since;
        DeprecatedReason = deprecatedReason;
        Tags = tags;
        Placeholders = placeholders;
        SourceLocation = sourceLocation;
        Message = message;
        ProducesStructuredContent = message.HasMarkup;
        IsTextInterchangeLossless = message.IsTextInterchangeLossless;
    }

    public int Id { get; }
    public string Key { get; }
    public string Pattern { get; }
    public string? Description { get; }
    public string? Since { get; }
    public string? DeprecatedReason { get; }
    public IReadOnlyList<string> Tags { get; }
    public IReadOnlyList<CompiledTextPlaceholder> Placeholders { get; }
    public TextSourceLocation SourceLocation { get; }
    public bool ProducesStructuredContent { get; }
    /// <summary>Whether the canonical pattern can be exchanged as plain XLIFF text without erasing execution structure.</summary>
    public bool IsTextInterchangeLossless { get; }
    internal CompiledMessagePattern Message { get; }
}

public sealed class CompiledTextPlaceholder
{
    internal CompiledTextPlaceholder(string name, TranslationArgumentType type, string format)
    {
        Name = name;
        Type = type;
        Format = format;
    }

    public string Name { get; }
    public TranslationArgumentType Type { get; }
    public string Format { get; }
}
