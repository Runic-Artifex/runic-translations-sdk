using System;
using System.Collections.Generic;

namespace Runic.Translations.Compiler.Generation;

/// <summary>Pure deterministic renderers over the canonical compiled translation IR.</summary>
public static class TranslationOutputRenderer
{
    /// <summary>The writer version of locale JSON artifacts and external packs.</summary>
    public const int LocaleArtifactVersion = 1;
    /// <summary>The locale artifact version carrying normalized message AST v2.</summary>
    public const int LocaleArtifactV2Version = 2;
    /// <summary>The RMF2 locale artifact version carrying inline contracts and effective locales.</summary>
    public const int LocaleArtifactV4Version = 4;

    /// <summary>The writer version of the template-manifest edge contract.</summary>
    public const int TemplateManifestVersion = 1;
    /// <summary>The template-manifest edge contract version for schema-v2 and RMF2 catalogs.</summary>
    public const int TemplateManifestV2Version = 2;

    /// <summary>The writer version of the TypeScript key/argument edge contract.</summary>
    public const int TypeScriptContractVersion = 1;

    /// <summary>The writer version of the host asset inventory edge contract.</summary>
    public const int AssetManifestVersion = 1;

    /// <summary>The generated ESM API and runtime compatibility version.</summary>
    public const int EsmAbiVersion = 3;
    /// <summary>The writer version of the generated ESM module manifest.</summary>
    public const int WebModuleManifestV2Version = 2;

    /// <summary>The experimental generated C++ ABI compatibility version.</summary>
    public const int CppAbiVersion = 1;

    /// <summary>Renders the strongly typed key hierarchy.</summary>
    public static TranslationGeneratedOutput RenderCSharpKeys(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderKeys(RequireCatalog(catalog));

    /// <summary>Renders strongly typed accessors that read the manager's current snapshot on every call.</summary>
    public static TranslationGeneratedOutput RenderCSharpAccessors(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderAccessors(RequireCatalog(catalog));

    /// <summary>Renders reflection-free arrays and descriptors consumed by the generated provider.</summary>
    public static TranslationGeneratedOutput RenderCSharpCatalogData(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderCatalogData(RequireCatalog(catalog));

    /// <summary>Renders the application-facing, reflection-free provider and manager factory.</summary>
    public static TranslationGeneratedOutput RenderCSharpRegistration(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderRegistration(RequireCatalog(catalog));

    // The execution-v2 carrier is intentionally internal until every generated
    // backend can be activated together. The source generator's explicit
    // staged profile is the only production assembly allowed to consume it.
    internal static TranslationGeneratedOutput RenderRmf2V5CSharpKeys(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderKeys(project ?? throw new ArgumentNullException(nameof(project)));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpAccessors(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderAccessors(project ?? throw new ArgumentNullException(nameof(project)));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpCatalogData(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderCatalogData(project ?? throw new ArgumentNullException(nameof(project)));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpRegistration(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderRegistration(project ?? throw new ArgumentNullException(nameof(project)));

    /// <summary>Renders one declared locale as canonical compact JSON using resolved fallback values.</summary>
    public static TranslationGeneratedOutput RenderLocaleJson(CompiledTextCatalog catalog, string locale)
    {
        ArgumentNullException.ThrowIfNull(locale);
        return EdgeOutputRenderer.RenderLocale(RequireCatalog(catalog), locale);
    }

    /// <summary>Renders the value-free, versioned template compiler edge manifest.</summary>
    public static TranslationGeneratedOutput RenderTemplateManifestJson(CompiledTextCatalog catalog) =>
        EdgeOutputRenderer.RenderTemplateManifest(RequireCatalog(catalog));

    /// <summary>Renders the versioned TypeScript key and argument contract without a runtime implementation.</summary>
    public static TranslationGeneratedOutput RenderTypeScriptContract(CompiledTextCatalog catalog) =>
        EdgeOutputRenderer.RenderTypeScriptContract(RequireCatalog(catalog));

    /// <summary>Renders the versioned host inventory for selected non-C# outputs of one catalog.</summary>
    public static TranslationGeneratedOutput RenderAssetManifestJson(
        CompiledTextCatalog catalog,
        IEnumerable<TranslationGeneratedOutput> selectedOutputs) =>
        EdgeOutputRenderer.RenderAssetManifest(RequireCatalog(catalog), selectedOutputs);

    /// <summary>Renders deterministic, independently tree-shakable ESM message modules and their manifest.</summary>
    public static IReadOnlyList<TranslationGeneratedOutput> RenderEsmModules(CompiledTextCatalog catalog) =>
        EsmOutputRenderer.Render(RequireCatalog(catalog));

    /// <summary>Renders an experimental dependency-free C++20 header/source pair.</summary>
    public static IReadOnlyList<TranslationGeneratedOutput> RenderCpp(CompiledTextCatalog catalog) =>
        CppOutputRenderer.Render(RequireCatalog(catalog));

    private static CompiledTextCatalog RequireCatalog(CompiledTextCatalog catalog) =>
        catalog ?? throw new ArgumentNullException(nameof(catalog));
}
