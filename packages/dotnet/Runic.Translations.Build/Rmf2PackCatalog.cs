using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Build;

/// <summary>
/// Packs a library's catalog for application tooling (<c>RunicTranslationsPackCatalog</c>): <c>runic/&lt;catalog&gt;/</c> holds
/// its runic.json, its sources and <c>contract.json</c> (the base locale's locale artifact v5, which describes every key's
/// inputs and markup), and <c>buildTransitive/&lt;PackageId&gt;.props</c> declares it to a consuming project, whose
/// runic.json <c>overrides</c> are then validated against it.
/// </summary>
/// <remarks>
/// <para>
/// RTR0105 warns when the catalog ID is not the package ID in lower case; RTR0106 rejects a <c>runic-</c> catalog ID outside
/// the first-party <c>Runic.</c> packages. RTR0106 is a check of the author's own pack: it relies on nuget.org's reservation
/// of the <c>Runic.</c> ID prefix, so only the Runic organization can publish a package that passes it.
/// </para>
/// <para>
/// NuGet imports one props file per package and prefers <c>buildTransitive/</c> over <c>build/</c>, so the generated
/// <c>buildTransitive/&lt;PackageId&gt;.props</c> imports the library's own <c>build/&lt;PackageId&gt;.props</c> when the
/// project packs one (its settings then reach transitive consumers as well). A project that packs its own
/// <c>buildTransitive/&lt;PackageId&gt;.props</c> must set <c>RunicTranslationsPackCatalogProps</c> to false and import
/// <c>runic/&lt;catalog&gt;/catalog.props</c> from it (RTR0104).
/// </para>
/// </remarks>
public sealed class Rmf2PackCatalog : ITask
{
    // The package ships Runic.Translations.Compiler once, beside the source generator in analyzers/dotnet/cs; MSBuild loads
    // this task from tools/net10.0, so its load context resolves the compiler there. A source build has it beside this
    // assembly, where the load context finds it first.
    static Rmf2PackCatalog()
    {
        Assembly task = typeof(Rmf2PackCatalog).Assembly;
        if (AssemblyLoadContext.GetLoadContext(task) is not { } context) return;
        string directory = Path.GetDirectoryName(task.Location) ?? "";
        context.Resolving += (loadContext, name) =>
        {
            if (!string.Equals(name.Name, "Runic.Translations.Compiler", StringComparison.Ordinal) || directory.Length == 0) return null;
            string path = Path.GetFullPath(Path.Combine(directory, "..", "..", "analyzers", "dotnet", "cs", "Runic.Translations.Compiler.dll"));
            return File.Exists(path) ? loadContext.LoadFromAssemblyPath(path) : null;
        };
    }

    [Required]
    public ITaskItem? Project { get; set; }
    [Required]
    public string PackageId { get; set; } = string.Empty;
    [Required]
    public string OutputDirectory { get; set; } = string.Empty;
    /// <summary>Whether to write <c>buildTransitive/&lt;PackageId&gt;.props</c>; false when the library packs its own, which then imports <c>runic/&lt;catalog&gt;/catalog.props</c>.</summary>
    public bool EmitPackageProps { get; set; } = true;
    /// <summary>The project's packed files (<c>None</c> and <c>Content</c> items with <c>Pack</c> true), to find its own build props.</summary>
    public ITaskItem[] PackItems { get; set; } = Array.Empty<ITaskItem>();
    [Output]
    public ITaskItem[] PackageFiles { get; set; } = Array.Empty<ITaskItem>();
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;

    public bool Execute()
    {
        string config = Path.GetFullPath(Project!.ItemSpec), root = Path.GetDirectoryName(config)!;
        try
        {
            var files = new SortedSet<string>(StringComparer.Ordinal);
            using (var json = JsonDocument.Parse(File.ReadAllBytes(config)))
            {
                if (json.RootElement.TryGetProperty("sourceRoots", out JsonElement mounts) && mounts.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement mount in mounts.EnumerateArray())
                        if (mount.ValueKind == JsonValueKind.Object && mount.TryGetProperty("path", out JsonElement path) && path.ValueKind == JsonValueKind.String)
                            Rmf2SourceDiscovery.Discover(Path.GetFullPath(path.GetString()!, root), root, files);
                }
                else Rmf2SourceDiscovery.Discover(root, root, files);
                // The library's own overrides of other catalogs are compiled into its assembly, not part of its catalog.
                if (json.RootElement.TryGetProperty("overrides", out _))
                    files.RemoveWhere(file => Rmf2SourceDiscovery.IsWithin(Path.Combine(root, "overrides"), file));
            }
            foreach (string file in files)
                if (!Rmf2SourceDiscovery.IsWithin(root, file))
                {
                    Error("RTR0104", config, "A packed catalog needs its sources beneath its runic.json directory; '" + file + "' is outside it.");
                    return false;
                }

            TranslationSource project = Read(config, config);
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, files.Select(file => Read(file, file)).ToArray());
            if (compilation.Project is not { } linked || !compilation.Success)
            {
                TranslationDiagnostic? first = compilation.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
                Error(first?.Id ?? "RTR0104", first?.Location.Path ?? config, "The catalog cannot be packed because it does not compile" + (first is null ? "." : ": " + first.Message));
                return false;
            }

