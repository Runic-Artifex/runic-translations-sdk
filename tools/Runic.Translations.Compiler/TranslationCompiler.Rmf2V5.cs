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
    internal static TranslationProjectProfileSelection SelectProjectProfile(TranslationSource project,
        TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, options, cancellationToken);
        TranslationProjectProfile profile = parsed.Root is { Kind: JsonKind.Object } root
            ? ReadProjectProfile(root, project, diagnostics)
            : TranslationProjectProfile.Current;
        return new(profile, Array.AsReadOnly(diagnostics.ToSortedArray()));
    }

    private static TranslationProjectProfile ReadProjectProfile(JsonValue root, TranslationSource project, DiagnosticBag diagnostics)
    {
        JsonProperty? property = root.Property("executionProfile");
        if (property is null) return TranslationProjectProfile.Current;
        if (property.Value.Kind != JsonKind.String ||
            !string.Equals(property.Value.Text, Rmf2ProjectV5.Profile, StringComparison.Ordinal))
        {
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error,
                "Unsupported executionProfile; expected 'rmf2-execution-v2', or omit it for the current v4 contract.",
                project, property.Value.Span);
            return TranslationProjectProfile.Current;
        }
        JsonProperty? layout = root.Property("sourceLayout");
        if (layout?.Value.Kind != JsonKind.String || !string.Equals(layout.Value.Text, "rmf2-v1", StringComparison.Ordinal))
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error,
                "rmf2-execution-v2 requires sourceLayout rmf2-v1.", project, property.Value.Span);
        return TranslationProjectProfile.Rmf2ExecutionV2;
    }

    internal static TranslationProfileCompilation CompileProjectForProfile(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationProjectProfile profile,
        TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
        => profile switch
        {
            TranslationProjectProfile.Current => new(profile, CompileProject(project, messages, options, cancellationToken), null),
            TranslationProjectProfile.Rmf2ExecutionV2 => new(profile, null, CompileRmf2ProjectV5(project, messages, options, cancellationToken)),
            _ => throw new ArgumentOutOfRangeException(nameof(profile)),
        };

    private sealed record Rmf2ProjectSourceV5(string Key, string[] Path, string Locale, TranslationSource Source, Rmf2ResourceNode Node);
    private sealed record Rmf2ProjectEntryV5(Rmf2ProjectSourceV5 Source, Rmf2LinkedMarkupV5 Linked)
    {
        internal Rmf2TranslationV5 Translation => new(Source.Key, Source.Locale, Linked.Message, Source.Node.NameLocation,
            Source.Node.Comments.Count == 0 ? null : string.Join("\n", Source.Node.Comments));
    }

    internal static Rmf2ProjectCompilationV5 CompileRmf2ProjectV5(TranslationSource project,
        IEnumerable<TranslationSource> messages, TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        TranslationSource[] sources = Materialize(messages);
        Rmf2ProjectCompilationV5 Result(Rmf2ProjectV5? value = null) => new(value, Array.AsReadOnly(diagnostics.ToSortedArray()));
        bool Failed() => diagnostics.Items.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
        if (RejectDuplicateSourcePaths(new[] { project }, sources, diagnostics)) return Result();
        ParsedJson parsed = StrictJsonParser.Parse(project, diagnostics, options, cancellationToken);
        ManifestModel? manifest = parsed.Root is null ? null : ReadMf2Project(parsed, diagnostics, options);
        if (manifest is null || Failed()) return Result();
        JsonValue config = parsed.Root!;
        _ = ReadProjectProfile(config, project, diagnostics);
        if (Failed()) return Result();
        if (config.Property("sourceLayout")?.Value.Text != "rmf2-v1")
        {
            diagnostics.Add("RTR0065", TranslationDiagnosticSeverity.Error, "rmf2-execution-v2 requires sourceLayout rmf2-v1.", project, config.Span);
            return Result();
        }
        var markup = new Rmf2ProjectMarkupV5(config, project, diagnostics);
        if (Failed()) return Result();
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var extracted = ReadRmf2ProjectSourcesV5(project, sources, config, discovered, diagnostics, options, cancellationToken);
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
        var canonical = new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal);
        var extras = new SortedDictionary<string, Rmf2ProjectEntryV5>(StringComparer.Ordinal);
        foreach (var source in extracted.Where(source => source.Locale == manifest.DefaultLocale))
            if (Compile(source, null) is { } entry) { canonical.Add(source.Key, entry); entries[source.Locale].Add(source.Key, entry); }
        foreach (var source in extracted.Where(source => source.Locale != manifest.DefaultLocale))
        {
            cancellationToken.ThrowIfCancellationRequested();
            canonical.TryGetValue(source.Key, out var origin);
            if (origin is null) AddPolicyDiagnostic("RTR0011", manifest.ExtraKeys, "Locale '" + source.Locale + "' defines non-canonical key '" + source.Key + "'.", source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            if (origin is null) extras.TryGetValue(source.Key, out origin);
            if (Compile(source, origin?.Linked.Message.Inputs) is { } entry)
            {
                entries[source.Locale].Add(source.Key, entry);
                if (!canonical.ContainsKey(source.Key)) extras.TryAdd(source.Key, entry);
            }
        }

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
                    names.UnionWith(translated.Linked.Names);
                }
                else if (locale.Tag != manifest.DefaultLocale)
                    AddPolicyDiagnostic("RTR0010", manifest.Completeness, "Locale '" + locale.Tag + "' lacks direct translation for key '" + pair.Key + "'.", project, locale.Span, diagnostics);
            }
            contracts.Add(new(contracts.Count, pair.Key, Array.AsReadOnly(pair.Value.Source.Path), pair.Value.Linked.Message.Inputs,
                requirements, names.Count != 0, names.ToArray()));
        }
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
                names.UnionWith(entry.Linked.Names);
            }
            extraContracts.Add(new(-1, pair.Key, Array.AsReadOnly(pair.Value.Source.Path), pair.Value.Linked.Message.Inputs,
                requirements, names.Count != 0, names.ToArray()));
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
            Rmf2ProjectMarkupV5.Export(usedContracts, contracts.Concat(extraContracts).ToArray(), linkedLocales), HashV5(Encoding.UTF8.GetBytes(callerContract)), SourceHashV5(project, sources)));

        Rmf2ProjectEntryV5? Compile(Rmf2ProjectSourceV5 source, IReadOnlyList<Rmf2InputV5>? callerInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var input = new TranslationSource(source.Source.Path, Encoding.UTF8.GetBytes(source.Node.Message!));
            var result = callerInputs is null ? Rmf2SemanticCompilerV5.Compile(input, options, cancellationToken) : Rmf2SemanticCompilerV5.CompileWithCallerContract(input, callerInputs, options, cancellationToken);
            foreach (var diagnostic in result.Diagnostics)
            {
                int from = source.Node.MessageByteMap[Math.Min(diagnostic.Location.StartByte, source.Node.MessageByteMap.Count - 1)];
                int to = source.Node.MessageByteMap[Math.Min(diagnostic.Location.StartByte + diagnostic.Location.LengthBytes, source.Node.MessageByteMap.Count - 1)];
                diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, source.Source, new(from, Math.Max(0, to - from)));
            }
            if (result.Message is null) return null;
            ValidateMetadataV5(source.Node, result.Message, diagnostics);
            if (result.Message.Variants.Any(variant => variant.Nodes.Count == 0))
                AddPolicyDiagnostic("RTR0021", manifest.EmptyValues, "Resource '" + source.Key + "' has an empty variant.", source.Source, new(source.Node.NameLocation.StartByte, source.Node.NameLocation.LengthBytes), diagnostics);
            return new(source, markup.Link(result.Message, source.Node.NameLocation, diagnostics));
        }
        void ValidateCallerContract(Rmf2ProjectEntryV5 origin, Rmf2ProjectEntryV5 translated)
        {
            foreach (var input in translated.Linked.Message.Inputs)
                if (!origin.Linked.Message.Inputs.Any(expected => expected.Name == input.Name && expected.Type == input.Type))
                    diagnostics.Add("RTR0016", TranslationDiagnosticSeverity.Error, "Translation introduces or changes caller input '" + input.Name + "' for '" + origin.Source.Key + "'.", translated.Source.Node.NameLocation);
        }
        void ValidateContentLocale(Rmf2TranslationV5 resource)
        {
            foreach (var selector in resource.Message.Selectors)
                if (selector.Function is "plural" or "ordinal" && !SupportsBuiltInPlural(resource.ContentLocale, selector.Function == "ordinal"))
                    diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in plural registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
            if (resource.Message.Declarations.Select(d => d.Expression).Concat(resource.Message.Variants.SelectMany(v => v.Nodes).OfType<Rmf2ExpressionNodeV5>().Select(n => n.Expression)).Any(expression => expression.Function == "runic:relative-time") && !SupportsRelativeTime(resource.ContentLocale))
                diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error, "The built-in relative-time registry does not support content locale '" + resource.ContentLocale + "'.", resource.SourceLocation);
        }
        void Error(string message, TranslationSource source, ByteSpan span) => diagnostics.Add("RTR0062", TranslationDiagnosticSeverity.Error, message, source, span);
    }

    private static List<Rmf2ProjectSourceV5> ReadRmf2ProjectSourcesV5(TranslationSource project, TranslationSource[] sources,
        JsonValue config, HashSet<string> locales, DiagnosticBag diagnostics, TranslationCompilerOptions options, CancellationToken cancellationToken)
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
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = mounts.Where(mount => source.Path.StartsWith(mount.Root, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !source.Path.EndsWith(".rmf2", StringComparison.Ordinal)) { Error("RMF2 sources must be {locale}.rmf2 beneath exactly one source root.", source, new(0, 0)); continue; }
            string currentPath = source.Path;
            while (currentPath.Length != 0)
            {
                if (portable.TryGetValue(currentPath, out string? previous) && previous != currentPath) Error("Case-only source or directory alias conflicts with '" + previous + "'.", source, new(0, 0));
                portable[currentPath] = currentPath;
                int slash = currentPath.LastIndexOf('/'); currentPath = slash < 0 ? "" : currentPath.Substring(0, slash);
            }
            string[] parts = source.Path.Substring(matches[0].Root.Length).Split('/');
            string spelling = parts[^1].Substring(0, parts[^1].Length - 5);
            if (!TryCanonicalizeLocale(spelling, out string locale) || parts.Take(parts.Length - 1).Any(segment => !IsIdentifier(segment))) { Error("Invalid RMF2 locale filename or directory segment.", source, new(0, 0)); continue; }
            if (spellings.TryGetValue(locale, out string? existing) && existing != spelling) Error("Duplicate canonical locale spelling.", source, new(0, 0));
            spellings[locale] = spelling; locales.Add(locale);
            string[] mount = matches[0].Prefix.Concat(parts.Take(parts.Length - 1)).ToArray();
            var resource = Rmf2ResourceReader.Read(source, options, cancellationToken);
            foreach (var diagnostic in resource.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
            for (int index = 1; index <= mount.Length; index++) Register(mount.Take(index).ToArray(), true, "", DiagnosticBag.Location(source, new(0, 0)));
            foreach (var node in resource.Nodes)
            {
                string[] path = mount.Concat(node.Path).ToArray();
                Register(path, node.IsGroup, string.Join("\n", node.Comments.Concat(node.Properties)), node.NameLocation);
                if (node.IsGroup) continue;
                string key = string.Join("_", path), logical = string.Join(".", path);
                if (generated.TryGetValue(key, out string? previous) && previous != logical) diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Logical path '" + logical + "' collides with '" + previous + "' under canonical underscore keys.", node.NameLocation);
                generated[key] = logical;
                result.Add(new(key, path, locale, source, node));
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
