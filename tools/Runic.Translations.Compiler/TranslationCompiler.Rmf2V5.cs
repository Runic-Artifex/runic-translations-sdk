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

    private sealed record Rmf2ProjectSourceV5(string Key, string[] Path, string Locale, TranslationSource Source, Rmf2ResourceNode Node, Rmf2SourceUnitV5? Unit);
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

    private static Rmf2ProjectCompilation CompileRmf2ProjectV5(TranslationSource project, TranslationSource[] sources,
        IReadOnlyDictionary<TranslationSource, Rmf2SourceUnitV5>? units, TranslationCompilerOptions options, CancellationToken cancellationToken,
        bool pseudoLocales = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new DiagnosticBag();
        Rmf2ProjectCompilation Result(Rmf2ProjectV5? value = null) => new(value, Array.AsReadOnly(diagnostics.ToSortedArray()));
        bool Failed() => diagnostics.Items.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
        if (RejectDuplicateSourcePaths(new[] { project }, sources, diagnostics)) return Result();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, options, cancellationToken);
        ManifestModel? manifest = parsed.Root is null ? null : ReadMf2Project(parsed, diagnostics, options);
        if (manifest is null || Failed()) return Result();
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
        var extracted = ReadRmf2ProjectSourcesV5(project, sources, units, config, discovered, diagnostics, options, cancellationToken);
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
        if (extracted.Select(source => source.Key).Distinct(StringComparer.Ordinal).Count() > options.MaximumKeysPerCatalog)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Catalog key count exceeds the configured limit.", project, config.Span);
        if (Failed()) return Result();

        var entries = locales.ToDictionary(locale => locale.Tag, _ => new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        // Keys each locale declares, compiled or not: a message that failed to compile has reported
        // its own error, so RTR0010 and RTR0011 do not report it again as missing or extra.
        var declaredKeys = locales.ToDictionary(locale => locale.Tag, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase);
        foreach (var source in extracted)
            if (declaredKeys.TryGetValue(source.Locale, out var keys)) keys.Add(source.Key);
        // RTR0010 points at the locale's grouped file beside the base-locale file of the message.
        var groupedFiles = new Dictionary<string, TranslationSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in extracted)
            if (source.Source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
                groupedFiles.TryAdd(ProjectDirectory(source.Source.Path) + "\0" + source.Locale, source.Source);
        string[] baseKeys = extracted.Where(source => source.Locale == manifest.DefaultLocale).Select(source => string.Join(".", source.Path)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var canonical = new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal);
        var extras = new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal);
        foreach (var source in extracted.Where(source => source.Locale == manifest.DefaultLocale))
            if (Compile(source, null) is { } entry) { canonical.Add(source.Key, entry); entries[source.Locale].Add(source.Key, entry); }
        foreach (var source in extracted.Where(source => source.Locale != manifest.DefaultLocale))
        {
            cancellationToken.ThrowIfCancellationRequested();
            canonical.TryGetValue(source.Key, out var origin);
            // A base message that failed to compile reported its own error; the key is not extra.
            if (origin is null && manifest.Policy(source.Locale, ValidationMembers.ExtraLocaleKeys) != TranslationPolicy.Allow && !declaredKeys[manifest.DefaultLocale].Contains(source.Key))
            {
                string logical = string.Join(".", source.Path);
                string? suggestion = SuggestKey(logical, baseKeys);
                AddPolicyDiagnostic("RTR0011", manifest.Policy(source.Locale, ValidationMembers.ExtraLocaleKeys), "Locale '" + source.Locale + "' defines '" + logical + "', which base locale '" + manifest.DefaultLocale + "' does not define." +
                    (suggestion is null ? "" : " Did you mean '" + suggestion + "'?"), source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            }
            if (origin is null) extras.TryGetValue(source.Key, out origin);
            if (Compile(source, origin?.Linked.Message.Inputs) is { } entry)
            {
                entries[source.Locale].Add(source.Key, entry);
                if (!canonical.ContainsKey(source.Key)) extras.TryAdd(source.Key, entry);
            }
        }

        // The document profile: the base locale (or the first locale of an extra key) infers
        // the content kind; every other locale is checked against it and normalized.
        var profiles = new Dictionary<Rmf2ProjectEntryV5, Rmf2DocumentAnalysisV5>(ReferenceEqualityComparer.Instance);
        var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in canonical.ToArray()) canonical[pair.Key] = Profile(pair.Value, null, entries[manifest.DefaultLocale]);
        foreach (var pair in extras.ToArray()) extras[pair.Key] = Profile(pair.Value, null, entries[pair.Value.Source.Locale]);
        foreach (var locale in entries.Values)
            foreach (var pair in locale.ToArray())
                if (!profiles.ContainsKey(pair.Value)) Profile(pair.Value, kinds[pair.Key], locale);

        var contracts = new List<Rmf2MessageContractV5>();
        foreach (var pair in canonical)
        {
            var requirements = markup.Requirements(pair.Key, pair.Value.Linked, pair.Value.Source.Node.NameLocation, diagnostics);
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var locale in locales)
            {
                if (entries[locale.Tag].TryGetValue(pair.Key, out var translated))
                {
                    ValidateCallerContract(pair.Value, translated);
                    Rmf2ProjectMarkupV5.ValidateSlots(pair.Key, requirements, translated.Linked, translated.Source.Node.NameLocation, diagnostics);
                    if (!ReferenceEquals(translated, pair.Value)) { ValidateStructure(pair.Value, translated); ValidateInputParity(pair.Value, translated); }
                    ValidateMaxLength(pair.Value, translated);
                    names.UnionWith(translated.Linked.Names);
                }
                else if (locale.Tag != manifest.DefaultLocale && !declaredKeys[locale.Tag].Contains(pair.Key))
                {
                    string message = "Locale '" + locale.Tag + "' does not translate '" + string.Join(".", pair.Value.Source.Path) + "' from base locale '" + manifest.DefaultLocale + "'.";
                    if (groupedFiles.TryGetValue(ProjectDirectory(pair.Value.Source.Source.Path) + "\0" + locale.Tag, out var file))
                        AddPolicyDiagnostic("RTR0010", manifest.Policy(locale.Tag, ValidationMembers.TranslationCompleteness), message, file, new(0, 0), diagnostics);
                    else
                        AddPolicyDiagnostic("RTR0010", manifest.Policy(locale.Tag, ValidationMembers.TranslationCompleteness), message, project, locale.Span, diagnostics);
                }
            }
            contracts.Add(new(contracts.Count, pair.Key, Array.AsReadOnly(pair.Value.Source.Path), pair.Value.Linked.Message.Inputs,
                requirements, names.Count != 0, names.ToArray(), profiles[pair.Value].Kind, profiles[pair.Value].Skeletons));
            // The readable C# surface leaves out a message with a reserved or clashing name;
            // its encoded member stays. Reported by every host because `code` is required.
            if (Rmf2ReadableNamesV1.SupportsClassName(manifest.ClassName) &&
                !Rmf2ReadableNamesV1.TryCreate(manifest.ClassName, contracts[^1], out _, out string? readableProblem))
                diagnostics.Add(Rmf2ReadableNamesV1.DiagnosticId, TranslationDiagnosticSeverity.Warning, readableProblem!, pair.Value.Source.Node.NameLocation);
        }
        // A reference factory whose readable name is another factory's encoded name keeps its encoded name.
        var referenceProblems = new List<(string Key, string Problem)>();
        Generation.Rmf2CSharpOutputRendererV5.ReadableReferences(manifest.ClassName, contracts, referenceProblems);
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
                Rmf2ProjectMarkupV5.ValidateSlots(pair.Key, requirements, entry.Linked, entry.Source.Node.NameLocation, diagnostics);
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
        var linkedLocales = new List<Rmf2LocaleV5>();
        var byTag = locales.ToDictionary(locale => locale.Tag, StringComparer.OrdinalIgnoreCase);
        foreach (var locale in locales)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = new SortedDictionary<string, Rmf2TranslationV5>(StringComparer.Ordinal);
            LocaleModel? current = locale;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current is not null && visited.Add(current.Tag))
            {
                foreach (var entry in entries[current.Tag]) resolved.TryAdd(entry.Key, entry.Value.Translation);
                current = current.Fallback is not null && byTag.TryGetValue(current.Fallback, out var next) ? next : null;
            }
            // Formatters use the resource's content locale, including fallback.
            foreach (var resource in resolved.Values) ValidateContentLocale(resource);
            linkedLocales.Add(new(locale.Tag, locale.Fallback, entries[locale.Tag].Values.Select(entry => entry.Translation).ToArray(), resolved.Values.ToArray()));
        }
        if (Failed()) return Result();
        var usedNames = entries.Values.SelectMany(locale => locale.Values).SelectMany(entry => entry.Linked.Names).ToHashSet(StringComparer.Ordinal);
        var usedContracts = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(new SortedDictionary<string, Rmf2MarkupContractV5>(markup.Contracts.Where(pair => usedNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));
        var canonicalNames = contracts.SelectMany(contract => contract.MarkupNames).ToHashSet(StringComparer.Ordinal);
        var callerMarkup = new ReadOnlyDictionary<string, Rmf2MarkupContractV5>(new SortedDictionary<string, Rmf2MarkupContractV5>(usedContracts.Where(pair => canonicalNames.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal));
        string callerContract = JsonSerializer.Serialize(new
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
        return Result(new(manifest.Id, manifest.CodeNamespace, manifest.ClassName, manifest.Visibility, manifest.DefaultLocale,
            manifest.UnsupportedLocale, manifest.MissingKey, contracts.AsReadOnly(), extraContracts.AsReadOnly(), linkedLocales.AsReadOnly(), usedContracts,
            Rmf2ProjectMarkupV5.Export(usedContracts, contracts.Concat(extraContracts).ToArray(), linkedLocales), HashV5(Encoding.UTF8.GetBytes(callerContract)), SourceHashV5(project, sources))
            { ReferenceFallbackText = manifest.ReferenceFallbackText });

        Rmf2ProjectEntryV5? Compile(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Rmf2SemanticResultV5 result;
            if (source.Unit is { } unit) result = unit.Lower(source.Node.Message!, callerInputs, cancellationToken);
            else
            {
                var input = new TranslationSource(source.Source.Path, Encoding.UTF8.GetBytes(source.Node.Message!));
                result = callerInputs is null ? Rmf2SemanticCompilerV5.Compile(input, options, cancellationToken) : Rmf2SemanticCompilerV5.CompileWithCallerContract(input, callerInputs, options, cancellationToken);
            }
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
                AddPolicyDiagnostic("RTR0021", manifest.Policy(source.Locale, ValidationMembers.EmptyValues), "Resource '" + source.Key + "' has an empty variant.", source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            return new(source, markup.Link(result.Message, source.Node.NameLocation, diagnostics));
        }
        Rmf2ProjectEntryV5 Profile(Rmf2ProjectEntryV5 entry, string? expectedKind, SortedDictionary<string, Rmf2ProjectEntryV5> locale)
        {
            var analysis = Rmf2DocumentProfileV5.Analyze(entry.Source.Key, entry.Linked.Message, expectedKind, markup.Contracts, entry.Source.Node.NameLocation, diagnostics);
            var updated = entry with { Linked = entry.Linked with { Message = analysis.Message } };
            profiles[updated] = analysis;
            kinds.TryAdd(entry.Source.Key, analysis.Kind);
            if (locale.TryGetValue(entry.Source.Key, out var current) && ReferenceEquals(current, entry)) locale[entry.Source.Key] = updated;
            return updated;
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
                    diagnostics.Add("RTR0074", TranslationDiagnosticSeverity.Error, "Translated structure of '" + origin.Source.Key + "' does not match any source structure (structure is locked); first difference at '" +
                        difference.Path + "': expected " + difference.Expected + ", found " + difference.Found + ".", translated.Source.Node.NameLocation);
                    continue;
                }
                var keys = target.Message.Variants[index].Keys;
                int same = Enumerable.Range(0, source.Message.Variants.Count).FirstOrDefault(candidate => SameKeys(source.Message.Variants[candidate].Keys, keys), -1);
                if (same >= 0 && source.Variants[same].Encoded is { } expected && expected != encoded)
                    diagnostics.Add("RTR0071", TranslationDiagnosticSeverity.Warning, "A translated variant of '" + origin.Source.Key + "' matches the structure of a different source variant, not the one with the same keys; it may have been copied from the wrong variant.", translated.Source.Node.NameLocation);
            }
            static bool SameKeys(IReadOnlyList<Rmf2KeyV5> left, IReadOnlyList<Rmf2KeyV5> right) =>
                left.Count == right.Count && left.Zip(right).All(pair => pair.First.Kind == pair.Second.Kind && (pair.First.Canonical ?? pair.First.Value) == (pair.Second.Canonical ?? pair.Second.Value));
        }
        void ValidateCallerContract(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            foreach (var input in translated.Linked.Message.Inputs)
                if (!origin.Linked.Message.Inputs.Any(expected => expected.Name == input.Name && expected.Type == input.Type))
                    diagnostics.Add("RTR0016", TranslationDiagnosticSeverity.Error, "Translation introduces or changes caller input '" + input.Name + "' for '" + origin.Source.Key + "'.", translated.Source.Node.NameLocation);
        }
        // RTR0094/RTR0095: plural or ordinal keys checked against the categories of the message's own locale, from
        // the generated CLDR rule metadata that both runtimes select with. A selector position whose keys are all
        // exact numbers (and the catch-all) chose not to use categories and is not checked for missing ones. An
        // integer selector (int64, as `:integer` declares) is checked against the categories integers reach.
        void ValidatePluralCategories(Rmf2ProjectSourceV5 source, Rmf2MessageV5 message)
        {
            TranslationPolicy policy = manifest.Policy(source.Locale, ValidationMembers.PluralCategories);
            if (policy == TranslationPolicy.Allow || message.Selectors.Count == 0 || message.Syntax.Variants.Count != message.Variants.Count) return;
            string logical = string.Join(".", source.Path);
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
                        (ordinal ? "ordinal" : "plural") + " categories" + (integer ? " for integers" : "") + " are " + string.Join(", ", categories) + ".", source.Source, MessageSpan(source, location), diagnostics);
                }
                if (used.Count == 0) continue;
                string[] missing = categories.Where(category => category != "other" && !used.Contains(category)).ToArray();
                if (missing.Length != 0)
                    AddPolicyDiagnostic("RTR0095", policy, "'" + logical + "' has no variant for the " + (ordinal ? "ordinal" : "plural") + " " + (missing.Length == 1 ? "category " : "categories ") +
                        string.Join(", ", missing) + " of locale '" + source.Locale + "'; " + (missing.Length == 1 ? "it falls" : "they fall") + " to the '*' variant.",
                        source.Source, NameSpan(source), diagnostics);
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
                AddPolicyDiagnostic("RTR0097", policy, "'" + string.Join(".", source.Path) + "' in locale '" + source.Locale + "' formats the boolean " + string.Join(", ", names) +
                    " as text, which reads 'true' or 'false' in every locale; select on it with .match and write the words in the variants.", source.Source, NameSpan(source), diagnostics);
        }
        // RTR0096: an input the base displays that no variant of the translation displays (union over variants).
        void ValidateInputParity(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            TranslationPolicy policy = manifest.Policy(translated.Source.Locale, ValidationMembers.InputParity);
            if (policy == TranslationPolicy.Allow) return;
            SortedSet<string> expected = DisplayedInputs(origin.Linked.Message), actual = DisplayedInputs(translated.Linked.Message);
            string[] dropped = expected.Where(name => !actual.Contains(name)).ToArray();
            if (dropped.Length != 0)
                AddPolicyDiagnostic("RTR0096", policy, "The '" + translated.Source.Locale + "' translation of '" + string.Join(".", origin.Source.Path) + "' displays " +
                    (dropped.Length == 1 ? "no " : "none of ") + string.Join(", ", dropped.Select(name => "$" + name)) + ", which base locale '" + manifest.DefaultLocale + "' displays.",
                    translated.Source.Source, NameSpan(translated.Source), diagnostics);
        }
        // RTR0098: the literal text of a variant is longer than @maxLength; placeholder values and markup are not counted.
        void ValidateMaxLength(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            // The base locale's limit is the developer's UI constraint: a translation may only tighten it, and XLIFF
            // export writes the same effective limit (EffectiveMaxLengthV5) as the unit's size restriction.
            if (MaxLengthV5(translated.Source.Node.Properties) is { } own && MaxLengthV5(origin.Source.Node.Properties) is { } baseLimit && own > baseLimit)
                diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Warning, "The '" + translated.Source.Locale + "' @maxLength " + own.ToString(CultureInfo.InvariantCulture) + " of '" +
                    string.Join(".", origin.Source.Path) + "' is larger than the limit of " + baseLimit.ToString(CultureInfo.InvariantCulture) + " in base locale '" + manifest.DefaultLocale +
                    "'; a translation can only lower it, so " + baseLimit.ToString(CultureInfo.InvariantCulture) + " applies.", translated.Source.Source, NameSpan(translated.Source));
            TranslationPolicy policy = manifest.Policy(translated.Source.Locale, ValidationMembers.MaxLength);
            if (policy == TranslationPolicy.Allow || EffectiveMaxLengthV5(MaxLengthV5(origin.Source.Node.Properties), MaxLengthV5(translated.Source.Node.Properties)) is not { } limit) return;
            int longest = translated.Linked.Message.Variants.Select(variant => TextLengthV5(variant.Nodes)).DefaultIfEmpty(0).Max();
            if (longest > limit)
                AddPolicyDiagnostic("RTR0098", policy, "The '" + translated.Source.Locale + "' text of '" + string.Join(".", origin.Source.Path) + "' is " + longest.ToString(CultureInfo.InvariantCulture) +
                    " characters long" + (translated.Linked.Message.Variants.Count > 1 ? " in its longest variant" : "") + ", more than its @maxLength of " + limit.ToString(CultureInfo.InvariantCulture) +
                    "; placeholder values are not counted.", translated.Source.Source, NameSpan(translated.Source), diagnostics);
        }
        void ValidateContentLocale(Rmf2TranslationV5 resource)
        {
            foreach (var selector in resource.Message.Selectors)
                if (selector.Function is "plural" or "ordinal" && !SupportsBuiltInPlural(resource.ContentLocale, selector.Function == "ordinal"))
                    diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in plural registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
            var expressions = resource.Message.Declarations.Select(d => d.Expression).Concat(resource.Message.Variants.SelectMany(v => v.Nodes).OfType<Rmf2ExpressionNodeV5>().Select(n => n.Expression)).ToArray();
            if (expressions.Any(expression => expression.Function == "runic:relative-time") && !SupportsRelativeTime(resource.ContentLocale))
                diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in relative-time registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
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

    private static List<Rmf2ProjectSourceV5> ReadRmf2ProjectSourcesV5(TranslationSource project, TranslationSource[] sources,
        IReadOnlyDictionary<TranslationSource, Rmf2SourceUnitV5>? units, JsonValue config, HashSet<string> locales, DiagnosticBag diagnostics, TranslationCompilerOptions options, CancellationToken cancellationToken)
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
        foreach (var source in sources)
        {
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
            if (spellings.TryGetValue(locale, out string? existing) && existing != spelling) Error("Duplicate canonical locale spelling.", source, new(0, 0));
            spellings[locale] = spelling; locales.Add(locale);
            string[] mount = matches[0].Prefix.Concat(namespaceParts).ToArray();
            Rmf2SourceUnitV5? unit = units is not null && units.TryGetValue(source, out var match) ? match : null;
            IReadOnlyList<Rmf2ResourceNode> nodes;
            if (grouped)
            {
                var resource = unit?.Resource ?? Rmf2ResourceReader.Read(source, options, cancellationToken);
                foreach (var diagnostic in resource.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
                nodes = resource.Nodes;
            }
            else
            {
                string? message = unit?.DirectMessage;
                if (message is null)
                {
                    try { message = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
                    catch (DecoderFallbackException) { Error("MF2 requires valid UTF-8.", source, new(0, 0)); continue; }
                }
                var location = DiagnosticBag.Location(source, new(0, source.Bytes.Length));
                var node = new Rmf2ResourceNode(directPath, message, Array.Empty<string>(), Array.Empty<string>(), location, location,
                    Enumerable.Range(0, source.Bytes.Length + 1).ToArray());
                node.MessageSyntax = unit?.DirectSyntax ?? Mf2SyntaxReader.Read(source, options, cancellationToken);
                nodes = new[] { node };
            }
            for (int index = 1; index <= mount.Length; index++) Register(mount.Take(index).ToArray(), true, "", DiagnosticBag.Location(source, new(0, 0)));
            foreach (var node in nodes)
            {
                string[] path = mount.Concat(node.Path).ToArray();
                Register(path, node.IsGroup, string.Join("\n", node.Comments.Concat(node.Properties)), node.NameLocation);
                if (node.IsGroup) continue;
                string key = string.Join("_", path), logical = string.Join(".", path);
                if (generated.TryGetValue(key, out string? previous) && previous != logical) diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Logical path '" + logical + "' collides with '" + previous + "' under canonical underscore keys.", node.NameLocation);
                generated[key] = logical;
                result.Add(new(key, path, locale, source, node, unit));
            }
            void Register(string[] path, bool group, string metadata, TextSourceLocation location)
            {
                string logical = string.Join(".", path), identity = locale + ":" + logical;
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
