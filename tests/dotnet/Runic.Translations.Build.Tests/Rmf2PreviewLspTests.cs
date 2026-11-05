using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic.renderPreview with a requested locale and a preview time zone, and configured pseudo-locales (L5-23, L5-25).
internal sealed class Rmf2PreviewLspTests
{
    private const string Project = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en","locales":["en","de"],"pseudoLocales":["en-XA"]}""";
    private const string English = "price = Total {$amount :number} at {$when :datetime style=short}\n";
    private const string German = "price = Summe {$amount :number} am {$when :datetime style=short}\n";

    private static JsonObject Samples() => new() { ["amount"] = "1234.5", ["when"] = "2026-01-15T12:00:00Z" };

    private static JsonNode Render(Session session, string uri, string locale, string? requested = null, string? timeZone = null) =>
        session.Request("workspace/executeCommand", new JsonObject
        {
            ["command"] = "runic.renderPreview",
            ["arguments"] = new JsonArray(uri, "price", locale, Samples(), requested, timeZone),
        });

    private static string Text(JsonNode response) =>
        string.Concat(response["result"]!["runs"]!.AsArray().Select(run => run!["text"]?.GetValue<string>() ?? ""));

    [Test, DisplayName("LSP preview formats in the requested locale and the preview time zone, and renders pseudo-locales")]
    public void RequestedLocaleTimeZoneAndPseudoLocales()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        File.WriteAllText(temporary.Resolve("en.rmf2"), English);
        File.WriteAllText(temporary.Resolve("de.rmf2"), German);
        string uri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri;
        using Session session = new(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 1, ["text"] = English } });

        JsonNode german = Render(session, uri, "de", timeZone: "utc");
        Assert.True(german["error"] is null, german.ToJsonString());
        Assert.Contains("1.234,5", Text(german));
        Assert.Contains("12:00", Text(german));
        Assert.Equal("de", german["result"]!["requestedLocale"]!.GetValue<string>());
        Assert.Equal("exact", german["result"]!["resolution"]!.GetValue<string>());
        Assert.Equal("utc", german["result"]!["timeZone"]!.GetValue<string>());

        // A de-CH user served the de catalog locale: Swiss grouping, the German text, resolved as a parent.
        JsonNode swiss = Render(session, uri, "de", "de-CH", "Asia/Tokyo");
        Assert.True(swiss["error"] is null, swiss.ToJsonString());
        Assert.Contains("Summe", Text(swiss));
        Assert.Contains("1'234.5", Text(swiss));
        Assert.Contains("21:00", Text(swiss));
        Assert.Equal("de-CH", swiss["result"]!["requestedLocale"]!.GetValue<string>());
        Assert.Equal("de-CH", swiss["result"]!["formattingLocale"]!.GetValue<string>());
        Assert.Equal("parent", swiss["result"]!["resolution"]!.GetValue<string>());
        Assert.Equal("ltr", swiss["result"]!["direction"]!.GetValue<string>());

        // A requested locale of another language keeps the content locale's formatting.
        JsonNode french = Render(session, uri, "de", "fr-FR");
        Assert.Equal("de", french["result"]!["formattingLocale"]!.GetValue<string>());
        Assert.Equal("related", french["result"]!["resolution"]!.GetValue<string>());

        Assert.Contains("is not available", Render(session, uri, "de", timeZone: "Mars/Olympus_Mons")["error"]?["message"]?.GetValue<string>() ?? "");
        Assert.Contains("not a valid BCP 47 tag", Render(session, uri, "de", "--")["error"]?["message"]?.GetValue<string>() ?? "");

        // runic/message lists the configured pseudo-locales; the preview generates them like a --pseudo-locales build.
        JsonNode info = session.Request("runic/message", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"] = new JsonObject { ["line"] = 0, ["character"] = 1 },
        })["result"]!;
        Assert.Equal("en-XA", string.Join(',', info["pseudoLocales"]!.AsArray().Select(item => item!.GetValue<string>())));
        JsonNode pseudo = Render(session, uri, "en-XA", timeZone: "utc");
        Assert.True(pseudo["error"] is null, pseudo.ToJsonString());
        string text = Text(pseudo);
        Assert.True(text.StartsWith('[') && text.Contains("1,234.5", StringComparison.Ordinal) && !text.Contains("Total", StringComparison.Ordinal),
            "The en-XA preview is not the generated pseudo text: " + text);
        JsonNode ast = session.Request("workspace/executeCommand", new JsonObject
        {
            ["command"] = "runic.preview", ["arguments"] = new JsonArray(uri, "price", "en-XA"),
        });
        Assert.True(ast["error"] is null && ast["result"]?["ast"] is not null, ast.ToJsonString());
    }
}
