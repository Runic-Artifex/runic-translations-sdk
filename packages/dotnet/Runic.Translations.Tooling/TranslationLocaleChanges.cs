using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>
/// The sources of a translation project as read from disk. Source paths (<see cref="TranslationSource.Path"/>) are
/// relative to <see cref="BaseDirectory"/> or absolute; the command line uses its working directory, the Translations
/// Editor the directory it reports paths against.
/// </summary>
/// <param name="BaseDirectory">The directory relative source paths and created files are resolved against.</param>
/// <param name="Project">runic.json.</param>
/// <param name="Messages">The <c>.rmf2</c> and <c>.mf2</c> sources below the source roots.</param>
/// <param name="SourceRoots">The source roots of runic.json (absolute, or relative to <see cref="BaseDirectory"/>).</param>
internal sealed record TranslationProjectSources(string BaseDirectory, TranslationSource Project, IReadOnlyList<TranslationSource> Messages, IReadOnlyList<string> SourceRoots)
{
    /// <summary>The absolute path of a source path.</summary>
    internal string FullPath(string path) => Path.GetFullPath(path, Path.GetFullPath(BaseDirectory));

    /// <summary>A path as source paths are written: relative to <see cref="BaseDirectory"/>, with <c>/</c>.</summary>
    internal string DisplayPath(string fullPath) => Path.GetRelativePath(Path.GetFullPath(BaseDirectory), fullPath).Replace('\\', '/');
}

/// <summary>A source file a locale change creates: its absolute path and its content.</summary>
internal sealed record TranslationLocaleFile(string Path, byte[] Bytes);

/// <summary>
/// What <see cref="TranslationLocaleChanges"/> plans: the new runic.json and the files to create or delete. Nothing is
/// written; the caller applies the plan (files first when removing, so a failed delete leaves the locale declared).
/// </summary>
/// <param name="Locale">The canonical locale tag.</param>
/// <param name="Fallback">The locale it falls back to (add, set fallback), or the replacement fallback of its dependents (remove).</param>
/// <param name="ProjectPath">The absolute path of runic.json.</param>
/// <param name="ProjectBytes">The new runic.json, its formatting preserved.</param>
internal sealed record TranslationLocalePlan(string Locale, string? Fallback, string ProjectPath, byte[] ProjectBytes)
{
    /// <summary>The files to create, in order; empty or copied from <c>copyFrom</c>.</summary>
    internal IReadOnlyList<TranslationLocaleFile> CreatedFiles { get; init; } = [];

    /// <summary>The absolute paths of the locale's sources to delete.</summary>
    internal IReadOnlyList<string> DeletedFiles { get; init; } = [];

    /// <summary>The directories that hold nothing but deleted files, deepest first; deleted after the files.</summary>
    internal IReadOnlyList<string> EmptiedDirectories { get; init; } = [];

    /// <summary>The locales whose fallback the plan changes (remove with a replacement fallback, set fallback).</summary>
    internal IReadOnlyList<string> ChangedFallbacks { get; init; } = [];

    /// <summary>The review file of the catalog (remove), whether it exists or not.</summary>
    internal string? ReviewPath { get; init; }

    /// <summary>The review file as read (remove), or null when it does not exist.</summary>
    internal TranslationInterchangeReview? Review { get; init; }

    /// <summary>The review entries of the removed locale.</summary>
    internal int ReviewEntries { get; init; }

    /// <summary>The project after the change.</summary>
    internal required Rmf2ProjectCompilation Compilation { get; init; }

    /// <summary>The errors of <see cref="Compilation"/> other than RTR0010 (an untranslated message); the change must not be applied when there are any.</summary>
    internal IReadOnlyList<TranslationDiagnostic> Errors { get; init; } = [];

    /// <summary>The RTR0010 diagnostics of the locale: messages it does not translate yet.</summary>
    internal int Untranslated { get; init; }
}

