using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>Who provides a locale of a library catalog in an application.</summary>
internal enum TranslationLibraryLocaleOrigin
{
    /// <summary>The library declares the locale.</summary>
    Library,
    /// <summary>The application adds it with an override source (<c>overrides/&lt;catalog&gt;/&lt;locale&gt;.rmf2</c>).</summary>
    Application,
}

/// <summary>
/// A locale of a library catalog as the application sees it, with its key coverage against the library's contract.
/// </summary>
/// <param name="Fallback">The locale it falls back to: the library's declared fallback, or for an added locale the one fallback hop
/// the compiler and both runtimes choose (its nearest CLDR parent among the library's and the added locales, else the library
/// locale CLDR likely subtags match, else the library's base locale; D055.3).</param>
/// <param name="Messages">The library's messages (the contract's keys).</param>
/// <param name="LibraryTranslated">The messages the library translates in this locale itself.</param>
/// <param name="Overridden">The messages the application overrides in this locale.</param>
/// <param name="Covered">The messages shown in this locale's own text: the library's or the application's.</param>
/// <param name="Pending">Whether the locale is added by an override source without messages yet, which the build does not add.</param>
internal sealed record TranslationLibraryLocale(string Tag, string? Fallback, TranslationLibraryLocaleOrigin Origin, int Messages, int LibraryTranslated, int Overridden, int Covered, bool Pending);

/// <summary>A message of a library catalog: its contract and the library's text per locale.</summary>
/// <param name="Key">The canonical key (<c>grid_title</c>).</param>
/// <param name="LogicalPath">The logical path override sources use (<c>grid.title</c> → <c>["grid", "title"]</c>).</param>
/// <param name="Inputs">The inputs as <c>$name: type</c>.</param>
/// <param name="Content">The content profile (<c>inline</c>, <c>document</c>).</param>
/// <param name="Structured">Whether the message has markup in any of the library's locales.</param>
/// <param name="BaseText">The library's base-locale message text.</param>
/// <param name="Texts">The library's own text per locale (direct resources only).</param>
/// <param name="Locations">Where the library defines the message per locale (packed source and position).</param>
/// <param name="Fingerprint">The per-unit source fingerprint of the base message, which an override records.</param>
internal sealed record TranslationLibraryMessage(string Key, IReadOnlyList<string> LogicalPath, IReadOnlyList<string> Inputs, string Content, bool Structured,
    string BaseText, string? Description, IReadOnlyDictionary<string, string> Texts, IReadOnlyDictionary<string, TextSourceLocation> Locations, string? Fingerprint);

/// <summary>An override message of the application.</summary>
/// <param name="Path">The absolute path of the override source.</param>
/// <param name="Location">The message's name in the override source.</param>
/// <param name="Changed">
/// Whether the library's base text changed since the override was written: its entry in the target's review file records
/// another base fingerprint. Null when the override has no entry (written by hand) or the library does not compile.
/// </param>
/// <param name="Known">Whether the library has the key; an override of an unknown key is <c>RTR0103</c>.</param>
internal sealed record TranslationLibraryOverride(string Key, string Locale, string Path, string Text, TextSourceLocation Location, bool? Changed, bool Known);

/// <summary>
/// A library catalog as an application overrides it: the library's contract, base texts and locales (layer 0, read-only)
/// and the application's overrides (layer 1), compiled together by the build's rules
/// (<see cref="TranslationCompiler.CompileRmf2OverlayV5"/>). Shared by the Translations Editor, the language server and
/// <c>runic-translations overrides</c>.
/// </summary>
internal sealed class TranslationLibraryCatalog
{
    private TranslationLibraryCatalog(TranslationOverrideTarget target, TranslationDependencyCatalog dependency, TranslationSource project, IReadOnlyList<TranslationSource> librarySources,
        IReadOnlyList<TranslationSource> overrideSources, Rmf2ProjectCompilation library, Rmf2ProjectCompilation overlay, IReadOnlyDictionary<string, string>? fingerprints,
        TranslationInterchangeReview? review, byte[]? reviewBytes, TranslationInterchangeException? reviewError)
    {
        Target = target; Dependency = dependency; Project = project; LibrarySources = librarySources; OverrideSources = overrideSources;
        Library = library; Overlay = overlay; Fingerprints = fingerprints; Review = review; ReviewBytes = reviewBytes; ReviewException = reviewError;
    }

    internal TranslationOverrideTarget Target { get; }
    internal TranslationDependencyCatalog Dependency { get; }
    internal string Catalog => Dependency.Catalog;
    /// <summary>The packed runic.json.</summary>
    internal TranslationSource Project { get; }
    internal IReadOnlyList<TranslationSource> LibrarySources { get; }
    internal IReadOnlyList<TranslationSource> OverrideSources { get; }
    /// <summary>The library alone (relaxed when it does not compile strictly, as review data is computed).</summary>
    internal Rmf2ProjectCompilation Library { get; }
    /// <summary>The library with the application's override sources, as the build compiles it.</summary>
    internal Rmf2ProjectCompilation Overlay { get; }
    /// <summary>The per-unit base fingerprints of the library, or null when it does not compile.</summary>
    internal IReadOnlyDictionary<string, string>? Fingerprints { get; }
    internal TranslationInterchangeReview? Review { get; }
    /// <summary>The bytes <see cref="Review"/> was read from, or null when the review file does not exist.</summary>
    internal byte[]? ReviewBytes { get; }
    /// <summary>Why the review file cannot be read (reported as <c>RTR0091</c>); overrides and keeps are refused until it is fixed.</summary>
    internal TranslationInterchangeException? ReviewException { get; }
    internal string? ReviewError => ReviewException?.Message;
    internal string BaseLocale => Library.Project?.DefaultLocale ?? "";
    internal IReadOnlyList<TranslationLibraryMessage> Messages { get; private set; } = [];
    internal IReadOnlyList<TranslationLibraryLocale> Locales { get; private set; } = [];
    internal IReadOnlyList<TranslationLibraryOverride> Overrides { get; private set; } = [];

