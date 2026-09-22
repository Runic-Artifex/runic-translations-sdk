using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
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
        runner.Add("RMF2 locale plans preserve mounted projects and commit atomically", ExecutionV2Locales);
        runner.Add("Direct MF2 workspaces expose semantic authoring and filename transactions", DirectSources);
    }
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static TranslationSource Project() => Source("runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"}");
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
    private static void DirectSources()
    {
        const string english = ".input {$name :string}\n{{Hello {$name}}}\n";
        const string german = ".input {$name :string}\n{{Hallo {$name}}}\n";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [
            Source("en/hello.mf2", english),
            Source("de/hello.mf2", german),
        ]);
        Assert.Equal(2, workspace.Documents.Count);
        Assert.Equal("en", workspace.Locale("en/hello.mf2"));
        Assert.Equal("hello", string.Join('.', workspace.LogicalPath("en/hello.mf2", workspace.Documents.Single(document => document.Source.Path == "en/hello.mf2").Nodes.Single())));
        TranslationWorkspaceTransactionPlan input = workspace.RenameInput("en/hello.mf2", "hello", "name", "person");
        Assert.Equal(2, input.Edits.Count);
        Assert.True(input.IsValid && input.Edits.All(edit => Encoding.UTF8.GetString(edit.GetUtf8Bytes()!).Contains("$person", StringComparison.Ordinal)),
            "Direct input rename did not cover each locale.");

        TranslationWorkspaceTransactionPlan renamed = workspace.Rename(["hello"], "greeting");
        Assert.True(renamed.IsValid, "Direct filename rename did not validate.");
        Assert.True(renamed.Edits.Any(edit => edit.RelativePath == "en/hello.mf2" && edit.Kind == TranslationWorkspaceEditKind.Delete) &&
            renamed.Edits.Any(edit => edit.RelativePath == "de/greeting.mf2" && edit.Kind == TranslationWorkspaceEditKind.Create),
            "Direct filename rename did not create and delete every locale path.");

        TranslationWorkspaceTransactionPlan created = workspace.CreateResource("en/hello.mf2", ["new_message"], "New message\n");
        Assert.True(created.IsValid && created.Edits.Select(edit => edit.RelativePath).Order(StringComparer.Ordinal)
            .SequenceEqual(["de/new_message.mf2", "en/new_message.mf2"]), "Direct message creation did not cover every locale.");
        Assert.True(workspace.Format("en/hello.mf2").IsValid, "Direct formatting validation failed.");
    }
    private static void Locales()
    {
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Project(), [Source("shop/en.rmf2", "title = Shop\n")]);
        var add = workspace.AddLocale("de", copyFrom: "en");
        Assert.True(add.Edits.Any(edit => edit.RelativePath == "shop/de.rmf2"), "Locale copy lost the physical namespace.");
        var config = new TranslationSource("runic.json", add.Edits.Single(edit => edit.RelativePath == "runic.json").GetUtf8Bytes()!);
        var sources = new[] { Source("shop/en.rmf2", "title = Shop\n"), new TranslationSource("shop/de.rmf2", add.Edits.Single(edit => edit.RelativePath == "shop/de.rmf2").GetUtf8Bytes()!) };
        var updated = new Rmf2Workspace(Path.GetTempPath(), config, sources);
        Assert.True(updated.SetFallback("de", "en").IsValid, "Valid fallback rejected.");
        Assert.True(updated.RemoveLocale("de").Edits.Any(edit => edit.RelativePath == "shop/de.rmf2" && edit.Kind == TranslationWorkspaceEditKind.Delete), "Locale removal missed a physical file.");
        Assert.Throws<TranslationAuthoringException>(() => updated.RemoveLocale("en"), "fallback");
        Assert.Throws<TranslationAuthoringException>(() => updated.SetFallback("de", "de"), "fallback");
    }
    private static void SlotContracts()
    {
        string config = Encoding.UTF8.GetString(Project().GetUtf8Bytes()).TrimEnd('}') + ",\"markup\":{\"slots\":{\"shop_payment\":{\"retry\":{\"min\":0,\"max\":1}}}}}";
        const string message = "shop {\n  payment =\n    .input {$state :string}\n    .match $state\n    yes {{{#action ref=retry}Retry{/action}}}\n    * {{Ready}}\n}\n";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Source("runic.json", config), [Source("en.rmf2", message)]);
        Assert.True(workspace.RenameSlot("en.rmf2", "shop_payment", "retry", "again").IsValid, "Slot rename lost its conditional bounds.");
        Assert.True(workspace.Rename(["shop"], "cart").IsValid, "Group rename lost a conditional slot contract.");
        Assert.True(workspace.MutateResource(["shop", "payment"], ["checkout"], true).IsValid, "Duplicate lost a conditional slot contract.");
        Assert.True(workspace.MutateResource(["shop", "payment"], ["checkout"]).IsValid, "Move lost a conditional slot contract.");
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
            Assert.True(plan.IsValid, "Message move or duplicate failed validation.");
        }
        Assert.True(workspace.MutateResource(["shop", "title"], null).IsValid, "Message deletion failed.");
        Assert.True(workspace.CreateResource("en.rmf2", ["new", "message"], "New").IsValid, "Message creation failed.");

        var localized = new Rmf2Workspace(Path.GetTempPath(), Project(), [
            Source("en.rmf2", "existing = English\n"),
            Source("de.rmf2", "existing = Deutsch\n"),
        ]);
        TranslationWorkspaceTransactionPlan created = localized.CreateResource("en.rmf2", ["new", "message"], "New");
        Assert.Equal(2, created.Edits.Count);
        Assert.True(created.IsValid, "RMF2 message creation did not update every locale.");
        Assert.True(created.Edits.All(edit => Encoding.UTF8.GetString(edit.GetUtf8Bytes()!).Contains("new {", StringComparison.Ordinal)),
            "RMF2 message creation missed a locale document.");
    }
    private static void MountedRename()
    {
        string config = Encoding.UTF8.GetString(Project().GetUtf8Bytes()).TrimEnd('}') + ",\r\n  \"sourceRoots\":[{\"path\":\"base\",\"namespace\":[\"shop\"]},{\"path\":\"localized\",\"namespace\":[\"shop\"]}]}";
        var workspace = new Rmf2Workspace(Path.GetTempPath(), Source("runic.json", config), [Source("base/cart/en.rmf2", "title = Cart\n"), Source("localized/cart/de.rmf2", "title = Warenkorb\n")]);
        var plan = workspace.Rename(["shop"], "store");
        Assert.Equal(1, plan.Edits.Count);
        Assert.Equal("runic.json", plan.Edits[0].RelativePath);
        Assert.Equal(config.Replace("\"shop\"", "\"store\"", StringComparison.Ordinal), Encoding.UTF8.GetString(plan.Edits[0].GetUtf8Bytes()!));
        Assert.True(plan.IsValid, "Mounted namespace rename failed validation.");
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
            Assert.True(rename.IsValid && Encoding.UTF8.GetString(rename.Edits[0].GetUtf8Bytes()!).Contains("# Keep this", StringComparison.Ordinal), "Rename lost metadata.");
            string revision = workspace.Revision("en.rmf2"); workspace.Update("en.rmf2", source.GetUtf8Bytes(), revision);
            Assert.Throws<TranslationAuthoringException>(() => workspace.Update("en.rmf2", [], "stale"), "revision");
            var extract = workspace.Extract("en.rmf2", ["shop"]); TranslationWorkspaceTransaction.Commit(extract);
            Assert.True(File.ReadAllText(Path.Combine(root, "en.rmf2")).Contains("# Group documentation", StringComparison.Ordinal), "Extract lost group documentation.");
            Assert.True(File.Exists(Path.Combine(root, "shop/en.rmf2")), "Extract did not create feature source.");
            var split = new Rmf2Workspace(root, project, [new TranslationSource("en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new TranslationSource("shop/en.rmf2", File.ReadAllBytes(Path.Combine(root, "shop/en.rmf2")))]);
            var inline = split.Inline("shop/en.rmf2", "en.rmf2"); TranslationWorkspaceTransaction.Commit(inline);
            Assert.True(inline.IsValid, "Inline plan did not validate.");
            Assert.True(File.ReadAllText(Path.Combine(root, "en.rmf2")).Contains("# Keep this", StringComparison.Ordinal), "Inline lost comment.");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void ExecutionV2Locales()
    {
        var planType = typeof(TranslationWorkspaceTransactionPlan);
        Assert.True(planType.GetNestedType("ValidationReceipt", System.Reflection.BindingFlags.NonPublic)?.IsNestedPrivate == true,
            "The selected-profile validation receipt is not private to the transaction plan.");
        Assert.True(planType.GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .All(constructor => constructor.GetParameters()[^1].ParameterType.Name == "Rmf2ProjectCompilationV5"),
            "A transaction-plan constructor accepts a detached validation receipt.");
        string root = Path.Combine(Path.GetTempPath(), "runic-rmf2-v2-authoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "translations"));
        Directory.CreateDirectory(Path.Combine(root, "base"));
        Directory.CreateDirectory(Path.Combine(root, "feature"));
        try
        {
            const string config = """
                {
                  "schemaVersion": 1,
                  "catalog": "app",
                  "code": { "namespace": "Example", "className": "AppText" },
                  "baseLocale": "en",
                  "sourceRoots": [
                    { "path": "../base", "namespace": [] },
                    { "path": "../feature", "namespace": ["shop"] }
                  ],
                  "locales": [
                    { "tag": "en" },
                    { "tag": "de", "fallback": "en" },
                    { "tag": "fr", "fallback": "de" }
                  ]
                }
                """;
            var files = new Dictionary<string, string>(StringComparer.Ordinal) {
                ["translations/runic.json"] = config,
                ["base/en.rmf2"] = "amount =\n  .local $n = {0.1 :number style=percent}\n  {{{$n}}}\n",
                ["base/de.rmf2"] = "amount = Betrag\n",
                ["base/fr.rmf2"] = "amount = Montant\n",
                ["feature/en.rmf2"] = "title = Shop\n",
                ["feature/de.rmf2"] = "title = Laden\n",
                ["feature/fr.rmf2"] = "title = Boutique\n",
            };
            foreach (var file in files)
            {
                string path = Path.Combine(root, file.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value, new UTF8Encoding(false));
            }

            Rmf2Workspace Open()
            {
                var project = new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(root, "translations/runic.json")));
                TranslationSource[] sources = Directory.EnumerateFiles(root, "*.rmf2", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path => new TranslationSource(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes(path))).ToArray();
                return new Rmf2Workspace(root, project, sources);
            }
            Dictionary<string, byte[]> Snapshot() => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
            static void AssertSnapshot(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual, string message)
            {
                Assert.Equal(string.Join('|', expected.Keys.Order(StringComparer.Ordinal)), string.Join('|', actual.Keys.Order(StringComparer.Ordinal)), message);
                foreach (var file in expected) Assert.True(actual[file.Key].SequenceEqual(file.Value), message + ": " + file.Key);
            }
            JsonObject Config() => JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "translations/runic.json")))!.AsObject();

            Dictionary<string, byte[]> beforeAdd = Snapshot();
            TranslationWorkspaceTransactionPlan add = Open().AddLocale("it", fallback: "de", copyFrom: "en");
            Assert.Equal("app", add.CatalogId);
            Assert.True(add.IsValid, "The RMF2 transaction did not validate.");
            AssertSnapshot(beforeAdd, Snapshot(), "Execution-v2 locale planning changed disk before commit");

            try { TranslationWorkspaceTransaction.CommitForTesting(add, 1); }
            catch (Exception) { }
            Assert.True(TranslationWorkspaceTransaction.GetPending(root) is not null, "Interrupted execution-v2 add left no recovery journal.");
            TranslationWorkspaceTransaction.Recover(root, TranslationWorkspaceRecoveryMode.Rollback);
            AssertSnapshot(beforeAdd, Snapshot(), "Execution-v2 rollback did not restore the mounted project byte-identically");

            add = Open().AddLocale("it", fallback: "de", copyFrom: "en");
            TranslationWorkspaceTransaction.Commit(add);
            Assert.True(File.ReadAllBytes(Path.Combine(root, "base/it.rmf2")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "base/en.rmf2"))), "Mounted base locale copy changed bytes.");
            Assert.True(File.ReadAllBytes(Path.Combine(root, "feature/it.rmf2")).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "feature/en.rmf2"))), "Mounted feature locale copy changed bytes.");
            Assert.True(TranslationWorkspaceTransaction.GetPending(root) is null, "Successful execution-v2 add retained a transaction journal.");

            Dictionary<string, byte[]> beforeCycle = Snapshot();
            Assert.Throws<TranslationAuthoringException>(() => Open().SetFallback("de", "fr"), "Fallback cycle");
            AssertSnapshot(beforeCycle, Snapshot(), "Rejected execution-v2 fallback cycle changed disk");

            Dictionary<string, byte[]> beforeFallback = Snapshot();
            TranslationWorkspaceTransactionPlan fallback = Open().SetFallback("fr", "en");
            AssertSnapshot(beforeFallback, Snapshot(), "Execution-v2 fallback planning changed disk before commit");
            TranslationWorkspaceTransaction.Commit(fallback);
            JsonObject configured = Config();
            Assert.Equal(2, configured["sourceRoots"]!.AsArray().Count, "Locale mutation lost mounted source roots.");
            Assert.Equal("en", configured["locales"]!.AsArray().Select(node => node!.AsObject()).Single(locale => locale["tag"]!.GetValue<string>() == "fr")["fallback"]!.GetValue<string>());

            Dictionary<string, byte[]> beforeRemove = Snapshot();
            TranslationWorkspaceTransactionPlan remove = Open().RemoveLocale("de", replacementFallback: "en");
            AssertSnapshot(beforeRemove, Snapshot(), "Execution-v2 removal planning changed disk before commit");
            TranslationWorkspaceTransaction.Commit(remove);
            Assert.True(!File.Exists(Path.Combine(root, "base/de.rmf2")) && !File.Exists(Path.Combine(root, "feature/de.rmf2")), "Execution-v2 removal left mounted locale sources.");
            configured = Config();
            JsonObject[] locales = configured["locales"]!.AsArray().Select(node => node!.AsObject()).ToArray();
            Assert.True(locales.All(locale => locale["tag"]!.GetValue<string>() != "de"), "Removed locale remained declared.");
            Assert.Equal("en", locales.Single(locale => locale["tag"]!.GetValue<string>() == "it")["fallback"]!.GetValue<string>(), "Dependent fallback was not redirected.");
            Assert.True(TranslationWorkspaceTransaction.GetPending(root) is null, "Successful execution-v2 removal retained a transaction journal.");
        }
        finally { Directory.Delete(root, true); }
    }
}
