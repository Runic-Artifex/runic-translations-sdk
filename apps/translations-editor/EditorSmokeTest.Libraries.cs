using System.Text.Json.Nodes;

namespace Runic.Translations.Editor;

internal static partial class EditorSmokeTest
{
    // L5-4, L5-5, L5-15, L5-16: a workspace with two catalogs, one of which overrides a packed library catalog (the
    // acme.table library of the build-time override tests), installed as a restore leaves it (obj/project.assets.json and a
    // NuGet package folder).
    private static async Task LibraryCatalogJourneyAsync(string container)
    {
        string workspace = Path.Combine(container, "workspace");
        WriteLibraryWorkspace(container);

        using var session = new EditorSession(workspace);
        WorkspaceSnapshot chooser = await session.LoadAsync().ConfigureAwait(false);
        Require(chooser.Catalog is null && string.Join(",", chooser.Catalogs.Select(catalog => catalog.Id).Order(StringComparer.Ordinal)) == "admin,shop" &&
            chooser.Catalogs.Single(catalog => catalog.Id == "shop").ManifestPaths.Single() == "shop/translations/runic.json",
            "A workspace with two catalogs did not offer both: " + string.Join(",", chooser.Catalogs.Select(catalog => catalog.Id)));
        EditorOperationResult opened = await session.OpenWorkspaceAsync(new EditorOpenWorkspaceRequest(workspace, "shop")).ConfigureAwait(false);
        Require(opened.Ok && opened.Snapshot?.Catalog?.Id == "shop" && opened.Snapshot.Catalogs.Any(catalog => catalog.Id == "admin"),
            "The shop catalog did not open, or the other catalog is not offered for switching.");
        EditorLibrarySummary summary = opened.Snapshot!.Libraries.Single();
        Require(summary is { Catalog: "acme.table", Package: "Acme.Table", Version: "1.0.0", Origin: "nuget", Overridden: true }, "The library catalog is not listed: " + summary);
        Require(!opened.Snapshot.Documents.Any(document => document.Path.Contains("/overrides/", StringComparison.Ordinal)), "Override sources became documents of the app catalog.");
        Require((await session.OpenWorkspaceAsync(new EditorOpenWorkspaceRequest(workspace, "admin/translations/runic.json")).ConfigureAwait(false)).Snapshot?.Catalog?.Id == "admin",
            "A catalog did not open by its runic.json path.");
        Require((await session.OpenWorkspaceAsync(new EditorOpenWorkspaceRequest(workspace, "shop")).ConfigureAwait(false)).Ok, "Switching back to the shop catalog failed.");

        EditorLibraryView library = Library(await session.LoadLibraryAsync(new EditorLibraryRequest("acme.table")).ConfigureAwait(false));
        Require(string.Join(",", library.Layers.Select(layer => $"{layer.Name}:{layer.Priority}")) == "library:0,override:1", "The library layers are not real.");
        Require(string.Join(",", library.Locales.Select(locale => $"{locale.Tag}:{locale.Origin}")) == "en:library,de:library", "Wrong library locales.");
        Require(library.Messages.Single(message => message.Key == "header_sort") is { Path: "header.sort", BaseText: "Sort by {$column}" } sort && sort.Texts["de"] == "Sortieren nach {$column}",
            "The library's base and German texts are missing.");

        // Override: written on the first Save from the locale's revision (none yet), then edited, changed in the library,
        // kept and reverted; every write is refused when the source changed since the dialog loaded it.
        string overridePath = Path.Combine(workspace, "shop", "translations", "overrides", "acme.table", "de.rmf2");
        Require(library.Locales.Single(locale => locale.Tag == "de").Revision is null, "A locale without an override source has a revision.");
        Directory.CreateDirectory(Path.GetDirectoryName(overridePath)!);
        // de.rmf2 is created elsewhere after the dialog showed de without one.
        await File.WriteAllTextAsync(overridePath, "empty = Nichts\n").ConfigureAwait(false);
        EditorLibraryResult created = await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: "")).ConfigureAwait(false);
        Require(created is { Ok: false, Conflict: true } && created.Message?.Code == "ui_backend_library_conflict" &&
            await File.ReadAllTextAsync(overridePath).ConfigureAwait(false) == "empty = Nichts\n", "An override over a source created meanwhile was written.");
        File.Delete(overridePath);
        EditorLibraryResult broken = await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortieren nach {$column", "")).ConfigureAwait(false);
        Require(broken is { Ok: false } && broken.Message?.Code == "ui_backend_library_invalid_text" && !File.Exists(overridePath), "An override with invalid text was written.");
        library = Library(await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: "")).ConfigureAwait(false));
        EditorLibraryOverride german = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single();
        Require(german is { Locale: "de", Text: "Sortieren nach {$column}", Changed: false, Path: "shop/translations/overrides/acme.table/de.rmf2" }, "The override was not seeded: " + german);
        library = Library(await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortiert nach {$column}", german.Revision)).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Text == "Sortiert nach {$column}" && library.Diagnostics.Count == 0,
            "The override text was not saved.");
        EditorLibraryResult stale = await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "X {$column}", german.Revision)).ConfigureAwait(false);
        Require(stale is { Ok: false, Conflict: true } && stale.Message?.Code == "ui_backend_library_conflict", "A save from a stale revision was not refused.");
        string saved = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Revision;
        EditorLibraryResult unparsed = await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortiert nach {$column", saved)).ConfigureAwait(false);
        Require(unparsed is { Ok: false } && unparsed.Message?.Code == "ui_backend_library_invalid_text" &&
            (await File.ReadAllTextAsync(overridePath).ConfigureAwait(false)).Contains("sort = Sortiert nach {$column}\n", StringComparison.Ordinal), "Invalid text was saved.");
        EditorLibraryResult invalid = await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortiert nach {$col}", saved)).ConfigureAwait(false);
        Require(invalid.Ok && invalid.Library!.Diagnostics.Any(diagnostic => diagnostic.Path.EndsWith("de.rmf2", StringComparison.Ordinal) && diagnostic.Id is "RTR0016" or "RTR0065"),
            "An input mismatch in an override is not reported as the build reports it.");
        await File.WriteAllTextAsync(Path.Combine(container, "packages", "acme.table", "1.0.0", "runic", "acme.table", "en.rmf2"),
            LibraryEnglish.Replace("Sort by {$column}", "Order by {$column}", StringComparison.Ordinal)).ConfigureAwait(false);
        library = Library(await session.LoadLibraryAsync(new EditorLibraryRequest("acme.table")).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Changed == true &&
            library.Diagnostics.Any(diagnostic => diagnostic.Id == "RTR0090"), "A changed library base text is not marked.");
        string shown = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Revision;
        // The override is edited elsewhere after the dialog loaded it: Keep and Revert are refused with a reload prompt.
        string onDisk = await File.ReadAllTextAsync(overridePath).ConfigureAwait(false);
        await File.WriteAllTextAsync(overridePath, onDisk + "# edited in VS Code\n").ConfigureAwait(false);
        Require((await session.KeepLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: shown)).ConfigureAwait(false)) is { Ok: false, Conflict: true },
            "Keep of an override changed elsewhere was not refused.");
        Require((await session.RevertLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: shown)).ConfigureAwait(false)) is { Ok: false, Conflict: true } &&
            File.Exists(overridePath), "Revert of an override changed elsewhere was not refused.");
        library = Library(await session.LoadLibraryAsync(new EditorLibraryRequest("acme.table")).ConfigureAwait(false));
        shown = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Revision;
        library = Library(await session.KeepLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: shown)).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Changed == false, "Keep override did not record the new library text.");
        shown = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Revision;
        library = Library(await session.RevertLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", ExpectedRevision: shown)).ConfigureAwait(false));
        Require(library.Messages.All(message => message.Overrides.Count == 0) && !File.Exists(Path.Combine(workspace, "shop", "translations", "overrides", "acme.table", "de.rmf2")),
            "Revert to library left the override behind.");

        // Add-locale on a library catalog: an app-added locale with the one fallback hop into the library.
        // The tag is canonicalized before it is added, and the result names it (de_ch → de-CH), so the dialog selects it.
        EditorLibraryResult addedLocale = await session.AddLibraryLocaleAsync(new EditorLibraryLocaleRequest("acme.table", "de_ch")).ConfigureAwait(false);
        Require(addedLocale.Locale == "de-CH", "The added locale's canonical tag was not returned: " + addedLocale.Locale);
        library = Library(addedLocale);
        Require(!(await session.AddLibraryLocaleAsync(new EditorLibraryLocaleRequest("acme.table", "DE-ch")).ConfigureAwait(false)).Ok, "A locale was added again in another spelling.");
        EditorLibraryLocale swiss = library.Locales.Single(locale => locale.Tag == "de-CH");
        Require(swiss is { Origin: "application", Fallback: "de", Pending: true, Covered: 0, Messages: 3 }, "The added locale is wrong: " + swiss);
        library = Library(await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "empty", "de-CH", "Nüt", swiss.Revision)).ConfigureAwait(false));
        Require(library.Locales.Single(locale => locale.Tag == "de-CH") is { Pending: false, Covered: 1, Overridden: 1 }, "The added locale's coverage did not count its override.");
        Require(!(await session.AddLibraryLocaleAsync(new EditorLibraryLocaleRequest("acme.table", "de")).ConfigureAwait(false)).Ok, "A library locale was added again.");

        // A catalog runic.json does not override yet: the first override lists it in "overrides".
        string manifest = Path.Combine(workspace, "admin", "translations", "runic.json");
        Require((await session.OpenWorkspaceAsync(new EditorOpenWorkspaceRequest(workspace, "admin")).ConfigureAwait(false)).Snapshot?.Libraries.Single() is { Overridden: false },
            "An installed catalog the project does not override is not offered.");
        EditorLibraryResult declared = await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "empty", "fr", "Aucune ligne")).ConfigureAwait(false);
        Require(declared is { Ok: true, ProjectChanged: true } && (await File.ReadAllTextAsync(manifest).ConfigureAwait(false)).Contains("\"overrides\": [{ \"catalog\": \"acme.table\", \"from\": \"Acme.Table\" }]", StringComparison.Ordinal),
            "The first override did not list the catalog in runic.json.");
        Require(declared.Library!.Locales.Single(locale => locale.Tag == "fr") is { Origin: "application", Fallback: "en" }, "fr does not fall back to the library's base locale.");
        // The editor's own write of runic.json is not an external change.
        EditorExternalChanges external = await session.CheckExternalChangesAsync().ConfigureAwait(false);
        Require(!external.Paths.Any(path => path.EndsWith("runic.json", StringComparison.Ordinal) || path.Contains("/overrides/", StringComparison.Ordinal)),
            "The editor's own library writes were reported as external changes: " + string.Join(", ", external.Paths));
        Require((await session.LoadAsync().ConfigureAwait(false)).Libraries.Single().Overridden, "The reloaded workspace does not see the new override entry.");

        static EditorLibraryView Library(EditorLibraryResult result)
        {
            Require(result.Ok && result.Library is not null, "The library operation failed: " + result.Message);
            return result.Library!;
        }
    }

    private const string LibraryEnglish = "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\nheader {\n  sort = Sort by {$column}\n}\n";
    private const string LibraryGerman = "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\nheader {\n  sort = Sortieren nach {$column}\n}\n";

    // container/packages: the NuGet package folder; container/workspace: shop (overrides acme.table) and admin.
    private static void WriteLibraryWorkspace(string container)
    {
        string library = Path.Combine(container, "packages", "acme.table", "1.0.0", "runic", "acme.table");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "runic.json"), """{"schemaVersion":1,"catalog":"acme.table","code":{"namespace":"Acme.Table","className":"TableText","visibility":"public"},"baseLocale":"en","locales":["en","de"]}""");
        File.WriteAllText(Path.Combine(library, "en.rmf2"), LibraryEnglish);
        File.WriteAllText(Path.Combine(library, "de.rmf2"), LibraryGerman);
        foreach ((string name, bool overrides) in new[] { ("shop", true), ("admin", false) })
        {
            string app = Path.Combine(container, "workspace", name);
            Directory.CreateDirectory(Path.Combine(app, "translations"));
            Directory.CreateDirectory(Path.Combine(app, "obj"));
            File.WriteAllText(Path.Combine(app, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
            File.WriteAllText(Path.Combine(app, "translations", "runic.json"), "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"" + name + "\",\n  \"code\": { \"namespace\": \"App\", \"className\": \"AppText\" },\n  \"baseLocale\": \"en\"" +
                (overrides ? ",\n  \"overrides\": [{ \"catalog\": \"acme.table\", \"from\": \"Acme.Table\" }]" : "") + "\n}\n");
            File.WriteAllText(Path.Combine(app, "translations", "en.rmf2"), "title = " + name + "\n");
            var assets = new JsonObject
            {
                ["version"] = 3,
                ["libraries"] = new JsonObject { ["Acme.Table/1.0.0"] = new JsonObject { ["type"] = "package", ["path"] = "acme.table/1.0.0",
                    ["files"] = new JsonArray("runic/acme.table/de.rmf2", "runic/acme.table/en.rmf2", "runic/acme.table/runic.json") } },
                ["packageFolders"] = new JsonObject { [Path.Combine(container, "packages") + Path.DirectorySeparatorChar] = new JsonObject() },
            };
            File.WriteAllText(Path.Combine(app, "obj", "project.assets.json"), assets.ToJsonString());
        }
    }
}
