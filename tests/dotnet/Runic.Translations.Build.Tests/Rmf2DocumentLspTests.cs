using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// Document profile v1 authoring features of the language server.
internal sealed class Rmf2DocumentLspTests
{
    private const string Project = """{"schemaVersion":1,"catalog":"app","code":{"namespace":"Example","className":"Text"},"baseLocale":"en"}""";
    private const string Text = """
        notice =
          {#h level=1}Getting started{/h}
          {#p}Open the {#link ref=guide}guide{/link}.{/p}
          {#ul}
            {#li}One{/li}
            {#li}Two{/li}
          {/ul}
        thai =
          {#p}ภาษา
          ไทย
          ภาษา{/p}
        messy = {#p}A{/p} {#ul}{#li}B{/li}{/ul}
        plain = See the guide.

        """;

    // CRLF line ends, and an astral character (a UTF-16 surrogate pair) before every position
    // the tests check, so UTF-8, UTF-16 and line arithmetic cannot drift apart unnoticed.
    private const string Heading = "  {#h level=1}\U0001D4A2 Getting {#link ref=guide href=$url}started{/link}{/h}";
    private const string ThaiLine = "  {#p}\U0001D4A2ภาษา";
    private const string CrlfText = "notice =\r\n" + Heading + "\r\n  {#ul}\r\n    {#li}\U0001D4A2 One{/li}\r\n  {/ul}\r\nthai =\r\n" + ThaiLine + "\r\n  ไทย{/p}\r\n";

    private static readonly string[] HeadingLevels = ["1", "2", "3", "4", "5", "6"];

