using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

internal sealed record CatalogImportSource(string Locale, string Path);

internal sealed record CatalogImportRequest(
    IReadOnlyList<CatalogImportSource> Sources,
    string OutputPath,
    string Catalog,
    string DefaultLocale,
    string CodeNamespace,
    string ClassName,
    bool DryRun,
    bool AllowPartial,
    string Format);

internal sealed record CatalogImportResult(
    IReadOnlyList<ToolArtifact> Artifacts,
    byte[] Report,
    IReadOnlyList<CatalogImportDiagnostic> Diagnostics,
    TranslationCompilation? Compilation,
    bool CanWrite);

internal sealed record CatalogImportDiagnostic(
    string Severity,
    string Code,
    string Path,
    string? Key,
    string Message);

internal static partial class CatalogImporter
{
    private const int MaximumInputBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    internal static CatalogImportResult Import(CatalogImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<CatalogImportDiagnostic>();
        var sources = ReadSources(request, diagnostics);
        var reports = new List<KeyReport>();
        var documents = new List<ImportedDocument>();
        for (int index = 0; index < sources.Count; index++)
        {
            ImportedSource source = sources[index];
            documents.Add(ParseSource(source, request.Format, request.AllowPartial, reports, diagnostics));
        }

        RemoveNonPortableSubset(documents, reports, diagnostics, request.AllowPartial);
        if (documents.Count != 0 && documents.All(static document => document.Entries.Count == 0))
        {
            diagnostics.Add(new CatalogImportDiagnostic(
                "error", "RIM0011", string.Empty, null,
                "No losslessly convertible messages remain; no output can be written."));
        }

        string defaultLocale = CanonicalLocale(request.DefaultLocale, "--default-locale", diagnostics);
        if (documents.Count != 0 && !documents.Any(document => string.Equals(document.Locale, defaultLocale, StringComparison.Ordinal)))
        {
            diagnostics.Add(new CatalogImportDiagnostic(
                "error", "RIM0002", string.Empty, null,
                $"Default locale '{defaultLocale}' has no --source mapping."));
        }

        byte[] manifest = RenderManifest(request, defaultLocale, documents);
        var artifacts = new List<ToolArtifact>
        {
            new($"{request.Catalog}.catalog.json", manifest),
        };
        var compilerDocuments = new List<TranslationSource>();
        for (int index = 0; index < documents.Count; index++)
        {
            ImportedDocument document = documents[index];
            byte[] bytes = RenderDocument(request.Catalog, document);
            string path = $"{request.Catalog}.{document.Locale}.json";
            artifacts.Add(new ToolArtifact(path, bytes));
            compilerDocuments.Add(new TranslationSource(path, bytes));
        }

        TranslationCompilation? compilation = null;
        if (documents.Count != 0 && !string.IsNullOrEmpty(defaultLocale))
        {
            compilation = TranslationCompiler.Compile(
                [new TranslationSource($"{request.Catalog}.catalog.json", manifest)],
                compilerDocuments);
        }

        bool importHasErrors = diagnostics.Any(static diagnostic => diagnostic.Severity == "error");
        bool compilerSucceeded = compilation?.Success == true;
        bool canWrite = !importHasErrors && compilerSucceeded;
        IReadOnlyList<string> outputPaths = artifacts
            .Select(static artifact => artifact.RelativePath)
            .Append("runic-import-report.json")
            .Order(StringComparer.Ordinal)
            .ToArray();
        byte[] report = RenderReport(request, documents, reports, diagnostics, compilation, outputPaths);
        artifacts.Add(new ToolArtifact("runic-import-report.json", report));
        artifacts.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return new CatalogImportResult(artifacts, report, diagnostics, compilation, canWrite);
    }

