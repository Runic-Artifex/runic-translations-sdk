using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using Runic.Translations;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tool;

/// <summary>Executes the same verified runtime plan as applications with inert preview bindings.</summary>
internal static class Rmf2Preview
{
    internal static JsonObject Render(CompiledTextCatalog catalog, string key, string locale, JsonObject samples)
    {
        TextArgumentType Type(CompiledTextPlaceholder p) => Enum.Parse<TextArgumentType>(p.Type == TranslationArgumentType.Boolean ? "Bool" : p.Type.ToString(), true);
        TextArgumentFormat Format(CompiledTextPlaceholder p) => p.Format.Length == 0 ? TextArgumentFormat.None : Enum.Parse<TextArgumentFormat>(p.Format, true);
        var contracts = catalog.CanonicalResources.Select(resource => new TranslationPackMessageContract(new TranslationKey(catalog.Id, resource.Id, resource.Key), resource.Placeholders.Select(p => new TranslationPackArgumentContract(p.Name, Type(p), Format(p))).ToArray())).ToArray();
        var locales = catalog.Locales.Where(value => value.Tag == locale || value.Tag == catalog.DefaultLocale).Select(value => {
            var contract = new TranslationPackContract(catalog.Id, value.Tag, catalog.Fingerprint, contracts, 4, catalog.Rmf2MarkupContract);
            var verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(TranslationOutputRenderer.RenderLocaleJson(catalog, value.Tag).GetUtf8Bytes()), contract).AsTask().GetAwaiter().GetResult();
            return new CompiledTranslationLocale(value.Tag, value.Tag == catalog.DefaultLocale ? null : catalog.DefaultLocale, verified.Messages.Select(message => new CompiledTranslationValue(message.Key.Id, "", message.Message!)).ToArray());
        }).ToArray();
        var runtime = new CompiledTranslationCatalog(catalog.Id, catalog.DefaultLocale, catalog.CanonicalResources.Select(resource => new CompiledTranslationDefinition(resource.Key, resource.Placeholders.Select(p => new TranslationPlaceholderDescriptor(p.Name, Type(p), Format(p))).ToArray())).ToArray(), locales);
        var definition = catalog.CanonicalResources.Single(value => value.Key == key);
        var arguments = definition.Placeholders.Select(p => {
            string value = samples[p.Name]?.ToString() ?? throw new ArgumentException("A sample is required for " + p.Name + ".");
            var format = Format(p);
            return Type(p) switch {
                TextArgumentType.String => new TextArgument(p.Name, value),
                TextArgumentType.Int => new TextArgument(p.Name, long.Parse(value, CultureInfo.InvariantCulture), format),
                TextArgumentType.Number => new TextArgument(p.Name, decimal.Parse(value, CultureInfo.InvariantCulture), format),
                TextArgumentType.Bool => new TextArgument(p.Name, bool.Parse(value), format),
                TextArgumentType.Date => new TextArgument(p.Name, DateOnly.Parse(value, CultureInfo.InvariantCulture), format),
                TextArgumentType.Time => new TextArgument(p.Name, TimeOnly.Parse(value, CultureInfo.InvariantCulture), format),
                TextArgumentType.DateTime => new TextArgument(p.Name, DateTimeOffset.Parse(value, CultureInfo.InvariantCulture), format),
                TextArgumentType.Guid => new TextArgument(p.Name, Guid.Parse(value), format),
                _ => throw new ArgumentException("Unsupported preview input type."),
            };
        }).ToArray();
        var snapshot = new CompiledTranslationSnapshot(runtime, locale);
        var resourceKey = new TranslationKey(catalog.Id, definition.Id, key);
        if (!definition.ProducesStructuredContent)
            return new JsonObject { ["key"] = key, ["locale"] = locale, ["runs"] = new JsonArray(new JsonObject { ["text"] = snapshot.Format(resourceKey, arguments) }) };
        var content = snapshot.FormatContent(resourceKey, arguments);
        var bindings = definition.Slots.ToDictionary(slot => slot.Key, slot => slot.Value switch {
            "runic:link" => (InlineMarkupBinding)new InlineLinkBinding(new Uri("https://example.invalid/")),
            "runic:action" => new InlineActionBinding(() => { }),
            "runic:icon" => new InlineIconBinding(slot.Key, false, _ => slot.Key),
            _ => throw new ArgumentException("Unsupported preview slot."),
        }, StringComparer.Ordinal);
        JsonObject Run(InlineMarkupRun run) => new() { ["name"] = run.Name, ["text"] = run.Text, ["options"] = new JsonObject(run.Options.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))), ["children"] = new JsonArray(run.Children.Select(child => (JsonNode)Run(child)).ToArray()) };
        return new JsonObject { ["key"] = key, ["locale"] = locale, ["runs"] = new JsonArray(new Rmf2InlineRenderer(catalog.Rmf2MarkupContract!).Render(key, content, bindings).Select(run => (JsonNode)Run(run)).ToArray()) };
    }

    internal static JsonObject Render(Rmf2ProjectV5 project, string key, string locale, JsonObject samples)
    {
        ArgumentNullException.ThrowIfNull(project);
        Rmf2LocaleV5 selectedLocale = project.Locales.SingleOrDefault(item => item.Tag == locale)
            ?? throw new ArgumentException("Unknown preview locale.", nameof(locale));
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        Rmf2V5Definition definition = table.Definitions.SingleOrDefault(item => item.Contract.Key == key)
            ?? throw new TranslationAuthoringException("Unknown preview message.");
        HashSet<string> resolved = selectedLocale.ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        TranslationPackMessageContract[] contracts = table.Definitions.Where(item => item.Canonical || resolved.Contains(item.Contract.Key))
            .OrderBy(item => item.Contract.Key, StringComparer.Ordinal)
            .Select(item => TranslationPackMessageContract.FromRmf2Inputs(
                new TranslationKey(project.Id, item.Id, item.Contract.Key), Inputs(item.Contract.Inputs))).ToArray();
        TranslationPackContract packContract = TranslationPackContract.CreateRmf2V5(
            project.Id, locale, project.CallerFingerprint, contracts, project.MarkupContract);
        VerifiedExternalTranslationPack verified = TranslationPackLoader.VerifyAsync(
            new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, locale).GetUtf8Bytes()), packContract).AsTask().GetAwaiter().GetResult();
        TextArgument[] arguments = definition.Contract.Inputs.Select(input => Argument(input, samples)).ToArray();
        CompiledRmf2Message message = verified.Messages.Single(item => item.Key.Name == key).Message!.Rmf2V5!;
        if (!definition.Contract.Structured)
            return new JsonObject { ["key"] = key, ["locale"] = locale, ["runs"] = new JsonArray(new JsonObject { ["text"] = message.Format(arguments, locale) }) };
        LocalizedTextContent content = message.FormatContent(arguments, locale);
        var bindings = definition.Contract.Slots.ToDictionary(slot => slot.Key, slot => slot.Value.Kind switch {
            "runic:link" => (InlineMarkupBinding)new InlineLinkBinding(new Uri("https://example.invalid/")),
            "runic:action" => new InlineActionBinding(() => { }),
            "runic:icon" => new InlineIconBinding(slot.Key, false, _ => slot.Key),
            _ => throw new ArgumentException("Unsupported preview slot."),
        }, StringComparer.Ordinal);
        JsonObject Run(InlineMarkupRun run) => new() { ["name"] = run.Name, ["text"] = run.Text, ["options"] = new JsonObject(run.Options.Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value)))), ["children"] = new JsonArray(run.Children.Select(child => (JsonNode)Run(child)).ToArray()) };
        return new JsonObject { ["key"] = key, ["locale"] = locale, ["runs"] = new JsonArray(new Rmf2InlineRenderer(project.MarkupContract).Render(key, content, bindings).Select(run => (JsonNode)Run(run)).ToArray()) };
    }

    private static CompiledRmf2Input[] Inputs(IReadOnlyList<Rmf2InputV5> inputs) =>
        inputs.Select(input => new CompiledRmf2Input(input.Name, Type(input.Type))).ToArray();

    private static TextArgument Argument(Rmf2InputV5 input, JsonObject samples)
    {
        string value = samples[input.Name]?.ToString() ?? throw new ArgumentException("A sample is required for " + input.Name + ".");
        TextArgument carrier = input.Type switch {
            "string" => new TextArgument("_", value),
            "int64" => new TextArgument("_", long.Parse(value, CultureInfo.InvariantCulture)),
            "decimal" => new TextArgument("_", decimal.Parse(value, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)),
            "boolean" => new TextArgument("_", bool.Parse(value)),
            "date" => new TextArgument("_", DateOnly.Parse(value, CultureInfo.InvariantCulture)),
            "time" => new TextArgument("_", TimeOnly.Parse(value, CultureInfo.InvariantCulture)),
            "datetime" => new TextArgument("_", DateTimeOffset.Parse(value, CultureInfo.InvariantCulture)),
            "guid" => new TextArgument("_", Guid.Parse(value)),
            _ => throw new ArgumentException("Unsupported preview input type."),
        };
        return TextArgument.CreateRmf2(input.Name, carrier);
    }

    private static TextArgumentType Type(string type) => type switch {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number,
        "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time,
        "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid,
        _ => throw new ArgumentException("Unsupported preview input type."),
    };
}
