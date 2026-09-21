using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring.Tests;

internal static class Rmf2AuthoringTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 syntax cache reuses only unchanged bounded source snapshots", SyntaxCache);
        runner.Add("RMF2 locale mutations validate fallback graphs and preserve physical namespaces", Locales);
        runner.Add("RMF2 resource renames and duplicates retain conditional slot contracts", SlotContracts);
        runner.Add("RMF2 input rename updates parameter and example metadata without altering literal text", InputRename);
        runner.Add("RMF2 message mutations retain metadata and validate the complete catalog", ResourceMutations);
        runner.Add("RMF2 mounted namespace rename preserves configuration trivia and physical roots", MountedRename);
        runner.Add("RMF2 input references follow logical resources without capturing translation locals", References);
        runner.Add("RMF2 local rename changes semantic references without touching literal text", LocalRename);
        runner.Add("RMF2 formatting and value edits preserve comments and exact message text", Format);
        runner.Add("RMF2 revisioned workspace renames extracts inlines and rejects stale buffers", Refactors);
        runner.Add("RMF2 TOML migration validates complete catalog and retains original source", Migration);
    }
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static TranslationSource Project(string layout = "rmf2-v1") => Source("runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\",\"sourceLayout\":\"" + layout + "\"}");
    private static void SyntaxCache()
    {
        var cache = new Rmf2WorkspaceCache(1);
        var original = cache.Create(Path.GetTempPath(), Project(), [Source("en.rmf2", "x = One\n")]).Documents[0];
        var same = cache.Create(Path.GetTempPath(), Project(), [Source("en.rmf2", "x = One\n")]).Documents[0];
        Assert.True(ReferenceEquals(original, same), "Unchanged syntax was reparsed.");
        var changed = cache.Create(Path.GetTempPath(), Project(), [Source("en.rmf2", "x = Two\n")]).Documents[0];
        Assert.True(!ReferenceEquals(original, changed), "Changed source reused stale syntax.");
        cache.Create(Path.GetTempPath(), Project(), [Source("de.rmf2", "x = Zwei\n")]);
        Assert.True(!ReferenceEquals(changed, cache.Create(Path.GetTempPath(), Project(), [Source("en.rmf2", "x = Two\n")]).Documents[0]), "Syntax cache exceeded its capacity.");
    }
    private static void Locales()
    {
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [Source("shop/en.rmf2", "title = Shop\n")]);
        var add = workspace.AddLocale("de", copyFrom: "en");
        Assert.True(add.Edits.Any(edit => edit.RelativePath == "shop/de.rmf2"), "Locale copy lost the physical namespace.");
        var config = new TranslationSource("runic.json", add.Edits.Single(edit => edit.RelativePath == "runic.json").GetUtf8Bytes()!);
        var sources = new[] { Source("shop/en.rmf2", "title = Shop\n"), new TranslationSource("shop/de.rmf2", add.Edits.Single(edit => edit.RelativePath == "shop/de.rmf2").GetUtf8Bytes()!) };
        var updated = new Rmf2Workspace(Path.GetTempPath(), config, sources);
        Assert.True(updated.SetFallback("de", "en").Compilation.Success, "Valid fallback rejected.");
        Assert.True(updated.RemoveLocale("de").Edits.Any(edit => edit.RelativePath == "shop/de.rmf2" && edit.Kind == TranslationWorkspaceEditKind.Delete), "Locale removal missed a physical file.");
        Assert.Throws<TranslationAuthoringException>(() => updated.RemoveLocale("en"), "fallback");
        Assert.Throws<TranslationAuthoringException>(() => updated.SetFallback("de", "de"), "fallback");
    }
    private static void SlotContracts()
    {
        string config = Encoding.UTF8.GetString(Project().GetUtf8Bytes()).TrimEnd('}') + ",\"markup\":{\"slots\":{\"shop_payment\":{\"retry\":{\"min\":0,\"max\":1}}}}}";
        const string message = "shop {\n  payment =\n    .input {$state :string}\n    .match $state\n    yes {{{#action ref=retry}Retry{/action}}}\n    * {{Ready}}\n}\n";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Source("runic.json", config), [Source("en.rmf2", message)]);
        Assert.True(workspace.RenameSlot("en.rmf2", "shop_payment", "retry", "again").Compilation.Success, "Slot rename lost its conditional bounds.");
        Assert.True(workspace.Rename(["shop"], "cart").Compilation.Success, "Group rename lost a conditional slot contract.");
        Assert.True(workspace.MutateResource(["shop", "payment"], ["checkout"], true).Compilation.Success, "Duplicate lost a conditional slot contract.");
        Assert.True(workspace.MutateResource(["shop", "payment"], ["checkout"]).Compilation.Success, "Move lost a conditional slot contract.");
    }
    private static void InputRename()
    {
        const string text = "# 😀 Greeting\r\n@param $name - Person\r\n@example { \"name\" : \"$name\" }\r\nx = Hello {$name} {|$name|}\r\nother = {$name}\r\n";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [Source("en.rmf2", text), Source("de.rmf2", "x = Hallo {$name}\nother = {$name}\n")]);
        var plan = workspace.RenameInput("en.rmf2", "x", "name", "person");
        Assert.Equal(2, plan.Edits.Count);
        string changed = Encoding.UTF8.GetString(plan.Edits.Single(e => e.RelativePath == "en.rmf2").GetUtf8Bytes()!);
        Assert.True(changed.Contains("@param $person - Person", StringComparison.Ordinal), "Parameter metadata was not renamed.");
        Assert.True(changed.Contains("{ \"person\" : \"$name\" }", StringComparison.Ordinal), "Example key or literal value was changed incorrectly.");
        Assert.True(changed.Contains("Hello {$person} {|$name|}\r\nother = {$name}", StringComparison.Ordinal), "Input rename changed literal text or another resource.");
    }
    private static void ResourceMutations()
    {
        var source = Source("en.rmf2", "shop {\n  # Greeting\n  @param $name - Person\n  title = Hello {$name}\n}\nother = Keep\n");
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [source]);
        foreach (bool duplicate in new[] { false, true })
        {
            var plan = workspace.MutateResource(["shop", "title"], ["account", "greeting"], duplicate);
            var document = Rmf2ResourceReader.Read(new TranslationSource("en.rmf2", plan.Edits[0].GetUtf8Bytes()!));
            var added = document.Nodes.Single(n => n.Key == "account_greeting");
            Assert.Equal("Greeting", added.Comments[0]);
            Assert.Equal("param $name - Person", added.Properties[0]);
            Assert.Equal(duplicate, document.Nodes.Any(n => n.Key == "shop_title"));
            Assert.True(plan.Compilation.Success, "Message move or duplicate failed validation.");
        }
        Assert.True(workspace.MutateResource(["shop", "title"], null).Compilation.Success, "Message deletion failed.");
        Assert.True(workspace.CreateResource("en.rmf2", ["new", "message"], "New").Compilation.Success, "Message creation failed.");
    }
    private static void MountedRename()
    {
        string config = Encoding.UTF8.GetString(Project().GetUtf8Bytes()).TrimEnd('}') + ",\r\n  \"sourceRoots\":[{\"path\":\"base\",\"namespace\":[\"shop\"]},{\"path\":\"localized\",\"namespace\":[\"shop\"]}]}";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Source("runic.json", config), [Source("base/cart/en.rmf2", "title = Cart\n"), Source("localized/cart/de.rmf2", "title = Warenkorb\n")]);
        var plan = workspace.Rename(["shop"], "store");
        Assert.Equal(1, plan.Edits.Count);
        Assert.Equal("runic.json", plan.Edits[0].RelativePath);
        Assert.Equal(config.Replace("\"shop\"", "\"store\"", StringComparison.Ordinal), Encoding.UTF8.GetString(plan.Edits[0].GetUtf8Bytes()!));
        Assert.True(plan.Compilation.Success, "Mounted namespace rename failed validation.");
        var directory = workspace.Rename(["shop", "cart"], "basket");
        Assert.True(directory.Edits.Any(e => e.RelativePath == "base/basket/en.rmf2") && directory.Edits.Any(e => e.RelativePath == "localized/basket/de.rmf2"), "Mounted directory rename moved the wrong roots.");
    }
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
            var plan = new Rmf2Workspace(root, project, [source]).MigrateToml(out var notes, out TranslationMigrationReport report);
            Assert.True(notes.Count == 1 && plan.Compilation.Success, "Migration failed to report trivia.");
            Assert.Equal(1, report.Losses.Count);
            Assert.Equal("RMF2-MIGRATION-COMMENT-OWNERSHIP", report.Losses[0].Code);
            Assert.Equal("en.toml", report.Losses[0].Location);
            new Rmf2Workspace(root, project, [source]).MigrateToml(out IReadOnlyList<string> compatibilityNotes);
            Assert.Equal(report.Losses[0].Message, compatibilityNotes.Single());
            TranslationWorkspaceTransaction.Commit(plan);
            Assert.Equal(Encoding.UTF8.GetString(source.GetUtf8Bytes()), File.ReadAllText(Path.Combine(root, "en.toml.bak")));
            Assert.True(File.Exists(Path.Combine(root, "en.rmf2")) && !File.Exists(Path.Combine(root, "en.toml")), "Migration did not replace the source layout.");
        }
        finally { Directory.Delete(root, true); }
    }
}
