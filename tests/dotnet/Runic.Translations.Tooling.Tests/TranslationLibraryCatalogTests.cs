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
        TranslationOverridePlan keep = TranslationOverrideChanges.Keep(catalog, ["empty"], "fr");
        Require(keep.KeepsSource && keep.Review is not null, "Keep override writes the override source.");
        TranslationOverrideChanges.Apply(keep, target);
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

    [Test, DisplayName("an unreadable override review file is reported (RTR0091) and overrides, keeps and reverts are refused")]
    public void UnreadableReviewFile()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "de"), target);
        // A merge left conflict markers in the review file.
        string conflicted = "<<<<<<< HEAD\n" + File.ReadAllText(target.ReviewPath) + "=======\n>>>>>>> theirs\n";
        File.WriteAllText(target.ReviewPath, conflicted);
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.ReviewException is not null && catalog.Diagnostics.Any(static diagnostic => diagnostic.Id == "RTR0091"), "The unreadable review file is not reported.");
        Require(Throws<InvalidOperationException>(() => TranslationOverrideChanges.Override(catalog, ["rows"], "de")), "An override was planned over an unreadable review file.");
        Require(Throws<InvalidOperationException>(() => TranslationOverrideChanges.Keep(catalog, ["empty"], "de")), "A keep was planned over an unreadable review file.");
        Require(Throws<InvalidOperationException>(() => TranslationOverrideChanges.Revert(catalog, "empty", "de")), "A revert was planned over an unreadable review file.");
        Require(File.ReadAllText(target.ReviewPath) == conflicted, "The unreadable review file was replaced.");
    }

    [Test, DisplayName("override writes are compare-and-replace: a changed source refuses, another writer's review entries are kept")]
    public void Concurrency()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "de"), target);
        TranslationLibraryCatalog stale = TranslationLibraryCatalog.Load(target);
        TranslationOverridePlan plan = TranslationOverrideChanges.Override(stale, ["rows"], "de");
        // Another writer (the editor, the CLI) overrides fr and edits de.rmf2 after the plan was computed.
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "fr"), target);
        string review = File.ReadAllText(target.ReviewPath);
        File.AppendAllText(fixture.OverridePath("de"), "# edited elsewhere\n");
        string edited = File.ReadAllText(fixture.OverridePath("de"));
        Require(Throws<TranslationOverrideConflictException>(() => TranslationOverrideChanges.Apply(plan, target)), "A plan over a changed override source was written.");
        Require(File.ReadAllText(fixture.OverridePath("de")) == edited && File.ReadAllText(target.ReviewPath) == review, "A refused plan wrote a file.");
        // A plan whose source is unchanged merges its review entry with the other writer's.
        TranslationOverridePlan german = TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["rows"], "de");
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["header_sort"], "fr"), target);
        TranslationOverrideChanges.Apply(german, target);
        string[] entries = TranslationLibraryCatalog.Load(target).Review!.Entries.Select(static entry => entry.Key + "/" + entry.Locale).Order(StringComparer.Ordinal).ToArray();
        Require(string.Join(",", entries) == "empty/de,empty/fr,header_sort/fr,rows/de", "Another writer's review entries were dropped: " + string.Join(",", entries));
        // A revert computed before the override was edited elsewhere is refused.
        TranslationOverridePlan revert = TranslationOverrideChanges.Revert(TranslationLibraryCatalog.Load(target), "empty", "fr");
        File.AppendAllText(fixture.OverridePath("fr"), "# edited elsewhere\n");
        Require(Throws<TranslationOverrideConflictException>(() => TranslationOverrideChanges.Apply(revert, target)), "A revert of a changed override source was written.");
        Require(TranslationLibraryCatalog.Load(target).Override("empty", "fr") is not null, "The refused revert removed the override.");
    }

    [Test, DisplayName("an added locale falls back as the compiler compiles it: a pending (empty) added locale is not a fallback target (pt, pt-BR)")]
    public void PendingAddedLocaleFallback()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Scaffold(TranslationLibraryCatalog.Load(target), "pt"), target);
        TranslationOverridePlan brazil = TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "pt-BR");
        Require(brazil.AddedLocaleFallback == "en", "pt-BR falls back to the pending pt: " + brazil.AddedLocaleFallback);
        TranslationOverrideChanges.Apply(brazil, target);
        Require(File.ReadAllText(fixture.OverridePath("pt-BR")).Contains("it falls back to en.", StringComparison.Ordinal), "The header names another fallback:\n" + File.ReadAllText(fixture.OverridePath("pt-BR")));
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        RequireSameFallbacks(catalog, ("pt-BR", "en"));
        TranslationLibraryLocale pending = catalog.Locales.Single(static locale => locale.Tag == "pt");
        Require(pending.Pending && pending.Fallback == "en" && !catalog.Overlay.Project!.OverlayAddedLocales.Contains("pt"), "Wrong pending locale: " + pending);
        // With its first message pt is added, and pt-BR falls back to it in Tooling and in the compiled catalog alike.
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(catalog, ["empty"], "pt", "Nenhuma linha"), target);
        catalog = TranslationLibraryCatalog.Load(target);
        RequireSameFallbacks(catalog, ("pt", "en"), ("pt-BR", "pt"));
        // A source with only an unknown key does not add its locale either.
        fixture.WriteOverride("pt-PT", "emtpy = Nada\n");
        catalog = TranslationLibraryCatalog.Load(target);
        // (The build fails on the unknown key, RTR0103, so only Tooling's view is compared here.)
        Require(catalog.Locales.Single(static locale => locale.Tag == "pt-PT").Pending && catalog.Locales.Single(static locale => locale.Tag == "pt-BR").Fallback == "pt",
            "A source with only an unknown key added its locale.");

        static void RequireSameFallbacks(TranslationLibraryCatalog catalog, params (string Tag, string Fallback)[] expected)
        {
            foreach ((string tag, string fallback) in expected)
            {
                string? tooling = catalog.Locales.Single(locale => locale.Tag == tag).Fallback;
                string? compiled = catalog.Overlay.Project!.Locales.Single(locale => locale.Tag == tag).FallbackTag;
                Require(tooling == fallback && compiled == fallback, $"{tag}: Tooling falls back to {tooling}, the compiled catalog to {compiled}, expected {fallback}.");
            }
        }
    }

    [Test, DisplayName("an override seeded from the library's locale text is marked when the library changes that text, not only the base text")]
    public void LibraryLocaleTextChange()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Overrides.Single();
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(catalog, ["empty"], "de"), target);
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "de-CH"), target);
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Override(TranslationLibraryCatalog.Load(target), ["empty"], "fr", "Aucune ligne"), target);
        Require(TranslationLibraryCatalog.Load(target).Overrides.All(static item => item.Changed == false), "A fresh override is marked changed.");
        // The library fixes its German text: de and de-CH (seeded from de) are marked, fr is not.
        File.WriteAllText(Path.Combine(fixture.LibraryRoot, "de.rmf2"), PackedLibraryFixture.LibraryGerman.Replace("empty = Keine Zeilen", "empty = Keine Einträge", StringComparison.Ordinal));
        catalog = TranslationLibraryCatalog.Load(target);
        Require(catalog.Override("empty", "de")!.Changed == true && catalog.Override("empty", "de-CH")!.Changed == true && catalog.Override("empty", "fr")!.Changed == false,
            "Wrong change marks: " + string.Join(", ", catalog.Overrides.Select(static item => item.Locale + "=" + item.Changed)));
        Require(catalog.Diagnostics.Count(static diagnostic => diagnostic.Id == "RTR0090") == 2, "Wrong RTR0090 count: " + string.Join("; ", catalog.Diagnostics.Select(static d => d.Id + " " + d.Message)));
        Require(TranslationLibraryCatalog.OverrideReviewDiagnostics(fixture.ProjectPath).Count(static diagnostic => diagnostic.Id == "RTR0090") == 2, "validate's override diagnostics differ.");
        TranslationOverrideChanges.Apply(TranslationOverrideChanges.Keep(catalog, ["empty"], "de"), target);
        Require(TranslationLibraryCatalog.Load(target).Override("empty", "de")!.Changed == false, "Keep did not record the library's new German text.");
    }

    [Test, DisplayName("a plan that lists the catalog in runic.json writes runic.json, the source and the review file together or not at all")]
    public void DeclaringPlan()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        string unlisted = """{"schemaVersion":1,"catalog":"shop","code":{"namespace":"Shop","className":"ShopText"},"baseLocale":"en","overrides":[]}""";
        File.WriteAllText(fixture.ProjectPath, unlisted);
        TranslationOverrideTarget target = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).TargetFor(PackedLibraryFixture.Catalog)!;
        Require(!target.Listed, "The catalog is listed.");
        TranslationLibraryCatalog catalog = TranslationLibraryCatalog.Load(target);
        Require(Throws<ArgumentException>(() => TranslationOverrideChanges.Override(catalog, ["titel"], "de")), "An unknown key was planned.");
        Require(File.ReadAllText(fixture.ProjectPath) == unlisted, "A refused request changed runic.json.");
        // The review file becomes unreadable between planning and writing: nothing stays written.
        TranslationOverridePlan plan = TranslationOverrideChanges.Declaring(TranslationOverrideChanges.Override(catalog, ["empty"], "de"), fixture.ProjectPath, target);
        Require(plan.ProjectBytes is not null, "The plan does not list the catalog.");
        Directory.CreateDirectory(target.Directory);
        File.WriteAllText(target.ReviewPath, "not json");
        Require(Throws<Runic.Translations.Tooling.TranslationInterchangeException>(() => TranslationOverrideChanges.Apply(plan, target)), "The plan was written over an unreadable review file.");
        Require(File.ReadAllText(fixture.ProjectPath) == unlisted && !File.Exists(fixture.OverridePath("de")), "A failed plan left runic.json or the override source written.");
        File.Delete(target.ReviewPath);
        TranslationOverrideChanges.Apply(plan, target);
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        Require(resolution.Override(PackedLibraryFixture.Catalog)!.Listed && File.Exists(fixture.OverridePath("de")) && File.Exists(target.ReviewPath), "The plan was not written whole.");
    }

    [Test, DisplayName("npm discovery reports what the Vite plugin rejects: two versions of a catalog, a reserved or invalid ID, no exported manifest, Plug'n'Play")]
    public void NpmDiscoveryParity()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create(npm: true);
        string app = Path.GetDirectoryName(fixture.ProjectDirectory)!;
        string nested = Path.Combine(app, "node_modules", "@acme", "grid", "node_modules", "@acme", "table");
        // @acme/grid depends on another version of @acme/table, installed nested.
        Directory.CreateDirectory(Path.Combine(app, "node_modules", "@acme", "grid"));
        // A library is followed only when it ships a catalog itself, as the Vite plugin does.
        File.WriteAllText(Path.Combine(app, "node_modules", "@acme", "grid", "manifest.json"), "{}");
        Directory.CreateDirectory(Path.Combine(app, "node_modules", "@acme", "grid", "runic"));
        File.WriteAllText(Path.Combine(app, "node_modules", "@acme", "grid", "runic", "runic.json"), "{}");
        File.WriteAllText(Path.Combine(app, "node_modules", "@acme", "grid", "package.json"),
            """{"name":"@acme/grid","version":"1.0.0","exports":{"./runic-translations":"./manifest.json"},"runicTranslations":{"catalog":"acme.grid","project":"./runic/runic.json"},"dependencies":{"@acme/table":"2.0.0","@acme/missing":"1.0.0"}}""");
        File.WriteAllText(Path.Combine(app, "package.json"), """{"name":"shop","private":true,"dependencies":{"@acme/table":"1.0.0","@acme/grid":"1.0.0","bad-id":"1.0.0","runic-fake":"1.0.0","no-export":"1.0.0"}}""");
        CopyDirectory(Path.Combine(app, "node_modules", "@acme", "table"), nested);
        File.WriteAllText(Path.Combine(nested, "package.json"), File.ReadAllText(Path.Combine(nested, "package.json")).Replace("\"1.0.0\"", "\"2.0.0\"", StringComparison.Ordinal));
        WritePackage("bad-id", "Acme_Bad", true);
        WritePackage("runic-fake", "runic-command-line", true);
        WritePackage("no-export", "acme.noexport", false);
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        string warnings = string.Join("\n", resolution.Warnings);
        Require(resolution.Catalogs.Select(static catalog => catalog.Catalog).SequenceEqual(["acme.grid", "acme.table"]), "Wrong catalogs: " + string.Join(",", resolution.Catalogs.Select(static c => c.Catalog)));
        Require(warnings.Contains("installed in two versions", StringComparison.Ordinal) && resolution.Override("acme.table")!.Problem!.Contains("two versions", StringComparison.Ordinal),
            "Two versions of a catalog are not reported: " + warnings);
        Require(warnings.Contains("'Acme_Bad' is not a catalog ID", StringComparison.Ordinal) && warnings.Contains("reserved for the Runic packages (RTR0106)", StringComparison.Ordinal) &&
            warnings.Contains("does not export './runic-translations'", StringComparison.Ordinal) && warnings.Contains("Dependency '@acme/missing' of '@acme/grid' is not installed", StringComparison.Ordinal),
            "Missing parity warnings:\n" + warnings);
        // Yarn Plug'n'Play: no node_modules, a .pnp.cjs.
        Directory.Delete(Path.Combine(app, "node_modules"), recursive: true);
        File.WriteAllText(Path.Combine(app, ".pnp.cjs"), "");
        resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        Require(resolution.Catalogs.Count == 0 && resolution.Warnings.Any(static warning => warning.Contains("Plug'n'Play", StringComparison.Ordinal)), "Plug'n'Play is not reported.");

        void WritePackage(string name, string catalog, bool export)
        {
            string root = Path.Combine(app, "node_modules", name);
            Directory.CreateDirectory(Path.Combine(root, "runic"));
            File.WriteAllText(Path.Combine(root, "runic", "runic.json"), "{}");
            File.WriteAllText(Path.Combine(root, "manifest.json"), "{}");
            string exports = export ? "\"exports\":{\"./runic-translations\":\"./manifest.json\"}," : "";
            File.WriteAllText(Path.Combine(root, "package.json"), "{\"name\":\"" + name + "\",\"version\":\"1.0.0\"," + exports + "\"runicTranslations\":{\"catalog\":\"" + catalog + "\",\"project\":\"./runic/runic.json\"}}");
        }

        static void CopyDirectory(string from, string to)
        {
            foreach (string directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
            Directory.CreateDirectory(to);
            foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories)) File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    [Test, DisplayName("NuGet discovery leaves out a package whose build assets the restore excludes, as the build does")]
    public void NuGetExcludedBuildAssets()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        string assets = Path.Combine(Path.GetDirectoryName(fixture.ProjectDirectory)!, "obj", "project.assets.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        json["targets"] = new System.Text.Json.Nodes.JsonObject { ["net10.0"] = new System.Text.Json.Nodes.JsonObject { ["Acme.Table/1.0.0"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "package" } } };
        File.WriteAllText(assets, json.ToJsonString());
        TranslationDependencyResolution resolution = TranslationDependencyCatalogs.Resolve(fixture.ProjectPath);
        Require(resolution.Catalogs.Count == 0 && resolution.Warnings.Single().Contains("excludes its build assets", StringComparison.Ordinal), "An excluded catalog was found: " + string.Join("; ", resolution.Warnings));
        json["targets"]!["net10.0"]!["Acme.Table/1.0.0"]!["build"] = new System.Text.Json.Nodes.JsonObject { ["buildTransitive/Acme.Table.props"] = new System.Text.Json.Nodes.JsonObject() };
        File.WriteAllText(assets, json.ToJsonString());
        Require(TranslationDependencyCatalogs.Resolve(fixture.ProjectPath).Catalogs.Count == 1, "An imported catalog was left out.");
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
