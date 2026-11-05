using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>Where a library catalog comes from.</summary>
internal enum TranslationDependencyOrigin
{
    /// <summary>A NuGet package that packs <c>runic/&lt;catalog&gt;/</c> (<c>RunicTranslationsPackCatalog</c>).</summary>
    NuGet,
    /// <summary>An npm package whose <c>package.json#runicTranslations</c> names its packed runic.json.</summary>
    Npm,
}

/// <summary>
/// A library catalog that an application can override: the packed <c>runic.json</c> of an installed package, with the
/// sources beside it (<c>runic/&lt;catalog&gt;/</c>). Found from the restore output and <c>node_modules</c>, as the build
/// and the Vite plugin find it; nothing is downloaded or restored.
/// </summary>
/// <param name="Catalog">The catalog ID.</param>
/// <param name="Package">The package ID (NuGet) or name (npm).</param>
/// <param name="Version">The installed version.</param>
/// <param name="ProjectPath">The absolute path of the packed runic.json.</param>
internal sealed record TranslationDependencyCatalog(string Catalog, string Package, string Version, TranslationDependencyOrigin Origin, string ProjectPath)
{
    /// <summary>The directory of the packed catalog.</summary>
    internal string Root => Path.GetDirectoryName(ProjectPath)!;

    /// <summary>The packed <c>contract.json</c> (the base locale's locale artifact v5), or null when the package has none (npm).</summary>
    internal string? ContractPath => File.Exists(Path.Combine(Root, "contract.json")) ? Path.Combine(Root, "contract.json") : null;

    /// <summary>Whether <paramref name="package"/> (runic.json <c>overrides[].from</c>) names this package.</summary>
    internal bool IsFrom(string? package) => package is null ||
        string.Equals(package, Package, Origin == TranslationDependencyOrigin.NuGet ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>"Acme.Grid 1.2.0" or "@acme/grid@1.2.0".</summary>
    internal string Display => Origin == TranslationDependencyOrigin.NuGet ? Package + " " + Version : Package + "@" + Version;
}

/// <summary>
/// A library catalog an application overrides: an entry of runic.json <c>overrides</c>, or a directory under
/// <c>translations/overrides/</c> that runic.json does not list (<see cref="Listed"/> false; the build ignores it).
/// </summary>
/// <param name="Directory">The absolute path of <c>translations/overrides/&lt;catalog&gt;/</c>, whether it exists or not.</param>
/// <param name="Dependency">The installed catalog, or null when no restored package ships it.</param>
/// <param name="Problem">Why <see cref="Dependency"/> is null or the target is not built, as the build reports it (<c>RTR0104</c>); null when usable.</param>
internal sealed record TranslationOverrideTarget(string Catalog, string? From, string Directory, TranslationDependencyCatalog? Dependency, string? Problem)
{
    /// <summary>Whether runic.json lists the catalog in <c>overrides</c>.</summary>
    internal bool Listed { get; init; } = true;

    /// <summary>The review file that records the library text each override was written for: <c>overrides/&lt;catalog&gt;/&lt;catalog&gt;.review.json</c>.</summary>
    internal string ReviewPath => Path.Combine(Directory, Catalog + ".review.json");