            string catalog = linked.Id;
            // runic-* catalog IDs belong to the first-party Runic packages (D055.3). nuget.org reserves the Runic. ID prefix for the
            // Runic organization, so a package that passes this check is first-party; npm packages are checked by their
            // @runic-artifex scope in the Vite plugin.
            if (catalog.StartsWith("runic-", StringComparison.Ordinal) && !PackageId.StartsWith("Runic.", StringComparison.OrdinalIgnoreCase))
            {
                Error("RTR0106", config, "Catalog ID '" + catalog + "' is reserved: 'runic-' catalogs belong to the Runic packages. Name the catalog after the package, such as '" +
                    PackageId.ToLowerInvariant() + "'.");
                return false;
            }
            if (!catalog.StartsWith("runic-", StringComparison.Ordinal) && !string.Equals(catalog, PackageId.ToLowerInvariant(), StringComparison.Ordinal))
                BuildEngine.LogWarningEvent(new BuildWarningEventArgs("translations", "RTR0105", config, 0, 0, 0, 0,
                    "Catalog ID '" + catalog + "' is not the package ID '" + PackageId + "' in lower case ('" + PackageId.ToLowerInvariant() +
                    "'). Catalog IDs are global: two libraries with one ID cannot be loaded together, and an application overrides a catalog by its ID.",
                    "RTR0105", nameof(Rmf2PackCatalog)));

            string output = Path.Combine(Path.GetFullPath(OutputDirectory), catalog);
            Directory.CreateDirectory(output);
            string contract = Path.Combine(output, "contract.json");
            WriteIfChanged(contract, Rmf2LocaleArtifactV5.Render(linked, linked.DefaultLocale).Text);
            string catalogProps = Path.Combine(output, "catalog.props");
            WriteIfChanged(catalogProps, "<Project>\n" +
                "  <!-- Generated by Runic.Translations.Build (RunicTranslationsPackCatalog): declares the packed catalog to a consuming project's runic.json \"overrides\". -->\n" +
                "  <ItemGroup>\n" +
                "    <RunicTranslationsPackedCatalog Include=\"$(MSBuildThisFileDirectory)runic.json\" Catalog=\"" + catalog + "\" Package=\"" + Escape(PackageId) + "\" />\n" +
                "  </ItemGroup>\n" +
                "</Project>\n");

            string packageRoot = "runic/" + catalog + "/";
            var items = new List<ITaskItem>
            {
                Item(config, packageRoot + "runic.json"),
                Item(contract, packageRoot + "contract.json"),
                Item(catalogProps, packageRoot + "catalog.props"),
            };
            foreach (string file in files)
                items.Add(Item(file, packageRoot + Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')));
            if (EmitPackageProps)
            {
                string buildProps = "build/" + PackageId + ".props", transitiveProps = "buildTransitive/" + PackageId + ".props";
                var packed = new HashSet<string>(PackItems.SelectMany(PackagePaths), StringComparer.OrdinalIgnoreCase);
                if (packed.Contains(transitiveProps))
                {
                    Error("RTR0104", config, "The project packs its own " + transitiveProps + ", where RunicTranslationsPackCatalog writes the props that declare catalog '" +
                        catalog + "'. Set RunicTranslationsPackCatalogProps to false and import '$(MSBuildThisFileDirectory)../" + packageRoot + "catalog.props' from your props file.");
                    return false;
                }
                // NuGet imports buildTransitive/<id>.props instead of build/<id>.props, so the library's own props are imported from it.
                string ownProps = packed.Contains(buildProps)
                    ? "  <Import Project=\"$(MSBuildThisFileDirectory)../" + buildProps + "\" Condition=\"Exists('$(MSBuildThisFileDirectory)../" + buildProps + "')\" />\n"
                    : "";
                string props = Path.Combine(Path.GetFullPath(OutputDirectory), "buildTransitive", PackageId + ".props");
                Directory.CreateDirectory(Path.GetDirectoryName(props)!);
                WriteIfChanged(props, "<Project>\n" + ownProps + "  <Import Project=\"$(MSBuildThisFileDirectory)../" + packageRoot + "catalog.props\" />\n</Project>\n");
                items.Add(Item(props, transitiveProps));
            }
            PackageFiles = items.ToArray();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            Error("RTR0104", config, exception.Message);
            return false;
        }
    }

    // The package paths of a packed item, as NuGet's pack computes them: PackagePath may list several paths; one that ends
    // with a separator or lacks the file's extension is a folder, which gets the file name appended.
    private static IEnumerable<string> PackagePaths(ITaskItem item)
    {
        if (!string.Equals(item.GetMetadata("Pack"), "true", StringComparison.OrdinalIgnoreCase)) yield break;
        string fileName = Path.GetFileName(item.ItemSpec), extension = Path.GetExtension(item.ItemSpec);
        foreach (string raw in item.GetMetadata("PackagePath").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string path = raw.Replace('\\', '/').TrimStart('/');
            if (path.Length == 0) continue;
            if (path.EndsWith('/')) path += fileName;
            else if (!string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) path += "/" + fileName;
            yield return path;
        }
    }

    private static TranslationSource Read(string path, string name) => new(name.Replace('\\', '/'), File.ReadAllBytes(path));

    private static TaskItem Item(string path, string packagePath)
    {
        var item = new TaskItem(path);
        item.SetMetadata("PackagePath", packagePath);
        return item;
    }

    private static string Escape(string value) => value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;");

    private static void WriteIfChanged(string path, string content)
    {
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        File.WriteAllBytes(path, bytes);
    }

    private void Error(string code, string file, string message) =>
        BuildEngine.LogErrorEvent(new BuildErrorEventArgs("translations", code, file, 0, 0, 0, 0, message, code, nameof(Rmf2PackCatalog)));
}
