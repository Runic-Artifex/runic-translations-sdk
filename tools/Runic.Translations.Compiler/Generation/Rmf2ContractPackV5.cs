using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler.Generation;

/// <summary>
/// The contract of a shipped release of a catalog, read from one of that release's locale artifacts v5
/// (<c>&lt;catalog&gt;.&lt;locale&gt;.locale-v5.json</c>): every artifact carries each key's caller inputs and the release's
/// markup contract, so it describes what that release accepts.
/// </summary>
internal sealed class Rmf2ShippedContractV5
{
    private Rmf2ShippedContractV5(string catalog, string fingerprint, string markupContract,
        IReadOnlyDictionary<string, IReadOnlyList<Rmf2InputV5>> inputs, JsonElement markupMessages, JsonElement markupContracts, IReadOnlySet<string> locales)
    {
        Catalog = catalog;
        ContractFingerprint = fingerprint;
        MarkupContract = markupContract;
        Inputs = inputs;
        MarkupMessages = markupMessages;
        MarkupContracts = markupContracts;
        Locales = locales;
    }

    internal string Catalog { get; }
    internal string ContractFingerprint { get; }
    // The release's markup contract exactly as its artifacts carry it: an exact-fingerprint loader compares it byte for byte.
    internal string MarkupContract { get; }
    internal IReadOnlyDictionary<string, IReadOnlyList<Rmf2InputV5>> Inputs { get; }
    internal JsonElement MarkupMessages { get; }
    internal JsonElement MarkupContracts { get; }
    internal IReadOnlySet<string> Locales { get; }

    /// <summary>Reads a locale artifact v5; throws <see cref="FormatException"/> when it is not one.</summary>
    internal static Rmf2ShippedContractV5 Parse(ReadOnlySpan<byte> json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 64 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("artifactVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != Rmf2LocaleArtifactV5.ArtifactVersion)
                throw new FormatException("The contract is not a locale artifact v5 (<catalog>.<locale>.locale-v5.json from generate --emit-json).");
            string catalog = root.GetProperty("catalog").GetString() ?? throw new FormatException("The contract has no catalog.");
            string fingerprint = root.GetProperty("contractFingerprint").GetString() ?? throw new FormatException("The contract has no fingerprint.");
            JsonElement markup = root.GetProperty("markupContract");
            if (markup.ValueKind != JsonValueKind.Object || !markup.TryGetProperty("version", out JsonElement markupVersion) || markupVersion.GetInt32() != Rmf2ProjectV5.MarkupContractVersion)
                throw new FormatException("The contract was built for another Runic markup contract version.");
            var inputs = new SortedDictionary<string, IReadOnlyList<Rmf2InputV5>>(StringComparer.Ordinal);
            foreach (System.Text.Json.JsonProperty message in root.GetProperty("messages").EnumerateObject())
                inputs.Add(message.Name, message.Value.GetProperty("ast").GetProperty("inputs").EnumerateArray()
                    .Select(input => new Rmf2InputV5(input.GetProperty("name").GetString()!, input.GetProperty("type").GetString()!)).ToArray());
            JsonElement messages = markup.TryGetProperty("messages", out JsonElement value) ? value.Clone() : default;
            JsonElement contracts = markup.TryGetProperty("contracts", out JsonElement definitions) ? definitions.Clone() : default;
            // The release's locales: every message maps each of them to its content locale.
            var locales = new HashSet<string>(StringComparer.Ordinal);
            if (messages.ValueKind == JsonValueKind.Object)
                foreach (System.Text.Json.JsonProperty message in messages.EnumerateObject())
                    if (message.Value.TryGetProperty("contentLocales", out JsonElement contentLocales))
                        foreach (System.Text.Json.JsonProperty locale in contentLocales.EnumerateObject()) locales.Add(locale.Name);
            return new Rmf2ShippedContractV5(catalog, fingerprint, markup.GetRawText(), inputs, messages, contracts, locales);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("The contract is not a valid locale artifact v5: " + exception.Message, exception);
        }
    }
}

/// <summary>A message left out of a pack for a shipped contract, and why.</summary>
internal sealed record Rmf2ContractPackSkipV5(string Locale, string Key, string Reason);