    /// <summary>The diagnostics of the override sources (the build's, located in them), with <c>RTR0090</c> for overrides whose library text changed.</summary>
    internal IReadOnlyList<TranslationDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>Why the library itself does not compile, or null.</summary>
    internal TranslationDiagnostic? LibraryError { get; private set; }

    internal TranslationLibraryMessage? Message(string key) => Messages.FirstOrDefault(message => message.Key == key);

    internal TranslationLibraryOverride? Override(string key, string locale) =>
        Overrides.FirstOrDefault(item => item.Key == key && string.Equals(item.Locale, locale, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Loads a library catalog with the application's overrides. <paramref name="buffers"/> (absolute path → unsaved content)
    /// replace or add override sources.
    /// </summary>
    /// <exception cref="InvalidOperationException">The target has no installed catalog (<see cref="TranslationOverrideTarget.Problem"/>).</exception>
    internal static TranslationLibraryCatalog Load(TranslationOverrideTarget target, IReadOnlyDictionary<string, byte[]>? buffers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        TranslationDependencyCatalog dependency = target.Dependency ?? throw new InvalidOperationException(target.Problem ?? $"Catalog '{target.Catalog}' is not installed.");
        LibrarySnapshot library = LibrarySnapshot.Get(dependency, cancellationToken);
        IReadOnlyList<TranslationSource> overrides = TranslationDependencyCatalogs.ReadOverrides(target, buffers, cancellationToken);
        Rmf2ProjectCompilation overlay = TranslationCompiler.CompileRmf2OverlayV5(library.Project, library.Sources, overrides, null, null, cancellationToken);
        TranslationInterchangeReview? review = null;
        byte[]? reviewBytes = null;
        TranslationInterchangeException? reviewError = null;
        try { (review, reviewBytes) = TranslationReviewFile.ReadSnapshot(target.ReviewPath, target.Catalog); }
        catch (TranslationInterchangeException error) { reviewError = error; }
        var result = new TranslationLibraryCatalog(target, dependency, library.Project, library.Sources, overrides, library.Review, overlay, library.Fingerprints, review, reviewBytes, reviewError);
        result.Build(library.Strict, cancellationToken);
        return result;
    }

    // The library alone, compiled once per installed version: its sources are read again only when a file below the packed
    // catalog changes (size or time), so the language server and the editor do not recompile a large library on every load.
    private sealed record LibrarySnapshot(string Stamp, TranslationSource Project, IReadOnlyList<TranslationSource> Sources, Rmf2ProjectCompilation Strict,
        Rmf2ProjectCompilation Review, IReadOnlyDictionary<string, string>? Fingerprints)
    {
        private const int Capacity = 16;
        private static readonly object Gate = new();
        private static readonly LinkedList<(string Key, LibrarySnapshot Snapshot)> Cache = new();

        internal static LibrarySnapshot Get(TranslationDependencyCatalog dependency, CancellationToken cancellationToken)
        {
            string key = dependency.ProjectPath + "\0" + dependency.Version;
            string stamp = TranslationDependencyCatalogs.LibraryStamp(dependency, cancellationToken);
            lock (Gate)
                for (LinkedListNode<(string Key, LibrarySnapshot Snapshot)>? node = Cache.First; node is not null; node = node.Next)
                    if (node.Value.Key == key && node.Value.Snapshot.Stamp == stamp)
                    {
                        Cache.Remove(node);
                        Cache.AddFirst(node);
                        return node.Value.Snapshot;
                    }
            (TranslationSource project, IReadOnlyList<TranslationSource> sources) = TranslationDependencyCatalogs.ReadLibrary(dependency, cancellationToken);
            Rmf2ProjectCompilation strict = TranslationCompiler.CompileRmf2ProjectV5(project, sources, null, cancellationToken);
            Rmf2ProjectCompilation review = TranslationReviewFile.ForReview(strict, project, sources, null, cancellationToken);
            IReadOnlyDictionary<string, string>? fingerprints = review.Success && review.Project is not null ? TranslationInterchange.ComputeSourceFingerprints(review) : null;
            var snapshot = new LibrarySnapshot(stamp, project, sources, strict, review, fingerprints);
            lock (Gate)
            {
                for (LinkedListNode<(string Key, LibrarySnapshot Snapshot)>? node = Cache.First; node is not null; node = node.Next)
                    if (node.Value.Key == key) { Cache.Remove(node); break; }
                Cache.AddFirst((key, snapshot));
                while (Cache.Count > Capacity) Cache.RemoveLast();
            }
            return snapshot;
        }
    }

    /// <summary>
    /// The review warnings of the application's overrides (<c>validate</c> and <c>status</c>): <c>RTR0090</c> for each
    /// override whose library text changed since it was written, and <c>RTR0091</c> for an unreadable override review file,
    /// over every catalog runic.json lists in <c>overrides</c> that an installed package ships. A catalog that is not
    /// installed or does not compile is skipped; the build reports it (<c>RTR0104</c>).
    /// </summary>
    internal static IReadOnlyList<TranslationDiagnostic> OverrideReviewDiagnostics(string projectPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        var diagnostics = new List<TranslationDiagnostic>();
        TranslationDependencyResolution resolution;
        try { resolution = TranslationDependencyCatalogs.Resolve(projectPath, null, cancellationToken); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return diagnostics; }
        if (resolution.OverridesRoot is null) return diagnostics;
        foreach (TranslationOverrideTarget target in resolution.Overrides.Where(static target => target.Listed && target.Dependency is not null))
        {
            if (!Directory.Exists(target.Directory)) continue;
            try
            {
                TranslationLibraryCatalog catalog = Load(target, null, cancellationToken);
                diagnostics.AddRange(catalog.Diagnostics.Where(static diagnostic => diagnostic.Id is TranslationReviewFile.StaleCode or TranslationReviewFile.InvalidCode));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return diagnostics;
    }

    private void Build(Rmf2ProjectCompilation strict, CancellationToken cancellationToken)
    {
        LibraryError = strict.Diagnostics.FirstOrDefault(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
        if (Library.Project is not { } project)
        {
            Diagnostics = Overlay.Diagnostics.Where(IsOverrideDiagnostic).ToArray();
            return;
        }
        var direct = project.Locales.ToDictionary(static locale => locale.Tag, static locale => locale.DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal), StringComparer.Ordinal);
        var messages = new List<TranslationLibraryMessage>();
        foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages.Concat(project.ExtraMessages).OrderBy(static contract => contract.Key, StringComparer.Ordinal))
        {
            var texts = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var locations = new SortedDictionary<string, TextSourceLocation>(StringComparer.Ordinal);
            string? description = null;
            foreach (var locale in direct)
                if (locale.Value.TryGetValue(contract.Key, out Rmf2TranslationV5? resource))
                {
                    texts[locale.Key] = resource.SourceText ?? "";
                    locations[locale.Key] = resource.SourceLocation;
                    if (locale.Key == project.DefaultLocale) description = resource.Description;
                }
            messages.Add(new TranslationLibraryMessage(contract.Key, contract.Path, contract.Inputs.Select(static input => "$" + input.Name + ": " + input.Type).ToArray(),
                contract.Content, contract.Structured, texts.GetValueOrDefault(project.DefaultLocale, ""), description, texts, locations,
                Fingerprints?.GetValueOrDefault(contract.Key)));
        }
        Messages = messages;

        // The application's override messages, read from the sources so they show while the overlay has errors.
        var overrides = new List<TranslationLibraryOverride>();
        var known = messages.ToDictionary(static message => message.Key, StringComparer.Ordinal);
        var reviewEntries = (Review?.Entries ?? []).GroupBy(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        var fileLocales = new SortedSet<string>(StringComparer.Ordinal);
        foreach (TranslationSource source in OverrideSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = source.Path[(source.Path.LastIndexOf('/') + 1)..];
            if (!file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) || !TranslationCompiler.TryCanonicalizeLocale(file[..^5], out string locale)) continue;
            fileLocales.Add(locale);
            Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source, null, cancellationToken);
            foreach (Rmf2ResourceNode node in document.Nodes.Where(static node => !node.IsGroup))
            {
                bool? changed = null;
                if (Fingerprints is not null && known.ContainsKey(node.Key) && reviewEntries.TryGetValue(TranslationReviewFile.Identity(node.Key, locale), out var entry) &&
                    entry.SourceFingerprint is not null)
                    changed = !string.Equals(entry.SourceFingerprint, OverrideFingerprint(node.Key, locale), StringComparison.Ordinal);
                overrides.Add(new TranslationLibraryOverride(node.Key, locale, source.Path, node.Message ?? "", node.NameLocation, changed, known.ContainsKey(node.Key)));
            }
        }
        Overrides = overrides;

        // Locales: the library's, then those the application adds, each with its one fallback hop. As in the compiler, a
        // locale counts as added only when its override source has a message the library knows; until then (a scaffold
        // with commented templates, or only unknown keys) it is pending and no other locale falls back to it.
        string[] libraryTags = project.Locales.Select(static locale => locale.Tag).Order(StringComparer.Ordinal).ToArray();
        var withMessages = overrides.Where(static item => item.Known).Select(static item => item.Locale).ToHashSet(StringComparer.Ordinal);
        string[] added = fileLocales.Where(tag => !libraryTags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToArray();
        _addedTags = added.Where(withMessages.Contains).ToArray();
        string[] candidates = libraryTags.Concat(_addedTags).Order(StringComparer.Ordinal).ToArray();
        var overriddenByLocale = overrides.Where(static item => item.Known).GroupBy(static item => item.Locale, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Select(static item => item.Key).ToHashSet(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        var locales = new List<TranslationLibraryLocale>();
        foreach (Rmf2LocaleV5 locale in project.Locales.OrderBy(locale => locale.Tag != project.DefaultLocale).ThenBy(static locale => locale.Tag, StringComparer.Ordinal))
        {
            HashSet<string> own = direct[locale.Tag].Keys.ToHashSet(StringComparer.Ordinal);
            HashSet<string> overridden = overriddenByLocale.GetValueOrDefault(locale.Tag) ?? [];
            locales.Add(new TranslationLibraryLocale(locale.Tag, locale.FallbackTag, TranslationLibraryLocaleOrigin.Library, messages.Count, own.Count, overridden.Count,
                own.Union(overridden).Count(), false));
        }
        foreach (string tag in added)
        {
            HashSet<string> overridden = overriddenByLocale.GetValueOrDefault(tag) ?? [];
            // A pending locale shows the fallback it gets with its first message.
            string fallback = withMessages.Contains(tag)
                ? TranslationCompiler.AddedLocaleFallback(tag, candidates, libraryTags, project.DefaultLocale)
                : FallbackOf(tag)!;
            locales.Add(new TranslationLibraryLocale(tag, fallback, TranslationLibraryLocaleOrigin.Application, messages.Count, 0, overridden.Count, overridden.Count,
                !withMessages.Contains(tag)));
        }
        Locales = locales;

        var diagnostics = Overlay.Diagnostics.Where(IsOverrideDiagnostic).ToList();
        if (ReviewException is { } reviewError)
            diagnostics.Add(new TranslationDiagnostic(TranslationReviewFile.InvalidCode, TranslationDiagnosticSeverity.Warning,
                $"The review file of the overrides of catalog '{Catalog}' cannot be read ({reviewError.Code}): {reviewError.Message} Fix or delete it; overrides of this catalog are not written until then, and changed library texts are not detected.",
                new TextSourceLocation(new TranslationSource(Target.ReviewPath, []).Path, 0, 0, 1, 1, 1, 1)));
        foreach (TranslationLibraryOverride item in overrides.Where(static item => item.Changed == true))
            diagnostics.Add(new TranslationDiagnostic(TranslationReviewFile.StaleCode, TranslationDiagnosticSeverity.Warning,
                $"The library text of '{string.Join(".", known[item.Key].LogicalPath)}' changed since this override was written: its base text or its {item.Locale} text ({Dependency.Display}). Compare the override with the library's text, then keep it with 'Keep override' in the Translations Editor or write it again.",
                item.Location) { MessageKey = item.Key, Locale = item.Locale });
        Diagnostics = diagnostics;
    }

    private string[] _addedTags = [];

    /// <summary>
    /// The fingerprint an override's review entry records: the library's base message (its per-unit source fingerprint) and
    /// the library text the locale shows (<see cref="LibraryText"/>), so an override is marked changed (<c>RTR0090</c>)
    /// when the library changes either, such as a fix of its German text under an override seeded from it.
    /// </summary>
    internal string OverrideFingerprint(string key, string locale)
    {
        IReadOnlyDictionary<string, string> fingerprints = Fingerprints ??
            throw new InvalidOperationException($"The library catalog '{Catalog}' ({Dependency.Display}) does not compile: {LibraryError?.Message}");
        if (!fingerprints.TryGetValue(key, out string? baseFingerprint)) throw new ArgumentException($"'{key}' is not a message of catalog '{Catalog}'.", nameof(key));
        byte[] hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(baseFingerprint + "\n" + LibraryText(key, locale).Replace("\r\n", "\n", StringComparison.Ordinal)));
        return "sha256:" + Convert.ToHexStringLower(hash);
    }

    private bool IsOverrideDiagnostic(TranslationDiagnostic diagnostic) =>
        OverrideSources.Any(source => string.Equals(source.Path, diagnostic.Location.Path, StringComparison.Ordinal));

    /// <summary>
    /// The text an override of <paramref name="key"/> in <paramref name="locale"/> starts with: the library text the locale
    /// shows now, its own or, for a locale the library lacks, that of the first locale on its fallback chain the library
    /// translates (de-CH → de), else the base text.
    /// </summary>
    internal string SeedText(string key, string locale) => LibraryText(key, locale);

    /// <summary>
    /// The library's own text that <paramref name="locale"/> shows for <paramref name="key"/> without overrides: the locale's,
    /// else that of the first locale on its fallback chain the library translates, else the base text. The chain is the
    /// library's alone (a locale the library lacks takes the one hop it would get among the library's locales), so the result
    /// changes only when the library does. Overrides are seeded with it, and their review entry records it
    /// (<see cref="OverrideFingerprint"/>).
    /// </summary>
    internal string LibraryText(string key, string locale)
    {
        TranslationLibraryMessage message = Message(key) ?? throw new ArgumentException($"Catalog '{Catalog}' has no message '{key}'.", nameof(key));
        if (Library.Project is not { } project) return message.BaseText;
        string[] libraryTags = project.Locales.Select(static item => item.Tag).Order(StringComparer.Ordinal).ToArray();
        string? current = locale;
        for (int hops = 0; current is not null && hops <= libraryTags.Length + 1; hops++)
        {
            string? tag = message.Texts.Keys.FirstOrDefault(candidate => string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase));
            if (tag is not null) return message.Texts[tag];
            string probe = current;
            Rmf2LocaleV5? declared = project.Locales.FirstOrDefault(item => string.Equals(item.Tag, probe, StringComparison.OrdinalIgnoreCase));
            current = declared is not null ? declared.FallbackTag
                : TranslationCompiler.AddedLocaleFallback(probe, libraryTags.Append(probe).Order(StringComparer.Ordinal).ToArray(), libraryTags, project.DefaultLocale);
        }
        return message.BaseText;
    }

    /// <summary>
    /// The locale <paramref name="locale"/> falls back to: the library's declared fallback, or for a locale the library lacks
    /// the one hop the compiler and both runtimes choose among the library's locales and the added ones that have a message
    /// (a pending locale gets this fallback with its first message); null for the base locale.
    /// </summary>
    internal string? FallbackOf(string locale)
    {
        if (Library.Project is not { } project) return null;
        Rmf2LocaleV5? declared = project.Locales.FirstOrDefault(item => string.Equals(item.Tag, locale, StringComparison.OrdinalIgnoreCase));
        if (declared is not null) return declared.FallbackTag;
        string[] libraryTags = project.Locales.Select(static item => item.Tag).Order(StringComparer.Ordinal).ToArray();
        string[] candidates = libraryTags.Concat(_addedTags).Append(locale).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return TranslationCompiler.AddedLocaleFallback(locale, candidates, libraryTags, project.DefaultLocale);
    }

    /// <summary>The comment lines above a scaffolded override: the library's base text, which the build validates the override against.</summary>
    internal IReadOnlyList<string> SeedComments(string key)
    {
        TranslationLibraryMessage message = Message(key) ?? throw new ArgumentException($"Catalog '{Catalog}' has no message '{key}'.", nameof(key));
        var lines = new List<string> { $"{Dependency.Display}, base locale {BaseLocale}:" };
        lines.AddRange(message.BaseText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(static line => "  " + line));
        return lines;
    }
}

/// <summary>
/// The files an override change writes, each with the bytes it was planned from: the override source (null bytes: delete
/// it), the review file's change, and runic.json when the catalog is listed in <c>overrides</c> by the change.
/// <see cref="TranslationOverrideChanges.Apply"/> refuses when one of them changed since (<see cref="TranslationOverrideConflictException"/>).
/// </summary>
/// <param name="Path">The absolute path of the override source.</param>
/// <param name="Bytes">Its new content, or null to delete it.</param>
/// <param name="Expected">The override source as the change was planned from, or null when it did not exist.</param>
internal sealed record TranslationOverridePlan(string Path, byte[]? Bytes, byte[]? Expected)
{
    /// <summary>
    /// The change to the target's review file, applied to its current content with compare-and-replace
    /// (<see cref="TranslationReviewFile"/>; another writer's entries are kept), or null to leave it unchanged. Returns null to write nothing.
    /// </summary>
    internal Func<TranslationInterchangeReview?, TranslationInterchangeReview?>? Review { get; init; }
    /// <summary>The keys the change overrides or reverts.</summary>
    internal IReadOnlyList<string> Keys { get; init; } = [];
    /// <summary>Whether the change creates the override source.</summary>
    internal bool Creates { get; init; }
    /// <summary>The fallback of a locale the change adds to the library catalog, or null when the library declares it.</summary>
    internal string? AddedLocaleFallback { get; init; }
    /// <summary>The application's runic.json when the change lists the catalog in its <c>overrides</c>, else null.</summary>
    internal string? ProjectPath { get; init; }
    /// <summary>runic.json after the change (with <see cref="ProjectPath"/>).</summary>
    internal byte[]? ProjectBytes { get; init; }
    /// <summary>runic.json as the change was planned from (with <see cref="ProjectPath"/>).</summary>
    internal byte[]? ExpectedProject { get; init; }
    /// <summary>Whether the override source keeps its content (Keep override checks it but only writes the review file).</summary>
    internal bool KeepsSource => Bytes is not null && Expected is not null && Bytes.AsSpan().SequenceEqual(Expected);
}

/// <summary>An override change was refused because a file it writes changed since the change was planned; nothing was written.</summary>
internal sealed class TranslationOverrideConflictException(string path, string message) : InvalidOperationException(message)
{
    /// <summary>The absolute path of the file that changed.</summary>
    internal string Path { get; } = path;
}

/// <summary>
/// Plans overrides of a library catalog: create an override seeded with the library's text, revert it to the library, keep it
/// after the library text changed, and scaffold a locale's override source. Nothing is written until <see cref="Apply"/>,
/// which writes every file of a plan or none.
/// </summary>
internal static class TranslationOverrideChanges
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Overrides <paramref name="keys"/> in <paramref name="locale"/>: each message is added to <c>&lt;locale&gt;.rmf2</c>
    /// with <paramref name="text"/> (one key) or the library's text (<see cref="TranslationLibraryCatalog.SeedText"/>), the
    /// library's base text as comments above it, and a review entry recording the library text it was written for.
    /// </summary>
    /// <exception cref="ArgumentException">A key is not the library's or is already overridden in the locale, or the locale is invalid.</exception>
    /// <exception cref="InvalidOperationException">The library does not compile, or the review file cannot be read.</exception>
    internal static TranslationOverridePlan Override(TranslationLibraryCatalog catalog, IReadOnlyList<string> keys, string locale, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(keys);
        string tag = Canonical(locale);
        if (keys.Count == 0) throw new ArgumentException("Choose at least one message to override.", nameof(keys));
        if (text is not null && keys.Count != 1) throw new ArgumentException("A text overrides exactly one message.", nameof(text));
        string path = catalog.Target.SourcePath(tag);
        TranslationSource? existing = catalog.OverrideSources.FirstOrDefault(source => string.Equals(source.Path, new TranslationSource(path, []).Path, StringComparison.Ordinal));
        byte[]? expected = existing?.GetUtf8Bytes() ?? (File.Exists(path) ? File.ReadAllBytes(path) : null);
        byte[] bytes = expected ?? Header(catalog, tag, scaffold: false);
        string[] distinct = keys.Distinct(StringComparer.Ordinal).ToArray();
        foreach (string key in distinct)
        {
            TranslationLibraryMessage message = catalog.Message(key) ?? throw new ArgumentException($"Catalog '{catalog.Catalog}' has no message '{key}'.", nameof(keys));
            if (catalog.Override(key, tag) is not null) throw new ArgumentException($"'{string.Join(".", message.LogicalPath)}' is already overridden in '{tag}'.", nameof(keys));
            bytes = Rmf2ResourceWriter.InsertMessage(new TranslationSource(path, bytes), message.LogicalPath, text ?? catalog.SeedText(key, tag), catalog.SeedComments(key));
        }
        return new TranslationOverridePlan(path, bytes, expected)
        {
            Review = Stamp(catalog, distinct, tag), Keys = distinct, Creates = expected is null, AddedLocaleFallback = AddedFallback(catalog, tag),
        };
    }

    /// <summary>
    /// Reverts the override of <paramref name="key"/> in <paramref name="locale"/> to the library: removes the message (and its
    /// comments) from the override source, deletes the source when nothing else is left in it, and drops its review entry.
    /// </summary>
    /// <exception cref="InvalidOperationException">The review file cannot be read.</exception>
    internal static TranslationOverridePlan Revert(TranslationLibraryCatalog catalog, string key, string locale)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        TranslationLibraryOverride item = catalog.Override(key, locale) ?? throw new ArgumentException($"'{key}' is not overridden in '{locale}'.", nameof(key));
        RequireReadableReview(catalog);
        TranslationSource source = catalog.OverrideSources.Single(source => source.Path == item.Path);
        Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source);
        Rmf2ResourceNode node = document.Nodes.First(node => !node.IsGroup && node.Key == key);
        byte[] bytes = Rmf2ResourceWriter.Replace(source.GetUtf8Bytes(), [(node.Location.StartByte, node.Location.LengthBytes, Array.Empty<byte>())]);
        bytes = RemoveEmptyGroups(new TranslationSource(source.Path, bytes));
        // A source with no messages left and nothing but comments the scaffold wrote is deleted, so an added locale disappears with its last override.
        bool empty = !Rmf2ResourceReader.Analyze(new TranslationSource(source.Path, bytes)).Nodes.Any() &&
            StrictUtf8.GetString(bytes).Split('\n').All(static line => line.Trim().Length == 0 || line.TrimStart().StartsWith('#'));
        string tag = item.Locale;
        return new TranslationOverridePlan(Path.GetFullPath(item.Path), empty ? null : bytes, source.GetUtf8Bytes())
        {
            Keys = [key],
            Review = current => current is not null && current.Entries.Any(entry => entry.Key == key && string.Equals(entry.Locale, tag, StringComparison.OrdinalIgnoreCase))
                ? new TranslationInterchangeReview(current.CatalogId, current.Entries.Where(entry => !(entry.Key == key && string.Equals(entry.Locale, tag, StringComparison.OrdinalIgnoreCase))))
                : null,
        };
    }