    private static (TemporaryDirectory Directory, Session Session, string Uri) Open(string text = Text)
    {
        var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        string path = temporary.Resolve("en.rmf2"), uri = new Uri(path).AbsoluteUri;
        File.WriteAllText(path, text);
        var session = new Session(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 1, ["text"] = text } });
        return (temporary, session, uri);
    }

    [Test, DisplayName("LSP document messages get block completion, hover, outline, folding and canonical formatting")]
    public void Authoring()
    {
        (TemporaryDirectory temporary, Session session, string uri) = Open();
        using (temporary)
        using (session)
        {
            string[] Labels(int line, int character) => session.Request("textDocument/completion", At(uri, line, character))["result"]!.AsArray()
                .Select(item => item!["label"]!.GetValue<string>()).ToArray();
            string[] list = Labels(5, 4);
            Assert.True(list.Contains("#li") && list.Contains("/ul") && !list.Contains("#p") && !list.Contains("#strong"),
                "Inside a list only list items belong: " + string.Join(' ', list));
            string[] item = Labels(4, 12);
            Assert.True(item.Contains("#strong") && !item.Contains("#li") && !item.Contains("#p"), "Inside a list item only inline markup belongs: " + string.Join(' ', item));
            string[] root = Labels(11, 8);
            Assert.True(root.Contains("#p") && root.Contains("#ul") && !root.Contains("#strong") && !root.Contains("#li") && !root.Contains("/p"),
                "The root of a document message takes blocks: " + string.Join(' ', root));
            string[] inline = Labels(12, 8);
            Assert.True(inline.Contains("#strong") && inline.Contains("#link") && !inline.Contains("#p") && !inline.Contains("#h") && !inline.Contains("#ul"),
                "The root of an inline message takes inline markup only: " + string.Join(' ', inline));
            string[] level = Labels(1, 12);
            Assert.True(HeadingLevels.All(level.Contains), "Heading levels were not offered: " + string.Join(' ', level));

            string hover = session.Request("textDocument/hover", At(uri, 0, 1))["result"]!["contents"]!["value"]!.GetValue<string>();
            Assert.Contains("Content: document", hover);
            Assert.Contains("Structure: h[level=1],p,ul(li,li)", hover);
            Assert.Contains("runic:ul · placement: block · children: list-items",
                session.Request("textDocument/hover", At(uri, 3, 4))["result"]!["contents"]!["value"]!.GetValue<string>());

            JsonArray symbols = session.Request("textDocument/documentSymbol", Document(uri))["result"]!.AsArray();
            JsonNode heading = symbols.Single(symbol => symbol!["name"]!.GetValue<string>() == "notice")!["children"]!.AsArray().Single()!;
            Assert.Equal("Getting started", heading["name"]!.GetValue<string>());
            Assert.Equal("h level=1", heading["detail"]!.GetValue<string>());
            Assert.Equal(1, heading["range"]!["start"]!["line"]!.GetValue<int>());

            JsonArray folding = session.Request("textDocument/foldingRange", Document(uri))["result"]!.AsArray();
            string ranges = string.Join(' ', folding.Select(range => range!["startLine"] + "-" + range["endLine"]));
            Assert.True(ranges.Split(' ').Contains("3-6") && ranges.Split(' ').Contains("8-10"), "Block folding ranges are missing: " + ranges);

            JsonArray edits = session.Request("textDocument/formatting", Document(uri))["result"]!.AsArray();
            Assert.Equal(Text.Replace("messy = {#p}A{/p} {#ul}{#li}B{/li}{/ul}\n", "messy =\n  {#p}A{/p}\n  {#ul}\n    {#li}B{/li}\n  {/ul}\n", StringComparison.Ordinal),
                edits.Single()!["newText"]!.GetValue<string>(), "Canonical document layout");
        }
    }

    [Test, DisplayName("LSP joins Southeast Asian document line breaks and previews document blocks")]
    public void FixAndPreview()
    {
        (TemporaryDirectory temporary, Session session, string uri) = Open();
        using (temporary)
        using (session)
        {
            JsonArray atBreak = CodeActions(session, uri, Position(8, 6), Position(9, 0));
            JsonNode action = atBreak.Single(item => item!["title"]!.GetValue<string>() == "Join lines 9 and 10 without a space")!;
            Assert.True(action["isPreferred"]!.GetValue<bool>(), "The fix at the requested line break is not preferred.");
            JsonNode edit = action["edit"]!["documentChanges"]![0]!["edits"]![0]!;
            Assert.Equal("", edit["newText"]!.GetValue<string>());
            Assert.Equal("8:10-9:2", Describe(edit["range"]!));
            Assert.True(session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"] is null, "The join-lines fix failed resolution.");

            // At the message name, where RTR0078 is reported, every break of the message has its own
            // fix with its own title, and none of them is preferred over the others.
            JsonArray atName = CodeActions(session, uri, Position(7, 0), Position(7, 4));
            string[] titles = atName.Select(item => item!["title"]!.GetValue<string>()).Where(title => title.StartsWith("Join lines", StringComparison.Ordinal)).ToArray();
            Assert.Equal("Join lines 9 and 10 without a space|Join lines 10 and 11 without a space", string.Join('|', titles));
            Assert.True(atName.All(item => !item!["isPreferred"]!.GetValue<bool>()), "Ambiguous line-break fixes were preferred: " + atName.ToJsonString());

            JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
                ["command"] = "runic.renderPreview", ["arguments"] = new JsonArray(uri, "notice", "en", new JsonObject()) })["result"]!;
            JsonArray blocks = preview["blocks"]!.AsArray();
            Assert.Equal("runic:h runic:p runic:ul", string.Join(' ', blocks.Select(block => block!["name"]!.GetValue<string>())));
            Assert.Equal("1", blocks[0]!["options"]!["level"]!.GetValue<string>());
            Assert.Equal("ul[1]/li[2]", blocks[2]!["blocks"]![1]!["occurrence"]!.GetValue<string>());
            Assert.Contains("runic:link", blocks[1]!["runs"]!.ToJsonString());
            Assert.Equal("Getting started\n\nOpen the guide.\n\n- One\n- Two", preview["runs"]!.AsArray().Single()!["text"]!.GetValue<string>());
        }
    }

    [Test, DisplayName("LSP document folding, outline and line-break fixes keep CRLF and surrogate-pair positions")]
    public void CrlfPositions()
    {
        (TemporaryDirectory temporary, Session session, string uri) = Open(CrlfText);
        using (temporary)
        using (session)
        {
            JsonArray folding = session.Request("textDocument/foldingRange", Document(uri))["result"]!.AsArray();
            string[] ranges = folding.Select(range => range!["startLine"] + "-" + range["endLine"]).ToArray();
            Assert.True(ranges.Contains("0-4") && ranges.Contains("2-4") && ranges.Contains("5-7") && ranges.Contains("6-7"),
                "CRLF folding ranges: " + string.Join(' ', ranges));

            JsonArray symbols = session.Request("textDocument/documentSymbol", Document(uri))["result"]!.AsArray();
            JsonNode notice = symbols.Single(symbol => symbol!["name"]!.GetValue<string>() == "notice")!;
            Assert.Equal("0:0-0:6", Describe(notice["selectionRange"]!));
            JsonNode heading = notice["children"]!.AsArray().Single()!;
            Assert.Equal("\U0001D4A2 Getting started", heading["name"]!.GetValue<string>());
            Assert.Equal("1:2-1:" + Heading.Length, Describe(heading["range"]!));
            Assert.Equal("1:2-1:" + "  {#h level=1}".Length, Describe(heading["selectionRange"]!));

            JsonNode action = CodeActions(session, uri, Position(6, ThaiLine.Length), Position(7, 0))
                .Single(item => item!["title"]!.GetValue<string>() == "Join lines 7 and 8 without a space")!;
            Assert.True(action["isPreferred"]!.GetValue<bool>(), "The only line-break fix is not preferred.");
            JsonNode edit = action["edit"]!["documentChanges"]![0]!["edits"]![0]!;
            Assert.Equal("6:" + ThaiLine.Length + "-7:2", Describe(edit["range"]!));
            Assert.Equal(CrlfText.Replace(ThaiLine + "\r\n  ไทย", ThaiLine + "ไทย", StringComparison.Ordinal), Apply(CrlfText, edit), "The join must remove CR, LF and the continuation indentation.");
        }
    }

    private static JsonArray CodeActions(Session session, string uri, JsonObject start, JsonObject end) =>
        session.Request("textDocument/codeAction", new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = uri }, ["range"] = new JsonObject { ["start"] = start, ["end"] = end },
            ["context"] = new JsonObject { ["diagnostics"] = new JsonArray() },
        })["result"]!.AsArray();

    // Applies one LSP text edit with UTF-16 positions, the server's default position encoding.
    private static string Apply(string text, JsonNode edit)
    {
        int Offset(JsonNode position)
        {
            int at = 0;
            for (int line = position["line"]!.GetValue<int>(); line > 0; line--) at = text.IndexOf('\n', at) + 1;
            return at + position["character"]!.GetValue<int>();
        }
        int from = Offset(edit["range"]!["start"]!), to = Offset(edit["range"]!["end"]!);
        return string.Concat(text.AsSpan(0, from), edit["newText"]!.GetValue<string>(), text.AsSpan(to));
    }

    private static string Describe(JsonNode range) =>
        range["start"]!["line"] + ":" + range["start"]!["character"] + "-" + range["end"]!["line"] + ":" + range["end"]!["character"];
    private static JsonObject Document(string uri) => new() { ["textDocument"] = new JsonObject { ["uri"] = uri } };
    private static JsonObject At(string uri, int line, int character) => new() { ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = Position(line, character) };
    private static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };
}