    /// <summary>The override source of a locale: <c>overrides/&lt;catalog&gt;/&lt;locale&gt;.rmf2</c>.</summary>
    internal string SourcePath(string locale) => Path.Combine(Directory, locale + ".rmf2");
}

/// <summary>The library catalogs a translation project can use and the ones its runic.json overrides.</summary>
/// <param name="ProjectPath">The absolute path of the application's runic.json.</param>
/// <param name="OverridesRoot">The absolute path of <c>overrides/</c> beside runic.json when it declares <c>overrides</c>, else null.</param>
/// <param name="Catalogs">Every installed library catalog, by catalog ID.</param>
/// <param name="Overrides">The overridden catalogs, in runic.json order, then unlisted directories by name.</param>
/// <param name="Warnings">Problems that left catalogs out (an unreadable restore output, an invalid npm declaration).</param>
internal sealed record TranslationDependencyResolution(string ProjectPath, string? OverridesRoot, IReadOnlyList<TranslationDependencyCatalog> Catalogs,
    IReadOnlyList<TranslationOverrideTarget> Overrides, IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// The files the resolution read besides runic.json (restore outputs, the application's package.json and
    /// <c>node_modules/.package-lock.json</c>): a cache of the resolution is valid while they are unchanged.
    /// </summary>
    internal IReadOnlyList<string> Inputs { get; init; } = [];

    /// <summary>The override target of a catalog ID, or null.</summary>
    internal TranslationOverrideTarget? Override(string catalog) => Overrides.FirstOrDefault(target => string.Equals(target.Catalog, catalog, StringComparison.Ordinal));

    /// <summary>
    /// The override target of a catalog: the runic.json entry, an unlisted directory, or for an installed catalog runic.json
    /// does not override yet a new unlisted target (<see cref="TranslationOverrideChanges.Declare"/> lists it). Null when no
    /// installed package ships the catalog and runic.json does not name it.
    /// </summary>
    internal TranslationOverrideTarget? TargetFor(string catalog)
    {
        if (Override(catalog) is { } target) return target;
        TranslationDependencyCatalog? installed = Catalogs.FirstOrDefault(item => item.Catalog == catalog);
        if (installed is null || !TranslationReviewFile.IsFileName(catalog)) return null;
        string directory = Path.Combine(OverridesRoot ?? Path.Combine(Path.GetDirectoryName(ProjectPath)!, "overrides"), catalog);
        return new TranslationOverrideTarget(catalog, installed.Package, directory, installed,
            $"Catalog '{catalog}' is not listed in runic.json \"overrides\".") { Listed = false };
    }

    /// <summary>The override target whose directory holds <paramref name="path"/>, or null when the file is not an override source.</summary>
    internal TranslationOverrideTarget? OverrideOf(string path)
    {
        if (OverridesRoot is null) return null;
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(OverridesRoot, full).Replace('\\', '/');
        if (Path.IsPathRooted(relative) || relative == "." || relative == ".." || relative.StartsWith("../", StringComparison.Ordinal)) return null;
        int separator = relative.IndexOf('/');
        return separator <= 0 ? null : Override(relative[..separator]);
    }
}

/// <summary>
/// Finds the library catalogs of an application's dependencies and the catalogs its runic.json overrides, for the
/// Translations Editor, the language server and <c>runic-translations overrides</c>. NuGet catalogs come from the
/// project's restore output (<c>obj/project.assets.json</c>): every package whose files include
/// <c>runic/&lt;catalog&gt;/runic.json</c>, which is what <c>RunicTranslationsPackCatalog</c> packs. npm catalogs come
/// from <c>node_modules</c> by the Vite plugin's rules: the dependencies of the nearest package.json that declare
/// <c>runicTranslations</c>, and transitively theirs. Nothing is restored or installed; a project that was not restored
/// shows no NuGet catalogs.
/// </summary>
internal static class TranslationDependencyCatalogs
{
    private const long MaximumAssetsBytes = 64L * 1024 * 1024;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const int MaximumSourceBytes = 8 * 1024 * 1024;
    private const int MaximumSources = 10_000;
    private const int MaximumNpmPackages = 5_000;
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    private static readonly string[] ApplicationFields = ["dependencies", "devDependencies", "peerDependencies", "optionalDependencies"];
    private static readonly string[] LibraryFields = ["dependencies", "peerDependencies", "optionalDependencies"];

    /// <summary>Resolves the library catalogs of the project whose runic.json is <paramref name="projectPath"/>.</summary>
    /// <param name="projectBytes">The runic.json content to use instead of the file (an unsaved buffer), or null.</param>
    internal static TranslationDependencyResolution Resolve(string projectPath, byte[]? projectBytes = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(projectPath);
        string fullProject = Path.GetFullPath(projectPath);
        string directory = Path.GetDirectoryName(fullProject)!;
        var warnings = new List<string>();
        byte[] bytes = projectBytes ?? (File.Exists(fullProject) ? File.ReadAllBytes(fullProject) : []);
        IReadOnlyList<Rmf2OverrideV5> declared = bytes.Length == 0 ? [] : TranslationCompiler.ReadOverridesV5(new TranslationSource(fullProject, bytes));
        bool declaresOverrides = DeclaresOverrides(bytes);
        string? overridesRoot = declaresOverrides ? Path.Combine(directory, "overrides") : null;

        var catalogs = new List<TranslationDependencyCatalog>();
        var inputs = new List<string>(AssetsFiles(directory));
        if (FindApplicationRoot(directory) is { } npmRoot)
        {
            inputs.Add(Path.Combine(npmRoot, "package.json"));
            inputs.Add(Path.Combine(npmRoot, "node_modules", ".package-lock.json"));
        }
        catalogs.AddRange(DiscoverNuGet(directory, warnings, cancellationToken));
        catalogs.AddRange(DiscoverNpm(directory, warnings, cancellationToken));
        catalogs.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Catalog, right.Catalog) is var order and not 0 ? order : left.Origin.CompareTo(right.Origin));

