using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Tooling;

/// <summary>Preview facade for compiler and versioned authoring operations.</summary>
public static class TranslationsTooling
{
    /// <summary>Runs the deterministic compiler without introducing a runtime dependency.</summary>
    public static TranslationCompilation Compile(
        IEnumerable<TranslationSource> manifests,
        IEnumerable<TranslationSource> documents,
        TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default) =>
        TranslationCompiler.Compile(manifests, documents, options, cancellationToken);

    /// <summary>Builds the canonical bytes-first locale-pack-v2 artifacts for one successful v2 catalog.</summary>
    public static LocalePackV2BuildResult BuildLocalePackV2(TranslationCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success) throw new LocalePackBuildException("LOCALEPACKV2-COMPILATION", "Locale-pack-v2 build requires a successful compiler result.");
        if (compilation.Catalogs.Count != 1) throw new LocalePackBuildException("LOCALEPACKV2-CATALOG", "Locale-pack-v2 build requires exactly one compiled catalog.");
        CompiledTextCatalog catalog = compilation.Catalogs[0];
        if (catalog.MessageGrammarVersion != TranslationOutputRenderer.LocaleArtifactV2Version)
            throw new LocalePackBuildException("LOCALEPACKV2-GRAMMAR", "Locale-pack-v2 build requires message grammar version 2.");

