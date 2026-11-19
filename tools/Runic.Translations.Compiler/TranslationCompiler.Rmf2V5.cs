using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Runic.Translations.Compiler;

public static partial class TranslationCompiler
{
    private static readonly string[] Rmf2MountMembers = { "path", "namespace" };

    internal static TranslationProjectProfileSelection SelectProjectProfile(TranslationSource project,
        TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        _ = StrictJsonParser.Parse(project, diagnostics, options, cancellationToken);
        return new(TranslationProjectProfile.Rmf2ExecutionV2, Array.AsReadOnly(diagnostics.ToSortedArray()));
    }

    internal static TranslationProfileCompilation CompileProjectForSelectedProfile(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        TranslationProjectProfileSelection selection = SelectProjectProfile(project, options, cancellationToken);
        if (!selection.Success)
            return new TranslationProfileCompilation(selection.Profile,
                new Rmf2ProjectCompilation(null, selection.Diagnostics));
        return new TranslationProfileCompilation(selection.Profile,
            CompileRmf2ProjectV5(project, messages, options, cancellationToken));
    }

    // Whether a path is a grouped <locale>.rmf2 file or a direct <locale>/<id>.mf2 file of the locale.
    private static bool IsLocaleSource(string path, string locale)
    {
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        string file = normalized[(slash + 1)..];
        if (file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
            return string.Equals(file[..^".rmf2".Length], locale, StringComparison.OrdinalIgnoreCase);
        if (!file.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) || slash <= 0) return false;
        int parent = normalized.LastIndexOf('/', slash - 1);
        return string.Equals(normalized[(parent + 1)..slash], locale, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Rmf2ProjectSourceV5(string Key, string[] Path, string Locale, TranslationSource Source, Rmf2ResourceNode Node, Rmf2SourceUnitV5? Unit)
    {
        private string? _logical;
        /// <summary>The logical path, <c>common.cancel</c>, joined once.</summary>
        internal string Logical => _logical ??= string.Join(".", Path);
        /// <summary>
        /// The memos of a cached compile (<see cref="Rmf2CompileCache"/>), which reuses this object while its file and the
        /// link context are unchanged; null in any other compile. Each memo is an immutable object replaced as a whole,
        /// so overlapping compiles read either the old or the new one.
        /// </summary>
        internal Rmf2SourceMemoV5? Memo { get; init; }
    }

    // The memoized results of one extracted message in a cached compile, each with the diagnostics it reported, keyed by
    // the identity of the inputs it was computed from (see docs/plans/incremental-validation.md).
    private sealed class Rmf2SourceMemoV5
    {
        internal CompiledMemo? Compiled;
        internal ProfiledMemo? Profiled;
        internal RequiredMemo? Required;
        internal PairedMemo? Paired;
        internal TranslatedMemo? Translated;
        internal ContractMemo? Contracted;
    }
    private sealed record ContractMemo(Rmf2ProjectEntryV5 Origin, IReadOnlyDictionary<string, Rmf2SlotV5> Requirements, string[] Names, int Id,
        Rmf2MessageContractV5 Contract, string? ReadableProblem);
    private sealed record CompiledMemo(IReadOnlyList<Rmf2InputV5>? Inputs, Rmf2ProjectEntryV5? Entry, TranslationDiagnostic[] Diagnostics);
    private sealed record ProfiledMemo(Rmf2ProjectEntryV5 Entry, string? ExpectedKind, Rmf2ProjectEntryV5 Updated, Rmf2DocumentAnalysisV5 Analysis, TranslationDiagnostic[] Diagnostics);
    private sealed record RequiredMemo(Rmf2ProjectEntryV5 Origin, IReadOnlyDictionary<string, Rmf2SlotV5> Requirements, TranslationDiagnostic[] Diagnostics);
    private sealed record PairedMemo(Rmf2ProjectEntryV5 Origin, Rmf2ProjectEntryV5 Translated, IReadOnlyDictionary<string, Rmf2SlotV5> Requirements, TranslationDiagnostic[] Diagnostics);
    private sealed record TranslatedMemo(Rmf2ProjectEntryV5 Entry, Rmf2TranslationV5 Translation, TranslationDiagnostic[] ContentDiagnostics);
    private sealed record Rmf2ProjectEntryV5(Rmf2ProjectSourceV5 Source, Rmf2LinkedMarkupV5 Linked)
    {
        internal Rmf2TranslationV5 Translation => new(Source.Key, Source.Locale, Linked.Message, Source.Node.NameLocation,
            Source.Node.Comments.Count == 0 ? null : string.Join("\n", Source.Node.Comments))
        { SourceText = Source.Node.Message, ParameterDescriptions = ParameterDescriptionsV5(Source.Node.Properties), Properties = Source.Node.Properties };
    }

    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default) =>
        CompileRmf2ProjectV5(project, messages, options, false, cancellationToken);

    /// <param name="pseudoLocales">Whether to generate the development pseudo-locales that runic.json declares in <c>pseudoLocales</c>.</param>
    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationCompilerOptions? options, bool pseudoLocales, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(messages);
        return CompileRmf2ProjectV5(project, Materialize(messages), null, options ?? new TranslationCompilerOptions(), cancellationToken, pseudoLocales);
    }

