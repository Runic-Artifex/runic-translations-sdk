using System;
using System.Collections.Generic;
using RunicTranslations.Compiler;

namespace RunicTranslations.Authoring;

public sealed record TextResourceProjectLocale(string Tag, string? Fallback = null);

public sealed class TextResourceProjectCreationRequest
{
    public TextResourceProjectCreationRequest(
        string directory,
        string catalogId,
        string defaultLocale,
        string codeNamespace,
        string className,
        IEnumerable<TextResourceProjectLocale>? additionalLocales = null,
        string layerName = "base",
        bool generateEsm = true,
        bool includeStarterMessage = true)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(catalogId);
        ArgumentNullException.ThrowIfNull(defaultLocale);
        ArgumentNullException.ThrowIfNull(codeNamespace);
        ArgumentNullException.ThrowIfNull(className);
        Directory = directory;
        CatalogId = catalogId;
        DefaultLocale = defaultLocale;
        CodeNamespace = codeNamespace;
        ClassName = className;
        AdditionalLocales = additionalLocales is null
            ? Array.Empty<TextResourceProjectLocale>()
            : new List<TextResourceProjectLocale>(additionalLocales).ToArray();
        LayerName = layerName;
        GenerateEsm = generateEsm;
        IncludeStarterMessage = includeStarterMessage;
    }

    public string Directory { get; }
    public string CatalogId { get; }
    public string DefaultLocale { get; }
    public string CodeNamespace { get; }
    public string ClassName { get; }
    public IReadOnlyList<TextResourceProjectLocale> AdditionalLocales { get; }
    public string LayerName { get; }
    public bool GenerateEsm { get; }
    public bool IncludeStarterMessage { get; }
}

public sealed class TextResourceProjectFile
{
    private readonly byte[] _utf8Bytes;

    internal TextResourceProjectFile(string relativePath, byte[] utf8Bytes)
    {
        RelativePath = relativePath;
        _utf8Bytes = utf8Bytes;
    }

    public string RelativePath { get; }
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();
    internal byte[] Bytes => _utf8Bytes;
}

public sealed class TextResourceProjectPlan
{
    internal TextResourceProjectPlan(
        TextResourceProjectCreationRequest request,
        IReadOnlyList<TextResourceProjectLocale> locales,
        IReadOnlyList<TextResourceProjectFile> files,
        TextResourceCompilation compilation)
    {
        Request = request;
        Locales = locales;
        Files = files;
        Compilation = compilation;
    }

    public TextResourceProjectCreationRequest Request { get; }
    public IReadOnlyList<TextResourceProjectLocale> Locales { get; }
    public IReadOnlyList<TextResourceProjectFile> Files { get; }
    public TextResourceCompilation Compilation { get; }
}

public sealed class TextResourceAuthoringException : Exception
{
    public TextResourceAuthoringException(string message)
        : base(message)
    {
    }

    internal TextResourceAuthoringException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
