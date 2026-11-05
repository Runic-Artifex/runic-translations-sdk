using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Runic.CommandLine;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary><c>runic-translations overrides add|list</c>: override messages of a packed library catalog (runic.json <c>overrides</c>).</summary>
[CommandGroup(Group, Description = "Override the text of library catalogs that installed packages pack.")]
internal static class OverridesCommandModule
{
    internal const string Group = "overrides";
    private const string ProjectDescription = "The application's translations directory or its runic.json. Defaults to ./runic.json, then ./translations/runic.json.";

    [Command("overrides add", Description = "Scaffold overrides of a library catalog in translations/overrides/<catalog>/<locale>.rmf2.",
        LongDescription = "Reads the library catalog from the installed package (the restore output's NuGet package folder, or node_modules), as the build and the Vite plugin find it; restore first. With --key, adds each message to <locale>.rmf2 seeded with the library's text in that locale (for a locale the library lacks, the text of the locale it falls back to), with the library's base text as comments above it, and records the library text it was written for (the base text and the text that locale shows) in overrides/<catalog>/<catalog>.review.json, so validate, status, the Translations Editor and the language server report the override (RTR0090) when a library update changes either. Without --key, creates <locale>.rmf2 with every library message as a commented-out template. A locale the library lacks is added with its first message and falls back to its nearest CLDR parent among the library's and the added locales, else to the library locale CLDR likely subtags match, else to the library's base locale. A catalog runic.json does not list in \"overrides\" yet is added there, from the package that ships it. The request is checked first; runic.json, the override source and the review file are then written together, each only if it did not change meanwhile, or nothing is written.\n\nExit codes: 0 success; 1 command failure, such as an inaccessible file; 2 invalid invocation, an unknown catalog, key or locale, or a key that is already overridden; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations overrides add acme.grid --locale fr", "runic-translations overrides add acme.grid --locale de --key grid.title", "runic-translations overrides add acme.grid --locale de-CH --key empty --key rows --dry-run"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> Add(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Argument(Required = true, ValueName = "catalog", Description = "The library catalog ID, as runic.json \"overrides\" lists it, such as acme.grid.")] string catalog,
        [Option("--locale", Required = true, ValueName = "tag", Description = "The locale to override, a library locale or one the application adds.")] string locale,
        [Option("--key", AllowMultipleValues = true, ValueName = "key", Description = "A library message to override, as its logical key (grid.title) or canonical key (grid_title); repeatable.")] IReadOnlyList<string> keys,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project,
        [Option("--dry-run", Description = "Report the change without writing it.")] bool dryRun) =>
        operations.Execute(new("overrides add", Project: project, Catalog: catalog, DryRun: dryRun) { Tag = locale, Keys = keys });

    [Command("overrides list", Description = "List the library catalogs of installed packages and the application's overrides of them.",
        LongDescription = "For each installed package that packs a catalog: its package, version and locales; for each catalog runic.json overrides: the locales the application adds (with their fallback), the overridden messages per locale, and overrides whose library library text (base text, or the text of the overridden locale) changed since they were written.\n\nExit codes: 0 success; 1 command failure; 2 invalid invocation; 3 internal failure; 130 cancelled.",
        Examples = ["runic-translations overrides list", "runic-translations overrides list --project src/Shop/translations"])]
    [CommandResult("runic.translations.tool/1", typeof(TranslationsToolCommandJsonContext))]
    internal static CommandOutcome<TranslationsToolCommandResult> List(
        [FromServices] ITranslationsToolCommandOperations operations,
        [Option("--project", Description = ProjectDescription, ValueName = "directory")] string? project) =>
        operations.Execute(new("overrides list", Project: project));
}

internal static class OverridesOperations
{
    internal static int Add(string projectPath, string catalogId, string locale, IReadOnlyList<string> keys, bool dryRun, ToolOperationResult result)
    {
        string manifest = Manifest(projectPath);
        (TranslationOverrideTarget target, TranslationLibraryCatalog catalog) = Load(manifest, catalogId);
        string current = Path.GetFullPath(Environment.CurrentDirectory);
        // The whole change is planned first (keys, locale, the library and the review file are checked); then runic.json,
        // the override source and the review file are written together, or nothing is.
        TranslationOverridePlan plan;
        try
        {
            plan = keys.Count == 0
                ? TranslationOverrideChanges.Scaffold(catalog, locale)
                : TranslationOverrideChanges.Override(catalog, keys.Select(key => KeyOf(catalog, key)).ToArray(), locale);
            plan = TranslationOverrideChanges.Declaring(plan, manifest, target);
        }
        catch (ArgumentException exception) { throw new ToolUsageException(exception.Message); }
        catch (InvalidOperationException exception) { throw new ToolDiagnosticException(exception.Message); }
        if (!dryRun)
        {
            try { TranslationOverrideChanges.Apply(plan, target with { Listed = true, Problem = null }); }
            catch (TranslationOverrideConflictException exception) { throw new ToolDiagnosticException(exception.Message); }
            catch (TranslationInterchangeException exception) { throw new ToolDiagnosticException($"The review file {InputFiles.DisplayPath(target.ReviewPath, current)} cannot be written ({exception.Code}): {exception.Message}"); }
        }
        if (plan.ProjectPath is not null)
            result.WriteOutputLine($"{(dryRun ? "would add" : "added")} {{ \"catalog\": \"{catalogId}\", \"from\": \"{target.Dependency!.Package}\" }} to \"overrides\" in {InputFiles.DisplayPath(manifest, current)}.");
        string file = InputFiles.DisplayPath(plan.Path, current);
        string verb = dryRun ? (plan.Creates ? "would create" : "would update") : (plan.Creates ? "created" : "updated");
        result.WriteOutputLine(plan.Keys.Count == 0
            ? $"{verb} {file} with {catalog.Messages.Count} commented-out template(s) of catalog {catalog.Catalog} ({catalog.Dependency.Display})."
            : $"{verb} {file}: overrides {string.Join(", ", plan.Keys.Select(key => string.Join(".", catalog.Message(key)!.LogicalPath)))} of catalog {catalog.Catalog} ({catalog.Dependency.Display}).");
        if (plan.AddedLocaleFallback is { } fallback)
            result.WriteOutputLine($"The library has no {Path.GetFileNameWithoutExtension(plan.Path)}; the application adds it, falling back to {fallback}" +
                (plan.Keys.Count == 0 ? " once it has a message." : "."));
        if (plan.Review is not null)
            result.WriteOutputLine($"{(dryRun ? "would record" : "recorded")} the library's text in {InputFiles.DisplayPath(target.ReviewPath, current)}.");
        return Program.Success;
    }

