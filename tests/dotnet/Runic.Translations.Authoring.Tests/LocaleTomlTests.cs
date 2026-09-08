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
        runner.Add("TOML multiline values remain readable with exact message bytes", ReadableValues);
        runner.Add("TOML new documents group prefixes without identifier collisions", GroupedRendering);
        runner.Add("TOML grouped edits preserve scope and unchanged table bytes", GroupedEdits);
        runner.Add("TOML additions and moves prefer deepest existing groups", ExistingGroupInsertion);
        runner.Add("TOML inline table edits preserve neighbors and identity", InlineEdits);
        runner.Add("TOML inline deletions and additions preserve valid separators", InlineDeletion);
        runner.Add("TOML nested inline moves preserve groups and comments", NestedInlineEdits);
        runner.Add("TOML array-table CRUD preserves stable row identities and order", ArrayTableEdits);
        runner.Add("TOML nested array-table edits bind stable parent rows", NestedArrayTableEdits);
        runner.Add("TOML inline array-table edits preserve row metadata", InlineArrayTableEdits);
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

    private static void ReadableValues()
    {
        string[] values = ["line one\nline two", "\nleading newline\n", "\r\nleading CRLF\r\n", "a\r\nb\nc\r", "apostrophe's\nvalue'", "end\n''", "three ''' quotes\n\"\"\" double", "\\path\nline \\ end", "control\u0001\nDEL\u007f", "\tleading tab\n😀 café", "\n", "\r\n", "''''\n\\\"\t"];
        foreach (string value in values)
        {
            string encoded = TranslationLocaleWriter.EncodeValue(value);
            Assert.True(encoded.StartsWith("'''\n", StringComparison.Ordinal) || encoded.StartsWith("\"\"\"\n", StringComparison.Ordinal), "Multiline message was flattened into escaped newline text.");
            var parsed = TranslationLocaleReader.Read(Source("message = " + encoded), "en");
            Assert.True(parsed.Success, "Readable multiline token did not parse: " + encoded);
            Assert.True(Encoding.UTF8.GetBytes(value).AsSpan().SequenceEqual(parsed.Entries.Single().Message.GetUtf8Bytes()), "Readable value changed decoded bytes: " + encoded);
        }
        Assert.Equal("'A short message'", TranslationLocaleWriter.EncodeValue("A short message"));
    }

    private static void GroupedRendering()
    {
        var entries = new System.Collections.Generic.KeyValuePair<string, string>[] {
            new("application_title", "Title"), new("validation_form_required", "Required\nDetails"),
            new("standalone", "Standalone"), new("standalone_child", "Still root"), new("_private", "Private") };
        byte[] bytes = TranslationLocaleWriter.Render(entries);
        string text = Encoding.UTF8.GetString(bytes);
        Assert.True(text.Contains("[application]\ntitle = 'Title'", StringComparison.Ordinal), "Application prefix was not grouped.");
        Assert.True(text.Contains("[validation]\nform_required = '''", StringComparison.Ordinal), "Validation prefix was not grouped.");
        Assert.True(text.Contains("standalone_child = 'Still root'", StringComparison.Ordinal), "Root/table name collision was not avoided.");
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", bytes), "en");
        Assert.True(document.Success, "Rendered groups did not parse.");
        foreach (var entry in entries) Assert.Equal(entry.Value, Encoding.UTF8.GetString(document.Entries.Single(item => item.Key == entry.Key).Message.GetUtf8Bytes()));
        Assert.True(bytes.AsSpan().SequenceEqual(TranslationLocaleWriter.Render(entries.Reverse())), "Group order was nondeterministic.");
    }

    private static void GroupedEdits()
    {
        const string original = "# document\r\n[application] # app\r\ntitle = 'Title' # retain\r\n[validation.form]\r\nrequired = 'Required'\r\n[empty]\r\n";
        byte[] added = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "only_source", "Only")]);
        Assert.Equal("# document\r\nonly_source = 'Only'\r\n" + original[12..], Encoding.UTF8.GetString(added));
        var parsed = TranslationLocaleReader.Read(new TranslationSource("en.toml", added), "en");
        Assert.True(parsed.Entries.Any(entry => entry.Key == "only_source"), "Root add inherited the last table.");
        byte[] renamed = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Rename, "validation_form_required", TargetKey: "validation_form_pending")]);
        Assert.Equal(original.Replace("required =", "pending =", StringComparison.Ordinal), Encoding.UTF8.GetString(renamed));
        byte[] moved = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Rename, "application_title", TargetKey: "window_caption")]);
        parsed = TranslationLocaleReader.Read(new TranslationSource("en.toml", moved), "en");
        Assert.True(parsed.Success && parsed.Entries.Any(entry => entry.Key == "window_caption") && parsed.Entries.All(entry => entry.Key != "application_title"), "Cross-group rename changed identity incorrectly.");
        Assert.True(Encoding.UTF8.GetString(moved).Contains("[application] # app\r\n # retain\r\n[validation.form]\r\nrequired = 'Required'\r\n[empty]\r\n", StringComparison.Ordinal), "Cross-group rename changed unrelated bytes.");
        byte[] empty = TranslationLocaleWriter.Apply(Source("# empty table\n[empty]\n"), "en", [new(TranslationLocaleEditKind.Add, "root", "Root")]);
        Assert.True(TranslationLocaleReader.Read(new TranslationSource("en.toml", empty), "en").Entries.Single().Key == "root", "Add into empty-table document inherited table scope.");
    }

    private static void ExistingGroupInsertion()
    {
        const string original = "[validation]\nsummary='Summary'\n[validation.form]\nrequired='Required'\n  [other] # next\nvalue='Other'\n";
        byte[] added = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "validation_form_pending", "Pending")]);
        Assert.Equal(original.Replace("  [other]", "pending = 'Pending'\n  [other]", StringComparison.Ordinal), Encoding.UTF8.GetString(added));
        byte[] moved = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Rename, "other_value", TargetKey: "validation_form_optional")]);
        var parsed = TranslationLocaleReader.Read(new TranslationSource("en.toml", moved), "en");
        var entry = parsed.Entries.Single(item => item.Key == "validation_form_optional");
        Assert.Equal("validation.form", string.Join('.', entry.TablePath));
        Assert.Equal("optional", string.Join('.', entry.KeyPath));
        byte[] empty = TranslationLocaleWriter.Apply(Source("[validation.form]\n[other]\nx='X'\n"), "en", [new(TranslationLocaleEditKind.Add, "validation_form_pending", "Pending")]);
        Assert.Equal("[validation.form]\npending = 'Pending'\n[other]\nx='X'\n", Encoding.UTF8.GetString(empty));
    }

    private static void InlineEdits()
    {
        const string original = "# keep 😀\nactions = { save = 'Save', cancel = \"Cancel\" } # tail\n[other]\nvalue='Untouched'\n";
        byte[] updated = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.SetValue, "actions_save", "Store"), new(TranslationLocaleEditKind.Rename, "actions_cancel", TargetKey: "actions_abort")]);
        Assert.Equal(original.Replace("'Save'", "'Store'", StringComparison.Ordinal).Replace("cancel =", "abort =", StringComparison.Ordinal), Encoding.UTF8.GetString(updated));
        updated = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "actions_retry", "Retry")]);
        Assert.Equal(original.Replace("\"Cancel\" }", "\"Cancel\", retry = 'Retry' }", StringComparison.Ordinal), Encoding.UTF8.GetString(updated));
        var parsed = TranslationLocaleReader.Read(new TranslationSource("en.toml", updated), "en");
        Assert.True(parsed.Success && parsed.Entries.Any(entry => entry.Key == "actions_retry"), "Inline add lost flattened identity.");
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "actions_save", "Collision")]), "already exists");
    }

    private static void InlineDeletion()
    {
        const string original = "actions = { first='One', middle='Two', last='Three' } # retain\n";
        foreach (string[] keys in new[] { new[] { "first" }, new[] { "middle" }, new[] { "last" }, new[] { "first", "middle" }, new[] { "middle", "last" }, new[] { "first", "middle", "last" } })
        {
            byte[] result = TranslationLocaleWriter.Apply(Source(original), "en", keys.Select(key => new TranslationLocaleEdit(TranslationLocaleEditKind.Delete, "actions_" + key)));
            var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", result), "en");
            Assert.True(document.Success, "Inline delete left invalid separators.");
            Assert.Equal(3 - keys.Length, document.Entries.Count);
            Assert.True(document.Entries.All(entry => !keys.Contains(entry.Key[8..])), "Deleted inline key remains.");
            Assert.True(Encoding.UTF8.GetString(result).EndsWith("} # retain\n", StringComparison.Ordinal), "Delete removed trailing trivia.");
        }
        byte[] replace = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.Delete, "actions_first"), new(TranslationLocaleEditKind.Delete, "actions_middle"), new(TranslationLocaleEditKind.Delete, "actions_last"),
            new(TranslationLocaleEditKind.Add, "actions_new", "New")]);
        Assert.Equal("actions_new", TranslationLocaleReader.Read(new TranslationSource("en.toml", replace), "en").Entries.Single().Key);
        byte[] trailing = TranslationLocaleWriter.Apply(Source("actions = { first='One', last='Last', }\n"), "en", [new(TranslationLocaleEditKind.Delete, "actions_last"), new(TranslationLocaleEditKind.Add, "actions_new", "New")]);
        Assert.Equal(2, TranslationLocaleReader.Read(new TranslationSource("en.toml", trailing), "en").Entries.Count);
    }

    private static void NestedInlineEdits()
    {
        const string original = "[screen]\nactions = { file = { open='Open', close='Close' }, save='Save' } # retain\n[other]\ncaption='Caption'\n";
        byte[] updated = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "screen_actions_file_retry", "Retry")]);
        Assert.Equal(original.Replace("close='Close' }", "close='Close', retry = 'Retry' }", StringComparison.Ordinal), Encoding.UTF8.GetString(updated));
        updated = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.Rename, "other_caption", TargetKey: "screen_actions_file_caption"),
            new(TranslationLocaleEditKind.Rename, "screen_actions_file_open", TargetKey: "other_open"),
            new(TranslationLocaleEditKind.Delete, "screen_actions_save")]);
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", updated), "en");
        Assert.True(document.Success, "Nested inline moves did not parse.");
        Assert.Equal("actions.file", string.Join('.', document.Entries.Single(entry => entry.Key == "screen_actions_file_caption").InlinePath));
        Assert.Equal("other", string.Join('.', document.Entries.Single(entry => entry.Key == "other_open").TablePath));
        Assert.True(document.Entries.All(entry => entry.Key != "screen_actions_save"), "Nested deletion failed.");
        const string comments = "actions = {\n first='One', # keep first comment\n last='Last', # keep last comment\n}\n";
        updated = TranslationLocaleWriter.Apply(Source(comments), "en", [new(TranslationLocaleEditKind.Delete, "actions_first"), new(TranslationLocaleEditKind.Add, "actions_new", "New\nLine")]);
        string text = Encoding.UTF8.GetString(updated);
        Assert.True(text.Contains("# keep first comment", StringComparison.Ordinal) && text.Contains("# keep last comment", StringComparison.Ordinal), "Inline edits removed comments.");
        Assert.Equal("New\nLine", Encoding.UTF8.GetString(TranslationLocaleReader.Read(new TranslationSource("en.toml", updated), "en").Entries.Single(entry => entry.Key == "actions_new").Message.GetUtf8Bytes()));
    }

    private static void ArrayTableEdits()
    {
        const string original = "# rows\n[[cards]]\n_id = 'first' # stable\ntitle='One'\n[[cards]]\n_id = 'second'\ntitle='Two'\n";
        byte[] added = TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "cards_first_subtitle", "Subtitle")]);
        Assert.Equal(original.Replace("title='One'\n", "title='One'\nsubtitle = 'Subtitle'\n", StringComparison.Ordinal), Encoding.UTF8.GetString(added));
        byte[] changed = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.SetValue, "cards_first_title", "First"),
            new(TranslationLocaleEditKind.Rename, "cards_second_title", TargetKey: "cards_second_caption")]);
        Assert.Equal(original.Replace("'One'", "'First'", StringComparison.Ordinal).Replace("title='Two'", "caption='Two'", StringComparison.Ordinal), Encoding.UTF8.GetString(changed));
        byte[] moved = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.Delete, "cards_first_title"),
            new(TranslationLocaleEditKind.Rename, "cards_second_title", TargetKey: "cards_first_moved")]);
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", moved), "en");
        Assert.True(document.Success, "Array row edits did not parse.");
        Assert.Equal("cards_first_moved", document.Entries.Single().Key);
        string text = Encoding.UTF8.GetString(moved);
        Assert.True(text.IndexOf("_id = 'first' # stable", StringComparison.Ordinal) < text.IndexOf("_id = 'second'", StringComparison.Ordinal), "Array row identities/order changed.");
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "cards_first__id", "alias")]), "metadata");
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Rename, "cards_first_title", TargetKey: "cards_first__id")]), "metadata");
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.SetValue, "cards_first__id", "other")]), "does not exist");
    }

    private static void NestedArrayTableEdits()
    {
        const string original = "[[menus]]\n_id='file'\nlabel='File'\n[[menus.items]]\n_id='open'\nlabel='Open'\n[[menus]]\n_id='edit'\nlabel='Edit'\n[[menus.items]]\n_id='copy'\nlabel='Copy'\n";
        byte[] changed = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.Add, "menus_file_items_open_hint", "Open hint"),
            new(TranslationLocaleEditKind.SetValue, "menus_edit_items_copy_label", "Copy item")]);
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", changed), "en");
        Assert.True(document.Success, "Nested array rows did not parse.");
        Assert.Equal("menus.file.items.open", string.Join('.', document.Entries.Single(entry => entry.Key == "menus_file_items_open_hint").TablePath));
        Assert.Equal("Copy item", Encoding.UTF8.GetString(document.Entries.Single(entry => entry.Key == "menus_edit_items_copy_label").Message.GetUtf8Bytes()));
        Assert.Equal(original.Replace("label='Open'\n", "label='Open'\nhint = 'Open hint'\n", StringComparison.Ordinal).Replace("'Copy'", "'Copy item'", StringComparison.Ordinal), Encoding.UTF8.GetString(changed));
    }

    private static void InlineArrayTableEdits()
    {
        const string original = "cards = [{ _id='first', title='One' }, { _id='second', title='Two' }] # rows\n_id='Ordinary message'\n";
        byte[] changed = TranslationLocaleWriter.Apply(Source(original), "en", [
            new(TranslationLocaleEditKind.Delete, "cards_first_title"),
            new(TranslationLocaleEditKind.Add, "cards_first_subtitle", "Subtitle"),
            new(TranslationLocaleEditKind.Rename, "cards_second_title", TargetKey: "cards_second_caption"),
            new(TranslationLocaleEditKind.SetValue, "_id", "Still a message")]);
        var document = TranslationLocaleReader.Read(new TranslationSource("en.toml", changed), "en");
        Assert.True(document.Success, "Inline array row edits did not parse.");
        Assert.Equal("cards.first", string.Join('.', document.Entries.Single(entry => entry.Key == "cards_first_subtitle").InlinePath));
        Assert.Equal("Two", Encoding.UTF8.GetString(document.Entries.Single(entry => entry.Key == "cards_second_caption").Message.GetUtf8Bytes()));
        Assert.Equal("Still a message", Encoding.UTF8.GetString(document.Entries.Single(entry => entry.Key == "_id").Message.GetUtf8Bytes()));
        string text = Encoding.UTF8.GetString(changed);
        Assert.True(text.Contains("_id='first'", StringComparison.Ordinal) && text.Contains("_id='second'", StringComparison.Ordinal), "Inline row metadata changed.");
        Assert.Throws<TranslationAuthoringException>(() => TranslationLocaleWriter.Apply(Source(original), "en", [new(TranslationLocaleEditKind.Add, "cards_first__id", "alias")]), "metadata");
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
