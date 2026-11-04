using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

/// <summary>One message read from a migration source and already written as an MF2 pattern.</summary>
/// <param name="SourceKey">The key as the source file names it, such as <c>settings.title</c> or <c>Errors.NotFound</c>.</param>
/// <param name="Segments">The RMF2 path below the import's group: identifier segments.</param>
/// <param name="Pattern">The MF2 message.</param>
/// <param name="Comments">The description lines (a <c>.resx</c> comment).</param>
internal sealed record ImportedMessage(string SourceKey, IReadOnlyList<string> Segments, string Pattern, IReadOnlyList<string> Comments);

/// <summary>A source file and the messages it maps to.</summary>
internal sealed record ImportedFile(string Locale, string Display, IReadOnlyList<ImportedMessage> Messages, int Read);

/// <summary>A source file that cannot be imported at all; nothing is written.</summary>
internal sealed class ImportSourceException(string code, string message) : Exception(message)
{
    internal string Code { get; } = code;
}

/// <summary>What a migration import reports besides the applied messages: skipped and refused messages and notices.</summary>
internal sealed class ImportReport
{
    internal List<TranslationsToolInterchangeEntry> Entries { get; } = [];
    internal List<TranslationsToolInterchangeNotice> Notices { get; } = [];
    internal int Skipped { get; private set; }
    internal int Refused { get; private set; }

    internal void Skip(string locale, string key, string code, string message)
    {
        Skipped++;
        Entries.Add(new(key, "skipped", code, message) { Locale = locale });
    }

    internal void Refuse(string locale, string key, string code, string message)
    {
        Refused++;
        Entries.Add(new(key, "refused", code, message) { Locale = locale });
    }

    internal void Notice(string location, string code, string message) => Notices.Add(new(code, location, message));
}

/// <summary>The locales a migration may import: runic.json's declared locales, matched case-insensitively and with <c>_</c> as <c>-</c>.</summary>
internal sealed class ImportLocales(string baseLocale, IReadOnlyList<string> declared, IReadOnlyList<string> requested)
{
    internal string BaseLocale { get; } = baseLocale;