    internal static int List(string projectPath, ToolOperationResult result)
    {
        string manifest = Manifest(projectPath);
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest);
        foreach (string warning in resolution.Warnings) result.WriteOutputLine("warning: " + warning);
        if (resolution.Catalogs.Count == 0) result.WriteOutputLine("No installed package packs a catalog (restore the project, or install its npm dependencies, first).");
        foreach (TranslationDependencyCatalog dependency in resolution.Catalogs)
            result.WriteOutputLine($"{dependency.Catalog}: {dependency.Display} ({(dependency.Origin == TranslationDependencyOrigin.NuGet ? "NuGet" : "npm")})");
        foreach (TranslationOverrideTarget target in resolution.Overrides)
        {
            result.WriteOutputLine($"overrides {target.Catalog}{(target.From is null ? "" : " from " + target.From)}:");
            if (target.Problem is not null) result.WriteOutputLine("  " + target.Problem);
            if (target.Dependency is null || !target.Listed) continue;
            TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
            foreach (TranslationLibraryLocale locale in catalog.Locales)
            {
                string origin = locale.Origin == TranslationLibraryLocaleOrigin.Library ? "library" : "added by the application" + (locale.Pending ? ", no messages yet" : "");
                string[] overridden = catalog.Overrides.Where(item => item.Known && string.Equals(item.Locale, locale.Tag, StringComparison.OrdinalIgnoreCase))
                    .Select(item => string.Join(".", catalog.Message(item.Key)!.LogicalPath) + (item.Changed == true ? " (library text changed)" : "")).ToArray();
                result.WriteOutputLine($"  {locale.Tag} ({origin}{(locale.Fallback is null ? "" : ", falls back to " + locale.Fallback)}): {locale.Covered}/{locale.Messages} messages in its own text" +
                    (overridden.Length == 0 ? "" : "; overrides " + string.Join(", ", overridden)));
            }
            foreach (TranslationDiagnostic diagnostic in catalog.Diagnostics)
                result.WriteOutputLine($"  {InputFiles.DisplayPath(diagnostic.Location.Path, Path.GetFullPath(Environment.CurrentDirectory))}({diagnostic.Location.Line},{diagnostic.Location.Column}): {diagnostic.Severity.ToString().ToLowerInvariant()} {diagnostic.Id}: {diagnostic.Message}");
        }
        return Program.Success;
    }

    // The target of a catalog and the library with the application's overrides. Nothing is written: a catalog runic.json
    // does not list yet is listed by the plan (TranslationOverrideChanges.Declaring).
    private static (TranslationOverrideTarget Target, TranslationLibraryCatalog Catalog) Load(string manifest, string catalogId)
    {
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(manifest);
        TranslationOverrideTarget? target = resolution.TargetFor(catalogId);
        if (target is null)
            throw new ToolUsageException($"No installed package packs catalog '{catalogId}'." + (resolution.Catalogs.Count == 0
                ? " Restore the project (dotnet restore, npm install) first."
                : $" Installed catalogs: {string.Join(", ", resolution.Catalogs.Select(catalog => catalog.Catalog))}."));
        if (target.Dependency is null) throw new ToolUsageException(target.Problem ?? $"Catalog '{catalogId}' is not installed.");
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        if (catalog.Library.Project is null)
            throw new ToolDiagnosticException($"The library catalog '{catalogId}' ({target.Dependency.Display}) does not compile with this tool: {catalog.LibraryError?.Id} {catalog.LibraryError?.Message}");
        return (target, catalog);
    }

    // A logical key (grid.title) or a canonical key (grid_title).
    private static string KeyOf(TranslationLibraryCatalog catalog, string key)
    {
        TranslationLibraryMessage? message = catalog.Messages.FirstOrDefault(item => item.Key == key) ??
            catalog.Messages.FirstOrDefault(item => string.Join(".", item.LogicalPath) == key);
        return message?.Key ?? throw new ToolUsageException($"Catalog '{catalog.Catalog}' has no message '{key}'.");
    }

    private static string Manifest(string projectPath)
    {
        string full = Path.GetFullPath(projectPath);
        string manifest = Directory.Exists(full) ? Path.Combine(full, "runic.json") : full;
        if (!File.Exists(manifest)) throw new ToolUsageException($"no runic.json at '{projectPath}'.");
        return manifest;
    }
}
