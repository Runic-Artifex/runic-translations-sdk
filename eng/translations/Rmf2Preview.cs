using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

using Runic.Translations.Generated;

namespace Runic.Translations.Internal;

/// <summary>Executes the verified RMF2 runtime plan with inert preview bindings.</summary>
/// <remarks>
/// The editor, the language server (<c>runic.renderPreview</c>), VS Code and the Visual Studio tool window all render
/// through this class, so a preview shows what the generated catalog formats at run time: the catalog's
/// <c>runtime.bidiIsolation</c>, the requested locale's regional formatting and the zone <c>timeZone=local</c> formats in.
/// </remarks>
internal static class Rmf2Preview
{
    /// <summary>Renders <paramref name="key"/> in the catalog locale <paramref name="locale"/>.</summary>
    /// <param name="project">The compiled project; compile with pseudo-locales to preview <c>en-XA</c> or <c>ar-XB</c>.</param>
    /// <param name="key">The message key.</param>
    /// <param name="locale">A locale of <paramref name="project"/>, whose text is shown.</param>
    /// <param name="samples">One sample per input.</param>
    /// <param name="requestedLocale">
    /// The tag a user asked for, such as <c>de-CH</c> for <c>de</c> content: numbers and dates format in it when it shares
    /// the content locale's language, as the runtime formats a <c>de-CH</c> request that resolved to <c>de</c>. The
    /// result reports it, the formatting locale and how it relates to <paramref name="locale"/> (exact, parent or related).
    /// Null is <paramref name="locale"/>.
    /// </param>
    /// <param name="timeZone">
    /// The zone that <c>timeZone=local</c>, the default of localized date-time styles, formats in: <c>utc</c>, <c>local</c>
    /// (the host's) or an IANA ID such as <c>Europe/Zurich</c>. A message's own <c>timeZone</c> option still wins. Null is
    /// the host's zone.
    /// </param>
    internal static JsonObject Render(Rmf2ProjectV5 project, string key, string locale, JsonObject samples,
        string? requestedLocale = null, string? timeZone = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        Rmf2LocaleV5 selectedLocale = project.Locales.SingleOrDefault(item => item.Tag == locale)
            ?? throw new ArgumentException("Unknown preview locale.", nameof(locale));
        string requested = locale;
        if (!string.IsNullOrWhiteSpace(requestedLocale) && !LocaleTag.TryCanonicalize(requestedLocale.Trim(), out requested))
            throw new ArgumentException("The requested locale '" + requestedLocale + "' is not a valid BCP 47 tag.", nameof(requestedLocale));
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        Rmf2V5Definition definition = table.Definitions.SingleOrDefault(item => item.Contract.Key == key)
            ?? throw new TranslationAuthoringException("Unknown preview message.");
        HashSet<string> resolved = selectedLocale.ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        TranslationPackMessageContract[] contracts = table.Definitions
            .Where(item => item.Canonical || resolved.Contains(item.Contract.Key))
            .OrderBy(item => item.Contract.Key, StringComparer.Ordinal)
            .Select(item => GeneratedCatalogSupport.CreatePackMessageContract(
                new TranslationKey(project.Id, item.Id, item.Contract.Key), Inputs(item.Contract.Inputs))).ToArray();
        TranslationPackContract packContract = GeneratedCatalogSupport.CreatePackContract(
            project.Id, locale, project.CallerFingerprint, contracts, project.MarkupContract);
        VerifiedExternalTranslationPack verified = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes()),
            packContract).AsTask().GetAwaiter().GetResult();
        TextArgument[] arguments = definition.Contract.Inputs.Select(input => Argument(input, samples)).ToArray();
        CompiledRmf2Message message = verified.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
        bool? isolation = project.BidiIsolation switch
        {
            TranslationBidiIsolation.On => true,
            TranslationBidiIsolation.Off => false,
            _ => null,
        };

        IDisposable zone;
        // An unknown preview zone is a bad argument, as an unknown locale is.
        try { zone = Rmf2LocalizedFormats.UseLocalTimeZone(string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim()); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException(exception.Message, nameof(timeZone), exception);
        }
        LocalizedTextContent content;
        using (zone)
            content = Rmf2RuntimeEvaluator.FormatContent(message, arguments, locale, TextPatternFormatter.DefaultMaximumOutputLength, requested, isolation);
        JsonObject result;
        if (!definition.Contract.Structured)
        {
            var text = new System.Text.StringBuilder();
            foreach (LocalizedTextContentNode node in content.Nodes.Span) text.Append(node.Value);
            result = TextResult(key, locale, text.ToString());
        }
        else
        {
            IReadOnlyDictionary<string, InlineMarkupBinding> bindings = definition.Contract.Slots.ToDictionary(
                slot => slot.Key, slot => PreviewBinding(slot.Key, slot.Value.Kind), StringComparer.Ordinal);
            result = definition.Contract.Content == Rmf2DocumentProfileV5.Document
                ? DocumentResult(key, locale, Rmf2MarkupContract.Link(project.MarkupContract), new LocalizedDocumentContent(content), bindings)
                : StructuredResult(key, locale, new Rmf2InlineRenderer(project.MarkupContract).Render(key, content, bindings));
        }
        // How the text was formatted, for the clients to show beside it.
        result["requestedLocale"] = requested;
        result["resolution"] = requested == locale ? "exact"
            : LocaleRequestResolver.Classify(requested, locale) == LocaleResolution.Parent ? "parent" : "related";
        result["formattingLocale"] = content.FormattingLocale;
        result["direction"] = LocaleDirection.ToHtml(LocaleDirection.Get(locale));
        if (!string.IsNullOrWhiteSpace(timeZone)) result["timeZone"] = timeZone.Trim();
        return result;
    }

    // Document messages return the semantic block tree. "runs" keeps one plain-text run so
    // clients that only read runs (the VS Code and Visual Studio previews) still show the text.
    private static JsonObject DocumentResult(string key, string locale, Rmf2MarkupContract contract,
        LocalizedDocumentContent content, IReadOnlyDictionary<string, InlineMarkupBinding> bindings)
    {
        var slots = bindings.ToDictionary(pair => pair.Key, pair => (MarkupBinding)pair.Value, StringComparer.Ordinal);
        var renderer = new Rmf2DocumentRenderer(contract);
        IReadOnlyList<DocumentBlock> blocks = renderer.Render(key, content, slots);
        string text = renderer.ToPlainText(key, content, slots, new Rmf2PlainTextOptions { AllowActionLabels = true });
        return new JsonObject
        {
            ["key"] = key,
            ["locale"] = locale,
            ["runs"] = new JsonArray(new JsonObject { ["text"] = text }),
            ["blocks"] = new JsonArray(blocks.Select(block => (JsonNode)Block(block)).ToArray()),
        };
    }

    private static JsonObject Block(DocumentBlock block) => new()
    {
        ["name"] = block.Name,
        ["occurrence"] = block.Occurrence,
        ["options"] = new JsonObject(block.Options.Select(pair =>
            KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
        ["blocks"] = new JsonArray(block.Blocks.Select(child => (JsonNode)Block(child)).ToArray()),
        ["runs"] = new JsonArray(block.Inlines.Select(run => (JsonNode)Run(run)).ToArray()),
    };

    private static JsonObject TextResult(string key, string locale, string text) => new()
    {
        ["key"] = key,
        ["locale"] = locale,
        ["runs"] = new JsonArray(new JsonObject { ["text"] = text }),
    };

    private static JsonObject StructuredResult(
        string key, string locale, IEnumerable<InlineMarkupRun> runs) => new()
    {
        ["key"] = key,
        ["locale"] = locale,
        ["runs"] = new JsonArray(runs.Select(run => (JsonNode)Run(run)).ToArray()),
    };

    private static JsonObject Run(InlineMarkupRun run) => new()
    {
        ["name"] = run.Name,
        ["text"] = run.Text,
        ["options"] = new JsonObject(run.Options.Select(pair =>
            KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))),
        ["children"] = new JsonArray(run.Children.Select(child => (JsonNode)Run(child)).ToArray()),
    };

    private static InlineMarkupBinding PreviewBinding(string slot, string kind) => kind switch
    {
        // Preview bindings satisfy the verified runtime contract but cannot navigate or invoke application code.
        "runic:link" => new InlineLinkBinding(new Uri("https://example.invalid/")),
        "runic:action" => new InlineActionBinding(static () => { }),
        "runic:icon" => new InlineIconBinding(slot, false, _ => slot),
        _ => throw new ArgumentException("Unsupported preview slot."),
    };

    private static CompiledRmf2Input[] Inputs(IReadOnlyList<Rmf2InputV5> inputs) =>
        inputs.Select(input => new CompiledRmf2Input(input.Name, Type(input.Type))).ToArray();

    private static TextArgument Argument(Rmf2InputV5 input, JsonObject samples)
    {
        JsonNode sample = samples[input.Name]
            ?? throw new ArgumentException("A sample is required for " + input.Name + ".");
        if (input.Type == "list")
        {
            // A list sample is a JSON array of strings.
            string[] items = sample is JsonArray array && array.All(static item => item is JsonValue value && value.TryGetValue(out string? _))
                ? array.Select(static item => item!.GetValue<string>()).ToArray()
                : throw new ArgumentException("The sample for " + input.Name + " must be an array of strings.");
            return GeneratedCatalogSupport.CreateArgument(input.Name, new TextArgument("_", items));
        }
        string value = sample.ToString();
        TextArgument carrier = input.Type switch
        {
            "string" => new TextArgument("_", value),
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(
                value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)),
            "boolean" => new TextArgument("_", bool.Parse(value)),
            "date" => new TextArgument("_", DateOnly.Parse(value, CultureInfo.InvariantCulture)),
            "time" => new TextArgument("_", TimeOnly.Parse(value, CultureInfo.InvariantCulture)),
            "datetime" => new TextArgument("_", DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)),
            "guid" => new TextArgument("_", Guid.Parse(value)),
            _ => throw new ArgumentException("Unsupported preview input type."),
        };
        return GeneratedCatalogSupport.CreateArgument(input.Name, carrier);
    }

    private static TextArgumentType Type(string type) => type switch
    {
        "string" => TextArgumentType.String,
        "int64" => TextArgumentType.Int,
        "decimal" => TextArgumentType.Number,
        "boolean" => TextArgumentType.Bool,
        "date" => TextArgumentType.Date,
        "time" => TextArgumentType.Time,
        "datetime" => TextArgumentType.DateTime,
        "guid" => TextArgumentType.Guid,
        "list" => TextArgumentType.List,
        _ => throw new ArgumentException("Unsupported preview input type."),
    };
}
