using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

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
                var schema = new Rmf2MarkupOptionContractV5(option.Value.Type, Array.AsReadOnly(values), null, option.Value.LiteralOnly, option.Value.Minimum, option.Value.Maximum);
                if (fallback is not null)
                {
                    if (!TryLiteral(schema, DefaultLiteral(schema.Type, fallback), out var canonical))
                        Error("Invalid default for markup option '" + option.Key + "'.");
                    else fallback = canonical!.Canonical ?? canonical.Value;
                }
                options.Add(option.Key, schema with { Default = fallback });
            }
            contracts.Add(entry.Key, new(entry.Key, entry.Value.Standalone, entry.Value.Interactive, entry.Value.PlainText,
                new ReadOnlyDictionary<string, Rmf2MarkupOptionContractV5>(options), entry.Value.Placement, entry.Value.Children));
        }
        Contracts = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(contracts);
        // The v1 schema uses string defaults even for numeric/boolean options.
        // Validate JSON shapes here while preserving the project's compatibility
        // bytes; the canonical v5 path uses the typed values above.
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

    private static readonly IReadOnlyDictionary<string, int> NoOccurrences = ReadOnlyDictionary<string, int>.Empty;
    // An empty occurrence count per variant, shared by the messages with that many variants and no markup.
    private static readonly IReadOnlyList<IReadOnlyDictionary<string, int>>[] SharedNoOccurrences = new IReadOnlyList<IReadOnlyDictionary<string, int>>[33];
    private static IReadOnlyList<IReadOnlyDictionary<string, int>> NoOccurrencesFor(int variants)
    {
        if (variants < SharedNoOccurrences.Length && Volatile.Read(ref SharedNoOccurrences[variants]) is { } shared) return shared;
        var empty = new IReadOnlyDictionary<string, int>[variants];
        Array.Fill(empty, NoOccurrences);
        var result = Array.AsReadOnly(empty);
        if (variants < SharedNoOccurrences.Length) Volatile.Write(ref SharedNoOccurrences[variants], result);
        return result;
    }

    internal Rmf2LinkedMarkupV5 Link(Rmf2MessageV5 message, TextSourceLocation location, DiagnosticBag diagnostics)
    {
        // Most messages have no markup: nothing to resolve or check, no slots, and an empty occurrence count per variant.
        bool markup = false;
        foreach (var variant in message.Variants)
        {
            foreach (var node in variant.Nodes)
                if (node is Rmf2MarkupV5) { markup = true; break; }
            if (markup) break;
        }
        if (!markup)
        {
            return new(message, ReadOnlyDictionary<string, string>.Empty, NoOccurrencesFor(message.Variants.Count), Array.Empty<string>());
        }
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
                // Inline nesting only; the document profile checks the total element depth (RTR0073).
                if (contract.Placement == "inline" && stack.Count(parent => parent.Placement == "inline") >= 16) Error("Inline markup nesting exceeds 16 levels.");
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
                    else if (!TryLiteral(schema, option.Value, out var value)) Error("Invalid typed literal for markup option '" + option.Key + "'.");
                    else options[option.Key] = value!;
                }
                foreach (var option in contract.Options)
                    if (!options.ContainsKey(option.Key))
                    {
                        if (option.Value.Default is null) Error("Missing required markup option '" + option.Key + "'.");
                        else if (TryLiteral(option.Value, DefaultLiteral(option.Value.Type, option.Value.Default), out var value)) options[option.Key] = value!;
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

    // Written with Utf8JsonWriter rather than JsonSerializer over anonymous types, so a trimmed or NativeAOT host (the
    // Translations Editor's packages) writes the same bytes: JsonSerializer's default options write through the same
    // writer, unindented with the default encoder, in this property order and with nulls.
    /// <summary>
    /// The JSON of each exported message by its contract and content locales, from one export to the next of a session
    /// (Rmf2LinkState): a message whose contract and content locales are unchanged is not written again.
    /// </summary>
    internal sealed class ExportFragments
    {
        internal Dictionary<Rmf2MessageContractV5, (string?[]? ContentLocales, string Json)> Messages { get; } = new(ReferenceEqualityComparer.Instance);
        // The locale of each position of ContentLocales.
        internal string[] Tags { get; set; } = [];
    }

    /// <param name="previous">The fragments of the last export of a session, or null.</param>
    /// <param name="next">Receives this export's fragments, or null.</param>
    internal static string Export(IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts,
        IReadOnlyList<Rmf2MessageContractV5> messages, IReadOnlyList<Rmf2LocaleV5>? locales = null, ExportFragments? previous = null, ExportFragments? next = null)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteExport(writer, contracts, messages, locales, previous, next);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteExport(Utf8JsonWriter writer, IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts,
        IReadOnlyList<Rmf2MessageContractV5> messages, IReadOnlyList<Rmf2LocaleV5>? locales, ExportFragments? previous, ExportFragments? next)
    {
        writer.WriteStartObject();
        writer.WriteNumber("version", Rmf2ProjectV5.MarkupContractVersion);
        writer.WriteStartObject("contracts");
        foreach (var contract in contracts.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            // Contract v2 always writes the placement and child model; runtimes accept exactly v2.
            writer.WriteStartObject(contract.Key);
            writer.WriteString("kind", contract.Value.Standalone ? "standalone" : "paired");
            writer.WriteString("placement", contract.Value.Placement);
            writer.WriteString("children", contract.Value.Children);
            writer.WriteBoolean("interactive", contract.Value.Interactive);
            writer.WriteString("plainText", contract.Value.PlainText);
            writer.WriteStartObject("options");
            foreach (var option in contract.Value.Options.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject(option.Key);
                writer.WriteString("type", option.Value.Type);
                WriteStrings(writer, "values", option.Value.Values);
                writer.WriteString("default", option.Value.Default);
                writer.WriteBoolean("literalOnly", option.Value.LiteralOnly);
                if (option.Value.Type == "integer")
                {
                    writer.WriteNumber("minimum", option.Value.Minimum);
                    writer.WriteNumber("maximum", option.Value.Maximum);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        // A later message with the same key replaces an earlier one, as the sorted dictionary this replaced did.
        var messagesByKey = new SortedDictionary<string, Rmf2MessageContractV5>(StringComparer.Ordinal);
        foreach (var message in messages) messagesByKey[message.Key] = message;
        // Each locale's content locale by key, built once: a lookup per message and locale, not a scan of the locale.
        var resolvedByLocale = locales?.Select(locale =>
        {
            var byKey = new Dictionary<string, string>(locale.ResolvedResources.Count, StringComparer.Ordinal);
            foreach (var resource in locale.ResolvedResources) byKey.TryAdd(resource.Key, resource.ContentLocale);
            return (locale.Tag, ByKey: byKey);
        }).ToArray();
        string[] tags = locales?.Select(locale => locale.Tag).ToArray() ?? [];
        if (previous is not null && !previous.Tags.AsSpan().SequenceEqual(tags)) previous = null;
        if (next is not null) next.Tags = tags;
        // A session writes each message on its own and keeps its JSON; any other export writes it in place.
        bool fragments = previous is not null || next is not null;
        var fragmentBuffer = fragments ? new ArrayBufferWriter<byte>() : null;
        writer.WriteStartObject("messages");
        foreach (var message in messagesByKey.Values)
        {
            string?[]? contentLocales = null;
            if (resolvedByLocale is not null)
            {
                contentLocales = new string?[resolvedByLocale.Length];
                for (int index = 0; index < resolvedByLocale.Length; index++) contentLocales[index] = resolvedByLocale[index].ByKey.GetValueOrDefault(message.Key);
            }
            if (!fragments)
            {
                writer.WritePropertyName(message.Key);
                WriteMessage(writer, message, contentLocales);
                continue;
            }
            string json;
            if (previous is not null && previous.Messages.TryGetValue(message, out var cached) &&
                (cached.ContentLocales is null ? contentLocales is null : contentLocales is not null && cached.ContentLocales.AsSpan().SequenceEqual(contentLocales)))
                json = cached.Json;
            else
            {
                fragmentBuffer!.Clear();
                using (var fragment = new Utf8JsonWriter(fragmentBuffer)) WriteMessage(fragment, message, contentLocales);
                json = Encoding.UTF8.GetString(fragmentBuffer.WrittenSpan);
            }
            next?.Messages.TryAdd(message, (contentLocales, json));
            writer.WritePropertyName(message.Key);
            writer.WriteRawValue(json, skipInputValidation: true);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();

        void WriteMessage(Utf8JsonWriter target, Rmf2MessageContractV5 message, string?[]? contentLocales)
        {
            target.WriteStartObject();
            target.WriteStartObject("slots");
            foreach (var slot in message.Slots.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                target.WriteStartObject(slot.Key);
                target.WriteString("kind", slot.Value.Kind);
                target.WriteNumber("min", slot.Value.Min);
                target.WriteNumber("max", slot.Value.Max);
                target.WriteEndObject();
            }
            target.WriteEndObject();
            target.WriteBoolean("structured", message.Structured);
            // The content kind is part of the caller contract; skeletons are not, because
            // they change no generated API. Packs are checked against them at load time.
            if (contentLocales is null)
            {
                WriteStrings(target, "markup", message.MarkupNames);
                target.WriteString("content", message.Content);
            }
            else
            {
                var byTag = new SortedDictionary<string, string>(StringComparer.Ordinal);
                for (int index = 0; index < contentLocales.Length; index++)
                    if (contentLocales[index] is { } contentLocale) byTag[resolvedByLocale![index].Tag] = contentLocale;
                target.WriteStartObject("contentLocales");
                foreach (var pair in byTag) target.WriteString(pair.Key, pair.Value);
                target.WriteEndObject();
                target.WriteString("content", message.Content);
                WriteStrings(target, "skeletons", message.Skeletons);
            }
            target.WriteEndObject();
        }
    }

    internal static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static bool AcceptsType(string schema, string type) => schema switch
    { "number" => type is "int64" or "decimal", "integer" => type == "int64", "boolean" => type == "boolean", _ => type == "string" };

    private static Rmf2ValueV5 DefaultLiteral(string type, string text) => type is "number" or "integer" ? new("number-literal", text, text) : new("string-literal", text);

    private static bool TryLiteral(Rmf2MarkupOptionContractV5 schema, Rmf2ValueV5 value, out Rmf2ValueV5? result)
    {
        result = value;
        if (schema.Type == "number")
        {
            if (value.Kind != "number-literal" || !Rmf2DecimalV5.TryCanonicalize(value.Value, out string canonical)) return false;
            result = new("number-literal", canonical, canonical); return true;
        }
        // A quoted or unquoted literal; only canonical decimal text is accepted, so the
        // text is already canonical. Both spellings normalize to one number literal.
        if (schema.Type == "integer")
        {
            if (value.Kind is not ("number-literal" or "string-literal") || !Rmf2IntegerOption.Accepts(value.Value, schema.Minimum, schema.Maximum)) return false;
            result = new("number-literal", value.Value, value.Value); return true;
        }
        if (value.Kind != "string-literal") return false;
        return schema.Type switch { "string" => true, "boolean" => value.Value is "true" or "false", "enum" => schema.Values.Contains(value.Value, StringComparer.Ordinal), _ => false };
    }
}