        var targets = new List<TranslationOverrideTarget>();
        string overridesDirectory = overridesRoot ?? Path.Combine(directory, "overrides");
        foreach (Rmf2OverrideV5 item in declared)
        {
            if (!TranslationReviewFile.IsFileName(item.Catalog) || targets.Any(target => target.Catalog == item.Catalog)) continue;
            TranslationDependencyCatalog[] shipped = catalogs.Where(catalog => catalog.Catalog == item.Catalog).ToArray();
            TranslationDependencyCatalog[] from = shipped.Where(catalog => catalog.IsFrom(item.From)).ToArray();
            string? problem = null;
            if (from.Length == 0)
                problem = shipped.Length == 0
                    ? $"Catalog '{item.Catalog}' to override is not packed by any installed package; reference '{item.From}' and restore (dotnet restore, npm install)."
                    : $"Catalog '{item.Catalog}' comes from package '{string.Join("', '", shipped.Select(static catalog => catalog.Package))}', not '{item.From}'; correct \"from\" in runic.json.";
            targets.Add(new TranslationOverrideTarget(item.Catalog, item.From, Path.Combine(overridesDirectory, item.Catalog), from.FirstOrDefault(), problem));
        }
        if (overridesRoot is not null && Directory.Exists(overridesRoot))
            foreach (string child in Directory.EnumerateDirectories(overridesRoot).Order(StringComparer.Ordinal))
            {
                string name = Path.GetFileName(child);
                if (targets.Any(target => target.Catalog == name) || !TranslationReviewFile.IsFileName(name)) continue;
                targets.Add(new TranslationOverrideTarget(name, null, child, catalogs.FirstOrDefault(catalog => catalog.Catalog == name),
                    $"translations/overrides/{name} is not listed in runic.json \"overrides\"; its sources are ignored.") { Listed = false });
            }
        return new TranslationDependencyResolution(fullProject, overridesRoot, catalogs, targets, warnings) { Inputs = inputs };
    }

    private static bool DeclaresOverrides(byte[] bytes)
    {
        if (bytes.Length == 0) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes, new JsonDocumentOptions { AllowTrailingCommas = false });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("overrides", out _);
        }
        catch (JsonException) { return false; }
    }

    /// <summary>The library catalogs of the NuGet packages the .NET project that holds <paramref name="directory"/> restored.</summary>
    internal static IReadOnlyList<TranslationDependencyCatalog> DiscoverNuGet(string directory, List<string> warnings, CancellationToken cancellationToken = default)
    {
        var result = new List<TranslationDependencyCatalog>();
        foreach (string assets in AssetsFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (new FileInfo(assets).Length > MaximumAssetsBytes) { warnings.Add($"'{assets}' is larger than 64 MiB; its packages were not searched for catalogs."); continue; }
                using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(assets));
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var folders = new List<string>();
                if (root.TryGetProperty("packageFolders", out JsonElement packageFolders) && packageFolders.ValueKind == JsonValueKind.Object)
                    foreach (System.Text.Json.JsonProperty folder in packageFolders.EnumerateObject()) folders.Add(folder.Name);
                if (!root.TryGetProperty("libraries", out JsonElement libraries) || libraries.ValueKind != JsonValueKind.Object) continue;
                foreach (System.Text.Json.JsonProperty library in libraries.EnumerateObject())
                {
                    JsonElement value = library.Value;
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String ||
                        type.GetString() != "package" || !value.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String ||
                        !value.TryGetProperty("files", out JsonElement files) || files.ValueKind != JsonValueKind.Array) continue;
                    int slash = library.Name.IndexOf('/');
                    if (slash <= 0) continue;
                    string package = library.Name[..slash], version = library.Name[(slash + 1)..];
                    foreach (JsonElement file in files.EnumerateArray())
                    {
                        if (file.ValueKind != JsonValueKind.String) continue;
                        string[] segments = file.GetString()!.Split('/');
                        if (segments.Length != 3 || segments[0] != "runic" || segments[2] != "runic.json" || !TranslationReviewFile.IsFileName(segments[1])) continue;
                        foreach (string folder in folders)
                        {
                            string candidate = Path.GetFullPath(Path.Combine(folder, path.GetString()!, "runic", segments[1], "runic.json"));
                            if (!File.Exists(candidate)) continue;
                            if (!result.Any(item => item.Catalog == segments[1] && string.Equals(item.Package, package, StringComparison.OrdinalIgnoreCase)))
                                result.Add(new TranslationDependencyCatalog(segments[1], package, version, TranslationDependencyOrigin.NuGet, candidate));
                            break;
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                warnings.Add($"The restore output '{assets}' cannot be read: {error.Message}");
            }
        }
        return result;
    }

    // obj/project.assets.json of the nearest directory at or above the runic.json directory that holds a project file, and
    // artifacts/obj/<project>/project.assets.json (UseArtifactsOutput) in a directory above it.
    private static IEnumerable<string> AssetsFiles(string directory)
    {
        for (string? current = Path.GetFullPath(directory); current is not null; current = Path.GetDirectoryName(current))
        {
            string[] projects;
            try { projects = Directory.EnumerateFiles(current).Where(file => ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { yield break; }
            if (projects.Length == 0)
            {
                // A repository root ends the search; a runic.json is never part of a project above it.
                if (Directory.Exists(Path.Combine(current, ".git")) || File.Exists(Path.Combine(current, ".git"))) yield break;
                continue;
            }
            string local = Path.Combine(current, "obj", "project.assets.json");
            if (File.Exists(local)) { yield return local; yield break; }
            foreach (string project in projects)
                for (string? above = current; above is not null; above = Path.GetDirectoryName(above))
                {
                    string artifacts = Path.Combine(above, "artifacts", "obj", Path.GetFileNameWithoutExtension(project), "project.assets.json");
                    if (File.Exists(artifacts)) { yield return artifacts; break; }
                }
            yield break;
        }
    }

    /// <summary>
    /// The library catalogs of the npm dependencies of the nearest package.json at or above <paramref name="directory"/>, as
    /// the Vite plugin's <c>dependencies: "auto"</c> finds them; none without a package.json.
    /// </summary>
    internal static IReadOnlyList<TranslationDependencyCatalog> DiscoverNpm(string directory, List<string> warnings, CancellationToken cancellationToken = default)
    {
        string? root = FindApplicationRoot(directory);
        var result = new List<TranslationDependencyCatalog>();
        if (root is null) return result;
        JsonElement? application = ReadPackage(Path.Combine(root, "package.json"), warnings);
        if (application is null) return result;
        var queue = new List<(string Name, string From)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Enqueue(application.Value, root, ApplicationFields);
        for (int index = 0; index < queue.Count && visited.Count < MaximumNpmPackages; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string name, string from) = queue[index];
            string? packageJson = FindPackage(from, name);
            if (packageJson is null) continue;
            string packageRoot = Path.GetDirectoryName(Path.GetFullPath(packageJson))!;
            try { packageRoot = new DirectoryInfo(packageRoot).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? packageRoot; }
            catch (IOException) { }
            if (!visited.Add(packageRoot)) continue;
            if (ReadPackage(packageJson, warnings) is not { } description) continue;
            if (!description.TryGetProperty("runicTranslations", out JsonElement declared)) continue;
            if (declared.ValueKind != JsonValueKind.Object || !declared.TryGetProperty("catalog", out JsonElement catalogValue) || catalogValue.ValueKind != JsonValueKind.String ||
                !declared.TryGetProperty("project", out JsonElement projectValue) || projectValue.ValueKind != JsonValueKind.String)
            {
                warnings.Add($"Dependency '{name}' ({packageRoot}) declares an invalid runicTranslations catalog; it needs \"catalog\" and \"project\".");
                continue;
            }
            string catalog = catalogValue.GetString()!, project = projectValue.GetString()!;
            string projectPath = Path.GetFullPath(project, packageRoot);
            if (!TranslationReviewFile.IsFileName(catalog) || Path.IsPathRooted(project) || !IsWithin(packageRoot, projectPath) || !File.Exists(projectPath))
            {
                warnings.Add($"Dependency '{name}' ({packageRoot}) declares runicTranslations.project '{project}', which is not a runic.json inside the package.");
                continue;
            }
            string version = description.TryGetProperty("version", out JsonElement versionValue) && versionValue.ValueKind == JsonValueKind.String ? versionValue.GetString()! : "";
            // Breadth first: the copy nearest the application is the one the plugin regenerates.
            if (!result.Any(item => item.Catalog == catalog))
                result.Add(new TranslationDependencyCatalog(catalog, name, version, TranslationDependencyOrigin.Npm, projectPath));
            Enqueue(description, packageRoot, LibraryFields);
        }
        return result;

        void Enqueue(JsonElement manifest, string from, string[] fields)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string field in fields)
                if (manifest.TryGetProperty(field, out JsonElement value) && value.ValueKind == JsonValueKind.Object)
                    foreach (System.Text.Json.JsonProperty dependency in value.EnumerateObject()) names.Add(dependency.Name);
            foreach (string name in names) queue.Add((name, from));
        }
    }

    // The nearest directory at or above directory with a package.json, as the Vite plugin finds the application.
    private static string? FindApplicationRoot(string directory)
    {
        for (string? current = Path.GetFullPath(directory); current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(Path.Combine(current, "package.json"))) return current;
        return null;
    }

    private static JsonElement? ReadPackage(string path, List<string> warnings)
    {
        try
        {
            if (new FileInfo(path).Length > MaximumManifestBytes) return null;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add($"'{path}' cannot be read: {error.Message}");
            return null;
        }
    }

    // Node's lookup of a package directory: node_modules/<name> from the importing directory upward.
    private static string? FindPackage(string from, string name)
    {
        if (name.Length == 0 || name.Contains("..", StringComparison.Ordinal) || name.Contains('\\')) return null;
        for (string? directory = Path.GetFullPath(from); directory is not null; directory = Path.GetDirectoryName(directory))
        {
            string candidate = Path.Combine([directory, "node_modules", .. name.Split('/'), "package.json"]);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    internal static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && relative != "." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>The packed runic.json of a library catalog and every <c>.rmf2</c>/<c>.mf2</c> file below it, as the build reads them.</summary>
    /// <exception cref="IOException">A file cannot be read, is larger than 8 MiB, or the catalog has more than 10,000 sources.</exception>
    internal static (TranslationSource Project, IReadOnlyList<TranslationSource> Sources) ReadLibrary(TranslationDependencyCatalog catalog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var project = new TranslationSource(catalog.ProjectPath, ReadBounded(catalog.ProjectPath));
        var sources = new List<TranslationSource>();
        foreach (string file in EnumerateSources(catalog.Root, cancellationToken))
            sources.Add(new TranslationSource(file, ReadBounded(file)));
        return (project, sources);
    }

    /// <summary>
    /// The override sources of a target (<c>&lt;locale&gt;.rmf2</c> files below its directory), with <paramref name="buffers"/>
    /// (absolute path → unsaved content) in place of the files and added when they are new.
    /// </summary>
    internal static IReadOnlyList<TranslationSource> ReadOverrides(TranslationOverrideTarget target, IReadOnlyDictionary<string, byte[]>? buffers = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        var sources = new SortedDictionary<string, TranslationSource>(StringComparer.Ordinal);
        if (Directory.Exists(target.Directory))
            foreach (string file in EnumerateSources(target.Directory, cancellationToken))
                sources[new TranslationSource(file, []).Path] = new TranslationSource(file, ReadBounded(file));
        if (buffers is not null)
            foreach (var buffer in buffers)
                if (IsWithin(target.Directory, Path.GetFullPath(buffer.Key)) && IsSource(buffer.Key))
                    sources[new TranslationSource(Path.GetFullPath(buffer.Key), []).Path] = new TranslationSource(Path.GetFullPath(buffer.Key), buffer.Value);
        return sources.Values.ToArray();
    }

    private static bool IsSource(string path) =>
        string.Equals(Path.GetExtension(path), ".rmf2", StringComparison.OrdinalIgnoreCase) || string.Equals(Path.GetExtension(path), ".mf2", StringComparison.OrdinalIgnoreCase);

    private static List<string> EnumerateSources(string root, CancellationToken cancellationToken)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                else if (IsSource(entry))
                {
                    if (found.Count >= MaximumSources) throw new IOException($"'{root}' holds more than {MaximumSources} translation sources.");
                    found.Add(entry);
                }
            }
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }

    private static byte[] ReadBounded(string path)
    {
        var information = new FileInfo(path);
        if (information.Length > MaximumSourceBytes) throw new IOException($"'{path}' is larger than 8 MiB.");
        return File.ReadAllBytes(path);
    }
}