    /// <summary>The declared locale a file or directory name stands for, or <see langword="null"/>.</summary>
    internal string? Declared(string name)
    {
        string tag = name.Replace('_', '-');
        return declared.FirstOrDefault(locale => string.Equals(locale, tag, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a name is a locale tag: declared, or a culture .NET knows (an undeclared locale is reported and skipped).</summary>
    internal bool IsLocale(string name)
    {
        if (Declared(name) is not null) return true;
        if (!Regex.IsMatch(name, "^[A-Za-z]{2,3}([-_][A-Za-z0-9]{2,8})*$", RegexOptions.CultureInvariant)) return false;
        try { return CultureInfo.GetCultureInfo(name.Replace('_', '-'), predefinedOnly: true).Name.Length != 0; }
        catch (CultureNotFoundException) { return false; }
    }

    /// <summary>Whether <c>--locale</c> selects the locale; without <c>--locale</c> every declared locale is imported.</summary>
    internal bool Selected(string locale) =>
        requested.Count == 0 || requested.Any(item => string.Equals(item.Replace('_', '-'), locale, StringComparison.OrdinalIgnoreCase));

    /// <summary>The single <c>--locale</c> value, for a file whose name names no locale.</summary>
    internal string? Single => requested.Count == 1 ? Declared(requested[0]) : null;
}

/// <summary>Shared mapping helpers: identifier segments, MF2 text escapes and pattern framing.</summary>
internal static class ImportMapping
{
    internal const int MaximumInputBytes = 8 * 1024 * 1024;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>An RMF2 identifier for a key segment: diacritics removed, other characters <c>_</c>, <c>_</c> before a leading digit.</summary>
    internal static string Segment(string value)
    {
        if (Identifier.IsMatch(value)) return value;
        var segment = new StringBuilder(value.Length + 1);
        foreach (char character in value.Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            segment.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
        }
        if (segment.Length == 0 || char.IsAsciiDigit(segment[0])) segment.Insert(0, '_');
        return segment.ToString();
    }

    /// <summary>Appends text with the MF2 text escapes: <c>\</c>, <c>{</c> and <c>}</c>.</summary>
    internal static void AppendText(StringBuilder pattern, ReadOnlySpan<char> text)
    {
        foreach (char value in text)
        {
            if (value is '\\' or '{' or '}') pattern.Append('\\');
            pattern.Append(value);
        }
    }

    /// <summary>A simple pattern, quoted when an unquoted one would lose outer whitespace, a leading <c>.</c> or a line break.</summary>
    internal static string Frame(string body) => TextCatalogImport.NeedsQuotes(body) ? "{{" + body + "}}" : body;

    /// <summary>Whether the pattern reads back unchanged from an RMF2 entry, as <c>import-text</c> checks.</summary>
    internal static bool ReadsBack(string pattern)
    {
        var source = new TranslationSource("import.rmf2", Encoding.UTF8.GetBytes(Runic.Translations.Authoring.Rmf2ResourceWriter.Entry("text", pattern, 0)));
        Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source);
        return document.Success && document.Nodes.Count == 1 && document.Nodes[0].Message == pattern;
    }

    internal static byte[] ReadBytes(string path, string display)
    {
        var information = new FileInfo(path);
        if (information.Length > MaximumInputBytes)
            throw new ImportSourceException("IMPORT-TOO-LARGE", $"{display}: the file exceeds {MaximumInputBytes} bytes.");
        return File.ReadAllBytes(path);
    }

    /// <summary>A source path relative to the working directory, or absolute when it lies outside it.</summary>
    internal static string Display(string fullPath)
    {
        string relative = Path.GetRelativePath(Path.GetFullPath(Environment.CurrentDirectory), fullPath);
        return (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? fullPath : relative).Replace('\\', '/');
    }
}

/// <summary>
/// Reads .NET <c>.resx</c> files: string <c>&lt;data&gt;</c> entries with their <c>&lt;comment&gt;</c>, composite
/// format holes <c>{0}</c> as <c>{$arg0}</c> and named holes <c>{name}</c> as <c>{$name}</c>.
/// </summary>
internal static class ResxImport
{
    private static readonly Regex Hole = new(@"^\s*(?:(?<index>[0-9]+)|(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*(?:,\s*(?<alignment>-?[0-9]+)\s*)?(?::(?<format>[^{}]*))?$", RegexOptions.CultureInvariant);
    private static readonly Regex NumericFormat = new("^(?<kind>[NnPpFf])(?<digits>[0-9]{0,2})$", RegexOptions.CultureInvariant);

    /// <summary>The files to import: a neutral file and its declared satellites, or one satellite.</summary>
    internal static List<ImportedFile> Read(string path, ImportLocales locales, ImportReport report)
    {
        string full = Path.GetFullPath(path);
        if (Directory.Exists(full)) throw new ToolUsageException($"--from resx expects a .resx file, not the directory '{path}'.");
        if (!File.Exists(full)) throw new ToolUsageException($"the file '{path}' does not exist.");
        if (!string.Equals(Path.GetExtension(full), ".resx", StringComparison.OrdinalIgnoreCase))
            throw new ToolUsageException($"--from resx expects a .resx file, not '{path}'.");
        string directory = Path.GetDirectoryName(full)!;
        string stem = Path.GetFileNameWithoutExtension(full);
        int dot = stem.LastIndexOf('.');
        var files = new List<ImportedFile>();
        if (dot > 0 && locales.IsLocale(stem[(dot + 1)..]))
        {
            // A satellite such as Strings.de.resx: only this file.
            string? locale = locales.Declared(stem[(dot + 1)..]);
            if (locale is null)
                throw new ToolUsageException($"the locale '{stem[(dot + 1)..]}' of '{path}' is not declared in runic.json.");
            if (locale == locales.BaseLocale)
                throw new ToolUsageException($"'{path}' names the base locale '{locale}'; import the neutral .resx file for it.");
            files.Add(ReadFile(full, locale, false, report));
            return files;
        }

        // A neutral file such as Strings.resx: the base locale, then every satellite Strings.<locale>.resx beside it.
        if (locales.Selected(locales.BaseLocale)) files.Add(ReadFile(full, locales.BaseLocale, true, report));
        foreach (string satellite in Directory.EnumerateFiles(directory, stem + ".*.resx").Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileNameWithoutExtension(satellite)[(stem.Length + 1)..];
            if (name.Contains('.') || !locales.IsLocale(name)) continue;
            string? locale = locales.Declared(name);
            if (locale is null)
            {
                report.Notice(ImportMapping.Display(satellite), "IMPORT-LOCALE", $"The locale '{name}' is not declared in runic.json; the file was not imported.");
                continue;
            }
            if (locale == locales.BaseLocale || !locales.Selected(locale)) continue;
            files.Add(ReadFile(satellite, locale, false, report));
        }
        return files;
    }

    // Comments become descriptions in the base locale only; a satellite's comments describe the same message.
    private static ImportedFile ReadFile(string path, string locale, bool isBase, ImportReport report)
    {
        string display = ImportMapping.Display(path);
        byte[] bytes = ImportMapping.ReadBytes(path, display);
        var messages = new List<ImportedMessage>();
        int read = 0;
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true };
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "root")
                throw new ImportSourceException("RESX-MALFORMED", $"{display}: the document element is not <root>.");
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 || reader.LocalName != "data") continue;
                string? name = reader.GetAttribute("name");
                string? type = reader.GetAttribute("type");
                string? mimeType = reader.GetAttribute("mimetype");
                string? value = null, comment = null;
                if (!reader.IsEmptyElement)
                {
                    using XmlReader data = reader.ReadSubtree();
                    data.Read();
                    while (data.Read())
                    {
                        if (data.NodeType != XmlNodeType.Element || data.Depth != 1) continue;
                        if (data.LocalName == "value") value = data.ReadElementContentAsString();
                        else if (data.LocalName == "comment") comment = data.ReadElementContentAsString();
                    }
                }
                if (name is null) continue;
                read++;
                if (name.StartsWith(">>", StringComparison.Ordinal) || name.StartsWith('$'))
                {
                    report.Skip(locale, name, "RESX-DESIGNER", "Windows Forms designer metadata is not text.");
                    continue;
                }
                if (mimeType is not null || type is not null && !type.StartsWith("System.String", StringComparison.Ordinal))
                {
                    report.Skip(locale, name, "RESX-NON-STRING", $"The entry is not a string ({type ?? mimeType}); only text is imported.");
                    continue;
                }
                messages.Add(new ImportedMessage(
                    name,
                    [ImportMapping.Segment(name.Replace('.', '_'))],
                    ToPattern(value ?? string.Empty, display + ":" + name, report),
                    string.IsNullOrWhiteSpace(comment) || !isBase ? [] : [comment.Trim()]));
            }
        }
        catch (XmlException exception)
        {
            throw new ImportSourceException("RESX-MALFORMED", $"{display}: not well-formed XML ({exception.Message})");
        }
        return new ImportedFile(locale, display, messages, read);
    }

    /// <summary>A .NET composite format string as an MF2 pattern.</summary>
    internal static string ToPattern(string text, string location, ImportReport report)
    {
        var body = new StringBuilder(text.Length + 8);
        int index = 0;
        while (index < text.Length)
        {
            char value = text[index];
            if (value == '{' && index + 1 < text.Length && text[index + 1] == '{') { body.Append("\\{"); index += 2; continue; }
            if (value == '}' && index + 1 < text.Length && text[index + 1] == '}') { body.Append("\\}"); index += 2; continue; }
            if (value == '{')
            {
                int end = text.IndexOf('}', index + 1);
                Match hole = end < 0 ? Match.Empty : Hole.Match(text, index + 1, end - index - 1);
                if (end >= 0 && hole.Success && hole.Index == index + 1 && hole.Length == end - index - 1)
                {
                    string name = hole.Groups["index"].Success ? "arg" + int.Parse(hole.Groups["index"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) : hole.Groups["name"].Value;
                    body.Append("{$").Append(name);
                    if (hole.Groups["alignment"].Success)
                        report.Notice(location, "RESX-FORMAT", $"The alignment of {text[index..(end + 1)]} was dropped.");
                    if (hole.Groups["format"].Success) body.Append(Format(hole.Groups["format"].Value, text[index..(end + 1)], location, report));
                    body.Append('}');
                    index = end + 1;
                    continue;
                }
                report.Notice(location, "RESX-BRACE", "A '{' that starts no format item was kept as text.");
            }
            else if (value == '}')
                report.Notice(location, "RESX-BRACE", "A '}' that ends no format item was kept as text.");
            ImportMapping.AppendText(body, text.AsSpan(index, 1));
            index++;
        }
        return ImportMapping.Frame(body.ToString());
    }

    // N2 and P0 become :number options; other format strings (dates, currency, custom) are dropped and reported.
    private static string Format(string format, string item, string location, ImportReport report)
    {
        Match numeric = NumericFormat.Match(format.Trim());
        if (numeric.Success)
        {
            string digits = numeric.Groups["digits"].Value.Length == 0 ? "2" : int.Parse(numeric.Groups["digits"].Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
            string fraction = $" minimumFractionDigits={digits} maximumFractionDigits={digits}";
            return char.ToUpperInvariant(numeric.Groups["kind"].Value[0]) switch
            {
                'N' => " :number" + fraction,
                'P' => " :number style=percent" + fraction,
                _ => " :number" + fraction + " useGrouping=never",
            };
        }
        report.Notice(location, "RESX-FORMAT", $"The format '{format}' of {item} has no RMF2 equivalent and was dropped; add a function such as :date or :number.");
        return string.Empty;
    }
}

/// <summary>
/// Reads i18next JSON v4 files: nested keys as RMF2 groups, <c>{{name}}</c> interpolation as <c>{$name}</c>, and the
/// plural suffixes <c>_zero</c>, <c>_one</c>, <c>_two</c>, <c>_few</c>, <c>_many</c>, <c>_other</c> (and
/// <c>_ordinal_*</c>) as a <c>.match</c> on <c>$count</c>.
/// </summary>
internal static class I18nextImport
{
    private const string DefaultNamespace = "translation";
    private static readonly string[] Categories = ["zero", "one", "two", "few", "many", "other"];
    private static readonly Regex Interpolation = new(@"\{\{(?<raw>-)?\s*(?<body>[^{}]*?)\s*\}\}", RegexOptions.CultureInvariant);
    private static readonly Regex Nesting = new(@"\$t\(", RegexOptions.CultureInvariant);
    private static readonly Regex Markup = new(@"</?[A-Za-z0-9]+(\s[^<>]*)?/?>", RegexOptions.CultureInvariant);
    private static readonly Regex Formatted = new(@"^(?<name>[^,]+?)\s*,\s*(?<format>[A-Za-z]+)\s*(?<options>\(.*\))?(?<rest>\s*,.*)?$", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>The files to import from a JSON file or a <c>locales/</c> directory (<c>&lt;lng&gt;/&lt;ns&gt;.json</c> or <c>&lt;lng&gt;.json</c>).</summary>
    internal static List<ImportedFile> Read(string path, ImportLocales locales, ImportReport report)
    {
        string full = Path.GetFullPath(path);
        var candidates = new List<(string Path, string Locale, string? Namespace)>();
        if (File.Exists(full))
        {
            string stem = Path.GetFileNameWithoutExtension(full);
            string parent = Path.GetFileName(Path.GetDirectoryName(full)!);
            if (locales.IsLocale(stem)) candidates.Add((full, stem, null));
            else if (locales.IsLocale(parent)) candidates.Add((full, parent, stem));
            else if (locales.Single is { } single) candidates.Add((full, single, null));
            else throw new ToolUsageException($"the locale of '{path}' is not in its name (<lng>.json or <lng>/<namespace>.json); pass one --locale.");
        }
        else if (Directory.Exists(full))
        {
            foreach (string file in Directory.EnumerateFiles(full, "*.json").Order(StringComparer.Ordinal))
                if (locales.IsLocale(Path.GetFileNameWithoutExtension(file))) candidates.Add((file, Path.GetFileNameWithoutExtension(file), null));
            foreach (string directory in Directory.EnumerateDirectories(full).Order(StringComparer.Ordinal))
            {
                if (!locales.IsLocale(Path.GetFileName(directory))) continue;
                foreach (string file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
                    candidates.Add((file, Path.GetFileName(directory), Path.GetFileNameWithoutExtension(file)));
            }
            if (candidates.Count == 0)
                throw new ToolUsageException($"'{path}' contains no <lng>.json or <lng>/<namespace>.json files.");
        }
        else throw new ToolUsageException($"the file or directory '{path}' does not exist.");

        var files = new List<ImportedFile>();
        foreach ((string file, string name, string? ns) in candidates)
        {
            string? locale = locales.Declared(name);
            if (locale is null)
            {
                report.Notice(ImportMapping.Display(file), "IMPORT-LOCALE", $"The locale '{name}' is not declared in runic.json; the file was not imported.");
                continue;
            }
            if (!locales.Selected(locale)) continue;
            files.Add(ReadFile(file, locale, ns, report));
        }
        return files;
    }

    private static ImportedFile ReadFile(string path, string locale, string? ns, ImportReport report)
    {
        string display = ImportMapping.Display(path);
        byte[] bytes = ImportMapping.ReadBytes(path, display);
        JsonDocument document;
        try
        {
            new UTF8Encoding(false, true).GetString(bytes);
            document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            throw new ImportSourceException("I18NEXT-MALFORMED", $"{display}: not valid UTF-8 JSON ({exception.Message})");
        }
        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ImportSourceException("I18NEXT-MALFORMED", $"{display}: an i18next resource file is a JSON object.");
            // The default namespace stays at the top level; another namespace is a group of its name.
            var prefix = new List<string>();
            if (ns is not null && ns != DefaultNamespace)
            {
                string segment = ImportMapping.Segment(ns);
                if (segment != ns) report.Notice(display, "IMPORT-KEY-RENAMED", $"The namespace '{ns}' is the group '{segment}'.");
                prefix.Add(segment);
            }
            var flat = new List<(string Key, JsonElement Value)>();
            Flatten(document.RootElement, null, flat);
            var strings = new Dictionary<string, string>(StringComparer.Ordinal);
            int read = 0;
            foreach ((string key, JsonElement value) in flat)
            {
                read++;
                switch (value.ValueKind)
                {
                    case JsonValueKind.String:
                        strings[key] = value.GetString()!;
                        break;
                    case JsonValueKind.Array:
                        report.Skip(locale, key, "I18NEXT-ARRAY", "An array (returnObjects) is not a message; split it into keys.");
                        break;
                    default:
                        report.Skip(locale, key, "I18NEXT-NON-STRING", $"The value is a JSON {value.ValueKind.ToString().ToLowerInvariant()}, not text.");
                        break;
                }
            }
            return new ImportedFile(locale, display, Messages(strings, locale, prefix, display, report), read);
        }
    }

    private static void Flatten(JsonElement element, string? prefix, List<(string, JsonElement)> output)
    {
        foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
        {
            string key = prefix is null ? property.Name : prefix + "." + property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object) Flatten(property.Value, key, output);
            else output.Add((key, property.Value.Clone()));
        }
    }

    // Plural forms share a base key and need its _other form, as in i18next; the rest are plain messages.
    private static List<ImportedMessage> Messages(Dictionary<string, string> strings, string locale, List<string> prefix, string display, ImportReport report)
    {
        var plurals = new Dictionary<(string Base, bool Ordinal), Dictionary<string, string>>();
        foreach ((string key, string value) in strings)
            if (PluralForm(key) is { } form && strings.ContainsKey(form.Base + (form.Ordinal ? "_ordinal_other" : "_other")))
            {
                if (!plurals.TryGetValue((form.Base, form.Ordinal), out Dictionary<string, string>? forms)) plurals[(form.Base, form.Ordinal)] = forms = new(StringComparer.Ordinal);
                forms[form.Category] = value;
            }

        // The message name of each plural: <key>, or <key>_ordinal for an ordinal next to a cardinal of the same key.
        var names = plurals.Keys.ToDictionary(static plural => plural,
            plural => plural.Ordinal && plurals.ContainsKey((plural.Base, false)) ? plural.Base + "_ordinal" : plural.Base);
        var pluralNames = names.Values.ToHashSet(StringComparer.Ordinal);
        var messages = new List<ImportedMessage>();
        var done = new HashSet<(string, bool)>();
        foreach ((string key, string value) in strings)
        {
            string location = display + ":" + key;
            if (PluralForm(key) is { } form && plurals.TryGetValue((form.Base, form.Ordinal), out Dictionary<string, string>? forms))
            {
                if (!done.Add((form.Base, form.Ordinal))) continue;
                string name = names[(form.Base, form.Ordinal)];
                string? plural = Plural(forms, form.Ordinal, locale, display + ":" + form.Base + (form.Ordinal ? "_ordinal_*" : "_*"), report);
                if (plural is null) report.Refuse(locale, name, "I18NEXT-NESTING", "A plural form nests another key ($t(...)); RMF2 messages cannot reference each other.");
                else messages.Add(new ImportedMessage(name, KeyPath(prefix, name, display, report), plural, []));
                continue;
            }
            if (pluralNames.Contains(key))
            {
                report.Refuse(locale, key, "IMPORT-KEY-COLLISION", $"'{key}' is both a message and the name of plural forms {key}_*; the plain message was not imported.");
                continue;
            }
            if (PluralForm(key) is { } single)
                report.Notice(location, "I18NEXT-PLURAL-INCOMPLETE", $"'{key}' has no {single.Base}{(single.Ordinal ? "_ordinal" : string.Empty)}_other form, so it was imported as a plain message.");
            string? pattern = Simple(value, location, report);
            if (pattern is null) report.Refuse(locale, key, "I18NEXT-NESTING", "The message nests another key ($t(...)); RMF2 messages cannot reference each other.");
            else messages.Add(new ImportedMessage(key, KeyPath(prefix, key, display, report), ImportMapping.Frame(pattern), []));
        }
        return messages;
    }

    private static List<string> KeyPath(List<string> prefix, string key, string display, ImportReport report)
    {
        var segments = new List<string>(prefix);
        foreach (string part in key.Split('.'))
        {
            string segment = ImportMapping.Segment(part);
            segments.Add(segment);
        }
        string mapped = string.Join('.', segments.Skip(prefix.Count));
        if (mapped != key) report.Notice(display + ":" + key, "IMPORT-KEY-RENAMED", $"The key '{key}' is '{mapped}'.");
        return segments;
    }

    private static (string Base, bool Ordinal, string Category)? PluralForm(string key)
    {
        int separator = key.LastIndexOf('_');
        if (separator <= 0) return null;
        string category = key[(separator + 1)..];
        if (!Categories.Contains(category, StringComparer.Ordinal)) return null;
        string head = key[..separator];
        if (head.EndsWith("_ordinal", StringComparison.Ordinal) && head.Length > "_ordinal".Length)
            return (head[..^"_ordinal".Length], true, category);
        return (head, false, category);
    }

    // .match on $count. _zero is the CLDR zero category where the locale has one, else the exact value 0, which
    // i18next uses for a count of 0 in every language. Forms the locale does not use are dropped and reported.
    private static string? Plural(Dictionary<string, string> forms, bool ordinal, string locale, string location, ImportReport report)
    {
        string[] used = Rmf2AuthoringService.PluralCategories(locale, ordinal);
        var variants = new List<(string Key, string Pattern)>();
        foreach (string category in Categories)
        {
            if (!forms.TryGetValue(category, out string? text)) continue;
            string? pattern = Simple(text, location, report);
            if (pattern is null) return null;
            if (category == "other") continue;
            if (category == "zero" && !ordinal && !used.Contains("zero")) { variants.Insert(0, ("0", pattern)); continue; }
            if (used.Length != 0 && !used.Contains(category))
            {
                report.Notice(location, "I18NEXT-PLURAL-CATEGORY", $"{locale} has no {(ordinal ? "ordinal" : "plural")} category '{category}'; that form was dropped.");
                continue;
            }
            variants.Add((category, pattern));
        }
        foreach (string category in used)
            if (category != "other" && !forms.ContainsKey(category))
                report.Notice(location, "I18NEXT-PLURAL-MISSING", $"There is no _{(ordinal ? "ordinal_" : string.Empty)}{category} form for {locale}; the _other form is used.");
        string other = Simple(forms["other"], location, report)!;
        string declaration = ordinal ? ".input {$count :number select=ordinal}" : ".input {$count :number}";
        if (variants.Count == 0) return declaration + "\n{{" + other + "}}";
        var message = new StringBuilder(declaration).Append("\n.match $count\n");
        foreach ((string key, string pattern) in variants) message.Append(key).Append(" {{").Append(pattern).Append("}}\n");
        return message.Append("* {{").Append(other).Append("}}").ToString();
    }

    /// <summary>The pattern body of an i18next string, or <see langword="null"/> when it nests another key.</summary>
    private static string? Simple(string text, string location, ImportReport report)
    {
        if (Nesting.IsMatch(text)) return null;
        if (Markup.IsMatch(text))
            report.Notice(location, "I18NEXT-MARKUP", "HTML or <Trans> tags were kept as text; RMF2 markup ({#b}...{/b}) is typed instead.");
        var body = new StringBuilder(text.Length + 8);
        int start = 0;
        foreach (Match match in Interpolation.Matches(text))
        {
            ImportMapping.AppendText(body, text.AsSpan(start, match.Index - start));
            body.Append(Placeholder(match.Groups["body"].Value, location, report));
            start = match.Index + match.Length;
        }
        ImportMapping.AppendText(body, text.AsSpan(start));
        return body.ToString();
    }

    private static string Placeholder(string expression, string location, ImportReport report)
    {
        string name = expression, function = string.Empty;
        Match formatted = Formatted.Match(expression);
        if (formatted.Success)
        {
            name = formatted.Groups["name"].Value.Trim();
            string format = formatted.Groups["format"].Value;
            function = format switch
            {
                "number" => " :number",
                "datetime" => " :date style=short",
                "list" => " :runic:list",
                _ => string.Empty,
            };
            if (function.Length == 0)
                report.Notice(location, "I18NEXT-FORMAT", $"The format '{format}' of {{{{{expression}}}}} has no RMF2 equivalent and was dropped.");
            else if (formatted.Groups["options"].Success || formatted.Groups["rest"].Success)
                report.Notice(location, "I18NEXT-FORMAT", $"The format options of {{{{{expression}}}}} were dropped; add RMF2 options to the function.");
        }
        string variable = ImportMapping.Segment(name.Replace('.', '_'));
        if (variable != name) report.Notice(location, "I18NEXT-INTERPOLATION", $"The placeholder {{{{{name}}}}} is the input ${variable}.");
        return "{$" + variable + function + "}";
    }
}
