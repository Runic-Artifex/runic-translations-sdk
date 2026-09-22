using System;
using System.Collections.Generic;
using System.Text;

namespace Runic.Translations.Compiler.Generation;

internal static class EdgeOutputRenderer
{
    internal static TranslationGeneratedOutput RenderRmf2V5AssetManifest(
        Rmf2ProjectV5 project,
        IEnumerable<TranslationGeneratedOutput> selectedOutputs)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(selectedOutputs);
        var assets = new List<TranslationGeneratedOutput>();
        foreach (TranslationGeneratedOutput output in selectedOutputs)
        {
            if (output is null)
                throw new ArgumentException("Selected outputs must not contain null.", nameof(selectedOutputs));
            if (output.Kind == TranslationGeneratedOutputKind.LocaleJson) assets.Add(output);
        }
        assets.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var json = new StringBuilder();
        json.Append("{\"assetManifestVersion\":").Append(TranslationOutputRenderer.AssetManifestVersion)
            .Append(",\"catalog\":").Append(GenerationSupport.JsonString(project.Id))
            .Append(",\"assets\":[");
        for (int index = 0; index < assets.Count; index++)
        {
            TranslationGeneratedOutput asset = assets[index];
            if (!paths.Add(asset.RelativePath))
                throw new ArgumentException("Selected outputs contain duplicate relative paths.", nameof(selectedOutputs));
            string locale = LocaleFor(project, asset.RelativePath);
            if (index > 0) json.Append(',');
            json.Append("{\"path\":").Append(GenerationSupport.JsonString(asset.RelativePath))
                .Append(",\"sha256\":").Append(GenerationSupport.JsonString(BareSha256(asset)))
                .Append(",\"byteLength\":").Append(asset.GetUtf8Bytes().Length)
                .Append(",\"mediaType\":").Append(GenerationSupport.JsonString(asset.MediaType))
                .Append(",\"locale\":").Append(GenerationSupport.JsonString(locale)).Append('}');
        }
        json.Append("]}");
        return new TranslationGeneratedOutput(
            TranslationGeneratedOutputKind.AssetManifestJson,
            project.Id + ".asset-manifest-v1.json",
            "application/json",
            json.ToString());
    }

    private static string BareSha256(TranslationGeneratedOutput output)
    {
        const string Prefix = "sha256:";
        if (!output.Sha256.StartsWith(Prefix, StringComparison.Ordinal) || output.Sha256.Length != Prefix.Length + 64)
            throw new ArgumentException("Selected output has an invalid SHA-256 value.", nameof(output));
        return output.Sha256.Substring(Prefix.Length);
    }

    private static string LocaleFor(Rmf2ProjectV5 project, string relativePath)
    {
        for (int index = 0; index < project.Locales.Count; index++)
        {
            string locale = project.Locales[index].Tag;
            if (string.Equals(relativePath, project.Id + "." + locale + ".locale-v5.json", StringComparison.Ordinal))
                return locale;
        }
        throw new ArgumentException("Locale output path is not canonical for the supplied RMF2 v5 project.", nameof(relativePath));
    }
}
