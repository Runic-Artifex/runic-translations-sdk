using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// `runic-translations serve` message and set-message: in-page translation editing in the Vite dev server (#178).
internal sealed class ServeMessageEditingTests
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private const string English = """
        # The page title.
        title = Hello

        # Taps, counted.
        clicks =
          .input {$count :integer}
          .match $count
          one {{One tap}}
          *   {{{$count} taps}}

        shop {
          # Markup stays as written.
          cart = Your {#strong}cart{/strong}
          gender =
            .input {$who :string}
            .match $who
            female {{She bought it}}
            * {{They bought it}}
        }
        # trailing comment

        """;

    private const string German = """
        title = Hallo
        clicks =
          .input {$count :integer}
          .match $count
          one {{Ein Tipp}}
          * {{{$count} Tipps}}
        shop {
          cart = Dein {#strong}Korb{/strong}
          gender =
            .input {$who :string}
            .match $who
            female {{Sie kaufte es}}
            * {{Sie kauften es}}
        }

        """;

    [Test, DisplayName("message reads every locale's text, location, file hash and structure")]
    public void ReadsMessage()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        using var server = new ServeModeTests.CompileServerHarness();
        JsonElement message = Message(server.Send(Read(project, "clicks")));
        Assert.Equal("app", message.GetProperty("catalog").GetString());
        Assert.Equal("catalog", message.GetProperty("kind").GetString());
        Assert.Equal("count", message.GetProperty("inputs")[0].GetProperty("name").GetString());
        JsonElement english = Locale(message, "en");
        Assert.Equal(".input {$count :integer}\n.match $count\none {{One tap}}\n*   {{{$count} taps}}", english.GetProperty("text").GetString());
        Assert.Equal(5, english.GetProperty("source").GetProperty("line").GetInt32());
        Assert.Equal(Full(project, "en.rmf2"), english.GetProperty("file").GetProperty("path").GetString());
        Assert.Equal(Hash(Path.Combine(project, "en.rmf2")), english.GetProperty("file").GetProperty("hash").GetString());
        JsonElement structure = english.GetProperty("structure");
        Assert.Equal("count", structure.GetProperty("selectors")[0].GetString());
        Assert.Equal("one,*", string.Join(",", structure.GetProperty("variants").EnumerateArray().Select(variant => variant.GetProperty("keys")[0].GetString())));
        Assert.Equal("de", Locale(message, "de").GetProperty("locale").GetString());
        Assert.Equal("en", Locale(message, "de").GetProperty("fallback").GetString());

        // A logical key names a grouped message, as its canonical key does.
        JsonElement cart = Message(server.Send(Read(project, "shop.cart")));
        Assert.Equal("shop_cart", cart.GetProperty("key").GetString());
        Assert.True(cart.GetProperty("structured").GetBoolean(), cart.ToString());
        Assert.Equal("Your {#strong}cart{/strong}", Locale(cart, "en").GetProperty("text").GetString());

        JsonElement unknown = server.Send(Read(project, "nope"));
        Assert.Equal(2, unknown.GetProperty("exitCode").GetInt32());
        Assert.Equal("not-found", unknown.GetProperty("result").GetProperty("error").GetString());
    }

    [Test, DisplayName("set-message changes only the entry's text, keeping formatting, comments and order (RMF2)")]
    public void WritesRmf2PreservingSource()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string english = Path.Combine(project, "en.rmf2"), german = Path.Combine(project, "de.rmf2");
        using var server = new ServeModeTests.CompileServerHarness();

        // A plain message, a plural message and a markup message, each edited as its whole text.
        JsonElement written = server.Send(Write(project, "title", "en", "Hello, edited", Hash(english)));
        Assert.True(written.GetProperty("ok").GetBoolean(), written.ToString());
        Assert.Equal(Full(project, "en.rmf2"), written.GetProperty("result").GetProperty("written")[0].GetString());
        Assert.Equal("Hello, edited", Locale(Message(written), "en").GetProperty("text").GetString());
        Assert.Equal(English.Replace("title = Hello\n", "title = Hello, edited\n", StringComparison.Ordinal), File.ReadAllText(english));

        string plural = ".input {$count :integer}\n.match $count\none {{One click}}\n* {{{$count} clicks}}";
        Assert.True(server.Send(Write(project, "clicks", "en", plural, Hash(english))).GetProperty("ok").GetBoolean(), "plural");
        string markup = "Your {#strong}basket{/strong}";
        Assert.True(server.Send(Write(project, "shop.cart", "en", markup, Hash(english))).GetProperty("ok").GetBoolean(), "markup");
        string selector = ".input {$who :string}\n.match $who\nfemale {{She took it}}\n* {{They took it}}";
        Assert.True(server.Send(Write(project, "shop_gender", "de", selector, Hash(german))).GetProperty("ok").GetBoolean(), "selector");

        string expectedEnglish = English
            .Replace("title = Hello\n", "title = Hello, edited\n", StringComparison.Ordinal)
            .Replace("  one {{One tap}}\n  *   {{{$count} taps}}\n", "  one {{One click}}\n  * {{{$count} clicks}}\n", StringComparison.Ordinal)
            .Replace("cart = Your {#strong}cart{/strong}", "cart = Your {#strong}basket{/strong}", StringComparison.Ordinal);
        Assert.Equal(expectedEnglish, File.ReadAllText(english));
        Assert.Equal(German.Replace("female {{Sie kaufte es}}\n    * {{Sie kauften es}}", "female {{She took it}}\n    * {{They took it}}", StringComparison.Ordinal),
            File.ReadAllText(german));

        // Writing a message's text as read leaves the file byte for byte.
        JsonElement read = Locale(Message(server.Send(Read(project, "clicks"))), "de");
        byte[] before = File.ReadAllBytes(german);
        Assert.True(server.Send(Write(project, "clicks", "de", read.GetProperty("text").GetString()!, Hash(german))).GetProperty("ok").GetBoolean(), "round trip");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(german)), "Writing the text as read changed the file.");
    }

    [Test, DisplayName("set-message keeps CRLF line endings")]
    public void KeepsLineEndings()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string english = Path.Combine(project, "en.rmf2");
        File.WriteAllText(english, English.Replace("\n", "\r\n", StringComparison.Ordinal), Utf8);
        using var server = new ServeModeTests.CompileServerHarness();
        JsonElement response = server.Send(Write(project, "shop.cart", "en", "Your {#strong}bag{/strong}", Hash(english)));
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal(English.Replace("cart = Your {#strong}cart{/strong}", "cart = Your {#strong}bag{/strong}", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal),
            File.ReadAllText(english));
    }

    [Test, DisplayName("set-message refuses a text that does not compile and writes nothing")]
    public void RejectsInvalidText()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string english = Path.Combine(project, "en.rmf2");
        using var server = new ServeModeTests.CompileServerHarness();
        foreach (string text in new[] { "Hello {$name", "Your {#strong}cart", ".input {$count :integer}\n.match $count\none {{One}}" })
        {
            JsonElement response = server.Send(Write(project, text.StartsWith("Your", StringComparison.Ordinal) ? "shop.cart" : text.StartsWith(".input", StringComparison.Ordinal) ? "clicks" : "title", "en", text, Hash(english)));
            Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
            Assert.Equal("invalid", response.GetProperty("result").GetProperty("error").GetString());
            Assert.True(response.GetProperty("diagnostics").EnumerateArray().Any(diagnostic => diagnostic.GetProperty("code").GetString()!.StartsWith("RTR", StringComparison.Ordinal)), response.ToString());
            Assert.Equal(English, File.ReadAllText(english));
        }
    }

    [Test, DisplayName("set-message refuses a stale hash with the current message")]
    public void RejectsStaleHash()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string german = Path.Combine(project, "de.rmf2");
        using var server = new ServeModeTests.CompileServerHarness();
        string read = Locale(Message(server.Send(Read(project, "title"))), "de").GetProperty("file").GetProperty("hash").GetString()!;
        // Saved in an editor meanwhile.
        File.WriteAllText(german, German.Replace("title = Hallo", "title = Hallo aus dem Editor", StringComparison.Ordinal), Utf8);
        JsonElement response = server.Send(Write(project, "title", "de", "Hallo, bearbeitet", read));
        Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("conflict", response.GetProperty("result").GetProperty("error").GetString());
        JsonElement current = Locale(response.GetProperty("result").GetProperty("message"), "de");
        Assert.Equal("Hallo aus dem Editor", current.GetProperty("text").GetString());
        Assert.Equal(Hash(german), current.GetProperty("file").GetProperty("hash").GetString());
        Assert.Contains("Hallo aus dem Editor", File.ReadAllText(german));
    }

    [Test, DisplayName("set-message adds a missing translation beside the base locale's source")]
    public void AddsMissingTranslation()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary, """{ "translationCompleteness": "allow" }""");
        string german = Path.Combine(project, "de.rmf2");
        File.WriteAllText(german, "# German\ntitle = Hallo\nshop {\n  cart = Dein {#strong}Korb{/strong}\n}\n", Utf8);
        using var server = new ServeModeTests.CompileServerHarness();
        JsonElement missing = Locale(Message(server.Send(Read(project, "shop.gender"))), "de");
        Assert.Equal("missing", missing.GetProperty("origin").GetString());
        Assert.Equal(Full(project, "de.rmf2"), missing.GetProperty("file").GetProperty("path").GetString());
        JsonElement response = server.Send(Write(project, "shop.gender", "de", ".input {$who :string}\n.match $who\nfemale {{Sie}}\n* {{Alle}}", missing.GetProperty("file").GetProperty("hash").GetString()));
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal("# German\ntitle = Hallo\nshop {\n  cart = Dein {#strong}Korb{/strong}\n  gender =\n    .input {$who :string}\n    .match $who\n    female {{Sie}}\n    * {{Alle}}\n}\n",
            File.ReadAllText(german));
        Assert.Equal("catalog", Locale(Message(response), "de").GetProperty("origin").GetString());
    }

    [Test, DisplayName("set-message writes direct MF2 files whole")]
    public void WritesDirectMf2()
    {
        using TemporaryDirectory temporary = new();
        string project = temporary.Resolve("translations");
        Directory.CreateDirectory(Path.Combine(project, "en"));
        Directory.CreateDirectory(Path.Combine(project, "de"));
        File.WriteAllText(Path.Combine(project, "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en", "locales": ["en", "de"] }
            """, Utf8);
        string plural = ".input {$count :integer}\n.match $count\none {{One file}}\n* {{{$count} files}}\n";
        File.WriteAllText(Path.Combine(project, "en", "files.mf2"), plural, Utf8);
        File.WriteAllText(Path.Combine(project, "de", "files.mf2"), ".input {$count :integer}\n.match $count\none {{Eine Datei}}\n* {{{$count} Dateien}}\n", Utf8);
        string german = Path.Combine(project, "de", "files.mf2");
        using var server = new ServeModeTests.CompileServerHarness();
        JsonElement read = Locale(Message(server.Send(Read(project, "files"))), "en");
        Assert.Equal(plural.TrimEnd('\n'), read.GetProperty("text").GetString());
        JsonElement response = server.Send(Write(project, "files", "de", ".input {$count :integer}\n.match $count\none {{Eine Datei}}\n* {{{$count} Dateien insgesamt}}", Hash(german)));
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        Assert.Equal(".input {$count :integer}\n.match $count\none {{Eine Datei}}\n* {{{$count} Dateien insgesamt}}\n", File.ReadAllText(german));
        JsonElement broken = server.Send(Write(project, "files", "de", ".match $count\n* {{x", Hash(german)));
        Assert.Equal("invalid", broken.GetProperty("result").GetProperty("error").GetString());
    }

    [Test, DisplayName("set-message writes a library catalog's text to the application's override source")]
    public void WritesLibraryOverride()
    {
        using TemporaryDirectory temporary = new();
        string library = temporary.Resolve("node_modules", "@acme", "grid");
        string packed = Path.Combine(library, "runic", "acme.grid");
        Directory.CreateDirectory(packed);
        File.WriteAllText(Path.Combine(library, "package.json"), """
            { "name": "@acme/grid", "version": "1.0.0", "exports": { "./runic-translations": "./translations/web-module-manifest-v3.json" },
              "runicTranslations": { "catalog": "acme.grid", "project": "./runic/acme.grid/runic.json" } }
            """, Utf8);
        // Discovery needs the exported manifest to exist; its content is the Vite plugin's concern.
        Directory.CreateDirectory(Path.Combine(library, "translations"));
        File.WriteAllText(Path.Combine(library, "translations", "web-module-manifest-v3.json"), "{}", Utf8);
        File.WriteAllText(Path.Combine(packed, "runic.json"), """
            { "schemaVersion": 1, "catalog": "acme.grid", "code": { "namespace": "Acme.Grid", "className": "GridText" }, "baseLocale": "en", "locales": ["en", "de"] }
            """, Utf8);
        File.WriteAllText(Path.Combine(packed, "en.rmf2"), "empty = No rows\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\n", Utf8);
        File.WriteAllText(Path.Combine(packed, "de.rmf2"), "empty = Keine Zeilen\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\n", Utf8);
        byte[] packedGerman = File.ReadAllBytes(Path.Combine(packed, "de.rmf2"));
        File.WriteAllText(temporary.Resolve("package.json"), """{ "name": "app", "private": true, "dependencies": { "@acme/grid": "1.0.0" } }""", Utf8);
        string project = WriteProject(temporary);
        string manifest = Path.Combine(project, "runic.json");
        using var server = new ServeModeTests.CompileServerHarness();

        JsonElement message = Message(server.Send(Read(project, "empty", "acme.grid")));
        Assert.Equal("library", message.GetProperty("kind").GetString());
        Assert.Equal("@acme/grid@1.0.0", message.GetProperty("package").GetString());
        JsonElement german = Locale(message, "de");
        Assert.Equal("library", german.GetProperty("origin").GetString());
        Assert.Equal("Keine Zeilen", german.GetProperty("text").GetString());
        string overrideFile = Full(project, "overrides/acme.grid/de.rmf2");
        Assert.Equal(overrideFile, german.GetProperty("file").GetProperty("path").GetString());
        Assert.Equal("none", german.GetProperty("file").GetProperty("hash").GetString());

        // The first write creates the override, records the library text in the review file and lists the catalog in runic.json.
        JsonElement created = server.Send(Write(project, "empty", "de", "Nichts zu zeigen", "none", "acme.grid"));
        Assert.True(created.GetProperty("ok").GetBoolean(), created.ToString());
        string[] written = created.GetProperty("result").GetProperty("written").EnumerateArray().Select(item => item.GetString()!).ToArray();
        Assert.Equal(overrideFile, written[0]);
        Assert.True(written.Contains(Full(project, "runic.json")), string.Join(", ", written));
        Assert.True(File.Exists(Path.Combine(project, "overrides", "acme.grid", "acme.grid.review.json")), "The review file was not written.");
        Assert.Contains("\"overrides\": [{ \"catalog\": \"acme.grid\", \"from\": \"@acme/grid\" }]", File.ReadAllText(manifest));
        string source = File.ReadAllText(overrideFile);
        Assert.Contains("empty = Nichts zu zeigen\n", source);
        Assert.Contains("# @acme/grid@1.0.0, base locale en:", source);
        Assert.Equal("override", Locale(Message(created), "de").GetProperty("origin").GetString());

        // The next write edits the override in place; a text that does not compile is refused.
        JsonElement edited = server.Send(Write(project, "empty", "de", "Nichts da", Hash(overrideFile), "acme.grid"));
        Assert.True(edited.GetProperty("ok").GetBoolean(), edited.ToString());
        Assert.Equal(source.Replace("empty = Nichts zu zeigen", "empty = Nichts da", StringComparison.Ordinal), File.ReadAllText(overrideFile));
        JsonElement invalid = server.Send(Write(project, "empty", "de", "{$missing", Hash(overrideFile), "acme.grid"));
        Assert.Equal("invalid", invalid.GetProperty("result").GetProperty("error").GetString());
        JsonElement stale = server.Send(Write(project, "empty", "de", "Nichts", "none", "acme.grid"));
        Assert.Equal("conflict", stale.GetProperty("result").GetProperty("error").GetString());
        Assert.True(packedGerman.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(packed, "de.rmf2"))), "The library's packed source was written.");
    }

    private static string WriteProject(TemporaryDirectory temporary, string? validation = null)
    {
        string project = temporary.Resolve("translations");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "runic.json"),
            "{ \"schemaVersion\": 1, \"catalog\": \"app\", \"code\": { \"namespace\": \"Example\", \"className\": \"AppText\" }, \"baseLocale\": \"en\", \"locales\": [\"en\", \"de\"]" +
            (validation is null ? "" : ", \"validation\": " + validation) + " }\n", Utf8);
        File.WriteAllText(Path.Combine(project, "en.rmf2"), English, Utf8);
        File.WriteAllText(Path.Combine(project, "de.rmf2"), German, Utf8);
        return project;
    }

    private static string Read(string project, string key, string? catalog = null) =>
        JsonSerializer.Serialize(new { id = 1, method = "message", project, key, catalog });

    private static string Write(string project, string key, string locale, string text, string? expectedHash, string? catalog = null) =>
        JsonSerializer.Serialize(new { id = 2, method = "set-message", project, key, locale, text, expectedHash, catalog });

    private static JsonElement Message(JsonElement response)
    {
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
        return response.GetProperty("result").GetProperty("message");
    }

    private static JsonElement Locale(JsonElement message, string locale) =>
        message.GetProperty("locales").EnumerateArray().Single(item => item.GetProperty("locale").GetString() == locale);

    private static string Hash(string path) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Full(string project, string relative) => Path.GetFullPath(Path.Combine(project, relative)).Replace('\\', '/');
}
