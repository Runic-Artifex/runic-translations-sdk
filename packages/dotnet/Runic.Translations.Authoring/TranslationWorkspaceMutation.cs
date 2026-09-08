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
        if (workspace.IsToml && !workspace.Messages.Any(file => file.Locale == copyFrom))
            workspace.Create($"{workspace.ProjectPrefix}{locale}.toml", []);
        foreach (FileState source in workspace.Messages.Where(file => file.Locale == copyFrom))
            workspace.Create(workspace.IsToml ? $"{workspace.ProjectPrefix}{locale}.toml" : $"{workspace.ProjectPrefix}{locale}/{source.MessageId}.mf2", source.Bytes);
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
        if (workspace.IsToml)
        {
            foreach (Locale locale in workspace.Locales)
                workspace.EditLocale(locale.Tag, [new(TranslationLocaleEditKind.Add, key, request.InitialValue)]);
            return workspace.Plan();
        }
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
        if (workspace.IsToml)
        {
            bool foundBase = false;
            foreach (FileState file in workspace.Messages)
            {
                var document = TranslationLocaleReader.Read(new TranslationSource(file.Path, file.Bytes), file.Locale);
                if (targetKey is not null && document.Entries.Any(entry => entry.Key == targetKey)) throw Error($"Message '{targetKey}' already exists.");
                var entry = document.Entries.FirstOrDefault(entry => entry.Key == sourceKey);
                if (entry is null) continue;
                if (file.Locale == workspace.BaseLocale) foundBase = true;
                TranslationLocaleEdit edit = request.Kind switch
                {
                    TranslationKeyMutationKind.Delete => new(TranslationLocaleEditKind.Delete, sourceKey),
                    TranslationKeyMutationKind.RenameOrMove => new(TranslationLocaleEditKind.Rename, sourceKey, TargetKey: targetKey),
                    TranslationKeyMutationKind.Duplicate => new(TranslationLocaleEditKind.Add, targetKey!, Utf8.GetString(entry.Message.GetUtf8Bytes())),
                    _ => throw Error("Unknown key mutation kind."),
                };
                workspace.EditLocale(file.Locale, [edit]);
            }
            if (!foundBase) throw Error($"Message '{sourceKey}' does not exist in the base locale.");
            return workspace.Plan();
        }
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

    public static TranslationWorkspaceTransactionPlan MigrateToLocaleToml(string root, string catalogId)
    {
        Workspace workspace = Load(root, catalogId);
        workspace.Migrate();
        return workspace.Plan();
    }

    public static TranslationWorkspaceTransactionPlan ApplyLocaleEdits(string root, string catalogId, IEnumerable<TranslationLocaleFileEdit> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Workspace workspace = Load(root, catalogId);
        if (!workspace.IsToml) throw Error("Entry edits require sourceLayout locale-toml.");
        foreach (var group in changes.GroupBy(change => Normalize(change.RelativePath), StringComparer.Ordinal))
        {
            FileState file = workspace.Messages.SingleOrDefault(file => file.Path == group.Key)
                ?? throw Error($"Locale document '{group.Key}' was not found.");
            foreach (TranslationLocaleFileEdit change in group)
                if (Canonical(change.Locale) != file.Locale || !string.Equals(change.ExpectedRevision, Revision(file.Bytes), StringComparison.Ordinal))
                    throw Error($"'{file.Path}' changed after the operation was planned.");
            workspace.EditLocale(file.Locale, group.SelectMany(change => change.Edits).ToArray());
        }
        return workspace.Plan();
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw Error($"Source directory '{directory}' is a symbolic link or reparse point.");
        foreach (string path in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw Error($"Source '{path}' is a symbolic link or reparse point.");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (Path.GetFileName(path).StartsWith('.')) continue;
                foreach (string file in SafeFiles(path)) yield return file;
            }
            else yield return path;
        }
    }

    private static bool HasExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadBounded(string path)
    {
        if (new FileInfo(path).Length > 8 * 1024 * 1024) throw Error($"Source '{path}' exceeds the document size limit.");
        return File.ReadAllBytes(path);
    }

    private static Workspace Load(string root, string catalogId)
    {
        string fullRoot = Path.GetFullPath(root);
        string direct = Path.Combine(fullRoot, "runic.json");
        string configPath = File.Exists(direct) ? direct : Path.Combine(fullRoot, "translations", "runic.json");
        if (!File.Exists(configPath)) throw Error("The workspace does not contain runic.json.");
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(Path.GetDirectoryName(configPath)!) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(configPath) & FileAttributes.ReparsePoint) != 0) throw Error("The workspace configuration crosses a symbolic link or reparse point.");
        byte[] configBytes = ReadBounded(configPath);
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

        bool isToml = config["sourceLayout"]?.GetValue<string>() == "locale-toml";
        var messages = new List<FileState>();
        if (isToml)
        {
            foreach (string path in SafeFiles(projectRoot).Where(path => HasExtension(path, ".toml") || HasExtension(path, ".mf2")).Order(StringComparer.Ordinal))
            {
                string local = Normalize(Path.GetRelativePath(projectRoot, path));
                if (!HasExtension(path, ".toml") || local.Contains('/')) throw Error($"Unsupported mixed or nested locale source '{local}'.");
                string locale = Canonical(Path.GetFileNameWithoutExtension(path));
                if (messages.Any(file => file.Locale == locale)) throw Error($"Locale '{locale}' has colliding source files.");
                byte[] bytes = ReadBounded(path);
                var document = TranslationLocaleReader.Read(new TranslationSource(prefix + local, bytes), locale);
                if (!document.Success) throw Error("Invalid locale document: " + string.Join("; ", document.Diagnostics.Select(item => item.Message)));
                messages.Add(new FileState(prefix + local, locale, string.Empty, bytes));
            }
        }
        else
        {
            foreach (string path in SafeFiles(projectRoot).Where(path => HasExtension(path, ".mf2")).Order(StringComparer.Ordinal))
        {
            string local = Normalize(Path.GetRelativePath(projectRoot, path));
            string[] parts = local.Split('/');
            if (parts.Length != 2) throw Error($"Unsupported legacy message path '{local}'.");
            string locale = Canonical(parts[0]);
            if (messages.Any(file => file.Locale == locale && !file.Path.StartsWith(prefix + parts[0] + "/", StringComparison.Ordinal)))
                throw Error($"Locale '{locale}' has colliding source directories.");
            messages.Add(new FileState(prefix + local, locale, Path.GetFileNameWithoutExtension(parts[1]), ReadBounded(path)));
        }

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
        public bool IsToml => config["sourceLayout"]?.GetValue<string>() == "locale-toml";
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

        public void EditLocale(string locale, IReadOnlyList<TranslationLocaleEdit> edits)
        {
            FileState? file = Messages.SingleOrDefault(file => file.Locale == locale);
            string path = file?.Path ?? $"{ProjectPrefix}{locale}.toml";
            byte[] bytes = TranslationLocaleWriter.Apply(new TranslationSource(path, file?.Bytes ?? []), locale, edits);
            if (file is null) Create(path, bytes);
            else _edits.Add(new TranslationWorkspaceEdit(path, TranslationWorkspaceEditKind.Replace, Revision(file.Bytes), bytes));
        }

        public void Migrate()
        {
            if (IsToml) throw Error("The project already uses locale-toml.");
            if (SafeFiles(Path.GetDirectoryName(configPath)!).Any(path => HasExtension(path, ".toml")))
                throw Error("Migration destination collides with an existing TOML file.");
            var legacy = TranslationCompiler.CompileProject(new TranslationSource(Normalize(Path.GetRelativePath(Root, configPath)), configBytes), Messages.Select(file => new TranslationSource(file.Path, file.Bytes)));
            if (!legacy.Success) throw Error("The legacy project is invalid: " + string.Join("; ", legacy.Diagnostics.Select(item => item.Message)));
            foreach (Locale locale in Locales.OrderBy(item => item.Tag, StringComparer.Ordinal))
            {
                var text = new StringBuilder();
                foreach (FileState file in Messages.Where(file => file.Locale == locale.Tag).OrderBy(file => file.MessageId, StringComparer.Ordinal))
                {
                    TranslationLocaleWriter.RequireKey(file.MessageId);
                    text.Append(file.MessageId).Append(" = ").Append(TranslationLocaleWriter.EncodeValue(Utf8.GetString(file.Bytes))).Append('\n');
                }
                Create($"{ProjectPrefix}{locale.Tag}.toml", Utf8.GetBytes(text.ToString()));
            }
            foreach (FileState file in Messages) Delete(file);
            config["sourceLayout"] = "locale-toml";
            ReplaceConfig();
        }

        public void Create(string path, byte[] bytes)
        {
            if (File.Exists(Path.Combine(Root, path)) || Directory.Exists(Path.Combine(Root, path)) ||
                Messages.Any(file => file.Path == path) || _edits.Any(edit => edit.RelativePath == path && edit.Kind != TranslationWorkspaceEditKind.Delete))
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
            TranslationCompilation compilation = TranslationCompiler.CompileProject(
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
