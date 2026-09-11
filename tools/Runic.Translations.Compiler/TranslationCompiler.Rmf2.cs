using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace Runic.Translations.Compiler;

public static partial class TranslationCompiler
{
    private static readonly string[] Rmf2MountMembers = { "path", "namespace" };

    private static void ReadRmf2Documents(string directory, TranslationSource[] sources, JsonValue config, ManifestModel manifest,
        List<DocumentModel> documents, HashSet<string> locales, DiagnosticBag diagnostics,
        TranslationCompilerOptions options, CancellationToken cancellationToken)
    {
        var registry = Rmf2MarkupRegistry.Read(config.Property("markup"), manifest.Source, diagnostics);
        var mounts = new List<(string Root, string[] Prefix)>();
        JsonProperty? roots = config.Property("sourceRoots");
        if (roots is null) mounts.Add((directory, Array.Empty<string>()));
        else if (roots.Value.Kind != JsonKind.Array || roots.Value.Items.Count == 0)
            diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "sourceRoots requires a nonempty array of {path, namespace} mounts.", manifest.Source, roots.Value.Span);
        else foreach (JsonValue item in roots.Value.Items)
        {
            if (item.Kind != JsonKind.Object) { diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Invalid source mount.", manifest.Source, item.Span); continue; }
            ValidateKnownMembers(item, Rmf2MountMembers, manifest.Source, diagnostics);
            JsonProperty? path = Required(item, "path", JsonKind.String, manifest.Source, diagnostics);
            JsonProperty? prefix = Required(item, "namespace", JsonKind.Array, manifest.Source, diagnostics);
            if (path is null || prefix is null) continue;
            string root = NormalizeResourceRoot(directory, path.Value.Text!);
            string[] segments = prefix.Value.Items.Select(v => v.Kind == JsonKind.String ? v.Text! : "").ToArray();
            if (segments.Any(s => !IsIdentifier(s))) diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Mount namespaces require identifier segments.", manifest.Source, prefix.Value.Span);
            if (mounts.Any(m => root.StartsWith(m.Root, StringComparison.OrdinalIgnoreCase) || m.Root.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Overlapping source roots are not allowed.", manifest.Source, path.Value.Span);
            mounts.Add((root, segments));
        }
        var identities = new Dictionary<string, (bool Group, TranslationSource Source, TextSourceLocation Location, string Metadata)>(StringComparer.Ordinal);
        var portablePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var localeSpellings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var generated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (TranslationSource source in sources)
        {
            var matches = mounts.Where(m => source.Path.StartsWith(m.Root, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1 || !source.Path.EndsWith(".rmf2", StringComparison.Ordinal))
            { diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "RMF2 sources must be {locale}.rmf2 beneath exactly one configured source root.", source, new ByteSpan(0, 0)); continue; }
            if (portablePaths.TryGetValue(source.Path, out string? previous) && previous != source.Path)
                diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Case-only source alias conflicts with '" + previous + "'.", source, new ByteSpan(0, 0));
            portablePaths[source.Path] = source.Path;
            string parent = source.Path;
            while (parent.LastIndexOf('/') > 0)
            {
                parent = parent.Substring(0, parent.LastIndexOf('/'));
                if (portablePaths.TryGetValue(parent, out string? priorDirectory) && priorDirectory != parent)
                    diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Case-only directory alias conflicts with '" + priorDirectory + "'.", source, new ByteSpan(0, 0));
                portablePaths[parent] = parent;
            }
            string relative = source.Path.Substring(matches[0].Root.Length);
            string[] parts = relative.Split('/');
            string localeText = parts[^1].Substring(0, parts[^1].Length - 5);
            if (!TryCanonicalizeLocale(localeText, out string locale) || parts.Take(parts.Length - 1).Any(s => !IsIdentifier(s)))
            { diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Invalid RMF2 locale filename or directory segment.", source, new ByteSpan(0, 0)); continue; }
            if (localeSpellings.TryGetValue(locale, out string? spelling) && spelling != localeText)
                diagnostics.Add("RTR0052", TranslationDiagnosticSeverity.Error, "Duplicate canonical locale spelling; use '" + spelling + "' consistently.", source, new ByteSpan(0, 0));
            localeSpellings[locale] = localeText;
            locales.Add(locale);
            string[] mount = matches[0].Prefix.Concat(parts.Take(parts.Length - 1)).ToArray();
            Rmf2ResourceDocument resource = Rmf2ResourceReader.Read(source, options, cancellationToken);
            foreach (TranslationDiagnostic diagnostic in resource.Diagnostics) diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, diagnostic.Location);
            var document = new DocumentModel(source) { SchemaVersion = 2, Catalog = manifest.Id, Locale = locale, Layer = "base" };
            // Implicit directory groups participate in leaf/group collision detection.
            for (int i = 1; i <= mount.Length; i++) Register(mount.Take(i).ToArray(), true, new TextSourceLocation(source.Path, 0, 0, 1, 1, 1, 1), "");
            foreach (Rmf2ResourceNode node in resource.Nodes)
            {
                string[] path = mount.Concat(node.Path).ToArray();
                Register(path, node.IsGroup, node.NameLocation, string.Join("\n", node.Comments.Concat(node.Properties)));
                if (node.IsGroup) continue;
                string key = string.Join("_", path), logical = string.Join(".", path);
                if (generated.TryGetValue(key, out string? existing) && existing != logical)
                    diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Logical path '" + logical + "' collides with '" + existing + "' under underscore generation.", node.NameLocation);
                generated[key] = logical;
                if (mount.Length > 0 && node.Path.Count > 1 && mount[^1] == node.Path[0])
                    diagnostics.Add("RTR0053", TranslationDiagnosticSeverity.Warning, "The enclosing directory prefix is repeated in this group; extraction normally removes it.", node.NameLocation);
                var messageSource = new TranslationSource(source.Path, Encoding.UTF8.GetBytes(node.Message!));
                var messageDiagnostics = new DiagnosticBag();
                Mf2ParsedMessage? message = Mf2MessageParser.Parse(messageSource, messageDiagnostics, options, cancellationToken, rmf2: true);
                foreach (TranslationDiagnostic diagnostic in messageDiagnostics.Items)
                {
                    int from = node.MessageByteMap[Math.Min(diagnostic.Location.StartByte, node.MessageByteMap.Count - 1)];
                    int to = node.MessageByteMap[Math.Min(diagnostic.Location.StartByte + diagnostic.Location.LengthBytes, node.MessageByteMap.Count - 1)];
                    diagnostics.Add(diagnostic.Id, diagnostic.Severity, diagnostic.Message, source, new ByteSpan(from, Math.Max(0, to - from)));
                }
                if (message is null) continue;
                message.Message.Rmf2 = true;
                message.Message.ContentLocale = locale;
                ValidateRmf2Metadata(node, message, diagnostics);
                IReadOnlyDictionary<string, string> slots = registry.Validate(message, node, diagnostics);
                var span = new ByteSpan(node.NameLocation.StartByte, node.NameLocation.LengthBytes);
                document.Resources.Add(new ResourceModel(key, message.Pattern, message.Message,
                    node.Comments.Count == 0 ? null : string.Join("\n", node.Comments), null, null, Array.Empty<string>(), message.Placeholders,
                    source, span, span, new ByteSpan(node.MessageByteMap[0], node.MessageByteMap[^1] - node.MessageByteMap[0]))
                    { KeyLocation = node.NameLocation, Rmf2 = true, Slots = slots });
            }
            documents.Add(document);
            void Register(string[] path, bool group, TextSourceLocation location, string metadata)
            {
                string identity = locale + ":" + string.Join(".", path);
                if (identities.TryGetValue(identity, out var first))
                {
                    if (!group || !first.Group || (metadata.Length != 0 && first.Metadata.Length != 0))
                    {
                        string error = "Duplicate message, message/group collision, or repeated group metadata for '" + identity + "'.";
                        diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, error + " First declaration: " + first.Location, location);
                        diagnostics.Add("RTR0054", TranslationDiagnosticSeverity.Error, error + " Conflicting declaration: " + location, first.Location);
                    }
                    else if (metadata.Length != 0) identities[identity] = (group, source, location, metadata);
                }
                else identities.Add(identity, (group, source, location, metadata));
            }
        }
    }

    private static string NormalizeResourceRoot(string directory, string path)
    {
        string combined = path.StartsWith('/') ? path : directory + path;
        var segments = new List<string>();
        foreach (string part in combined.Replace('\\', '/').Split('/'))
        { if (part == "." || part.Length == 0) continue; if (part == ".." && segments.Count > 0 && segments[^1] != "..") segments.RemoveAt(segments.Count - 1); else segments.Add(part); }
        return (combined.StartsWith('/') ? "/" : "") + string.Join("/", segments) + (segments.Count == 0 ? "" : "/");
    }

    private static void ValidateRmf2Metadata(Rmf2ResourceNode node, Mf2ParsedMessage message, DiagnosticBag diagnostics)
    {
        foreach (string property in node.Properties)
        {
            int space = property.IndexOf(' '); string name = space < 0 ? property : property.Substring(0, space);
            string value = space < 0 ? "" : property.Substring(space + 1).Trim();
            if (name == "param")
            {
                string parameter = value.Split(' ', 2)[0].TrimStart('$');
                if (!value.StartsWith('$') || !message.Placeholders.Any(p => p.Name == parameter))
                    diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Error, "@param must document an existing caller input.", node.NameLocation);
            }
            else if (name == "example")
            {
                try
                {
                    using JsonDocument example = JsonDocument.Parse(value);
                    if (example.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
                    foreach (PlaceholderModel input in message.Placeholders)
                    {
                        if (!example.RootElement.TryGetProperty(input.Name, out JsonElement item) || !ExampleType(input.Type, item)) throw new JsonException();
                    }
                    foreach (System.Text.Json.JsonProperty item in example.RootElement.EnumerateObject())
                        if (!message.Placeholders.Any(p => p.Name == item.Name)) throw new JsonException();
                }
                catch (JsonException) { diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Error, "@example must be a JSON object matching the message caller inputs.", node.NameLocation); }
            }
        }
    }
    private static bool ExampleType(TranslationArgumentType type, JsonElement value) => type switch
    {
        TranslationArgumentType.Int => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
        TranslationArgumentType.Number => value.ValueKind == JsonValueKind.Number,
        TranslationArgumentType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        _ => value.ValueKind == JsonValueKind.String,
    };
}

