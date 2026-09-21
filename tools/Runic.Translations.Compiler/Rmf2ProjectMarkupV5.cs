using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Runic.Translations.Compiler;

internal sealed record Rmf2LinkedMarkupV5(Rmf2MessageV5 Message,
    IReadOnlyDictionary<string, string> Slots, IReadOnlyList<IReadOnlyDictionary<string, int>> VariantCounts,
    IReadOnlyList<string> Names);

internal sealed class Rmf2ProjectMarkupV5
{
    private readonly Rmf2MarkupRegistry _registry;
    internal IReadOnlyDictionary<string, Rmf2MarkupContractV5> Contracts { get; }
    internal JsonValue? SlotConstraints => _registry.SlotConstraints;

    internal Rmf2ProjectMarkupV5(JsonValue config, TranslationSource project, DiagnosticBag diagnostics)
    {
        _registry = Rmf2MarkupRegistry.Read(config.Property("markup"), project, diagnostics);
        var contracts = new SortedDictionary<string, Rmf2MarkupContractV5>(StringComparer.Ordinal);
        foreach (var entry in _registry.Contracts)
        {
            var options = new SortedDictionary<string, Rmf2MarkupOptionContractV5>(StringComparer.Ordinal);
            foreach (var option in entry.Value.Options)
            {
                string[] values = option.Value.Values.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                string? fallback = option.Value.Default;
                if (fallback is not null)
                {
                    if (!TryLiteral(option.Value.Type, values, new(option.Value.Type == "number" ? "number-literal" : "string-literal", fallback), out var canonical))
                        Error("Invalid default for markup option '" + option.Key + "'.");
                    else fallback = canonical!.Canonical ?? canonical.Value;
                }
                options.Add(option.Key, new(option.Value.Type, Array.AsReadOnly(values), fallback, option.Value.LiteralOnly));
            }
            contracts.Add(entry.Key, new(entry.Key, entry.Value.Standalone, entry.Value.Interactive, entry.Value.PlainText,
                new ReadOnlyDictionary<string, Rmf2MarkupOptionContractV5>(options)));
        }
        Contracts = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(contracts);
        // The v1 schema uses string defaults even for numeric/boolean options.
        // Validate JSON shapes here; the shared v4 reader intentionally retains
        // its historical coercions and output bytes.
        if (config.Property("markup")?.Value.Property("contracts")?.Value is { Kind: JsonKind.Array } declarations)
            foreach (var declaration in declarations.Items.Where(item => item.Kind == JsonKind.Object))
            {
                if (declaration.Property("interactive")?.Value.Kind is not (JsonKind.True or JsonKind.False)) Error("Markup interactive must be a boolean.");
                if (declaration.Property("options")?.Value is not { Kind: JsonKind.Object } schemas) continue;
                foreach (var option in schemas.Properties)
                {
                    if (option.Value.Property("default") is { } fallback && fallback.Value.Kind != JsonKind.String) Error("Markup option defaults must be strings in project-v1.");
                    if (option.Value.Property("literalOnly") is { } literal && literal.Value.Kind is not (JsonKind.True or JsonKind.False)) Error("Markup literalOnly must be a boolean.");
                    if (option.Value.Property("values") is { } values && (values.Value.Kind != JsonKind.Array || values.Value.Items.Count == 0 || values.Value.Items.Any(v => v.Kind != JsonKind.String))) Error("Markup enum values must be a nonempty string array.");
                }
            }
        void Error(string message) => diagnostics.Add("RTR0060", TranslationDiagnosticSeverity.Error, message, project, config.Span);
    }

