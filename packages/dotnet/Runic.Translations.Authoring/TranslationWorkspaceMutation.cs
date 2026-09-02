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
        Workspace workspace = Load(request.Root, request.CatalogId);
        string locale = Canonical(request.Locale);
        string copyFrom = Canonical(request.CopyFromLocale);
        string fallback = request.Fallback is null ? workspace.BaseLocale : Canonical(request.Fallback);
        workspace.RequireMissingLocale(locale);
        workspace.RequireLocale(copyFrom);
        workspace.RequireLocale(fallback);
        if (locale == fallback) throw Error($"Locale '{locale}' cannot fall back to itself.");
        workspace.Locales.Add(new Locale(locale, fallback));
        workspace.ReplaceConfig();
        foreach (FileState source in workspace.Messages.Where(file => file.Locale == copyFrom))
            workspace.Create($"{workspace.ProjectPrefix}{locale}/{source.MessageId}.mf2", source.Bytes);
        return workspace.Plan();
    }

    public static TranslationWorkspaceTransactionPlan RemoveLocale(TranslationRemoveLocaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Workspace workspace = Load(request.Root, request.CatalogId);
        string locale = Canonical(request.Locale);
        workspace.RequireLocale(locale);
        if (locale == workspace.BaseLocale) throw Error("The base locale cannot be removed.");
        string replacement = request.ReplacementFallback is null ? workspace.BaseLocale : Canonical(request.ReplacementFallback);
        workspace.RequireLocale(replacement);
        if (replacement == locale) throw Error("The replacement fallback cannot be the removed locale.");
        workspace.Locales.RemoveAll(item => item.Tag == locale);
        for (int index = 0; index < workspace.Locales.Count; index++)
            if (workspace.Locales[index].Fallback == locale)
                workspace.Locales[index] = workspace.Locales[index] with { Fallback = replacement };
        workspace.ReplaceConfig();
        foreach (FileState file in workspace.Messages.Where(file => file.Locale == locale)) workspace.Delete(file);
        return workspace.Plan();
    }

    public static TranslationWorkspaceTransactionPlan SetFallback(TranslationSetFallbackRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Workspace workspace = Load(request.Root, request.CatalogId);
        string locale = Canonical(request.Locale);
        workspace.RequireLocale(locale);
        if (locale == workspace.BaseLocale) throw Error("The base locale cannot declare a fallback.");
        string fallback = request.Fallback is null ? workspace.BaseLocale : Canonical(request.Fallback);
        workspace.RequireLocale(fallback);
        if (locale == fallback) throw Error($"Locale '{locale}' cannot fall back to itself.");
        int index = workspace.Locales.FindIndex(item => item.Tag == locale);
        workspace.Locales[index] = workspace.Locales[index] with { Fallback = fallback };
        workspace.ReplaceConfig();
        return workspace.Plan();
    }

    public static TranslationWorkspaceTransactionPlan CreateKey(TranslationCreateKeyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Workspace workspace = Load(request.Root, request.CatalogId);
        string key = Identifier(request.Key);
        byte[] content = Utf8.GetBytes(request.InitialValue + (request.InitialValue.EndsWith('\n') ? string.Empty : "\n"));
        foreach (Locale locale in workspace.Locales)
            workspace.Create($"{workspace.ProjectPrefix}{locale.Tag}/{key}.mf2", content);
        return workspace.Plan();
    }

    public static TranslationWorkspaceTransactionPlan MutateKey(TranslationKeyMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Workspace workspace = Load(request.Root, request.CatalogId);
        string sourceKey = Identifier(request.SourceKey);
        string? targetKey = request.Kind == TranslationKeyMutationKind.Delete ? null : Identifier(request.TargetKey ?? string.Empty);
        List<FileState> sources = workspace.Messages.Where(file => file.MessageId == sourceKey).ToList();
        if (!sources.Any(file => file.Locale == workspace.BaseLocale)) throw Error($"Message '{sourceKey}' does not exist in the base locale.");
        if (targetKey is not null && workspace.Messages.Any(file => file.MessageId == targetKey))
            throw Error($"Message '{targetKey}' already exists.");
        foreach (FileState source in sources)
        {
            if (targetKey is not null)
                workspace.Create($"{workspace.ProjectPrefix}{source.Locale}/{targetKey}.mf2", source.Bytes);
            if (request.Kind != TranslationKeyMutationKind.Duplicate) workspace.Delete(source);
        }
        return workspace.Plan();
    }

    private static Workspace Load(string root, string catalogId)
    {
        string fullRoot = Path.GetFullPath(root);
        string direct = Path.Combine(fullRoot, "runic.json");
        string configPath = File.Exists(direct) ? direct : Path.Combine(fullRoot, "translations", "runic.json");
        if (!File.Exists(configPath)) throw Error("The workspace does not contain runic.json.");
        byte[] configBytes = File.ReadAllBytes(configPath);
        JsonObject config;
        try { config = JsonNode.Parse(configBytes)?.AsObject() ?? throw Error("runic.json must contain an object."); }
        catch (JsonException exception) { throw new TranslationAuthoringException("runic.json is malformed.", exception); }
        string actualCatalog = config["catalog"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(actualCatalog, catalogId, StringComparison.Ordinal)) throw Error($"Catalog '{catalogId}' was not found.");
        string baseLocale = Canonical(config["baseLocale"]?.GetValue<string>() ?? string.Empty);
        string projectRoot = Path.GetDirectoryName(configPath)!;
        string prefix = Normalize(Path.GetRelativePath(fullRoot, projectRoot));
        if (prefix == ".") prefix = string.Empty;
        else prefix += "/";

        var messages = new List<FileState>();
        foreach (string path in Directory.EnumerateFiles(projectRoot, "*.mf2", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string local = Normalize(Path.GetRelativePath(projectRoot, path));
            string[] parts = local.Split('/');
            if (parts.Length != 2) continue;
            messages.Add(new FileState(prefix + local, Canonical(parts[0]), Path.GetFileNameWithoutExtension(parts[1]), File.ReadAllBytes(path)));
        }

        List<Locale> locales = ReadLocales(config, messages, baseLocale);
        return new Workspace(fullRoot, actualCatalog, configPath, prefix, config, configBytes, baseLocale, locales, messages);
    }

    private static List<Locale> ReadLocales(JsonObject config, IReadOnlyList<FileState> messages, string baseLocale)
    {
        var result = new List<Locale>();
        if (config["locales"] is JsonArray declared)
        {
            foreach (JsonNode? node in declared)
            {
                if (node is JsonValue value)
                {
                    string tag = Canonical(value.GetValue<string>());
                    result.Add(new Locale(tag, tag == baseLocale ? null : baseLocale));
                }
                else if (node is JsonObject item)
                {
                    string tag = Canonical(item["tag"]?.GetValue<string>() ?? string.Empty);
                    string? fallback = item["fallback"] is null ? (tag == baseLocale ? null : baseLocale) : Canonical(item["fallback"]!.GetValue<string>());
                    result.Add(new Locale(tag, fallback));
                }
            }
        }
        else
        {
            result.Add(new Locale(baseLocale, null));
            foreach (string locale in messages.Select(file => file.Locale).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                if (locale != baseLocale) result.Add(new Locale(locale, baseLocale));
        }
        if (!result.Any(locale => locale.Tag == baseLocale)) result.Insert(0, new Locale(baseLocale, null));
        return result;
    }

    private static string Identifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw Error("The message ID is required.");
        value = value.Trim();
        if (!(value[0] == '_' || char.IsAsciiLetter(value[0])) || value.Any(character => character != '_' && !char.IsAsciiLetterOrDigit(character)))
            throw Error($"Message ID '{value}' must be a TypeScript identifier and MF2 filename.");
        return value;
    }

    private static string Canonical(string value) => TranslationProjectScaffolder.CanonicalizeLocale(value.Trim());
    private static string Normalize(string path) => path.Replace('\\', '/');
    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static TranslationAuthoringException Error(string message) => new(message);

    private sealed class Workspace(
        string root,
        string catalogId,
        string configPath,
        string projectPrefix,
        JsonObject config,
        byte[] configBytes,
        string baseLocale,
        List<Locale> locales,
        List<FileState> messages)
    {
        private readonly List<TranslationWorkspaceEdit> _edits = [];
        public string Root { get; } = root;
        public string CatalogId { get; } = catalogId;
        public string ProjectPrefix { get; } = projectPrefix;
        public string BaseLocale { get; } = baseLocale;
        public List<Locale> Locales { get; } = locales;
        public List<FileState> Messages { get; } = messages;

        public void RequireLocale(string locale)
        {
            if (!Locales.Any(item => item.Tag == locale)) throw Error($"Locale '{locale}' is not declared.");
        }

        public void RequireMissingLocale(string locale)
        {
            if (Locales.Any(item => item.Tag == locale)) throw Error($"Locale '{locale}' is already declared.");
        }

        public void ReplaceConfig()
        {
            var array = new JsonArray();
            foreach (Locale locale in Locales)
            {
                if (locale.Fallback is null || locale.Fallback == BaseLocale)
                    array.Add((JsonNode?)JsonValue.Create(locale.Tag));
                else
                    array.Add((JsonNode)new JsonObject { ["tag"] = locale.Tag, ["fallback"] = locale.Fallback });
            }
            config["locales"] = array;
            byte[] bytes = Utf8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            string relative = Normalize(Path.GetRelativePath(Root, configPath));
            _edits.Add(new TranslationWorkspaceEdit(relative, TranslationWorkspaceEditKind.Replace, Revision(configBytes), bytes));
        }

        public void Create(string path, byte[] bytes)
        {
            if (Messages.Any(file => file.Path == path) || _edits.Any(edit => edit.RelativePath == path && edit.Kind != TranslationWorkspaceEditKind.Delete))
                throw Error($"'{path}' already exists.");
            _edits.Add(new TranslationWorkspaceEdit(path, TranslationWorkspaceEditKind.Create, null, bytes));
        }

        public void Delete(FileState file) =>
            _edits.Add(new TranslationWorkspaceEdit(file.Path, TranslationWorkspaceEditKind.Delete, Revision(file.Bytes), null));

        public TranslationWorkspaceTransactionPlan Plan()
        {
            if (_edits.Count == 0) throw Error("The requested mutation does not change the workspace.");
            _edits.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
            string configRelative = Normalize(Path.GetRelativePath(Root, configPath));
            byte[] proposedConfig = _edits.FirstOrDefault(edit => edit.RelativePath == configRelative)?.Bytes ?? configBytes;
            var proposed = Messages.ToDictionary(file => file.Path, file => file.Bytes, StringComparer.Ordinal);
            foreach (TranslationWorkspaceEdit edit in _edits)
            {
                if (edit.RelativePath == configRelative) continue;
                if (edit.Kind == TranslationWorkspaceEditKind.Delete) proposed.Remove(edit.RelativePath);
                else proposed[edit.RelativePath] = edit.Bytes!;
            }
            TranslationCompilation compilation = TranslationCompiler.CompileMf2Project(
                new TranslationSource(configRelative, proposedConfig),
                proposed.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new TranslationSource(pair.Key, pair.Value)));
            if (!compilation.Success)
            {
                string diagnostics = string.Join("\n", compilation.Diagnostics.Where(item => item.Severity == TranslationDiagnosticSeverity.Error).Select(item => $"{item.Id} {item.Message}"));
                throw Error("The proposed translation project is invalid:\n" + diagnostics);
            }
            return new TranslationWorkspaceTransactionPlan(Root, CatalogId, _edits.ToArray(), compilation);
        }
    }

    private sealed record Locale(string Tag, string? Fallback);
    private sealed record FileState(string Path, string Locale, string MessageId, byte[] Bytes);
}
