using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;
using Runic.Translations.Compiler;

namespace Runic.Translations.Build;

/// <summary>Discovers configured source mounts without executing application code.</summary>
public sealed class Rmf2SourceDiscovery : ITask
{
    private const string CommandLineCatalog = "runic-command-line";
    /// <summary>A package catalog in the former format (<see cref="TranslationCatalogFormats.FormerFormatWarning"/>); never reported in 0.24.</summary>
    internal const string FormerFormatCode = "RTR0109";

    [Required]
    public ITaskItem[] Projects { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>
    /// The packed library catalogs of referenced packages, with Package and Catalog metadata: <c>RunicTranslationsPackedCatalog</c>
    /// items naming <c>runic/&lt;catalog&gt;/runic.json</c>, and <c>RaxTranslationsPackedCatalog</c> items naming
    /// <c>rax/&lt;catalog&gt;/rax.json</c> from packages built after the rename. A package that declares a catalog in both
    /// formats is read in the new one (<see cref="TranslationCatalogFormats.PackagePreference"/>).
    /// </summary>
    public ITaskItem[] PackedCatalogs { get; set; } = Array.Empty<ITaskItem>();
    [Output]
    public string[] Sources { get; set; } = Array.Empty<string>();
    /// <summary>The override sources of runic.json <c>overrides</c> (<c>translations/overrides/&lt;catalog&gt;/</c>), with Catalog metadata.</summary>
    [Output]
    public ITaskItem[] OverrideSources { get; set; } = Array.Empty<ITaskItem>();
    /// <summary>
    /// The runic.json and sources of each packed catalog that runic.json overrides, and of <c>runic-command-line</c> whenever a
    /// referenced package packs it, as additional files for the source generator (RunicTranslationKind PackedCatalogProject or
    /// PackedCatalogSource).
    /// </summary>
    [Output]
    public ITaskItem[] PackedCatalogFiles { get; set; } = Array.Empty<ITaskItem>();
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;
    public bool Execute()
    {
        try
        {
            var files = new SortedSet<string>(StringComparer.Ordinal);
            var overrideFiles = new SortedSet<string>(StringComparer.Ordinal);
            var overridden = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ITaskItem project in Projects)
            {
                string config = Path.GetFullPath(project.ItemSpec), root = Path.GetDirectoryName(config)!;
                // A project's sources follow its file's format: a rax.json catalog (Command Line's, from its package after the
                // rename) has .ramf2 sources, a runic.json catalog .rmf2 ones.
                TranslationCatalogFormat format = TranslationCatalogFormats.OfProject(config);
                if (new FileInfo(config).Length > 8 * 1024 * 1024) throw new IOException("Translation project exceeds byte limit.");
                using var json = JsonDocument.Parse(File.ReadAllBytes(config));
                if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("runic.json must contain a JSON object.");
                // runic.json "overrides": translations/overrides/<catalog>/<locale>.rmf2 belongs to the library catalogs, not
                // to this project's catalog. The compiler validates the entries themselves.
                string? overridesRoot = null;
                if (json.RootElement.TryGetProperty("overrides", out JsonElement overrides) && overrides.ValueKind == JsonValueKind.Array)
                {
                    overridesRoot = Path.Combine(root, "overrides");
                    foreach (JsonElement entry in overrides.EnumerateArray())
                        if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("catalog", out JsonElement catalog) && catalog.ValueKind == JsonValueKind.String)
                            overridden.Add(catalog.GetString()!);
                    if (Directory.Exists(overridesRoot)) Discover(overridesRoot, root, overrideFiles);
                }
                if (json.RootElement.TryGetProperty("sourceRoots", out JsonElement mounts))
                {
                    if (mounts.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("sourceRoots must be an array.");
                    foreach (JsonElement mount in mounts.EnumerateArray())
                    {
                        if (mount.ValueKind != JsonValueKind.Object ||
                            !mount.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(path.GetString()))
                            throw new InvalidOperationException("Each source root must declare a non-empty path.");
                        Discover(Path.GetFullPath(path.GetString()!, root), root, files, format);
                    }
                }
                else Discover(root, root, files, format);
                if (overridesRoot is not null) files.RemoveWhere(file => IsWithin(overridesRoot, file));
            }
            Sources = new List<string>(files).ToArray();
            var overrideItems = new List<ITaskItem>();
            foreach (string file in overrideFiles)
            {
                // translations/overrides/<catalog>/<file>: the catalog is the first directory below overrides.
                string relative = Path.GetRelativePath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Projects[0].ItemSpec))!, "overrides"), file);
                int separator = relative.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
                var item = new TaskItem(file);
                item.SetMetadata("Catalog", separator < 0 ? "" : relative.Substring(0, separator));
                overrideItems.Add(item);
            }
            OverrideSources = overrideItems.ToArray();
            var packedItems = new List<ITaskItem>();
            foreach (ITaskItem packed in PreferredPackedCatalogs(PackedCatalogs))
            {
                string catalog = packed.GetMetadata("Catalog"), package = packed.GetMetadata("Package");
                // runic-command-line also goes to the generator without an override, so it can report application messages
                // named after its keys, which have no effect (RTR0107).
                if (!overridden.Contains(catalog) && !string.Equals(catalog, CommandLineCatalog, StringComparison.Ordinal)) continue;
                string config = Path.GetFullPath(packed.ItemSpec), root = Path.GetDirectoryName(config)!;
                if (!File.Exists(config)) continue;
                TranslationCatalogFormat format = TranslationCatalogFormats.OfProject(config);
                if (TranslationCatalogFormats.FormerFormatWarning(catalog, package, format) is { } former)
                    BuildEngine.LogWarningEvent(new BuildWarningEventArgs("translations", FormerFormatCode, config, 0, 0, 0, 0, former,
                        FormerFormatCode, nameof(Rmf2SourceDiscovery)));
                var project = new TaskItem(config);
                project.SetMetadata("RunicTranslationKind", "PackedCatalogProject");
                project.SetMetadata("RunicTranslationPackedCatalog", catalog);
                project.SetMetadata("RunicTranslationPackage", package);
                packedItems.Add(project);
                var sources = new SortedSet<string>(StringComparer.Ordinal);
                Discover(root, root, sources, format);
                foreach (string source in sources)
                {
                    var item = new TaskItem(source);
                    item.SetMetadata("RunicTranslationKind", "PackedCatalogSource");
                    item.SetMetadata("RunicTranslationProject", config);
                    packedItems.Add(item);
                }
            }
            PackedCatalogFiles = packedItems.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("translations", "RTR0052", "", 0, 0, 0, 0, exception.Message, "", nameof(Rmf2SourceDiscovery))); return false;
        }
    }
    /// <summary>
    /// The packed catalogs to read: each package's catalog once, in the format <see cref="TranslationCatalogFormats.PackagePreference"/>
    /// prefers when the package declares it in both (its runic.json and its rax.json).
    /// </summary>
    internal static IReadOnlyList<ITaskItem> PreferredPackedCatalogs(IReadOnlyList<ITaskItem> packed)
    {
        var chosen = new List<ITaskItem>();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem item in packed)
        {
            string identity = item.GetMetadata("Package") + "\0" + item.GetMetadata("Catalog");
            if (!index.TryGetValue(identity, out int at)) { index[identity] = chosen.Count; chosen.Add(item); continue; }
            int Rank(ITaskItem candidate)
            {
                TranslationCatalogFormat format = TranslationCatalogFormats.OfProject(candidate.ItemSpec);
                for (int order = 0; order < TranslationCatalogFormats.PackagePreference.Count; order++)
                    if (TranslationCatalogFormats.PackagePreference[order] == format) return order;
                return int.MaxValue;
            }
            if (Rank(item) < Rank(chosen[at])) chosen[at] = item;
        }
        return chosen;
    }

    internal static void Discover(string root, string projectRoot, SortedSet<string> files) =>
        Discover(root, projectRoot, files, TranslationCatalogFormat.Runic);

    /// <summary>Adds the sources of a catalog in <paramref name="format"/> below <paramref name="root"/>: <c>.mf2</c>, and <c>.rmf2</c> or <c>.ramf2</c>.</summary>
    internal static void Discover(string root, string projectRoot, SortedSet<string> files, TranslationCatalogFormat format)
    {
        ValidateNoReparseAncestors(root, projectRoot);
        var directories = new Stack<string>(); directories.Push(root); int visited = 0;
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            if (++visited > 100_000) throw new IOException("Translation discovery directory limit exceeded.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Translation source roots must not traverse symbolic links.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Translation sources must not traverse symbolic links.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                else if (TranslationCatalogFormats.IsSource(path, format)) files.Add(path);
            }
        }
    }

    // Checks the ancestors of a source root below the deepest directory it shares with the project.
    // Components above that belong to the environment: macOS /tmp and /var, or a symlinked home or
    // checkout, are links a project cannot avoid. The traversal checks the root and its contents.
    private static void ValidateNoReparseAncestors(string path, string projectRoot)
    {
        string fullPath = Path.GetFullPath(path);
        string? boundary = Path.GetFullPath(projectRoot);
        while (boundary is not null && !IsWithin(boundary, fullPath)) boundary = Path.GetDirectoryName(boundary);
        string? current = Path.GetDirectoryName(fullPath);
        while (current is not null && boundary is not null && IsWithin(boundary, current) &&
               Path.GetRelativePath(boundary, current) != ".")
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Translation source roots must not traverse symbolic links.");
            current = Path.GetDirectoryName(current);
        }
    }

    internal static bool IsWithin(string root, string path) => PathContainment.IsWithin(root, path);
}
