using System;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Tests.Shared;
using Runic.Translations.Tooling;
using TUnit.Core;

namespace Runic.Translations.Tooling.Tests;

// Library catalogs of dependencies and the application's overrides of them (T5d: L5-4, L5-5, L5-15, L5-16), shared by the
// Translations Editor, the language server and runic-translations overrides.
internal sealed class TranslationLibraryCatalogTests
{
    [Test, DisplayName("NuGet and npm library catalogs resolve from the restore output and node_modules")]
    [Arguments(false)]
    [Arguments(true)]
    public void Resolves(bool npm)
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create(npm);
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        Require(resolution.Warnings.Count == 0, "Unexpected warnings: " + string.Join("; ", resolution.Warnings));
        TranslationDependencyCatalog catalog = resolution.Catalogs.Single();
        Require(catalog.Catalog == "acme.table" && catalog.Version == "1.0.0", "Wrong catalog: " + catalog);
        Require(catalog.Origin == (npm ? TranslationDependencyOrigin.Npm : TranslationDependencyOrigin.NuGet), "Wrong origin: " + catalog.Origin);
        Require(catalog.Package == (npm ? "@acme/table" : "Acme.Table"), "Wrong package: " + catalog.Package);
        Require(Path.GetFullPath(catalog.Root) == Path.GetFullPath(fixture.LibraryRoot), "Wrong root: " + catalog.Root);
        TranslationOverrideTarget target = resolution.Overrides.Single();
        Require(target.Dependency == catalog && target.Problem is null && target.Listed, "The override target is not resolved: " + target);
        Require(resolution.OverrideOf(Path.Combine(fixture.OverridesDirectory, "fr.rmf2")) == target, "An override source does not map to its target.");
        Require(resolution.OverrideOf(Path.Combine(fixture.ProjectDirectory, "en.rmf2")) is null, "An application source maps to an override target.");
    }

    [Test, DisplayName("an override of a catalog no installed package ships, or from another package, reports the build's problem")]
    public void UnresolvedTargets()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        File.WriteAllText(fixture.ProjectPath, """{"schemaVersion":1,"catalog":"shop","code":{"namespace":"Shop","className":"ShopText"},"baseLocale":"en","overrides":[{"catalog":"acme.table","from":"Other.Package"},{"catalog":"acme.chart","from":"Acme.Chart"}]}""");
        Directory.CreateDirectory(Path.Combine(fixture.ProjectDirectory, "overrides", "stray"));
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        Require(resolution.Override("acme.table")!.Problem!.Contains("not 'Other.Package'", StringComparison.Ordinal), "Wrong-package problem missing.");
        Require(resolution.Override("acme.chart")!.Problem!.Contains("not packed by any installed package", StringComparison.Ordinal), "Missing-package problem missing.");
        TranslationOverrideTarget stray = resolution.Override("stray")!;
        Require(!stray.Listed && stray.Problem!.Contains("not listed", StringComparison.Ordinal), "An unlisted overrides directory was not reported.");
        Require(TranslationDependencyCatalogs.Resolve(fixture.ProjectPath, Encoding.UTF8.GetBytes("""{"schemaVersion":1,"catalog":"shop","code":{"namespace":"Shop","className":"ShopText"},"baseLocale":"en"}""")).Overrides.Count == 0,
            "An unsaved runic.json without overrides still lists targets.");
    }

    [Test, DisplayName("the library view shows the contract, base texts, library and app-added locales with coverage and the one fallback hop")]
    public void LibraryView()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        fixture.WriteOverride("de", "empty = Nichts da\n");
        fixture.WriteOverride("de-CH", "empty = Nüt\n");
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single());
        Require(catalog.BaseLocale == "en" && catalog.LibraryError is null, "Wrong base locale or library error.");
        Require(string.Join(",", catalog.Messages.Select(static message => message.Key)) == "empty,header_sort,rows", "Wrong keys: " + string.Join(",", catalog.Messages.Select(static message => message.Key)));
        TranslationLibraryMessage sort = catalog.Message("header_sort")!;
        Require(string.Join(".", sort.LogicalPath) == "header.sort" && sort.Inputs.Single() == "$column: string", "Wrong contract: " + string.Join(",", sort.Inputs));
        Require(sort.BaseText == "Sort by {$column}" && sort.Texts["de"] == "Sortieren nach {$column}" && sort.Fingerprint is not null, "Wrong library texts.");
        Require(sort.Locations["en"].Path.EndsWith("/runic/acme.table/en.rmf2", StringComparison.Ordinal), "Wrong library location: " + sort.Locations["en"].Path);
        Require(string.Join(",", catalog.Locales.Select(static locale => $"{locale.Tag}:{locale.Origin}:{locale.Fallback}:{locale.Covered}/{locale.Messages}")) ==
            "en:Library::3/3,de:Library:en:3/3,de-CH:Application:de:1/3", "Wrong locales: " + string.Join(",", catalog.Locales));
        TranslationLibraryOverride german = catalog.Override("empty", "de")!;
        Require(german.Text == "Nichts da" && german.Known && german.Changed is null, "Wrong override: " + german);
        Require(catalog.Diagnostics.Count == 0, "Unexpected diagnostics: " + string.Join("; ", catalog.Diagnostics.Select(static d => d.Id + " " + d.Message)));
        Require(catalog.Overlay.Success && catalog.Overlay.Project!.OverlayAddedLocales.SequenceEqual(["de-CH"]), "The overlay does not add de-CH.");
    }

    [Test, DisplayName("override diagnostics match the build: unknown keys (RTR0103) and changed inputs, located in the override source")]
    public void Diagnostics()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        string path = fixture.WriteOverride("fr", "emtpy = Rien\nheader {\n  sort = Trier par {$col}\n}\n");
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single());
        TranslationDiagnostic unknown = catalog.Diagnostics.Single(static diagnostic => diagnostic.Id == "RTR0103");
        Require(unknown.Message.Contains("no message 'emtpy'", StringComparison.Ordinal) && unknown.Location.Path == new TranslationSource(path, []).Path, "Wrong RTR0103: " + unknown.Message);
        Require(catalog.Diagnostics.Any(static diagnostic => diagnostic.Id is "RTR0016" or "RTR0065" && diagnostic.Message.Contains("col", StringComparison.Ordinal)),
            "No input mismatch: " + string.Join("; ", catalog.Diagnostics.Select(static d => d.Id + " " + d.Message)));
        Require(!catalog.Override("emtpy", "fr")!.Known, "An unknown key is reported as known.");
        // An unsaved buffer replaces the file.
        TranslationLibraryCatalog fixedCatalog = TranslationLibraryCatalog.Load(TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single(),
            new System.Collections.Generic.Dictionary<string, byte[]> { [path] = Encoding.UTF8.GetBytes("empty = Rien\n") });
        Require(fixedCatalog.Diagnostics.Count == 0 && fixedCatalog.Override("empty", "fr") is not null, "A buffer did not replace the override source.");
    }

    [Test, DisplayName("override, changed library text, keep and revert to library")]
    public void OverrideLifecycle()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        TranslationOverridePlan plan = TranslationOverrideChanges.Override(catalog, ["header_sort"], "de");
        Require(plan.Creates && plan.AddedLocaleFallback is null, "A library locale was reported as added.");
        TranslationOverrideChanges.Apply(plan, target);
        string written = File.ReadAllText(fixture.OverridePath("de"));
        Require(written.Contains("header {\n", StringComparison.Ordinal) && written.Contains("  sort = Sortieren nach {$column}\n", StringComparison.Ordinal) &&
            written.Contains("# Acme.Table 1.0.0, base locale en:", StringComparison.Ordinal) && written.Contains("#   Sort by {$column}", StringComparison.Ordinal),
            "The override is not seeded with the library text and base comments:\n" + written);
        Require(File.Exists(target.ReviewPath), "No review entry records the library text.");
        catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.Override("header_sort", "de")!.Changed == false && catalog.Diagnostics.Count == 0, "A fresh override is marked changed.");
        Require(Throws<ArgumentException>(() => TranslationOverrideChanges.Override(catalog, ["header_sort"], "de")), "A second override of one key was planned.");

        // The library updates its base text of an overridden key.
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(catalog, ["empty"], "fr", "Aucune ligne"), target);
        fixture.ChangeLibraryEmptyText("Nothing here");
        catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.Override("empty", "fr")!.Changed == true && catalog.Override("header_sort", "de")!.Changed == false, "The library change is not marked.");
        Require(catalog.Diagnostics.Single().Id == "RTR0090", "No changed-library diagnostic.");
        TranslationOverrideChanges.Apply(new TranslationOverridePlan(fixture.OverridePath("fr"), File.ReadAllBytes(fixture.OverridePath("fr")), TranslationOverrideChanges.Keep(catalog, ["empty"], "fr")), target);
        Require(TranslationLibraryCatalog.Load(target).Override("empty", "fr")!.Changed == false, "Keep override did not record the new library text.");

        // Revert to library: the message, its comments and its now-empty group go; the last override deletes the file.
        catalog = TranslationLibraryCatalog.Load(target);
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Revert(catalog, "header_sort", "de"), target);
        Require(!File.Exists(fixture.OverridePath("de")), "The emptied override source was kept:\n" + (File.Exists(fixture.OverridePath("de")) ? File.ReadAllText(fixture.OverridePath("de")) : ""));
        catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.Override("header_sort", "de") is null && catalog.Review!.Entries.All(static entry => entry.Locale != "de"), "The review entry of a reverted override was kept.");
    }

    [Test, DisplayName("scaffolding an app-added locale writes commented templates and reports the one fallback hop")]
    public void Scaffold()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        TranslationOverridePlan plan = TranslationOverrideChanges.Scaffold(catalog, "de-ch");
        Require(plan.AddedLocaleFallback == "de" && plan.Path == fixture.OverridePath("de-CH"), "Wrong scaffold: " + plan.Path + " → " + plan.AddedLocaleFallback);
        TranslationOverrideChanges.Apply(plan, target);
        string text = File.ReadAllText(plan.Path);
        Require(text.Contains("# header {\n#   sort = Sortieren nach {$column}\n# }", StringComparison.Ordinal) && text.Contains("falls back to de", StringComparison.Ordinal), "Wrong templates:\n" + text);
        catalog = TranslationLibraryCatalog.Load(target);
        TranslationLibraryLocale added = catalog.Locales.Single(static locale => locale.Tag == "de-CH");
        Require(added.Origin == TranslationLibraryLocaleOrigin.Application && added.Pending && added.Fallback == "de", "The scaffolded locale is not a pending app-added locale: " + added);
        Require(TranslationOverrideChanges.Scaffold(catalog, "fr").AddedLocaleFallback == "en", "fr does not fall back to the base locale.");
        Require(Throws<ArgumentException>(() => TranslationOverrideChanges.Scaffold(catalog, "de-CH")), "An existing override source was scaffolded again.");
        // Overriding into the scaffold keeps the templates and adds the message.
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(catalog, ["empty"], "de-CH"), target);
        catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.Override("empty", "de-CH")!.Text == "Keine Zeilen" && !catalog.Locales.Single(static locale => locale.Tag == "de-CH").Pending, "The added locale has no override.");
        Require(catalog.Overlay.Success, "The overlay does not compile: " + string.Join("; ", catalog.Overlay.Diagnostics.Select(static d => d.Message)));
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
