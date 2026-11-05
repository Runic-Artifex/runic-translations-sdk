using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Runic.Translations.Tests.Shared;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations overrides add|list and the language server on override sources of packed library catalogs (T5d: L5-17),
// on the acme.table library of the build-time override tests, installed as a restored NuGet package or in node_modules.
internal sealed class LibraryOverrideTests
{
    private static ProcessResult Tool(PackedLibraryFixture fixture, params string[] arguments) =>
        Processes.DotNet(Path.Combine(fixture.Root, "app"), [RepositoryPaths.ToolAssembly, .. arguments]);

    [Test, DisplayName("CLI overrides add scaffolds templates, seeds keys with the library text and records it; overrides list reports locales")]
    [Arguments(false)]
    [Arguments(true)]
    public void OverridesAdd(bool npm)
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create(npm);
        ProcessResult scaffold = Tool(fixture, "overrides", "add", "acme.table", "--locale", "fr", "--project", "translations");
        Assert.Equal(0, scaffold.ExitCode, scaffold.Combined);
        Assert.Contains("falling back to en", scaffold.StandardOutput);
        string french = File.ReadAllText(fixture.OverridePath("fr"));
        Assert.Contains("# empty = No rows", french);
        Assert.Contains("#   sort = Sort by {$column}", french);