    private static List<ImportedSource> ReadSources(
        CatalogImportRequest request,
        List<CatalogImportDiagnostic> diagnostics)
    {
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var sources = new List<ImportedSource>(request.Sources.Count);
        var locales = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        for (int index = 0; index < request.Sources.Count; index++)
        {
            CatalogImportSource configured = request.Sources[index];
            string locale = CanonicalLocale(configured.Locale, "--source", diagnostics);
            string fullPath = Path.GetFullPath(configured.Path, currentDirectory);
            string displayPath = NormalizePath(Path.GetRelativePath(currentDirectory, fullPath));
            if (!locales.Add(locale))
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0002", displayPath, null,
                    $"Locale '{locale}' is mapped more than once."));
                continue;
            }

            if (!paths.Add(fullPath))
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0002", displayPath, null,
                    "The same source file is mapped to more than one locale."));
                continue;
            }

            if (!File.Exists(fullPath))
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0001", displayPath, null,
                    "Source file does not exist."));
                continue;
            }

            var information = new FileInfo(fullPath);
            if (information.Length > MaximumInputBytes)
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0001", displayPath, null,
                    $"Source file exceeds the {MaximumInputBytes}-byte import limit."));
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
                Utf8.GetCharCount(bytes);
            }
            catch (DecoderFallbackException)
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0001", displayPath, null,
                    "Source file is not valid UTF-8."));
                continue;
            }

            sources.Add(new ImportedSource(locale, displayPath, bytes));
        }

        sources.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Locale, right.Locale));
        return sources;
    }

    private static ImportedDocument ParseSource(
        ImportedSource source,
        string configuredFormat,
        bool allowPartial,
        List<KeyReport> reports,
        List<CatalogImportDiagnostic> diagnostics)
    {
        var entries = new SortedDictionary<string, ImportedEntry>(StringComparer.Ordinal);
        var mappedKeys = new HashSet<string>(StringComparer.Ordinal);
        bool inlang = configuredFormat == "inlang";
        try
        {
            using JsonDocument document = JsonDocument.Parse(source.Bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                diagnostics.Add(new CatalogImportDiagnostic(
                    "error", "RIM0003", source.Path, null,
                    "A catalog source must have a JSON object root."));
            }
            else
            {
                if (configuredFormat == "auto") inlang = DetectInlang(document.RootElement);
                WalkObject(document.RootElement, Array.Empty<string>(), source, allowPartial, entries, mappedKeys, reports, diagnostics, ref inlang, isRoot: true);
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add(new CatalogImportDiagnostic(
                "error", "RIM0003", source.Path, null,
                $"Invalid JSON at line {(exception.LineNumber ?? 0) + 1}, byte {(exception.BytePositionInLine ?? 0) + 1}: {exception.Message}"));
        }

        return new ImportedDocument(source.Locale, source.Path, inlang ? "inlang-message-format" : "json", entries);
    }

    private static void WalkObject(
        JsonElement value,
        IReadOnlyList<string> parent,
        ImportedSource source,
        bool allowPartial,
        SortedDictionary<string, ImportedEntry> entries,
        HashSet<string> mappedKeys,
        List<KeyReport> reports,
        List<CatalogImportDiagnostic> diagnostics,
        ref bool inlang,
        bool isRoot)
    {
        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!propertyNames.Add(property.Name))
            {
                AddEntryDiagnostic(source, allowPartial, reports, diagnostics, string.Join('.', parent.Append(property.Name)), null,
                    "RIM0004", "Duplicate JSON property names cannot be converted deterministically.");
                continue;
            }

            if (isRoot && property.NameEquals("$schema"))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    diagnostics.Add(new CatalogImportDiagnostic(
                        "error", "RIM0005", source.Path, "$schema",
                        "The source format marker '$schema' must be a string."));
                }
                continue;
            }

            if (property.Name.StartsWith('$'))
            {
                AddEntryDiagnostic(source, allowPartial, reports, diagnostics, string.Join('.', parent.Append(property.Name)), null,
                    "RIM0005", $"Metadata member '{property.Name}' has no lossless mapping.");
                continue;
            }

            string[] segments = parent.Concat(SplitKey(property.Name)).ToArray();
            string sourceKey = string.Join('.', parent.Append(property.Name));
            string[] mappedSegments = segments.Select(SanitizeSegment).ToArray();
            string mappedKey = string.Join('.', mappedSegments);
            bool changed = !segments.SequenceEqual(mappedSegments, StringComparer.Ordinal) || property.Name.Contains('.', StringComparison.Ordinal);
            if (!mappedKeys.Add(mappedKey) && property.Value.ValueKind is not JsonValueKind.Object)
            {
                AddEntryDiagnostic(source, allowPartial, reports, diagnostics, sourceKey, mappedKey,
                    "RIM0006", $"Key mapping collides with another source key at '{mappedKey}'.");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (property.Value.EnumerateObject().Any(static child => child.Name.StartsWith('$')))
                {
                    AddEntryDiagnostic(source, allowPartial, reports, diagnostics, sourceKey, mappedKey,
                        "RIM0005", "A metadata-bearing object cannot be converted as a plain nested resource group.");
                    continue;
                }
                WalkObject(property.Value, segments, source, allowPartial, entries, mappedKeys, reports, diagnostics, ref inlang, isRoot: false);
                continue;
            }

            JsonObject? message;
            string? contract;
            string? errorCode;
            string? error;
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                message = inlang
                    ? ConvertPattern(property.Value.GetString()!, new PatternContext(), out contract, out errorCode, out error)
                    : ConvertLiteralPattern(property.Value.GetString()!, out contract, out errorCode, out error);
            }
            else if (property.Value.ValueKind == JsonValueKind.Array)
            {
                if (!inlang)
                {
                    message = null;
                    contract = null;
                    errorCode = "RIM0005";
                    error = "JSON arrays are not part of the conventional catalog subset; use --format inlang only for inlang message files.";
                }
                else
                {
                    message = ConvertComplexMessage(property.Value, out contract, out errorCode, out error);
                }
            }
            else
            {
                message = null;
                contract = null;
                errorCode = "RIM0005";
                error = $"JSON {property.Value.ValueKind.ToString().ToLowerInvariant()} values are not losslessly convertible; expected a string, nested object, or supported inlang variant array.";
            }

            if (message is null)
            {
                AddEntryDiagnostic(source, allowPartial, reports, diagnostics, sourceKey, mappedKey,
                    errorCode ?? "RIM0005", error ?? "Message is outside the supported lossless subset.");
                continue;
            }

            entries[mappedKey] = new ImportedEntry(mappedSegments, message, contract!);
            reports.Add(new KeyReport(source.Locale, source.Path, sourceKey, mappedKey, changed, "converted", Array.Empty<string>()));
        }
    }

    private static JsonObject ConvertLiteralPattern(
        string pattern,
        out string? contract,
        out string? errorCode,
        out string? error)
    {
        var message = BuildMessage(new PatternContext(), Array.Empty<JsonObject>(), [new JsonObject
        {
            ["match"] = new JsonObject(),
            ["value"] = new JsonArray((JsonNode?)pattern),
        }]);
        contract = ContractSignature(message);
        errorCode = null;
        error = null;
        return new JsonObject { ["$value"] = message };
    }

    private static JsonObject? ConvertPattern(
        string pattern,
        PatternContext context,
        out string? contract,
        out string? errorCode,
        out string? error)
    {
        int position = 0;
        List<JsonNode?>? nodes = ParseNodes(pattern, ref position, null, context, out errorCode, out error);
        if (nodes is null)
        {
            contract = null;
            return null;
        }

        JsonObject message = BuildMessage(context, Array.Empty<JsonObject>(), [new JsonObject
        {
            ["match"] = new JsonObject(),
            ["value"] = new JsonArray(nodes.ToArray()),
        }]);
        contract = ContractSignature(message);
        return new JsonObject { ["$value"] = message };
    }

    private static JsonObject? ConvertComplexMessage(
        JsonElement array,
        out string? contract,
        out string? errorCode,
        out string? error)
    {
        contract = null;
        errorCode = "RIM0008";
        error = null;
        if (array.GetArrayLength() != 1 || array[0].ValueKind != JsonValueKind.Object)
        {
            error = "A supported inlang complex message must be an array containing exactly one variant descriptor object.";
            return null;
        }

        JsonElement descriptor = array[0];
        if (!HasOnlyProperties(descriptor, ["declarations", "selectors", "match"], out string? duplicate, out string? unknown))
        {
            errorCode = duplicate is null ? "RIM0005" : "RIM0004";
            error = duplicate is null
                ? $"Message metadata or member '{unknown}' has no lossless Runic mapping."
                : $"Duplicate member '{duplicate}' cannot be converted deterministically.";
            return null;
        }

        if (!descriptor.TryGetProperty("declarations", out JsonElement declarations) || declarations.ValueKind != JsonValueKind.Array ||
            !descriptor.TryGetProperty("selectors", out JsonElement selectors) || selectors.ValueKind != JsonValueKind.Array ||
            !descriptor.TryGetProperty("match", out JsonElement matches) || matches.ValueKind != JsonValueKind.Object)
        {
            error = "A supported inlang complex message requires array 'declarations', array 'selectors', and object 'match' members.";
            return null;
        }

        var context = new PatternContext();
        var selectorDeclarations = new Dictionary<string, SelectorDeclaration>(StringComparer.Ordinal);
        var declarationNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement declarationElement in declarations.EnumerateArray())
        {
            if (declarationElement.ValueKind != JsonValueKind.String)
            {
                error = "inlang declarations must be strings.";
                return null;
            }

            string declaration = declarationElement.GetString()!;
            Match input = InputDeclarationRegex().Match(declaration);
            if (input.Success)
            {
                string declaredInputName = input.Groups[1].Value;
                if (!declarationNames.Add(declaredInputName))
                {
                    error = $"Declaration '{declaredInputName}' is duplicated.";
                    return null;
                }
                if (!context.AddInput(declaredInputName, "untyped", out error)) return null;
                continue;
            }

            Match local = LocalDeclarationRegex().Match(declaration);
            if (!local.Success)
            {
                error = $"Declaration '{declaration}' uses unsupported syntax or options.";
                return null;
            }

            string localName = local.Groups[1].Value;
            string inputName = local.Groups[2].Value;
            string function = local.Groups[3].Value;
            if (!declarationNames.Add(localName))
            {
                error = $"Declaration '{localName}' is duplicated.";
                return null;
            }
            if (function is "plural" or "ordinal")
            {
                if (!context.AddInput(inputName, "int64", out error)) return null;
                if (!selectorDeclarations.TryAdd(localName, new SelectorDeclaration(localName, inputName, function)))
                {
                    error = $"Declaration '{localName}' is duplicated.";
                    return null;
                }
            }
            else if (TryFormat(function, out string type, out string runicFunction))
            {
                if (!context.AddInput(inputName, type, out error)) return null;
                if (!context.LocalFormats.TryAdd(localName, new FormatDeclaration(localName, inputName, runicFunction)))
                {
                    error = $"Declaration '{localName}' is duplicated.";
                    return null;
                }
            }
            else
            {
                error = $"Formatter '{function}' has no exact mapping in the supported subset.";
                return null;
            }
        }

        var runicSelectors = new List<JsonObject>();
        var selectorNames = new List<string>();
        foreach (JsonElement selectorElement in selectors.EnumerateArray())
        {
            if (selectorElement.ValueKind != JsonValueKind.String || !IdentifierRegex().IsMatch(selectorElement.GetString()!))
            {
                error = "Selectors must be identifier strings.";
                return null;
            }

            string selectorName = selectorElement.GetString()!;
            if (selectorNames.Contains(selectorName, StringComparer.Ordinal))
            {
                error = $"Selector '{selectorName}' is duplicated.";
                return null;
            }

            selectorNames.Add(selectorName);
            if (selectorDeclarations.TryGetValue(selectorName, out SelectorDeclaration? declared))
            {
                runicSelectors.Add(new JsonObject
                {
                    ["name"] = declared.Name,
                    ["input"] = declared.Input,
                    ["function"] = declared.Function,
                });
            }
            else if (context.LocalFormats.ContainsKey(selectorName))
            {
                error = $"Formatted local '{selectorName}' cannot be used as a selector without losing its formatter semantics.";
                return null;
            }
            else
            {
                if (!context.AddInput(selectorName, "string", out error)) return null;
                runicSelectors.Add(new JsonObject
                {
                    ["name"] = selectorName,
                    ["input"] = selectorName,
                    ["function"] = "literal",
                });
            }
        }

        foreach (string declaredSelector in selectorDeclarations.Keys)
        {
            if (!selectorNames.Contains(declaredSelector, StringComparer.Ordinal))
            {
                error = $"Selector local '{declaredSelector}' is declared but not present in selectors.";
                return null;
            }
        }

        var variants = new List<JsonObject>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        bool catchAll = false;
        foreach (JsonProperty matchProperty in matches.EnumerateObject())
        {
            if (!signatures.Add(matchProperty.Name))
            {
                errorCode = "RIM0004";
                error = $"Variant match '{matchProperty.Name}' is duplicated.";
                return null;
            }

            if (matchProperty.Value.ValueKind != JsonValueKind.String)
            {
                error = $"Variant '{matchProperty.Name}' is not a string pattern.";
                return null;
            }

            JsonObject? runicMatch = ParseMatch(matchProperty.Name, selectorNames, out bool isCatchAll, out error);
            if (runicMatch is null) return null;
            catchAll |= isCatchAll;
            int position = 0;
            List<JsonNode?>? nodes = ParseNodes(matchProperty.Value.GetString()!, ref position, null, context, out errorCode, out error);
            if (nodes is null) return null;
            variants.Add(new JsonObject
            {
                ["match"] = runicMatch,
                ["value"] = new JsonArray(nodes.ToArray()),
            });
        }

        if (variants.Count == 0 || !catchAll)
        {
            errorCode = "RIM0008";
            error = "Variants require an explicit all-'other' catch-all match.";
            return null;
        }

        JsonObject message = BuildMessage(context, runicSelectors, variants);
        contract = ContractSignature(message);
        errorCode = null;
        error = null;
        return new JsonObject { ["$value"] = message };
    }

    private static List<JsonNode?>? ParseNodes(
        string pattern,
        ref int position,
        string? closingTag,
        PatternContext context,
        out string? errorCode,
        out string? error)
    {
        errorCode = "RIM0007";
        error = null;
        var nodes = new List<JsonNode?>();
        var text = new StringBuilder();
        while (position < pattern.Length)
        {
            char current = pattern[position];
            if (current == '\\')
            {
                if (position + 1 >= pattern.Length || pattern[position + 1] is not ('{' or '}' or '\\'))
                {
                    error = "Only \\{, \\}, and \\\\ escapes are supported in imported inlang patterns.";
                    return null;
                }

                text.Append(pattern[position + 1]);
                position += 2;
                continue;
            }

            if (current == '}')
            {
                error = "An unescaped '}' occurs outside an expression.";
                return null;
            }

            if (current != '{')
            {
                text.Append(current);
                position++;
                continue;
            }

            int end = pattern.IndexOf('}', position + 1);
            if (end < 0)
            {
                error = "An expression or markup placeholder is missing its closing '}'.";
                return null;
            }

            FlushText(nodes, text);
            string token = pattern[(position + 1)..end];
            position = end + 1;
            if (token.Length != 0 && token[0] == '/')
            {
                string tag = token[1..];
                if (closingTag is null || !string.Equals(tag, closingTag, StringComparison.Ordinal))
                {
                    error = $"Markup closing tag '{tag}' is unbalanced.";
                    return null;
                }

                return nodes;
            }

            if (token.Length != 0 && token[0] == '#')
            {
                bool standalone = token[^1] == '/';
                string tag = standalone ? token[1..^1] : token[1..];
                if (!IdentifierRegex().IsMatch(tag))
                {
                    error = "Markup with options, attributes, or non-identifier names is not in the lossless subset.";
                    return null;
                }

                List<JsonNode?> children = [];
                if (!standalone)
                {
                    List<JsonNode?>? parsed = ParseNodes(pattern, ref position, tag, context, out errorCode, out error);
                    if (parsed is null) return null;
                    children = parsed;
                }

                nodes.Add(new JsonObject
                {
                    ["markup"] = new JsonObject
                    {
                        ["name"] = tag,
                        ["children"] = new JsonArray(children.ToArray()),
                    },
                });
                continue;
            }

            Match expression = ExpressionRegex().Match(token);
            if (!expression.Success)
            {
                error = $"Expression '{{{token}}}' uses unsupported syntax or formatter options.";
                return null;
            }

            string name = expression.Groups[1].Value;
            string function = expression.Groups[2].Value;
            if (context.LocalFormats.TryGetValue(name, out FormatDeclaration? local))
            {
                if (function.Length != 0)
                {
                    error = $"Local '{name}' cannot be annotated again in a pattern.";
                    return null;
                }

                nodes.Add(new JsonObject { ["local"] = name });
                continue;
            }

            if (function.Length == 0)
            {
                if (!context.Inputs.ContainsKey(name) && !context.AddInput(name, "string", out error)) return null;
                nodes.Add(new JsonObject { ["input"] = name });
                continue;
            }

            if (!TryFormat(function, out string type, out string runicFunction))
            {
                error = $"Inline formatter '{function}' has no exact mapping in the supported subset.";
                return null;
            }

            if (!context.AddInput(name, type, out error)) return null;
            nodes.Add(new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["input"] = name,
                    ["function"] = runicFunction,
                },
            });
        }

        FlushText(nodes, text);
        if (closingTag is not null)
        {
            error = $"Markup tag '{closingTag}' is not closed.";
            return null;
        }

        errorCode = null;
        return nodes;
    }

    private static JsonObject BuildMessage(
        PatternContext context,
        IReadOnlyList<JsonObject> selectors,
        IReadOnlyList<JsonObject> variants)
    {
        var inputs = new JsonObject();
        foreach (KeyValuePair<string, string> input in context.Inputs)
        {
            inputs[input.Key] = new JsonObject { ["type"] = input.Value == "untyped" ? "string" : input.Value };
        }

        var declarations = new JsonArray();
        foreach (FormatDeclaration local in context.LocalFormats.Values.OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            declarations.Add((JsonNode)new JsonObject
            {
                ["name"] = local.Name,
                ["input"] = local.Input,
                ["function"] = local.Function,
            });
        }

        return new JsonObject
        {
            ["inputs"] = inputs,
            ["declarations"] = declarations,
            ["selectors"] = new JsonArray(selectors.Select(static selector => (JsonNode?)selector).ToArray()),
            ["variants"] = new JsonArray(variants.Select(static variant => (JsonNode?)variant).ToArray()),
        };
    }

    private static JsonObject? ParseMatch(
        string source,
        List<string> selectors,
        out bool catchAll,
        out string? error)
    {
        catchAll = false;
        error = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] parts = source.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int index = 0; index < parts.Length; index++)
        {
            int equals = parts[index].IndexOf('=');
            if (equals <= 0 || equals == parts[index].Length - 1)
            {
                error = $"Variant match '{source}' must use selector=value pairs separated by commas.";
                return null;
            }

            string name = parts[index][..equals].Trim();
            string value = parts[index][(equals + 1)..].Trim();
            if (!selectors.Contains(name, StringComparer.Ordinal) || !values.TryAdd(name, value))
            {
                error = $"Variant match '{source}' names an unknown or duplicate selector '{name}'.";
                return null;
            }
        }

        if (values.Count != selectors.Count)
        {
            error = $"Variant match '{source}' must name every selector.";
            return null;
        }

        var result = new JsonObject();
        catchAll = true;
        for (int index = 0; index < selectors.Count; index++)
        {
            string selector = selectors[index];
            string value = values[selector];
            string mapped = value == "other" ? "*" : value;
            catchAll &= mapped == "*";
            result[selector] = mapped;
        }

        return result;
    }

    private static void RemoveNonPortableSubset(
        IReadOnlyList<ImportedDocument> documents,
        List<KeyReport> reports,
        List<CatalogImportDiagnostic> diagnostics,
        bool allowPartial)
    {
        if (documents.Count == 0) return;
        string[] keys = reports
            .Where(static report => report.RunicKey is not null)
            .Select(static report => report.RunicKey!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        for (int keyIndex = 0; keyIndex < keys.Length; keyIndex++)
        {
            string key = keys[keyIndex];
            ImportedEntry[] present = documents
                .Select(document => document.Entries.TryGetValue(key, out ImportedEntry? entry) ? entry : null)
                .Where(static entry => entry is not null)
                .Cast<ImportedEntry>()
                .ToArray();
            bool complete = present.Length == documents.Count;
            bool compatible = complete && present.Select(static entry => entry.Contract).Distinct(StringComparer.Ordinal).Count() == 1;
            if (complete && compatible) continue;

            string code = complete ? "RIM0010" : "RIM0009";
            string message = complete
                ? $"Message '{key}' has different input or selector contracts between locales."
                : $"Message '{key}' is not losslessly convertible in every locale.";
            diagnostics.Add(new CatalogImportDiagnostic(
                allowPartial ? "warning" : "error", code, string.Empty, key,
                message + (allowPartial ? " It was omitted from the partial output." : " Use --allow-partial to omit it from every locale.")));
            foreach (ImportedDocument document in documents) document.Entries.Remove(key);
            for (int reportIndex = 0; reportIndex < reports.Count; reportIndex++)
            {
                if (string.Equals(reports[reportIndex].RunicKey, key, StringComparison.Ordinal) && reports[reportIndex].Status == "converted")
                {
                    reports[reportIndex] = reports[reportIndex] with { Status = "omitted", DiagnosticCodes = [code] };
                }
            }
        }
    }

    private static byte[] RenderManifest(
        CatalogImportRequest request,
        string defaultLocale,
        IReadOnlyList<ImportedDocument> documents)
    {
        var locales = new JsonArray();
        foreach (ImportedDocument document in documents.OrderBy(static document => document.Locale, StringComparer.Ordinal))
        {
            var locale = new JsonObject { ["tag"] = document.Locale };
            if (!string.Equals(document.Locale, defaultLocale, StringComparison.Ordinal)) locale["fallback"] = defaultLocale;
            locales.Add((JsonNode)locale);
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["catalog"] = request.Catalog,
            ["code"] = new JsonObject
            {
                ["namespace"] = request.CodeNamespace,
                ["className"] = request.ClassName,
                ["visibility"] = "public",
            },
            ["defaultLocale"] = defaultLocale,
            ["locales"] = locales,
            ["layers"] = new JsonArray(new JsonObject { ["name"] = "base", ["priority"] = 0 }),
            ["validation"] = new JsonObject
            {
                ["translationCompleteness"] = "error",
                ["extraLocaleKeys"] = "error",
                ["emptyValues"] = "allow",
            },
        };
        return JsonBytes(root);
    }

    private static byte[] RenderDocument(string catalog, ImportedDocument document)
    {
        var resources = new JsonObject();
        foreach (ImportedEntry entry in document.Entries.Values)
        {
            JsonObject group = resources;
            for (int index = 0; index < entry.Segments.Length - 1; index++)
            {
                if (group[entry.Segments[index]] is not JsonObject child)
                {
                    child = new JsonObject();
                    group[entry.Segments[index]] = child;
                }

                group = child;
            }

            group[entry.Segments[^1]] = entry.Value.DeepClone();
        }

        var root = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["catalog"] = catalog,
            ["locale"] = document.Locale,
            ["layer"] = "base",
            ["resources"] = resources,
        };
        return JsonBytes(root);
    }

    private static byte[] RenderReport(
        CatalogImportRequest request,
        IReadOnlyList<ImportedDocument> documents,
        IReadOnlyList<KeyReport> reports,
        IReadOnlyList<CatalogImportDiagnostic> diagnostics,
        TranslationCompilation? compilation,
        IReadOnlyList<string> outputPaths)
    {
        var sourceArray = new JsonArray();
        foreach (ImportedDocument document in documents)
        {
            sourceArray.Add((JsonNode)new JsonObject
            {
                ["locale"] = document.Locale,
                ["path"] = document.SourcePath,
                ["format"] = document.Format,
            });
        }

        var keyArray = new JsonArray();
        foreach (KeyReport report in reports
            .OrderBy(static report => report.Locale, StringComparer.Ordinal)
            .ThenBy(static report => report.SourceKey, StringComparer.Ordinal))
        {
            keyArray.Add((JsonNode)new JsonObject
            {
                ["locale"] = report.Locale,
                ["sourcePath"] = report.SourcePath,
                ["sourceKey"] = report.SourceKey,
                ["runicKey"] = report.RunicKey,
                ["mappingChanged"] = report.MappingChanged,
                ["status"] = report.Status,
                ["diagnosticCodes"] = new JsonArray(report.DiagnosticCodes.Select(static code => (JsonNode?)code).ToArray()),
            });
        }

        var diagnosticArray = new JsonArray();
        foreach (CatalogImportDiagnostic diagnostic in diagnostics
            .OrderBy(static diagnostic => diagnostic.Path, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Key, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal))
        {
            diagnosticArray.Add((JsonNode)new JsonObject
            {
                ["severity"] = diagnostic.Severity,
                ["code"] = diagnostic.Code,
                ["path"] = diagnostic.Path,
                ["key"] = diagnostic.Key,
                ["message"] = diagnostic.Message,
            });
        }

        var compilerDiagnostics = new JsonArray();
        if (compilation is not null)
        {
            foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
            {
                compilerDiagnostics.Add((JsonNode)new JsonObject
                {
                    ["severity"] = diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning",
                    ["code"] = diagnostic.Id,
                    ["path"] = diagnostic.Location.Path,
                    ["line"] = diagnostic.Location.Line,
                    ["column"] = diagnostic.Location.Column,
                    ["message"] = diagnostic.Message,
                });
            }
        }

        var root = new JsonObject
        {
            ["reportVersion"] = 1,
            ["mode"] = request.DryRun ? "dry-run" : "write",
            ["oneWay"] = true,
            ["losslessSubset"] = "strings, identifier inputs, exact built-in formatters, simple semantic markup, and inlang selector variants without options or metadata",
            ["catalog"] = request.Catalog,
            ["defaultLocale"] = request.DefaultLocale,
            ["allowPartial"] = request.AllowPartial,
            ["sources"] = sourceArray,
            ["keys"] = keyArray,
            ["diagnostics"] = diagnosticArray,
            ["compiler"] = new JsonObject
            {
                ["success"] = compilation?.Success == true,
                ["diagnostics"] = compilerDiagnostics,
            },
            ["outputs"] = new JsonArray(outputPaths.Select(static path => (JsonNode?)path).ToArray()),
        };
        return JsonBytes(root);
    }

    private static void AddEntryDiagnostic(
        ImportedSource source,
        bool allowPartial,
        List<KeyReport> reports,
        List<CatalogImportDiagnostic> diagnostics,
        string sourceKey,
        string? mappedKey,
        string code,
        string message)
    {
        string severity = allowPartial ? "warning" : "error";
        diagnostics.Add(new CatalogImportDiagnostic(severity, code, source.Path, sourceKey, message));
        reports.Add(new KeyReport(
            source.Locale, source.Path, sourceKey, mappedKey,
            mappedKey is not null && !string.Equals(sourceKey, mappedKey, StringComparison.Ordinal),
            "rejected", [code]));
    }

    private static bool HasOnlyProperties(
        JsonElement value,
        IReadOnlyList<string> allowed,
        out string? duplicate,
        out string? unknown)
    {
        duplicate = null;
        unknown = null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                duplicate = property.Name;
                return false;
            }

            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                unknown = property.Name;
                return false;
            }
        }

        return true;
    }

    private static bool DetectInlang(JsonElement value)
    {
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (property.NameEquals("$schema") || property.Value.ValueKind == JsonValueKind.Array) return true;
            if (property.Value.ValueKind == JsonValueKind.Object && DetectInlang(property.Value)) return true;
        }

        return false;
    }

    private static string ContractSignature(JsonObject message)
    {
        var contract = new JsonObject
        {
            ["inputs"] = message["inputs"]!.DeepClone(),
            ["declarations"] = message["declarations"]!.DeepClone(),
            ["selectors"] = message["selectors"]!.DeepClone(),
        };
        return contract.ToJsonString();
    }

    private static bool TryFormat(string source, out string type, out string function)
    {
        (type, function) = source switch
        {
            "string" => ("string", "string"),
            "number" => ("decimal", "number"),
            "integer" => ("int64", "integer"),
            "date" => ("date", "date"),
            "time" => ("time", "time"),
            "datetime" => ("instant", "datetime"),
            _ => (string.Empty, string.Empty),
        };
        return type.Length != 0;
    }

    private static void FlushText(List<JsonNode?> nodes, StringBuilder text)
    {
        if (text.Length == 0) return;
        nodes.Add(text.ToString());
        text.Clear();
    }

    private static string[] SplitKey(string value) => value.Split('.', StringSplitOptions.None);

    private static string SanitizeSegment(string value)
    {
        if (value.Length == 0) return "_";
        var builder = new StringBuilder(value.Length + 1);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            bool valid = index == 0
                ? character == '_' || char.IsAsciiLetter(character)
                : character == '_' || char.IsAsciiLetterOrDigit(character);
            builder.Append(valid ? character : '_');
        }

        if (builder[0] == '$') builder.Insert(0, '_');
        return builder.ToString();
    }

    private static string CanonicalLocale(
        string value,
        string option,
        List<CatalogImportDiagnostic> diagnostics)
    {
        string normalized = value.Replace('_', '-');
        if (!LocaleRegex().IsMatch(normalized))
        {
            diagnostics.Add(new CatalogImportDiagnostic(
                "error", "RIM0002", string.Empty, null,
                $"{option} locale '{value}' is not a supported BCP 47-style tag."));
            return normalized;
        }

        string[] segments = normalized.Split('-');
        segments[0] = segments[0].ToLowerInvariant();
        for (int index = 1; index < segments.Length; index++)
        {
            segments[index] = segments[index].Length switch
            {
                2 => segments[index].ToUpperInvariant(),
                4 => char.ToUpperInvariant(segments[index][0]) + segments[index][1..].ToLowerInvariant(),
                _ => segments[index].ToLowerInvariant(),
            };
        }

        return string.Join('-', segments);
    }

    private static byte[] JsonBytes(JsonNode node)
    {
        string text = node.ToJsonString(IndentedJson) + "\n";
        return Utf8.GetBytes(text);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex("^[A-Za-z]{2,8}(?:-[A-Za-z0-9]{1,8})*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocaleRegex();

    [GeneratedRegex("^\\s*input\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex InputDeclarationRegex();

    [GeneratedRegex("^\\s*local\\s+([A-Za-z_][A-Za-z0-9_]*)\\s*=\\s*([A-Za-z_][A-Za-z0-9_]*)\\s*:\\s*([A-Za-z][A-Za-z0-9]*)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LocalDeclarationRegex();

    [GeneratedRegex("^\\s*([A-Za-z_][A-Za-z0-9_]*)(?:\\s*:\\s*([A-Za-z][A-Za-z0-9]*))?\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex ExpressionRegex();

    private sealed record ImportedSource(string Locale, string Path, byte[] Bytes);

    private sealed record ImportedEntry(string[] Segments, JsonObject Value, string Contract);

    private sealed record ImportedDocument(
        string Locale,
        string SourcePath,
        string Format,
        SortedDictionary<string, ImportedEntry> Entries);

    private sealed record KeyReport(
        string Locale,
        string SourcePath,
        string SourceKey,
        string? RunicKey,
        bool MappingChanged,
        string Status,
        IReadOnlyList<string> DiagnosticCodes);

    private sealed record FormatDeclaration(string Name, string Input, string Function);

    private sealed record SelectorDeclaration(string Name, string Input, string Function);

    private sealed class PatternContext
    {
        internal SortedDictionary<string, string> Inputs { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, FormatDeclaration> LocalFormats { get; } = new(StringComparer.Ordinal);

        internal bool AddInput(string name, string type, out string? error)
        {
            error = null;
            if (!IdentifierRegex().IsMatch(name))
            {
                error = $"Input '{name}' is not a Runic identifier.";
                return false;
            }

            if (Inputs.TryGetValue(name, out string? existing) &&
                existing != "untyped" && type != "untyped" &&
                !string.Equals(existing, type, StringComparison.Ordinal))
            {
                error = $"Input '{name}' is inferred as both '{existing}' and '{type}'.";
                return false;
            }

            if (!Inputs.TryGetValue(name, out existing) || existing == "untyped") Inputs[name] = type;
            return true;
        }
    }
}
