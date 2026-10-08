using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Session = Runic.Translations.Build.Tests.Rmf2DiagnosticLspTests.Session;

namespace Runic.Translations.Build.Tests;

// Document profile v1 authoring features of the language server.
internal static class Rmf2DocumentLspTests
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
          ไทย{/p}
        messy = {#p}A{/p} {#ul}{#li}B{/li}{/ul}

        """;

    private static readonly string[] HeadingLevels = ["1", "2", "3", "4", "5", "6"];

    internal static void Register(TestRunner runner)
    {
        runner.Add("LSP document messages get block completion, hover, outline, folding and canonical formatting", Authoring);
        runner.Add("LSP joins Southeast Asian document line breaks and previews document blocks", FixAndPreview);
    }

    private static (TemporaryDirectory Directory, Session Session, string Uri) Open()
    {
        var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.Resolve("runic.json"), Project);
        string path = temporary.Resolve("en.rmf2"), uri = new Uri(path).AbsoluteUri;
        File.WriteAllText(path, Text);
        var session = new Session(temporary.Path);
        session.Request("initialize", new JsonObject { ["rootUri"] = new Uri(temporary.Path).AbsoluteUri, ["capabilities"] = new JsonObject() });
        session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 1, ["text"] = Text } });
        return (temporary, session, uri);
    }

    private static void Authoring()
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
            string[] root = Labels(10, 8);
            Assert.True(root.Contains("#p") && root.Contains("#ul") && root.Contains("#strong") && !root.Contains("#li") && !root.Contains("/p"),
                "The message root takes blocks or inline content: " + string.Join(' ', root));
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
            Assert.True(ranges.Split(' ').Contains("3-6") && ranges.Split(' ').Contains("8-9"), "Block folding ranges are missing: " + ranges);

            JsonArray edits = session.Request("textDocument/formatting", Document(uri))["result"]!.AsArray();
            Assert.Equal(Text.Replace("messy = {#p}A{/p} {#ul}{#li}B{/li}{/ul}\n", "messy =\n  {#p}A{/p}\n  {#ul}\n    {#li}B{/li}\n  {/ul}\n", StringComparison.Ordinal),
                edits.Single()!["newText"]!.GetValue<string>(), "Canonical document layout");
        }
    }

    private static void FixAndPreview()
    {
        (TemporaryDirectory temporary, Session session, string uri) = Open();
        using (temporary)
        using (session)
        {
            var range = At(uri, 8, 6);
            range.Remove("position");
            range["range"] = new JsonObject { ["start"] = Position(8, 6), ["end"] = Position(9, 0) };
            range["context"] = new JsonObject { ["diagnostics"] = new JsonArray() };
            JsonNode action = session.Request("textDocument/codeAction", range)["result"]!.AsArray()
                .Single(item => item!["title"]!.GetValue<string>() == "Join the lines without a space")!;
            JsonNode edit = action["edit"]!["documentChanges"]![0]!["edits"]![0]!;
            Assert.Equal("", edit["newText"]!.GetValue<string>());
            Assert.Equal("8:10-9:2", edit["range"]!["start"]!["line"] + ":" + edit["range"]!["start"]!["character"] + "-" + edit["range"]!["end"]!["line"] + ":" + edit["range"]!["end"]!["character"]);
            Assert.True(session.Request("codeAction/resolve", action.DeepClone().AsObject())["error"] is null, "The join-lines fix failed resolution.");

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

    private static JsonObject Document(string uri) => new() { ["textDocument"] = new JsonObject { ["uri"] = uri } };
    private static JsonObject At(string uri, int line, int character) => new() { ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = Position(line, character) };
    private static JsonObject Position(int line, int character) => new() { ["line"] = line, ["character"] = character };
}
