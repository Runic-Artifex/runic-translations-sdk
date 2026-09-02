using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>Creates the convention-based <c>runic.json</c> and MF2 project layout.</summary>
public static class TranslationProjectScaffolder
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TranslationProjectPlan Render(TranslationProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string defaultLocale = TranslationProjectScaffolder.CanonicalizeLocale(RequireValue(request.DefaultLocale, "default locale"));
        var locales = new List<TranslationProjectLocale> { new(defaultLocale) };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { defaultLocale };
        for (int index = 0; index < request.AdditionalLocales.Count; index++)
        {
            TranslationProjectLocale item = request.AdditionalLocales[index]
                ?? throw new TranslationAuthoringException("An additional locale entry is null.");
            string tag = TranslationProjectScaffolder.CanonicalizeLocale(RequireValue(item.Tag, "locale tag"));
            string fallback = item.Fallback is null
                ? defaultLocale
                : TranslationProjectScaffolder.CanonicalizeLocale(RequireValue(item.Fallback, $"fallback for locale '{tag}'"));
            if (!seen.Add(tag)) throw new TranslationAuthoringException($"Locale '{tag}' is declared more than once.");
            locales.Add(new TranslationProjectLocale(tag, fallback));
        }

        var files = new List<TranslationProjectFile>(1 + (request.IncludeStarterMessage ? locales.Count : 0))
        {
            new("runic.json", RenderProject(request, defaultLocale, locales)),
        };
        if (request.IncludeStarterMessage)
        {
            byte[] starter = Utf8.GetBytes(RequireValue(request.ClassName, "class name") + "\n");
            for (int index = 0; index < locales.Count; index++)
                files.Add(new TranslationProjectFile($"{locales[index].Tag}/application_title.mf2", starter));
        }
        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));

        TranslationCompilation compilation = TranslationCompiler.CompileMf2Project(
            new TranslationSource("runic.json", files.Find(static file => file.RelativePath == "runic.json")!.Bytes),
            MessageSources(files));
        if (!compilation.Success) throw new TranslationAuthoringException(FormatDiagnostics(compilation.Diagnostics));
        return new TranslationProjectPlan(request, locales.ToArray(), files.ToArray(), compilation);
    }

    private static byte[] RenderProject(
        TranslationProjectCreationRequest request,
        string defaultLocale,
        List<TranslationProjectLocale> locales)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://runic-artifex.eu/schemas/translations/project-v1.schema.json");
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("catalog", RequireValue(request.CatalogId, "catalog ID"));
            writer.WriteStartObject("code");
            writer.WriteString("namespace", RequireValue(request.CodeNamespace, "code namespace"));
            writer.WriteString("className", RequireValue(request.ClassName, "class name"));
            writer.WriteEndObject();
            writer.WriteString("baseLocale", defaultLocale);
            writer.WriteStartArray("locales");
            for (int index = 0; index < locales.Count; index++)
            {
                TranslationProjectLocale locale = locales[index];
                if (locale.Fallback is null || string.Equals(locale.Fallback, defaultLocale, StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteStringValue(locale.Tag);
                }
                else
                {
                    writer.WriteStartObject();
                    writer.WriteString("tag", locale.Tag);
                    writer.WriteString("fallback", locale.Fallback);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        byte[] result = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(result);
        result[^1] = (byte)'\n';
        return result;
    }

    private static TranslationSource[] MessageSources(List<TranslationProjectFile> files)
    {
        var sources = new List<TranslationSource>();
        for (int index = 0; index < files.Count; index++)
            if (files[index].RelativePath.EndsWith(".mf2", StringComparison.Ordinal))
                sources.Add(new TranslationSource(files[index].RelativePath, files[index].Bytes));
        return sources.ToArray();
    }

    private static string FormatDiagnostics(IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        var result = new StringBuilder("The proposed translation project is invalid:");
        for (int index = 0; index < diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = diagnostics[index];
            if (diagnostic.Severity != TranslationDiagnosticSeverity.Error) continue;
            result.Append('\n').Append(diagnostic.Location.Path).Append('(')
                .Append(diagnostic.Location.Line).Append(',').Append(diagnostic.Location.Column).Append("): ")
                .Append(diagnostic.Id).Append(' ').Append(diagnostic.Message);
        }
        return result.ToString();
    }

    private static string RequireValue(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new TranslationAuthoringException($"The {name} is required.");
        return value.Trim();
    }

    internal static string CanonicalizeLocale(string value)
    {
        if (value.Length == 0 || value[0] == '-' || value[^1] == '-') throw InvalidLocale(value);
        string[] parts = value.Split('-');
        if (parts[0].Length is < 2 or > 8 || !AllLetters(parts[0])) throw InvalidLocale(value);

        var result = new StringBuilder(value.Length).Append(parts[0].ToLowerInvariant());
        bool extension = false;
        for (int index = 1; index < parts.Length; index++)
        {
            string part = parts[index];
            if (part.Length is 0 or > 8 || !AllAlphaNumeric(part)) throw InvalidLocale(value);
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
            if (value[index] is not (>= 'A' and <= 'Z') and not (>= 'a' and <= 'z')) return false;
        return true;
    }

    private static bool AllDigits(string value)
    {
        for (int index = 0; index < value.Length; index++)
            if (value[index] is not (>= '0' and <= '9')) return false;
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