        ProcessResult key = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "header.sort", "--key", "empty", "--project", "translations");
        Assert.Equal(0, key.ExitCode, key.Combined);
        string german = File.ReadAllText(fixture.OverridePath("de"));
        Assert.Contains("  sort = Sortieren nach {$column}", german);
        Assert.Contains("empty = Keine Zeilen", german);
        Assert.Contains("#   Sort by {$column}", german);
        Assert.True(File.Exists(Path.Combine(fixture.OverridesDirectory, "acme.table.review.json")), "The library base text was not recorded.");

        ProcessResult again = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "empty", "--project", "translations");
        Assert.Equal(2, again.ExitCode, again.Combined);
        Assert.Contains("already overridden", again.Combined);
        ProcessResult unknown = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "emtpy", "--project", "translations");
        Assert.Equal(2, unknown.ExitCode, unknown.Combined);
        Assert.Contains("has no message 'emtpy'", unknown.Combined);

        fixture.ChangeLibraryEmptyText("Nothing here");
        ProcessResult list = Tool(fixture, "overrides", "list", "--project", "translations");
        Assert.Equal(0, list.ExitCode, list.Combined);
        Assert.Contains($"acme.table: {(npm ? "@acme/table@1.0.0 (npm)" : "Acme.Table 1.0.0 (NuGet)")}", list.StandardOutput);
        Assert.Contains("de (library, falls back to en): 3/3 messages in its own text; overrides header.sort, empty (library text changed)", list.StandardOutput);
        Assert.Contains("fr (added by the application, no messages yet, falls back to en): 0/3", list.StandardOutput);

        // validate and status report overrides whose library text changed (RTR0090), so CI can gate them.
        ProcessResult validate = Tool(fixture, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        Assert.Contains("RTR0090", validate.Combined);
        Assert.Contains("acme.table/de.rmf2", validate.Combined);
        ProcessResult status = Tool(fixture, "status", "--project", "translations");
        Assert.Equal(0, status.ExitCode, status.Combined);
        Assert.Contains("RTR0090", status.Combined);
    }

    [Test, DisplayName("CLI overrides add lists an installed catalog in runic.json overrides and refuses one no package ships")]
    public void OverridesAddDeclares()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        File.WriteAllText(fixture.ProjectPath, "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"shop\",\n  \"code\": { \"namespace\": \"Shop\", \"className\": \"ShopText\" },\n  \"baseLocale\": \"en\"\n}\n");
        string original = File.ReadAllText(fixture.ProjectPath);
        // A request that fails (an unknown key, an invalid locale) writes nothing, runic.json included.
        ProcessResult wrongKey = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "titel", "--project", "translations");
        Assert.Equal(2, wrongKey.ExitCode, wrongKey.Combined);
        ProcessResult wrongLocale = Tool(fixture, "overrides", "add", "acme.table", "--locale", "not a locale", "--key", "empty", "--project", "translations");
        Assert.Equal(2, wrongLocale.ExitCode, wrongLocale.Combined);
        Assert.Equal(original, File.ReadAllText(fixture.ProjectPath));
        Assert.False(Directory.Exists(fixture.OverridesDirectory), "A failed request created the overrides directory.");
        ProcessResult dry = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "empty", "--dry-run", "--project", "translations");
        Assert.Equal(0, dry.ExitCode, dry.Combined);
        Assert.Contains("would add { \"catalog\": \"acme.table\", \"from\": \"Acme.Table\" }", dry.StandardOutput);
        Assert.False(File.ReadAllText(fixture.ProjectPath).Contains("overrides", StringComparison.Ordinal), "A dry run changed runic.json.");
        Assert.False(File.Exists(fixture.OverridePath("de")), "A dry run wrote an override.");
        ProcessResult added = Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "empty", "--project", "translations");
        Assert.Equal(0, added.ExitCode, added.Combined);
        Assert.Equal("{\n  \"schemaVersion\": 1,\n  \"catalog\": \"shop\",\n  \"code\": { \"namespace\": \"Shop\", \"className\": \"ShopText\" },\n  \"baseLocale\": \"en\",\n  \"overrides\": [{ \"catalog\": \"acme.table\", \"from\": \"Acme.Table\" }]\n}\n",
            File.ReadAllText(fixture.ProjectPath));
        ProcessResult validate = Tool(fixture, "validate", "--project", "translations");
        Assert.Equal(0, validate.ExitCode, validate.Combined);
        ProcessResult missing = Tool(fixture, "overrides", "add", "acme.chart", "--locale", "de", "--project", "translations");
        Assert.Equal(2, missing.ExitCode, missing.Combined);
        Assert.Contains("No installed package packs catalog 'acme.chart'", missing.Combined);
    }

    [Test, DisplayName("LSP override sources: build diagnostics, library completion, hover with the base text, definition into a read-only library document")]
    public void LanguageServer()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        const string text = "emtpy = Rien\nheader {\n  sort = Trier par {$col}\n}\n";
        string path = fixture.WriteOverride("fr", text), uri = new Uri(path).AbsoluteUri;
        fixture.WriteOverride("de", "empty = Nichts\n");
        using var session = new Rmf2DiagnosticLspTests.Session(fixture.Root);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(fixture.Root).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text, ["version"] = 1 } });
        JsonNode info = session.Request("runic/documentInfo", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } })["result"]!;
        Assert.Equal("acme.table", info["catalog"]!.GetValue<string>());
        Assert.Equal("Acme.Table 1.0.0", info["library"]!.GetValue<string>());
        Assert.Equal("fr", info["locale"]!.GetValue<string>());
        JsonArray diagnostics = session.Publications.Last(frame => frame["params"]?["uri"]?.GetValue<string>() == uri)["params"]!["diagnostics"]!.AsArray();
        string codes = string.Join(",", diagnostics.Select(item => item!["code"]!.GetValue<string>()).Order(StringComparer.Ordinal));
        Assert.True(diagnostics.Any(item => item!["code"]!.GetValue<string>() == "RTR0103" && item["message"]!.GetValue<string>().Contains("emtpy", StringComparison.Ordinal) &&
            item["range"]!["start"]!["line"]!.GetValue<int>() == 0), "No RTR0103 at the unknown key: " + diagnostics.ToJsonString());
        Assert.True(diagnostics.Any(item => item!["code"]!.GetValue<string>() is "RTR0016" or "RTR0065" && item["range"]!["start"]!["line"]!.GetValue<int>() == 2), "No input mismatch: " + codes);
        // The application catalog itself stays clean: its sources do not include the overrides.
        Assert.False(session.Publications.Any(frame => frame["params"]?["uri"]?.GetValue<string>()?.EndsWith("/translations/en.rmf2", StringComparison.Ordinal) == true &&
            frame["params"]!["diagnostics"]!.AsArray().Count != 0), "The application's own catalog reports override diagnostics.");

        JsonArray top = session.Request("textDocument/completion", Position(uri, 0, 0))["result"]!.AsArray();
        Assert.Equal("empty,header,rows", string.Join(",", top.Select(item => item!["label"]!.GetValue<string>())));
        Assert.Equal("Library: No rows", top.Single(item => item!["label"]!.GetValue<string>() == "empty")!["detail"]!.GetValue<string>());
        JsonArray nested = session.Request("textDocument/completion", Position(uri, 2, 3))["result"]!.AsArray();
        Assert.Equal("sort", string.Join(",", nested.Select(item => item!["label"]!.GetValue<string>())));
        JsonArray inputs = session.Request("textDocument/completion", Position(uri, 2, 20))["result"]!.AsArray();
        Assert.True(inputs.Any(item => item!["label"]!.GetValue<string>() == "$column"), "The library's inputs are not offered: " + inputs.ToJsonString());

        string hover = session.Request("textDocument/hover", Position(uri, 2, 3))["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("header.sort: override of library catalog acme.table (Acme.Table 1.0.0)", hover);
        Assert.Contains("Base text (en): Sort by {$column}", hover);
        Assert.Contains("Locale: fr, added by the application; falls back to en", hover);
        Assert.Contains("Inputs: $column: string", hover);

        JsonArray definition = session.Request("textDocument/definition", Position(uri, 2, 3))["result"]!.AsArray();
        string libraryUri = definition.Single()!["uri"]!.GetValue<string>();
        Assert.True(libraryUri.StartsWith("runic-library:/acme.table/en.rmf2?project=", StringComparison.Ordinal), libraryUri);
        Assert.Equal(7, definition.Single()!["range"]!["start"]!["line"]!.GetValue<int>());
        JsonNode document = session.Request("runic/libraryDocument", new JsonObject { ["uri"] = libraryUri })["result"]!;
        Assert.Equal(PackedLibraryFixture.LibraryEnglish, document["text"]!.GetValue<string>());
        JsonNode escape = session.Request("runic/libraryDocument", new JsonObject { ["uri"] = libraryUri.Replace("/en.rmf2?", "/..%2F..%2F..%2F..%2Fapp%2Ftranslations%2Fen.rmf2?", StringComparison.Ordinal) });
        Assert.True(escape["error"] is not null, "A library document outside the packed catalog was read.");

        JsonNode catalogs = session.Request("runic/libraryCatalogs", new JsonObject { ["uri"] = new Uri(fixture.ProjectPath).AbsoluteUri })["result"]!;
        JsonNode catalog = catalogs["catalogs"]!.AsArray().Single()!;
        Assert.True(catalog["overridden"]!.GetValue<bool>(), "The overridden catalog is not reported as overridden: " + catalog.ToJsonString());
        Assert.Equal("en,de,fr", string.Join(",", catalog["locales"]!.AsArray().Select(locale => locale!["tag"]!.GetValue<string>())));
        Assert.Equal("de", string.Join(",", catalog["messages"]!.AsArray().Single(message => message!["key"]!.GetValue<string>() == "empty")!["overridden"]!.AsArray().Select(locale => locale!.GetValue<string>())));

        // A library update that changes an overridden base text is marked (RTR0090) once the override records it.
        Assert.Equal(0, Tool(fixture, "overrides", "add", "acme.table", "--locale", "de", "--key", "rows", "--project", "translations").ExitCode);
        fixture.ChangeLibraryEmptyText("Nothing here");
        string reviewPath = Path.Combine(fixture.OverridesDirectory, "acme.table.review.json");
        File.WriteAllText(reviewPath, File.ReadAllText(reviewPath).Replace("\"key\": \"rows\"", "\"key\": \"empty\"", StringComparison.Ordinal));
        string deUri = new Uri(fixture.OverridePath("de")).AbsoluteUri;
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri, ["text"] = File.ReadAllText(fixture.OverridePath("de")), ["version"] = 1 } });
        session.Request("runic/documentInfo", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = deUri } });
        JsonArray german = session.Publications.Last(frame => frame["params"]?["uri"]?.GetValue<string>() == deUri)["params"]!["diagnostics"]!.AsArray();
        Assert.True(german.Any(item => item!["code"]!.GetValue<string>() == "RTR0090" && item["message"]!.GetValue<string>().Contains("changed since this override", StringComparison.Ordinal)),
            "No changed-library warning: " + german.ToJsonString());
    }

    [Test, DisplayName("LSP override sources of a catalog no restored package ships report RTR0104 and keep syntax diagnostics")]
    public void LanguageServerWithoutRestore()
    {
        using PackedLibraryFixture fixture = PackedLibraryFixture.Create();
        File.Delete(Path.Combine(fixture.Root, "app", "obj", "project.assets.json"));
        const string text = "empty = Rien\n";
        string uri = new Uri(fixture.WriteOverride("fr", text)).AbsoluteUri;
        using var session = new Rmf2DiagnosticLspTests.Session(fixture.Root);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(fixture.Root).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["text"] = text, ["version"] = 1 } });
        string hover = session.Request("textDocument/hover", Position(uri, 0, 1))["result"]!["contents"]!["value"]!.GetValue<string>();
        Assert.Contains("not packed by any installed package", hover);
        JsonNode diagnostic = session.Publications.Last(frame => frame["params"]?["uri"]?.GetValue<string>() == uri)["params"]!["diagnostics"]!.AsArray().Single()!;
        Assert.Equal("RTR0104", diagnostic["code"]!.GetValue<string>());
        Assert.Equal(2, diagnostic["severity"]!.GetValue<int>());
    }

    private static JsonObject Position(string uri, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };
}
