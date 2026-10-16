using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Runic.CommandLine;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

/// <summary>The values of <c>import-text</c>: the group, the <c>locale=path</c> tables and the optional base-text list.</summary>
internal sealed record TextImportRequest(string Group, IReadOnlyList<string> Tables, string? BaseTexts);

/// <summary>
/// Imports a catalog keyed by base-language text (<c>{ "Remove user {name}": "Benutzer {name} entfernen" }</c>)
/// into one RMF2 group with generated, stable keys, and writes a base-text index beside it.
/// </summary>
internal static class TextCatalogImport
{
    internal const string DiagnosticCode = "RCLI9014";
    internal const string IndexFileName = "base-text.json";
    private const string Header = "# Imported by `runic-translations import-text`. The next import rewrites this file; edit the source tables instead.";
    private const int MaximumInputBytes = 8 * 1024 * 1024;
    private const int MaximumSlugWords = 7;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Placeholder = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.CultureInvariant);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Plans, writes (unless <paramref name="dryRun"/>) and validates an import. Returns the exit code.</summary>
    internal static int Run(
        string projectPath,
        TextImportRequest request,
        bool dryRun,
        ToolOperationResult result,
        Func<CompilerInputs, int> validate)
    {
        if (!Identifier.IsMatch(request.Group))
            throw new ToolUsageException($"--group must be an RMF2 identifier ([A-Za-z_][A-Za-z0-9_]*), not '{request.Group}'.");

        // Reading the project first reports a missing runic.json like validate does.
        CompilerInputs current = InputFiles.ReadProject(projectPath);
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string supplied = Path.GetFullPath(projectPath, currentDirectory);
        string configPath = Directory.Exists(supplied) ? Path.Combine(supplied, "runic.json") : supplied;
        string root = Path.GetDirectoryName(configPath)!;
        Manifest manifest = Manifest.Read(current.Project.GetUtf8Bytes(), root);
        string groupDirectory = Path.Combine(root, request.Group);
        IReadOnlyList<string> prefix = manifest.Namespace(groupDirectory)
            ?? throw new ToolUsageException($"'{InputFiles.DisplayPath(groupDirectory, currentDirectory)}' is not inside a source root of runic.json.");

        var errors = new List<string>();
        List<(string Locale, string Path)> tableArguments = ParseTables(request.Tables, manifest);

        // Base texts: --base first, then the keys of each table in the order given.
        var texts = new List<string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (request.BaseTexts is not null)
            foreach (string text in ReadBaseTexts(request.BaseTexts, errors))
                if (known.Add(text)) texts.Add(text);
        var tables = new List<(string Locale, Dictionary<string, string> Entries)>();
        foreach ((string locale, string path) in tableArguments)
        {
            Dictionary<string, string> entries = ReadTable(path, errors);
            tables.Add((locale, entries));
            foreach (string text in entries.Keys)
                if (known.Add(text)) texts.Add(text);
        }

        foreach ((string locale, Dictionary<string, string> entries) in tables)
        foreach ((string text, string translation) in entries)
        {
            HashSet<string> available = Placeholders(text);
            foreach (string name in Placeholders(translation))
                if (!available.Contains(name))
                    errors.Add($"The {locale} translation of {Quote(text)} uses the placeholder {{{name}}}, which the base text does not contain.");
        }

        string baseFile = Path.Combine(groupDirectory, manifest.BaseLocale + ".rmf2");
        Dictionary<string, string> previous = ReadPreviousKeys(baseFile, InputFiles.DisplayPath(baseFile, currentDirectory), errors);

        var patterns = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string text in texts.Concat(tables.SelectMany(table => table.Entries.Values)))
        {
            if (patterns.ContainsKey(text)) continue;
            string? pattern = ToRepresentablePattern(text);
            if (pattern is null) errors.Add($"The text {Quote(text)} cannot be written as an RMF2 pattern.");
            else patterns[text] = pattern;
        }

        if (errors.Count != 0)
        {
            foreach (string error in errors) result.AddDiagnostic(DiagnosticCode, "import", error, CommandDiagnosticSeverity.Error);
            result.WriteOutputLine("No files were written.");
            result.ExitCategory = CommandExitCategory.Validation;
            return 1;
        }

        // Keys: a text that is still present keeps its key; the keys of removed texts are not reused.
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(previous.Values, StringComparer.Ordinal);
        foreach (string text in texts)
            if (previous.TryGetValue(text, out string? key)) keys[text] = key;
        int added = 0;
        foreach (string text in texts)
        {
            if (keys.ContainsKey(text)) continue;
            keys[text] = UniqueKey(Slug(text), taken);
            added++;
        }

        string Generated(string key) => string.Join('_', prefix.Append(key));

        var artifacts = new List<ToolArtifact>
        {
            new(manifest.BaseLocale + ".rmf2", RenderResource(texts.Select(text => (keys[text], patterns[text])))),
        };
        foreach ((string locale, Dictionary<string, string> entries) in tables)
            artifacts.Add(new(locale + ".rmf2", RenderResource(texts.Where(entries.ContainsKey).Select(text => (keys[text], patterns[entries[text]])))));
        artifacts.Add(new(IndexFileName, RenderIndex(texts.Select(text => (text, Generated(keys[text]))))));

        string groupDisplay = InputFiles.DisplayPath(groupDirectory, currentDirectory);
        result.WriteOutputLine($"imported {texts.Count} base text(s) into {groupDisplay}: {added} new, {texts.Count - added} kept, {previous.Count(pair => !known.Contains(pair.Key))} removed.");
        foreach ((string text, string key) in previous)
            if (!known.Contains(text)) result.WriteOutputLine($"removed {Generated(key)}: {Quote(text)}");
        foreach (ToolArtifact artifact in artifacts)
            result.WriteOutputLine((dryRun ? "would write " : "wrote ") + groupDisplay + "/" + artifact.RelativePath);

        CompilerInputs planned;
        if (dryRun)
        {
            // Validate the planned content in memory, as the files would be read after writing.
            var messages = current.Messages.ToDictionary(source => source.Path, StringComparer.Ordinal);
            foreach (ToolArtifact artifact in artifacts)
            {
                if (!artifact.RelativePath.EndsWith(".rmf2", StringComparison.Ordinal)) continue;
                string display = InputFiles.DisplayPath(Path.Combine(groupDirectory, artifact.RelativePath), currentDirectory);
                messages[display] = new TranslationSource(display, artifact.Content);
            }
            var ordered = messages.Values.ToList();
            ordered.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
            planned = current with { Messages = ordered };
        }
        else
        {
            ArtifactFiles.WriteAtomically(groupDirectory, artifacts);
            planned = InputFiles.ReadProject(projectPath);
        }

        return validate(planned);
    }

    private static List<(string Locale, string Path)> ParseTables(IReadOnlyList<string> values, Manifest manifest)
    {
        var tables = new List<(string Locale, string Path)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in values)
        {
            int separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
                throw new ToolUsageException($"--table expects <locale>=<path>, not '{value}'.");
            string requested = value[..separator];
            if (string.Equals(requested, manifest.BaseLocale, StringComparison.OrdinalIgnoreCase))
                throw new ToolUsageException($"--table {requested} names the base locale; list base texts without a translation with --base.");
            string locale = manifest.Locales.FirstOrDefault(declared => string.Equals(declared, requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new ToolUsageException($"--table locale '{requested}' is not declared in runic.json.");
            if (!seen.Add(locale)) throw new ToolUsageException($"--table {locale} may be specified only once.");
            tables.Add((locale, value[(separator + 1)..]));
        }
        return tables;
    }

    private static List<string> ReadBaseTexts(string path, List<string> errors)
    {
        var texts = new List<string>();
        using JsonDocument? document = ParseJson(path, errors);
        if (document is null) return texts;
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{Display(path)}: --base must be a JSON array of base texts.");
            return texts;
        }
        int index = 0;
        foreach (JsonElement item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String) texts.Add(item.GetString()!);
            else errors.Add($"{Display(path)}: item {index} is not a string.");
            index++;
        }
        return texts;
    }

    private static Dictionary<string, string> ReadTable(string path, List<string> errors)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        using JsonDocument? document = ParseJson(path, errors);
        if (document is null) return entries;
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{Display(path)}: a table must be a JSON object that maps base text to translated text.");
            return entries;
        }
        foreach (System.Text.Json.JsonProperty property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
                errors.Add($"{Display(path)}: the value of {Quote(property.Name)} is not a string.");
            else if (!entries.TryAdd(property.Name, property.Value.GetString()!))
                errors.Add($"{Display(path)}: {Quote(property.Name)} appears more than once.");
        }
        return entries;
    }

    private static JsonDocument? ParseJson(string path, List<string> errors)
    {
        var information = new FileInfo(path);
        if (!information.Exists) throw new FileNotFoundException($"'{Display(path)}' does not exist.", path);
        if (information.Length > MaximumInputBytes)
        {
            errors.Add($"{Display(path)}: the file exceeds {MaximumInputBytes} bytes.");
            return null;
        }
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            StrictUtf8.GetString(bytes);
            return JsonDocument.Parse(bytes);
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            errors.Add($"{Display(path)}: not valid UTF-8 JSON ({exception.Message})");
            return null;
        }
    }

    // The keys of a previous import, by base text. Entries this tool cannot map back to text are ignored.
    private static Dictionary<string, string> ReadPreviousKeys(string path, string display, List<string> errors)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(path)) return keys;
        var source = new TranslationSource(display, File.ReadAllBytes(path));
        Rmf2ResourceDocument document = Rmf2ResourceReader.Read(source);
        if (!document.Success)
        {
            errors.Add($"{display} cannot be read to keep its keys: {string.Join("; ", document.Diagnostics.Select(diagnostic => diagnostic.Message))}");
            return keys;
        }
        foreach (Rmf2ResourceNode node in document.Nodes)
        {
            if (node.IsGroup || node.Path.Count != 1) continue;
            string? text = FromPattern(node.Message!);
            if (text is not null) keys.TryAdd(text, node.Path[0]);
        }
        return keys;
    }

    private static HashSet<string> Placeholders(string text) =>
        Placeholder.Matches(text).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>The pattern of <paramref name="text"/>, quoted when an unquoted one would not read back as the same text.</summary>
    private static string? ToRepresentablePattern(string text)
    {
        string pattern = ToPattern(text);
        if (!NeedsQuotes(text) && ReadsBack(pattern, text)) return pattern;
        string quoted = "{{" + pattern + "}}";
        return ReadsBack(quoted, text) ? quoted : null;
    }

    private static bool NeedsQuotes(string text) =>
        text.Length == 0 || text[0] == '.' || IsFramingSpace(text[0]) || IsFramingSpace(text[^1]) ||
        text.Contains('\n') || text.Contains('\r');

    // Whitespace and bidirectional marks that MF2 or RMF2 framing may trim around an unquoted pattern.
    private static bool IsFramingSpace(char value) =>
        char.IsWhiteSpace(value) || value is '؜' or '‎' or '‏' or (>= '⁦' and <= '⁩');

    private static bool ReadsBack(string pattern, string text)
    {
        var source = new TranslationSource("import.rmf2", Encoding.UTF8.GetBytes(Rmf2ResourceWriter.Entry("text", pattern, 0)));
        Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source);
        return document.Success && document.Nodes.Count == 1 && document.Nodes[0].Message is { } message &&
            FromPattern(message) == text;
    }

    /// <summary>Text with <c>{name}</c> placeholders as an RMF2 pattern: <c>{$name}</c>, other braces and backslashes escaped.</summary>
    internal static string ToPattern(string text)
    {
        var pattern = new StringBuilder(text.Length + 8);
        int start = 0;
        foreach (Match match in Placeholder.Matches(text))
        {
            AppendEscaped(pattern, text.AsSpan(start, match.Index - start));
            pattern.Append("{$").Append(match.Groups[1].Value).Append('}');
            start = match.Index + match.Length;
        }
        AppendEscaped(pattern, text.AsSpan(start));
        return pattern.ToString();
    }

    private static void AppendEscaped(StringBuilder pattern, ReadOnlySpan<char> text)
    {
        foreach (char value in text)
        {
            if (value is '\\' or '{' or '}') pattern.Append('\\');
            pattern.Append(value);
        }
    }

    /// <summary>The text of a pattern this tool writes: <c>{$name}</c> (with any annotation) as <c>{name}</c>, escapes removed.</summary>
    internal static string? FromPattern(string pattern)
    {
        ReadOnlySpan<char> body = pattern.AsSpan();
        if (body.StartsWith("{{", StringComparison.Ordinal) && body.EndsWith("}}", StringComparison.Ordinal) && body.Length >= 4)
            body = body[2..^2];
        else if (body.StartsWith(".", StringComparison.Ordinal))
            return null;
        var text = new StringBuilder(body.Length);
        for (int index = 0; index < body.Length; index++)
        {
            char value = body[index];
            if (value == '\\')
            {
                if (++index == body.Length) return null;
                text.Append(body[index]);
            }
            else if (value == '{')
            {
                int end = body[index..].IndexOf('}');
                if (end < 0) return null;
                string expression = body.Slice(index + 1, end - 1).ToString().Trim();
                Match variable = Regex.Match(expression, @"^\$([A-Za-z_][A-Za-z0-9_]*)(\s+:.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!variable.Success) return null;
                text.Append('{').Append(variable.Groups[1].Value).Append('}');
                index += end;
            }
            else if (value == '}') return null;
            else text.Append(value);
        }
        return text.ToString();
    }

    /// <summary>Up to seven lowercase ASCII words of the text without placeholders, joined by <c>_</c>.</summary>
    internal static string Slug(string text)
    {
        string decomposed = Placeholder.Replace(text, " ").Normalize(NormalizationForm.FormKD);
        var words = new List<string>(MaximumSlugWords);
        var word = new StringBuilder();
        foreach (char value in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(value) == UnicodeCategory.NonSpacingMark) continue;
            char lower = char.ToLowerInvariant(value);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                word.Append(lower);
                continue;
            }
            if (word.Length != 0) words.Add(word.ToString());
            word.Clear();
            if (words.Count == MaximumSlugWords) break;
        }
        if (word.Length != 0 && words.Count < MaximumSlugWords) words.Add(word.ToString());
        if (words.Count == 0) return "text";
        string slug = string.Join('_', words);
        return char.IsAsciiDigit(slug[0]) ? "n_" + slug : slug;
    }

    private static string UniqueKey(string slug, HashSet<string> taken)
    {
        string key = slug;
        for (int number = 2; !taken.Add(key); number++) key = slug + "_" + number.ToString(CultureInfo.InvariantCulture);
        return key;
    }

    private static byte[] RenderResource(IEnumerable<(string Key, string Pattern)> entries)
    {
        // The blank line keeps the header from documenting the first entry.
        var text = new StringBuilder(Header).Append("\n\n");
        foreach ((string key, string pattern) in entries) text.Append(Rmf2ResourceWriter.Entry(key, pattern, 0));
        return StrictUtf8.GetBytes(text.ToString());
    }

    private static byte[] RenderIndex(IEnumerable<(string Text, string Key)> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach ((string text, string key) in entries) writer.WriteString(text, key);
            writer.WriteEndObject();
        }
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static string Quote(string text) => "\"" + JsonEncodedText.Encode(text, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) + "\"";

    private static string Display(string path) => path.Replace('\\', '/');

    /// <summary>The parts of runic.json the import needs: locales and source roots.</summary>
    private sealed record Manifest(string BaseLocale, IReadOnlyList<string> Locales, IReadOnlyList<(string Path, IReadOnlyList<string> Namespace)> Roots)
    {
        internal static Manifest Read(ReadOnlyMemory<byte> utf8, string root)
        {
            JsonDocument document;
            try { document = JsonDocument.Parse(utf8); }
            catch (JsonException) { throw new ToolUsageException("runic.json is not valid JSON; run validate for its diagnostics."); }
            using (document)
            {
                JsonElement project = document.RootElement;
                if (project.ValueKind != JsonValueKind.Object || !project.TryGetProperty("baseLocale", out JsonElement baseLocale) || baseLocale.ValueKind != JsonValueKind.String)
                    throw new ToolUsageException("runic.json must declare a baseLocale string.");
                string declaredBase = baseLocale.GetString()!;
                var locales = new List<string> { declaredBase };
                if (project.TryGetProperty("locales", out JsonElement declared) && declared.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement item in declared.EnumerateArray())
                    {
                        string? tag = item.ValueKind == JsonValueKind.String ? item.GetString()
                            : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("tag", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()
                            : null;
                        if (tag is not null && !locales.Contains(tag, StringComparer.OrdinalIgnoreCase)) locales.Add(tag);
                    }
                var roots = new List<(string, IReadOnlyList<string>)>();
                if (!project.TryGetProperty("sourceRoots", out JsonElement mounts))
                    roots.Add((Path.GetFullPath(root), []));
                else if (mounts.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement mount in mounts.EnumerateArray())
                    {
                        if (mount.ValueKind != JsonValueKind.Object || !mount.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String) continue;
                        var segments = new List<string>();
                        if (mount.TryGetProperty("namespace", out JsonElement names) && names.ValueKind == JsonValueKind.Array)
                            foreach (JsonElement name in names.EnumerateArray())
                                if (name.ValueKind == JsonValueKind.String) segments.Add(name.GetString()!);
                        roots.Add((Path.GetFullPath(path.GetString()!, root), segments));
                    }
                return new Manifest(declaredBase, locales, roots);
            }
        }

        /// <summary>The namespace segments of a directory: its source root's namespace and the directories below it.</summary>
        internal string[]? Namespace(string directory)
        {
            foreach ((string path, IReadOnlyList<string> prefix) in Roots)
            {
                string relative = System.IO.Path.GetRelativePath(path, directory);
                if (relative == "." || System.IO.Path.IsPathRooted(relative) || relative == ".." ||
                    relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    relative.StartsWith(".." + System.IO.Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    continue;
                return prefix.Concat(relative.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)).ToArray();
            }
            return null;
        }
    }
}
