using System;
using System.Collections.Generic;

namespace Runic.Translations.Compiler.Generation;

/// <summary>Pure deterministic renderers over the selected RMF2 compilation carrier.</summary>
internal static class TranslationOutputRenderer
{
    internal const int AssetManifestVersion = 1;
    internal const int EsmAbiVersion = 4;
    internal const int WebModuleManifestVersion = 3;

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpKeys(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderKeys(RequireProject(project));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpAccessors(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderAccessors(RequireProject(project));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpCatalogData(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderCatalogData(RequireProject(project));

    internal static TranslationGeneratedOutput RenderRmf2V5CSharpRegistration(Rmf2ProjectV5 project) =>
        Rmf2CSharpOutputRendererV5.RenderRegistration(RequireProject(project));

    internal static IReadOnlyList<TranslationGeneratedOutput> RenderRmf2V5EsmModules(Rmf2ProjectV5 project) =>
        Rmf2EsmOutputRendererV5.Render(RequireProject(project));

    internal static TranslationGeneratedOutput RenderRmf2V5AssetManifestJson(
        Rmf2ProjectV5 project,
        IEnumerable<TranslationGeneratedOutput> selectedOutputs) =>
        EdgeOutputRenderer.RenderRmf2V5AssetManifest(
            RequireProject(project),
            selectedOutputs ?? throw new ArgumentNullException(nameof(selectedOutputs)));

    private static Rmf2ProjectV5 RequireProject(Rmf2ProjectV5 project) =>
        project ?? throw new ArgumentNullException(nameof(project));
}
