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

/// <summary>A revisioned catalog of disk snapshots or unsaved buffers. Only affected physical files are reparsed.</summary>
public sealed class Rmf2Workspace
{
    private readonly string _root;
    private readonly TranslationSource _project;
    private readonly string _projectDirectory;
    private readonly List<(string Root, string[] Prefix)> _mounts = new();
    private readonly bool _hasMounts;
    private readonly Dictionary<string, TranslationSource> _sources;
    private readonly Dictionary<string, Rmf2ResourceDocument> _syntax = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public Rmf2Workspace(string root, TranslationSource project, IEnumerable<TranslationSource> sources)
    {
        _root = Path.GetFullPath(root); _project = project;
        _projectDirectory = Path.GetDirectoryName(Path.GetFullPath(_project.Path, _root))!;
        using var config = JsonDocument.Parse(_project.GetUtf8Bytes());
        _hasMounts = config.RootElement.TryGetProperty("sourceRoots", out var mounts);
        if (_hasMounts)
            foreach (var mount in mounts.EnumerateArray())
                _mounts.Add((Path.GetFullPath(mount.GetProperty("path").GetString()!, _projectDirectory), mount.GetProperty("namespace").EnumerateArray().Select(v => v.GetString()!).ToArray()));
        else _mounts.Add((_projectDirectory, Array.Empty<string>()));
        _sources = sources.ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (var source in _sources.Values) if (source.Path.EndsWith(".rmf2", StringComparison.Ordinal)) _syntax.Add(source.Path, Rmf2ResourceReader.Read(source));
    }
    public IReadOnlyList<Rmf2ResourceDocument> Documents => _syntax.Values.OrderBy(d => d.Source.Path, StringComparer.Ordinal).ToArray();
    public string Revision(string path) => Hash(_sources[path].GetUtf8Bytes());
    public void Update(string path, byte[] content, string expectedRevision)
    {
        if (Revision(path) != expectedRevision) throw new TranslationAuthoringException("The resource buffer revision changed.");
        var source = new TranslationSource(path, content); _sources[path] = source; _syntax[path] = Rmf2ResourceReader.Read(source);
    }
    public TranslationCompilation Validate() => TranslationCompiler.CompileProject(_project, _sources.Values);

    public TranslationWorkspaceTransactionPlan Rename(IReadOnlyList<string> logicalPath, string newName)
    {
        if (logicalPath.Count == 0 || !System.Text.RegularExpressions.Regex.IsMatch(newName, "^[A-Za-z_][A-Za-z0-9_]*$")) throw new TranslationAuthoringException("A resource path and identifier are required.");
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var source in _sources.Values)
        {
            string[] prefix = Prefix(source.Path);
            if (!prefix.Take(Math.Min(prefix.Length, logicalPath.Count)).SequenceEqual(logicalPath.Take(Math.Min(prefix.Length, logicalPath.Count)), StringComparer.Ordinal)) continue;
            if (logicalPath.Count <= prefix.Length)
            {
                // A namespace supplied by an explicit mount is configuration, not a folder rename.
                if (HasMounts()) throw new TranslationAuthoringException("Rename an explicitly mounted namespace in sourceRoots; the physical root must remain stable.");
                string projectDirectory = Path.GetDirectoryName(_project.Path)?.Replace('\\', '/') ?? "";
                string[] directories = prefix.ToArray(); directories[logicalPath.Count - 1] = newName;
                string target = (projectDirectory.Length == 0 ? "" : projectDirectory + "/") + string.Join('/', directories) + "/" + Path.GetFileName(source.Path);
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
        var changes = new Dictionary<string, byte[]?>(StringComparer.Ordinal); var warnings = new List<string>();
        JsonObject config = JsonNode.Parse(_project.GetUtf8Bytes())!.AsObject();
        if (config["sourceLayout"]?.GetValue<string>() != "locale-toml") throw new TranslationAuthoringException("TOML migration requires sourceLayout locale-toml.");
        config["sourceLayout"] = "rmf2-v1";
        foreach (var source in _sources.Values)
        {
            string path = Path.ChangeExtension(source.Path, ".rmf2"), backup = source.Path + ".bak";
            if (_sources.ContainsKey(path) || File.Exists(Path.Combine(_root, Relative(backup)))) throw new TranslationAuthoringException("Migration destination or backup exists.");
            changes[path] = Rmf2ResourceWriter.ImportToml(source, Path.GetFileNameWithoutExtension(source.Path), out var messages);
            warnings.AddRange(messages); changes[source.Path] = null; changes[backup] = source.GetUtf8Bytes();
        }
        changes[_project.Path] = Utf8.GetBytes(config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        notes = warnings.AsReadOnly(); return Plan(changes);
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
        var compilation = TranslationCompiler.CompileProject(project, sources.Values);
        if (!compilation.Success) throw new TranslationAuthoringException("The complete edited catalog is invalid: " + string.Join("; ", compilation.Diagnostics.Select(d => d.Message)));
        return new TranslationWorkspaceTransactionPlan(_root, compilation.Catalogs[0].Id, edits.AsReadOnly(), compilation);
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
    private bool HasMounts() => _hasMounts;
    private string Relative(string path)
    {
        string relative = Path.GetRelativePath(_root, Path.GetFullPath(path, _root)).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)) throw new TranslationAuthoringException("An edit escapes the workspace root.");
        return relative;
    }
    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
}