/// <summary>
/// Locale artifacts v5 for an older shipped release of a catalog: only the messages that release accepts, stamped with its
/// contract fingerprint and markup contract. Such a pack loads in that release as an exact-fingerprint pack
/// (<c>IExternalTranslationSource</c>, <c>decodeLocaleArtifact</c>) as well as an overlay.
/// </summary>
internal static class Rmf2ContractPackV5
{
    /// <summary>
    /// Renders the pack of <paramref name="localeTag"/>, or returns <see langword="null"/> when the shipped release does not
    /// declare the locale. Messages left out are added to <paramref name="skipped"/>.
    /// </summary>
    internal static TranslationGeneratedOutput? Render(Rmf2ProjectV5 project, string localeTag, Rmf2ShippedContractV5 shipped, ICollection<Rmf2ContractPackSkipV5> skipped)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(shipped);
        if (!string.Equals(project.Id, shipped.Catalog, StringComparison.Ordinal))
            throw new ArgumentException("The contract is of catalog '" + shipped.Catalog + "', not '" + project.Id + "'.", nameof(shipped));
        Rmf2LocaleV5 locale = project.Locales.Single(item => item.Tag == localeTag);
        if (!shipped.Locales.Contains(localeTag)) return null;
        var contracts = project.CanonicalMessages.Concat(project.ExtraMessages).ToDictionary(item => item.Key, StringComparer.Ordinal);
        using JsonDocument currentMarkup = JsonDocument.Parse(project.MarkupContract);
        JsonElement currentMessages = currentMarkup.RootElement.TryGetProperty("messages", out JsonElement messagesValue) ? messagesValue : default;
        JsonElement currentContracts = currentMarkup.RootElement.TryGetProperty("contracts", out JsonElement contractsValue) ? contractsValue : default;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("artifactVersion", Rmf2LocaleArtifactV5.ArtifactVersion);
            writer.WriteNumber("messageGrammarVersion", Rmf2ProjectV5.MessageGrammarVersion);
            writer.WriteString("profile", Rmf2ProjectV5.Profile);
            writer.WriteString("catalog", project.Id);
            writer.WriteString("locale", locale.Tag);
            writer.WriteString("contractFingerprint", shipped.ContractFingerprint);
            writer.WriteStartObject("messages");
            foreach (Rmf2TranslationV5 resource in locale.ResolvedResources.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                Rmf2MessageContractV5 contract = contracts[resource.Key];
                string? reason = Incompatibility(resource, contract, localeTag, shipped, currentMessages, currentContracts, out IReadOnlyList<Rmf2InputV5>? inputs);
                if (reason is not null) { skipped.Add(new(localeTag, resource.Key, reason)); continue; }
                writer.WriteStartObject(resource.Key);
                writer.WriteString("contentLocale", resource.ContentLocale);
                writer.WritePropertyName("ast");
                // The shipped release's input order, which its loaders compare by position.
                using JsonDocument ast = JsonDocument.Parse(Rmf2MessageJsonV5.Serialize(resource.Message with { Inputs = inputs! }));
                ast.RootElement.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WritePropertyName("markupContract");
            writer.WriteRawValue(shipped.MarkupContract, skipInputValidation: false);
            writer.WriteEndObject();
        }
        return new TranslationGeneratedOutput(TranslationGeneratedOutputKind.LocaleJson,
            project.Id + "." + locale.Tag + ".locale-v5.json", "application/json", Encoding.UTF8.GetString(stream.ToArray()));
    }

    // Why the shipped release would not accept the message, or null. Conservative: the same inputs, the same content locale for
    // the pack's locale, the same markup facts for the key (content kind, slots, skeletons) and the same definitions of the
    // markup it may use.
    private static string? Incompatibility(Rmf2TranslationV5 resource, Rmf2MessageContractV5 contract, string locale, Rmf2ShippedContractV5 shipped,
        JsonElement currentMessages, JsonElement currentContracts, out IReadOnlyList<Rmf2InputV5>? inputs)
    {
        inputs = null;
        if (!shipped.Inputs.TryGetValue(resource.Key, out IReadOnlyList<Rmf2InputV5>? shippedInputs)) return "unknown-key";
        if (shippedInputs.Count != contract.Inputs.Count || contract.Inputs.Any(input => !shippedInputs.Contains(input))) return "incompatible-inputs";
        JsonElement shippedMessage = shipped.MarkupMessages.ValueKind == JsonValueKind.Object && shipped.MarkupMessages.TryGetProperty(resource.Key, out JsonElement value) ? value : default;
        if (shippedMessage.ValueKind != JsonValueKind.Object || !shippedMessage.TryGetProperty("contentLocales", out JsonElement contentLocales) ||
            !contentLocales.TryGetProperty(locale, out JsonElement contentLocale) || contentLocale.GetString() != resource.ContentLocale)
            return "content-locale-changed";
        JsonElement currentMessage = currentMessages.ValueKind == JsonValueKind.Object && currentMessages.TryGetProperty(resource.Key, out JsonElement current) ? current : default;
        if (MarkupFacts(currentMessage) != MarkupFacts(shippedMessage)) return MemberText(currentMessage, "content") != MemberText(shippedMessage, "content") ? "content-kind-changed" : "incompatible-markup";
        foreach (string name in contract.MarkupNames)
            if (MemberText(currentContracts, name) != MemberText(shipped.MarkupContracts, name)) return "incompatible-markup";
        inputs = shippedInputs;
        return null;
    }

    // A message's markup entry without its content locales, as canonical text.
    private static string MarkupFacts(JsonElement message)
    {
        if (message.ValueKind != JsonValueKind.Object) return "";
        var builder = new StringBuilder();
        foreach (System.Text.Json.JsonProperty property in message.EnumerateObject().Where(item => item.Name != "contentLocales").OrderBy(item => item.Name, StringComparer.Ordinal))
            builder.Append(property.Name).Append('=').Append(property.Value.GetRawText()).Append(';');
        return builder.ToString();
    }

    private static string? MemberText(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out JsonElement member) ? member.GetRawText() : null;
}
