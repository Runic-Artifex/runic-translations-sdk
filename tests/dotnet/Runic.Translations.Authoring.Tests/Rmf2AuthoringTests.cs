using System;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring.Tests;

internal static class Rmf2AuthoringTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 input references follow logical resources without capturing translation locals", References);
        runner.Add("RMF2 local rename changes semantic references without touching literal text", LocalRename);
        runner.Add("RMF2 formatting and value edits preserve comments and exact message text", Format);
        runner.Add("RMF2 revisioned workspace renames extracts inlines and rejects stale buffers", Refactors);
        runner.Add("RMF2 TOML migration validates complete catalog and retains original source", Migration);
    }
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static TranslationSource Project(string layout = "rmf2-v1") => Source("runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\",\"sourceLayout\":\"" + layout + "\"}");
    private static void References()
    {
        var en = Source("en.rmf2", "shop {\n  title =\n    .input {$name :string}\n    {{Hello {$name}}}\n}\nother = {$name}\n");
        var de = Source("shop/de.rmf2", "title = 😀 {$name} {|$name|}\nother = {$name}\n");
        var fr = Source("shop/fr.rmf2", "title =\n  .local $name = {|Bonjour|}\n  {{{$name}}}\n");
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [en, de, fr]);
        var references = workspace.VariableReferences("en.rmf2", "shop_title", "name");
        Assert.Equal(3, references.Count);
        Assert.Equal(1, references.Count(r => r.IsDeclaration));
        Assert.True(references.All(r => r.Document.Source.Path != "shop/fr.rmf2"), "A local in another translation was captured.");
        foreach (var reference in references)
        {
            int start = reference.Resource.MessageByteMap[reference.Location.StartByte];
            int end = reference.Resource.MessageByteMap[reference.Location.StartByte + reference.Location.LengthBytes];
            Assert.Equal("$name", Encoding.UTF8.GetString(reference.Document.Source.GetUtf8Bytes(), start, end - start));
        }
        Assert.Equal(2, workspace.VariableReferences("shop/fr.rmf2", "title", "name").Count);
        workspace.Update("shop/de.rmf2", Encoding.UTF8.GetBytes("title = Hallo\n"), workspace.Revision("shop/de.rmf2"));
        Assert.Equal(2, workspace.VariableReferences("en.rmf2", "shop_title", "name").Count);
        var config = System.Text.Json.Nodes.JsonNode.Parse(Project().GetUtf8Bytes())!;
        config["sourceRoots"] = System.Text.Json.Nodes.JsonNode.Parse("[{\"path\":\"base\",\"namespace\":[\"shop\"]},{\"path\":\"localized\",\"namespace\":[\"shop\"]}]");
        var mounted = new Rmf2Workspace(Path.GetTempPath(), Source("runic.json", config.ToJsonString()), [Source("base/en.rmf2", "title = {$name}\n"), Source("localized/de.rmf2", "title = {$name}\n")]);
        Assert.Equal(2, mounted.VariableReferences("base/en.rmf2", "title", "name").Count);
    }
    private static void LocalRename()
    {
        const string text = "message =\n  .input {$input :string}\n  .local $alias = {$input}\n  {{Hello {$alias}, literal {|$alias|} and plain $alias}}\nother = {$alias}\n";
        var renamed = Rmf2ResourceWriter.RenameLocal(Source("en.rmf2", text), "message", "alias", "display");
        string result = Encoding.UTF8.GetString(renamed);
        Assert.True(result.Contains(".local $display = {$input}", StringComparison.Ordinal) && result.Contains("Hello {$display}", StringComparison.Ordinal), "Semantic local occurrences were not renamed.");
        Assert.True(result.Contains("{|$alias|} and plain $alias", StringComparison.Ordinal) && result.Contains("other = {$alias}", StringComparison.Ordinal), "Local rename changed unrelated text.");
        Assert.Throws<TranslationAuthoringException>(() => Rmf2ResourceWriter.RenameLocal(Source("en.rmf2", text), "message", "alias", "input"), "capture");
    }
    private static void Format()
    {
        var source = Source("en.rmf2", "shop {\n    # Translator context\n    title =\n        {{First\n          Indented\n        }}\n}\n");
        byte[] formatted = Rmf2ResourceWriter.Format(source);
        var document = Rmf2ResourceReader.Read(new TranslationSource("en.rmf2", formatted));
        Assert.Equal("{{First\n  Indented\n}}", document.Nodes[1].Message);
        Assert.True(Encoding.UTF8.GetString(formatted).Contains("  # Translator context", StringComparison.Ordinal), "Formatter lost comment.");
        byte[] edited = Rmf2ResourceWriter.SetMessage(new TranslationSource("en.rmf2", formatted), "shop_title", "New\nMessage");
        Assert.Equal("Translator context", Rmf2ResourceReader.Read(new TranslationSource("en.rmf2", edited)).Nodes[1].Comments[0]);
    }
    private static void Refactors()
    {
        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var project = Project(); var source = Source("en.rmf2", "# Group documentation\nshop {\n  # Keep this\n  title = Shop\n}\n");
            File.WriteAllBytes(Path.Combine(root, "runic.json"), project.GetUtf8Bytes()); File.WriteAllBytes(Path.Combine(root, "en.rmf2"), source.GetUtf8Bytes());
            var workspace = new Rmf2Workspace(root, project, [source]);
            var rename = workspace.Rename(["shop", "title"], "label");
            Assert.True(rename.Compilation.Success && Encoding.UTF8.GetString(rename.Edits[0].GetUtf8Bytes()!).Contains("# Keep this", StringComparison.Ordinal), "Rename lost metadata.");
            string revision = workspace.Revision("en.rmf2"); workspace.Update("en.rmf2", source.GetUtf8Bytes(), revision);
            Assert.Throws<TranslationAuthoringException>(() => workspace.Update("en.rmf2", [], "stale"), "revision");
            var extract = workspace.Extract("en.rmf2", ["shop"]); TranslationWorkspaceTransaction.Commit(extract);
            Assert.True(File.ReadAllText(Path.Combine(root, "en.rmf2")).Contains("# Group documentation", StringComparison.Ordinal), "Extract lost group documentation.");
            Assert.True(File.Exists(Path.Combine(root, "shop/en.rmf2")), "Extract did not create feature source.");
            var split = new Rmf2Workspace(root, project, [new TranslationSource("en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new TranslationSource("shop/en.rmf2", File.ReadAllBytes(Path.Combine(root, "shop/en.rmf2")))]);
            var inline = split.Inline("shop/en.rmf2", "en.rmf2"); TranslationWorkspaceTransaction.Commit(inline);
            Assert.Equal(workspace.Validate().Catalogs[0].Fingerprint, inline.Compilation.Catalogs[0].Fingerprint);
            Assert.True(File.ReadAllText(Path.Combine(root, "en.rmf2")).Contains("# Keep this", StringComparison.Ordinal), "Inline lost comment.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Migration()
    {
        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-migration-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var project = Project("locale-toml"); var source = Source("en.toml", "# Translator note\n[shop]\ntitle='Shop'\n");
            File.WriteAllBytes(Path.Combine(root, "runic.json"), project.GetUtf8Bytes()); File.WriteAllBytes(Path.Combine(root, "en.toml"), source.GetUtf8Bytes());
            var plan = new Rmf2Workspace(root, project, [source]).MigrateToml(out var notes);
            Assert.True(notes.Count == 1 && plan.Compilation.Success, "Migration failed to report trivia.");
            TranslationWorkspaceTransaction.Commit(plan);
            Assert.Equal(Encoding.UTF8.GetString(source.GetUtf8Bytes()), File.ReadAllText(Path.Combine(root, "en.toml.bak")));
            Assert.True(File.Exists(Path.Combine(root, "en.rmf2")) && !File.Exists(Path.Combine(root, "en.toml")), "Migration did not replace the source layout.");
        }
        finally { Directory.Delete(root, true); }
    }
}
