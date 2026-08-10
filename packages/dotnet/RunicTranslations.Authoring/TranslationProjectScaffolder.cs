using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using RunicTranslations.Compiler;

namespace RunicTranslations.Authoring;

public static class TranslationProjectScaffolder
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TranslationProjectPlan Render(TranslationProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string catalogId = RequireValue(request.CatalogId, "catalog ID");
        string codeNamespace = RequireValue(request.CodeNamespace, "code namespace");
        string className = RequireValue(request.ClassName, "class name");
        string layerName = RequireValue(request.LayerName, "layer name");
        string defaultLocale = CanonicalizeLocale(RequireValue(request.DefaultLocale, "default locale"));

        var locales = new List<TranslationProjectLocale>
        {
            new(defaultLocale),
        };
        var knownLocales = new HashSet<string>(StringComparer.Ordinal)
        {
            defaultLocale,
        };

        for (int index = 0; index < request.AdditionalLocales.Count; index++)
        {
            TranslationProjectLocale locale = request.AdditionalLocales[index]
                ?? throw new TranslationAuthoringException("An additional locale entry is null.");
            string tag = CanonicalizeLocale(RequireValue(locale.Tag, "locale tag"));
            string fallback = locale.Fallback is null
                ? defaultLocale
                : CanonicalizeLocale(RequireValue(locale.Fallback, $"fallback for locale '{tag}'"));
            if (!knownLocales.Add(tag))
            {
                throw new TranslationAuthoringException($"Locale '{tag}' is declared more than once.");
            }

            if (tag == fallback)
            {
                throw new TranslationAuthoringException($"Locale '{tag}' cannot fall back to itself.");
            }

            locales.Add(new TranslationProjectLocale(tag, fallback));
        }

        for (int index = 1; index < locales.Count; index++)
        {
            string fallback = locales[index].Fallback!;
            if (!knownLocales.Contains(fallback))
            {
                throw new TranslationAuthoringException(
                    $"Fallback locale '{fallback}' for '{locales[index].Tag}' is not declared by the project.");
            }
        }

        RejectFallbackCycles(locales);
        TranslationProjectCreationRequest canonicalRequest = new(
            request.Directory,
            catalogId,
            defaultLocale,
            codeNamespace,
            className,
            locales.GetRange(1, locales.Count - 1),
            layerName,
            request.GenerateEsm,
            request.IncludeStarterMessage,
            request.IncludeVsCodeSettings);

        var files = new List<TranslationProjectFile>(locales.Count + 2)
        {
            new($"{catalogId}.catalog.json", RenderManifest(canonicalRequest, locales)),
        };
        for (int index = 0; index < locales.Count; index++)
        {
            files.Add(new TranslationProjectFile(
                $"{catalogId}.{locales[index].Tag}.json",
                RenderDocument(canonicalRequest, locales[index].Tag)));
        }
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        TranslationCompilation compilation = Compile(files);
        if (!compilation.Success)
        {
            throw new TranslationAuthoringException(FormatDiagnostics(compilation.Diagnostics));
        }
        if (canonicalRequest.IncludeVsCodeSettings)
        {
            files.Add(new TranslationProjectFile(
                ".vscode/settings.json",
                RenderVsCodeSettings(canonicalRequest.CatalogId)));
            files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        }

        return new TranslationProjectPlan(canonicalRequest, locales.ToArray(), files.ToArray(), compilation);
    }

    private static byte[] RenderManifest(
        TranslationProjectCreationRequest request,
        List<TranslationProjectLocale> locales) => RenderJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("catalog", request.CatalogId);
        writer.WriteStartObject("code");
        writer.WriteString("namespace", request.CodeNamespace);
        writer.WriteString("className", request.ClassName);
        writer.WriteString("visibility", "public");
        writer.WriteEndObject();
        writer.WriteString("defaultLocale", request.DefaultLocale);
        writer.WriteStartArray("locales");
        for (int index = 0; index < locales.Count; index++)
        {
            writer.WriteStartObject();
            writer.WriteString("tag", locales[index].Tag);
            if (locales[index].Fallback is not null)
            {
                writer.WriteString("fallback", locales[index].Fallback);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteStartArray("layers");
        writer.WriteStartObject();
        writer.WriteString("name", request.LayerName);
        writer.WriteNumber("priority", 0);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteStartObject("validation");
        writer.WriteString("translationCompleteness", "error");
        writer.WriteString("extraLocaleKeys", "error");
        writer.WriteString("emptyValues", "error");
        writer.WriteEndObject();
        writer.WriteStartObject("runtime");
        writer.WriteString("unsupportedLocale", "parentsThenDefault");
        writer.WriteString("missingKey", "returnMarker");
        writer.WriteEndObject();
        if (request.GenerateEsm)
        {
            writer.WriteStartObject("outputs");
            writer.WriteStartObject("typescript");
            writer.WriteBoolean("enabled", true);
            writer.WriteString("moduleName", request.CatalogId);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    });

    private static byte[] RenderDocument(TranslationProjectCreationRequest request, string locale) => RenderJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 2);
        writer.WriteString("catalog", request.CatalogId);
        writer.WriteString("locale", locale);
        writer.WriteString("layer", request.LayerName);
        writer.WriteStartObject("resources");
        if (request.IncludeStarterMessage)
        {
            writer.WriteStartObject("Application");
            writer.WriteString("Name", request.ClassName);
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    });

    private static byte[] RenderVsCodeSettings(string catalogId) => RenderJson(writer =>
    {
        const string schemaRoot = "https://runic-artifex.eu/schemas/translations/";
        writer.WriteStartObject();
        writer.WriteStartArray("json.schemas");
        WriteAssociation(writer, schemaRoot + "catalog-v2.schema.json", ["**/*.catalog.json"]);
        WriteAssociation(writer, schemaRoot + "resources-v2.schema.json", ["**/" + catalogId + ".*.json", "!**/" + catalogId + ".catalog.json"]);
        writer.WriteEndArray();
        writer.WriteEndObject();

        static void WriteAssociation(Utf8JsonWriter writer, string url, string[] patterns)
        {
            writer.WriteStartObject();
            writer.WriteString("url", url);
            writer.WriteStartArray("fileMatch");
            for (int index = 0; index < patterns.Length; index++) writer.WriteStringValue(patterns[index]);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    });

    private static byte[] RenderJson(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            write(writer);
            writer.Flush();
        }

        byte[] result = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    private static TranslationCompilation Compile(List<TranslationProjectFile> files)
    {
        var manifests = new List<TranslationSource>(1);
        var documents = new List<TranslationSource>(files.Count - 1);
        for (int index = 0; index < files.Count; index++)
        {
            TranslationProjectFile file = files[index];
            var source = new TranslationSource(file.RelativePath, file.Bytes);
            if (file.RelativePath.EndsWith(".catalog.json", StringComparison.Ordinal))
            {
                manifests.Add(source);
            }
            else
            {
                documents.Add(source);
            }
        }

        return TranslationCompiler.Compile(manifests, documents);
    }

    private static string FormatDiagnostics(IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        var result = new StringBuilder("The proposed translation project is invalid:");
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = diagnostics[index];
            if (diagnostic.Severity != TranslationDiagnosticSeverity.Error)
            {
                continue;
            }

            result.Append('\n')
                .Append(diagnostic.Location.Path)
                .Append('(')
                .Append(diagnostic.Location.Line)
                .Append(',')
                .Append(diagnostic.Location.Column)
                .Append("): ")
                .Append(diagnostic.Id)
                .Append(' ')
                .Append(diagnostic.Message);
        }

        return result.ToString();
    }

    private static void RejectFallbackCycles(List<TranslationProjectLocale> locales)
    {
        var fallbacks = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < locales.Count; index++)
        {
            if (locales[index].Fallback is not null)
            {
                fallbacks.Add(locales[index].Tag, locales[index].Fallback!);
            }
        }

        for (int index = 0; index < locales.Count; index++)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string current = locales[index].Tag;
            while (fallbacks.TryGetValue(current, out string? fallback))
            {
                if (!visited.Add(current))
                {
                    throw new TranslationAuthoringException(
                        $"Locale fallback relationships contain a cycle through '{current}'.");
                }

                current = fallback;
            }
        }
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new TranslationAuthoringException($"The {name} is required.");
        }

        return value.Trim();
    }

    internal static string CanonicalizeLocale(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-')
        {
            throw InvalidLocale(value);
        }

        string[] parts = value.Split('-');
        if (parts[0].Length is < 2 or > 8 || !AllLetters(parts[0]))
        {
            throw InvalidLocale(value);
        }

        var result = new StringBuilder(value.Length).Append(parts[0].ToLowerInvariant());
        bool extension = false;
        for (int index = 1; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length is 0 or > 8 || !AllAlphaNumeric(part))
            {
                throw InvalidLocale(value);
            }

            result.Append('-');
            if (part.Length == 1)
            {
                extension = true;
                result.Append(part.ToLowerInvariant());
            }
            else if (!extension && part.Length == 4 && AllLetters(part))
            {
                result.Append(char.ToUpperInvariant(part[0])).Append(part[1..].ToLowerInvariant());
            }
            else if (!extension && ((part.Length == 2 && AllLetters(part)) || (part.Length == 3 && AllDigits(part))))
            {
                result.Append(part.ToUpperInvariant());
            }
            else
            {
                result.Append(part.ToLowerInvariant());
            }
        }

        return result.ToString();
    }

    private static TranslationAuthoringException InvalidLocale(string value) =>
        new($"Locale tag '{value}' is not a valid portable BCP-47 tag.");

    private static bool AllLetters(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')) return false;
        }

        return true;
    }

    private static bool AllDigits(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] is not (>= '0' and <= '9')) return false;
        }

        return true;
    }

    private static bool AllAlphaNumeric(string value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z') and not (>= '0' and <= '9')) return false;
        }

        return true;
    }
}
