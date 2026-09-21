using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler.Generation;

// Profile-selected v5 emission consumes the typed project carrier directly. It must not
// pass through CompiledTextCatalog because that carrier cannot represent v5.
internal static class Rmf2LocaleArtifactV5
{
    internal const int ArtifactVersion = 5;

    internal static TranslationGeneratedOutput Render(Rmf2ProjectV5 project, string localeTag)
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