/// <summary>
/// Declares, removes and re-links the locales of a translation project: <c>runic-translations locale add|remove</c> and
/// the Translations Editor. runic.json keeps its formatting; every edit replaces only the bytes of the changed entry.
/// </summary>
internal static class TranslationLocaleChanges
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private sealed record Manifest(TranslationSource Source, JsonValue Root, string BaseLocale, JsonProperty? Locales, JsonProperty BaseProperty);

    /// <summary>Plans adding <paramref name="tag"/>: its runic.json entry and its source files.</summary>
    /// <param name="fallback">The declared locale it falls back to; null for the base locale.</param>
    /// <param name="copyFrom">
    /// A declared locale whose sources seed the new ones; null creates empty <c>&lt;tag&gt;.rmf2</c> files and, for a
    /// project of direct <c>.mf2</c> files, none. With it, each created <c>.rmf2</c> file starts as a copy of the
    /// copy-from locale's file in the same directory, and each <c>.mf2</c> file of the copy-from locale is copied to
    /// <c>&lt;root&gt;/&lt;tag&gt;/</c>.
    /// </param>
    /// <exception cref="TranslationToolingUsageException">The project, the tag or a locale argument is invalid.</exception>
    internal static TranslationLocalePlan PlanAdd(TranslationProjectSources sources, string tag, string? fallback, string? copyFrom = null)
    {
        Manifest manifest = ReadManifest(sources.Project);
        string locale = Canonical(tag, "the locale");
        string target = fallback is null ? manifest.BaseLocale : Canonical(fallback, "--fallback");
        List<string> declared = Declared(sources, manifest);
        if (declared.Contains(locale, StringComparer.OrdinalIgnoreCase))
            throw new TranslationToolingUsageException($"locale '{locale}' is already declared.");
        if (!declared.Contains(target, StringComparer.OrdinalIgnoreCase))
            throw new TranslationToolingUsageException($"--fallback {target} is not a declared locale ({string.Join(", ", declared)}).");
        string? copy = null;
        if (copyFrom is not null)
        {
            string requested = Canonical(copyFrom, "the locale to copy");
            copy = declared.FirstOrDefault(item => string.Equals(item, requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new TranslationToolingUsageException($"the locale to copy, {requested}, is not a declared locale ({string.Join(", ", declared)}).");
        }

        // The entry: a plain tag when it falls back to the base locale and the list uses tags, else an object.
        bool objects = manifest.Locales?.Value.Items.Any(static item => item.Kind == JsonKind.Object) == true;
        string entry = target != manifest.BaseLocale || objects ? Entry(locale, target) : Quote(locale);
        byte[] bytes = manifest.Source.GetUtf8Bytes();
        string updated;
        if (manifest.Locales is { } locales && locales.Value.Kind == JsonKind.Array)
            updated = InsertItem(bytes, locales.Value, entry, NewLine(bytes));
        else
            updated = WithLocalesList(bytes, manifest, declared.Select(Quote).Append(entry));

        // An empty <tag>.rmf2 beside every base-locale .rmf2 file; direct .mf2 projects need no file yet.
        var created = new List<TranslationLocaleFile>();
        // The .rmf2 files of the copy-from locale by directory.
        var copies = new Dictionary<string, TranslationSource>(StringComparer.Ordinal);
        if (copy is not null)
            foreach (TranslationSource source in sources.Messages)
                if (source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) &&
                    TranslationCompiler.TryCanonicalizeLocale(Path.GetFileNameWithoutExtension(source.Path), out string owner) && string.Equals(owner, copy, StringComparison.OrdinalIgnoreCase))
                    copies.TryAdd(Path.GetDirectoryName(sources.FullPath(source.Path))!, source);
        foreach (TranslationSource source in sources.Messages)
        {
            if (!source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileNameWithoutExtension(source.Path), manifest.BaseLocale, StringComparison.OrdinalIgnoreCase)) continue;
            string directory = Path.GetDirectoryName(sources.FullPath(source.Path))!;
            string path = Path.Combine(directory, locale + ".rmf2");
            if (File.Exists(path) || created.Any(file => string.Equals(file.Path, path, StringComparison.Ordinal))) continue;
            // With copyFrom, the copy-from locale's file in the same directory, when there is one.
            created.Add(new(path, copies.GetValueOrDefault(directory)?.GetUtf8Bytes() ?? []));
        }
        if (copy is not null)
            foreach (TranslationSource source in sources.Messages)
            {
                if (source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)) continue;
                string full = sources.FullPath(source.Path);
                if (DirectLocale(full, sources) is not { } owner || !TranslationCompiler.TryCanonicalizeLocale(owner.Owner, out string canonical) ||
                    !string.Equals(canonical, copy, StringComparison.OrdinalIgnoreCase)) continue;
                string path = Path.Combine(Path.GetDirectoryName(owner.Directory)!, locale, Path.GetRelativePath(owner.Directory, full));
                if (!File.Exists(path) && !created.Any(file => string.Equals(file.Path, path, StringComparison.Ordinal))) created.Add(new(path, source.GetUtf8Bytes()));
            }

        // The project must still compile; a new locale only lacks translations (RTR0010).
        var project = new TranslationSource(manifest.Source.Path, Encoding.UTF8.GetBytes(updated));
        var messages = sources.Messages.Concat(created.Select(file => new TranslationSource(sources.DisplayPath(file.Path), file.Bytes))).ToArray();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, messages);
        return new TranslationLocalePlan(locale, target, sources.FullPath(manifest.Source.Path), Encoding.UTF8.GetBytes(updated))
        {
            CreatedFiles = created,
            Compilation = compilation,
            Errors = Errors(compilation),
            Untranslated = Untranslated(compilation, locale),
        };
    }

    /// <summary>Plans removing <paramref name="tag"/>: its runic.json entry, its source files and its review entries.</summary>
    /// <param name="replacementFallback">
    /// The declared locale that the locales falling back to <paramref name="tag"/> fall back to instead; null refuses
    /// to remove a locale that another locale falls back to.
    /// </param>
    /// <exception cref="TranslationToolingUsageException">The project or the tag is invalid, or the locale cannot be removed.</exception>
    /// <exception cref="TranslationInterchangeException">The review file cannot be read.</exception>
    internal static TranslationLocalePlan PlanRemove(TranslationProjectSources sources, string tag, string? replacementFallback = null)
    {
        Manifest manifest = ReadManifest(sources.Project);
        string locale = Canonical(tag, "the locale");
        if (string.Equals(locale, manifest.BaseLocale, StringComparison.OrdinalIgnoreCase))
            throw new TranslationToolingUsageException($"'{locale}' is the base locale and cannot be removed.");
        List<string> declared = Declared(sources, manifest);
        if (!declared.Contains(locale, StringComparer.OrdinalIgnoreCase))
            throw new TranslationToolingUsageException($"locale '{locale}' is not declared ({string.Join(", ", declared)}).");
        string[] dependents = (manifest.Locales?.Value.Items ?? []).Where(static item => item.Kind == JsonKind.Object)
            .Where(item => item.Property("fallback")?.Value.Text is { } value && TranslationCompiler.TryCanonicalizeLocale(value, out string canonical) && string.Equals(canonical, locale, StringComparison.OrdinalIgnoreCase))
            .Select(static item => item.Property("tag")?.Value.Text ?? "?").ToArray();
        string? replacement = null;
        if (replacementFallback is not null)
        {
            string requested = Canonical(replacementFallback, "the replacement fallback");
            replacement = declared.FirstOrDefault(item => string.Equals(item, requested, StringComparison.OrdinalIgnoreCase))
                ?? throw new TranslationToolingUsageException($"the replacement fallback {requested} is not a declared locale ({string.Join(", ", declared)}).");
            if (string.Equals(replacement, locale, StringComparison.OrdinalIgnoreCase) ||
                dependents.Any(dependent => TranslationCompiler.TryCanonicalizeLocale(dependent, out string canonical) && string.Equals(canonical, replacement, StringComparison.OrdinalIgnoreCase)))
                throw new TranslationToolingUsageException($"the replacement fallback {replacement} falls back to '{locale}' itself; choose another locale.");
        }
        else if (dependents.Length != 0)
            throw new TranslationToolingUsageException($"locale '{locale}' is the fallback of {string.Join(", ", dependents)}; remove or change those first.");

        byte[] bytes = manifest.Source.GetUtf8Bytes();
        string updated = StrictUtf8.GetString(bytes);
        var changedFallbacks = new List<string>();
        if (replacement is not null && dependents.Length != 0)
        {
            // Each dependent falls back to the replacement; one edit at a time, re-reading the entries in between.
            foreach (string dependent in dependents)
            {
                Manifest current = ReadManifest(new TranslationSource(manifest.Source.Path, Encoding.UTF8.GetBytes(updated)));
                string canonical = Canonical(dependent, "the locale");
                updated = SetFallback(current, Declared(sources, current), canonical, replacement);
                changedFallbacks.Add(canonical);
            }
            bytes = Encoding.UTF8.GetBytes(updated);
            manifest = ReadManifest(new TranslationSource(manifest.Source.Path, bytes));
        }
        if (manifest.Locales is { } locales && locales.Value.Kind == JsonKind.Array)
        {
            int index = IndexOf(locales.Value, locale);
            if (index >= 0) updated = RemoveItem(bytes, locales.Value, index);
        }

        // The locale's sources: <tag>.rmf2 files, or the .mf2 files below a <tag>/ directory of a source root.
        var deleted = new List<string>();
        // The directories between a direct .mf2 file and its <tag>/ directory, which go when they become empty.
        var directories = new List<string>();
        foreach (TranslationSource source in sources.Messages)
        {
            string full = sources.FullPath(source.Path);
            bool grouped = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
            (string Owner, string Directory)? direct = grouped ? null : DirectLocale(full, sources);
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

        string catalog = ReadCatalog(manifest);
        string reviewPath = ReviewPath(sources, catalog);
        TranslationInterchangeReview? review = TranslationReviewFile.Read(reviewPath, catalog);
        int reviewEntries = review?.Entries.Count(entry => string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase)) ?? 0;

        var project = new TranslationSource(manifest.Source.Path, Encoding.UTF8.GetBytes(updated));
        TranslationSource[] remaining = sources.Messages.Where(source => !deletedSet.Contains(sources.FullPath(source.Path))).ToArray();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, remaining);
        return new TranslationLocalePlan(locale, replacement, sources.FullPath(manifest.Source.Path), Encoding.UTF8.GetBytes(updated))
        {
            DeletedFiles = deleted,
            EmptiedDirectories = emptied.OrderByDescending(static directory => directory.Length).ToArray(),
            ChangedFallbacks = changedFallbacks,
            ReviewPath = reviewPath,
            Review = review,
            ReviewEntries = reviewEntries,
            Compilation = compilation,
            Errors = Errors(compilation),
        };
    }

    /// <summary>
    /// Plans changing the fallback of the declared locale <paramref name="tag"/> to <paramref name="fallback"/>: a plain
    /// <c>"tag"</c> entry becomes <c>{ "tag": …, "fallback": … }</c>, and an object entry of only those two members
    /// becomes the plain tag again when the fallback is the base locale and the other entries are plain tags.
    /// </summary>
    /// <exception cref="TranslationToolingUsageException">The project or a locale is invalid, or the fallback would form a cycle.</exception>
    internal static TranslationLocalePlan PlanSetFallback(TranslationProjectSources sources, string tag, string fallback)
    {
        Manifest manifest = ReadManifest(sources.Project);
        string locale = Canonical(tag, "the locale");
        string target = Canonical(fallback, "the fallback");
        List<string> declared = Declared(sources, manifest);
        locale = declared.FirstOrDefault(item => string.Equals(item, locale, StringComparison.OrdinalIgnoreCase))
            ?? throw new TranslationToolingUsageException($"locale '{locale}' is not declared ({string.Join(", ", declared)}).");
        target = declared.FirstOrDefault(item => string.Equals(item, target, StringComparison.OrdinalIgnoreCase))
            ?? throw new TranslationToolingUsageException($"the fallback {target} is not a declared locale ({string.Join(", ", declared)}).");
        if (string.Equals(locale, manifest.BaseLocale, StringComparison.Ordinal))
            throw new TranslationToolingUsageException($"'{locale}' is the base locale and has no fallback.");
        if (string.Equals(locale, target, StringComparison.Ordinal))
            throw new TranslationToolingUsageException($"locale '{locale}' cannot fall back to itself.");

        string updated = SetFallback(manifest, declared, locale, target);
        var project = new TranslationSource(manifest.Source.Path, Encoding.UTF8.GetBytes(updated));
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, sources.Messages);
        return new TranslationLocalePlan(locale, target, sources.FullPath(manifest.Source.Path), Encoding.UTF8.GetBytes(updated))
        {
            ChangedFallbacks = [locale],
            Compilation = compilation,
            Errors = Errors(compilation),
            Untranslated = Untranslated(compilation, locale),
        };
    }

    /// <summary>The review file of <paramref name="catalog"/> beside runic.json.</summary>
    /// <exception cref="TranslationToolingUsageException">The catalog identifier cannot name a file.</exception>
    internal static string ReviewPath(TranslationProjectSources sources, string catalog)
    {
        if (!TranslationReviewFile.IsFileName(catalog))
            throw new TranslationToolingUsageException($"the catalog identifier '{catalog}' cannot name a review file.");
        return TranslationReviewFile.PathFor(sources.FullPath(sources.Project.Path), catalog);
    }

    /// <summary>Whether <paramref name="path"/> lies inside <paramref name="directory"/>.</summary>
    internal static bool IsWithin(string path, string directory)
    {
        string relative = Path.GetRelativePath(directory, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    private static TranslationDiagnostic[] Errors(Rmf2ProjectCompilation compilation) =>
        compilation.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error && diagnostic.Id != "RTR0010").ToArray();

    private static int Untranslated(Rmf2ProjectCompilation compilation, string locale) =>
        compilation.Diagnostics.Count(diagnostic => diagnostic.Id == "RTR0010" && diagnostic.Message.StartsWith("Locale '" + locale + "'", StringComparison.Ordinal));

    private static Manifest ReadManifest(TranslationSource source)
    {
        var diagnostics = new DiagnosticBag();
        ParsedJson parsed = StrictJsonParser.Parse(source, diagnostics, new TranslationCompilerOptions(), default);
        if (parsed.Root is not { Kind: JsonKind.Object } root || diagnostics.Items.Any(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error))
            throw new TranslationToolingUsageException($"{source.Path} is not a valid runic.json; run runic-translations validate.");
        JsonProperty baseProperty = root.Property("baseLocale") is { Value.Kind: JsonKind.String } found ? found
            : throw new TranslationToolingUsageException($"{source.Path} has no baseLocale.");
        if (!TranslationCompiler.TryCanonicalizeLocale(baseProperty.Value.Text!, out string baseLocale))
            throw new TranslationToolingUsageException($"{source.Path} has an invalid baseLocale; run runic-translations validate.");
        return new(source, root, baseLocale, root.Property("locales"), baseProperty);
    }

    private static string ReadCatalog(Manifest manifest) =>
        manifest.Root.Property("catalog")?.Value.Text ?? throw new TranslationToolingUsageException($"{manifest.Source.Path} has no catalog.");

    // The declared locales: runic.json's list, or without one those the compiler finds in the sources.
    private static List<string> Declared(TranslationProjectSources sources, Manifest manifest)
    {
        if (manifest.Locales is { Value.Kind: JsonKind.Array } locales)
            return locales.Value.Items.Select(static item => item.Kind == JsonKind.Object ? item.Property("tag")?.Value.Text : item.Text)
                .Select(static value => value is not null && TranslationCompiler.TryCanonicalizeLocale(value, out string canonical) ? canonical : null)
                .OfType<string>().ToList();
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(manifest.Source, sources.Messages);
        var found = compilation.Locales.ToList();
        if (!found.Contains(manifest.BaseLocale, StringComparer.OrdinalIgnoreCase)) found.Insert(0, manifest.BaseLocale);
        return found.OrderBy(locale => locale == manifest.BaseLocale ? 0 : 1).ThenBy(static locale => locale, StringComparer.Ordinal).ToList();
    }

    private static string Canonical(string value, string what) =>
        TranslationCompiler.TryCanonicalizeLocale(value.Trim(), out string canonical) ? canonical
            : throw new TranslationToolingUsageException($"{what} '{value}' is not a valid BCP 47 locale tag.");

    private static int IndexOf(JsonValue array, string locale) =>
        array.Items.ToList().FindIndex(item => TranslationCompiler.TryCanonicalizeLocale((item.Kind == JsonKind.Object ? item.Property("tag")?.Value.Text : item.Text) ?? "", out string canonical) &&
            string.Equals(canonical, locale, StringComparison.OrdinalIgnoreCase));

    // The text of runic.json with the fallback of the declared locale changed.
    private static string SetFallback(Manifest manifest, List<string> declared, string locale, string fallback)
    {
        byte[] bytes = manifest.Source.GetUtf8Bytes();
        bool toBase = string.Equals(fallback, manifest.BaseLocale, StringComparison.Ordinal);
        if (manifest.Locales is not { Value.Kind: JsonKind.Array } locales)
        {
            // Without "locales" every locale falls back to the base locale; another fallback needs the list.
            if (toBase) return StrictUtf8.GetString(bytes);
            return WithLocalesList(bytes, manifest, declared.Select(item => item == locale ? Entry(locale, fallback) : Quote(item)));
        }
        JsonValue array = locales.Value;
        int index = IndexOf(array, locale);
        if (index < 0) throw new TranslationToolingUsageException($"locale '{locale}' is not declared in {manifest.Source.Path}.");
        JsonValue item = array.Items[index];
        bool othersPlain = array.Items.Where((_, position) => position != index).All(static other => other.Kind == JsonKind.String);
        if (item.Kind != JsonKind.Object)
            // A plain tag falls back to the base locale already.
            return toBase ? StrictUtf8.GetString(bytes) : Replace(bytes, item.Span, Entry(locale, fallback));
        if (toBase && othersPlain && item.Properties.Count == 2 && item.Property("tag") is not null && item.Property("fallback") is not null)
            return Replace(bytes, item.Span, Quote(locale));
        if (item.Property("fallback") is { } existing)
            return Replace(bytes, existing.Value.Span, Quote(fallback));
        // An object without "fallback": the member goes after "tag" (or first), in the object's own layout.
        JsonValue anchor = item.Property("tag")?.Value ?? item.Properties[^1].Value;
        int end = anchor.Span.Start + anchor.Span.Length;
        return Encoding.UTF8.GetString(bytes, 0, end) + ", \"fallback\": " + Quote(fallback) + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
    }

    private static string Replace(byte[] bytes, ByteSpan span, string text) =>
        Encoding.UTF8.GetString(bytes, 0, span.Start) + text + Encoding.UTF8.GetString(bytes, span.Start + span.Length, bytes.Length - span.Start - span.Length);

    // Without "locales" the compiler declares the locales it finds; the list now names them explicitly.
    private static string WithLocalesList(byte[] bytes, Manifest manifest, IEnumerable<string> entries)
    {
        string indent = LineIndent(bytes, manifest.BaseProperty.NameSpan.Start);
        int end = manifest.BaseProperty.Value.Span.Start + manifest.BaseProperty.Value.Span.Length;
        string list = "[" + string.Join(", ", entries) + "]";
        return Encoding.UTF8.GetString(bytes, 0, end) + "," + NewLine(bytes) + indent + "\"locales\": " + list + Encoding.UTF8.GetString(bytes, end, bytes.Length - end);
    }

    private static string Entry(string locale, string fallback) => "{ \"tag\": " + Quote(locale) + ", \"fallback\": " + Quote(fallback) + " }";

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
    private static (string Owner, string Directory)? DirectLocale(string path, TranslationProjectSources sources)
    {
        foreach (string root in sources.SourceRoots)
        {
            string full = sources.FullPath(root);
            string relative = Path.GetRelativePath(full, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return segments.Length > 1 ? (segments[0], Path.Combine(full, segments[0])) : null;
        }
        return null;
    }

    private static string Quote(string value) => "\"" + value + "\"";
}
