using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring.Tests;

internal static class WorkspaceMutationTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("Locale addition previews and commits a compiler-valid transaction", AddLocale);
        runner.Add("Locale removal deletes documents and repairs fallback edges", RemoveLocale);
        runner.Add("Fallback mutation rejects cycles before writing", FallbackCycle);
        runner.Add("Key lifecycle mutations preserve values across locales", KeyLifecycle);
        runner.Add("Transaction plans snapshot validated edits and expose immutable bytes", ImmutablePlan);
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
            new TranslationAddLocaleRequest(project.Path, "product", "fr-fr", "de", "de"));
        Assert.Equal(2, plan.Edits.Count);
        Assert.True(plan.IsValid, "The locale-addition preview did not compile.");
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "fr-FR", "application_title.mf2")), "Planning wrote a locale document.");
        TranslationWorkspaceTransaction.Commit(plan);

        Assert.True(File.Exists(System.IO.Path.Combine(project.Path, "fr-FR", "application_title.mf2")), "The canonical locale message was not created.");
        JsonObject manifest = Read(project.Path, "runic.json");
        Assert.True(manifest["locales"]!.AsArray().Any(node => LocaleTag(node) == "fr-FR"), "The locale declaration is missing.");
    }

    private static void RemoveLocale()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de"), new("fr", "en")]);
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.RemoveLocale(
            new TranslationRemoveLocaleRequest(project.Path, "product", "en", "de"));
        Assert.Equal(2, plan.Edits.Count);
        TranslationWorkspaceTransaction.Commit(plan);
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "en", "application_title.mf2")), "The removed locale message still exists.");
        JsonArray locales = Read(project.Path, "runic.json")["locales"]!.AsArray();
        Assert.True(locales.All(node => LocaleTag(node) != "en"), "The locale declaration still exists.");
        JsonNode? french = locales.Single(node => LocaleTag(node) == "fr");
        Assert.Equal("de", LocaleFallback(french, "de"));
    }

    private static void FallbackCycle()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de"), new("fr", "de")]);
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.SetFallback(
            new TranslationSetFallbackRequest(project.Path, "product", "en", "fr")));
        byte[] before = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "runic.json"));
        Assert.Throws<TranslationAuthoringException>(
            () => TranslationWorkspaceMutation.SetFallback(
                new TranslationSetFallbackRequest(project.Path, "product", "fr", "en")),
            "cycle");
        Assert.True(before.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "runic.json"))), "Rejected fallback mutation changed the config.");
    }

    private static void KeyLifecycle()
    {
        using ProjectWorkspace project = new(additionalLocales: [new("en", "de")]);
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.CreateKey(
            new TranslationCreateKeyRequest(project.Path, "product", "dialog_confirm", "Confirm")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.RenameOrMove, "dialog_confirm", "action_confirm")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.Duplicate, "action_confirm", "action_confirm_again")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(
            new TranslationKeyMutationRequest(project.Path, "product", TranslationKeyMutationKind.Delete, "action_confirm", null)));

        foreach (string locale in new[] { "de", "en" })
        {
            Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, locale, "dialog_confirm.mf2")), "The source message remains.");
            Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, locale, "action_confirm.mf2")), "The deleted message remains.");
            Assert.Equal("Confirm\n", File.ReadAllText(System.IO.Path.Combine(project.Path, locale, "action_confirm_again.mf2"), Encoding.UTF8));
        }
    }

    private static void ImmutablePlan()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan validated = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
        TranslationWorkspaceEdit[] callerEdits = validated.Edits.ToArray();
        TranslationWorkspaceEdit created = validated.Edits.Single(edit => edit.Kind == TranslationWorkspaceEditKind.Create);
        byte[] expectedBytes = created.GetUtf8Bytes()!;

        var replacement = new TranslationWorkspaceEdit("unvalidated.mf2", TranslationWorkspaceEditKind.Create, null, Encoding.UTF8.GetBytes("unvalidated"));
        callerEdits[0] = replacement;
        Assert.True(validated.Edits is not TranslationWorkspaceEdit[], "The plan exposed its edit snapshot as a replaceable array.");
        var publicList = (IList<TranslationWorkspaceEdit>)validated.Edits;
        try { publicList[0] = replacement; throw new InvalidOperationException("The public edit list accepted a replacement."); }
        catch (NotSupportedException) { }
        var untypedPublicList = (System.Collections.IList)validated.Edits;
        try { untypedPublicList[0] = replacement; throw new InvalidOperationException("The untyped public edit list accepted a replacement."); }
        catch (NotSupportedException) { }

        byte[] publicBytes = created.GetUtf8Bytes()!;
        publicBytes[0] ^= 0xff;
        Assert.True(created.GetUtf8Bytes()!.SequenceEqual(expectedBytes), "The public byte accessor exposed mutable plan storage.");

        TranslationWorkspaceTransaction.Commit(validated);
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "unvalidated.mf2")), "Commit applied an edit supplied after validation.");
        Assert.True(File.ReadAllBytes(System.IO.Path.Combine(project.Path, created.RelativePath)).SequenceEqual(expectedBytes),
            "Commit did not apply the snapshotted validated edit bytes.");
    }

    private static void StaleRevision()
    {
        using ProjectWorkspace project = new();
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
        string path = System.IO.Path.Combine(project.Path, "runic.json");
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
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
        Interrupt(plan);
        TranslationPendingTransaction pending = TranslationWorkspaceTransaction.GetPending(project.Path)
            ?? throw new InvalidOperationException("The interrupted transaction left no journal.");
        Assert.Equal(2, pending.Paths.Count);
        TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Complete);
        Assert.True(TranslationWorkspaceTransaction.GetPending(project.Path) is null, "Completed recovery left a journal.");
    }

    private static void RollbackRecovery()
    {
        using ProjectWorkspace project = new();
        byte[] manifest = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "runic.json"));
        byte[] german = File.ReadAllBytes(System.IO.Path.Combine(project.Path, "de", "application_title.mf2"));
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
        Interrupt(plan);
        TranslationWorkspaceTransaction.Recover(project.Path, TranslationWorkspaceRecoveryMode.Rollback);
        Assert.True(manifest.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "runic.json"))), "Rollback did not restore the config byte-exactly.");
        Assert.True(german.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(project.Path, "de", "application_title.mf2"))), "Rollback changed an unaffected message.");
        Assert.True(!File.Exists(System.IO.Path.Combine(project.Path, "fr", "application_title.mf2")), "Rollback retained the created locale message.");
    }

    private static void FinalBoundaryRecovery()
    {
        using (var completed = new ProjectWorkspace())
        {
            TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
                new TranslationAddLocaleRequest(completed.Path, "product", "fr", "de", "de"));
            Interrupt(plan, 2);
            TranslationWorkspaceTransaction.Recover(completed.Path, TranslationWorkspaceRecoveryMode.Complete);
        }
        using (var rolledBack = new ProjectWorkspace())
        {
            byte[] manifest = File.ReadAllBytes(System.IO.Path.Combine(rolledBack.Path, "runic.json"));
            TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
                new TranslationAddLocaleRequest(rolledBack.Path, "product", "fr", "de", "de"));
            Interrupt(plan, 2);
            TranslationWorkspaceTransaction.Recover(rolledBack.Path, TranslationWorkspaceRecoveryMode.Rollback);
            Assert.True(manifest.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(rolledBack.Path, "runic.json"))), "Final-boundary rollback did not restore the config.");
        }
    }

    private static void RecoveryConflict()
    {
        using ProjectWorkspace project = new();
        string manifestPath = System.IO.Path.Combine(project.Path, "runic.json");
        byte[] original = File.ReadAllBytes(manifestPath);
        TranslationWorkspaceTransactionPlan plan = TranslationWorkspaceMutation.AddLocale(
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
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
            new TranslationAddLocaleRequest(project.Path, "product", "fr", "de", "de"));
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

    private static string? LocaleTag(JsonNode? node) => node is JsonObject item
        ? item["tag"]?.GetValue<string>()
        : node?.GetValue<string>();

    private static string? LocaleFallback(JsonNode? node, string baseLocale) => node is JsonObject item
        ? item["fallback"]?.GetValue<string>() ?? baseLocale
        : LocaleTag(node) == baseLocale ? null : baseLocale;

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
            JsonObject config = Read(Path, "runic.json");
            File.WriteAllText(System.IO.Path.Combine(Path, "runic.json"), config.ToJsonString());
            foreach (string localeFile in Directory.EnumerateFiles(Path, "*.rmf2"))
            {
                string locale = System.IO.Path.GetFileNameWithoutExtension(localeFile);
                Directory.CreateDirectory(System.IO.Path.Combine(Path, locale));
                File.WriteAllText(System.IO.Path.Combine(Path, locale, "application_title.mf2"), "ProductText\n");
                File.Delete(localeFile);
            }
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
