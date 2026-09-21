using System;
using System.Collections.Generic;
using System.Linq;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

internal static class CompilerOutputAdapter
{
    internal static IReadOnlyList<ToolArtifact> Render(
        IReadOnlyList<CompiledTextCatalog> catalogs,
        ToolEmission emission)
    {
        ArgumentNullException.ThrowIfNull(catalogs);
        var orderedCatalogs = new List<CompiledTextCatalog>(catalogs.Count);
        for (int index = 0; index < catalogs.Count; index++)
        {
            orderedCatalogs.Add(catalogs[index]);
        }

        orderedCatalogs.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        var outputs = new List<TranslationGeneratedOutput>();
        for (int index = 0; index < orderedCatalogs.Count; index++)
        {
            CompiledTextCatalog catalog = orderedCatalogs[index];
            var catalogOutputs = new List<TranslationGeneratedOutput>();
            if ((emission & ToolEmission.CSharp) != 0)
            {
                catalogOutputs.Add(TranslationOutputRenderer.RenderCSharpKeys(catalog));
                catalogOutputs.Add(TranslationOutputRenderer.RenderCSharpAccessors(catalog));
                catalogOutputs.Add(TranslationOutputRenderer.RenderCSharpCatalogData(catalog));
                catalogOutputs.Add(TranslationOutputRenderer.RenderCSharpRegistration(catalog));
            }

            if ((emission & ToolEmission.Json) != 0)
            {
                var locales = new List<string>(catalog.Locales.Count);
                for (int localeIndex = 0; localeIndex < catalog.Locales.Count; localeIndex++)
                {
                    locales.Add(catalog.Locales[localeIndex].Tag);
                }

                locales.Sort(StringComparer.Ordinal);
                for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
                {
                    catalogOutputs.Add(TranslationOutputRenderer.RenderLocaleJson(catalog, locales[localeIndex]));
                }
            }

            if ((emission & ToolEmission.TemplateManifest) != 0)
            {
                catalogOutputs.Add(TranslationOutputRenderer.RenderTemplateManifestJson(catalog));
            }

            if ((emission & ToolEmission.TypeScript) != 0)
            {
                catalogOutputs.Add(TranslationOutputRenderer.RenderTypeScriptContract(catalog));
            }

            if ((emission & ToolEmission.Esm) != 0)
            {
                IReadOnlyList<TranslationGeneratedOutput> esm = TranslationOutputRenderer.RenderEsmModules(catalog);
                for (int outputIndex = 0; outputIndex < esm.Count; outputIndex++)
                    catalogOutputs.Add(esm[outputIndex]);
            }

            if ((emission & ToolEmission.Cpp) != 0)
            {
                IReadOnlyList<TranslationGeneratedOutput> cpp = TranslationOutputRenderer.RenderCpp(catalog);
                for (int outputIndex = 0; outputIndex < cpp.Count; outputIndex++)
                    catalogOutputs.Add(cpp[outputIndex]);
            }

            if ((emission & (ToolEmission.Json | ToolEmission.TemplateManifest | ToolEmission.TypeScript)) != 0)
                catalogOutputs.Add(TranslationOutputRenderer.RenderAssetManifestJson(catalog, catalogOutputs));

            outputs.AddRange(catalogOutputs);
        }

        var artifacts = new List<ToolArtifact>(outputs.Count);
        for (int index = 0; index < outputs.Count; index++)
        {
            artifacts.Add(new ToolArtifact(outputs[index].RelativePath, outputs[index].GetUtf8Bytes()));
        }

        return ArtifactFiles.Normalize(artifacts);
    }

    internal static IReadOnlyList<ToolArtifact> Render(Rmf2ProjectV5 project, ToolEmission emission)
    {
        ArgumentNullException.ThrowIfNull(project);
        if ((emission & ToolEmission.Cpp) != 0)
            throw new ToolDiagnosticException("RTR0065: C++ generation does not support executionProfile 'rmf2-execution-v2'.");

        var outputs = new List<TranslationGeneratedOutput>();
        if ((emission & ToolEmission.CSharp) != 0)
        {
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpKeys(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(project));
        }
        if ((emission & ToolEmission.Json) != 0)
            foreach (string locale in project.Locales.Select(static item => item.Tag).OrderBy(static item => item, StringComparer.Ordinal))
                outputs.Add(Rmf2LocaleArtifactV5.Render(project, locale));
        // The v5 ESM contract owns its declarations and versioned web manifest.
        // Preserve the established switches by treating either edge-only request
        // as selecting that cohesive output group.
        if ((emission & (ToolEmission.Esm | ToolEmission.TypeScript | ToolEmission.TemplateManifest)) != 0)
            outputs.AddRange(TranslationOutputRenderer.RenderRmf2V5EsmModules(project));

        return ArtifactFiles.Normalize(outputs
            .Select(static output => new ToolArtifact(output.RelativePath, output.GetUtf8Bytes()))
            .ToArray());
    }
}
