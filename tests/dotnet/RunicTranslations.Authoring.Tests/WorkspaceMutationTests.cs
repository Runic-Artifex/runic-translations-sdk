using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using RunicTranslations.Authoring;

namespace RunicTranslations.Authoring.Tests;

internal static class WorkspaceMutationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Locale addition previews and commits a compiler-valid transaction", AddLocale);
        runner.Add("Locale removal deletes documents and repairs fallback edges", RemoveLocale);
        runner.Add("Fallback mutation rejects cycles before writing", FallbackCycle);
        runner.Add("Key lifecycle mutations preserve values across locales", KeyLifecycle);
        runner.Add("Transaction rejects stale revisions without partial writes", StaleRevision);
        runner.Add("Interrupted transaction can complete from its journal", CompleteRecovery);
        runner.Add("Interrupted transaction can roll back byte-exactly", RollbackRecovery);
        runner.Add("Recovery works after the final edit boundary", FinalBoundaryRecovery);
        runner.Add("Recovery refuses to overwrite post-interruption edits", RecoveryConflict);
        runner.Add("Transaction and recovery journals reject path escapes", PathEscapes);
    }

    private static void AddLocale()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr-fr", "de", "base", "de"));
        Assert.Equal(2, plan.Edits.Count);
        Assert.True(plan.Compilation.Success, "The locale-addition preview did not compile.");
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "product.fr-FR.json")), "Planning wrote a locale document.");
        TranslationWorkspaceTransaction.Commit(plan);

        TranslationWorkspaceDiscoveryResult result = TranslationWorkspaceDiscovery.Discover(project.Path);
        Assert.True(AssertSingle(result.Catalogs).Compilation.Success, "The committed locale addition did not compile.");
        Assert.True(result.Files.Any(file => file.Locale == "fr-FR"), "The canonical locale document was not created.");
        JsonObject manifest = Read(project.Path, "product.catalog.json");
        Assert.True(manifest["locales"]!.AsArray().Any(node => node?["tag"]?.GetValue<string>() == "fr-FR"), "The locale declaration is missing.");
    }

    private static void RemoveLocale()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de"), new("fr", "en")]);
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.RemoveLocale(
            new TranslationRemoveLocaleRequest(project.Path, "product", "en", "de"));
        Assert.Equal(2, plan.Edits.Count);
        TranslationWorkspaceTransaction.Commit(plan);
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "product.en.json")), "The removed locale document still exists.");
        JsonArray locales = Read(project.Path, "product.catalog.json")["locales"]!.AsArray();
        Assert.True(locales.All(node => node?["tag"]?.GetValue<string>() != "en"), "The locale declaration still exists.");
        JsonObject french = locales.Single(node => node?["tag"]?.GetValue<string>() == "fr")!.AsObject();
        Assert.Equal("de", french["fallback"]!.GetValue<string>());
    }

    private static void FallbackCycle()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de"), new("fr", "de")]);
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.SetFallback(
            new TranslationSetFallbackRequest(project.Path, "product", "en", "fr")));
        byte[] before = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.catalog.json"));
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationWorkspaceMutation.SetFallback(
                new TranslationSetFallbackRequest(project.Path, "product", "fr", "en")),
            "cycle");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.catalog.json"))), "Rejected fallback mutation changed the manifest.");
    }

    private static void KeyLifecycle()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de")]);
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.CreateKey(
            new TranslationCreateKeyRequest(project.Path, "product", "Dialog.Confirm", "Confirm", "base")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.RenameOrMove, "Dialog.Confirm", "Actions.Confirm")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.Duplicate, "Actions.Confirm", "Actions.ConfirmAgain")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.Delete, "Actions.Confirm", null)));

        foreach (string locale in new[] { "de", "en" })
        {
            JsonObject resources = Read(project.Path, $"product.{locale}.json")["resources"]!.AsObject();
            Assert.True(resources["Dialog"] is null, "The empty source group was not removed.");
            Assert.True(resources["Actions"]?["Confirm"] is null, "The deleted key remains.");
            Assert.Equal("Confirm", resources["Actions"]!["ConfirmAgain"]!.GetValue<string>());
        }
        Assert.True(AssertSingle(TranslationWorkspaceDiscovery.Discover(project.Path).Catalogs).Compilation.Success, "The key lifecycle result did not compile.");
    }

    private static void StaleRevision()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.CreateKey(
            new TranslationCreateKeyRequest(project.Path, "product", "Dialog.Confirm", "Confirm", "base"));
        string path = System.IO.Path.Combine(project.Path, "product.de.json");
        File.AppendAllText(path, Environment.NewLine);
        byte[] changed = File.ReadAllBytes(path);
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceTransaction.Commit(plan), "changed after");
        Assert.True(changed.AsSpan().SequenceEqual(File.ReadAllBytes(path)), "Stale transaction changed the resource document.");
        Assert.True(TranslationWorkspaceTransaction.GetPending(project.Path) is null, "A stale transaction left a journal.");
    }

    private static void CompleteRecovery()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "base", "de"));
        Interrupt(plan);
        TranslationPendingTransaction pending = TranslationWorkspaceTransaction.GetPending(project.Path)
            ?? throw new InvalidOperationException("The interrupted transaction left no journal.");
        Assert.Equal(2, pending.Paths.Count);
        TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Complete);
        Assert.True(TranslationWorkspaceTransaction.GetPending(project.Path) is null, "Completed recovery left a journal.");
        Assert.True(AssertSingle(TranslationWorkspaceDiscovery.Discover(project.Path).Catalogs).Compilation.Success, "Completed recovery did not compile.");
    }

    private static void RollbackRecovery()
    {
        using ProjectWorkspace project = new();
        byte[] manifest = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.catalog.json"));
        byte[] german = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.de.json"));
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "base", "de"));
        Interrupt(plan);
        TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Rollback);
        Assert.True(manifest.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.catalog.json"))), "Rollback did not restore the manifest byte-exactly.");
        Assert.True(german.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "product.de.json"))), "Rollback changed an unaffected document.");
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "product.fr.json")), "Rollback retained the created locale document.");
    }

    private static void FinalBoundaryRecovery()
    {
        using (var completed = new ProjectWorkspace())
        {
            TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
                new TranslationAddLocaleRequest(completed.Path, "product", "fr", "de", "base", "de"));
            Interrupt(plan, 2);
            TranslationWorkspaceTransaction.Recover(completed.Path, TranslationWorkspaceRecoveryMode.Complete);
            Assert.True(AssertSingle(TranslationWorkspaceDiscovery.Discover(completed.Path).Catalogs).Compilation.Success, "Final-boundary completion did not compile.");
        }
        using (var rolledBack = new ProjectWorkspace())
        {
            byte[] manifest = File.ReadAllBytes(System.IO.Path.Combine(rolledBack.Path, "product.catalog.json"));
            TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
                new TranslationAddLocaleRequest(rolledBack.Path, "product", "fr", "de", "base", "de"));
            Interrupt(plan, 2);
            TranslationWorkspaceTransaction.Recover(rolledBack.Path, TranslationWorkspaceRecoveryMode.Rollback);
            Assert.True(manifest.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(rolledBack.Path, "product.catalog.json"))), "Final-boundary rollback did not restore the manifest.");
        }
    }

    private static void RecoveryConflict()
    {
        using ProjectWorkspace project = new();
        string manifestPath = System.IO.Path.Combine(project.Path, "product.catalog.json");
        byte[] original = File.ReadAllBytes(manifestPath);
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "base", "de"));
        Interrupt(plan, 1);
        File.WriteAllText(manifestPath, "{}", new UTF8Encoding(false));
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Complete),
            "retained");
        Assert.True(TranslationWorkspaceTransaction.GetPending(project.Path) is not null, "A recovery conflict removed the journal.");
        File.WriteAllBytes(manifestPath, original);
        TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Rollback);
    }

    private static void PathEscapes()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan valid = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "base", "de"));
        var escapedEdit = new TranslationWorkspaceEdit("../escape.json", TranslationWorkspaceEditKind.Create, null, Encoding.UTF8.GetBytes("{}"));
        var escapedPlan = new TranslationWorkspaceTransactionPlan(project.Path, "product", [escapedEdit], valid.Compilation);
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceTransaction.Commit(escapedPlan), "escapes");

        string journalPath = System.IO.Path.Combine(project.Path, ".runic-translations.transaction.json");
        string journal = "{\"Version\":1,\"Root\":" + JsonValue.Create(project.Path)!.ToJsonString() +
            ",\"CatalogId\":\"product\",\"Entries\":[{\"Path\":\"../escape.json\",\"TemporaryName\":null,\"OriginalBase64\":null,\"Delete\":true,\"NewRevision\":null}]}";
        File.WriteAllText(journalPath, journal, new UTF8Encoding(false));
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceTransaction.GetPending(project.Path), "invalid");
        File.Delete(journalPath);
    }

    private static void Interrupt(TranslationWorkspaceTransactionPlan plan, int afterEdit = 1)
    {
        try { TranslationWorkspaceTransaction.CommitForTesting(plan, afterEdit); }
        catch (Exception) { return; }
        throw new InvalidOperationException("The transaction interruption was not injected.");
    }

    private static JsonObject Read(string root, string path) =>
        JsonNode.Parse(File.ReadAllBytes(System.IO.Path.Combine(root, path)))!.AsObject();

    private static T AssertSingle<T>(System.Collections.Generic.IReadOnlyList<T> items)
    {
        Assert.Equal(1, items.Count);
        return items[0];
    }

    private sealed class ProjectWorkspace : IDisposable
    {
        private readonly string _container;

        public ProjectWorkspace(TranslationProjectLocale[]? additionalLocales = null)
        {
            _container = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"runic-mutation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_container);
            Path = System.IO.Path.Combine(_container, "Project");
            TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(
                    Path,
                    "product",
                    "de",
                    "Customer.Product",
                    "ProductText",
                    additionalLocales)));
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(_container, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
