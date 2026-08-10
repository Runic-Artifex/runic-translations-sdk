using System;
using System.Collections.Generic;
using RunicTranslations.Compiler;

namespace RunicTranslations.Authoring;

public enum TextResourceWorkspaceFileKind
{
    CatalogManifest,
    ResourceDocument,
    MalformedJson,
    OtherJson,
}

public enum TextResourceAuthoringDiagnosticSeverity
{
    Warning,
    Error,
}

public sealed class TextResourceWorkspaceDiscoveryOptions
{
    public TextResourceWorkspaceDiscoveryOptions(
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

public sealed class TextResourceWorkspaceFile
{
    private readonly byte[] _utf8Bytes;

    internal TextResourceWorkspaceFile(
        string relativePath,
        TextResourceWorkspaceFileKind kind,
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
    public TextResourceWorkspaceFileKind Kind { get; }
    public string? CatalogId { get; }
    public string? Locale { get; }
    public string? Layer { get; }
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();
    internal byte[] Bytes => _utf8Bytes;
}

public sealed record TextResourceAuthoringDiagnostic(
    string Id,
    TextResourceAuthoringDiagnosticSeverity Severity,
    string Message,
    string RelativePath);

public sealed class TextResourceDiscoveredCatalog
{
    internal TextResourceDiscoveredCatalog(
        string id,
        IReadOnlyList<string> manifestPaths,
        IReadOnlyList<string> documentPaths,
        TextResourceCompilation compilation)
    {
        Id = id;
        ManifestPaths = manifestPaths;
        DocumentPaths = documentPaths;
        Compilation = compilation;
    }

    public string Id { get; }
    public IReadOnlyList<string> ManifestPaths { get; }
    public IReadOnlyList<string> DocumentPaths { get; }
    public TextResourceCompilation Compilation { get; }
}

public sealed class TextResourceWorkspaceDiscoveryResult
{
    internal TextResourceWorkspaceDiscoveryResult(
        string root,
        IReadOnlyList<TextResourceWorkspaceFile> files,
        IReadOnlyList<TextResourceDiscoveredCatalog> catalogs,
        IReadOnlyList<TextResourceAuthoringDiagnostic> diagnostics)
    {
        Root = root;
        Files = files;
        Catalogs = catalogs;
        Diagnostics = diagnostics;
    }

    public string Root { get; }
    public IReadOnlyList<TextResourceWorkspaceFile> Files { get; }
    public IReadOnlyList<TextResourceDiscoveredCatalog> Catalogs { get; }
    public IReadOnlyList<TextResourceAuthoringDiagnostic> Diagnostics { get; }
}
