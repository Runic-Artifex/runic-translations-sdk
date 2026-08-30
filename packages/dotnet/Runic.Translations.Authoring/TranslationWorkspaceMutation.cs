using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

public static class TranslationWorkspaceMutation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static TranslationWorkspaceTransactionPlan AddLocale(TranslationAddLocaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceModel workspace = Load(request.Root, request.CatalogId);
        string locale = TranslationProjectScaffolder.CanonicalizeLocale(Required(request.Locale, "locale"));
        string copyFrom = TranslationProjectScaffolder.CanonicalizeLocale(Required(request.CopyFromLocale, "copy-from locale"));
        string? fallback = request.Fallback is null
            ? workspace.DefaultLocale
            : TranslationProjectScaffolder.CanonicalizeLocale(Required(request.Fallback, "fallback"));
        string layer = Required(request.Layer, "layer");
        if (workspace.LocaleObjects.ContainsKey(locale)) throw Error($"Locale '{locale}' is already declared.");
        if (!workspace.LocaleObjects.ContainsKey(copyFrom)) throw Error($"Copy-from locale '{copyFrom}' is not declared.");
        if (fallback is not null && !workspace.LocaleObjects.ContainsKey(fallback)) throw Error($"Fallback locale '{fallback}' is not declared.");
        if (string.Equals(locale, fallback, StringComparison.Ordinal)) throw Error($"Locale '{locale}' cannot fall back to itself.");
        if (!workspace.Layers.Contains(layer)) throw Error($"Layer '{layer}' is not declared.");

        var localeObject = new JsonObject { ["tag"] = locale };
        if (fallback is not null) localeObject["fallback"] = fallback;
        workspace.Locales.Add((JsonNode?)localeObject);

        ResourceFile source = workspace.Resources.SingleOrDefault(file =>
            string.Equals(file.Locale, copyFrom, StringComparison.Ordinal) &&
            string.Equals(file.Layer, layer, StringComparison.Ordinal))
            ?? throw Error($"Locale '{copyFrom}' has no document in layer '{layer}'.");
        string newPath = NewLocalePath(workspace, locale, layer);
        if (workspace.Discovery.Files.Any(file => string.Equals(file.RelativePath, newPath, PathComparison)))
            throw Error($"Cannot create '{newPath}' because that path already exists.");
        var document = new JsonObject
        {
            ["schemaVersion"] = workspace.SchemaVersion,
            ["catalog"] = workspace.CatalogId,
            ["locale"] = locale,
            ["layer"] = layer,
            ["resources"] = source.Resources.DeepClone(),
        };
        workspace.AddedResources.Add(new ResourceFile(newPath, document, locale, layer, null));
        workspace.ManifestChanged = true;
        return Plan(workspace);
    }

    public static TranslationWorkspaceTransactionPlan RemoveLocale(TranslationRemoveLocaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceModel workspace = Load(request.Root, request.CatalogId);
        string locale = TranslationProjectScaffolder.CanonicalizeLocale(Required(request.Locale, "locale"));
        if (string.Equals(locale, workspace.DefaultLocale, StringComparison.Ordinal))
            throw Error("The default locale cannot be removed. Change the default locale in the manifest first.");
        if (!workspace.LocaleObjects.Remove(locale, out JsonObject? removed)) throw Error($"Locale '{locale}' is not declared.");
        workspace.Locales.Remove(removed);

        string? replacement = request.ReplacementFallback is null
            ? workspace.DefaultLocale
            : TranslationProjectScaffolder.CanonicalizeLocale(Required(request.ReplacementFallback, "replacement fallback"));
        if (replacement is not null && string.Equals(replacement, locale, StringComparison.Ordinal))
            throw Error("The replacement fallback cannot be the locale being removed.");
        if (replacement is not null && !workspace.LocaleObjects.ContainsKey(replacement))
            throw Error($"Replacement fallback locale '{replacement}' is not declared.");
        foreach (JsonObject candidate in workspace.LocaleObjects.Values)
        {
            if (!string.Equals(candidate["fallback"]?.GetValue<string>(), locale, StringComparison.Ordinal)) continue;
            if (replacement is null) candidate.Remove("fallback");
            else candidate["fallback"] = replacement;
        }
        foreach (ResourceFile file in workspace.Resources.Where(file => string.Equals(file.Locale, locale, StringComparison.Ordinal)))
            file.Delete = true;
        workspace.ManifestChanged = true;
        return Plan(workspace);
    }

    public static TranslationWorkspaceTransactionPlan SetFallback(TranslationSetFallbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceModel workspace = Load(request.Root, request.CatalogId);
        string locale = TranslationProjectScaffolder.CanonicalizeLocale(Required(request.Locale, "locale"));
        if (!workspace.LocaleObjects.TryGetValue(locale, out JsonObject? localeObject)) throw Error($"Locale '{locale}' is not declared.");
        if (string.Equals(locale, workspace.DefaultLocale, StringComparison.Ordinal) && request.Fallback is not null)
            throw Error("The default locale cannot declare a fallback.");
        if (request.Fallback is null)
        {
            localeObject.Remove("fallback");
        }
        else
        {
            string fallback = TranslationProjectScaffolder.CanonicalizeLocale(Required(request.Fallback, "fallback"));
            if (string.Equals(locale, fallback, StringComparison.Ordinal)) throw Error($"Locale '{locale}' cannot fall back to itself.");
            if (!workspace.LocaleObjects.ContainsKey(fallback)) throw Error($"Fallback locale '{fallback}' is not declared.");
            localeObject["fallback"] = fallback;
        }
        workspace.ManifestChanged = true;
        return Plan(workspace);
    }

    public static TranslationWorkspaceTransactionPlan CreateKey(TranslationCreateKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceModel workspace = Load(request.Root, request.CatalogId);
        string[] key = KeySegments(request.Key);
        string layer = Required(request.Layer, "layer");
        if (!workspace.Layers.Contains(layer)) throw Error($"Layer '{layer}' is not declared.");
        foreach (string locale in workspace.LocaleObjects.Keys)
        {
            ResourceFile? file = workspace.Resources.SingleOrDefault(candidate =>
                string.Equals(candidate.Locale, locale, StringComparison.Ordinal) &&
                string.Equals(candidate.Layer, layer, StringComparison.Ordinal));
            if (file is null) throw Error($"Locale '{locale}' has no document in layer '{layer}'.");
            if (!TrySet(file.Resources, key, JsonValue.Create(request.InitialValue), overwrite: false))
                throw Error($"Key '{request.Key}' already exists or conflicts with a group in '{file.Path}'.");
            file.Changed = true;
        }
        return Plan(workspace);
    }

    public static TranslationWorkspaceTransactionPlan MutateKey(TranslationKeyMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceModel workspace = Load(request.Root, request.CatalogId);
        string[] source = KeySegments(request.SourceKey);
        string[]? target = request.Kind == TranslationKeyMutationKind.Delete
            ? null
            : KeySegments(Required(request.TargetKey, "target key"));
        if (target is not null && IsStrictPrefix(source, target))
            throw Error($"Target key '{request.TargetKey}' cannot be nested below its source key.");
        if (target is not null && workspace.Resources.Any(file => Get(file.Resources, target) is not null))
            throw Error($"Target key '{request.TargetKey}' already exists in this catalog.");
        bool found = false;
        foreach (ResourceFile file in workspace.Resources)
        {
            JsonNode? value = Get(file.Resources, source);
            if (value is null) continue;
            found = true;
            JsonNode clone = value.DeepClone();
            if (request.Kind != TranslationKeyMutationKind.Duplicate) Remove(file.Resources, source);
            if (target is not null && !TrySet(file.Resources, target, clone, overwrite: false))
                throw Error($"Target key '{request.TargetKey}' conflicts with a group in '{file.Path}'.");
            file.Changed = true;
        }
        if (!found) throw Error($"Key '{request.SourceKey}' does not exist in this catalog.");
        return Plan(workspace);
    }

    private static WorkspaceModel Load(string root, string catalogId)
    {
        string requiredCatalog = Required(catalogId, "catalog ID");
        TranslationWorkspaceDiscoveryResult discovery = TranslationWorkspaceDiscovery.Discover(root);
        TranslationDiscoveredCatalog catalog = discovery.Catalogs.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, requiredCatalog, StringComparison.Ordinal))
            ?? throw Error($"Catalog '{requiredCatalog}' was not found.");
        if (!catalog.Compilation.Success) throw Error("The catalog must compile before structural mutations can be planned.");
        if (catalog.ManifestPaths.Count != 1) throw Error($"Catalog '{requiredCatalog}' must have exactly one manifest.");

        TranslationWorkspaceFile manifestFile = discovery.Files.Single(file => file.RelativePath == catalog.ManifestPaths[0]);
        JsonObject manifest = ParseObject(manifestFile);
        JsonArray locales = manifest["locales"]?.AsArray() ?? throw Error("The catalog manifest has no locales array.");
        var localeObjects = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (JsonNode? node in locales)
        {
            JsonObject item = node?.AsObject() ?? throw Error("A locale declaration is invalid.");
            string tag = item["tag"]?.GetValue<string>() ?? throw Error("A locale declaration has no tag.");
            localeObjects.Add(tag, item);
        }
        var layers = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? node in manifest["layers"]?.AsArray() ?? throw Error("The catalog manifest has no layers array."))
            layers.Add(node?["name"]?.GetValue<string>() ?? throw Error("A layer declaration has no name."));

        var resources = new List<ResourceFile>();
        foreach (string path in catalog.DocumentPaths)
        {
            TranslationWorkspaceFile file = discovery.Files.Single(candidate => candidate.RelativePath == path);
            JsonObject document = ParseObject(file);
            resources.Add(new ResourceFile(
                path,
                document,
                file.Locale ?? throw Error($"Resource document '{path}' has no locale."),
                file.Layer ?? throw Error($"Resource document '{path}' has no layer."),
                Revision(file.GetUtf8Bytes())));
        }
        return new WorkspaceModel(
            discovery,
            requiredCatalog,
            manifestFile.RelativePath,
            Revision(manifestFile.GetUtf8Bytes()),
            manifest,
            locales,
            localeObjects,
            layers,
            resources,
            manifest["defaultLocale"]?.GetValue<string>() ?? throw Error("The manifest has no default locale."),
            manifest["schemaVersion"]?.GetValue<int>() ?? 2);
    }

    private static TranslationWorkspaceTransactionPlan Plan(WorkspaceModel workspace)
    {
        var edits = new List<TranslationWorkspaceEdit>();
        if (workspace.ManifestChanged)
            edits.Add(new TranslationWorkspaceEdit(workspace.ManifestPath, TranslationWorkspaceEditKind.Replace, workspace.ManifestRevision, Render(workspace.Manifest)));
        foreach (ResourceFile file in workspace.Resources)
        {
            if (file.Delete) edits.Add(new TranslationWorkspaceEdit(file.Path, TranslationWorkspaceEditKind.Delete, file.Revision, null));
            else if (file.Changed) edits.Add(new TranslationWorkspaceEdit(file.Path, TranslationWorkspaceEditKind.Replace, file.Revision, Render(file.Document)));
        }
        foreach (ResourceFile file in workspace.AddedResources)
            edits.Add(new TranslationWorkspaceEdit(file.Path, TranslationWorkspaceEditKind.Create, null, Render(file.Document)));
        if (edits.Count == 0) throw Error("The requested mutation does not change the workspace.");
        edits.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));

        var manifests = new List<TranslationSource> { new(workspace.ManifestPath, ProposedBytes(workspace.ManifestPath)) };
        var documents = new List<TranslationSource>();
        foreach (ResourceFile file in workspace.Resources)
        {
            if (!file.Delete) documents.Add(new TranslationSource(file.Path, ProposedBytes(file.Path)));
        }
        foreach (ResourceFile file in workspace.AddedResources)
            documents.Add(new TranslationSource(file.Path, Render(file.Document)));
        TranslationCompilation compilation = TranslationCompiler.Compile(manifests, documents);
        if (!compilation.Success) throw Error(FormatDiagnostics(compilation));
        return new TranslationWorkspaceTransactionPlan(workspace.Discovery.Root, workspace.CatalogId, edits.ToArray(), compilation);

        byte[] ProposedBytes(string path)
        {
            TranslationWorkspaceEdit? edit = edits.Find(candidate => candidate.RelativePath == path);
            if (edit?.Bytes is not null) return edit.Bytes;
            return workspace.Discovery.Files.Single(file => file.RelativePath == path).GetUtf8Bytes();
        }
    }

    private static string NewLocalePath(WorkspaceModel workspace, string locale, string layer)
    {
        string directory = Path.GetDirectoryName(workspace.ManifestPath)?.Replace('\\', '/') ?? string.Empty;
        string layerSuffix = workspace.Layers.Count == 1 ? string.Empty : $".{layer}";
        string fileName = $"{workspace.CatalogId}.{locale}{layerSuffix}.json";
        return directory.Length == 0 ? fileName : directory + "/" + fileName;
    }

    private static JsonObject ParseObject(TranslationWorkspaceFile file) =>
        JsonNode.Parse(file.GetUtf8Bytes())?.AsObject() ?? throw Error($"'{file.RelativePath}' is not a JSON object.");

    private static byte[] Render(JsonObject value) => Utf8.GetBytes(value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

    private static JsonNode? Get(JsonObject resources, string[] segments)
    {
        JsonNode? current = resources;
        for (int index = 0; index < segments.Length; index++)
        {
            if (current is not JsonObject group || !group.TryGetPropertyValue(segments[index], out current)) return null;
        }
        return current;
    }

    private static bool TrySet(JsonObject resources, string[] segments, JsonNode? value, bool overwrite)
    {
        JsonObject current = resources;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            if (!current.TryGetPropertyValue(segments[index], out JsonNode? next))
            {
                var created = new JsonObject();
                current[segments[index]] = created;
                current = created;
            }
            else if (next is JsonObject group && !group.ContainsKey("$value"))
            {
                current = group;
            }
            else
            {
                return false;
            }
        }
        string leaf = segments[^1];
        if (!overwrite && current.ContainsKey(leaf)) return false;
        current[leaf] = value;
        return true;
    }

    private static void Remove(JsonObject resources, string[] segments)
    {
        var parents = new List<(JsonObject Parent, string Segment)>();
        JsonObject current = resources;
        for (int index = 0; index < segments.Length - 1; index++)
        {
            parents.Add((current, segments[index]));
            current = current[segments[index]]!.AsObject();
        }
        current.Remove(segments[^1]);
        for (int index = parents.Count - 1; index >= 0; index--)
        {
            JsonObject child = parents[index].Parent[parents[index].Segment]!.AsObject();
            if (child.Count != 0) break;
            parents[index].Parent.Remove(parents[index].Segment);
        }
    }

    private static string[] KeySegments(string value)
    {
        string key = Required(value, "key");
        string[] result = key.Split('.');
        if (result.Any(static segment => segment.Length == 0 || segment[0] == '$')) throw Error($"Key '{key}' is invalid.");
        return result;
    }

    private static bool IsStrictPrefix(string[] prefix, string[] value)
    {
        if (prefix.Length >= value.Length) return false;
        for (int index = 0; index < prefix.Length; index++)
            if (!string.Equals(prefix[index], value[index], StringComparison.Ordinal)) return false;
        return true;
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw Error($"The {name} is required.") : value.Trim();

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FormatDiagnostics(TranslationCompilation compilation) => string.Join('\n', compilation.Diagnostics
        .Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error)
        .Select(static diagnostic => $"{diagnostic.Location.Path}({diagnostic.Location.Line},{diagnostic.Location.Column}): {diagnostic.Id} {diagnostic.Message}"));
    private static TranslationAuthoringException Error(string message) => new(message);
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed class WorkspaceModel(
        TranslationWorkspaceDiscoveryResult discovery,
        string catalogId,
        string manifestPath,
        string manifestRevision,
        JsonObject manifest,
        JsonArray locales,
        Dictionary<string, JsonObject> localeObjects,
        HashSet<string> layers,
        List<ResourceFile> resources,
        string defaultLocale,
        int schemaVersion)
    {
        public TranslationWorkspaceDiscoveryResult Discovery { get; } = discovery;
        public string CatalogId { get; } = catalogId;
        public string ManifestPath { get; } = manifestPath;
        public string ManifestRevision { get; } = manifestRevision;
        public JsonObject Manifest { get; } = manifest;
        public JsonArray Locales { get; } = locales;
        public Dictionary<string, JsonObject> LocaleObjects { get; } = localeObjects;
        public HashSet<string> Layers { get; } = layers;
        public List<ResourceFile> Resources { get; } = resources;
        public List<ResourceFile> AddedResources { get; } = [];
        public string DefaultLocale { get; } = defaultLocale;
        public int SchemaVersion { get; } = schemaVersion;
        public bool ManifestChanged { get; set; }
    }

    private sealed class ResourceFile(string path, JsonObject document, string locale, string layer, string? revision)
    {
        public string Path { get; } = path;
        public JsonObject Document { get; } = document;
        public JsonObject Resources => Document["resources"]!.AsObject();
        public string Locale { get; } = locale;
        public string Layer { get; } = layer;
        public string? Revision { get; } = revision;
        public bool Changed { get; set; }
        public bool Delete { get; set; }
    }
}
