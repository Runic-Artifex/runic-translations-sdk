using System;
using System.Collections.Generic;
using System.Linq;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

internal static class CompilerOutputAdapter
{
    internal static IReadOnlyList<ToolArtifact> Render(Rmf2ProjectV5 project, ToolEmission emission)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (emission == ToolEmission.None) emission = ToolEmission.CSharp | ToolEmission.Json | ToolEmission.Esm;
        ToolEmission unsupported = emission & (ToolEmission.TypeScript | ToolEmission.TemplateManifest | ToolEmission.Cpp);
        if (unsupported != ToolEmission.None)
            throw new ToolDiagnosticException("RTR0065: the semantic translation contract does not support " +
                UnsupportedSwitch(unsupported) + ". Use --emit-csharp, --emit-json, or --emit-esm.");

        var outputs = new List<TranslationGeneratedOutput>();
        if ((emission & ToolEmission.CSharp) != 0)
        {
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpKeys(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(project));
        }
        if ((emission & ToolEmission.Json) != 0)
        {
            foreach (string locale in project.Locales.Select(static item => item.Tag).OrderBy(static item => item, StringComparer.Ordinal))
                outputs.Add(Rmf2LocaleArtifactV5.Render(project, locale));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5AssetManifestJson(project, outputs));
        }
        if ((emission & ToolEmission.Esm) != 0)
            outputs.AddRange(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));

        return ArtifactFiles.Normalize(outputs
            .Select(static output => new ToolArtifact(output.RelativePath, output.GetUtf8Bytes()))
            .ToArray());
    }

    private static string UnsupportedSwitch(ToolEmission emission)
    {
        if ((emission & ToolEmission.TypeScript) != 0) return "--emit-typescript";
        if ((emission & ToolEmission.TemplateManifest) != 0) return "--emit-template-manifest";
        return "--emit-cpp";
    }
}
