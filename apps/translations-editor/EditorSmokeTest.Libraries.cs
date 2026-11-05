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

        // Override: seeded with the library text, then edited, changed in the library, kept and reverted.
        library = Library(await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de")).ConfigureAwait(false));
        EditorLibraryOverride german = library.Messages.Single(message => message.Key == "header_sort").Overrides.Single();
        Require(german is { Locale: "de", Text: "Sortieren nach {$column}", Changed: false, Path: "shop/translations/overrides/acme.table/de.rmf2" }, "The override was not seeded: " + german);
        library = Library(await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortiert nach {$column}", german.Revision)).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Text == "Sortiert nach {$column}" && library.Diagnostics.Count == 0,
            "The override text was not saved.");
        EditorLibraryResult stale = await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "X {$column}", german.Revision)).ConfigureAwait(false);
        Require(!stale.Ok && stale.Message?.Code == "ui_backend_document_conflict", "A save from a stale revision was not refused.");
        EditorLibraryResult invalid = await session.SaveLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de", "Sortiert nach {$col}")).ConfigureAwait(false);
        Require(invalid.Ok && invalid.Library!.Diagnostics.Any(diagnostic => diagnostic.Path.EndsWith("de.rmf2", StringComparison.Ordinal) && diagnostic.Id is "RTR0016" or "RTR0065"),
            "An input mismatch in an override is not reported as the build reports it.");
        await File.WriteAllTextAsync(Path.Combine(container, "packages", "acme.table", "1.0.0", "runic", "acme.table", "en.rmf2"),
            LibraryEnglish.Replace("Sort by {$column}", "Order by {$column}", StringComparison.Ordinal)).ConfigureAwait(false);
        library = Library(await session.LoadLibraryAsync(new EditorLibraryRequest("acme.table")).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Changed == true &&
            library.Diagnostics.Any(diagnostic => diagnostic.Id == "RTR0090"), "A changed library base text is not marked.");
        library = Library(await session.KeepLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de")).ConfigureAwait(false));
        Require(library.Messages.Single(message => message.Key == "header_sort").Overrides.Single().Changed == false, "Keep override did not record the new library text.");
        library = Library(await session.RevertLibraryOverrideAsync(new EditorLibraryOverrideRequest("acme.table", "header_sort", "de")).ConfigureAwait(false));
        Require(library.Messages.All(message => message.Overrides.Count == 0) && !File.Exists(Path.Combine(workspace, "shop", "translations", "overrides", "acme.table", "de.rmf2")),
            "Revert to library left the override behind.");

        // Add-locale on a library catalog: an app-added locale with the one fallback hop into the library.
        library = Library(await session.AddLibraryLocaleAsync(new EditorLibraryLocaleRequest("acme.table", "de-CH")).ConfigureAwait(false));
        EditorLibraryLocale swiss = library.Locales.Single(locale => locale.Tag == "de-CH");
        Require(swiss is { Origin: "application", Fallback: "de", Pending: true, Covered: 0, Messages: 3 }, "The added locale is wrong: " + swiss);
        library = Library(await session.OverrideLibraryMessageAsync(new EditorLibraryOverrideRequest("acme.table", "empty", "de-CH", "Nüt")).ConfigureAwait(false));
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
