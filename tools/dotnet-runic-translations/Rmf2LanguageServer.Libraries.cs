using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using JsonValue = System.Text.Json.Nodes.JsonValue;

namespace Runic.Translations.Tool;

// Library catalogs and the application's overrides of them (T5d, L5-17). A file under translations/overrides/<catalog>/ of a
// project whose runic.json declares "overrides" is an overlay on the packed library catalog, which Tooling finds in the
// restore output or node_modules as the build does: its diagnostics are the build's (RTR0103, input and markup checks,
// RTR0104 when the catalog is not installed), completion offers the library's keys and inputs, hover shows the library's
// base text, and definition opens the library source as a read-only runic-library: document.
internal sealed partial class Rmf2LanguageServer
{
    /// <summary>The URI scheme of the read-only library documents that definition opens (<c>runic/libraryDocument</c>).</summary>
    internal const string LibraryScheme = "runic-library";

    // The dependency resolution per project, reused while runic.json and the restore/npm inputs are unchanged.
    private readonly Dictionary<string, (string Stamp, TranslationDependencyResolution Resolution)> _dependencies = new(PathComparer);

    private sealed record OverrideContext(string Project, TranslationOverrideTarget Target, TranslationLibraryCatalog? Catalog, string Locale);

    // The project's resolution when its runic.json declares "overrides", else null.
    private TranslationDependencyResolution? Dependencies(string project)
    {
        string manifest = Path.Combine(project, "runic.json");
        byte[] bytes;
        if (TryGetBuffer(manifest, out _, out Buffer? buffer)) bytes = Utf8.GetBytes(buffer.Text);
        else
        {
            try { bytes = File.ReadAllBytes(manifest); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }
        if (TranslationManifestReader.Read(bytes, project).OverridesRoot is null) return null;
        string manifestStamp = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (_dependencies.TryGetValue(project, out var cached) && cached.Stamp == manifestStamp + InputsStamp(cached.Resolution.Inputs)) return cached.Resolution;
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest, bytes, _requestCancellation);
        _dependencies[project] = (manifestStamp + InputsStamp(resolution.Inputs), resolution);
        return resolution;
    }

    private static string InputsStamp(IReadOnlyList<string> inputs) =>
        string.Concat(inputs.Select(path => "|" + (FileStamp(path) ?? "-")));

    // The override sources of the project's buffers (absolute path → text), which replace the files.
    private Dictionary<string, byte[]> OverrideBuffers(TranslationDependencyResolution resolution, IEnumerable<string> group)
    {
        var buffers = new Dictionary<string, byte[]>(PathComparer);
        foreach (string uri in group)
        {
            string path = Path.GetFullPath(LocalPath(uri));
            if (resolution.OverrideOf(path) is not null && _buffers.TryGetValue(uri, out Buffer? buffer)) buffers[path] = Utf8.GetBytes(buffer.Text);
        }
        return buffers;
    }

