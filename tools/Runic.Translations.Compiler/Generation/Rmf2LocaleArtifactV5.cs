using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler.Generation;

// RMF2 emission consumes the typed v5 project carrier directly rather than a
// reduced catalog projection that would erase execution-v2 semantics.
internal static class Rmf2LocaleArtifactV5
{
    internal const int ArtifactVersion = 5;

    internal static TranslationGeneratedOutput Render(Rmf2ProjectV5 project, string localeTag) => Render(project, localeTag, null);

    /// <summary>
    /// Renders the overlay pack of one locale of a library catalog compiled with override sources
    /// (<c>TranslationCompiler.CompileRmf2OverlayV5</c>): a locale artifact v5 with only the overridden messages, which the
    /// runtimes load as an overlay (.NET <c>TranslationOverlay</c>, ESM <c>installOverlay</c>) and check key by key.
    /// </summary>
    internal static TranslationGeneratedOutput RenderOverlay(Rmf2ProjectV5 project, string localeTag)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (!project.OverlayKeys.TryGetValue(localeTag, out IReadOnlyList<string>? keys))
            throw new ArgumentException("Locale '" + localeTag + "' has no override messages in catalog '" + project.Id + "'.", nameof(localeTag));
        return Render(project, localeTag, new HashSet<string>(keys, StringComparer.Ordinal));
    }

    private static TranslationGeneratedOutput Render(Rmf2ProjectV5 project, string localeTag, HashSet<string>? only)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(localeTag);
        Rmf2LocaleV5 locale = project.Locales.SingleOrDefault(item => item.Tag == localeTag)
            ?? throw new ArgumentException("Locale '" + localeTag + "' is not declared by catalog '" + project.Id + "'.", nameof(localeTag));
        var contracts = new Dictionary<string, Rmf2MessageContractV5>(
            project.CanonicalMessages.Count + project.ExtraMessages.Count, StringComparer.Ordinal);
        foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages.Concat(project.ExtraMessages))
            if (!contracts.TryAdd(contract.Key, contract))
                throw new InvalidOperationException("Duplicate v5 message contract '" + contract.Key + "'.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("artifactVersion", ArtifactVersion);
            writer.WriteNumber("messageGrammarVersion", Rmf2ProjectV5.MessageGrammarVersion);
            writer.WriteString("profile", Rmf2ProjectV5.Profile);
            writer.WriteString("catalog", project.Id);
            writer.WriteString("locale", locale.Tag);
            writer.WriteString("contractFingerprint", project.CallerFingerprint);
            writer.WriteStartObject("messages");
            foreach (Rmf2TranslationV5 resource in locale.ResolvedResources.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (only is not null && !only.Contains(resource.Key)) continue;
                if (!contracts.TryGetValue(resource.Key, out Rmf2MessageContractV5? contract))
                    throw new InvalidOperationException("Resolved v5 resource '" + resource.Key + "' has no message contract.");
                writer.WriteStartObject(resource.Key);
                writer.WriteString("contentLocale", resource.ContentLocale);
                writer.WritePropertyName("ast");
                // Every locale artifact carries the canonical caller surface.
                // Unused locale inputs remain inert but keep snapshot invocation
                // independent of which locale supplied the resolved message.
                using JsonDocument ast = JsonDocument.Parse(Rmf2MessageJsonV5.Serialize(resource.Message with { Inputs = contract.Inputs }));
                ast.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WritePropertyName("markupContract");
            using (JsonDocument markup = JsonDocument.Parse(project.MarkupContract)) markup.RootElement.WriteTo(writer);
            writer.WriteEndObject();
        }
        return new TranslationGeneratedOutput(TranslationGeneratedOutputKind.LocaleJson,
            project.Id + "." + locale.Tag + ".locale-v5.json", "application/json", Encoding.UTF8.GetString(stream.ToArray()));
    }
}