        var documents = new List<TranslationGeneratedOutput>(catalog.Locales.Count);
        foreach (CompiledTextLocale locale in catalog.Locales.OrderBy(static item => item.Tag, StringComparer.Ordinal))
            documents.Add(TranslationOutputRenderer.RenderLocaleJson(catalog, locale.Tag));
        return new LocalePackV2BuildResult(documents);
    }

    /// <summary>Migrates one schema-v2 resource document to the canonical v3 MF2-subset envelope.</summary>
    public static SourceV3MigrationResult MigrateV2ToV3(ReadOnlyMemory<byte> source)
    {
        if (source.Length == 0) throw new SourceMigrationException("MIGV3-EMPTY", "A source document is required.");
        JsonNode? parsed;
        try { RejectDuplicateJsonProperties(source.Span); parsed = JsonNode.Parse(source.Span); }
        catch (JsonException exception) { throw new SourceMigrationException("MIGV3-MALFORMED", "The source document is not valid JSON.", exception); }
        if (parsed is not JsonObject root) throw new SourceMigrationException("MIGV3-ROOT", "The source document root must be an object.");
        if (!IsSchemaVersion2(root))
            throw new SourceMigrationException("MIGV3-UNSUPPORTED-SOURCE", "Only schema version 2 resource documents can be migrated to v3.");
        ValidateV2Document(root);

        var losses = new List<SourceMigrationLoss>();
        JsonObject migrated = MigrateDocument(root, losses, out int inputLeaves, out int structuredMessages);
        ValidateV3Document(migrated);
        byte[] document = JsonSerializer.SerializeToUtf8Bytes(migrated, ToolingJsonContext.Default.JsonObject);
        SourceV3MigrationReport report = new(losses, inputLeaves, structuredMessages);
        return new SourceV3MigrationResult(document, report);
    }

    /// <summary>Inspects a v2 source document without emitting a migrated document.</summary>
    public static SourceV3MigrationInspection InspectV2ToV3(ReadOnlyMemory<byte> source)
    {
        SourceV3MigrationResult migration = MigrateV2ToV3(source);
        return new SourceV3MigrationInspection(
            migration.Report.InputLeaves,
            migration.Report.StructuredMessages,
            migration.Report.Losses.Count,
            migration.Report.IsLossless,
            migration.Report.ToJson());
    }

    private static void RejectDuplicateJsonProperties(ReadOnlySpan<byte> source)
    {
        var scopes = new List<HashSet<string>?>();
        var reader = new Utf8JsonReader(source, new JsonReaderOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject) scopes.Add(new HashSet<string>(StringComparer.Ordinal));
            else if (reader.TokenType == JsonTokenType.StartArray) scopes.Add(null);
            else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray) scopes.RemoveAt(scopes.Count - 1);
            else if (reader.TokenType == JsonTokenType.PropertyName && scopes[^1] is HashSet<string> names && !names.Add(reader.GetString()!))
                throw new SourceMigrationException("MIGV3-DUPLICATE-MEMBER", "The source document contains a duplicate JSON member.");
        }
    }

    private static JsonObject MigrateDocument(JsonObject root, List<SourceMigrationLoss> losses, out int inputLeaves, out int structuredMessages)
    {
        var result = new JsonObject();
        result["$schema"] = "https://runic-artifex.eu/schemas/translations/resources-v3.schema.json";
        result["schemaVersion"] = 3;
        Copy(root, result, "catalog");
        Copy(root, result, "locale");
        Copy(root, result, "layer");
        if (root["resources"] is not JsonObject resources)
            throw new SourceMigrationException("MIGV3-RESOURCES", "A v2 resource document must contain an object-valued resources member.");
        int[] counts = new int[2];
        result["resources"] = MigrateGroup(resources, "/resources", losses, counts);
        inputLeaves = counts[0];
        structuredMessages = counts[1];
        return result;
    }

    private static JsonObject MigrateGroup(JsonObject source, string pointer, List<SourceMigrationLoss> losses, int[] counts)
    {
        var result = new JsonObject();
        foreach ((string key, JsonNode? value) in source.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (value is JsonValue) { counts[0]++; result[key] = value.DeepClone(); }
            else if (value is JsonObject child && child.ContainsKey("$value")) result[key] = MigrateLeaf(child, pointer + "/" + Escape(key), losses, counts);
            else if (value is JsonObject childGroup) result[key] = MigrateGroup(childGroup, pointer + "/" + Escape(key), losses, counts);
            else throw new SourceMigrationException("MIGV3-RESOURCE", "A resource must be a string, metadata leaf, or group.");
        }
        return result;
    }

    private static JsonObject MigrateLeaf(JsonObject source, string pointer, List<SourceMigrationLoss> losses, int[] counts)
    {
        var result = new JsonObject();
        JsonNode? value = source["$value"];
        counts[0]++;
        if (value is JsonObject structured)
        {
            counts[1]++;
            result["$value"] = new JsonObject { ["mf2"] = new JsonObject { ["profile"] = "runic-mf2-subset/1", ["ast"] = MigrateMessage(structured, pointer + "/$value", losses) } };
        }
        else if (value is not null) result["$value"] = value.DeepClone();
        else throw new SourceMigrationException("MIGV3-LEAF", "A metadata leaf is missing $value.");

        foreach (string name in new[] { "$description", "$since", "$deprecated", "$tags" }) Copy(source, result, name);
        if (source["$placeholders"] is JsonObject placeholders) result["$placeholders"] = MigrateInputs(placeholders, pointer + "/$placeholders", losses);
        return result;
    }

    private static JsonObject MigrateMessage(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        if (source["inputs"] is not JsonObject inputs || source["selectors"] is not JsonArray selectors || source["variants"] is not JsonArray variants)
            throw new SourceMigrationException("MIGV3-MESSAGE", "A v2 structured message must contain inputs, selectors, and variants.");
        var result = new JsonObject { ["astVersion"] = 3, ["profile"] = "runic-mf2-subset/1", ["inputs"] = MigrateInputs(inputs, pointer + "/inputs", losses) };
        var declarations = new JsonArray();
        if (source["declarations"] is JsonArray sourceDeclarations)
            foreach (JsonNode? declaration in sourceDeclarations) declarations.Add((JsonNode?)MigrateDeclaration(RequireObject(declaration, "MIGV3-DECLARATION"), pointer + "/declarations", losses));
        result["declarations"] = declarations;
        var migratedSelectors = new JsonArray();
        foreach (JsonNode? selector in selectors) migratedSelectors.Add((JsonNode?)MigrateSelector(RequireObject(selector, "MIGV3-SELECTOR")));
        result["selectors"] = migratedSelectors;
        var migratedVariants = new JsonArray();
        foreach (JsonNode? variant in variants) migratedVariants.Add((JsonNode?)MigrateVariant(RequireObject(variant, "MIGV3-VARIANT"), pointer + "/variants", losses));
        result["variants"] = migratedVariants;
        return result;
    }

    private static JsonObject MigrateInputs(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        var result = new JsonObject();
        foreach ((string name, JsonNode? node) in source.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            JsonObject descriptor = RequireObject(node, "MIGV3-INPUT");
            string type = RequireString(descriptor, "type", "MIGV3-INPUT");
            string format = descriptor["format"]?.GetValue<string>() ?? DefaultFormat(type);
            if (descriptor["format"] is null) losses.Add(new SourceMigrationLoss("MIGV3-DEFAULT-FORMAT-MATERIALIZED", pointer + "/" + Escape(name), "The v2 implicit input format was made explicit.", false));
            result[name] = new JsonObject { ["type"] = type, ["format"] = format };
        }
        return result;
    }

    private static JsonObject MigrateDeclaration(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        string name = RequireString(source, "name", "MIGV3-DECLARATION");
        string input = RequireString(source, "input", "MIGV3-DECLARATION");
        string function = RequireString(source, "function", "MIGV3-DECLARATION");
        return new JsonObject { ["name"] = name, ["function"] = function, ["operand"] = Operand("input", input), ["options"] = Options(source, pointer + "/" + Escape(name), losses) };
    }

    private static JsonObject MigrateSelector(JsonObject source) => new()
    {
        ["name"] = RequireString(source, "name", "MIGV3-SELECTOR"),
        ["operand"] = Operand("input", RequireString(source, "input", "MIGV3-SELECTOR")),
        ["function"] = RequireString(source, "function", "MIGV3-SELECTOR"),
    };

    private static JsonObject MigrateVariant(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        if (source["match"] is not JsonObject matches) throw new SourceMigrationException("MIGV3-VARIANT", "A v2 variant must contain an object-valued match member.");
        JsonNode? value = source["value"];
        if (value is null) throw new SourceMigrationException("MIGV3-VARIANT", "A v2 variant must contain a value member.");
        var normalizedMatches = new JsonObject();
        foreach ((string name, JsonNode? match) in matches.OrderBy(static entry => entry.Key, StringComparer.Ordinal)) normalizedMatches[name] = match?.DeepClone();
        return new JsonObject { ["matches"] = normalizedMatches, ["pattern"] = MigratePattern(value, pointer, losses) };
    }

    private static JsonArray MigratePattern(JsonNode source, string pointer, List<SourceMigrationLoss> losses)
    {
        var result = new JsonArray();
        if (source is JsonValue text) { result.Add((JsonNode?)new JsonObject { ["kind"] = "text", ["value"] = text.GetValue<string>() }); return result; }
        if (source is not JsonArray nodes) throw new SourceMigrationException("MIGV3-PATTERN", "A v2 pattern must be a string or array.");
        foreach (JsonNode? node in nodes)
        {
            if (node is JsonValue literal) { result.Add((JsonNode?)new JsonObject { ["kind"] = "text", ["value"] = literal.GetValue<string>() }); continue; }
            JsonObject expression = RequireObject(node, "MIGV3-PATTERN");
            if (expression["input"] is JsonValue input) result.Add((JsonNode?)new JsonObject { ["kind"] = "expression", ["operand"] = Operand("input", input.GetValue<string>()) });
            else if (expression["local"] is JsonValue local) result.Add((JsonNode?)new JsonObject { ["kind"] = "expression", ["operand"] = Operand("local", local.GetValue<string>()) });
            else if (expression["format"] is JsonObject format) result.Add((JsonNode?)new JsonObject { ["kind"] = "format", ["function"] = RequireString(format, "function", "MIGV3-FORMAT"), ["operand"] = Operand("input", RequireString(format, "input", "MIGV3-FORMAT")), ["options"] = Options(format, pointer, losses) });
            else if (expression["markup"] is JsonObject markup) result.Add((JsonNode?)MigrateMarkup(markup, pointer, losses));
            else throw new SourceMigrationException("MIGV3-PATTERN", "A v2 pattern node is not in the closed migration subset.");
        }
        return result;
    }

    private static JsonObject MigrateMarkup(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        if (source["children"] is not JsonArray children) throw new SourceMigrationException("MIGV3-MARKUP", "A v2 markup expression must contain children.");
        var attributes = new JsonObject();
        if (source["attributes"] is JsonObject sourceAttributes)
            foreach ((string name, JsonNode? value) in sourceAttributes.OrderBy(static entry => entry.Key, StringComparer.Ordinal)) attributes[name] = value?.DeepClone();
        return new JsonObject { ["kind"] = "markup", ["name"] = RequireString(source, "name", "MIGV3-MARKUP"), ["attributes"] = attributes, ["children"] = MigratePattern(children, pointer, losses) };
    }

    private static JsonObject Options(JsonObject source, string pointer, List<SourceMigrationLoss> losses)
    {
        var result = new JsonObject();
        foreach (string name in new[] { "format", "unit", "numeric" }) if (source[name] is not null) result[name] = source[name]!.DeepClone();
        if (RequireString(source, "function", "MIGV3-FORMAT") == "relativeTime" && result["numeric"] is null)
        {
            result["numeric"] = "always";
            losses.Add(new SourceMigrationLoss("MIGV3-DEFAULT-NUMERIC-MATERIALIZED", pointer, "The v2 implicit relative-time numeric option was made explicit.", false));
        }
        return result;
    }

    // This is the executable subset of the published v3 schema used as a final
    // migration gate. The source validator is intentionally separate so a future
    // migration change cannot accidentally emit an unchecked v3 shape.
    private static void ValidateV3Document(JsonObject root)
    {
        ValidateMembers(root, "v3 document", ["$schema", "schemaVersion", "catalog", "locale", "layer", "resources"], ["$schema", "schemaVersion", "catalog", "locale", "layer", "resources"]);
        if (RequireString(root, "$schema", "MIGV3-OUTPUT") != "https://runic-artifex.eu/schemas/translations/resources-v3.schema.json" || root["schemaVersion"]?.GetValue<int>() != 3)
            throw new SourceMigrationException("MIGV3-OUTPUT", "The migration did not emit the canonical v3 identity.");
        if (!IsCatalog(RequireString(root, "catalog", "MIGV3-OUTPUT")) || !IsCanonicalLocale(RequireString(root, "locale", "MIGV3-OUTPUT")) || !IsCatalog(RequireString(root, "layer", "MIGV3-OUTPUT")))
            throw new SourceMigrationException("MIGV3-OUTPUT", "The migrated v3 identity is invalid.");
        ValidateV3Group(RequireObject(root["resources"], "MIGV3-OUTPUT"));
    }

    private static void ValidateV3Group(JsonObject group)
    {
        foreach ((string name, JsonNode? value) in group)
        {
            if (!IsIdentifier(name)) throw new SourceMigrationException("MIGV3-OUTPUT", "A migrated resource name is invalid.");
            if (value is JsonValue scalar && IsString(scalar)) continue;
            JsonObject node = RequireObject(value, "MIGV3-OUTPUT");
            if (!node.ContainsKey("$value")) { ValidateV3Group(node); continue; }
            ValidateMembers(node, "v3 metadata leaf", ["$value", "$description", "$placeholders", "$since", "$deprecated", "$tags"], ["$value"]);
            if (node["$value"] is JsonObject envelope)
            {
                ValidateMembers(envelope, "MF2 envelope", ["mf2"], ["mf2"]);
                JsonObject mf2 = RequireObject(envelope["mf2"], "MIGV3-OUTPUT");
                ValidateMembers(mf2, "MF2 envelope", ["profile", "ast"], ["profile", "ast"]);
                if (RequireString(mf2, "profile", "MIGV3-OUTPUT") != "runic-mf2-subset/1") throw new SourceMigrationException("MIGV3-OUTPUT", "The MF2 profile is invalid.");
                ValidateV3Ast(RequireObject(mf2["ast"], "MIGV3-OUTPUT"));
            }
            else if (node["$value"] is not JsonValue valueText || !IsString(valueText)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 leaf value is invalid.");
            if (node["$placeholders"] is JsonObject placeholders) ValidateV3Inputs(placeholders);
        }
    }

    private static void ValidateV3Ast(JsonObject ast)
    {
        ValidateMembers(ast, "v3 MF2 AST", ["astVersion", "profile", "inputs", "declarations", "selectors", "variants"], ["astVersion", "profile", "inputs", "declarations", "selectors", "variants"]);
        if (ast["astVersion"]?.GetValue<int>() != 3 || RequireString(ast, "profile", "MIGV3-OUTPUT") != "runic-mf2-subset/1") throw new SourceMigrationException("MIGV3-OUTPUT", "The v3 AST version or profile is invalid.");
        JsonObject inputs = RequireObject(ast["inputs"], "MIGV3-OUTPUT");
        ValidateV3Inputs(inputs);
        JsonArray selectors = ast["selectors"] as JsonArray ?? throw new SourceMigrationException("MIGV3-OUTPUT", "The v3 selector list is invalid.");
        JsonArray variants = ast["variants"] as JsonArray ?? throw new SourceMigrationException("MIGV3-OUTPUT", "The v3 variant list is invalid.");
        if (selectors.Count > 16 || variants.Count is < 1 or > 256) throw Limit("The v3 AST exceeds a profile bound.");
        var inputTypes = inputs.ToDictionary(static pair => pair.Key, static pair => RequireString(RequireObject(pair.Value, "MIGV3-OUTPUT"), "type", "MIGV3-OUTPUT"), StringComparer.Ordinal);
        var localNames = new HashSet<string>(StringComparer.Ordinal);
        JsonArray declarations = ast["declarations"] as JsonArray ?? throw new SourceMigrationException("MIGV3-OUTPUT", "The v3 declaration list is invalid.");
        foreach (JsonNode? node in declarations)
        {
            JsonObject declaration = RequireObject(node, "MIGV3-OUTPUT");
            ValidateMembers(declaration, "v3 declaration", ["name", "function", "operand", "options"], ["name", "function", "operand", "options"]);
            string name = RequireString(declaration, "name", "MIGV3-OUTPUT");
            string function = RequireString(declaration, "function", "MIGV3-OUTPUT");
            string input = V3OperandInput(declaration["operand"], "MIGV3-OUTPUT");
            if (!IsIdentifier(name) || !localNames.Add(name) || !inputTypes.TryGetValue(input, out string? inputType) || !IsV3Function(function) || !FunctionMatches(function, inputType)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 declaration is invalid.");
            ValidateV3FunctionOptions(RequireObject(declaration["options"], "MIGV3-OUTPUT"), function, inputType);
        }
        var selectorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in selectors)
        {
            JsonObject selector = RequireObject(node, "MIGV3-OUTPUT");
            ValidateMembers(selector, "v3 selector", ["name", "operand", "function"], ["name", "operand", "function"]);
            string name = RequireString(selector, "name", "MIGV3-OUTPUT");
            string input = V3OperandInput(selector["operand"], "MIGV3-OUTPUT");
            string function = RequireString(selector, "function", "MIGV3-OUTPUT");
            if (!IsIdentifier(name) || !selectorNames.Add(name) || !inputTypes.TryGetValue(input, out string? inputType) || function is not ("literal" or "plural" or "ordinal") || function is "plural" or "ordinal" && inputType is not ("int64" or "decimal")) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 selector is invalid.");
        }
        foreach (JsonNode? node in variants)
        {
            JsonObject variant = RequireObject(node, "MIGV3-OUTPUT");
            ValidateMembers(variant, "v3 variant", ["matches", "pattern"], ["matches", "pattern"]);
            JsonObject matches = RequireObject(variant["matches"], "MIGV3-OUTPUT");
            if (matches.Count != selectorNames.Count || matches.Any(pair => !selectorNames.Contains(pair.Key) || !IsString(pair.Value) || pair.Value!.GetValue<string>().Length == 0)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 variant match is invalid.");
            int nodes = 0;
            ValidateV3Nodes(variant["pattern"] as JsonArray ?? throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 pattern is invalid."), inputTypes, localNames, 0, ref nodes);
        }
    }

    private static void ValidateV3Inputs(JsonObject inputs)
    {
        if (inputs.Count > 32) throw Limit("A v3 message has more than 32 inputs.");
        foreach ((string name, JsonNode? node) in inputs)
        {
            JsonObject descriptor = RequireObject(node, "MIGV3-OUTPUT");
            ValidateMembers(descriptor, "v3 input", ["type", "format"], ["type", "format"]);
            string type = RequireString(descriptor, "type", "MIGV3-OUTPUT");
            string format = RequireString(descriptor, "format", "MIGV3-OUTPUT");
            if (!IsIdentifier(name) || !IsFormatAllowed(type, format)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 input descriptor is invalid.");
        }
    }

    private static string V3OperandInput(JsonNode? node, string code)
    {
        JsonObject operand = RequireObject(node, code);
        ValidateMembers(operand, "v3 operand", ["kind", "name"], ["kind", "name"]);
        if (RequireString(operand, "kind", code) != "input") throw new SourceMigrationException(code, "This v3 migration only emits input operands here.");
        return RequireString(operand, "name", code);
    }

    private static void ValidateV3Options(JsonObject options)
    {
        ValidateMembers(options, "v3 options", ["format", "unit", "numeric"], []);
        if (options["format"] is not null && !IsString(options["format"]) || options["unit"] is not null && (!IsString(options["unit"]) || options["unit"]!.GetValue<string>() is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year")) || options["numeric"] is not null && (!IsString(options["numeric"]) || options["numeric"]!.GetValue<string>() is not ("always" or "auto"))) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 option is invalid.");
    }

    private static void ValidateV3FunctionOptions(JsonObject options, string function, string inputType)
    {
        ValidateV3Options(options);
        if (function == "relativeTime")
        {
            if (options["unit"] is not JsonValue || !IsString(options["unit"]) || options["numeric"] is not JsonValue || !IsString(options["numeric"]))
                throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 relative-time format requires unit and numeric options.");
            return;
        }
        if (options["unit"] is not null || options["numeric"] is not null) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 scalar format has relative-time options.");
        string format = options["format"]?.GetValue<string>() ?? DefaultFormat(inputType);
        if (!IsFormatAllowed(inputType, format)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 format option is incompatible with its input type.");
    }

    private static void ValidateV3Nodes(JsonArray nodes, IReadOnlyDictionary<string, string> inputs, ISet<string> locals, int depth, ref int count)
    {
        if (depth > 16) throw Limit("A v3 pattern exceeds the markup depth limit.");
        foreach (JsonNode? node in nodes)
        {
            if (++count > 4096) throw Limit("A v3 pattern exceeds the node limit.");
            JsonObject item = RequireObject(node, "MIGV3-OUTPUT");
            string kind = RequireString(item, "kind", "MIGV3-OUTPUT");
            if (kind == "text") { ValidateMembers(item, "v3 text node", ["kind", "value"], ["kind", "value"]); if (RequireString(item, "value", "MIGV3-OUTPUT").Length > 65536) throw Limit("A v3 text node exceeds the profile limit."); }
            else if (kind == "expression") { ValidateMembers(item, "v3 expression node", ["kind", "operand"], ["kind", "operand"]); JsonObject operand = RequireObject(item["operand"], "MIGV3-OUTPUT"); ValidateMembers(operand, "v3 operand", ["kind", "name"], ["kind", "name"]); if (RequireString(operand, "kind", "MIGV3-OUTPUT") == "input" ? !inputs.ContainsKey(RequireString(operand, "name", "MIGV3-OUTPUT")) : !locals.Contains(RequireString(operand, "name", "MIGV3-OUTPUT"))) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 expression references an unknown name."); }
            else if (kind == "format") { ValidateMembers(item, "v3 format node", ["kind", "function", "operand", "options"], ["kind", "function", "operand", "options"]); string function = RequireString(item, "function", "MIGV3-OUTPUT"); string input = V3OperandInput(item["operand"], "MIGV3-OUTPUT"); if (!IsV3Function(function) || !inputs.TryGetValue(input, out string? inputType) || !FunctionMatches(function, inputType)) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 format function is invalid."); ValidateV3FunctionOptions(RequireObject(item["options"], "MIGV3-OUTPUT"), function, inputType); }
            else if (kind == "markup") { ValidateMembers(item, "v3 markup node", ["kind", "name", "attributes", "children"], ["kind", "name", "attributes", "children"]); if (!IsIdentifier(RequireString(item, "name", "MIGV3-OUTPUT"))) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 markup name is invalid."); JsonObject attributes = RequireObject(item["attributes"], "MIGV3-OUTPUT"); if (attributes.Any(pair => !IsIdentifier(pair.Key) || !IsString(pair.Value))) throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 markup attribute is invalid."); ValidateV3Nodes(item["children"] as JsonArray ?? throw new SourceMigrationException("MIGV3-OUTPUT", "V3 markup children are invalid."), inputs, locals, depth + 1, ref count); }
            else throw new SourceMigrationException("MIGV3-OUTPUT", "A v3 node kind is invalid.");
        }
    }

    private static bool IsV3Function(string value) => value is "string" or "integer" or "number" or "date" or "time" or "datetime" or "uuid" or "relativeTime";

    private static void ValidateV2Document(JsonObject root)
    {
        ValidateMembers(root, "document", ["$schema", "schemaVersion", "catalog", "locale", "layer", "resources"], ["schemaVersion", "catalog", "locale", "layer", "resources"]);
        if (root["$schema"] is not null && !IsString(root["$schema"])) throw new SourceMigrationException("MIGV3-DOCUMENT", "The optional source schema URI must be a string.");
        string catalog = RequireString(root, "catalog", "MIGV3-DOCUMENT");
        string locale = RequireString(root, "locale", "MIGV3-DOCUMENT");
        string layer = RequireString(root, "layer", "MIGV3-DOCUMENT");
        if (!IsCatalog(catalog) || !IsCanonicalLocale(locale) || !IsCatalog(layer))
            throw new SourceMigrationException("MIGV3-DOCUMENT", "The v2 catalog, locale, or layer identity is invalid.");
        if (root["resources"] is not JsonObject resources)
            throw new SourceMigrationException("MIGV3-RESOURCES", "A v2 resource document must contain an object-valued resources member.");
        ValidateGroup(resources);
    }

    private static void ValidateGroup(JsonObject group)
    {
        foreach ((string name, JsonNode? value) in group)
        {
            if (!IsIdentifier(name)) throw new SourceMigrationException("MIGV3-RESOURCE", "A resource name is invalid.");
            if (value is JsonValue text && IsString(text)) continue;
            if (value is not JsonObject objectValue) throw new SourceMigrationException("MIGV3-RESOURCE", "A resource must be a string, metadata leaf, or group.");
            if (objectValue.ContainsKey("$value")) ValidateLeaf(objectValue);
            else
            {
                if (objectValue.Any(static member => member.Key.StartsWith('$')))
                    throw new SourceMigrationException("MIGV3-UNKNOWN-MEMBER", "A resource group contains an unknown metadata member.");
                ValidateGroup(objectValue);
            }
        }
    }

    private static void ValidateLeaf(JsonObject leaf)
    {
        ValidateMembers(leaf, "metadata leaf", ["$value", "$description", "$placeholders", "$since", "$deprecated", "$tags"], ["$value"]);
        if (leaf["$value"] is JsonValue value && IsString(value)) { }
        else if (leaf["$value"] is JsonObject message) ValidateMessage(message);
        else throw new SourceMigrationException("MIGV3-LEAF", "A metadata leaf value must be a string or structured v2 message.");
        foreach (string member in new[] { "$description", "$since", "$deprecated" }) if (leaf[member] is not null && !IsString(leaf[member])) throw new SourceMigrationException("MIGV3-LEAF", "Metadata text must be a string.");
        if (leaf["$tags"] is JsonArray tags)
        {
            var uniqueTags = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonNode? tag in tags)
            {
                if (!IsString(tag) || !uniqueTags.Add(tag!.GetValue<string>()))
                    throw new SourceMigrationException("MIGV3-LEAF", "Metadata tags must be unique strings.");
            }
        }
        else if (leaf["$tags"] is not null) throw new SourceMigrationException("MIGV3-LEAF", "Metadata tags must be an array.");
        if (leaf["$placeholders"] is JsonObject placeholders) ValidateInputs(placeholders);
        else if (leaf["$placeholders"] is not null) throw new SourceMigrationException("MIGV3-INPUT", "Placeholder declarations must be an object.");
    }

    private static void ValidateMessage(JsonObject message)
    {
        ValidateMembers(message, "structured message", ["inputs", "declarations", "selectors", "variants"], ["inputs", "selectors", "variants"]);
        if (message["inputs"] is not JsonObject inputs) throw new SourceMigrationException("MIGV3-INPUT", "Message inputs must be an object.");
        ValidateInputs(inputs);
        if (message["declarations"] is JsonArray declarations)
            foreach (JsonNode? declaration in declarations) ValidateDeclaration(RequireObject(declaration, "MIGV3-DECLARATION"));
        else if (message["declarations"] is not null) throw new SourceMigrationException("MIGV3-DECLARATION", "Declarations must be an array.");
        if (message["selectors"] is not JsonArray selectors) throw new SourceMigrationException("MIGV3-SELECTOR", "Selectors must be an array.");
        foreach (JsonNode? selector in selectors) ValidateSelector(RequireObject(selector, "MIGV3-SELECTOR"));
        if (message["variants"] is not JsonArray variants) throw new SourceMigrationException("MIGV3-VARIANT", "Variants must be an array.");
        foreach (JsonNode? variant in variants) ValidateVariant(RequireObject(variant, "MIGV3-VARIANT"));
        ValidateV2MessageConstraints(inputs, message["declarations"] as JsonArray, selectors, variants);
    }

    private static void ValidateInputs(JsonObject inputs)
    {
        foreach ((string name, JsonNode? node) in inputs)
        {
            if (!IsIdentifier(name)) throw new SourceMigrationException("MIGV3-INPUT", "An input name is invalid.");
            JsonObject descriptor = RequireObject(node, "MIGV3-INPUT");
            ValidateMembers(descriptor, "input", ["type", "format"], ["type"]);
            string inputType = RequireString(descriptor, "type", "MIGV3-INPUT");
            if (inputType is not ("string" or "bool" or "int64" or "decimal" or "date" or "time" or "instant" or "uuid")) throw new SourceMigrationException("MIGV3-INPUT", "An input type is unsupported.");
            if (descriptor["format"] is not null)
            {
                if (!IsString(descriptor["format"])) throw new SourceMigrationException("MIGV3-INPUT", "An input format must be a string.");
                if (!IsFormatAllowed(inputType, RequireString(descriptor, "format", "MIGV3-INPUT"))) throw new SourceMigrationException("MIGV3-INPUT", "An input format is incompatible with its type.");
            }
        }
    }

    private static void ValidateDeclaration(JsonObject declaration)
    {
        ValidateMembers(declaration, "declaration", ["name", "input", "function", "format", "unit", "numeric"], ["name", "input", "function"]);
        RequireString(declaration, "name", "MIGV3-DECLARATION"); RequireString(declaration, "input", "MIGV3-DECLARATION"); RequireString(declaration, "function", "MIGV3-DECLARATION");
        ValidateStringOptions(declaration, "MIGV3-DECLARATION");
    }

    private static void ValidateSelector(JsonObject selector)
    {
        ValidateMembers(selector, "selector", ["name", "input", "function"], ["name", "input", "function"]);
        RequireString(selector, "name", "MIGV3-SELECTOR"); RequireString(selector, "input", "MIGV3-SELECTOR"); RequireString(selector, "function", "MIGV3-SELECTOR");
    }

    private static void ValidateVariant(JsonObject variant)
    {
        ValidateMembers(variant, "variant", ["match", "value"], ["match", "value"]);
        if (variant["match"] is not JsonObject matches) throw new SourceMigrationException("MIGV3-VARIANT", "Variant matches must be an object.");
        foreach ((string name, JsonNode? match) in matches) if (!IsIdentifier(name) || !IsString(match)) throw new SourceMigrationException("MIGV3-VARIANT", "Variant matches must have identifier names and string values.");
        ValidatePattern(variant["value"]!);
    }

    private static void ValidatePattern(JsonNode pattern)
    {
        if (pattern is JsonValue text && IsString(text)) return;
        if (pattern is not JsonArray nodes) throw new SourceMigrationException("MIGV3-PATTERN", "A pattern must be a string or array.");
        foreach (JsonNode? node in nodes)
        {
            if (node is JsonValue literal && IsString(literal)) continue;
            JsonObject expression = RequireObject(node, "MIGV3-PATTERN");
            int forms = (expression["input"] is null ? 0 : 1) + (expression["local"] is null ? 0 : 1) + (expression["format"] is null ? 0 : 1) + (expression["markup"] is null ? 0 : 1);
            if (forms != 1) throw new SourceMigrationException("MIGV3-PATTERN", "A pattern node must contain exactly one supported expression.");
            if (expression["input"] is not null) { ValidateMembers(expression, "input expression", ["input"], ["input"]); if (!IsString(expression["input"])) throw new SourceMigrationException("MIGV3-PATTERN", "An input expression must name an input."); }
            else if (expression["local"] is not null) { ValidateMembers(expression, "local expression", ["local"], ["local"]); if (!IsString(expression["local"])) throw new SourceMigrationException("MIGV3-PATTERN", "A local expression must name a declaration."); }
            else if (expression["format"] is JsonObject format) { ValidateMembers(expression, "format expression", ["format"], ["format"]); ValidateMembers(format, "format", ["input", "function", "format", "unit", "numeric"], ["input", "function"]); RequireString(format, "input", "MIGV3-FORMAT"); RequireString(format, "function", "MIGV3-FORMAT"); ValidateStringOptions(format, "MIGV3-FORMAT"); }
            else if (expression["markup"] is JsonObject markup)
            {
                ValidateMembers(expression, "markup expression", ["markup"], ["markup"]);
                ValidateMembers(markup, "markup", ["name", "attributes", "children"], ["name", "children"]);
                RequireString(markup, "name", "MIGV3-MARKUP");
                if (markup["attributes"] is JsonObject attributes)
                {
                    foreach ((string name, JsonNode? attribute) in attributes)
                        if (!IsIdentifier(name) || !IsString(attribute)) throw new SourceMigrationException("MIGV3-MARKUP", "Markup attributes require identifier names and string values.");
                }
                else if (markup["attributes"] is not null) throw new SourceMigrationException("MIGV3-MARKUP", "Markup attributes must be an object.");
                ValidatePattern(markup["children"]!);
            }
            else throw new SourceMigrationException("MIGV3-PATTERN", "A pattern expression is malformed.");
        }
    }

    private static void ValidateV2MessageConstraints(JsonObject inputs, JsonArray? declarations, JsonArray selectors, JsonArray variants)
    {
        if (inputs.Count > 32) throw Limit("A message has more than 32 inputs.");
        var inputTypes = inputs.ToDictionary(static pair => pair.Key, static pair => RequireString(RequireObject(pair.Value, "MIGV3-INPUT"), "type", "MIGV3-INPUT"), StringComparer.Ordinal);
        var localNames = new HashSet<string>(StringComparer.Ordinal);
        if (declarations is not null)
        {
            foreach (JsonNode? node in declarations)
            {
                JsonObject declaration = RequireObject(node, "MIGV3-DECLARATION");
                string name = RequireString(declaration, "name", "MIGV3-DECLARATION");
                string input = RequireString(declaration, "input", "MIGV3-DECLARATION");
                string function = RequireString(declaration, "function", "MIGV3-DECLARATION");
                if (!IsIdentifier(name) || !localNames.Add(name) || !inputTypes.TryGetValue(input, out string? inputType) || !FunctionMatches(function, inputType))
                    throw new SourceMigrationException("MIGV3-DECLARATION", "A declaration name, input, or function is invalid.");
                ValidateFunctionOptions(declaration, function, inputType, "MIGV3-DECLARATION");
            }
        }

        if (selectors.Count > 16) throw Limit("A message has more than 16 selectors.");
        var selectorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in selectors)
        {
            JsonObject selector = RequireObject(node, "MIGV3-SELECTOR");
            string name = RequireString(selector, "name", "MIGV3-SELECTOR");
            string input = RequireString(selector, "input", "MIGV3-SELECTOR");
            string function = RequireString(selector, "function", "MIGV3-SELECTOR");
            if (!IsIdentifier(name) || !selectorNames.Add(name) || !inputTypes.TryGetValue(input, out string? inputType) || function is not ("literal" or "plural" or "ordinal"))
                throw new SourceMigrationException("MIGV3-SELECTOR", "A selector name, input, or function is invalid.");
            if (function is "plural" or "ordinal" && inputType is not ("int64" or "decimal"))
                throw new SourceMigrationException("MIGV3-SELECTOR", "Plural selectors require an integer or decimal input.");
        }

        if (variants.Count is < 1) throw new SourceMigrationException("MIGV3-VARIANT", "A message requires at least one variant.");
        if (variants.Count > 256) throw Limit("A message has more than 256 variants.");
        foreach (JsonNode? node in variants)
        {
            JsonObject variant = RequireObject(node, "MIGV3-VARIANT");
            JsonObject matches = RequireObject(variant["match"], "MIGV3-VARIANT");
            if (matches.Count != selectorNames.Count || matches.Any(pair => !selectorNames.Contains(pair.Key) || !IsString(pair.Value) || pair.Value!.GetValue<string>().Length == 0))
                throw new SourceMigrationException("MIGV3-VARIANT", "Variant matches must be nonempty strings for every selector.");
            int nodes = 0;
            ValidatePatternBounds(variant["value"]!, inputTypes, localNames, 0, ref nodes);
        }
    }

    private static void ValidatePatternBounds(JsonNode pattern, IReadOnlyDictionary<string, string> inputs, ISet<string> locals, int markupDepth, ref int nodes)
    {
        if (pattern is JsonValue text)
        {
            if (!IsString(text) || text.GetValue<string>().Length > 65536) throw Limit("A pattern text node exceeds the v3 profile limit.");
            return;
        }
        JsonArray values = pattern as JsonArray ?? throw new SourceMigrationException("MIGV3-PATTERN", "A pattern must be a string or array.");
        foreach (JsonNode? item in values)
        {
            if (item is JsonValue literal)
            {
                if (!IsString(literal) || literal.GetValue<string>().Length > 65536) throw Limit("A pattern text node exceeds the v3 profile limit.");
                if (++nodes > 4096) throw Limit("A pattern exceeds the v3 profile node limit.");
                continue;
            }
            JsonObject expression = RequireObject(item, "MIGV3-PATTERN");
            if (++nodes > 4096) throw Limit("A pattern exceeds the v3 profile node limit.");
            if (expression["input"] is JsonValue input)
            {
                if (!inputs.ContainsKey(input.GetValue<string>())) throw new SourceMigrationException("MIGV3-PATTERN", "An expression references an undeclared input.");
            }
            else if (expression["local"] is JsonValue local)
            {
                if (!locals.Contains(local.GetValue<string>())) throw new SourceMigrationException("MIGV3-PATTERN", "An expression references an undeclared local.");
            }
            else if (expression["format"] is JsonObject format)
            {
                string inputName = RequireString(format, "input", "MIGV3-FORMAT");
                string function = RequireString(format, "function", "MIGV3-FORMAT");
                if (!inputs.TryGetValue(inputName, out string? inputType) || !FunctionMatches(function, inputType))
                    throw new SourceMigrationException("MIGV3-FORMAT", "A format expression input or function is invalid.");
                ValidateFunctionOptions(format, function, inputType, "MIGV3-FORMAT");
            }
            else if (expression["markup"] is JsonObject markup)
            {
                if (markupDepth >= 16) throw Limit("A pattern exceeds the v3 profile markup depth limit.");
                ValidatePatternBounds(markup["children"]!, inputs, locals, markupDepth + 1, ref nodes);
            }
        }
    }

    private static void ValidateFunctionOptions(JsonObject value, string function, string inputType, string code)
    {
        if (function == "relativeTime")
        {
            if (inputType is not ("int64" or "decimal") || value["unit"] is not JsonValue unit || value["numeric"] is not null && !IsString(value["numeric"]))
                throw new SourceMigrationException(code, "Relative-time options are invalid.");
            if (unit.GetValue<string>() is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year"))
                throw new SourceMigrationException(code, "Relative-time requires a supported unit.");
            return;
        }
        if (value["unit"] is not null || value["numeric"] is not null) throw new SourceMigrationException(code, "Scalar formats cannot declare relative-time options.");
        string format = value["format"]?.GetValue<string>() ?? DefaultFormat(inputType);
        if (!IsFormatAllowed(inputType, format)) throw new SourceMigrationException(code, "A function format is incompatible with its input type.");
    }

    private static bool FunctionMatches(string function, string inputType) => function switch
    {
        "string" => inputType == "string",
        "integer" => inputType == "int64",
        "number" => inputType == "decimal",
        "date" => inputType == "date",
        "time" => inputType == "time",
        "datetime" => inputType == "instant",
        "uuid" => inputType == "uuid",
        "relativeTime" => inputType is "int64" or "decimal",
        _ => false,
    };

    private static SourceMigrationException Limit(string message) => new("MIGV3-LIMIT", message);

    private static void ValidateStringOptions(JsonObject value, string code)
    {
        foreach (string option in new[] { "format", "unit", "numeric" }) if (value[option] is not null && !IsString(value[option])) throw new SourceMigrationException(code, "A format option must be a string.");
    }

    private static void ValidateMembers(JsonObject value, string subject, string[] allowed, string[] required)
    {
        foreach ((string name, JsonNode? _) in value) if (Array.IndexOf(allowed, name) < 0) throw new SourceMigrationException("MIGV3-UNKNOWN-MEMBER", "The " + subject + " contains unknown member '" + name + "'.");
        foreach (string name in required) if (!value.ContainsKey(name)) throw new SourceMigrationException("MIGV3-MISSING-MEMBER", "The " + subject + " is missing required member '" + name + "'.");
    }

    private static bool IsString(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<string>(out _);
    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || (!char.IsAsciiLetter(value[0]) && value[0] != '_')) return false;
        for (int index = 1; index < value.Length; index++) if (!char.IsAsciiLetterOrDigit(value[index]) && value[index] != '_') return false;
        return true;
    }

    private static bool IsCatalog(string value)
    {
        if (value.Length == 0 || value[0] is < 'a' or > 'z') return false;
        for (int index = 1; index < value.Length; index++) if (!(value[index] is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')) return false;
        return true;
    }

    private static bool IsFormatAllowed(string inputType, string format) => inputType switch
    {
        "string" => format == "none",
        "bool" => format == "lower",
        "int64" => format is "plain" or "grouped",
        "decimal" => format is "plain" or "grouped" or "fixed0" or "fixed1" or "fixed2" or "fixed3" or "fixed4" or "fixed5" or "fixed6" or "percent0" or "percent1" or "percent2" or "percent3" or "percent4",
        "date" or "instant" => format is "iso" or "short" or "medium" or "long",
        "time" => format is "iso" or "short" or "medium",
        "uuid" => format is "d" or "n",
        _ => false,
    };

    private static bool IsCanonicalLocale(string value)
    {
        if (value.Length is < 2 or > 63 || value[0] == '-' || value[^1] == '-') return false;
        string[] parts = value.Split('-');
        if (parts[0].Length is < 2 or > 8 || !parts[0].All(static character => character is >= 'a' and <= 'z')) return false;
        bool extension = false;
        for (int index = 1; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length is < 1 or > 8 || !part.All(char.IsAsciiLetterOrDigit)) return false;
            bool letters = part.All(char.IsAsciiLetter);
            bool digits = part.All(char.IsAsciiDigit);
            if (part.Length == 1) { if (!part.All(IsAsciiLower)) return false; extension = true; }
            else if (!extension && part.Length == 4 && letters)
            {
                if (!IsAsciiUpper(part[0]) || !part.Substring(1).All(IsAsciiLower)) return false;
            }
            else if (!extension && part.Length == 2 && letters)
            {
                if (!part.All(IsAsciiUpper)) return false;
            }
            else if (!extension && part.Length == 3 && digits) { }
            else if (part.Any(IsAsciiUpper)) return false;
        }
        return true;
    }

    private static bool IsAsciiLower(char value) => value is >= 'a' and <= 'z';
    private static bool IsAsciiUpper(char value) => value is >= 'A' and <= 'Z';

    private static JsonObject Operand(string kind, string name) => new() { ["kind"] = kind, ["name"] = name };
    private static bool IsSchemaVersion2(JsonObject root)
    {
        try { return root["schemaVersion"]?.GetValue<int>() == 2; }
        catch (InvalidOperationException) { return false; }
    }
    private static void Copy(JsonObject source, JsonObject destination, string name) { if (source[name] is JsonNode value) destination[name] = value.DeepClone(); }
    private static JsonObject RequireObject(JsonNode? value, string code) => value as JsonObject ?? throw new SourceMigrationException(code, "An expected object is missing or malformed.");
    private static string RequireString(JsonObject value, string name, string code)
    {
        if (value[name] is JsonValue scalar && scalar.TryGetValue<string>(out string? text) && text is not null) return text;
        throw new SourceMigrationException(code, "An expected string member is missing or malformed.");
    }
    private static string Escape(string segment) => segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static string DefaultFormat(string inputType) => inputType switch { "string" => "none", "bool" => "lower", "int64" or "decimal" => "plain", "date" or "time" or "instant" => "iso", "uuid" => "d", _ => "none" };
}

/// <summary>Canonical result of one v2-to-v3 source migration.</summary>
public sealed class SourceV3MigrationResult
{
    internal SourceV3MigrationResult(byte[] documentBytes, SourceV3MigrationReport report) { DocumentBytes = documentBytes; Report = report; }
    /// <summary>Canonical compact UTF-8 v3 document bytes.</summary>
    public byte[] DocumentBytes { get; }
    /// <summary>Deterministic loss report.</summary>
    public SourceV3MigrationReport Report { get; }
}

/// <summary>Canonical locale-pack-v2 artifacts built from one compiled catalog.</summary>
public sealed class LocalePackV2BuildResult
{
    internal LocalePackV2BuildResult(IReadOnlyList<TranslationGeneratedOutput> documents) => Documents = documents;
    /// <summary>One deterministic locale-pack-v2 JSON artifact per declared locale.</summary>
    public IReadOnlyList<TranslationGeneratedOutput> Documents { get; }
}

/// <summary>Stable failure for locale-pack-v2 build preconditions.</summary>
public sealed class LocalePackBuildException : Exception
{
    internal LocalePackBuildException(string code, string message) : base(message) => Code = code;
    /// <summary>Stable machine-readable locale pack build rejection ID.</summary>
    public string Code { get; }
}

/// <summary>Machine-readable migration report. Informational materializations do not imply semantic loss.</summary>
public sealed class SourceV3MigrationReport
{
    private readonly IReadOnlyList<SourceMigrationLoss> _losses;
    internal SourceV3MigrationReport(IReadOnlyList<SourceMigrationLoss> losses, int inputLeaves, int structuredMessages)
    {
        _losses = losses.OrderBy(static item => item.Location, StringComparer.Ordinal).ThenBy(static item => item.Code, StringComparer.Ordinal).ToArray();
        InputLeaves = inputLeaves; StructuredMessages = structuredMessages;
    }
    /// <summary>Number of source leaves inspected.</summary>
    public int InputLeaves { get; }
    /// <summary>Number of structured messages migrated.</summary>
    public int StructuredMessages { get; }
    /// <summary>Canonical ordered migration events.</summary>
    public IReadOnlyList<SourceMigrationLoss> Losses => _losses;
    /// <summary>True when no event reports a semantic loss.</summary>
    public bool IsLossless => !_losses.Any(static item => item.SemanticLoss);
    /// <summary>Renders the report as canonical compact JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(new SourceV3MigrationReportJson(1, 2, 3, IsLossless, _losses), ToolingJsonContext.Default.SourceV3MigrationReportJson);
}

internal sealed record SourceV3MigrationReportJson(int ReportVersion, int InputSchemaVersion, int OutputSchemaVersion, bool IsLossless, IReadOnlyList<SourceMigrationLoss> Losses);

/// <summary>One deterministic migration event.</summary>
public sealed class SourceMigrationLoss
{
    internal SourceMigrationLoss(string code, string location, string message, bool semanticLoss) { Code = code; Location = location; Message = message; SemanticLoss = semanticLoss; }
    public string Code { get; }
    public string Location { get; }
    public string Message { get; }
    public bool SemanticLoss { get; }
}

/// <summary>Stable inspection result for build and editor callers.</summary>
public sealed class SourceV3MigrationInspection
{
    internal SourceV3MigrationInspection(int inputLeaves, int structuredMessages, int eventCount, bool isLossless, string reportJson) { InputLeaves = inputLeaves; StructuredMessages = structuredMessages; EventCount = eventCount; IsLossless = isLossless; ReportJson = reportJson; }
    public int InputLeaves { get; }
    public int StructuredMessages { get; }
    public int EventCount { get; }
    public bool IsLossless { get; }
    public string ReportJson { get; }
}

/// <summary>Stable failure for unsupported or malformed migration input.</summary>
public sealed class SourceMigrationException : Exception
{
    internal SourceMigrationException(string code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    /// <summary>Stable machine-readable migration rejection ID.</summary>
    public string Code { get; }
}
