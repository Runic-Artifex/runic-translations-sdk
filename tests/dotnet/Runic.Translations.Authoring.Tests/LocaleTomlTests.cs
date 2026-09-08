using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring.Tests;

internal static class LocaleTomlTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("TOML grouped edits preserve Unicode comments and untouched bytes", PreserveTrivia);
        runner.Add("TOML key rename and delete have explicit comment ownership", RenameDelete);
        runner.Add("TOML value encoding preserves decoded UTF-8", EncodeValues);
        runner.Add("TOML lifecycle uses physical files and rejects stale file hashes", Lifecycle);
        runner.Add("MF2 migration dry-run is deterministic and rollback preserves originals", Migration);
        runner.Add("MF2 migration rejects occupied TOML destinations", MigrationCollision);
        runner.Add("Transaction rejects oversized staging before creating files", StagingBudget);
        runner.Add("Authoring discovers uppercase TOML and rejects uppercase mixed sources", UppercaseLocaleSources);
        runner.Add("Migration discovers uppercase MF2 and rejects uppercase TOML collisions", UppercaseMigrationSources);
    }

    private static void PreserveTrivia()
    {
        const string original = "# café 😀\r\nfirst = 'One' # keep\r\nsecond=\"Two\"\r\n# tail\r\n";
        byte[] result = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.SetValue, "first", "New"),
            new(TranslationLocaleEditKind.SetValue, "second", "Deux")]);
        Assert.Equal("# café 😀\r\nfirst = 'New' # keep\r\nsecond='Deux'\r\n# tail\r\n", Encoding.UTF8.GetString(result));
        Assert.Equal(original, Encoding.UTF8.GetString(TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.SetValue, "second", "Two")])));
    }

    private static void RenameDelete()
    {
        byte[] result = TranslationLocaleWriter.Apply(Source("# lead\na = 'A' # inline\nb = 'B'\n"), "en", [
            new(TranslationLocaleEditKind.Rename, "b", TargetKey: "renamed"), new(TranslationLocaleEditKind.Delete, "a")]);
        Assert.Equal("# lead\n # inline\nrenamed = 'B'\n", Encoding.UTF8.GetString(result));
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source("a='A'\nb='B'"), "en", [new(TranslationLocaleEditKind.Rename, "a", TargetKey: "b")]), "already exists");
    }

    private static void EncodeValues()
    {
        foreach (string value in new[] { "", "😀 café", "quotes ' and \" and \\", "\r\n\tfirst\nlast\r", "\u0001\u007f", "'''\n\"\"\"" })
        {
            var parsed = TranslationLocaleReader.Read(Source("message = " + TranslationLocaleWriter.EncodeValue(value)), "en");
            Assert.True(parsed.Success, "Encoded TOML did not parse.");
            Assert.True(Encoding.UTF8.GetBytes(value).AsSpan().SequenceEqual(parsed.Entries.Single().Message.GetUtf8Bytes()), "Encoding changed decoded message bytes.");
        }
    }

    private static void Lifecycle()
    {
        using var project = new Workspace();
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.CreateKey(new(project.Root, "product", "hello", "Hello")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(new(project.Root, "product", TranslationKeyMutationKind.RenameOrMove, "hello", "welcome")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.AddLocale(new(project.Root, "product", "de", "en", "en")));
        string path = Path.Combine(project.Root, "en.toml");
        string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        var plan = TranslationWorkspaceMutation.ApplyLocaleEdits(project.Root, "product", [
            new("en.toml", "en", hash, [new(TranslationLocaleEditKind.SetValue, "welcome", "Welcome")]),
            new("en.toml", "en", hash, [new(TranslationLocaleEditKind.SetValue, "application_title", "Title")])]);
        Assert.Equal(1, plan.Edits.Count);
        File.AppendAllText(path, "# external\n");
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceTransaction.Commit(plan), "changed after");
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceMutation.ApplyLocaleEdits(project.Root, "product", [new("en.toml", "en", hash, [new(TranslationLocaleEditKind.SetValue, "welcome", "Oops")])]), "changed after");
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(new(project.Root, "product", TranslationKeyMutationKind.Duplicate, "welcome", "copy")));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MutateKey(new(project.Root, "product", TranslationKeyMutationKind.Delete, "welcome", null)));
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.RemoveLocale(new(project.Root, "product", "de", "en")));
        Assert.False(File.Exists(Path.Combine(project.Root, "de.toml")), "Removed locale file remains.");
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", File.ReadAllBytes(path)), "en");
        Assert.True(document.Entries.Any(entry => entry.Key == "copy") && document.Entries.All(entry => entry.Key != "welcome"), "Key lifecycle failed.");
    }

    private static void Migration()
    {
        using var project = new Workspace(legacy: true);
        string originalPath = Path.Combine(project.Root, "en", "message.mf2");
        byte[] original = File.ReadAllBytes(originalPath);
        byte[] config = File.ReadAllBytes(Path.Combine(project.Root, "runic.json"));
        var first = TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product");
        var second = TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product");
        Assert.False(File.Exists(Path.Combine(project.Root, "en.toml")), "Dry-run wrote a destination.");
        Assert.Equal(string.Join('|', first.Edits.Select(edit => edit.RelativePath)), string.Join('|', second.Edits.Select(edit => edit.RelativePath)));
        for (int i = 0; i < first.Edits.Count; i++) Assert.True((first.Edits[i].GetUtf8Bytes() ?? []).AsSpan().SequenceEqual(second.Edits[i].GetUtf8Bytes() ?? []), "Migration is nondeterministic.");
        try { TranslationWorkspaceTransaction.CommitForTesting(first, first.Edits.Count); }
        catch (Exception) { }
        TranslationWorkspaceTransaction.Recover(project.Root, TranslationWorkspaceRecoveryMode.Rollback);
        Assert.True(original.AsSpan().SequenceEqual(File.ReadAllBytes(originalPath)), "Rollback changed original MF2 bytes.");
        Assert.True(config.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(project.Root, "runic.json"))), "Rollback changed original config bytes.");
        TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product"));
        var parsed = TranslationLocaleReader.Read(new TranslationSource("en.toml", File.ReadAllBytes(Path.Combine(project.Root, "en.toml"))), "en");
        Assert.True(original.AsSpan().SequenceEqual(parsed.Entries.Single().Message.GetUtf8Bytes()), "Migration changed decoded source bytes.");
        Assert.False(File.Exists(originalPath), "Migration retained an active legacy source.");
    }

    private static void MigrationCollision()
    {
        using var project = new Workspace(legacy: true);
        File.WriteAllText(Path.Combine(project.Root, "en.toml"), "# occupied");
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product"), "collides");
        Assert.True(File.Exists(Path.Combine(project.Root, "en", "message.mf2")), "Collision removed original input.");
    }

    private static void UppercaseLocaleSources()
    {
        using var project = new Workspace();
        string lower = Path.Combine(project.Root, "en.toml");
        string upper = Path.Combine(project.Root, "en.TOML");
        File.Move(lower, upper);
        var plan = TranslationWorkspaceMutation.CreateKey(new(project.Root, "product", "greeting", "Hello"));
        Assert.Equal("en.TOML", plan.Edits.Single().RelativePath);
        Assert.Equal(TranslationWorkspaceEditKind.Replace, plan.Edits.Single().Kind);
        TranslationWorkspaceTransaction.Commit(plan);
        var document = TranslationLocaleReader.Read(new TranslationSource("en.TOML", File.ReadAllBytes(upper)), "en");
        Assert.True(document.Entries.Any(entry => entry.Key == "greeting"), "Uppercase locale file was omitted.");
        Directory.CreateDirectory(Path.Combine(project.Root, "en"));
        File.WriteAllText(Path.Combine(project.Root, "en", "mixed.MF2"), "Mixed");
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceMutation.CreateKey(new(project.Root, "product", "another", "Another")), "mixed");
    }

    private static void UppercaseMigrationSources()
    {
        using var project = new Workspace(legacy: true);
        string lower = Path.Combine(project.Root, "en", "message.mf2");
        string upper = Path.Combine(project.Root, "en", "message.MF2");
        File.Move(lower, upper);
        byte[] original = File.ReadAllBytes(upper);
        var plan = TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product");
        Assert.True(plan.Edits.Any(edit => edit.RelativePath == "en/message.MF2" && edit.Kind == TranslationWorkspaceEditKind.Delete), "Uppercase legacy source was omitted.");
        var locale = plan.Edits.Single(edit => edit.RelativePath == "en.toml");
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", locale.GetUtf8Bytes()!), "en");
        Assert.True(original.AsSpan().SequenceEqual(document.Entries.Single().Message.GetUtf8Bytes()), "Uppercase legacy message bytes changed.");
        File.WriteAllText(Path.Combine(project.Root, "en.TOML"), "# occupied");
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceMutation.MigrateToLocaleToml(project.Root, "product"), "collides");
        Assert.True(File.Exists(upper), "Collision changed the original legacy source.");
    }

    private static void StagingBudget()
    {
        using var project = new Workspace();
        var valid = TranslationWorkspaceMutation.CreateKey(new(project.Root, "product", "new_key", "New"));
        var oversized = new TranslationWorkspaceTransactionPlan(project.Root, "product", [
            new TranslationWorkspaceEdit("first.toml", TranslationWorkspaceEditKind.Create, null, Encoding.UTF8.GetBytes("a='A'")),
            new TranslationWorkspaceEdit("oversized.toml", TranslationWorkspaceEditKind.Create, null, new byte[8 * 1024 * 1024 + 1])], valid.Compilation);
        string before = string.Join('|', Directory.EnumerateFiles(project.Root).Order(StringComparer.Ordinal));
        Assert.Throws<TranslationAuthoringException>(() => TranslationWorkspaceTransaction.Commit(oversized), "byte limit");
        Assert.Equal(before, string.Join('|', Directory.EnumerateFiles(project.Root).Order(StringComparer.Ordinal)), "Budget rejection staged or created files.");
    }

    private static TranslationSource Source(string content) => new("en.toml", Encoding.UTF8.GetBytes(content));

    private sealed class Workspace : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "runic-toml-" + Guid.NewGuid().ToString("N"));
        public Workspace(bool legacy = false)
        {
            if (!legacy) TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(new(Root, "product", "en", "Example.Product", "ProductText")));
            else
            {
                Directory.CreateDirectory(Path.Combine(Root, "en"));
                File.WriteAllText(Path.Combine(Root, "runic.json"), "{\"schemaVersion\":1,\"catalog\":\"product\",\"baseLocale\":\"en\",\"locales\":[\"en\"],\"code\":{\"namespace\":\"Example.Product\",\"className\":\"ProductText\"}}");
                File.WriteAllText(Path.Combine(Root, "en", "message.mf2"), "  Hello 😀\r\n", new UTF8Encoding(false));
            }
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