internal static class Rmf2ContractValidation
{
    internal static string ValidateAndExport(CompiledTextCatalog catalog, JsonValue config, TranslationSource project, DiagnosticBag diagnostics)
    {
        Rmf2MarkupRegistry registry = Rmf2MarkupRegistry.Read(config.Property("markup"), project, diagnostics);
        if (registry.SlotConstraints is { } constraints)
        {
            if (constraints.Kind != JsonKind.Object) Error("markup.slots must be an object.", new TextSourceLocation(project.Path, 0, 0, 1, 1, 1, 1));
            else foreach (JsonProperty item in constraints.Properties)
            {
                var resource = catalog.CanonicalResources.FirstOrDefault(r => r.Key == item.Name);
                if (resource is null || item.Value.Kind != JsonKind.Object) { Error("Unknown message or invalid slot constraints '" + item.Name + "'.", new TextSourceLocation(project.Path, 0, 0, 1, 1, 1, 1)); continue; }
                foreach (JsonProperty slot in item.Value.Properties)
                    if (!resource.Slots.ContainsKey(slot.Name) || slot.Value.Kind != JsonKind.Object || slot.Value.Properties.Any(p => p.Name is not ("min" or "max")))
                        Error("Unknown slot or unsupported constraint '" + slot.Name + "'.", resource.SourceLocation);
            }
        }
        var messages = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (CompiledTranslation source in catalog.CanonicalResources)
        {
            var requirements = new SortedDictionary<string, object>(StringComparer.Ordinal);
            var sourceVariants = Counts(source.Message);
            foreach (var slot in source.Slots)
            {
                JsonValue? settings = registry.SlotConstraints?.Property(source.Key)?.Value.Property(slot.Key)?.Value;
                int min = 1, max = 1;
                if (settings is null && sourceVariants.Any(v => !v.ContainsKey(slot.Key)))
                    Error("Conditional source slot '" + slot.Key + "' requires explicit markup.slots." + source.Key + " constraints.", source.SourceLocation);
                if (settings is not null)
                {
                    if (settings.Kind != JsonKind.Object || !int.TryParse(settings.Property("min")?.Value.Text, out min) ||
                        !int.TryParse(settings.Property("max")?.Value.Text, out max) || min < 0 || max < min || max > 4096)
                    { Error("Slot constraints require integer min/max with 0 <= min <= max <= 4096.", source.SourceLocation); min = max = 1; }
                }
                requirements[slot.Key] = new { kind = slot.Value, min, max };
                foreach (CompiledTextLocale locale in catalog.Locales)
                {
                    CompiledTranslation? translated = locale.ResolvedResources.FirstOrDefault(r => r.Key == source.Key);
                    if (translated is null) continue;
                    foreach (var variant in Counts(translated.Message))
                    {
                        int count = variant.TryGetValue(slot.Key, out int found) ? found : 0;
                        if (count < min || count > max) Error("Slot '" + slot.Key + "' must occur " + min + ".." + max + " times in every variant of '" + source.Key + "' (" + locale.Tag + ").", translated.SourceLocation);
                    }
                }
            }
            var contentLocales = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var locale in catalog.Locales)
            {
                var effective = locale.ResolvedResources.FirstOrDefault(r => r.Key == source.Key);
                if (effective is not null) contentLocales[locale.Tag] = effective.Message.ContentLocale ?? locale.Tag;
            }
            messages[source.Key] = new { slots = requirements, structured = source.ProducesStructuredContent, contentLocales };
        }
        var contracts = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var entry in registry.Contracts)
        {
            var options = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var option in entry.Value.Options)
                options[option.Key] = new { type = option.Value.Type, values = option.Value.Values, @default = option.Value.Default, literalOnly = option.Value.LiteralOnly };
            contracts[entry.Key] = new { kind = entry.Value.Standalone ? "standalone" : "paired", interactive = entry.Value.Interactive, plainText = entry.Value.PlainText, options };
        }
        return JsonSerializer.Serialize(new { version = 1, contracts, messages });
        void Error(string message, TextSourceLocation location) => diagnostics.Add("RTR0062", TranslationDiagnosticSeverity.Error, message, location);
    }
    private static List<Dictionary<string, int>> Counts(CompiledMessagePattern message)
    {
        var result = new List<Dictionary<string, int>>();
        foreach (var nodes in message.IsVariant ? message.Variants.Select(v => v.Pattern.Nodes) : new[] { message.Nodes })
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal); Visit(nodes); result.Add(counts);
            void Visit(IReadOnlyList<CompiledMessageNode> values)
            {
                foreach (CompiledMessageMarkup node in values.OfType<CompiledMessageMarkup>())
                {
                    if (node.Name is "runic:link" or "runic:action" or "runic:icon" && node.Attributes.TryGetValue("ref", out string? slot))
                        counts[slot] = counts.TryGetValue(slot, out int count) ? count + 1 : 1;
                    Visit(node.Children);
                }
            }
        }
        return result;
    }
}
