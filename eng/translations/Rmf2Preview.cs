using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Internal;

/// <summary>Executes the verified RMF2 runtime plan with inert preview bindings.</summary>
internal static class Rmf2Preview
{
    internal static JsonObject Render(Rmf2ProjectV5 project, string key, string locale, JsonObject samples)
    {
        ArgumentNullException.ThrowIfNull(project);
        Rmf2LocaleV5 selectedLocale = project.Locales.SingleOrDefault(item => item.Tag == locale)
            ?? throw new ArgumentException("Unknown preview locale.", nameof(locale));
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        Rmf2V5Definition definition = table.Definitions.SingleOrDefault(item => item.Contract.Key == key)
            ?? throw new TranslationAuthoringException("Unknown preview message.");
        HashSet<string> resolved = selectedLocale.ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        TranslationPackMessageContract[] contracts = table.Definitions
            .Where(item => item.Canonical || resolved.Contains(item.Contract.Key))
            .OrderBy(item => item.Contract.Key, StringComparer.Ordinal)
            .Select(item => TranslationPackMessageContract.FromRmf2Inputs(
                new TranslationKey(project.Id, item.Id, item.Contract.Key), Inputs(item.Contract.Inputs))).ToArray();
        TranslationPackContract packContract = TranslationPackContract.CreateRmf2V5(
            project.Id, locale, project.CallerFingerprint, contracts, project.MarkupContract);
        VerifiedExternalTranslationPack verified = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes()),
            packContract).AsTask().GetAwaiter().GetResult();
        TextArgument[] arguments = definition.Contract.Inputs.Select(input => Argument(input, samples)).ToArray();
        CompiledRmf2Message message = verified.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
        if (!definition.Contract.Structured)
            return TextResult(key, locale, message.Format(arguments, locale));

        LocalizedTextContent content = message.FormatContent(arguments, locale);
        IReadOnlyDictionary<string, InlineMarkupBinding> bindings = definition.Contract.Slots.ToDictionary(
            slot => slot.Key, slot => PreviewBinding(slot.Key, slot.Value.Kind), StringComparer.Ordinal);
        if (definition.Contract.Content == Rmf2DocumentProfileV5.Document)
            return DocumentResult(key, locale, Rmf2MarkupContract.Link(project.MarkupContract), new LocalizedDocumentContent(content), bindings);
        return StructuredResult(
            key, locale, new Rmf2InlineRenderer(project.MarkupContract).Render(key, content, bindings));
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
        string value = samples[input.Name]?.ToString()
            ?? throw new ArgumentException("A sample is required for " + input.Name + ".");
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
        return TextArgument.CreateRmf2(input.Name, carrier);
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
        _ => throw new ArgumentException("Unsupported preview input type."),
    };
}