    /// <summary>
    /// Records that the overrides of <paramref name="keys"/> in <paramref name="locale"/> were checked against the library's
    /// current text ("Keep override"). The plan writes only the review file, and refuses when the override source changed.
    /// </summary>
    internal static TranslationOverridePlan Keep(TranslationLibraryCatalog catalog, IReadOnlyList<string> keys, string locale)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(keys);
        string tag = Canonical(locale);
        TranslationLibraryOverride? first = null;
        foreach (string key in keys)
        {
            TranslationLibraryOverride item = catalog.Override(key, tag) ?? throw new ArgumentException($"'{key}' is not overridden in '{tag}'.", nameof(keys));
            if (first is not null && first.Path != item.Path) throw new ArgumentException("The overrides to keep are in different sources.", nameof(keys));
            first = item;
        }
        if (first is null) throw new ArgumentException("Choose at least one override to keep.", nameof(keys));
        byte[] source = catalog.OverrideSources.Single(candidate => candidate.Path == first.Path).GetUtf8Bytes();
        return new TranslationOverridePlan(Path.GetFullPath(first.Path), source, source) { Review = Stamp(catalog, keys, tag), Keys = keys.Distinct(StringComparer.Ordinal).ToArray() };
    }

    /// <summary>
    /// The override source of a locale without overrides yet (<c>runic-translations overrides add</c> without <c>--key</c>, and
    /// the editor's add-locale on a library catalog): a header and every library message as a commented-out template with the
    /// library's text, to uncomment. A locale the library lacks is added with its first message, falling back by the one-hop rule.
    /// </summary>
    internal static TranslationOverridePlan Scaffold(TranslationLibraryCatalog catalog, string locale, bool templates = true)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string tag = Canonical(locale);
        string path = catalog.Target.SourcePath(tag);
        if (File.Exists(path) || catalog.OverrideSources.Any(source => string.Equals(source.Path, new TranslationSource(path, []).Path, StringComparison.Ordinal)))
            throw new ArgumentException($"'{Path.GetFileName(path)}' already exists; add messages with --key.", nameof(locale));
        var text = new StringBuilder(StrictUtf8.GetString(Header(catalog, tag, scaffold: true)));
        if (templates && catalog.Messages.Count != 0)
        {
            // The templates are the RMF2 the override would be, commented out line by line: uncomment a message (and its
            // groups) to override it.
            byte[] template = [];
            foreach (TranslationLibraryMessage message in catalog.Messages)
                template = Rmf2ResourceWriter.InsertMessage(new TranslationSource(path, template), message.LogicalPath,
                    catalog.SeedText(message.Key, tag).Replace("\r\n", "\n", StringComparison.Ordinal));
            text.Append("#\n# Uncomment a message (with its groups) to override it:\n");
            foreach (string line in StrictUtf8.GetString(template).TrimEnd('\n').Split('\n')) text.Append("# ").Append(line).Append('\n');
        }
        byte[] bytes = StrictUtf8.GetBytes(text.ToString());
        Rmf2ResourceDocument check = Rmf2ResourceReader.Analyze(new TranslationSource(path, bytes));
        if (!check.Success) throw new InvalidOperationException("The scaffolded override source does not parse: " + check.Diagnostics[0].Message);
        return new TranslationOverridePlan(path, bytes, null) { Creates = true, AddedLocaleFallback = AddedFallback(catalog, tag) };
    }

    /// <summary>
    /// <paramref name="plan"/> with the application's runic.json (<paramref name="projectPath"/>) listing the catalog in
    /// <c>overrides</c> (<see cref="Declare"/>), written by <see cref="Apply"/> with the rest. Unchanged when it is listed already.
    /// </summary>
    /// <exception cref="InvalidOperationException">runic.json is not valid.</exception>
    internal static TranslationOverridePlan Declaring(TranslationOverridePlan plan, string projectPath, TranslationOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        if (target.Listed) return plan;
        TranslationDependencyCatalog dependency = target.Dependency ?? throw new InvalidOperationException(target.Problem ?? $"Catalog '{target.Catalog}' is not installed.");
        string full = Path.GetFullPath(projectPath);
        byte[] current = File.ReadAllBytes(full);
        byte[]? declared = Declare(new TranslationSource(full, current), target.Catalog, dependency.Package);
        return declared is null ? plan : plan with { ProjectPath = full, ProjectBytes = declared, ExpectedProject = current };
    }

    /// <summary>
    /// runic.json with <c>{ "catalog": …, "from": … }</c> added to <c>overrides</c> (created after the last member when missing),
    /// keeping the rest of the file byte for byte. Returns null when the catalog is already listed.
    /// </summary>
    /// <exception cref="InvalidOperationException">runic.json is not a valid JSON object, or <c>overrides</c> is not an array.</exception>
    internal static byte[]? Declare(TranslationSource project, string catalog, string from)
    {
        ArgumentNullException.ThrowIfNull(project);
        var diagnostics = new DiagnosticBag();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, new TranslationCompilerOptions(), default);
        if (parsed.Root is not { Kind: JsonKind.Object } root || diagnostics.Items.Any(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error))
            throw new InvalidOperationException($"{project.Path} is not a valid runic.json; fix it first.");
        byte[] bytes = project.GetUtf8Bytes();
        string text = StrictUtf8.GetString(bytes);
        string newLine = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string entry = "{ \"catalog\": " + System.Text.Json.JsonSerializer.Serialize(catalog) + ", \"from\": " + System.Text.Json.JsonSerializer.Serialize(from) + " }";
        if (root.Property("overrides") is { } overrides)
        {
            if (overrides.Value.Kind != JsonKind.Array) throw new InvalidOperationException("runic.json \"overrides\" must be an array.");
            if (overrides.Value.Items.Any(item => item.Kind == JsonKind.Object && item.Property("catalog")?.Value.Text == catalog)) return null;
            JsonValue array = overrides.Value;
            int close = array.Span.Start + array.Span.Length - 1;
            if (array.Items.Count == 0) return Splice(bytes, close, entry);
            JsonValue last = array.Items[^1];
            int end = last.Span.Start + last.Span.Length;
            bool multiline = StrictUtf8.GetString(bytes, array.Span.Start, array.Items[0].Span.Start - array.Span.Start).Contains('\n', StringComparison.Ordinal);
            return Splice(bytes, end, multiline ? "," + newLine + LineIndent(bytes, last.Span.Start) + entry : ", " + entry);
        }
        if (root.Properties.Count == 0) return Splice(bytes, root.Span.Start + 1, newLine + "  \"overrides\": [" + entry + "]" + newLine);
        JsonProperty final = root.Properties[^1];
        return Splice(bytes, final.Value.Span.Start + final.Value.Span.Length, "," + newLine + LineIndent(bytes, final.NameSpan.Start) + "\"overrides\": [" + entry + "]");

        static byte[] Splice(byte[] bytes, int at, string value) => [.. bytes.AsSpan(0, at), .. StrictUtf8.GetBytes(value), .. bytes.AsSpan(at)];
        static string LineIndent(byte[] bytes, int at)
        {
            int start = at;
            while (start > 0 && bytes[start - 1] is (byte)' ' or (byte)'\t') start--;
            return StrictUtf8.GetString(bytes, start, at - start);
        }
    }

    /// <summary>
    /// Writes a plan: the override source (or deletes it), runic.json when the plan lists the catalog, then the review file.
    /// Each file is replaced atomically and only while it still holds the bytes the plan was computed from; when a later
    /// step fails, the files already written are restored, so a plan is written whole or not at all.
    /// </summary>
    /// <exception cref="TranslationOverrideConflictException">The override source or runic.json changed since the plan was computed.</exception>
    /// <exception cref="TranslationInterchangeException">The review file cannot be read, or kept changing while it was written.</exception>
    internal static void Apply(TranslationOverridePlan plan, TranslationOverrideTarget target)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(target);
        // Everything is checked before the first write.
        RequireUnchanged(plan.Path, plan.Expected);
        if (plan.ProjectPath is not null) RequireUnchanged(plan.ProjectPath, plan.ExpectedProject);
        var written = new List<(string Path, byte[]? Previous, byte[]? Bytes)>();
        try
        {
            if (!plan.KeepsSource)
            {
                Replace(plan.Path, plan.Bytes, plan.Expected);
                written.Add((plan.Path, plan.Expected, plan.Bytes));
            }
            if (plan.ProjectPath is not null)
            {
                Replace(plan.ProjectPath, plan.ProjectBytes, plan.ExpectedProject);
                written.Add((plan.ProjectPath, plan.ExpectedProject, plan.ProjectBytes));
            }
            if (plan.Review is { } change) UpdateReview(target, change);
        }
        catch
        {
            // Restores what this plan wrote, unless another writer replaced it meanwhile.
            for (int index = written.Count - 1; index >= 0; index--)
            {
                (string path, byte[]? previous, byte[]? bytes) = written[index];
                try { Replace(path, previous, bytes); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or TranslationOverrideConflictException) { }
            }
            throw;
        }
    }

    private static void UpdateReview(TranslationOverrideTarget target, Func<TranslationInterchangeReview?, TranslationInterchangeReview?> change)
    {
        for (int attempt = 1; ; attempt++)
        {
            (TranslationInterchangeReview? current, byte[]? bytes) = TranslationReviewFile.ReadSnapshot(target.ReviewPath, target.Catalog);
            if (change(current) is not { } next) return;
            try
            {
                if (next.Entries.Count != 0) TranslationReviewFile.Write(target.ReviewPath, next, bytes);
                else if (bytes is not null)
                {
                    // The last entry is gone: the file goes too, while it still holds what was read.
                    byte[]? now = TranslationReviewFile.ReadBytes(target.ReviewPath);
                    if (now is null || !now.AsSpan().SequenceEqual(bytes))
                        throw new TranslationInterchangeException(TranslationReviewFile.ChangedCode, $"The review file '{Path.GetFileName(target.ReviewPath)}' changed on disk after it was read; nothing was written. Try again.");
                    File.Delete(target.ReviewPath);
                }
                return;
            }
            catch (TranslationInterchangeException exception) when (exception.Code == TranslationReviewFile.ChangedCode && attempt < 5) { }
        }
    }

    private static void RequireUnchanged(string path, byte[]? expected)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"'{path}' is a symbolic link or reparse point; it is not written.");
        byte[]? current = File.Exists(path) ? File.ReadAllBytes(path) : null;
        bool unchanged = current is null ? expected is null : expected is not null && current.AsSpan().SequenceEqual(expected);
        if (!unchanged)
            throw new TranslationOverrideConflictException(Path.GetFullPath(path),
                $"'{Path.GetFileName(path)}' changed on disk after the override was planned (an editor save, the language server or the command line); nothing was written. Reload and try again.");
    }

    // Replaces (or, with null bytes, deletes) a file atomically while it still holds expected.
    private static void Replace(string path, byte[]? bytes, byte[]? expected)
    {
        string directory = Path.GetDirectoryName(path)!;
        string? temporary = null;
        try
        {
            if (bytes is not null)
            {
                Directory.CreateDirectory(directory);
                temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temporary, bytes);
            }
            RequireUnchanged(path, expected);
            if (temporary is not null) File.Move(temporary, path, overwrite: true);
            else if (File.Exists(path)) File.Delete(path);
        }
        finally
        {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RequireReadableReview(TranslationLibraryCatalog catalog)
    {
        if (catalog.ReviewException is { } error)
            throw new InvalidOperationException($"The review file '{Path.GetFileName(catalog.Target.ReviewPath)}' of the overrides of catalog '{catalog.Catalog}' cannot be read ({error.Code}): {error.Message} Fix or delete it first (RTR0091); nothing was written.");
    }

    // The review change recording the library text (base and locale) the overrides were written or kept for.
    private static Func<TranslationInterchangeReview?, TranslationInterchangeReview?> Stamp(TranslationLibraryCatalog catalog, IReadOnlyList<string> keys, string locale)
    {
        RequireReadableReview(catalog);
        if (catalog.Fingerprints is null)
            throw new InvalidOperationException($"The library catalog '{catalog.Catalog}' ({catalog.Dependency.Display}) does not compile: {catalog.LibraryError?.Message}");
        var fingerprints = keys.Distinct(StringComparer.Ordinal).ToDictionary(key => key, key => catalog.OverrideFingerprint(key, locale), StringComparer.Ordinal);
        string id = catalog.Catalog;
        return current => TranslationReviewFile.Mark(current, id, fingerprints.Keys, locale, TranslationReviewState.Translated, null, fingerprints);
    }

    private static byte[] Header(TranslationLibraryCatalog catalog, string locale, bool scaffold)
    {
        bool library = catalog.Locales.Any(item => item.Origin == TranslationLibraryLocaleOrigin.Library && string.Equals(item.Tag, locale, StringComparison.OrdinalIgnoreCase));
        string kind = library
            ? $"replace the library's {locale} text"
            : $"add {locale}, which the library lacks; it falls back to {AddedFallback(catalog, locale)}" + (scaffold ? " (the build adds it with its first message)" : "");
        return StrictUtf8.GetBytes($"# Overrides of catalog {catalog.Catalog} ({catalog.Dependency.Display}): messages here {kind}.\n" +
            "# Keys are the library's logical keys; the build validates each message against the library (RTR0103).\n");
    }

    private static string? AddedFallback(TranslationLibraryCatalog catalog, string locale) =>
        catalog.Library.Project is { } project && !project.Locales.Any(item => string.Equals(item.Tag, locale, StringComparison.OrdinalIgnoreCase))
            ? catalog.FallbackOf(locale) : null;

    private static string Canonical(string locale)
    {
        if (string.IsNullOrWhiteSpace(locale) || !TranslationCompiler.TryCanonicalizeLocale(locale, out string tag))
            throw new ArgumentException($"'{locale}' is not a valid BCP 47 locale tag.", nameof(locale));
        return tag;
    }

    // Groups the removal of their last message left empty (`grid {\n}`), innermost first.
    private static byte[] RemoveEmptyGroups(TranslationSource source)
    {
        for (int pass = 0; pass < 32; pass++)
        {
            Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source);
            Rmf2ResourceNode? empty = document.Nodes.Where(static node => node.IsGroup).LastOrDefault(group =>
                !document.Nodes.Any(node => node.Path.Count > group.Path.Count && node.Path.Take(group.Path.Count).SequenceEqual(group.Path, StringComparer.Ordinal) &&
                    node.NameLocation.StartByte > group.NameLocation.StartByte && node.NameLocation.StartByte < group.Location.StartByte + group.Location.LengthBytes));
            if (empty is null) return source.GetUtf8Bytes();
            byte[] bytes = source.GetUtf8Bytes();
            int end = Math.Min(empty.Location.StartByte + empty.Location.LengthBytes, bytes.Length);
            // The whole line of the closing brace goes too.
            while (end < bytes.Length && bytes[end] is (byte)'\r' or (byte)'\n') { end++; if (bytes[end - 1] == (byte)'\n') break; }
            source = new TranslationSource(source.Path, Rmf2ResourceWriter.Replace(bytes, [(empty.Location.StartByte, end - empty.Location.StartByte, Array.Empty<byte>())]));
        }
        return source.GetUtf8Bytes();
    }
}
