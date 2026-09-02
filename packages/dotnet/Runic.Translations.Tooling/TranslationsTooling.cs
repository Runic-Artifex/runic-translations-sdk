using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tooling;

/// <summary>Preview facade for MF2 compiler and authoring operations.</summary>
public static class TranslationsTooling
{
    /// <summary>Compiles one conventional MF2 project without introducing a runtime dependency.</summary>
    public static TranslationCompilation CompileProject(
        TranslationSource project,
        IEnumerable<TranslationSource> messages,
        TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TranslationCompiler.CompileMf2Project(project, messages, options, cancellationToken);

    /// <summary>Builds the canonical bytes-first locale-pack-v2 artifacts for one successful MF2 project.</summary>
    public static LocalePackV2BuildResult BuildLocalePackV2(TranslationCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success) throw new LocalePackBuildException("LOCALEPACKV2-COMPILATION", "Locale-pack-v2 build requires a successful compiler result.");
        if (compilation.Catalogs.Count != 1) throw new LocalePackBuildException("LOCALEPACKV2-CATALOG", "Locale-pack-v2 build requires exactly one compiled catalog.");
        CompiledTextCatalog catalog = compilation.Catalogs[0];
        if (catalog.MessageGrammarVersion != TranslationOutputRenderer.LocaleArtifactV2Version)
            throw new LocalePackBuildException("LOCALEPACKV2-GRAMMAR", "Locale-pack-v2 build requires message grammar version 2.");

        var documents = new List<TranslationGeneratedOutput>(catalog.Locales.Count);
        foreach (CompiledTextLocale locale in catalog.Locales.OrderBy(static item => item.Tag, StringComparer.Ordinal))
            documents.Add(TranslationOutputRenderer.RenderLocaleJson(catalog, locale.Tag));
        return new LocalePackV2BuildResult(documents);
    }
}

/// <summary>Canonical locale-pack-v2 artifacts built from one compiled catalog.</summary>
public sealed class LocalePackV2BuildResult
{
    internal LocalePackV2BuildResult(IReadOnlyList<TranslationGeneratedOutput> documents) => Documents = documents;
    /// <summary>One deterministic locale-pack-v2 JSON artifact per declared locale.</summary>
    public IReadOnlyList<TranslationGeneratedOutput> Documents { get; }
}

/// <summary>Stable failure for locale-pack-v2 build preconditions.</summary>
public sealed class LocalePackBuildException : Exception
{
    internal LocalePackBuildException(string code, string message) : base(message) => Code = code;
    /// <summary>Stable machine-readable locale pack build rejection ID.</summary>
    public string Code { get; }
}