    /// <summary>
    /// Compiles with the pseudo-locales generated, adding <paramref name="simulatedPseudoLocales"/> as if runic.json
    /// declared them in <c>pseudoLocales</c> (the editor's pseudo-localization simulation). runic.json is read as by
    /// any other compile; a tag that is not a supported pseudo-locale, or that runic.json declares as a locale, is ignored.
    /// </summary>
    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationCompilerOptions? options, IReadOnlyCollection<string> simulatedPseudoLocales, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(simulatedPseudoLocales);
        return CompileRmf2ProjectV5(project, Materialize(messages), null, options ?? new TranslationCompilerOptions(), cancellationToken, true, simulatedPseudoLocales: simulatedPseudoLocales);
    }

    /// <summary>
    /// Links a project from per-source units (<see cref="Rmf2SourceUnitV5"/>), reusing their parse and
    /// lowering work. Units built with other options are recompiled, so the result is always identical
    /// to compiling the units' sources directly.
    /// </summary>
    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<Rmf2SourceUnitV5> units, TranslationCompilerOptions options, CancellationToken cancellationToken = default) =>
        CompileRmf2ProjectV5(project, units, options, false, cancellationToken);

    /// <param name="pseudoLocales">Whether to generate the development pseudo-locales that runic.json declares in <c>pseudoLocales</c>.</param>
    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<Rmf2SourceUnitV5> units, TranslationCompilerOptions options, bool pseudoLocales, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(options);
        var sources = new List<TranslationSource>();
        var lookup = new Dictionary<TranslationSource, Rmf2SourceUnitV5>(ReferenceEqualityComparer.Instance);
        foreach (Rmf2SourceUnitV5 unit in units)
        {
            if (unit is null) throw new ArgumentException("A source unit collection contains null.", nameof(units));
            sources.Add(unit.Source);
            if (ReferenceEquals(unit.Options, options)) lookup[unit.Source] = unit;
        }
        return CompileRmf2ProjectV5(project, Materialize(sources), lookup, options, cancellationToken, pseudoLocales);
    }

    /// <summary>Reads the <c>overrides</c> of a runic.json; empty when it declares none or cannot be read (its compilation reports why).</summary>
    internal static IReadOnlyList<Rmf2OverrideV5> ReadOverridesV5(TranslationSource project, TranslationCompilerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, options, CancellationToken.None);
        ManifestModel? manifest = parsed.Root is null ? null : ReadMf2Project(parsed, diagnostics, options);
        return manifest is null ? Array.Empty<Rmf2OverrideV5>() : manifest.Overrides.Select(item => new Rmf2OverrideV5(item.Catalog, item.From)).ToArray();
    }

    /// <summary>
    /// Compiles a library catalog, from the <c>runic.json</c> and sources its package ships (<c>runic/&lt;catalog&gt;/</c>),
    /// together with an application's override sources (<c>translations/overrides/&lt;catalog&gt;/&lt;locale&gt;.rmf2</c>).
    /// </summary>
    /// <remarks>
    /// Each override message is checked against the library's definition of its key: a key the library does not have is
    /// <c>RTR0103</c>, and inputs, content kind, markup and document structure are checked as for a translation of the
    /// library's base locale. The result is the library catalog with the override messages in place of the library's, and the
    /// locales they add (falling back to their nearest declared parent, else the library's base locale);
    /// <see cref="Rmf2ProjectV5.OverlayKeys"/> lists the overridden keys. The contract fingerprint is the library's.
    /// </remarks>
    /// <param name="locales">
    /// The application's locales, or <see langword="null"/> for all: the result keeps the library locales they resolve to as the
    /// runtimes resolve a requested locale (the locale itself, else the CLDR likely-subtag and parent-locale match, such as
    /// <c>zh-Hant</c> for <c>zh-TW</c>), their fallback chains and the base locale, and drops the others, so an application
    /// that supports two locales does not ship a library's thirty.
    /// </param>
    internal static Rmf2ProjectCompilation CompileRmf2OverlayV5(TranslationSource libraryProject, IEnumerable<TranslationSource> librarySources,
        IEnumerable<TranslationSource> overrideSources, TranslationCompilerOptions? options = null, IReadOnlyCollection<string>? locales = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(libraryProject);
        ArgumentNullException.ThrowIfNull(librarySources);
        ArgumentNullException.ThrowIfNull(overrideSources);
        return CompileRmf2ProjectV5(libraryProject, Materialize(librarySources), null, options ?? new TranslationCompilerOptions(), cancellationToken,
            false, Materialize(overrideSources), locales);
    }

    // The compile of Rmf2CompileCache: its units, link context and memo-miss counters.
    internal static Rmf2ProjectCompilation CompileRmf2ProjectV5Cached(TranslationSource project, TranslationSource[] sources,
        IReadOnlyDictionary<TranslationSource, Rmf2SourceUnitV5> units, TranslationCompilerOptions options, bool pseudoLocales,
        Rmf2LinkContext context, Rmf2LinkState state, Rmf2CompileCounters counters, CancellationToken cancellationToken) =>
        CompileRmf2ProjectV5(project, sources, units, options, cancellationToken, pseudoLocales, context: context, counters: counters, state: state);

    private static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project, TranslationSource[] sources,
        IReadOnlyDictionary<TranslationSource, Rmf2SourceUnitV5>? units, TranslationCompilerOptions options, CancellationToken cancellationToken,
        bool pseudoLocales = false, TranslationSource[]? overlaySources = null, IReadOnlyCollection<string>? keepLocales = null,
        IReadOnlyCollection<string>? simulatedPseudoLocales = null, Rmf2LinkContext? context = null, Rmf2CompileCounters? counters = null, Rmf2LinkState? state = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new DiagnosticBag();
        // A memoized computation runs with its own bag, and its diagnostics are replayed at the same point of the compile.
        TranslationDiagnostic[] Capture(Action compute)
        {
            var saved = diagnostics;
            diagnostics = new DiagnosticBag();
            try { compute(); return diagnostics.Items.ToArray(); }
            finally { diagnostics = saved; }
        }
        void Replay(TranslationDiagnostic[] items) { foreach (var item in items) diagnostics.Append(item); }
        Rmf2ProjectCompilation Result(Rmf2ProjectV5? value = null) => new(value, Array.AsReadOnly(diagnostics.ToSortedArray()));
        bool Failed() => diagnostics.Items.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
        if (RejectDuplicateSourcePaths(new[] { project }, sources, diagnostics)) return Result();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, options, cancellationToken);
        ManifestModel? manifest = parsed.Root is null ? null : ReadMf2Project(parsed, diagnostics, options);
        if (manifest is null || Failed()) return Result();
        foreach (string simulated in simulatedPseudoLocales ?? [])
            if (Array.Find(Rmf2PseudoLocalesV5.Supported, supported => string.Equals(supported, simulated, StringComparison.OrdinalIgnoreCase)) is { } tag &&
                !manifest.PseudoLocales.Exists(pseudo => string.Equals(pseudo.Tag, tag, StringComparison.OrdinalIgnoreCase)) &&
                !manifest.Locales.Exists(locale => string.Equals(locale.Tag, tag, StringComparison.OrdinalIgnoreCase)))
                manifest.PseudoLocales.Add(new LocaleModel(tag, null, manifest.DefaultLocaleSpan, manifest.DefaultLocaleSpan));
        JsonValue config = parsed.Root!;
        var markup = new Rmf2ProjectMarkupV5(config, project, diagnostics);
        if (Failed()) return Result();
        if (manifest.PseudoLocales.Count != 0)
        {
            // A pseudo-locale is generated from the base sources; files of its own would be ambiguous in a development
            // build and, in a release build of a catalog that discovers its locales, would ship as a real locale.
            foreach (TranslationSource source in sources)
                foreach (LocaleModel pseudo in manifest.PseudoLocales)
                    if (IsLocaleSource(source.Path, pseudo.Tag))
                        diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Pseudo-locale '" + pseudo.Tag + "' is generated; remove its source '" + source.Path + "'.", project, pseudo.Span);
            if (Failed()) return Result();
        }
        if (pseudoLocales && manifest.PseudoLocales.Count != 0)
        {
            sources = Rmf2PseudoLocalesV5.Synthesize(sources, manifest.DefaultLocale, manifest.PseudoLocales.Select(static pseudo => pseudo.Tag).ToArray(), options);
            if (manifest.Locales.Count != 0)
                foreach (LocaleModel pseudo in manifest.PseudoLocales)
                    manifest.Locales.Add(new(pseudo.Tag, manifest.DefaultLocale, pseudo.Span, pseudo.Span));
        }
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // A relaxed compile (review fingerprints) applies no validation policy; a base-locale-only one also reads no translation.
        if (options.RelaxedValidation || options.BaseLocaleOnly) manifest.Relaxed = true;
        var extracted = ReadRmf2ProjectSourcesV5(project, sources, units, config, discovered, diagnostics, options, cancellationToken,
            options.BaseLocaleOnly ? manifest.DefaultLocale : null, context);
        var overlayExtracted = overlaySources is null ? new List<Rmf2ProjectSourceV5>() : ReadRmf2OverlaySourcesV5(overlaySources, diagnostics, options, cancellationToken);
        if (discovered.Count > options.MaximumLocalesPerCatalog)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Locale count exceeds the configured limit.", project, manifest.DefaultLocaleSpan);
        if (manifest.Locales.Count == 0)
        {
            foreach (string locale in discovered.Order(StringComparer.Ordinal))
                manifest.Locales.Add(new(locale, locale == manifest.DefaultLocale ? null : manifest.DefaultLocale, new(0, 0), new(0, 0)));
            if (sources.Length == 0) manifest.Locales.Add(new(manifest.DefaultLocale, null, manifest.DefaultLocaleSpan, manifest.DefaultLocaleSpan));
        }
        ValidateFallbackGraph(manifest, diagnostics);
        if (sources.Length != 0 && !discovered.Contains(manifest.DefaultLocale))
            diagnostics.Add("RTR0009", TranslationDiagnosticSeverity.Error, "The base locale has no translation sources.", project, manifest.DefaultLocaleSpan);
        var locales = manifest.Locales.OrderBy(locale => locale.Tag, StringComparer.Ordinal).ToArray();
        var declaredLocales = locales.Select(locale => locale.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string locale in discovered)
            if (!declaredLocales.Contains(locale)) diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Document locale '" + locale + "' is not declared.", project, config.Span);
        // Keys each locale declares, compiled or not: a message that failed to compile has reported
        // its own error, so RTR0010 and RTR0011 do not report it again as missing or extra.
        var declaredKeys = locales.ToDictionary(locale => locale.Tag, locale => new HashSet<string>(state?.KeyOrder(locale.Tag)?.Length ?? 0, StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        var undeclaredKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in extracted)
            if (declaredKeys.TryGetValue(source.Locale, out var keys)) keys.Add(source.Key);
            else undeclaredKeys.Add(source.Key);
        if (DistinctKeys() > options.MaximumKeysPerCatalog)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Catalog key count exceeds the configured limit.", project, config.Span);
        // The number of distinct keys in all locales: the largest locale's, and those of the others it lacks.
        int DistinctKeys()
        {
            var largest = undeclaredKeys;
            foreach (var set in declaredKeys.Values) if (set.Count > largest.Count) largest = set;
            var others = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in declaredKeys.Values.Append(undeclaredKeys))
                if (!ReferenceEquals(set, largest))
                    foreach (string key in set) if (!largest.Contains(key)) others.Add(key);
            return largest.Count + others.Count;
        }
        if (Failed()) return Result();

        var entries = locales.ToDictionary(locale => locale.Tag, locale => new LocaleEntries(state?.KeyOrder(locale.Tag)), StringComparer.OrdinalIgnoreCase);
        // RTR0010 points at the locale's grouped file beside the base-locale file of the message.
        var groupedFiles = new Dictionary<string, TranslationSource>(StringComparer.OrdinalIgnoreCase);
        TranslationSource? previousFile = null;
        foreach (var source in extracted)
        {
            // Once per file: a file's messages are consecutive.
            if (ReferenceEquals(source.Source, previousFile)) continue;
            previousFile = source.Source;
            if (source.Source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
                groupedFiles.TryAdd(ProjectDirectory(source.Source.Path) + "\0" + source.Locale, source.Source);
        }
        // The suggestions of RTR0011, needed only when a locale defines a key the base locale lacks.
        string[]? baseKeys = null;
        string[] BaseKeys() => baseKeys ??= extracted.Where(source => source.Locale == manifest.DefaultLocale).Select(source => source.Logical).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var canonical = new LocaleEntries(state?.KeyOrder(manifest.DefaultLocale));
        var extras = new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal);
        // Lowering a message depends on that message (and, for a translation, its base-locale inputs) only: it runs in
        // parallel, and its results are applied in source order, as a sequential compile applies them.
        var baseSources = extracted.Where(source => source.Locale == manifest.DefaultLocale).ToArray();
        var baseLowered = CompilerParallel.Map(baseSources.Length, index => CompiledFor(baseSources[index], null) is null ? LowerSource(baseSources[index], null) : null, cancellationToken);
        for (int index = 0; index < baseSources.Length; index++)
            if (Compile(baseSources[index], null, baseLowered[index]) is { } entry) { canonical.Add(baseSources[index].Key, entry); entries[baseSources[index].Locale].Add(baseSources[index].Key, entry); }
        var translationSources = extracted.Where(source => source.Locale != manifest.DefaultLocale).ToArray();
        // A translation of a base-locale key is lowered against that key's inputs, known now; one of an extra key waits
        // for the first locale that defines the key (below).
        var translationLowered = CompilerParallel.Map(translationSources.Length, index =>
            canonical.TryGetValue(translationSources[index].Key, out var origin) && CompiledFor(translationSources[index], origin.Linked.Message.Inputs) is null
                ? (Inputs: origin.Linked.Message.Inputs, Result: LowerSource(translationSources[index], origin.Linked.Message.Inputs)) : default,
            cancellationToken);
        for (int translationIndex = 0; translationIndex < translationSources.Length; translationIndex++)
        {
            var source = translationSources[translationIndex];
            cancellationToken.ThrowIfCancellationRequested();
            canonical.TryGetValue(source.Key, out var origin);
            // A base message that failed to compile reported its own error; the key is not extra.
            if (origin is null && manifest.Policy(source.Locale, ValidationMembers.ExtraLocaleKeys) != TranslationPolicy.Allow && !declaredKeys[manifest.DefaultLocale].Contains(source.Key))
            {
                string logical = source.Logical;
                string? suggestion = SuggestKey(logical, BaseKeys());
                AddPolicyDiagnostic("RTR0011", manifest.Policy(source.Locale, ValidationMembers.ExtraLocaleKeys), "Locale '" + source.Locale + "' defines '" + logical + "', which base locale '" + manifest.DefaultLocale + "' does not define." +
                    (suggestion is null ? "" : " Did you mean '" + suggestion + "'?"), source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            }
            if (origin is null) extras.TryGetValue(source.Key, out origin);
            var callerInputs = origin?.Linked.Message.Inputs;
            var lowered = translationLowered[translationIndex];
            if (Compile(source, callerInputs, lowered.Result is not null && ReferenceEquals(lowered.Inputs, callerInputs) ? lowered.Result : null) is { } entry)
            {
                entries[source.Locale].Add(source.Key, entry);
                if (!canonical.ContainsKey(source.Key)) extras.TryAdd(source.Key, entry);
            }
        }

        // The document profile: the base locale (or the first locale of an extra key) infers
        // the content kind; every other locale is checked against it and normalized.
        var profiles = new Dictionary<Rmf2ProjectEntryV5, Rmf2DocumentAnalysisV5>(extracted.Count, ReferenceEqualityComparer.Instance);
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in canonical.ToArray()) canonical[pair.Key] = Profile(pair.Value, null, entries[manifest.DefaultLocale]);
        foreach (var pair in extras.ToArray()) extras[pair.Key] = Profile(pair.Value, null, entries[pair.Value.Source.Locale]);
        foreach (var locale in entries.Values)
            foreach (string key in locale.Order)
                if (locale[key] is var entry && !profiles.ContainsKey(entry)) Profile(entry, kinds[key], locale);

        var contracts = new List<Rmf2MessageContractV5>();
        foreach (var pair in canonical)
        {
            IReadOnlyDictionary<string, Rmf2SlotV5> requirements;
            if (pair.Value.Source.Memo is not { } baseMemo) requirements = markup.Requirements(pair.Key, pair.Value.Linked, pair.Value.Source.Node.NameLocation, diagnostics);
            else
            {
                if (baseMemo.Required is not { } required || !ReferenceEquals(required.Origin, pair.Value))
                {
                    IReadOnlyDictionary<string, Rmf2SlotV5>? computed = null;
                    var reported = Capture(() => computed = markup.Requirements(pair.Key, pair.Value.Linked, pair.Value.Source.Node.NameLocation, diagnostics));
                    baseMemo.Required = required = new(pair.Value, computed!, reported);
                }
                Replay(required.Diagnostics);
                requirements = required.Requirements;
            }
            // The markup names of the key's messages in every locale; most messages have none.
            SortedSet<string>? names = null;
            foreach (var locale in locales)
            {
                if (entries[locale.Tag].TryGetValue(pair.Key, out var translated))
                {
                    // The checks of a translation against its base message depend on the two entries and the key's slot requirements.
                    if (translated.Source.Memo is not { } pairMemo) ValidatePair(pair.Value, translated, requirements);
                    else
                    {
                        if (pairMemo.Paired is not { } paired || !ReferenceEquals(paired.Origin, pair.Value) || !ReferenceEquals(paired.Translated, translated) || !ReferenceEquals(paired.Requirements, requirements))
                        {
                            if (counters is not null) counters.PairsChecked++;
                            pairMemo.Paired = paired = new(pair.Value, translated, requirements, Capture(() => ValidatePair(pair.Value, translated, requirements)));
                        }
                        Replay(paired.Diagnostics);
                    }
                    if (translated.Linked.Names.Count != 0) (names ??= new SortedSet<string>(StringComparer.Ordinal)).UnionWith(translated.Linked.Names);
                }
                else if (locale.Tag != manifest.DefaultLocale && !declaredKeys[locale.Tag].Contains(pair.Key))
                {
                    string message = "Locale '" + locale.Tag + "' does not translate '" + pair.Value.Source.Logical + "' from base locale '" + manifest.DefaultLocale + "'.";
                    if (groupedFiles.TryGetValue(ProjectDirectory(pair.Value.Source.Source.Path) + "\0" + locale.Tag, out var file))
                        AddPolicyDiagnostic("RTR0010", manifest.Policy(locale.Tag, ValidationMembers.TranslationCompleteness), message, file, new(0, 0), diagnostics, (pair.Key, locale.Tag));
                    else
                        AddPolicyDiagnostic("RTR0010", manifest.Policy(locale.Tag, ValidationMembers.TranslationCompleteness), message, project, locale.Span, diagnostics, (pair.Key, locale.Tag));
                }
            }
            string[] markupNames = names?.ToArray() ?? [];
            // The contract and its readable-name check depend on the base entry, the slots, the markup names and the position.
            if (pair.Value.Source.Memo?.Contracted is { } contracted && ReferenceEquals(contracted.Origin, pair.Value) && ReferenceEquals(contracted.Requirements, requirements) &&
                contracted.Id == contracts.Count && contracted.Names.AsSpan().SequenceEqual(markupNames))
            {
                contracts.Add(contracted.Contract);
                if (contracted.ReadableProblem is { } problem) diagnostics.Add(Rmf2ReadableNamesV1.DiagnosticId, TranslationDiagnosticSeverity.Warning, problem, pair.Value.Source.Node.NameLocation);
                continue;
            }
            contracts.Add(new(contracts.Count, pair.Key, Array.AsReadOnly(pair.Value.Source.Path), pair.Value.Linked.Message.Inputs,
                requirements, markupNames.Length != 0, markupNames, profiles[pair.Value].Kind, profiles[pair.Value].Skeletons));
            // The readable C# surface leaves out a message with a reserved or clashing name;
            // its encoded member stays. Reported by every host because `code` is required.
            string? readable = null;
            if (Rmf2ReadableNamesV1.SupportsClassName(manifest.ClassName) &&
                !Rmf2ReadableNamesV1.TryCreate(manifest.ClassName, contracts[^1], out _, out string? readableProblem))
                diagnostics.Add(Rmf2ReadableNamesV1.DiagnosticId, TranslationDiagnosticSeverity.Warning, readable = readableProblem!, pair.Value.Source.Node.NameLocation);
            if (pair.Value.Source.Memo is { } contractMemo) contractMemo.Contracted = new(pair.Value, requirements, markupNames, contracts.Count - 1, contracts[^1], readable);
        }
        // A reference factory whose readable name is another factory's encoded name keeps its encoded name.
        List<(string Key, string Problem)> referenceProblems;
        if (state?.ReadableReferences is { } readableMemo && SameItems(readableMemo.Contracts, contracts)) referenceProblems = readableMemo.Problems;
        else
        {
            referenceProblems = new List<(string Key, string Problem)>();
            Generation.Rmf2CSharpOutputRendererV5.ReadableReferences(manifest.ClassName, contracts, referenceProblems);
            if (state is not null) state.ReadableReferences = new(contracts.ToArray(), referenceProblems);
        }
        foreach ((string key, string problem) in referenceProblems)
            diagnostics.Add(Rmf2ReadableNamesV1.DiagnosticId, TranslationDiagnosticSeverity.Warning, problem, canonical[key].Source.Node.NameLocation);
        // Allowed extra keys are retained for dynamic consumers, but never add
        // canonical generated API members. Their contracts must still agree.
        var extraContracts = new List<Rmf2MessageContractV5>();
        foreach (var pair in extras)
        {
            var requirements = markup.Requirements(pair.Key, pair.Value.Linked, pair.Value.Source.Node.NameLocation, diagnostics);
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries.Values.Where(locale => locale.ContainsKey(pair.Key)).Select(locale => locale[pair.Key]))
            {
                ValidateCallerContract(pair.Value, entry);
                Rmf2ProjectMarkupV5.ValidateSlots(pair.Value.Source.Logical, requirements, entry.Linked, entry.Source.Node.NameLocation, diagnostics);
                if (!ReferenceEquals(entry, pair.Value)) ValidateStructure(pair.Value, entry);
                names.UnionWith(entry.Linked.Names);
            }
            extraContracts.Add(new(-1, pair.Key, Array.AsReadOnly(pair.Value.Source.Path), pair.Value.Linked.Message.Inputs,
                requirements, names.Count != 0, names.ToArray(), profiles[pair.Value].Kind, profiles[pair.Value].Skeletons));
        }
        if (markup.SlotConstraints is { } constraints)
        {
            if (constraints.Kind != JsonKind.Object) Error("markup.slots must be an object.", project, constraints.Span);
            else foreach (var item in constraints.Properties)
            {
                var contract = contracts.FirstOrDefault(contract => contract.Key == item.Name);
                if (contract is null || item.Value.Kind != JsonKind.Object) { Error("Unknown canonical message or invalid slot constraints '" + item.Name + "'.", project, item.Value.Span); continue; }
                foreach (var slot in item.Value.Properties)
                    if (!contract.Slots.ContainsKey(slot.Name)) Error("Unknown source slot '" + slot.Name + "'.", project, slot.Value.Span);
            }
        }
        // Override sources of an application (CompileRmf2OverlayV5): each message is checked against the library's
        // definition of its key and replaces the library's message in its locale; a locale the library lacks is added.
        var overlayKeys = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var overlayAdded = new List<string>();
        if (overlaySources is not null)
        {
            var known = canonical.Values.Concat(extras.Values).Select(entry => entry.Source.Logical).Order(StringComparer.Ordinal).ToArray();
            var byKey = contracts.Concat(extraContracts).ToDictionary(contract => contract.Key, StringComparer.Ordinal);
            var keysByLocale = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var added = new List<LocaleModel>();
            // The fallback of every added locale is chosen once all of them are known, by the runtimes' rule, so de-CH falls
            // back to an added de whatever the order of the files.
            string[] libraryTags = locales.Select(locale => locale.Tag).Order(StringComparer.Ordinal).ToArray();
            string[] addedTags = overlayExtracted.Where(source => !entries.ContainsKey(source.Locale) && (canonical.ContainsKey(source.Key) || extras.ContainsKey(source.Key)))
                .Select(source => source.Locale).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            string[] candidates = libraryTags.Concat(addedTags).Order(StringComparer.Ordinal).ToArray();
            foreach (string tag in addedTags)
            {
                entries[tag] = new LocaleEntries();
                added.Add(new LocaleModel(tag, AddedLocaleFallback(tag, candidates, libraryTags, manifest.DefaultLocale), default, default));
                overlayAdded.Add(tag);
            }
            foreach (var source in overlayExtracted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string logical = source.Logical;
                if (!canonical.TryGetValue(source.Key, out var origin) && !extras.TryGetValue(source.Key, out origin))
                {
                    string? suggestion = SuggestKey(logical, known);
                    diagnostics.Add("RTR0103", TranslationDiagnosticSeverity.Error, "Catalog '" + manifest.Id + "' has no message '" + logical + "' to override." +
                        (suggestion is null ? "" : " Did you mean '" + suggestion + "'?"), source.Source, NameSpan(source));
                    continue;
                }
                var target = entries[source.Locale];
                if (Compile(source, origin.Linked.Message.Inputs) is not { } compiledEntry) continue;
                var entry = Profile(compiledEntry, kinds[source.Key], new LocaleEntries());
                Rmf2MessageContractV5 contract = byKey[source.Key];
                ValidateCallerContract(origin, entry);
                Rmf2ProjectMarkupV5.ValidateSlots(source.Key, contract.Slots, entry.Linked, entry.Source.Node.NameLocation, diagnostics);
                // A message the library formats as plain text (no markup in any of its locales) cannot gain markup: its
                // generated accessor returns a string.
                if (!contract.Structured && entry.Linked.Names.Count != 0)
                    diagnostics.Add("RTR0103", TranslationDiagnosticSeverity.Error, "'" + logical + "' is plain text in catalog '" + manifest.Id +
                        "', so its override cannot use markup (" + string.Join(", ", entry.Linked.Names) + ").", source.Source, NameSpan(source));
                ValidateStructure(origin, entry);
                ValidateInputParity(origin, entry);
                ValidateMaxLength(origin, entry);
                target[source.Key] = entry;
                if (!keysByLocale.TryGetValue(source.Locale, out var keys)) keysByLocale[source.Locale] = keys = new SortedSet<string>(StringComparer.Ordinal);
                keys.Add(source.Key);
            }
            foreach (var pair in keysByLocale) overlayKeys[pair.Key] = pair.Value.ToArray();
            locales = locales.Concat(added).OrderBy(locale => locale.Tag, StringComparer.Ordinal).ToArray();
        }
        if (keepLocales is not null)
        {
            var declared = locales.ToDictionary(locale => locale.Tag, StringComparer.OrdinalIgnoreCase);
            string[] available = locales.Select(locale => locale.Tag).Order(StringComparer.Ordinal).ToArray();
            string[] matchable = PseudoLocaleTags.WithoutPseudo(available);
            var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest.DefaultLocale };
            foreach (string requested in keepLocales)
            {
                // The CLI rejects an invalid tag before compiling.
                if (!TryCanonicalizeLocale(requested, out string tag)) continue;
                // The locale the runtimes resolve the application locale to (LocaleNegotiation.Match, the ESM matchLocale): the
                // locale itself, else CLDR likely subtags and parent locales (zh-TW to zh-Hant, sr-ME to sr-Latn, en-GB to
                // en-001), else a truncation with the same likely script, never a pseudo-locale for another tag. A subset that
                // keeps it resolves to it too.
                string? current = declared.ContainsKey(tag) ? tag : CldrLocales.Match(tag, matchable) ?? CldrLocales.Truncate(tag, matchable);
                // The locale and its fallback chain; the graph was validated, the bound only guards against a cycle.
                for (int hops = 0; current is not null && hops <= locales.Length && declared.TryGetValue(current, out var model); hops++)
                {
                    kept.Add(model.Tag);
                    current = model.Fallback;
                }
            }
            locales = locales.Where(locale => kept.Contains(locale.Tag)).ToArray();
            foreach (string key in overlayKeys.Keys.Where(key => !kept.Contains(key)).ToArray()) overlayKeys.Remove(key);
            overlayAdded.RemoveAll(locale => !kept.Contains(locale));
        }
        var linkedLocales = new List<Rmf2LocaleV5>();
        var byTag = locales.ToDictionary(locale => locale.Tag, StringComparer.OrdinalIgnoreCase);
        // One translation per entry, shared by its locale and every locale that falls back to it.
        var translations = new Dictionary<Rmf2ProjectEntryV5, Rmf2TranslationV5>(extracted.Count, ReferenceEqualityComparer.Instance);
        Rmf2TranslationV5 TranslationOf(Rmf2ProjectEntryV5 entry)
        {
            // In a session, the entry's memo holds its translation; else one translation per entry for this compile.
            if (entry.Source.Memo?.Translated is { } memo && ReferenceEquals(memo.Entry, entry)) return memo.Translation;
            if (!translations.TryGetValue(entry, out var translation)) translations.Add(entry, translation = entry.Translation);
            return translation;
        }
        // The content-locale checks of a resolved resource depend on the resource only.
        void CheckContentLocale(Rmf2ProjectEntryV5 entry, Rmf2TranslationV5 resource)
        {
            if (entry.Source.Memo is not { } memo) { ValidateContentLocale(resource); return; }
            if (memo.Translated is not { } translated || !ReferenceEquals(translated.Entry, entry) || !ReferenceEquals(translated.Translation, resource))
                memo.Translated = translated = new(entry, resource, Capture(() => ValidateContentLocale(resource)));
            Replay(translated.ContentDiagnostics);
        }
        foreach (var locale in locales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // The locale's entries, then its fallbacks' for the keys it lacks, in key order: a merge of the sorted key lists.
            var chain = new List<LocaleEntries>();
            LocaleModel? current = locale;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current is not null && visited.Add(current.Tag))
            {
                chain.Add(entries[current.Tag]);
                current = current.Fallback is not null && byTag.TryGetValue(current.Fallback, out var next) ? next : null;
            }
            var resolved = new List<Rmf2ProjectEntryV5>(chain[0].Count);
            var resolvedKeys = new List<string>(chain[0].Count);
            foreach (var link in chain) Merge(resolvedKeys, resolved, link);
            // Formatters use the resource's content locale, including fallback.
            var resources = new Rmf2TranslationV5[resolved.Count];
            for (int resourceIndex = 0; resourceIndex < resources.Length; resourceIndex++)
            {
                var resource = resources[resourceIndex] = TranslationOf(resolved[resourceIndex]);
                CheckContentLocale(resolved[resourceIndex], resource);
            }
            linkedLocales.Add(new(locale.Tag, locale.Fallback, entries[locale.Tag].Values.Select(TranslationOf).ToArray(), resources));
        }
        // Adds the entries of a fallback whose keys are not resolved yet, keeping the key order (both lists are ordered).
        static void Merge(List<string> keys, List<Rmf2ProjectEntryV5> values, LocaleEntries fallback)
        {
            string[] order = fallback.Order;
            if (keys.Count == 0)
            {
                foreach (string key in order) { keys.Add(key); values.Add(fallback[key]); }
                return;
            }
            // A complete translation has the same keys as its fallback: nothing to add.
            if (keys.Count == order.Length)
            {
                bool same = true;
                for (int index = 0; same && index < order.Length; index++) same = string.Equals(keys[index], order[index], StringComparison.Ordinal);
                if (same) return;
            }
            var mergedKeys = new List<string>(Math.Max(keys.Count, order.Length));
            var mergedValues = new List<Rmf2ProjectEntryV5>(mergedKeys.Capacity);
            int left = 0, right = 0;
            while (left < keys.Count || right < order.Length)
            {
                int comparison = left == keys.Count ? 1 : right == order.Length ? -1 : string.CompareOrdinal(keys[left], order[right]);
                if (comparison <= 0) { mergedKeys.Add(keys[left]); mergedValues.Add(values[left]); left++; if (comparison == 0) right++; }
                else { mergedKeys.Add(order[right]); mergedValues.Add(fallback[order[right]]); right++; }
            }
            keys.Clear(); keys.AddRange(mergedKeys);
            values.Clear(); values.AddRange(mergedValues);
        }
        if (Failed()) return Result();
        // The next compile of the session reuses the key orders while the keys are the same.
        if (state is not null) foreach (var locale in entries) state.SetKeyOrder(locale.Key, locale.Value.Order);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var locale in entries.Values)
            foreach (var entry in locale.UnorderedValues)
                if (entry.Linked.Names.Count != 0) usedNames.UnionWith(entry.Linked.Names);
        var usedContracts = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(new SortedDictionary<string, Rmf2MarkupContractV5>(markup.Contracts.Where(pair => usedNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));
        var canonicalNames = contracts.SelectMany(contract => contract.MarkupNames).ToHashSet(StringComparer.Ordinal);
        var callerMarkup = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(new SortedDictionary<string, Rmf2MarkupContractV5>(usedContracts.Where(pair => canonicalNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));
        // The contract exports depend on the contracts (memoized per key) and the used markup; a session reuses them while those are the same.
        string[] callerNames = callerMarkup.Keys.ToArray();
        string callerContract;
        if (state?.CallerContract is { } callerMemo && callerMemo.Catalog == manifest.Id && callerMemo.Markup.AsSpan().SequenceEqual(callerNames) && SameItems(callerMemo.Contracts, contracts))
            callerContract = callerMemo.Json;
        else
        {
            callerContract = JsonSerializer.Serialize(new
        {
            callerContractVersion = 1,
            profile = Rmf2ProjectV5.Profile, messageGrammarVersion = Rmf2ProjectV5.MessageGrammarVersion,
            runtimeAbiVersion = Rmf2ProjectV5.RuntimeAbiVersion, generatedNameVersion = Rmf2GeneratedNamesV1.Version,
            catalog = manifest.Id,
            messages = contracts.Select(contract => new
            {
                key = contract.Key,
                path = contract.Path,
                inputs = contract.Inputs.Select(input => new { name = input.Name, type = input.Type }),
            }),
            markup = Rmf2ProjectMarkupV5.Export(callerMarkup, contracts),
        });
            if (state is not null) state.CallerContract = new(manifest.Id, contracts.ToArray(), callerNames, callerContract);
        }
        var exported = contracts.Concat(extraContracts).ToArray();
        string[] usedMarkup = usedContracts.Keys.ToArray();
        string markupContract;
        if (state?.MarkupExport is { } exportMemo && exportMemo.Used.AsSpan().SequenceEqual(usedMarkup) && SameItems(exportMemo.Contracts, exported) && SameContentLocales(exportMemo.Locales, linkedLocales))
            markupContract = exportMemo.Json;
        else
        {
            markupContract = Rmf2ProjectMarkupV5.Export(usedContracts, exported, linkedLocales);
            if (state is not null) state.MarkupExport = new(exported, usedMarkup, linkedLocales.ToArray(), markupContract);
        }
        static bool SameItems<T>(IReadOnlyList<T> left, IReadOnlyList<T> right) where T : class
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++) if (!ReferenceEquals(left[index], right[index])) return false;
            return true;
        }
        // The export reads each locale's resolved keys and their content locales only.
        static bool SameContentLocales(IReadOnlyList<Rmf2LocaleV5> left, IReadOnlyList<Rmf2LocaleV5> right)
        {
            if (left.Count != right.Count) return false;
            for (int index = 0; index < left.Count; index++)
            {
                if (left[index].Tag != right[index].Tag) return false;
                IReadOnlyList<Rmf2TranslationV5> before = left[index].ResolvedResources, after = right[index].ResolvedResources;
                if (before.Count != after.Count) return false;
                for (int resource = 0; resource < before.Count; resource++)
                    if (!ReferenceEquals(before[resource], after[resource]) && (before[resource].Key != after[resource].Key || before[resource].ContentLocale != after[resource].ContentLocale)) return false;
            }
            return true;
        }
        return Result(new(manifest.Id, manifest.CodeNamespace, manifest.ClassName, manifest.Visibility, manifest.DefaultLocale,
            manifest.UnsupportedLocale, manifest.MissingKey, contracts.AsReadOnly(), extraContracts.AsReadOnly(), linkedLocales.AsReadOnly(), usedContracts,
            markupContract, HashV5(Encoding.UTF8.GetBytes(callerContract)),
            SourceHashV5(project, overlaySources is null ? sources : sources.Concat(overlaySources).ToArray()))
            {
                Overrides = manifest.Overrides.Select(item => new Rmf2OverrideV5(item.Catalog, item.From)).ToArray(),
                OverlayKeys = overlayKeys,
                OverlayAddedLocales = overlayAdded.ToArray(),
                ReferenceFallbackText = manifest.ReferenceFallbackText,
                BidiIsolation = manifest.BidiIsolation,
                PluralCategoryPolicies = locales.ToDictionary(locale => locale.Tag, locale => manifest.Policy(locale.Tag, ValidationMembers.PluralCategories), StringComparer.Ordinal),
                FormatCurrencies = manifest.FormatCurrencies.Order(StringComparer.Ordinal).ToArray(),
                FormatUnits = manifest.FormatUnits.Order(StringComparer.Ordinal).ToArray(),
                // As declared, also in a relaxed compilation: status reports it for catalogs whose translations are incomplete.
                CompletenessPolicies = locales.ToDictionary(locale => locale.Tag, locale => manifest.DeclaredPolicy(locale.Tag, ValidationMembers.TranslationCompleteness), StringComparer.Ordinal),
            });

        // The semantic lowering of one message, from the syntax its reader read (the same bytes a reparse would read).
        Rmf2SemanticResultV5 LowerSource(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Unit is { } unit) return unit.Lower(source.Node.Message!, source.Node.MessageSyntax, callerInputs, cancellationToken);
            var syntax = source.Node.MessageSyntax ?? Mf2SyntaxReader.Read(TranslationSource.Owned(source.Source.Path, Encoding.UTF8.GetBytes(source.Node.Message!)), options, cancellationToken);
            return Rmf2SemanticCompilerV5.Lower(syntax, callerInputs, options, cancellationToken);
        }
        // The memoized compile of a source against these caller inputs (by identity), or null.
        CompiledMemo? CompiledFor(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs) =>
            source.Memo?.Compiled is { } memo && ReferenceEquals(memo.Inputs, callerInputs) ? memo : null;
        Rmf2ProjectEntryV5? Compile(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs, Rmf2SemanticResultV5? lowered = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (source.Memo is not { } memo) return CompileNow(source, callerInputs, lowered);
            if (CompiledFor(source, callerInputs) is not { } compiled)
            {
                Rmf2ProjectEntryV5? entry = null;
                if (counters is not null) counters.MessagesCompiled++;
                var reported = Capture(() => entry = CompileNow(source, callerInputs, lowered));
                memo.Compiled = compiled = new(callerInputs, entry, reported);
            }
            Replay(compiled.Diagnostics);
            return compiled.Entry;
        }
        Rmf2ProjectEntryV5? CompileNow(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs, Rmf2SemanticResultV5? lowered)
        {
            Rmf2SemanticResultV5 result = lowered ?? LowerSource(source, callerInputs);
            foreach (var diagnostic in result.Diagnostics)
            {
                int from = source.Node.MessageByteMap[Math.Min(diagnostic.Location.StartByte, source.Node.MessageByteMap.Count - 1)];
                int to = source.Node.MessageByteMap[Math.Min(diagnostic.Location.StartByte + diagnostic.Location.LengthBytes, source.Node.MessageByteMap.Count - 1)];
                diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, source.Source, new(from, Math.Max(0, to - from)));
            }
            if (result.Message is null) return null;
            ValidateMetadataV5(source.Node, result.Message, diagnostics);
            ValidatePluralCategories(source, result.Message);
            ValidateFormattedBooleans(source, result.Message);
            if (result.Message.Variants.Any(variant => variant.Nodes.Count == 0))
                AddPolicyDiagnostic("RTR0021", manifest.Policy(source.Locale, ValidationMembers.EmptyValues), "Resource '" + source.Logical + "' has an empty variant.", source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            return new(source, markup.Link(result.Message, source.Node.NameLocation, diagnostics));
        }
        Rmf2ProjectEntryV5 Profile(Rmf2ProjectEntryV5 entry, string? expectedKind, LocaleEntries locale)
        {
            Rmf2DocumentAnalysisV5 analysis;
            Rmf2ProjectEntryV5 updated;
            if (entry.Source.Memo is not { } memo)
            {
                analysis = Rmf2DocumentProfileV5.Analyze(entry.Source.Logical, entry.Linked.Message, expectedKind, markup.Contracts, entry.Source.Node.NameLocation, diagnostics);
                updated = entry with { Linked = entry.Linked with { Message = analysis.Message } };
            }
            else
            {
                if (memo.Profiled is not { } profiled || !ReferenceEquals(profiled.Entry, entry) || profiled.ExpectedKind != expectedKind)
                {
                    Rmf2DocumentAnalysisV5? computed = null;
                    var reported = Capture(() => computed = Rmf2DocumentProfileV5.Analyze(entry.Source.Logical, entry.Linked.Message, expectedKind, markup.Contracts, entry.Source.Node.NameLocation, diagnostics));
                    memo.Profiled = profiled = new(entry, expectedKind, entry with { Linked = entry.Linked with { Message = computed!.Message } }, computed, reported);
                }
                Replay(profiled.Diagnostics);
                analysis = profiled.Analysis;
                updated = profiled.Updated;
            }
            profiles[updated] = analysis;
            kinds.TryAdd(entry.Source.Key, analysis.Kind);
            if (locale.TryGetValue(entry.Source.Key, out var current) && ReferenceEquals(current, entry)) locale[entry.Source.Key] = updated;
            return updated;
        }
        void ValidatePair(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated, IReadOnlyDictionary<string, Rmf2SlotV5> requirements)
        {
            ValidateCallerContract(origin, translated);
            Rmf2ProjectMarkupV5.ValidateSlots(origin.Source.Logical, requirements, translated.Linked, translated.Source.Node.NameLocation, diagnostics);
            if (!ReferenceEquals(translated, origin)) { ValidateStructure(origin, translated); ValidateInputParity(origin, translated); }
            ValidateMaxLength(origin, translated);
        }
        // Structure is locked (RTR0074): every translated document variant must match one of
        // the source skeletons. RTR0071 flags a match against a different key tuple's variant.
        void ValidateStructure(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            Rmf2DocumentAnalysisV5 source = profiles[origin], target = profiles[translated];
            if (source.Kind != Rmf2DocumentProfileV5.Document) return;
            var sourceSkeletons = source.Variants.Select(variant => variant.Skeleton).OfType<IReadOnlyList<Rmf2SkeletonNodeV5>>().ToArray();
            for (int index = 0; index < target.Variants.Count; index++)
            {
                if (target.Variants[index].Encoded is not { } encoded) continue;
                if (!source.Variants.Any(variant => variant.Encoded == encoded))
                {
                    var difference = Rmf2DocumentProfileV5.FirstDifference(sourceSkeletons, target.Variants[index].Skeleton!);
                    diagnostics.Add("RTR0074", TranslationDiagnosticSeverity.Error, "Translated structure of '" + origin.Source.Logical + "' does not match any source structure (structure is locked); first difference at '" +
                        difference.Path + "': expected " + difference.Expected + ", found " + difference.Found + ".", translated.Source.Node.NameLocation);
                    continue;
                }
                var keys = target.Message.Variants[index].Keys;
                int same = Enumerable.Range(0, source.Message.Variants.Count).FirstOrDefault(candidate => SameKeys(source.Message.Variants[candidate].Keys, keys), -1);
                if (same >= 0 && source.Variants[same].Encoded is { } expected && expected != encoded)
                    diagnostics.Add("RTR0071", TranslationDiagnosticSeverity.Warning, "A translated variant of '" + origin.Source.Logical + "' matches the structure of a different source variant, not the one with the same keys; it may have been copied from the wrong variant.", translated.Source.Node.NameLocation);
            }
            static bool SameKeys(IReadOnlyList<Rmf2KeyV5> left, IReadOnlyList<Rmf2KeyV5> right) =>
                left.Count == right.Count && left.Zip(right).All(pair => pair.First.Kind == pair.Second.Kind && (pair.First.Canonical ?? pair.First.Value) == (pair.Second.Canonical ?? pair.Second.Value));
        }
        void ValidateCallerContract(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            foreach (var input in translated.Linked.Message.Inputs)
                if (!origin.Linked.Message.Inputs.Any(expected => expected.Name == input.Name && expected.Type == input.Type))
                    diagnostics.Add("RTR0016", TranslationDiagnosticSeverity.Error, "Translation introduces or changes caller input '" + input.Name + "' for '" + origin.Source.Logical + "'.", translated.Source.Node.NameLocation);
        }
        // RTR0094/RTR0095: plural or ordinal keys checked against the categories of the message's own locale, from
        // the generated CLDR rule metadata that both runtimes select with. A selector position whose keys are all
        // exact numbers (and the catch-all) chose not to use categories and is not checked for missing ones. An
        // integer selector (int64, as `:integer` declares) is checked against the categories integers reach.
        void ValidatePluralCategories(Rmf2ProjectSourceV5 source, Rmf2MessageV5 message)
        {
            TranslationPolicy policy = manifest.Policy(source.Locale, ValidationMembers.PluralCategories);
            if (policy == TranslationPolicy.Allow || message.Selectors.Count == 0 || message.Syntax.Variants.Count != message.Variants.Count) return;
            // A pseudo-locale is synthesized from the base locale's variants; nobody can add the categories its own
            // rules reach, so its plural keys are not checked (the base locale's are).
            if (manifest.PseudoLocales.Any(pseudo => string.Equals(pseudo.Tag, source.Locale, StringComparison.OrdinalIgnoreCase))) return;
            string logical = source.Logical;
            for (int position = 0; position < message.Selectors.Count; position++)
            {
                Rmf2SelectorV5 selector = message.Selectors[position];
                if (selector.Function is not ("plural" or "ordinal")) continue;
                bool ordinal = selector.Function == "ordinal";
                IReadOnlyList<string> categories = Rmf2AuthoringService.PluralCategories(source.Locale, ordinal, selector.Type == "int64");
                bool integer = categories.Count != Rmf2AuthoringService.PluralCategories(source.Locale, ordinal).Count;
                if (categories.Count == 0) continue; // RTR0031 reports a locale without rules.
                var used = new SortedSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < message.Variants.Count; index++)
                {
                    Rmf2KeyV5 key = message.Variants[index].Keys[position];
                    if (key.Kind != "literal" || key.Canonical is not null || key.Value is not ("zero" or "one" or "two" or "few" or "many" or "other")) continue;
                    used.Add(key.Value);
                    if (categories.Contains(key.Value)) continue;
                    TextSourceLocation location = message.Syntax.Variants[index].KeyLocations[position];
                    AddPolicyDiagnostic("RTR0094", policy, "Variant key '" + key.Value + "' of '" + logical + "' is never selected in locale '" + source.Locale + "', whose " +
                        (ordinal ? "ordinal" : "plural") + " categories" + (integer ? " for integers" : "") + " are " + string.Join(", ", categories) + ".", source.Source, MessageSpan(source, location), diagnostics, (source.Key, source.Locale));
                }
                if (used.Count == 0) continue;
                string[] missing = categories.Where(category => category != "other" && !used.Contains(category)).ToArray();
                if (missing.Length != 0)
                    AddPolicyDiagnostic("RTR0095", policy, "'" + logical + "' has no variant for the " + (ordinal ? "ordinal" : "plural") + " " + (missing.Length == 1 ? "category " : "categories ") +
                        string.Join(", ", missing) + " of locale '" + source.Locale + "'; " + (missing.Length == 1 ? "it falls" : "they fall") + " to the '*' variant.",
                        source.Source, NameSpan(source), diagnostics, (source.Key, source.Locale));
            }
        }
        // RTR0097: a boolean formatted as text prints "true" or "false" in every locale.
        void ValidateFormattedBooleans(Rmf2ProjectSourceV5 source, Rmf2MessageV5 message)
        {
            TranslationPolicy policy = manifest.Policy(source.Locale, ValidationMembers.FormattedBooleans);
            if (policy == TranslationPolicy.Allow) return;
            string[] names = message.Variants.SelectMany(variant => variant.Nodes).OfType<Rmf2ExpressionNodeV5>()
                .Where(node => node.Expression.ValueType == "boolean")
                .Select(node => node.Expression.Operand.Kind is "input" or "local" ? "$" + node.Expression.Operand.Value : "|" + node.Expression.Operand.Value + "|")
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (names.Length != 0)
                AddPolicyDiagnostic("RTR0097", policy, "'" + source.Logical + "' in locale '" + source.Locale + "' formats the boolean " + string.Join(", ", names) +
                    " as text, which reads 'true' or 'false' in every locale; select on it with .match and write the words in the variants.", source.Source, NameSpan(source), diagnostics, (source.Key, source.Locale));
        }
        // RTR0096: an input the base displays that no variant of the translation displays (union over variants).
        void ValidateInputParity(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            TranslationPolicy policy = manifest.Policy(translated.Source.Locale, ValidationMembers.InputParity);
            if (policy == TranslationPolicy.Allow) return;
            SortedSet<string> expected = DisplayedInputs(origin.Linked.Message), actual = DisplayedInputs(translated.Linked.Message);
            string[] dropped = expected.Where(name => !actual.Contains(name)).ToArray();
            if (dropped.Length != 0)
                AddPolicyDiagnostic("RTR0096", policy, "The '" + translated.Source.Locale + "' translation of '" + origin.Source.Logical + "' displays " +
                    (dropped.Length == 1 ? "no " : "none of ") + string.Join(", ", dropped.Select(name => "$" + name)) + ", which base locale '" + manifest.DefaultLocale + "' displays.",
                    translated.Source.Source, NameSpan(translated.Source), diagnostics, (origin.Source.Key, translated.Source.Locale));
        }
        // RTR0098: the literal text of a variant is longer than @maxLength; placeholder values and markup are not counted.
        void ValidateMaxLength(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            // The base locale's limit is the developer's UI constraint: a translation may only tighten it, and XLIFF
            // export writes the same effective limit (EffectiveMaxLengthV5) as the unit's size restriction.
            if (MaxLengthV5(translated.Source.Node.Properties) is { } own && MaxLengthV5(origin.Source.Node.Properties) is { } baseLimit && own > baseLimit)
                diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Warning, "The '" + translated.Source.Locale + "' @maxLength " + own.ToString(CultureInfo.InvariantCulture) + " of '" +
                    origin.Source.Logical + "' is larger than the limit of " + baseLimit.ToString(CultureInfo.InvariantCulture) + " in base locale '" + manifest.DefaultLocale +
                    "'; a translation can only lower it, so " + baseLimit.ToString(CultureInfo.InvariantCulture) + " applies.", translated.Source.Source, NameSpan(translated.Source));
            TranslationPolicy policy = manifest.Policy(translated.Source.Locale, ValidationMembers.MaxLength);
            if (policy == TranslationPolicy.Allow || EffectiveMaxLengthV5(MaxLengthV5(origin.Source.Node.Properties), MaxLengthV5(translated.Source.Node.Properties)) is not { } limit) return;
            int longest = translated.Linked.Message.Variants.Select(variant => TextLengthV5(variant.Nodes)).DefaultIfEmpty(0).Max();
            if (longest > limit)
                AddPolicyDiagnostic("RTR0098", policy, "The '" + translated.Source.Locale + "' text of '" + origin.Source.Logical + "' is " + longest.ToString(CultureInfo.InvariantCulture) +
                    " characters long" + (translated.Linked.Message.Variants.Count > 1 ? " in its longest variant" : "") + ", more than its @maxLength of " + limit.ToString(CultureInfo.InvariantCulture) +
                    "; placeholder values are not counted.", translated.Source.Source, NameSpan(translated.Source), diagnostics, (origin.Source.Key, translated.Source.Locale));
        }
        void ValidateContentLocale(Rmf2TranslationV5 resource)
        {
            foreach (var selector in resource.Message.Selectors)
                if (selector.Function is "plural" or "ordinal" && !SupportsBuiltInPlural(resource.ContentLocale, selector.Function == "ordinal"))
                    diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in plural registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
            var expressions = resource.Message.Declarations.Select(d => d.Expression).Concat(resource.Message.Variants.SelectMany(v => v.Nodes).OfType<Rmf2ExpressionNodeV5>().Select(n => n.Expression)).ToArray();
            if (expressions.Any(expression => expression.Function == "runic:relative-time") && !SupportsRelativeTime(resource.ContentLocale))
                diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in relative-time registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
            if (expressions.Any(expression => expression.Function is Rmf2FormatDataV1.CurrencyFunction or Rmf2FormatDataV1.UnitFunction ||
                    expression.Options.Any(option => option.Name == Rmf2FormatDataV1.NotationOption)) && !Rmf2FormatDataV1.Supports(resource.ContentLocale))
                diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The pinned CLDR currency, unit and compact-notation data does not cover content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
            // D055.4: a currency or unit chosen at run time comes from the set runic.json declares.
            foreach (var expression in expressions)
            {
                if (expression.Function is not (Rmf2FormatDataV1.CurrencyFunction or Rmf2FormatDataV1.UnitFunction) ||
                    !expression.Options.Any(option => option.Name == expression.Function && option.Value.Kind is "input" or "local")) continue;
                bool currency = expression.Function == Rmf2FormatDataV1.CurrencyFunction;
                if ((currency ? manifest.FormatCurrencies : manifest.FormatUnits).Count == 0)
                    diagnostics.Add("RTR0102", TranslationDiagnosticSeverity.Error, "A " + (currency ? "currency" : "unit") + " chosen at run time must come from a declared set: list the " +
                        (currency ? "currencies" : "units") + " it may take in runic.json formats." + (currency ? "currencies" : "units") + ".", resource.SourceLocation);
            }
            // A list value formats with the list patterns even without an explicit :runic:list.
            if (expressions.Any(expression => expression.Function == "runic:list" || expression.ValueType == "list") && !TranslationCapabilityRegistry.SupportsList(resource.ContentLocale))
                diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in list registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
        }
        void Error(string message, TranslationSource source, ByteSpan span) => diagnostics.Add("RTR0062", TranslationDiagnosticSeverity.Error, message, source, span);
    }

    // A location inside the extracted message, mapped back to its source file.
    private static ByteSpan MessageSpan(Rmf2ProjectSourceV5 source, TextSourceLocation location)
    {
        IReadOnlyList<int> map = source.Node.MessageByteMap;
        int from = map[Math.Min(location.StartByte, map.Count - 1)];
        int to = map[Math.Min(location.StartByte + location.LengthBytes, map.Count - 1)];
        return new(from, Math.Max(0, to - from));
    }

    private static ByteSpan NameSpan(Rmf2ProjectSourceV5 source) => new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes);

    // The inputs a message displays: those its pattern expressions read, directly or through locals.
    private static SortedSet<string> DisplayedInputs(Rmf2MessageV5 message)
    {
        if (message.Inputs.Count == 0) return new SortedSet<string>(StringComparer.Ordinal);
        var locals = new Dictionary<string, Rmf2ExpressionV5>(StringComparer.Ordinal);
        foreach (Rmf2DeclarationV5 declaration in message.Declarations)
            if (declaration.Kind == "local") locals.TryAdd(declaration.Name, declaration.Expression);
        var result = new SortedSet<string>(StringComparer.Ordinal);
        foreach (Rmf2ExpressionNodeV5 node in message.Variants.SelectMany(variant => variant.Nodes).OfType<Rmf2ExpressionNodeV5>())
        {
            Rmf2ValueV5 operand = node.Expression.Operand;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (operand.Kind == "local" && visited.Add(operand.Value) && locals.TryGetValue(operand.Value, out Rmf2ExpressionV5? local)) operand = local.Operand;
            if (operand.Kind == "input") result.Add(operand.Value);
        }
        return result;
    }

    // The @maxLength of a message's properties, or null when it declares none or an invalid one (RTR0051).
    internal static int? MaxLengthV5(IReadOnlyList<string> properties)
    {
        foreach (string property in properties)
        {
            int space = property.IndexOf(' ');
            if ((space < 0 ? property : property.Substring(0, space)) != "maxLength") continue;
            return int.TryParse(space < 0 ? "" : property.Substring(space + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value > 0 ? value : null;
        }
        return null;
    }

    // The limit a translation is held to: the smaller of the base's and its own @maxLength, either of which may be absent.
    internal static int? EffectiveMaxLengthV5(int? baseLimit, int? translationLimit) =>
        baseLimit is { } a && translationLimit is { } b ? Math.Min(a, b) : baseLimit ?? translationLimit;

    // Unicode code points of the literal text after NFC normalization, as XLIFF's xliff:codepoints profile counts.
    private static int TextLengthV5(IReadOnlyList<Rmf2NodeV5> nodes)
    {
        int length = 0;
        foreach (Rmf2TextV5 text in nodes.OfType<Rmf2TextV5>())
        {
            string normalized = text.Value.Normalize(NormalizationForm.FormC);
            for (int index = 0; index < normalized.Length; index++)
                if (!char.IsLowSurrogate(normalized[index])) length++;
        }
        return length;
    }

    /// <summary>
    /// The entries of one locale by key, enumerated in ordinal key order. A hash table with the key order computed once
    /// per compile, or taken from the previous compile of a session (<see cref="Rmf2LinkState"/>) while the locale's keys
    /// are the same, instead of a sorted tree that every compile rebuilds.
    /// </summary>
    private sealed class LocaleEntries : IEnumerable<KeyValuePair<string, Rmf2ProjectEntryV5>>
    {
        private readonly Dictionary<string, Rmf2ProjectEntryV5> _byKey;
        private readonly string[]? _previousOrder;
        private string[]? _order;

        internal LocaleEntries(string[]? previousOrder = null)
        {
            _previousOrder = previousOrder;
            _byKey = new Dictionary<string, Rmf2ProjectEntryV5>(previousOrder?.Length ?? 0, StringComparer.Ordinal);
        }

        internal int Count => _byKey.Count;
        internal void Add(string key, Rmf2ProjectEntryV5 entry) { _byKey.Add(key, entry); _order = null; }
        internal bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out Rmf2ProjectEntryV5 entry) => _byKey.TryGetValue(key, out entry);
        internal bool ContainsKey(string key) => _byKey.ContainsKey(key);
        internal Rmf2ProjectEntryV5 this[string key]
        {
            get => _byKey[key];
            set { if (!_byKey.ContainsKey(key)) _order = null; _byKey[key] = value; }
        }

        /// <summary>The keys in ordinal order.</summary>
        internal string[] Order
        {
            get
            {
                if (_order is not null) return _order;
                if (_previousOrder is { } previous && previous.Length == _byKey.Count && Array.TrueForAll(previous, _byKey.ContainsKey)) return _order = previous;
                var keys = new string[_byKey.Count];
                _byKey.Keys.CopyTo(keys, 0);
                Array.Sort(keys, StringComparer.Ordinal);
                return _order = keys;
            }
        }

        /// <summary>The entries in no particular order.</summary>
        internal Dictionary<string, Rmf2ProjectEntryV5>.ValueCollection UnorderedValues => _byKey.Values;

        internal IEnumerable<Rmf2ProjectEntryV5> Values
        {
            get { foreach (string key in Order) yield return _byKey[key]; }
        }

        internal KeyValuePair<string, Rmf2ProjectEntryV5>[] ToArray()
        {
            string[] order = Order;
            var result = new KeyValuePair<string, Rmf2ProjectEntryV5>[order.Length];
            for (int index = 0; index < order.Length; index++) result[index] = new(order[index], _byKey[order[index]]);
            return result;
        }

        public IEnumerator<KeyValuePair<string, Rmf2ProjectEntryV5>> GetEnumerator()
        {
            foreach (string key in Order) yield return new(key, _byKey[key]);
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // One node of a file as a compile extracts it: its paths and, for a message, the source the compile links.
    private sealed record Rmf2NodeExtractV5(Rmf2ResourceNode Node, string Logical, string Identity, string Metadata, string? Key, Rmf2ProjectSourceV5? Message);

    private static List<Rmf2ProjectSourceV5> ReadRmf2ProjectSourcesV5(TranslationSource project, TranslationSource[] sources,
        IReadOnlyDictionary<TranslationSource, Rmf2SourceUnitV5>? units, JsonValue config, HashSet<string> locales, DiagnosticBag diagnostics, TranslationCompilerOptions options, CancellationToken cancellationToken,
        string? onlyLocale = null, Rmf2LinkContext? context = null)
    {
        string directory = ProjectDirectory(project.Path);
        var mounts = new List<(string Root, string[] Prefix)>();
        JsonProperty? roots = config.Property("sourceRoots");
        if (roots is null) mounts.Add((directory, Array.Empty<string>()));
        else if (roots.Value.Kind != JsonKind.Array || roots.Value.Items.Count == 0) Error("sourceRoots requires a nonempty array of mounts.", project, roots.Value.Span);
        else foreach (var item in roots.Value.Items)
        {
            if (item.Kind != JsonKind.Object) { Error("Invalid source mount.", project, item.Span); continue; }
            ValidateKnownMembers(item, Rmf2MountMembers, project, diagnostics);
            var path = Required(item, "path", JsonKind.String, project, diagnostics);
            var prefix = Required(item, "namespace", JsonKind.Array, project, diagnostics);
            if (path is null || prefix is null) continue;
            if (string.IsNullOrWhiteSpace(path.Value.Text)) { Error("Each source root must declare a non-empty path.", project, path.Value.Span); continue; }
            string root = NormalizeResourceRoot(directory, path.Value.Text!);
            string[] segments = prefix.Value.Items.Select(value => value.Text ?? "").ToArray();
            if (prefix.Value.Items.Any(value => value.Kind != JsonKind.String) || segments.Any(segment => !IsIdentifier(segment))) Error("Mount namespaces require identifier segments.", project, prefix.Value.Span);
            if (mounts.Any(mount => root.StartsWith(mount.Root, StringComparison.OrdinalIgnoreCase) || mount.Root.StartsWith(root, StringComparison.OrdinalIgnoreCase))) Error("Overlapping source roots are not allowed.", project, path.Value.Span);
            mounts.Add((root, segments));
        }
        var result = new List<Rmf2ProjectSourceV5>();
        var portable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var spellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identities = new Dictionary<string, (bool Group, string Metadata, TextSourceLocation Location)>(StringComparer.Ordinal);
        var pathKinds = new Dictionary<string, bool>(StringComparer.Ordinal);
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);
        bool? groupedInput = null;
        // Reading a grouped file depends on its bytes only: every file the loop below reads is read in parallel first.
        var read = CompilerParallel.Map(sources.Length, index =>
        {
            TranslationSource source = sources[index];
            if (!source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) || (units is not null && units.ContainsKey(source))) return null;
            if (onlyLocale is not null)
            {
                string file = source.Path[(source.Path.LastIndexOf('/') + 1)..^".rmf2".Length];
                if (!TryCanonicalizeLocale(file, out string fileLocale) || !string.Equals(fileLocale, onlyLocale, StringComparison.OrdinalIgnoreCase)) return null;
            }
            return Rmf2ResourceReader.Read(source, options, cancellationToken);
        }, cancellationToken);
        for (int sourceIndex = 0; sourceIndex < sources.Length; sourceIndex++)
        {
            var source = sources[sourceIndex];
            cancellationToken.ThrowIfCancellationRequested();
            bool grouped = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
            bool direct = source.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase);
            var matches = (grouped || direct) ? mounts.Where(mount => source.Path.StartsWith(mount.Root, StringComparison.Ordinal)).ToArray() : Array.Empty<(string Root, string[] Prefix)>();
            if ((!grouped && !direct) || matches.Length != 1) { Error("Translation sources must be .mf2 or .rmf2 beneath exactly one source root.", source, new(0, 0)); continue; }
            if (groupedInput is { } prior && prior != grouped)
            {
                Error("A translation project cannot mix direct .mf2 and grouped .rmf2 sources; choose one source representation.", source, new(0, 0));
                continue;
            }
            groupedInput = grouped;
            string currentPath = source.Path;
            while (currentPath.Length != 0)
            {
                if (portable.TryGetValue(currentPath, out string? previous) && previous != currentPath) Error("Case-only source or directory alias conflicts with '" + previous + "'.", source, new(0, 0));
                portable[currentPath] = currentPath;
                int slash = currentPath.LastIndexOf('/'); currentPath = slash < 0 ? "" : currentPath.Substring(0, slash);
            }
            string[] parts = grouped ? source.Path.Substring(matches[0].Root.Length).Split('/') : Array.Empty<string>();
            string spelling;
            string[] namespaceParts;
            string[] directPath = Array.Empty<string>();
            if (grouped)
            {
                spelling = parts[^1].Substring(0, parts[^1].Length - 5);
                namespaceParts = parts.Take(parts.Length - 1).ToArray();
            }
            else
            {
                if (!TryMf2Identity(matches[0].Root, source.Path, out spelling, out string messageId)) { Error("Direct MF2 sources must use '{locale}/{message-id}.mf2' relative to their source root.", source, new(0, 0)); continue; }
                namespaceParts = Array.Empty<string>();
                directPath = new[] { messageId };
            }
            if (!TryCanonicalizeLocale(spelling, out string locale) || namespaceParts.Any(segment => !IsIdentifier(segment)) || directPath.Any(segment => !IsIdentifier(segment))) { Error("Invalid translation locale filename or directory segment.", source, new(0, 0)); continue; }
            if (onlyLocale is not null && !string.Equals(locale, onlyLocale, StringComparison.OrdinalIgnoreCase)) continue;
            if (spellings.TryGetValue(locale, out string? existing) && existing != spelling) Error("Duplicate canonical locale spelling.", source, new(0, 0));
            spellings[locale] = spelling; locales.Add(locale);
            string[] mount = matches[0].Prefix.Concat(namespaceParts).ToArray();
            Rmf2SourceUnitV5? unit = units is not null && units.TryGetValue(source, out var match) ? match : null;
            // A cached compile reuses a file's extracted nodes while the file and the link context (which decides the
            // mounts) are unchanged, so its messages keep their identity and memos from one compile to the next.
            var extract = context is not null && unit is not null ? unit.Extraction(context) as Rmf2NodeExtractV5[] : null;
            IReadOnlyList<Rmf2ResourceNode>? nodes = null;
            if (grouped)
            {
                var resource = unit?.Resource ?? read[sourceIndex] ?? Rmf2ResourceReader.Read(source, options, cancellationToken);
                foreach (var diagnostic in resource.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
                nodes = resource.Nodes;
            }
            else if (extract is null)
            {
                string? message = unit?.DirectMessage;
                if (message is null)
                {
                    try { message = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
                    catch (DecoderFallbackException) { Error("MF2 requires valid UTF-8.", source, new(0, 0)); continue; }
                }
                var location = DiagnosticBag.Location(source, new(0, source.Bytes.Length));
                var node = new Rmf2ResourceNode(directPath, message, Array.Empty<string>(), Array.Empty<string>(), location, location,
                    Rmf2ByteMap.Identity(source.Bytes.Length + 1));
                node.MessageSyntax = unit?.DirectSyntax ?? Mf2SyntaxReader.Read(source, options, cancellationToken);
                nodes = new[] { node };
            }
            if (extract is null)
            {
                extract = new Rmf2NodeExtractV5[nodes!.Count];
                for (int index = 0; index < extract.Length; index++)
                {
                    var node = nodes[index];
                    string[] path = mount.Concat(node.Path).ToArray();
                    string logical = string.Join(".", path);
                    string? key = node.IsGroup ? null : string.Join("_", path);
                    extract[index] = new(node, logical, locale + ":" + logical, string.Join("\n", node.Comments.Concat(node.Properties)), key,
                        key is null ? null : new Rmf2ProjectSourceV5(key, path, locale, source, node, unit) { Memo = context is null ? null : new() });
                }
                if (context is not null && unit is not null) unit.SetExtraction(context, extract);
            }
            for (int index = 1; index <= mount.Length; index++)
            {
                string logical = string.Join(".", mount.Take(index));
                Register(logical, locale + ":" + logical, true, "", DiagnosticBag.Location(source, new(0, 0)));
            }
            foreach (var item in extract)
            {
                Register(item.Logical, item.Identity, item.Node.IsGroup, item.Metadata, item.Node.NameLocation);
                if (item.Message is not { } extracted) continue;
                string key = item.Key!, logical = item.Logical;
                if (generated.TryGetValue(key, out string? previous) && previous != logical) diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Logical path '" + logical + "' collides with '" + previous + "' under canonical underscore keys.", item.Node.NameLocation);
                generated[key] = logical;
                result.Add(extracted);
            }
            void Register(string logical, string identity, bool group, string metadata, TextSourceLocation location)
            {
                if (pathKinds.TryGetValue(logical, out bool previousKind) && previousKind != group) diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, "Message/group collision for '" + logical + "' across locales.", location);
                pathKinds[logical] = group;
                if (identities.TryGetValue(identity, out var first) && (!group || !first.Group || (metadata.Length != 0 && first.Metadata.Length != 0)))
                {
                    diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, "Duplicate declaration '" + identity + "'; first declaration: " + first.Location + ".", location);
                    diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, "Conflicting declaration '" + identity + "': " + location + ".", first.Location);
                }
                else if (!identities.ContainsKey(identity) || metadata.Length != 0) identities[identity] = (group, metadata, location);
            }
        }
        return result;
        void Error(string message, TranslationSource source, ByteSpan span) => diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, message, source, span);
    }

    // Override sources are grouped <locale>.rmf2 files whose keys are the library catalog's full logical keys, whatever
    // source roots the library itself uses.
    private static List<Rmf2ProjectSourceV5> ReadRmf2OverlaySourcesV5(TranslationSource[] sources, DiagnosticBag diagnostics,
        TranslationCompilerOptions options, CancellationToken cancellationToken)
    {
        var result = new List<Rmf2ProjectSourceV5>();
        var declared = new Dictionary<string, TextSourceLocation>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string file = source.Path[(source.Path.LastIndexOf('/') + 1)..];
            if (!file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) || !TryCanonicalizeLocale(file[..^5], out string locale))
            {
                diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Override sources must be grouped '<locale>.rmf2' files.", source, new(0, 0));
                continue;
            }
            var resource = Rmf2ResourceReader.Read(source, options, cancellationToken);
            foreach (var diagnostic in resource.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
            foreach (var node in resource.Nodes)
            {
                if (node.IsGroup) continue;
                string[] path = node.Path.ToArray();
                string identity = locale + ":" + string.Join(".", path);
                if (declared.TryGetValue(identity, out var first))
                {
                    diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, "Duplicate declaration '" + identity + "'; first declaration: " + first + ".", node.NameLocation);
                    continue;
                }
                declared[identity] = node.NameLocation;
                result.Add(new(string.Join("_", path), path, locale, source, node, null));
            }
        }
        return result;
    }

    // The fallback of a locale that override sources add, as both runtimes choose it for an added locale without an explicit
    // fallback (OverlayTranslationProvider in .NET, overlayState in ESM): its nearest CLDR parent among the library's and the
    // added locales (de-CH to an added de or the library's de, es-MX to es-419, zh-TW to zh-Hant, never sr-Latn-RS to a
    // Cyrillic sr), else the library locale CLDR likely subtags match (de-CH to de-DE), else the library's base locale.
    // A pseudo-locale (en-XA, ar-XB) is never chosen. Generated code passes it explicitly, so the runtimes follow this choice.
    internal static string AddedLocaleFallback(string locale, string[] candidates, string[] libraryLocales, string defaultLocale)
    {
        candidates = PseudoLocaleTags.WithoutPseudo(candidates);
        libraryLocales = PseudoLocaleTags.WithoutPseudo(libraryLocales);
        return CldrLocales.NearestParent(locale, candidates) ??
            CldrLocales.Match(locale, libraryLocales) ?? CldrLocales.Truncate(locale, libraryLocales) ??
            defaultLocale;
    }

    private static string NormalizeResourceRoot(string directory, string path)
    {
        string combined = path.StartsWith('/') ? path : directory + path;
        var segments = new List<string>();
        foreach (string part in combined.Replace('\\', '/').Split('/'))
        {
            if (part == "." || part.Length == 0) continue;
            if (part == ".." && segments.Count > 0 && segments[^1] != "..") segments.RemoveAt(segments.Count - 1);
            else segments.Add(part);
        }
        return (combined.StartsWith('/') ? "/" : "") + string.Join("/", segments) + (segments.Count == 0 ? "" : "/");
    }

    // "@param $name - description" (the dash is optional) by NFC input name; null when the entry documents none.
    private static Dictionary<string, string>? ParameterDescriptionsV5(IReadOnlyList<string> properties)
    {
        Dictionary<string, string>? result = null;
        foreach (string property in properties)
        {
            if (!property.StartsWith("param ", StringComparison.Ordinal)) continue;
            string value = property.Substring("param ".Length).Trim();
            if (!value.StartsWith('$')) continue;
            string[] parts = value.Split(' ', 2);
            string name = parts[0].Substring(1).Normalize(NormalizationForm.FormC), description = parts.Length < 2 ? "" : parts[1].Trim();
            if (description.StartsWith('-')) description = description.Substring(1).Trim();
            if (name.Length != 0 && description.Length != 0) (result ??= new Dictionary<string, string>(StringComparer.Ordinal))[name] = description;
        }
        return result;
    }

    private static void ValidateMetadataV5(Rmf2ResourceNode node, Rmf2MessageV5 message, DiagnosticBag diagnostics)
    {
        foreach (string property in node.Properties)
        {
            int space = property.IndexOf(' '); string name = space < 0 ? property : property.Substring(0, space);
            string value = space < 0 ? "" : property.Substring(space + 1).Trim();
            if (name == "param")
            {
                string parameter = value.Split(' ', 2)[0].TrimStart('$').Normalize(NormalizationForm.FormC);
                if (!value.StartsWith('$') || !message.Inputs.Any(input => input.Name == parameter)) Error("@param must document an existing caller input.");
            }
            else if (name == "maxLength")
            {
                if (MaxLengthV5(new[] { property }) is null) Error("@maxLength must be a positive whole number of characters, such as @maxLength 40.");
                else if (node.Properties.Count(item => item == "maxLength" || item.StartsWith("maxLength ", StringComparison.Ordinal)) > 1) Error("@maxLength may appear only once per message.");
            }
            else if (name == "example")
            {
                try
                {
                    using var example = JsonDocument.Parse(value);
                    if (example.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                    var items = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                    foreach (var item in example.RootElement.EnumerateObject())
                        if (!items.TryAdd(item.Name.Normalize(NormalizationForm.FormC), item.Value)) throw new JsonException();
                    if (items.Count != message.Inputs.Count) throw new JsonException();
                    foreach (var input in message.Inputs)
                    {
                        if (!items.TryGetValue(input.Name, out var item)) throw new JsonException();
                        bool valid = input.Type switch
                        {
                            "int64" => item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out _),
                            "decimal" => item.ValueKind == JsonValueKind.Number && Rmf2DecimalV5.TryCanonicalize(item.GetRawText(), out _),
                            "boolean" => item.ValueKind is JsonValueKind.True or JsonValueKind.False,
                            "list" => item.ValueKind == JsonValueKind.Array && item.EnumerateArray().All(element => element.ValueKind == JsonValueKind.String),
                            _ => item.ValueKind == JsonValueKind.String,
                        };
                        if (!valid) throw new JsonException();
                    }
                }
                catch (JsonException) { Error("@example must be a JSON object matching the message caller inputs."); }
            }
        }
        void Error(string message) => diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Error, message, node.NameLocation);
    }

    private static string SourceHashV5(TranslationSource project, TranslationSource[] sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(Encoding.UTF8.GetBytes(Rmf2ProjectV5.Profile));
        Add(project.Bytes);
        string directory = ProjectDirectory(project.Path);
        foreach (var source in sources)
        {
            Add(Encoding.UTF8.GetBytes(source.Path.StartsWith(directory, StringComparison.Ordinal) ? source.Path.Substring(directory.Length) : source.Path));
            Add(source.Bytes);
        }
        return "sha256:" + Convert.ToHexStringLower(hash.GetHashAndReset());
        void Add(byte[] value)
        {
            hash.AppendData(Encoding.ASCII.GetBytes(value.Length.ToString(CultureInfo.InvariantCulture) + ":"));
            hash.AppendData(value);
        }
    }
    private static string HashV5(byte[] bytes) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}
