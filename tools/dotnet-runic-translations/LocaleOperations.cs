using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.CommandLine;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary><c>runic-translations locale add|remove</c>: declare or drop a locale in runic.json and its source files.</summary>
[CommandGroup(Group, Description = "Add or remove a locale of a translation project.")]
internal static class LocaleCommandModule
{
    internal const string Group = "locale";
    private const string ProjectDescription = "Translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.";

    [Command("locale add", Description = "Declare a locale in runic.json and create its empty source files.",
        LongDescription = "Adds the locale to runic.json's locales (as \"tag\", or { \"tag\", \"fallback\" } with --fallback or when the other entries are objects) and creates an empty <tag>.rmf2 beside every base-locale .rmf2 file. runic.json keeps its formatting. The locale starts untranslated: validate reports RTR0010 for each message until it is translated, or set the locale entry's validation.translationCompleteness to warning. Nothing is written when the result would not compile.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation, an invalid or declared tag, or translation diagnostics; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations locale add fr", "runic-translations locale add de-CH --fallback de", "runic-translations locale add pt-BR --project src/App/translations --dry-run"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Add(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Required = true, ValueName = "tag", Description = "The BCP 47 locale tag to add, such as fr or de-CH.")] string tag,
        [Option("--fallback", ValueName = "locale", Description = "The declared locale it falls back to; defaults to the base locale.")] string? fallback,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--dry-run", Description = "Report the changes without writing them.")] bool dryRun) =>
        operations.Execute(new("locale add", Project: project, DryRun: dryRun) { Tag = tag, Fallback = fallback });

    [Command("locale remove", Description = "Remove a locale from runic.json, delete its source files and its review entries.",
        LongDescription = "Removes the locale's entry from runic.json (keeping its formatting), deletes its <tag>.rmf2 files or <tag>/ .mf2 files and its entries in <catalog>.review.json. The base locale and a locale that another locale falls back to cannot be removed. The files are deleted first, then runic.json is updated; if a file cannot be deleted, the locale stays declared and the command can be run again. Use --dry-run to list the files and directories first; files outside the runic.json directory (from sourceRoots) are marked.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation or a locale that cannot be removed; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations locale remove fr --dry-run", "runic-translations locale remove fr"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Remove(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Required = true, ValueName = "tag", Description = "The declared locale to remove.")] string tag,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--dry-run", Description = "Report the changes without writing or deleting anything.")] bool dryRun) =>
        operations.Execute(new("locale remove", Project: project, DryRun: dryRun) { Tag = tag });
}

internal static class LocaleOperations
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private sealed record Manifest(TranslationSource Source, JsonValue Root, string BaseLocale, JsonProperty? Locales, JsonProperty BaseProperty);

    internal static int Add(string projectPath, string tag, string? fallback, bool dryRun, ToolOperationResult result)
    {
        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        Manifest manifest = ReadManifest(inputs);
        string locale = Canonical(tag, "the locale");
        string target = fallback is null ? manifest.BaseLocale : Canonical(fallback, "--fallback");
        List<string> declared = Declared(inputs, manifest);
        if (declared.Contains(locale, StringComparer.OrdinalIgnoreCase))
            throw new ToolUsageException($"locale '{locale}' is already declared.");
        if (!declared.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new ToolUsageException($"--fallback {target} is not a declared locale ({string.Join(", ", declared)}).");

        // The entry: a plain tag when it falls back to the base locale and the list uses tags, else an object.
        bool objects = manifest.Locales?.Value.Items.Any(static item => item.Kind == JsonKind.Object) == true;
        string entry = target != manifest.BaseLocale || objects ? "{ \"tag\": " + Quote(locale) + ", \"fallback\": " + Quote(target) + " }" : Quote(locale);
        byte[] bytes = manifest.Source.GetUtf8Bytes();
        string updated;
        if (manifest.Locales is { } locales && locales.Value.Kind == JsonKind.Array)
            updated = InsertItem(bytes, locales.Value, entry, NewLine(bytes));
        else
        {
            // Without "locales" the compiler declares the locales it finds; the list now names them explicitly.
            string indent = LineIndent(bytes, manifest.BaseProperty.NameSpan.Start);
            int end = manifest.BaseProperty.Value.Span.Start + manifest.BaseProperty.Value.Span.Length;
            string list = "[" + string.Join(", ", declared.Select(Quote).Append(entry)) + "]";
            updated = Encoding.UTF8.GetString(bytes, 0, end) + "," + NewLine(bytes) + indent + "\"locales\": " + list + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
        }

        // An empty <tag>.rmf2 beside every base-locale .rmf2 file; direct .mf2 projects need no file yet.
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        var created = new List<string>();
        foreach (TranslationSource source in inputs.Messages)
        {
            if (!source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileNameWithoutExtension(source.Path), manifest.BaseLocale, StringComparison.OrdinalIgnoreCase)) continue;
            string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source.Path, current))!, locale + ".rmf2");
            if (!File.Exists(path) && !created.Contains(path, StringComparer.Ordinal)) created.Add(path);
        }

        // The project must still compile; a new locale only lacks translations (RTR0010).
        var project = new TranslationSource(manifest.Source.Path, Encoding.UTF8.GetBytes(updated));
        var messages = inputs.Messages.Concat(created.Select(path => new TranslationSource(InputFiles.DisplayPath(path, current), []))).ToArray();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, messages);
        TranslationDiagnostic[] errors = compilation.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error && diagnostic.Id != "RTR0010").ToArray();
        if (errors.Length != 0)
        {
            Program.ReportDiagnostics(errors, result);
            result.WriteOutputLine($"locale '{locale}' was not added: the project would not compile.");
            return 1;
        }
        int untranslated = compilation.Diagnostics.Count(diagnostic => diagnostic.Id == "RTR0010" && diagnostic.Message.StartsWith("Locale '" + locale + "'", StringComparison.Ordinal));
        if (!dryRun)
        {
            InterchangeOperations.WriteFile(Path.GetFullPath(manifest.Source.Path, current), Encoding.UTF8.GetBytes(updated));
            foreach (string path in created) InterchangeOperations.WriteFile(path, []);
        }
        string verb = dryRun ? "would add" : "added";
        result.WriteOutputLine($"{verb} locale {locale} (fallback {target}) to {manifest.Source.Path}.");
        foreach (string path in created) result.WriteOutputLine($"{(dryRun ? "would create" : "created")} {InputFiles.DisplayPath(path, current)}.");
        if (untranslated != 0 || messages.Length != 0)
            result.WriteOutputLine($"The messages of {locale} are not translated yet; validate reports RTR0010 for them until they are, unless the locale entry sets validation.translationCompleteness.");
        return 0;
    }

    internal static int Remove(string projectPath, string tag, bool dryRun, ToolOperationResult result)
    {
        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        Manifest manifest = ReadManifest(inputs);
        string locale = Canonical(tag, "the locale");
        if (string.Equals(locale, manifest.BaseLocale, StringComparison.OrdinalIgnoreCase))
            throw new ToolUsageException($"'{locale}' is the base locale and cannot be removed.");
        List<string> declared = Declared(inputs, manifest);
        if (!declared.Contains(locale, StringComparer.OrdinalIgnoreCase))
            throw new ToolUsageException($"locale '{locale}' is not declared ({string.Join(", ", declared)}).");
        string[] dependents = (manifest.Locales?.Value.Items ?? []).Where(static item => item.Kind == JsonKind.Object)
            .Where(item => item.Property("fallback")?.Value.Text is { } value && TranslationCompiler.TryCanonicalizeLocale(value, out string canonical) && string.Equals(canonical, locale, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Property("tag")?.Value.Text ?? "?").ToArray();
        if (dependents.Length != 0)
            throw new ToolUsageException($"locale '{locale}' is the fallback of {string.Join(", ", dependents)}; remove or change those first.");

        byte[] bytes = manifest.Source.GetUtf8Bytes();
        string updated = StrictUtf8.GetString(bytes);
        if (manifest.Locales is { } locales && locales.Value.Kind == JsonKind.Array)
        {
            int index = locales.Value.Items.ToList().FindIndex(item => TranslationCompiler.TryCanonicalizeLocale((item.Kind == JsonKind.Object ? item.Property("tag")?.Value.Text : item.Text) ?? "", out string canonical) &&
                string.Equals(canonical, locale, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) updated = RemoveItem(bytes, locales.Value, index);
        }

        // The locale's sources: <tag>.rmf2 files, or the .mf2 files below a <tag>/ directory of a source root.
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        var deleted = new List<string>();
        // The directories between a direct .mf2 file and its <tag>/ directory, which go when they become empty.
        var directories = new List<string>();
        foreach (TranslationSource source in inputs.Messages)
        {
            string full = Path.GetFullPath(source.Path, current);
            bool grouped = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
            (string Owner, string Directory)? direct = grouped ? null : DirectLocale(full, inputs.SourceRoots ?? []);
            string? owner = grouped ? Path.GetFileNameWithoutExtension(full) : direct?.Owner;
            if (owner is null || !TranslationCompiler.TryCanonicalizeLocale(owner, out string canonical) || !string.Equals(canonical, locale, StringComparison.OrdinalIgnoreCase)) continue;
            deleted.Add(full);
            if (direct is { } localeDirectory)
                for (string? directory = Path.GetDirectoryName(full); directory is not null && directory.Length >= localeDirectory.Directory.Length; directory = Path.GetDirectoryName(directory))
                {
                    if (!directories.Contains(directory, StringComparer.Ordinal)) directories.Add(directory);
                    if (string.Equals(directory, localeDirectory.Directory, StringComparison.Ordinal)) break;
                }
        }
        // Deepest first, and only those that hold nothing but what is deleted.
        var deletedSet = new HashSet<string>(deleted, StringComparer.Ordinal);
        var emptied = new HashSet<string>(StringComparer.Ordinal);
        foreach (string directory in directories.OrderByDescending(static directory => directory.Length))
            if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).All(entry => deletedSet.Contains(entry) || emptied.Contains(entry))) emptied.Add(directory);

        string reviewPath = ReviewFile.PathFor(inputs, ReadCatalog(manifest));
        TranslationInterchangeReview? review = ReviewFile.Read(reviewPath, ReadCatalog(manifest));
        int reviewEntries = review?.Entries.Count(entry => string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase)) ?? 0;
        string projectDirectory = Path.GetDirectoryName(Path.GetFullPath(manifest.Source.Path, current))!;
        if (!dryRun)
        {
            // The files go first: if one cannot be deleted, runic.json still declares the locale, and running the
            // command again finishes the removal. A read-only file is refused before anything is deleted.
            foreach (string path in deleted)
                if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReadOnly))
                    throw new IOException($"{InputFiles.DisplayPath(path, current)} is read-only; nothing was deleted.");
            foreach (string path in deleted) File.Delete(path);
            foreach (string directory in emptied.OrderByDescending(static directory => directory.Length))
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            InterchangeOperations.WriteFile(Path.GetFullPath(manifest.Source.Path, current), Encoding.UTF8.GetBytes(updated));
            if (review is not null && reviewEntries != 0)
                ReviewFile.Write(reviewPath, new TranslationInterchangeReview(review.CatalogId, review.Entries.Where(entry => !string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))));
        }
        result.WriteOutputLine($"{(dryRun ? "would remove" : "removed")} locale {locale} from {manifest.Source.Path}.");
        // Sources outside the runic.json directory (sourceRoots such as ../shared) are part of the project, and marked.
        foreach (string path in deleted)
            result.WriteOutputLine($"{(dryRun ? "would delete" : "deleted")} {InputFiles.DisplayPath(path, current)}{(IsWithin(path, projectDirectory) ? "" : " (outside the project directory)")}.");
        foreach (string directory in emptied.OrderByDescending(static directory => directory.Length))
            result.WriteOutputLine($"{(dryRun ? "would remove the directory" : "removed the directory")} {InputFiles.DisplayPath(directory, current)}/{(IsWithin(directory, projectDirectory) ? "" : " (outside the project directory)")}.");
        if (reviewEntries != 0) result.WriteOutputLine($"{(dryRun ? "would remove" : "removed")} {reviewEntries} review entr{(reviewEntries == 1 ? "y" : "ies")} from {InputFiles.DisplayPath(reviewPath, current)}.");
        return 0;
    }

    private static Manifest ReadManifest(CompilerInputs inputs)
    {
        var diagnostics = new DiagnosticBag();
        ParsedJson parsed = StrictJsonParser.Parse(inputs.Project, diagnostics, new TranslationCompilerOptions(), default);
        if (parsed.Root is not { Kind: JsonKind.Object } root || diagnostics.Items.Any(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error))
            throw new ToolUsageException($"{inputs.Project.Path} is not a valid runic.json; run runic-translations validate.");
        JsonProperty baseProperty = root.Property("baseLocale") is { Value.Kind: JsonKind.String } found ? found
            : throw new ToolUsageException($"{inputs.Project.Path} has no baseLocale.");
        if (!TranslationCompiler.TryCanonicalizeLocale(baseProperty.Value.Text!, out string baseLocale))
            throw new ToolUsageException($"{inputs.Project.Path} has an invalid baseLocale; run runic-translations validate.");
        return new(inputs.Project, root, baseLocale, root.Property("locales"), baseProperty);
    }

    private static string ReadCatalog(Manifest manifest) =>
        manifest.Root.Property("catalog")?.Value.Text ?? throw new ToolUsageException($"{manifest.Source.Path} has no catalog.");

    // The declared locales: runic.json's list, or without one those the compiler finds in the sources.
    private static List<string> Declared(CompilerInputs inputs, Manifest manifest)
    {
        if (manifest.Locales is { Value.Kind: JsonKind.Array } locales)
            return locales.Value.Items.Select(static item => item.Kind == JsonKind.Object ? item.Property("tag")?.Value.Text : item.Text)
                .Select(static value => value is not null && TranslationCompiler.TryCanonicalizeLocale(value, out string canonical) ? canonical : null)
                .OfType<string>().ToList();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, inputs.Messages);
        var found = compilation.Locales.ToList();
        if (!found.Contains(manifest.BaseLocale, StringComparer.OrdinalIgnoreCase)) found.Insert(0, manifest.BaseLocale);
        return found.OrderBy(locale => locale == manifest.BaseLocale ? 0 : 1).ThenBy(static locale => locale, StringComparer.Ordinal).ToList();
    }

    private static string Canonical(string value, string what) =>
        TranslationCompiler.TryCanonicalizeLocale(value.Trim(), out string canonical) ? canonical
            : throw new ToolUsageException($"{what} '{value}' is not a valid BCP 47 locale tag.");

    // Inserts an item after the last one: on its own line, indented like the last item, when the list is one per line.
    private static string InsertItem(byte[] bytes, JsonValue array, string entry, string newLine)
    {
        int close = array.Span.Start + array.Span.Length - 1;
        if (array.Items.Count == 0)
            return Encoding.UTF8.GetString(bytes, 0, close) + entry + Encoding.UTF8.GetString(bytes, close, bytes.Length - close);
        JsonValue last = array.Items[^1];
        int end = last.Span.Start + last.Span.Length;
        bool multiline = Encoding.UTF8.GetString(bytes, array.Span.Start, array.Items[0].Span.Start - array.Span.Start).Contains('\n');
        string separator = multiline ? "," + newLine + LineIndent(bytes, last.Span.Start) : ", ";
        return Encoding.UTF8.GetString(bytes, 0, end) + separator + entry + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
    }

    // Removes an item with the separator before it (or, for the first item, the one after it).
    private static string RemoveItem(byte[] bytes, JsonValue array, int index)
    {
        JsonValue item = array.Items[index];
        int from, to;
        if (index > 0) { JsonValue previous = array.Items[index - 1]; from = previous.Span.Start + previous.Span.Length; to = item.Span.Start + item.Span.Length; }
        else if (array.Items.Count > 1) { from = item.Span.Start; to = array.Items[1].Span.Start; }
        else { from = item.Span.Start; to = item.Span.Start + item.Span.Length; }
        return Encoding.UTF8.GetString(bytes, 0, from) + Encoding.UTF8.GetString(bytes, to, bytes.Length - to);
    }

    // The file's own line ending: CRLF when its first line ends with one, so an edit adds no mixed endings.
    private static string NewLine(byte[] bytes)
    {
        int lf = Array.IndexOf(bytes, (byte)'\n');
        return lf > 0 && bytes[lf - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    private static string LineIndent(byte[] bytes, int position)
    {
        int start = position;
        while (start > 0 && bytes[start - 1] != (byte)'\n') start--;
        int end = start;
        while (end < position && bytes[end] is (byte)' ' or (byte)'\t') end++;
        return Encoding.UTF8.GetString(bytes, start, end - start);
    }

    // The locale of a direct .mf2 source and its directory: the first directory below the source root that contains it.
    private static (string Owner, string Directory)? DirectLocale(string path, IReadOnlyList<string> roots)
    {
        foreach (string root in roots)
        {
            string full = Path.GetFullPath(root);
            string relative = Path.GetRelativePath(full, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Length > 1 ? (segments[0], Path.Combine(full, segments[0])) : null;
        }
        return null;
    }

    private static bool IsWithin(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    private static string Quote(string value) => "\"" + value + "\"";
}
