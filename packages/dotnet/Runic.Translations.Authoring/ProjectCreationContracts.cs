using System;
using System.Collections.Generic;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

public sealed record TranslationProjectLocale(string Tag, string? Fallback = null);

public sealed class TranslationProjectCreationRequest
{
    public TranslationProjectCreationRequest(
        string directory,
        string catalogId,
        string defaultLocale,
        string codeNamespace,
        string className,
        IEnumerable<TranslationProjectLocale>? additionalLocales = null,
        string layerName = "base",
        bool generateEsm = true,
        bool includeStarterMessage = true,
        bool includeVsCodeSettings = false)
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
            ? Array.Empty<TranslationProjectLocale>()
            : new List<TranslationProjectLocale>(additionalLocales).ToArray();
        LayerName = layerName;
        GenerateEsm = generateEsm;
        IncludeStarterMessage = includeStarterMessage;
        IncludeVsCodeSettings = includeVsCodeSettings;
    }

    public string Directory { get; }
    public string CatalogId { get; }
    public string DefaultLocale { get; }
    public string CodeNamespace { get; }
    public string ClassName { get; }
    public IReadOnlyList<TranslationProjectLocale> AdditionalLocales { get; }
    public string LayerName { get; }
    public bool GenerateEsm { get; }
    public bool IncludeStarterMessage { get; }
    public bool IncludeVsCodeSettings { get; }
}

public sealed class TranslationProjectFile
{
    private readonly byte[] _utf8Bytes;

    internal TranslationProjectFile(string relativePath, byte[] utf8Bytes)
    {
        RelativePath = relativePath;
        _utf8Bytes = utf8Bytes;
    }

    public string RelativePath { get; }
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();
    internal byte[] Bytes => _utf8Bytes;
}

public sealed class TranslationProjectPlan
{
    internal TranslationProjectPlan(
        TranslationProjectCreationRequest request,
        IReadOnlyList<TranslationProjectLocale> locales,
        IReadOnlyList<TranslationProjectFile> files,
        TranslationCompilation compilation)
    {
        Request = request;
        Locales = locales;
        Files = files;
        Compilation = compilation;
    }

    public TranslationProjectCreationRequest Request { get; }
    public IReadOnlyList<TranslationProjectLocale> Locales { get; }
    public IReadOnlyList<TranslationProjectFile> Files { get; }
    public TranslationCompilation Compilation { get; }
}

public sealed class TranslationAuthoringException : Exception
{
    public TranslationAuthoringException(string message)
        : base(message)
    {
    }

    internal TranslationAuthoringException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
