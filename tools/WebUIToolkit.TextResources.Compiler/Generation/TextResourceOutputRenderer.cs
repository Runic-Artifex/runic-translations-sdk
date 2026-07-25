using System;
using System.Collections.Generic;

namespace WebUIToolkit.TextResources.Compiler.Generation;

/// <summary>Pure deterministic renderers over the canonical compiled text-resource IR.</summary>
public static class TextResourceOutputRenderer
{
    /// <summary>The writer version of locale JSON artifacts and external packs.</summary>
    public const int LocaleArtifactVersion = 1;

    /// <summary>The writer version of the template-manifest edge contract.</summary>
    public const int TemplateManifestVersion = 1;

    /// <summary>The writer version of the TypeScript key/argument edge contract.</summary>
    public const int TypeScriptContractVersion = 1;

    /// <summary>The writer version of the host asset inventory edge contract.</summary>
    public const int AssetManifestVersion = 1;

    /// <summary>Renders the strongly typed key hierarchy.</summary>
    public static TextResourceGeneratedOutput RenderCSharpKeys(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderKeys(RequireCatalog(catalog));

    /// <summary>Renders strongly typed accessors that read the manager's current snapshot on every call.</summary>
    public static TextResourceGeneratedOutput RenderCSharpAccessors(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderAccessors(RequireCatalog(catalog));

    /// <summary>Renders reflection-free arrays and descriptors consumed by the generated provider.</summary>
    public static TextResourceGeneratedOutput RenderCSharpCatalogData(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderCatalogData(RequireCatalog(catalog));

    /// <summary>Renders the application-facing, reflection-free provider and manager factory.</summary>
    public static TextResourceGeneratedOutput RenderCSharpRegistration(CompiledTextCatalog catalog) =>
        CSharpOutputRenderer.RenderRegistration(RequireCatalog(catalog));

    /// <summary>Renders one declared locale as canonical compact JSON using resolved fallback values.</summary>
    public static TextResourceGeneratedOutput RenderLocaleJson(CompiledTextCatalog catalog, string locale)
    {
        if (locale is null) throw new ArgumentNullException(nameof(locale));
        return EdgeOutputRenderer.RenderLocale(RequireCatalog(catalog), locale);
    }

    /// <summary>Renders the value-free, versioned template compiler edge manifest.</summary>
    public static TextResourceGeneratedOutput RenderTemplateManifestJson(CompiledTextCatalog catalog) =>
        EdgeOutputRenderer.RenderTemplateManifest(RequireCatalog(catalog));

    /// <summary>Renders the versioned TypeScript key and argument contract without a runtime implementation.</summary>
    public static TextResourceGeneratedOutput RenderTypeScriptContract(CompiledTextCatalog catalog) =>
        EdgeOutputRenderer.RenderTypeScriptContract(RequireCatalog(catalog));

    /// <summary>Renders the versioned host inventory for selected non-C# outputs of one catalog.</summary>
    public static TextResourceGeneratedOutput RenderAssetManifestJson(
        CompiledTextCatalog catalog,
        IEnumerable<TextResourceGeneratedOutput> selectedOutputs) =>
        EdgeOutputRenderer.RenderAssetManifest(RequireCatalog(catalog), selectedOutputs);

    private static CompiledTextCatalog RequireCatalog(CompiledTextCatalog catalog) =>
        catalog ?? throw new ArgumentNullException(nameof(catalog));
}
