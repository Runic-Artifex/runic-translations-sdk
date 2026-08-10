using System;
using System.Collections.Generic;
using RunicTranslations.Compiler;
using RunicTranslations.Compiler.Generation;

namespace RunicTranslations.Tool;

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
        var outputs = new List<TextResourceGeneratedOutput>();
        for (int index = 0; index < orderedCatalogs.Count; index++)
        {
            CompiledTextCatalog catalog = orderedCatalogs[index];
            var catalogOutputs = new List<TextResourceGeneratedOutput>();
            if ((emission & ToolEmission.CSharp) != 0)
            {
                catalogOutputs.Add(TextResourceOutputRenderer.RenderCSharpKeys(catalog));
                catalogOutputs.Add(TextResourceOutputRenderer.RenderCSharpAccessors(catalog));
                catalogOutputs.Add(TextResourceOutputRenderer.RenderCSharpCatalogData(catalog));
                catalogOutputs.Add(TextResourceOutputRenderer.RenderCSharpRegistration(catalog));
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
                    catalogOutputs.Add(TextResourceOutputRenderer.RenderLocaleJson(catalog, locales[localeIndex]));
                }
            }

            if ((emission & ToolEmission.TemplateManifest) != 0)
            {
                catalogOutputs.Add(TextResourceOutputRenderer.RenderTemplateManifestJson(catalog));
            }

            if ((emission & ToolEmission.TypeScript) != 0)
            {
                catalogOutputs.Add(TextResourceOutputRenderer.RenderTypeScriptContract(catalog));
            }

            if ((emission & ToolEmission.Esm) != 0)
            {
                IReadOnlyList<TextResourceGeneratedOutput> esm = TextResourceOutputRenderer.RenderEsmModules(catalog);
                for (int outputIndex = 0; outputIndex < esm.Count; outputIndex++)
                    catalogOutputs.Add(esm[outputIndex]);
            }

            if ((emission & ToolEmission.Cpp) != 0)
            {
                IReadOnlyList<TextResourceGeneratedOutput> cpp = TextResourceOutputRenderer.RenderCpp(catalog);
                for (int outputIndex = 0; outputIndex < cpp.Count; outputIndex++)
                    catalogOutputs.Add(cpp[outputIndex]);
            }

            if ((emission & (ToolEmission.Json | ToolEmission.TemplateManifest | ToolEmission.TypeScript)) != 0)
                catalogOutputs.Add(TextResourceOutputRenderer.RenderAssetManifestJson(catalog, catalogOutputs));

            outputs.AddRange(catalogOutputs);
        }

        var artifacts = new List<ToolArtifact>(outputs.Count);
        for (int index = 0; index < outputs.Count; index++)
        {
            artifacts.Add(new ToolArtifact(outputs[index].RelativePath, outputs[index].GetUtf8Bytes()));
        }

        return ArtifactFiles.Normalize(artifacts);
    }
}
