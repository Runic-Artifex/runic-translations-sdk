using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>A semantic variable occurrence with an extracted-message location and its physical resource map.</summary>
public sealed record Rmf2VariableReference(Rmf2ResourceDocument Document, Rmf2ResourceNode Resource, TextSourceLocation Location, bool IsDeclaration);

/// <summary>A revisioned catalog of disk snapshots or unsaved buffers. Only affected physical files are reparsed.</summary>
public sealed class Rmf2Workspace
{
    private readonly CancellationToken _cancellationToken;
    private readonly string _root;
    private readonly string _baseLocale;
    private readonly TranslationSource _project;
    private readonly string _projectDirectory;
    private readonly List<(string Root, string[] Prefix)> _mounts = new();
    private readonly bool _hasMounts;
    private readonly Dictionary<string, TranslationSource> _sources;
    private readonly Dictionary<string, Rmf2ResourceDocument> _syntax = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public Rmf2Workspace(string root, TranslationSource project, IEnumerable<TranslationSource> sources) : this(root, project, sources, CancellationToken.None) { }
    public Rmf2Workspace(string root, TranslationSource project, IEnumerable<TranslationSource> sources, CancellationToken cancellationToken)
        : this(root, project, sources, null, cancellationToken) { }
    internal Rmf2Workspace(string root, TranslationSource project, IEnumerable<TranslationSource> sources, Rmf2WorkspaceCache? cache, CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken; cancellationToken.ThrowIfCancellationRequested();
        _root = Path.GetFullPath(root); _project = project;
        _projectDirectory = Path.GetDirectoryName(Path.GetFullPath(_project.Path, _root))!;
        using var config = JsonDocument.Parse(_project.GetUtf8Bytes());
        _baseLocale = config.RootElement.GetProperty("baseLocale").GetString()!;
        _hasMounts = config.RootElement.TryGetProperty("sourceRoots", out var mounts);
        if (_hasMounts)
            foreach (var mount in mounts.EnumerateArray())
                _mounts.Add((Path.GetFullPath(mount.GetProperty("path").GetString()!, _projectDirectory), mount.GetProperty("namespace").EnumerateArray().Select(v => v.GetString()!).ToArray()));
        else _mounts.Add((_projectDirectory, Array.Empty<string>()));
        _sources = sources.ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (var source in _sources.Values) if (source.Path.EndsWith(".rmf2", StringComparison.Ordinal)) _syntax.Add(source.Path, cache?.Read(source, cancellationToken) ?? Rmf2ResourceReader.Read(source, cancellationToken: _cancellationToken));
    }
    public IReadOnlyList<Rmf2ResourceDocument> Documents => _syntax.Values.OrderBy(d => d.Source.Path, StringComparer.Ordinal).ToArray();
    public string BaseLocale => _baseLocale;
    public string Revision(string path) => Hash(_sources[path].GetUtf8Bytes());
    public void Update(string path, byte[] content, string expectedRevision)
    {
        if (Revision(path) != expectedRevision) throw new TranslationAuthoringException("The resource buffer revision changed.");
        var source = new TranslationSource(path, content); _sources[path] = source; _syntax[path] = Rmf2ResourceReader.Read(source, cancellationToken: _cancellationToken);
    }
    public Rmf2LanguageService LanguageService => Rmf2LanguageService.Create(_project, _cancellationToken);
    /// <summary>Completes syntax using the base message as the functional-slot authority.</summary>
    public IReadOnlyList<Rmf2Completion> Complete(string path, string key, int byteOffset)
    {
        var origin = _syntax[path].Nodes.Single(node => !node.IsGroup && node.Key == key);
        var logical = LogicalPath(path, origin);
        var baseMessage = Documents.Where(document => string.Equals(Path.GetFileNameWithoutExtension(document.Source.Path), _baseLocale, StringComparison.OrdinalIgnoreCase))
            .SelectMany(document => document.Nodes.Where(node => !node.IsGroup && LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal))).FirstOrDefault();
        return LanguageService.Complete(origin.MessageSyntax, byteOffset, baseMessage?.MessageSyntax);
    }
    public TranslationCompilation Validate() => TranslationCompiler.CompileProject(_project, _sources.Values, null, _cancellationToken);
    internal TranslationProfileCompilation ValidateForProfile() => TranslationCompiler.CompileProjectForSelectedProfile(_project, _sources.Values, null, _cancellationToken);

    /// <summary>Finds locals in one message, or caller inputs across translations of the same logical resource.</summary>
    public IReadOnlyList<Rmf2VariableReference> VariableReferences(string path, string key, string name)
    {
        var origin = _syntax[path].Nodes.Single(n => !n.IsGroup && n.Key == key);
        if (origin.MessageSyntax!.VariableReferences(name).Count == 0) return Array.Empty<Rmf2VariableReference>();
        bool local = origin.MessageSyntax.Declarations.Any(d => d.Kind == "local" && d.Name == name);
        var logical = LogicalPath(path, origin);
        var result = new List<Rmf2VariableReference>();
        foreach (var document in Documents)
        foreach (var resource in document.Nodes.Where(n => !n.IsGroup))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (local ? document.Source.Path != path || resource.Key != key : !LogicalPath(document.Source.Path, resource).SequenceEqual(logical, StringComparer.Ordinal)) continue;
            var syntax = resource.MessageSyntax!;
            // The same spelling can name a translation-local value rather than this caller input.
            if (!local && syntax.Declarations.Any(d => d.Kind == "local" && d.Name == name)) continue;
            var declarations = syntax.Declarations.Where(d => d.Name == name).Select(d => d.NameLocation.StartByte).ToHashSet();
            foreach (var location in syntax.VariableReferences(name))
                result.Add(new Rmf2VariableReference(document, resource, location, declarations.Contains(location.StartByte)));
        }
        return result.AsReadOnly();
    }

    public TranslationWorkspaceTransactionPlan CreateResource(string path, IReadOnlyList<string> logicalPath, string message)
    {
        var source = _sources.TryGetValue(path, out var existing) ? existing : new TranslationSource(path, Array.Empty<byte>());
        return Plan(new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [path] = Rmf2ResourceWriter.AddMessage(source, LocalPath(path, logicalPath), message) });
    }
    /// <summary>Deletes, duplicates or moves a message across its locale resources while retaining attached metadata.</summary>
    public TranslationWorkspaceTransactionPlan MutateResource(IReadOnlyList<string> logicalPath, IReadOnlyList<string>? targetPath, bool duplicate = false)
    {
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var document in Documents)
        {
            var node = document.Nodes.SingleOrDefault(n => !n.IsGroup && LogicalPath(document.Source.Path, n).SequenceEqual(logicalPath, StringComparer.Ordinal));
            if (node is null) continue;
            byte[] bytes = document.Source.GetUtf8Bytes();
            if (!duplicate) bytes = Rmf2ResourceWriter.Replace(bytes, [(node.Location.StartByte, node.Location.LengthBytes, Array.Empty<byte>())]);
            if (targetPath is not null)
            {
                var local = LocalPath(document.Source.Path, targetPath);
                var source = new TranslationSource(document.Source.Path, bytes);
                bytes = Rmf2ResourceWriter.AddMessage(source, local, node.Message!);
                var added = Rmf2ResourceReader.Read(new TranslationSource(source.Path, bytes)).Nodes.Single(n => !n.IsGroup && n.Path.SequenceEqual(local, StringComparer.Ordinal));
                string newline = Utf8.GetString(bytes).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                string indent = new(' ', added.NameLocation.Column - 1);
                string metadata = string.Concat(node.Comments.Select(value => indent + "# " + value + newline).Concat(node.Properties.Select(value => indent + "@" + value + newline)));
                if (metadata.Length != 0) bytes = Rmf2ResourceWriter.Replace(bytes, [(added.Location.StartByte, 0, Utf8.GetBytes(metadata))]);
            }
            changes[document.Source.Path] = bytes;
        }
        if (changes.Count == 0) throw new TranslationAuthoringException("No matching message was found.");
        UpdateSlotKeys(changes, new Dictionary<string, string?> { [string.Join('_', logicalPath)] = targetPath is null ? null : string.Join('_', targetPath) }, duplicate);
        return Plan(changes);
    }

    /// <summary>Renames an explicit slot across a resource's locales and conditional slot contract.</summary>
    public TranslationWorkspaceTransactionPlan RenameSlot(string path, string key, string name, string newName)
    {
        var origin = _syntax[path].Nodes.Single(node => !node.IsGroup && node.Key == key);
        var logical = LogicalPath(path, origin);
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var document in Documents)
        foreach (var node in document.Nodes.Where(node => !node.IsGroup && LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal)))
        {
            byte[] edited = Rmf2ResourceWriter.RenameSlot(document.Source, node.Key, name, newName, _project);
            if (!edited.SequenceEqual(document.Source.GetUtf8Bytes())) changes[document.Source.Path] = edited;
        }
        if (changes.Count == 0) throw new TranslationAuthoringException("No explicit functional slot was found. Implicit slots must be made explicit before renaming.");
        var config = JsonNode.Parse(_project.GetUtf8Bytes())!;
        if (config["markup"]?["slots"]?[string.Join('_', logical)] is JsonObject slots && slots.TryGetPropertyValue(name, out var bounds))
        {
            if (slots.ContainsKey(newName)) throw new TranslationAuthoringException("The target slot contract already exists.");
            slots[newName] = bounds?.DeepClone(); slots.Remove(name);
            changes[_project.Path] = Utf8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        return Plan(changes);
    }

    /// <summary>Renames a caller input in resource sources; application call sites are a separate cross-language edit.</summary>
    public TranslationWorkspaceTransactionPlan RenameInput(string path, string key, string name, string newName)
    {
        var origin = _syntax[path].Nodes.Single(n => !n.IsGroup && n.Key == key);
        if (origin.MessageSyntax!.Declarations.Any(d => d.Kind == "local" && d.Name == name)) throw new TranslationAuthoringException("Use local rename for a message-local variable.");
        var references = VariableReferences(path, key, name);
        if (references.Count == 0) throw new TranslationAuthoringException("No matching input was found.");
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var resource in references.Select(reference => (reference.Document, reference.Resource)).Distinct())
            changes[resource.Document.Source.Path] = Rmf2ResourceWriter.RenameInput(resource.Document.Source, resource.Resource.Key, name, newName);
        return Plan(changes);
    }

    public TranslationWorkspaceTransactionPlan RenameLocal(string path, string key, string name, string newName) =>
        Plan(new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [path] = Rmf2ResourceWriter.RenameLocal(_sources[path], key, name, newName) });

    public TranslationWorkspaceTransactionPlan Rename(IReadOnlyList<string> logicalPath, string newName)
    {
        if (logicalPath.Count == 0 || !System.Text.RegularExpressions.Regex.IsMatch(newName, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new TranslationAuthoringException("A resource path and identifier are required.");
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var renamedMounts = _mounts.Select((mount, index) => (mount, index))
            .Where(item => logicalPath.Count <= item.mount.Prefix.Length && item.mount.Prefix.Take(logicalPath.Count).SequenceEqual(logicalPath, StringComparer.Ordinal)).Select(item => item.index).ToHashSet();
        if (_hasMounts && renamedMounts.Count > 0)
        {
            byte[] bytes = _project.GetUtf8Bytes(); var reader = new Utf8JsonReader(bytes);
            var edits = new List<(int, int, byte[])>(); int mountIndex = -1, segment = -1; bool roots = false, ns = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1) roots = reader.ValueTextEquals("sourceRoots");
                if (!roots) continue;
                if (reader.TokenType == JsonTokenType.StartObject && reader.CurrentDepth == 2) { mountIndex++; ns = false; }
                if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 3) { ns = reader.ValueTextEquals("namespace"); segment = -1; }
                if (ns && reader.TokenType == JsonTokenType.String && reader.CurrentDepth == 4)
                {
                    segment++;
                    if (renamedMounts.Contains(mountIndex) && segment == logicalPath.Count - 1)
                        edits.Add(((int)reader.TokenStartIndex, (int)(reader.BytesConsumed - reader.TokenStartIndex), JsonSerializer.SerializeToUtf8Bytes(newName)));
                }
            }
            changes[_project.Path] = Rmf2ResourceWriter.Replace(bytes, edits);
        }
        foreach (var source in _sources.Values)
        {
            string[] prefix = Prefix(source.Path);
            if (!prefix.Take(Math.Min(prefix.Length, logicalPath.Count)).SequenceEqual(logicalPath.Take(Math.Min(prefix.Length, logicalPath.Count)), StringComparer.Ordinal)) continue;
            if (logicalPath.Count <= prefix.Length)
            {
                var mount = Mount(source.Path);
                if (logicalPath.Count <= mount.Prefix.Length) continue; // The namespace is edited in runic.json.
                string[] directories = prefix.Skip(mount.Prefix.Length).ToArray(); directories[logicalPath.Count - mount.Prefix.Length - 1] = newName;
                string target = Path.Combine(mount.Root, Path.Combine(directories), Path.GetFileName(source.Path));
                if (!Path.IsPathRooted(source.Path)) target = Path.GetRelativePath(_root, target).Replace('\\', '/');
                changes.Add(source.Path, null); Add(target, source.GetUtf8Bytes());
            }
            else
            {
                string[] local = logicalPath.Skip(prefix.Length).ToArray();
                if (_syntax[source.Path].Nodes.Any(n => n.Path.SequenceEqual(local, StringComparer.Ordinal)))
                    changes[source.Path] = Rmf2ResourceWriter.Rename(source, local, newName);
            }
        }
        if (changes.Count == 0) throw new TranslationAuthoringException("No matching logical resource path.");
        var keyChanges = Documents.SelectMany(document => document.Nodes.Where(node => !node.IsGroup).Select(node => LogicalPath(document.Source.Path, node)))
            .Where(path => path.Count >= logicalPath.Count && path.Take(logicalPath.Count).SequenceEqual(logicalPath, StringComparer.Ordinal))
            .GroupBy(path => string.Join('_', path), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (string?)string.Join('_', group.First().Select((segment, index) => index == logicalPath.Count - 1 ? newName : segment)), StringComparer.Ordinal);
        UpdateSlotKeys(changes, keyChanges, false);
        return Plan(changes);
        void Add(string target, byte[] bytes) { if (_sources.ContainsKey(target) || !changes.TryAdd(target, bytes)) throw new TranslationAuthoringException("Rename destination exists."); }
    }

    /// <summary>Moves a complete physical group into its matching directory, retaining attached documentation.</summary>
    public TranslationWorkspaceTransactionPlan Extract(string sourcePath, IReadOnlyList<string> localGroup)
    {
        TranslationSource source = _sources[sourcePath];
        var document = Rmf2ResourceWriter.Require(source);
        var group = document.Nodes.Single(n => n.IsGroup && n.Path.SequenceEqual(localGroup, StringComparer.Ordinal));
        string destination = (Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') is { Length: > 0 } directory ? directory + "/" : "") + string.Join('/', localGroup) + "/" + Path.GetFileName(sourcePath);
        if (_sources.ContainsKey(destination)) throw new TranslationAuthoringException("Extract destination already exists.");
        byte[] raw = source.GetUtf8Bytes();
        int openingEnd = Array.IndexOf(raw, (byte)'\n', group.NameLocation.StartByte);
        int closingStart = group.Location.StartByte + group.Location.LengthBytes;
        while (closingStart > openingEnd && raw[closingStart - 1] is 10 or 13) closingStart--;
        while (closingStart > openingEnd && raw[closingStart - 1] != 10) closingStart--;
        string body = Utf8.GetString(raw.AsSpan(openingEnd + 1, closingStart - openingEnd - 1));
        string[] lines = body.Split('\n'); int margin = lines.Where(l => l.Trim().Length > 0).Select(l => l.Length - l.TrimStart(' ').Length).DefaultIfEmpty(0).Min();
        string extracted = string.Join('\n', lines.Select(l => l.Substring(Math.Min(margin, l.Length - l.TrimStart(' ').Length))));
        // Keep the authoritative documented group declaration; the extracted file reopens it.
        bool documented = group.Comments.Count != 0 || group.Properties.Count != 0;
        int removeStart = documented ? openingEnd + 1 : group.Location.StartByte;
        int removeLength = documented ? closingStart - removeStart : group.Location.LengthBytes;
        return Plan(new Dictionary<string, byte[]?>(StringComparer.Ordinal) {
            [sourcePath] = Rmf2ResourceWriter.Replace(raw, [(removeStart, removeLength, Array.Empty<byte>())]),
            [destination] = Utf8.GetBytes(extracted),
        });
    }

    public TranslationWorkspaceTransactionPlan Inline(string sourcePath, string destinationPath)
    {
        string[] sourcePrefix = Prefix(sourcePath), targetPrefix = Prefix(destinationPath);
        if (sourcePrefix.Length <= targetPrefix.Length || !sourcePrefix.Take(targetPrefix.Length).SequenceEqual(targetPrefix, StringComparer.Ordinal) || Path.GetFileName(sourcePath) != Path.GetFileName(destinationPath))
            throw new TranslationAuthoringException("Inline destination must be an ancestor resource for the same locale.");
        Rmf2ResourceWriter.Require(_sources[sourcePath]);
        string[] groups = sourcePrefix.Skip(targetPrefix.Length).ToArray();
        var result = new StringBuilder(_sources.TryGetValue(destinationPath, out var target) ? Utf8.GetString(target.GetUtf8Bytes()).TrimEnd('\r', '\n') + "\n" : "");
        for (int i = 0; i < groups.Length; i++) result.Append(' ', i * 2).Append(groups[i]).Append(" {\n");
        string body = Utf8.GetString(_sources[sourcePath].GetUtf8Bytes());
        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n')) result.Append(' ', groups.Length * 2).Append(line).Append('\n');
        for (int i = groups.Length - 1; i >= 0; i--) result.Append(' ', i * 2).Append("}\n");
        return Plan(new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [sourcePath] = null, [destinationPath] = Utf8.GetBytes(result.ToString()) });
    }

    public TranslationWorkspaceTransactionPlan Format(string path) => Plan(new Dictionary<string, byte[]?>(StringComparer.Ordinal) { [path] = Rmf2ResourceWriter.Format(_sources[path]) });

    public TranslationWorkspaceTransactionPlan MigrateToml(out IReadOnlyList<string> notes)
    {
        TranslationWorkspaceTransactionPlan plan = MigrateToml(out notes, out _);
        return plan;
    }

    /// <summary>Migrates locale TOML to RMF2 and exposes stable structured loss details.</summary>
    public TranslationWorkspaceTransactionPlan MigrateToml(out IReadOnlyList<string> notes, out TranslationMigrationReport report)
    {
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var losses = new List<TranslationMigrationLoss>();
        JsonObject config = JsonNode.Parse(_project.GetUtf8Bytes())!.AsObject();
        if (config["sourceLayout"]?.GetValue<string>() != "locale-toml") throw new TranslationAuthoringException("TOML migration requires sourceLayout locale-toml.");
        config["sourceLayout"] = "rmf2-v1";
        foreach (var source in _sources.Values)
        {
            string path = Path.ChangeExtension(source.Path, ".rmf2"), backup = source.Path + ".bak";
            if (_sources.ContainsKey(path) || File.Exists(Path.Combine(_root, Relative(backup)))) throw new TranslationAuthoringException("Migration destination or backup exists.");
            changes[path] = Rmf2ResourceWriter.ImportTomlWithReport(source, Path.GetFileNameWithoutExtension(source.Path), out TranslationMigrationReport sourceReport);
            losses.AddRange(sourceReport.Losses.Select(loss => loss with { Location = Relative(loss.Location) }));
            changes[source.Path] = null; changes[backup] = source.GetUtf8Bytes();
        }
        changes[_project.Path] = Utf8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        report = new TranslationMigrationReport(losses
            .OrderBy(static loss => loss.Location, StringComparer.Ordinal)
            .ThenBy(static loss => loss.Code, StringComparer.Ordinal));
        notes = report.Notes;
        return Plan(changes);
    }

    /// <summary>Convenience overload for callers that only consume structured migration data.</summary>
    public TranslationWorkspaceTransactionPlan MigrateTomlWithReport(out TranslationMigrationReport report)
    {
        return MigrateToml(out _, out report);
    }

    /// <summary>Adds a locale by copying its physical source documents through a validated transaction.</summary>
    public TranslationWorkspaceTransactionPlan AddLocale(string locale, string? fallback = null, string? copyFrom = null) => ChangeLocale("add", locale, fallback, copyFrom);
    /// <summary>Removes a non-base locale and redirects dependent fallback edges.</summary>
    public TranslationWorkspaceTransactionPlan RemoveLocale(string locale, string? replacementFallback = null) => ChangeLocale("remove", locale, replacementFallback, null);
    /// <summary>Changes a locale fallback after checking the complete fallback graph.</summary>
    public TranslationWorkspaceTransactionPlan SetFallback(string locale, string? fallback = null) => ChangeLocale("fallback", locale, fallback, null);
    private TranslationWorkspaceTransactionPlan ChangeLocale(string operation, string locale, string? fallback, string? copyFrom)
    {
        locale = TranslationProjectScaffolder.CanonicalizeLocale(locale.Trim());
        ProfileLocaleView catalog = ValidateLocaleView();
        if (!catalog.Success) throw new TranslationAuthoringException("Repair catalog diagnostics before changing locales: " + string.Join("; ", catalog.Diagnostics.Select(diagnostic => diagnostic.Message)));
        fallback = TranslationProjectScaffolder.CanonicalizeLocale((fallback ?? catalog.DefaultLocale!).Trim());
        var locales = catalog.Locales.ToDictionary(item => item.Tag, item => item.Fallback, StringComparer.Ordinal);
        if (!locales.ContainsKey(fallback) || fallback == locale) throw new TranslationAuthoringException("A different existing fallback locale is required.");
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        if (operation == "add")
        {
            if (locales.ContainsKey(locale)) throw new TranslationAuthoringException("The locale already exists.");
            copyFrom = TranslationProjectScaffolder.CanonicalizeLocale((copyFrom ?? catalog.DefaultLocale!).Trim());
            if (!locales.ContainsKey(copyFrom)) throw new TranslationAuthoringException("The source locale does not exist.");
            foreach (var source in _sources.Values.Where(source => string.Equals(Path.GetFileNameWithoutExtension(source.Path), copyFrom, StringComparison.OrdinalIgnoreCase)))
            {
                string target = Path.Combine(Path.GetDirectoryName(source.Path) ?? "", locale + ".rmf2").Replace('\\', '/');
                if (_sources.ContainsKey(target)) throw new TranslationAuthoringException("A destination source file already exists.");
                changes.Add(target, source.GetUtf8Bytes());
            }
            locales.Add(locale, fallback);
        }
        else
        {
            if (!locales.ContainsKey(locale) || locale == catalog.DefaultLocale) throw new TranslationAuthoringException("An existing non-base locale is required.");
            if (operation == "fallback") locales[locale] = fallback;
            else
            {
                locales.Remove(locale);
                foreach (string dependent in locales.Where(pair => pair.Value == locale).Select(pair => pair.Key).ToArray()) locales[dependent] = fallback;
                foreach (var source in _sources.Values.Where(source => string.Equals(Path.GetFileNameWithoutExtension(source.Path), locale, StringComparison.OrdinalIgnoreCase))) changes[source.Path] = null;
            }
        }
        var config = JsonNode.Parse(_project.GetUtf8Bytes())!.AsObject();
        config["locales"] = new JsonArray(locales.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => (JsonNode)(pair.Value is null ? new JsonObject { ["tag"] = pair.Key } : new JsonObject { ["tag"] = pair.Key, ["fallback"] = pair.Value })).ToArray());
        changes[_project.Path] = Utf8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return Plan(changes);
    }

    private ProfileLocaleView ValidateLocaleView()
    {
        TranslationProfileCompilation compilation = ValidateForProfile();
        if (compilation.Current is { Catalogs.Count: 1 } current)
        {
            CompiledTextCatalog catalog = current.Catalogs[0];
            return new ProfileLocaleView(catalog.Id, catalog.DefaultLocale,
                catalog.Locales.Select(locale => new ProfileLocale(locale.Tag, locale.FallbackTag)).ToArray(), compilation.Diagnostics);
        }
        if (compilation.Rmf2?.Project is { } project)
            return new ProfileLocaleView(project.Id, project.DefaultLocale,
                project.Locales.Select(locale => new ProfileLocale(locale.Tag, locale.FallbackTag)).ToArray(), compilation.Diagnostics);
        return new ProfileLocaleView(null, null, Array.Empty<ProfileLocale>(), compilation.Diagnostics);
    }

    private void UpdateSlotKeys(Dictionary<string, byte[]?> changes, IReadOnlyDictionary<string, string?> keys, bool duplicate)
    {
        byte[] bytes = changes.GetValueOrDefault(_project.Path) ?? _project.GetUtf8Bytes();
        var config = JsonNode.Parse(bytes)!;
        if (config["markup"]?["slots"] is not JsonObject slots) return;
        bool changed = false;
        foreach (var pair in keys)
        {
            if (pair.Key == pair.Value || !slots.TryGetPropertyValue(pair.Key, out var value)) continue;
            if (pair.Value is not null)
            {
                if (slots.ContainsKey(pair.Value)) throw new TranslationAuthoringException("The target resource already has a slot contract.");
                slots.Add(pair.Value, value?.DeepClone());
            }
            if (!duplicate) slots.Remove(pair.Key);
            changed = true;
        }
        if (!changed) return;
        var reader = new Utf8JsonReader(bytes); bool markup = false;
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.CurrentDepth == 1) markup = reader.ValueTextEquals("markup");
            if (!markup || reader.CurrentDepth != 2 || !reader.ValueTextEquals("slots")) continue;
            reader.Read(); int start = (int)reader.TokenStartIndex; reader.Skip();
            changes[_project.Path] = Rmf2ResourceWriter.Replace(bytes, [(start, (int)reader.BytesConsumed - start, Utf8.GetBytes(slots.ToJsonString()))]);
            return;
        }
    }

    private TranslationWorkspaceTransactionPlan Plan(Dictionary<string, byte[]?> changes)
    {
        var sources = new Dictionary<string, TranslationSource>(_sources, StringComparer.Ordinal); TranslationSource project = _project;
        var edits = new List<TranslationWorkspaceEdit>();
        foreach (var change in changes)
        {
            TranslationSource? original = change.Key == _project.Path ? _project : _sources.GetValueOrDefault(change.Key);
            edits.Add(new TranslationWorkspaceEdit(Relative(change.Key), change.Value is null ? TranslationWorkspaceEditKind.Delete : original is null ? TranslationWorkspaceEditKind.Create : TranslationWorkspaceEditKind.Replace,
                original is null ? null : Hash(original.GetUtf8Bytes()), change.Value));
            if (change.Key == _project.Path) project = new TranslationSource(change.Key, change.Value!);
            else if (change.Value is null) sources.Remove(change.Key);
            else if (change.Key.EndsWith(".rmf2", StringComparison.Ordinal)) sources[change.Key] = new TranslationSource(change.Key, change.Value);
        }
        TranslationProfileCompilation profileCompilation = TranslationCompiler.CompileProjectForSelectedProfile(project, sources.Values, null, _cancellationToken);
        if (!profileCompilation.Success) throw new TranslationAuthoringException("The complete edited catalog is invalid: " + string.Join("; ", profileCompilation.Diagnostics.Select(d => d.Message)));
        string catalogId = profileCompilation.Current?.Catalogs[0].Id ?? profileCompilation.Rmf2!.Project!.Id;
        return new TranslationWorkspaceTransactionPlan(_root, catalogId, edits.AsReadOnly(), profileCompilation);
    }

    private sealed record ProfileLocale(string Tag, string? Fallback);
    private sealed record ProfileLocaleView(string? Catalog, string? DefaultLocale,
        IReadOnlyList<ProfileLocale> Locales, IReadOnlyList<TranslationDiagnostic> Diagnostics)
    {
        internal bool Success => Catalog is not null && DefaultLocale is not null &&
            !Diagnostics.Any(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
    }

    public IReadOnlyList<string> LogicalPath(string path, Rmf2ResourceNode node) => Prefix(path).Concat(node.Path).ToArray();
    /// <summary>Resolves an explicit logical path relative to a physical resource mount.</summary>
    public IReadOnlyList<string> LocalPath(string path, IReadOnlyList<string> logicalPath)
    {
        string[] prefix = Prefix(path);
        if (logicalPath.Count <= prefix.Length || !logicalPath.Take(prefix.Length).SequenceEqual(prefix, StringComparer.Ordinal))
            throw new TranslationAuthoringException("The resource path is outside the destination namespace.");
        return logicalPath.Skip(prefix.Length).ToArray();
    }
    private (string Root, string[] Prefix) Mount(string path)
    {
        string physical = Path.GetFullPath(path, _root);
        foreach (var mount in _mounts)
        {
            string relative = Path.GetRelativePath(mount.Root, physical);
            if (relative != ".." && !relative.StartsWith("../", StringComparison.Ordinal) && !Path.IsPathRooted(relative)) return mount;
        }
        throw new TranslationAuthoringException("Resource is outside every configured source root.");
    }
    private string[] Prefix(string path)
    {
        string physical = Path.GetFullPath(path, _root);
        foreach (var mount in _mounts)
        {
            string relative = Path.GetRelativePath(mount.Root, physical);
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            string directory = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? "";
            return mount.Prefix.Concat(directory.Split('/', StringSplitOptions.RemoveEmptyEntries)).ToArray();
        }
        throw new TranslationAuthoringException("Resource is outside every configured source root.");
    }
    private string Relative(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path, _root)).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)) throw new TranslationAuthoringException("An edit escapes the workspace root.");
        return relative;
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