    internal Rmf2LinkedMarkupV5 Link(Rmf2MessageV5 message, TextSourceLocation location, DiagnosticBag diagnostics)
    {
        var slots = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        var counts = new List<IReadOnlyDictionary<string, int>>();
        var variants = new List<Rmf2VariantV5>();
        var types = message.Inputs.ToDictionary(input => input.Name, input => input.Type, StringComparer.Ordinal);
        foreach (var local in message.Declarations.Where(declaration => declaration.Kind == "local")) types[local.Name] = local.Expression.ValueType;
        foreach (var variant in message.Variants)
        {
            var stack = new Stack<Rmf2MarkupContractV5>();
            var nodes = new List<Rmf2NodeV5>();
            var occurrences = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var node in variant.Nodes)
            {
                if (node is not Rmf2MarkupV5 tag) { nodes.Add(node); continue; }
                string name = _registry.Aliases.GetValueOrDefault(tag.Name) ?? tag.Name;
                if (!Contracts.TryGetValue(name, out var contract)) { Error("Unknown markup '" + tag.Name + "'."); continue; }
                names.Add(name);
                if (tag.MarkupKind == "close")
                {
                    if (stack.Count == 0 || stack.Pop().Name != name) Error("Unbalanced canonical inline markup.");
                    if (tag.Options.Count != 0) Error("Closing markup cannot declare options.");
                    nodes.Add(tag with { Name = name });
                    continue;
                }
                if (contract.Standalone != (tag.MarkupKind == "standalone")) Error("Markup '" + name + "' uses the wrong paired/standalone form.");
                if (contract.Interactive && stack.Any(parent => parent.Interactive)) Error("Interactive markup cannot be nested inside another interactive element.");
                if (stack.Count >= 16) Error("Inline markup nesting exceeds 16 levels.");
                var options = new SortedDictionary<string, Rmf2ValueV5>(StringComparer.Ordinal);
                foreach (var option in tag.Options) options[option.Name] = option.Value;
                bool functional = name is "runic:link" or "runic:action" or "runic:icon";
                if (functional)
                {
                    var reference = options.GetValueOrDefault("ref") ?? new("string-literal", name == "runic:icon" ? "" : name.Substring(6));
                    if (reference.Kind != "string-literal" || !Rmf2MarkupRegistry.Name.IsMatch(reference.Value)) Error("Functional markup ref requires a static slot ID; icon requires an explicit ref.");
                    else
                    {
                        if (slots.TryGetValue(reference.Value, out string? previous) && previous != name) Error("Slot '" + reference.Value + "' has conflicting kinds.");
                        slots[reference.Value] = name;
                        occurrences[reference.Value] = occurrences.GetValueOrDefault(reference.Value) + 1;
                    }
                    options["ref"] = reference;
                }
                foreach (var option in options.ToArray())
                {
                    if (functional && option.Key == "ref") continue;
                    if (!contract.Options.TryGetValue(option.Key, out var schema)) { Error("Unknown option '" + option.Key + "' on '" + name + "'."); continue; }
                    if (option.Value.Kind is "input" or "local")
                    {
                        if (schema.LiteralOnly || !types.TryGetValue(option.Value.Value, out string? type) || !AcceptsType(schema.Type, type)) Error("Markup option '" + option.Key + "' has an incompatible variable type.");
                    }
                    else if (!TryLiteral(schema.Type, schema.Values, option.Value, out var value)) Error("Invalid typed literal for markup option '" + option.Key + "'.");
                    else options[option.Key] = value!;
                }
                foreach (var option in contract.Options)
                    if (!options.ContainsKey(option.Key))
                    {
                        if (option.Value.Default is null) Error("Missing required markup option '" + option.Key + "'.");
                        else if (TryLiteral(option.Value.Type, option.Value.Values,
                            new(option.Value.Type == "number" ? "number-literal" : "string-literal", option.Value.Default), out var value)) options[option.Key] = value!;
                    }
                nodes.Add(tag with { Name = name, Options = options.Select(option => new Rmf2OptionV5(option.Key, option.Value)).ToArray() });
                if (tag.MarkupKind == "open") stack.Push(contract);
            }
            if (stack.Count != 0) Error("Unclosed canonical inline markup.");
            counts.Add(new ReadOnlyDictionary<string, int>(occurrences));
            variants.Add(variant with { Nodes = nodes.AsReadOnly() });
        }
        return new(message with { Variants = variants.AsReadOnly() }, new ReadOnlyDictionary<string, string>(slots), counts.AsReadOnly(), names.ToArray());
        void Error(string message) => diagnostics.Add("RTR0061", TranslationDiagnosticSeverity.Error, message, location);
    }

    internal IReadOnlyDictionary<string, Rmf2SlotV5> Requirements(string key, Rmf2LinkedMarkupV5 source,
        TextSourceLocation location, DiagnosticBag diagnostics)
    {
        var result = new SortedDictionary<string, Rmf2SlotV5>(StringComparer.Ordinal);
        foreach (var slot in source.Slots)
        {
            int min = 1, max = 1;
            JsonValue? settings = SlotConstraints?.Property(key)?.Value.Property(slot.Key)?.Value;
            if (settings is null && source.VariantCounts.Any(variant => !variant.ContainsKey(slot.Key))) Error("Conditional source slot '" + slot.Key + "' requires explicit markup.slots constraints.");
            if (settings is not null && (settings.Kind != JsonKind.Object || settings.Properties.Any(p => p.Name is not ("min" or "max")) ||
                settings.Property("min")?.Value.Kind != JsonKind.Number || settings.Property("max")?.Value.Kind != JsonKind.Number ||
                !int.TryParse(settings.Property("min")?.Value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out min) ||
                !int.TryParse(settings.Property("max")?.Value.Text, NumberStyles.None, CultureInfo.InvariantCulture, out max) || min < 0 || max < min || max > 4096))
            { Error("Slot constraints require integer min/max with 0 <= min <= max <= 4096."); min = max = 1; }
            result[slot.Key] = new(slot.Value, min, max);
        }
        return new ReadOnlyDictionary<string, Rmf2SlotV5>(result);
        void Error(string message) => diagnostics.Add("RTR0062", TranslationDiagnosticSeverity.Error, message, location);
    }

    internal static void ValidateSlots(string key, IReadOnlyDictionary<string, Rmf2SlotV5> expected,
        Rmf2LinkedMarkupV5 translation, TextSourceLocation location, DiagnosticBag diagnostics)
    {
        foreach (var slot in translation.Slots)
            if (!expected.TryGetValue(slot.Key, out var requirement) || requirement.Kind != slot.Value) Error("Translation changes slot '" + slot.Key + "' or its kind in '" + key + "'.");
        foreach (var slot in expected)
            foreach (var variant in translation.VariantCounts)
            {
                int count = variant.GetValueOrDefault(slot.Key);
                if (count < slot.Value.Min || count > slot.Value.Max) Error("Slot '" + slot.Key + "' must occur " + slot.Value.Min + ".." + slot.Value.Max + " times in every variant of '" + key + "'.");
            }
        void Error(string message) => diagnostics.Add("RTR0062", TranslationDiagnosticSeverity.Error, message, location);
    }

    internal static string Export(IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts,
        IReadOnlyList<Rmf2MessageContractV5> messages, IReadOnlyList<Rmf2LocaleV5>? locales = null)
    {
        var exportedContracts = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            var options = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var option in contract.Value.Options) options[option.Key] = new { type = option.Value.Type, values = option.Value.Values, @default = option.Value.Default, literalOnly = option.Value.LiteralOnly };
            exportedContracts[contract.Key] = new { kind = contract.Value.Standalone ? "standalone" : "paired", interactive = contract.Value.Interactive, plainText = contract.Value.PlainText, options };
        }
        var exportedMessages = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            var slots = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var slot in message.Slots) slots[slot.Key] = new { kind = slot.Value.Kind, min = slot.Value.Min, max = slot.Value.Max };
            if (locales is null) exportedMessages[message.Key] = new { slots, structured = message.Structured, markup = message.MarkupNames };
            else
            {
                var contentLocales = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var locale in locales)
                    if (locale.ResolvedResources.FirstOrDefault(resource => resource.Key == message.Key) is { } resource) contentLocales[locale.Tag] = resource.ContentLocale;
                exportedMessages[message.Key] = new { slots, structured = message.Structured, contentLocales };
            }
        }
        return JsonSerializer.Serialize(new { version = 1, contracts = exportedContracts, messages = exportedMessages });
    }

    private static bool AcceptsType(string schema, string type) => schema switch
    { "number" => type is "int64" or "decimal", "boolean" => type == "boolean", _ => type == "string" };

    private static bool TryLiteral(string type, IReadOnlyList<string> values, Rmf2ValueV5 value, out Rmf2ValueV5? result)
    {
        result = value;
        if (type == "number")
        {
            if (value.Kind != "number-literal" || !Rmf2DecimalV5.TryCanonicalize(value.Value, out string canonical)) return false;
            result = new("number-literal", canonical, canonical); return true;
        }
        if (value.Kind != "string-literal") return false;
        return type switch { "string" => true, "boolean" => value.Value is "true" or "false", "enum" => values.Contains(value.Value, StringComparer.Ordinal), _ => false };
    }
}
