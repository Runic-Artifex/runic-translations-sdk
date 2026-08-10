using System;
using System.Collections.Generic;
using RunicTranslations.Compiler;

namespace RunicTranslations.Authoring;

public enum TranslationWorkspaceFileKind
{
    CatalogManifest,
    ResourceDocument,
    MalformedJson,
    OtherJson,
}

public enum TranslationAuthoringDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed class TranslationWorkspaceDiscoveryOptions
{
    public TranslationWorkspaceDiscoveryOptions(
        int maximumDepth = 16,
        int maximumEntries = 8_192,
        int maximumJsonFiles = 512,
        int maximumFileBytes = 8 * 1024 * 1024,
        long maximumTotalBytes = 64L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumJsonFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumFileBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumTotalBytes);
        MaximumDepth = maximumDepth;
        MaximumEntries = maximumEntries;
        MaximumJsonFiles = maximumJsonFiles;
        MaximumFileBytes = maximumFileBytes;
        MaximumTotalBytes = maximumTotalBytes;
    }

    public int MaximumDepth { get; }
    public int MaximumEntries { get; }
    public int MaximumJsonFiles { get; }
    public int MaximumFileBytes { get; }
    public long MaximumTotalBytes { get; }
}

public sealed class TranslationWorkspaceFile
{
    private readonly byte[] _utf8Bytes;

    internal TranslationWorkspaceFile(
        string relativePath,
        TranslationWorkspaceFileKind kind,
        string? catalogId,
        string? locale,
        string? layer,
        byte[] utf8Bytes)
    {
        RelativePath = relativePath;
        Kind = kind;
        CatalogId = catalogId;
        Locale = locale;
        Layer = layer;
        _utf8Bytes = utf8Bytes;
    }

    public string RelativePath { get; }
    public TranslationWorkspaceFileKind Kind { get; }
    public string? CatalogId { get; }
    public string? Locale { get; }
    public string? Layer { get; }
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();
    internal byte[] Bytes => _utf8Bytes;
}

public sealed record TranslationAuthoringDiagnostic(
    string Id,
    TranslationAuthoringDiagnosticSeverity Severity,
    string Message,
    string RelativePath);

public sealed class TranslationDiscoveredCatalog
{
    internal TranslationDiscoveredCatalog(
        string id,
        IReadOnlyList<string> manifestPaths,
        IReadOnlyList<string> documentPaths,
        TranslationCompilation compilation)
    {
        Id = id;
        ManifestPaths = manifestPaths;
        DocumentPaths = documentPaths;
        Compilation = compilation;
    }

    public string Id { get; }
    public IReadOnlyList<string> ManifestPaths { get; }
    public IReadOnlyList<string> DocumentPaths { get; }
    public TranslationCompilation Compilation { get; }
}

public sealed class TranslationWorkspaceDiscoveryResult
{
    internal TranslationWorkspaceDiscoveryResult(
        string root,
        IReadOnlyList<TranslationWorkspaceFile> files,
        IReadOnlyList<TranslationDiscoveredCatalog> catalogs,
        IReadOnlyList<TranslationAuthoringDiagnostic> diagnostics)
    {
        Root = root;
        Files = files;
        Catalogs = catalogs;
        Diagnostics = diagnostics;
    }

    public string Root { get; }
    public IReadOnlyList<TranslationWorkspaceFile> Files { get; }
    public IReadOnlyList<TranslationDiscoveredCatalog> Catalogs { get; }
    public IReadOnlyList<TranslationAuthoringDiagnostic> Diagnostics { get; }
}
