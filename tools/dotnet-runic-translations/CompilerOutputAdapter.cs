using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

internal static class CompilerOutputAdapter
{
    // outputPath: the output directory, so the ESM output's messages.d.ts.map can name the sources relative to itself.
    internal static IReadOnlyList<ToolArtifact> Render(Rmf2ProjectV5 project, ToolEmission emission, bool sharedRuntime = false, string? outputPath = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (emission == ToolEmission.None) emission = ToolEmission.CSharp | ToolEmission.Json | ToolEmission.Esm;
        var outputs = new List<TranslationGeneratedOutput>();
        if ((emission & ToolEmission.CSharp) != 0)
        {
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpKeys(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(project));
            // No reference set here, so the readable file is always written; an old runtime fails with CS0246.
            if (Rmf2ReadableNamesV1.SupportsClassName(project.ClassName))
                outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpReadable(project));
        }
        if ((emission & ToolEmission.Json) != 0)
        {
            foreach (string locale in project.Locales.Select(static item => item.Tag).OrderBy(static item => item, StringComparer.Ordinal))
                outputs.Add(Rmf2LocaleArtifactV5.Render(project, locale));
            outputs.Add(TranslationOutputRenderer.RenderRmf2V5AssetManifestJson(project, outputs));
        }
        if ((emission & ToolEmission.Esm) != 0)
            outputs.AddRange(TranslationOutputRenderer.RenderRmf2V5EsmModules(project, sharedRuntime,
                outputPath is null ? null : DeclarationMapSource(Path.Combine(outputPath, project.Id + ".esm-v5"))));

        return ArtifactFiles.Normalize(outputs
            .Select(static output => new ToolArtifact(output.RelativePath, output.GetUtf8Bytes()))
            .ToArray());
    }

    // A source location path (relative to the current directory, like every input path) as a path relative to the map's
    // directory with forward slashes; null for a source on another drive, which has no relative path.
    private static Func<string, string?> DeclarationMapSource(string mapDirectory)
    {
        string current = Path.GetFullPath(Environment.CurrentDirectory), directory = Path.GetFullPath(mapDirectory, current);
        return path =>
        {
            string relative = Path.GetRelativePath(directory, Path.GetFullPath(path, current));
            return Path.IsPathRooted(relative) ? null : relative.Replace('\\', '/');
        };
    }

    // generate --emit-json --contract: packs for a shipped release, for the locales it declares, and their asset manifest.
    // omittedLocales: current locales the shipped release lacks; droppedLocales: shipped locales the current sources lack.
    internal static IReadOnlyList<ToolArtifact> RenderForContract(Rmf2ProjectV5 project, Rmf2ShippedContractV5 shipped,
        ICollection<Rmf2ContractPackSkipV5> skipped, out IReadOnlyList<string> omittedLocales, out IReadOnlyList<string> droppedLocales)
    {
        droppedLocales = shipped.Locales.Where(locale => !project.Locales.Any(item => item.Tag == locale)).Order(StringComparer.Ordinal).ToArray();
        var outputs = new List<TranslationGeneratedOutput>();
        var omitted = new List<string>();
        foreach (string locale in project.Locales.Select(static item => item.Tag).OrderBy(static item => item, StringComparer.Ordinal))
        {
            if (Rmf2ContractPackV5.Render(project, locale, shipped, skipped) is { } pack) outputs.Add(pack);
            else omitted.Add(locale);
        }
        outputs.Add(TranslationOutputRenderer.RenderRmf2V5AssetManifestJson(project, outputs));
        omittedLocales = omitted;
        return ArtifactFiles.Normalize(outputs
            .Select(static output => new ToolArtifact(output.RelativePath, output.GetUtf8Bytes()))
            .ToArray());
    }
}