    // The diagnostics of every override source of the project, as the build reports them.
    private List<TranslationDiagnostic> LibraryDiagnostics(string project, IReadOnlyCollection<string> group)
    {
        try
        {
            if (Dependencies(project) is not { } resolution) return new List<TranslationDiagnostic>();
            Dictionary<string, byte[]> buffers = OverrideBuffers(resolution, group);
            var diagnostics = new List<TranslationDiagnostic>();
            foreach (TranslationOverrideTarget target in resolution.Overrides)
            {
                _requestCancellation.ThrowIfCancellationRequested();
                IReadOnlyList<TranslationSource> sources = TranslationDependencyCatalogs.ReadOverrides(target, buffers, _requestCancellation);
                if (sources.Count == 0) continue;
                if (target.Problem is not null)
                {
                    // Not installed (restore may fix it) and unlisted directories are warnings; a wrong "from" fails the build.
                    var severity = target.Listed && target.Dependency is null && resolution.Catalogs.Any(item => item.Catalog == target.Catalog)
                        ? TranslationDiagnosticSeverity.Error : TranslationDiagnosticSeverity.Warning;
                    foreach (TranslationSource source in sources)
                        diagnostics.Add(new TranslationDiagnostic("RTR0104", severity, target.Problem, new TextSourceLocation(source.Path, 0, 0, 1, 1, 1, 1)));
                    if (target.Dependency is null || !target.Listed) continue;
                }
                TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target, buffers, _requestCancellation);
                if (catalog.Library.Project is null)
                    foreach (TranslationSource source in sources)
                        diagnostics.Add(new TranslationDiagnostic("RTR0104", TranslationDiagnosticSeverity.Error,
                            $"The packed catalog '{target.Catalog}' of {target.Dependency!.Display} does not compile with this tool: {catalog.LibraryError?.Id} {catalog.LibraryError?.Message}",
                            new TextSourceLocation(source.Path, 0, 0, 1, 1, 1, 1)));
                diagnostics.AddRange(catalog.Diagnostics);
            }
            return diagnostics;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or TranslationInterchangeException)
        {
            Console.Error.WriteLine($"runic-rmf2: library catalogs of {project} cannot be read: {error.Message}");
            return new List<TranslationDiagnostic>();
        }
    }

    // The library catalog an open override source belongs to, or null when the buffer is not an override source.
    private OverrideContext? OverrideContextOf(string uri)
    {
        string? project = BufferProject(uri, out _);
        if (project is null || Dependencies(project) is not { } resolution) return null;
        string path = Path.GetFullPath(LocalPath(uri));
        if (resolution.OverrideOf(path) is not { } target) return null;
        string file = Path.GetFileName(path);
        string locale = file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) && TranslationCompiler.TryCanonicalizeLocale(file[..^5], out string tag) ? tag : Path.GetFileNameWithoutExtension(file);
        TranslationLibraryCatalog? catalog = null;
        if (target.Dependency is not null)
        {
            try { catalog = TranslationLibraryCatalog.Load(target, OverrideBuffers(resolution, [uri]), _requestCancellation); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return new OverrideContext(project, target, catalog, locale);
    }

    private static JsonObject? LibraryHover(OverrideContext context, Rmf2ResourceNode? entry)
    {
        if (entry is null) return null;
        string logical = string.Join(".", entry.Path);
        TranslationLibraryCatalog? catalog = context.Catalog;
        if (catalog is null)
            return HoverResult(new Rmf2HoverContent { Summary = $"{logical}: override of library catalog {context.Target.Catalog}", Contract = [context.Target.Problem ?? "The library catalog is not installed."],
                Comments = entry.Comments, Properties = entry.Properties }.Render());
        string library = $"library catalog {catalog.Catalog} ({catalog.Dependency.Display})";
        if (entry.IsGroup)
            return HoverResult($"{logical}: group of {library}, {catalog.Messages.Count(message => message.LogicalPath.Take(entry.Path.Count).SequenceEqual(entry.Path, StringComparer.Ordinal) && message.LogicalPath.Count > entry.Path.Count)} message(s)");
        TranslationLibraryMessage? message = catalog.Message(entry.Key);
        if (message is null)
            return HoverResult(new Rmf2HoverContent { Summary = $"{logical}: {catalog.Catalog} has no such message, so this override fails the build (RTR0103).",
                Comments = entry.Comments, Properties = entry.Properties }.Render());
        var lines = new List<string>();
        string? own = message.Texts.Keys.FirstOrDefault(tag => !string.Equals(tag, catalog.BaseLocale, StringComparison.OrdinalIgnoreCase) && string.Equals(tag, context.Locale, StringComparison.OrdinalIgnoreCase));
        if (own is not null) lines.Add($"Library text ({own}): {message.Texts[own].Replace("\n", "\n  ", StringComparison.Ordinal)}");
        TranslationLibraryLocale? locale = catalog.Locales.FirstOrDefault(item => string.Equals(item.Tag, context.Locale, StringComparison.OrdinalIgnoreCase));
        if (locale is { Origin: TranslationLibraryLocaleOrigin.Application } || (locale is null && !catalog.Locales.Any(item => string.Equals(item.Tag, context.Locale, StringComparison.OrdinalIgnoreCase))))
            lines.Add($"Locale: {context.Locale}, added by the application; falls back to {catalog.FallbackOf(context.Locale)}");
        if (message.Inputs.Count != 0) lines.Add("Inputs: " + string.Join(", ", message.Inputs));
        lines.Add("Content: " + message.Content + (message.Structured ? "" : ", plain text (no markup)"));
        if (catalog.Override(entry.Key, context.Locale) is { Changed: true })
            lines.Add("Override: the library's base text changed since this override was written (RTR0090).");
        string[] baseComments = message.Description is null ? [] : message.Description.Split('\n');
        return HoverResult(new Rmf2HoverContent
        {
            Summary = $"{logical}: override of {library}", BaseLocale = catalog.BaseLocale, BaseText = message.BaseText, BaseComments = baseComments,
            Contract = lines, Comments = entry.Comments, Properties = entry.Properties,
        }.Render());

        static JsonObject HoverResult(string value) => new() { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = value } };
    }

    // Keys of the library below the group at the position (a message's name completes among its siblings), and inside a
    // message the library's inputs for it.
    private static JsonArray LibraryCompletion(OverrideContext context, Rmf2ResourceNode? entry, int atByte)
    {
        if (context.Catalog is not { } catalog) return new JsonArray();
        bool inMessage = entry is { IsGroup: false } && atByte > entry.NameLocation.StartByte + entry.NameLocation.LengthBytes;
        if (inMessage)
        {
            TranslationLibraryMessage? message = catalog.Message(entry!.Key);
            var items = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (string input in message?.Inputs ?? []) items[input.Split(':')[0]] = "Library input " + input;
            foreach (string label in new[] { "one", "two", "few", "many", "zero", "*" }) items.TryAdd(label, "Runic semantic translation contract");
            return new JsonArray(items.Select(item => (JsonNode)new JsonObject { ["label"] = item.Key, ["detail"] = item.Value, ["kind"] = 6 }).ToArray());
        }
        IReadOnlyList<string> parent = entry is null ? [] : entry.IsGroup && atByte > entry.NameLocation.StartByte + entry.NameLocation.LengthBytes ? entry.Path : entry.Path.Take(entry.Path.Count - 1).ToArray();
        var result = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (TranslationLibraryMessage message in catalog.Messages)
        {
            if (message.LogicalPath.Count <= parent.Count || !message.LogicalPath.Take(parent.Count).SequenceEqual(parent, StringComparer.Ordinal)) continue;
            string segment = message.LogicalPath[parent.Count];
            bool group = message.LogicalPath.Count > parent.Count + 1;
            bool overridden = catalog.Override(message.Key, context.Locale) is not null;
            if (group)
            {
                result.TryAdd(segment, new JsonObject { ["label"] = segment, ["kind"] = 9, ["detail"] = $"Group of {catalog.Catalog}" });
                continue;
            }
            result[segment] = new JsonObject
            {
                ["label"] = segment, ["kind"] = 10, ["detail"] = (overridden ? "Overridden; library: " : "Library: ") + FirstLine(message.BaseText),
                ["documentation"] = new JsonObject { ["kind"] = "plaintext", ["value"] = $"Base text ({catalog.BaseLocale}):\n{message.BaseText}" +
                    (message.Inputs.Count == 0 ? "" : "\nInputs: " + string.Join(", ", message.Inputs)) },
                ["insertText"] = segment + " = " + (message.BaseText.Contains('\n', StringComparison.Ordinal) ? "" : catalog.SeedText(message.Key, context.Locale)),
            };
        }
        return new JsonArray(result.Values.Select(item => (JsonNode)item).ToArray());

        static string FirstLine(string text) => text.Split('\n')[0];
    }

    // The library's definition of the message: its own text in the locale, then the base text, as read-only documents.
    private JsonArray LibraryDefinition(OverrideContext context, Rmf2ResourceNode? entry)
    {
        if (entry is null || context.Catalog is not { } catalog || catalog.Message(entry.Key) is not { } message) return new JsonArray();
        var locations = new JsonArray();
        foreach (var location in message.Locations.OrderBy(item => string.Equals(item.Key, context.Locale, StringComparison.OrdinalIgnoreCase) ? 0 : item.Key == catalog.BaseLocale ? 1 : 2)
            .Where(item => string.Equals(item.Key, context.Locale, StringComparison.OrdinalIgnoreCase) || item.Key == catalog.BaseLocale))
        {
            string path = Path.GetFullPath(location.Value.Path);
            if (!TryReadProjectFile(path, out string text, out TranslationSource? encoded) || encoded is null) continue;
            locations.Add((JsonNode)new JsonObject { ["uri"] = LibraryUri(context.Project, catalog.Dependency, path), ["range"] = Range(text, encoded, location.Value) });
        }
        return locations;
    }

    // runic-library:/<catalog>/<path in the packed catalog>?project=<file URI of the application's runic.json>
    private static string LibraryUri(string project, TranslationDependencyCatalog dependency, string path)
    {
        string relative = Path.GetRelativePath(dependency.Root, path).Replace('\\', '/');
        return LibraryScheme + ":/" + Uri.EscapeDataString(dependency.Catalog) + "/" + string.Join("/", relative.Split('/').Select(Uri.EscapeDataString)) +
            "?project=" + Uri.EscapeDataString(new Uri(Path.Combine(project, "runic.json")).AbsoluteUri);
    }

    // runic/libraryDocument { uri }: the text of a library source behind a runic-library: URI. Only sources of a catalog that
    // an installed package of that project ships can be read.
    private JsonObject LibraryDocument(JsonObject args)
    {
        var uri = new Uri(args["uri"]?.GetValue<string>() ?? throw new ArgumentException("Missing library document URI."));
        if (uri.Scheme != LibraryScheme) throw new ArgumentException("Not a library document URI.");
        string query = uri.Query.TrimStart('?');
        string? project = query.Split('&').Select(part => part.Split('=', 2)).Where(part => part.Length == 2 && part[0] == "project").Select(part => Uri.UnescapeDataString(part[1])).FirstOrDefault();
        if (project is null) throw new ArgumentException("The library document URI names no project.");
        string manifest = LocalPath(project);
        string[] segments = uri.AbsolutePath.TrimStart('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length < 2 || !File.Exists(manifest)) throw new ArgumentException("Unknown library document.");
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest, null, _requestCancellation);
        TranslationDependencyCatalog catalog = resolution.Catalogs.FirstOrDefault(item => item.Catalog == segments[0])
            ?? throw new ArgumentException($"No installed package ships catalog '{segments[0]}'.");
        string path = Path.GetFullPath(Path.Combine([catalog.Root, .. segments[1..]]));
        if (!TranslationDependencyCatalogs.IsWithin(catalog.Root, path) || !path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("Unknown library document.");
        return new JsonObject { ["uri"] = uri.OriginalString, ["text"] = File.ReadAllText(path, Utf8), ["catalog"] = catalog.Catalog, ["package"] = catalog.Display };
    }

    // runic/libraryCatalogs { uri }: the installed library catalogs of the project of a file (or of a runic.json), whether
    // runic.json overrides each, and for the overridden ones their locales, messages and overrides.
    private JsonObject LibraryCatalogs(JsonObject args)
    {
        string path = LocalPath(args["uri"]?.GetValue<string>() ?? throw new ArgumentException("Missing document URI."));
        string project = Path.GetFileName(path).Equals("runic.json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(Path.GetFullPath(path))! : ProjectDirectory(path, allowExternalAncestor: true);
        string manifest = Path.Combine(project, "runic.json");
        byte[]? bytes = TryGetBuffer(manifest, out _, out Buffer? buffer) ? Utf8.GetBytes(buffer.Text) : null;
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest, bytes, _requestCancellation);
        var catalogs = new JsonArray();
        foreach (string id in resolution.Catalogs.Select(item => item.Catalog).Concat(resolution.Overrides.Select(item => item.Catalog)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            TranslationOverrideTarget? target = resolution.TargetFor(id);
            TranslationDependencyCatalog? dependency = target?.Dependency ?? resolution.Catalogs.FirstOrDefault(item => item.Catalog == id);
            var item = new JsonObject
            {
                ["catalog"] = id, ["package"] = dependency?.Package, ["version"] = dependency?.Version,
                ["origin"] = dependency is null ? null : dependency.Origin == TranslationDependencyOrigin.NuGet ? "nuget" : "npm",
                ["overridden"] = target?.Listed == true && resolution.Override(id) is not null, ["problem"] = resolution.Override(id)?.Problem,
                ["directory"] = target is null ? null : new Uri(target.Directory + Path.DirectorySeparatorChar).AbsoluteUri,
            };
            if (target?.Dependency is not null)
            {
                TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target, OverrideBuffers(resolution, BufferUris(project)), _requestCancellation);
                item["baseLocale"] = catalog.BaseLocale;
                item["locales"] = new JsonArray(catalog.Locales.Select(locale => (JsonNode)new JsonObject
                {
                    ["tag"] = locale.Tag, ["origin"] = locale.Origin == TranslationLibraryLocaleOrigin.Library ? "library" : "application", ["fallback"] = locale.Fallback,
                    ["messages"] = locale.Messages, ["libraryTranslated"] = locale.LibraryTranslated, ["overridden"] = locale.Overridden, ["covered"] = locale.Covered, ["pending"] = locale.Pending,
                }).ToArray());
                item["messages"] = new JsonArray(catalog.Messages.Select(message => (JsonNode)new JsonObject
                {
                    ["key"] = message.Key, ["path"] = string.Join(".", message.LogicalPath), ["baseText"] = message.BaseText,
                    ["inputs"] = new JsonArray(message.Inputs.Select(input => (JsonNode?)JsonValue.Create(input)).ToArray()),
                    ["overridden"] = new JsonArray(catalog.Overrides.Where(entry => entry.Key == message.Key).Select(entry => (JsonNode?)JsonValue.Create(entry.Locale)).ToArray()),
                    ["changed"] = new JsonArray(catalog.Overrides.Where(entry => entry.Key == message.Key && entry.Changed == true).Select(entry => (JsonNode?)JsonValue.Create(entry.Locale)).ToArray()),
                }).ToArray());
            }
            catalogs.Add((JsonNode)item);
        }
        return new JsonObject
        {
            ["project"] = new Uri(manifest).AbsoluteUri, ["catalogs"] = catalogs,
            ["warnings"] = new JsonArray(resolution.Warnings.Select(warning => (JsonNode?)JsonValue.Create(warning)).ToArray()),
        };
    }
}
