using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Editor;
using TUnit.Core;

namespace Runic.Translations.Authoring.Tests;

internal sealed class EditorStateTests
{
    [Test, DisplayName("Editor state is optional deterministic and revision checked")]
    public void RoundTrip()
    {
        using TemporaryDirectory directory = new();
        TranslationEditorStateLoadResult missing = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(missing.Error is null && missing.Revision is null && missing.State.Entries.Count == 0,
            "A missing optional sidecar did not load as empty state.");
        var state = new TranslationEditorState("product",
            [
                new("Common.Save", "de", "approved", "Reviewed", "source:123", new Dictionary<string, string> { ["count"] = "2" }),
                new("Common.Save", "en", "needs-review", null, "source:123", new Dictionary<string, string>()),
            ],
            [new("Save", "Speichern", "de", "Product terminology")]);
        TranslationEditorStateLoadResult saved = TranslationEditorStateStore.Save(directory.Path, state, null);
        TranslationEditorStateLoadResult loaded = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(loaded.Error is null && loaded.Revision == saved.Revision && loaded.SchemaVersion == 2, loaded.Error ?? "Sidecar revision changed.");
        // Version 2 keeps notes and samples only; review states and fingerprints belong to the SDK review file.
        Assert.Equal(1, loaded.State.Entries.Count);
        Assert.True(loaded.State.Entries[0] is { State: null, SourceFingerprint: null, Note: "Reviewed" } && loaded.State.Entries[0].Samples["count"] == "2",
            "The version 2 entry kept a review state or lost its note or samples.");
        Assert.True(!File.ReadAllText(System.IO.Path.Combine(directory.Path, saved.Path)).Contains("\"state\"", StringComparison.Ordinal), "Version 2 wrote a review state.");
        Assert.Equal("Speichern", loaded.State.Terminology[0].Preferred);
        byte[] first = File.ReadAllBytes(System.IO.Path.Combine(directory.Path, saved.Path));
        TranslationEditorStateStore.Save(directory.Path, loaded.State, loaded.Revision);
        Assert.True(first.AsSpan().SequenceEqual(File.ReadAllBytes(System.IO.Path.Combine(directory.Path, saved.Path))),
            "Equivalent editor state did not render byte-identically.");
        Assert.Throws<TranslationEditorStateException>(
            () => TranslationEditorStateStore.Save(directory.Path, state, null), "changed on disk");
    }

    [Test, DisplayName("A version 1 editor state is still read, with its review states, for the move to the review file")]
    public void ReadsVersionOne()
    {
        using TemporaryDirectory directory = new();
        string sidecar = System.IO.Path.Combine(directory.Path, ".runic-translations", "product.editor-state.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sidecar)!);
        File.WriteAllText(sidecar,
            """{"$schema":"runic.translations.editor-state/1","catalog":"product","messages":{"Save":{"de":{"state":"approved","sourceFingerprint":"fnv1a64:1","note":"Ok"}}},"terminology":[]}""",
            new UTF8Encoding(false));
        TranslationEditorStateLoadResult loaded = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(loaded.Error is null && loaded.SchemaVersion == 1 && loaded.State.Entries.Single() is { State: "approved", SourceFingerprint: "fnv1a64:1", Note: "Ok" },
            loaded.Error ?? "The version 1 entry was not read.");
        File.WriteAllText(sidecar,
            """{"$schema":"runic.translations.editor-state/2","catalog":"product","messages":{"Save":{"de":{"state":"approved"}}},"terminology":[]}""",
            new UTF8Encoding(false));
        Assert.True(TranslationEditorStateStore.Load(directory.Path, "product").Error?.Contains("Unknown editor-state member", StringComparison.Ordinal) == true,
            "Version 2 accepted a review state.");
    }

    [Test, DisplayName("Malformed editor state is isolated from compiler inputs")]
    public void Malformed()
    {
        using TemporaryDirectory directory = new();
        string sidecar = System.IO.Path.Combine(directory.Path, ".runic-translations", "product.editor-state.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sidecar)!);
        File.WriteAllText(sidecar, "{", new UTF8Encoding(false));
        TranslationEditorStateLoadResult result = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(result.Error is not null && result.State.Entries.Count == 0,
            "Malformed editor state was not isolated as an empty optional state.");

        TranslationEditorStateLoadResult second = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(second.Error is not null && second.State.Entries.Count == 0,
            "Malformed optional state did not remain isolated from MF2 project inputs.");
    }

    [Test, DisplayName("Editor state bounds and paths reject hostile data")]
    public void Hostile()
    {
        using TemporaryDirectory directory = new();
        Assert.Throws<TranslationEditorStateException>(
            () => TranslationEditorStateStore.Load(directory.Path, "../escape"), "catalog ID");
        var duplicate = new TranslationEditorState("product",
            [
                new("Save", "de", "draft", null, null, new Dictionary<string, string>()),
                new("Save", "de", "approved", null, null, new Dictionary<string, string>()),
            ], []);
        Assert.Throws<TranslationEditorStateException>(
            () => TranslationEditorStateStore.Save(directory.Path, duplicate, null), "Duplicate");

        string sidecar = System.IO.Path.Combine(directory.Path, ".runic-translations", "product.editor-state.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(sidecar)!);
        File.WriteAllText(sidecar,
            """{"$schema":"runic.translations.editor-state/1","catalog":"product","catalog":"other","messages":{},"terminology":[]}""",
            new UTF8Encoding(false));
        TranslationEditorStateLoadResult duplicateProperty = TranslationEditorStateStore.Load(directory.Path, "product");
        Assert.True(duplicateProperty.Error?.Contains("Duplicate", StringComparison.Ordinal) == true,
            "Duplicate sidecar properties were not isolated as malformed editor state.");
    }

    // The case measures a 15-second budget, so it does not share the processors with other cases.
    [Test, NotInParallel, DisplayName("Editor state handles 50000 key-locale reviews within its budget")]
    public void Scale()
    {
        using TemporaryDirectory directory = new();
        var entries = new List<TranslationEditorStateEntry>(TranslationEditorStateStore.MaximumEntries);
        for (int key = 0; key < 500; key++)
            for (int locale = 0; locale < 100; locale++)
                entries.Add(new TranslationEditorStateEntry(
                    $"Group.Message{key:D4}", $"x-{locale:D3}", null, "Checked", null,
                    new Dictionary<string, string>()));
        var state = new TranslationEditorState("scale", entries, []);
        Stopwatch timer = Stopwatch.StartNew();
        TranslationEditorStateLoadResult saved = TranslationEditorStateStore.Save(directory.Path, state, null);
        TranslationEditorStateLoadResult loaded = TranslationEditorStateStore.Load(directory.Path, "scale");
        timer.Stop();
        Assert.True(loaded.Error is null && loaded.State.Entries.Count == TranslationEditorStateStore.MaximumEntries,
            loaded.Error ?? "The scale sidecar lost entries.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15),
            $"The 50,000-entry editor-state round trip exceeded 15 seconds ({timer.Elapsed}).");
        Assert.True(new FileInfo(System.IO.Path.Combine(directory.Path, saved.Path)).Length <= TranslationEditorStateStore.MaximumBytes,
            "The representative scale sidecar exceeded its documented bound.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "runic-editor-state-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
    }
}
