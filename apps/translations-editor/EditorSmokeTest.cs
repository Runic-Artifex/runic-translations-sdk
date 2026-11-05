using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Testing;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;
using Runic.CommandLine;
using Runic.Platform;
using Runic.Navigation;
using System.Xml.Linq;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    private static async Task AuthoringJourneyAsync(string project)
    {
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "authoring-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        const string source = "# Cart count context 🦊\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{One item}}\n  * {{Many items}}\n";
        const string target = "# Warenkorbkontext\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{Ein Artikel}}\n  * {{Viele Artikel}}\n";
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), source).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), target).ConfigureAwait(false);
        using var session = new EditorSession(project);
        var snapshot = await session.LoadAsync().ConfigureAwait(false);
        Require(snapshot.Success, "Authoring fixture did not compile.");
        var document = snapshot.Documents.Single(document => document.Path == "de.rmf2");
        var entry = document.Entries!.Single(entry => entry.Key == "cart");
        Require(entry.Authoring?.Supported == true && entry.Authoring.Variants.Count == 2, "Existing plural wasn't projected.");
        Require(entry.Context?.Comments.Count == 1 && entry.Context.Comments[0] == "Warenkorbkontext" && entry.Context.Tags.Contains("checkout") && entry.Context.Examples.Count == 2,
            "Context/tags/examples were dropped.");
        Require(entry.Semantic?.Text.Count == 2 && entry.Semantic.Text[0] == "Ein Artikel" && entry.Semantic.Text[1] == "Viele Artikel" && entry.Semantic.Placeholders.Count == 1 && entry.Semantic.Placeholders[0] == "count:int64", "Semantic QA projection wasn't typed branch text.");
        var draft = await session.ApplyAuthoringOperationAsync(document.Path, document.Content, entry.Key,
            entry.Authoring!.Revision, new EditorMessageOperation("set-pattern", "0", "Genau ein Artikel")).ConfigureAwait(false);
        Require(draft.Success, "Branch edit didn't compile.");
        Require(draft.Content.StartsWith("# Warenkorbkontext\n@tag checkout\n@example {\"count\":1}\n@example {\"count\":2}\n", StringComparison.Ordinal), "Untouched metadata wasn't retained byte-for-byte.");
        var changed = draft.Entries.Single();
        Require(changed.Context?.Examples.Count == 2, "Draft examples were dropped.");
        var one = await session.PreviewMessageAsync(document.Path, draft.Content, "de", "cart", JsonSerializer.Serialize(changed.Context!.Examples[0])).ConfigureAwait(false);
        var many = await session.PreviewMessageAsync(document.Path, draft.Content, "de", "cart", JsonSerializer.Serialize(changed.Context.Examples[1])).ConfigureAwait(false);
        Require(one.RenderedJson?.Contains("Genau ein Artikel", StringComparison.Ordinal) == true && many.RenderedJson?.Contains("Viele Artikel", StringComparison.Ordinal) == true,
            "Selected canonical examples didn't choose the expected plural branches.");
        var stale = await session.ApplyAuthoringOperationAsync(document.Path, draft.Content, "cart", entry.Authoring.Revision,
            new EditorMessageOperation("set-pattern", "0", "Stale")).ConfigureAwait(false);
        Require(!stale.Success && stale.Content == draft.Content && stale.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-AUTHORING"), "Stale authoring edit was accepted.");
        Require((await session.SaveAsync(document.Path, draft.Content, document.Revision).ConfigureAwait(false)).Ok, "Authoring source wasn't saved.");
        var reloaded = (await session.LoadAsync().ConfigureAwait(false)).Documents.Single(document => document.Path == "de.rmf2");
        Require(reloaded.Entries!.Single().Authoring?.Variants[0].Pattern == "Genau ein Artikel", "Branch edit didn't survive save/reload.");
        // Input renames update attached caller examples and @param metadata.
        var rename = await session.ApplyAuthoringOperationAsync("en.rmf2", source, "cart", snapshot.Documents.Single(document => document.Path == "en.rmf2").Entries!.Single().Authoring!.Revision,
            new EditorMessageOperation("rename-input", Name: "count", NewName: "amount")).ConfigureAwait(false);
        Require(rename.Content.Contains("\"amount\":1", StringComparison.Ordinal) && !rename.Content.Contains("\"count\":1", StringComparison.Ordinal), "Input rename didn't update example contracts.");
        const string broken = "# 🦊 incomplete\nempty =\n";
        var validation = await session.ValidateAsync("de.rmf2", broken).ConfigureAwait(false);
        var diagnostic = validation.Diagnostics.Single(diagnostic => diagnostic.QuickFixes?.Count == 1);
        Require(diagnostic.Span?.StartUtf16 == broken.IndexOf("empty", StringComparison.Ordinal), "Diagnostic selection wasn't physical UTF16.");
        var fix = diagnostic.QuickFixes!.Single();
        var repaired = await session.ApplyDiagnosticFixAsync("de.rmf2", broken, fix).ConfigureAwait(false);
        Require(repaired.Content == "# 🦊 incomplete\nempty = {{}}\n" && !repaired.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-FIX"), "Safe repair didn't preserve unrelated content.");
        var staleFix = await session.ApplyDiagnosticFixAsync("de.rmf2", broken + "other = New\n", fix).ConfigureAwait(false);
        Require(staleFix.Diagnostics.Any(diagnostic => diagnostic.Id == "EDITOR-FIX"), "Stale repair wasn't refused as an operation result.");
    }

    private static async Task MountedContextJourneyAsync(string root)
    {
        string project = Path.Combine(root, "translations"), feature = Path.Combine(root, "feature");
        Directory.CreateDirectory(project); Directory.CreateDirectory(feature);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"),
            "{\"schemaVersion\":1,\"catalog\":\"context\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[\"en\"],\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n").ConfigureAwait(false);
        const string text = "# Checkout context 🦊\n@tag checkout\n@example {\"count\":1}\ncart =\n  .input {$count :integer}\n  .match $count\n  one {{One item}}\n  * {{Many items}}\n";
        await File.WriteAllTextAsync(Path.Combine(feature, "en.rmf2"), text).ConfigureAwait(false);
        using var session = new EditorSession(root);
        var loaded = await session.LoadAsync().ConfigureAwait(false);
        var document = loaded.Documents.Single(document => document.Path == "feature/en.rmf2");
        var entry = document.Entries!.Single();
        Require(loaded.Success && entry.Key == "shop_cart" && entry.Context?.Comments.Single() == "Checkout context 🦊" && entry.Context.Examples.Single()["count"] == "1", "Mounted context/examples weren't exposed under compiler identity.");
        var draft = await session.ApplyAuthoringOperationAsync(document.Path, document.Content, entry.Key,
            entry.Authoring!.Revision, new("set-pattern", "0", "Exactly one item")).ConfigureAwait(false);
        var changed = draft.Entries.Single();
        Require(changed.Context?.Tags.Single() == "checkout" && changed.Context.Examples.Single()["count"] == "1", "Unsaved mounted context/examples weren't retained.");
        var preview = await session.PreviewMessageAsync(document.Path, draft.Content, "en", entry.Key,
            JsonSerializer.Serialize(changed.Context!.Examples.Single())).ConfigureAwait(false);
        Require(preview.RenderedJson?.Contains("Exactly one item", StringComparison.Ordinal) == true, "Mounted selected example wasn't previewed canonically.");
    }

    private static async Task MalformedManifestJourneyAsync(string root)
    {
        // A hand-edited or half-saved runic.json must produce diagnostics, not
        // exceptions, in load, mutation planning and the external-change scan,
        // and the editor must recover once the manifest is fixed.
        string project = Path.Combine(root, "translations"), feature = Path.Combine(root, "feature");
        Directory.CreateDirectory(project); Directory.CreateDirectory(feature);
        const string valid = "{\"schemaVersion\":1,\"catalog\":\"manifest\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"ManifestText\"},\"baseLocale\":\"en\",\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n";
        string manifest = Path.Combine(project, "runic.json");
        await File.WriteAllTextAsync(manifest, valid).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(feature, "en.rmf2"), "title = Shop\n").ConfigureAwait(false);
        using var session = new EditorSession(root);
        WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
        Require(initial.Success && initial.Documents.Any(document => document.Path == "feature/en.rmf2"), "The manifest fixture did not load.");
        await DrainExternalChangesAsync(session, null).ConfigureAwait(false);

        string Mount(string path) => valid.Replace("\"path\":\"../feature\"", "\"path\":\"" + path + "\"", StringComparison.Ordinal);
        string noMount = TranslationManifestReader.SourceRootWithoutPath;
        // Message is the manifest diagnostic the load must report; Mutation is
        // the reason create-key planning must give, when it gets that far.
        var cases = new (string Name, string Content, string Message, string? Mutation)[] {
            ("array root", "[\"not\",\"an\",\"object\"]\n", "Runic project root must be an object.", null),
            ("missing path", valid.Replace("\"path\":\"../feature\",", "", StringComparison.Ordinal), "Missing required member 'path'.", noMount),
            ("whitespace path", Mount("   "), noMount, noMount),
            ("non-object mount", valid.Replace("[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", "[42]", StringComparison.Ordinal), "Invalid source mount.", noMount),
            ("non-array sourceRoots", valid.Replace("[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]", "{\"path\":\"../feature\",\"namespace\":[\"shop\"]}", StringComparison.Ordinal),
                "sourceRoots requires a nonempty array of mounts.", TranslationManifestReader.SourceRootsNotArray),
            ("half-saved JSON", valid[..(valid.Length / 2)], "Expected a JSON value.", null),
            // A path still being typed names a directory that does not exist yet.
            ("missing source root", Mount("../featur"), EditorWorkspace.SourceRootMissing("featur"),
                EditorWorkspace.SourceRootMissing("featur")),
            ("escaping source root", Mount("../../.."), EditorWorkspace.SourceRootOutsideWorkspace("../.."),
                EditorWorkspace.SourceRootOutsideWorkspace("../..")),
            ("missing baseLocale", valid.Replace("\"baseLocale\":\"en\",", "", StringComparison.Ordinal), "Missing required member 'baseLocale'.", TranslationManifestReader.MissingBaseLocale),
        };
        foreach (var malformed in cases)
        {
            await File.WriteAllTextAsync(manifest, malformed.Content).ConfigureAwait(false);
            // The scan must report the manifest change and then settle; a scan
            // that throws would fail again on every subsequent check. Scan before
            // loading: a load resets the external-change baseline.
            await DrainExternalChangesAsync(session, "translations/runic.json").ConfigureAwait(false);
            await session.CheckExternalChangesAsync().ConfigureAwait(false);
            WorkspaceSnapshot broken = await session.LoadAsync().ConfigureAwait(false);
            EditorDiagnostic[] manifestErrors = broken.Diagnostics
                .Where(diagnostic => diagnostic.Path == "translations/runic.json" && diagnostic.Severity == "error").ToArray();
            Require(!broken.Success && manifestErrors.Length != 0, $"A malformed manifest ({malformed.Name}) did not produce a manifest diagnostic.");
            Require(manifestErrors.Any(diagnostic => diagnostic.Message == malformed.Message),
                $"The {malformed.Name} manifest diagnostic was not '{malformed.Message}': {string.Join(" | ", manifestErrors.Select(diagnostic => diagnostic.Message))}");
            EditorMutationPreview mutation = session.PreviewMutation(new EditorMutationRequest("create-key", null, null, null, null, null, "shop.subtitle", "Subtitle"));
            Require(!mutation.Ok, $"Mutation planning accepted a malformed manifest ({malformed.Name}).");
            Require(malformed.Mutation is null || mutation.Message?.Detail == malformed.Mutation,
                $"Mutation planning for the {malformed.Name} manifest did not refuse with '{malformed.Mutation}': {mutation.Message?.Code} {mutation.Message?.Detail}");
        }

        await File.WriteAllTextAsync(manifest, valid).ConfigureAwait(false);
        await DrainExternalChangesAsync(session, "translations/runic.json").ConfigureAwait(false);
        WorkspaceSnapshot recovered = await session.LoadAsync().ConfigureAwait(false);
        Require(recovered.Success && recovered.Documents.Any(document => document.Path == "feature/en.rmf2"), "The editor did not recover after the manifest was fixed.");
    }

    private static async Task DrainExternalChangesAsync(EditorSession session, string? expectedPath)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        bool observed = expectedPath is null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            EditorExternalChanges changes = await session.CheckExternalChangesAsync().ConfigureAwait(false);
            observed |= changes.Paths.Contains(expectedPath);
            if (observed && changes.Paths.Count == 0) return;
            await Task.Delay(100).ConfigureAwait(false);
        }
        Require(observed, $"The external-change scan never reported '{expectedPath}'.");
    }

    // I-165: `export --output` on the command line writes a rooted path where
    // it points and a relative path under the workspace, as before. The UI
    // bridge keeps every export inside the workspace.
    private static async Task InterchangeOutputJourneyAsync(string container, IEditorCommandOperations operations)
    {
        string project = Path.Combine(container, "workspace");
        string outside = Path.Combine(container, "outside");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "output-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);
        EditorCommandRequest Export(string format, string output) => new(
            "export", [project], null, false, false, false, CommandOutputMode.Json, Format: format, Output: output);

        string xliffDirectory = Path.Combine(outside, "xliff");
        CommandOutcome<EditorCommandResult> xliff = await operations.ExecuteAsync(Export("xliff", xliffDirectory)).ConfigureAwait(false);
        string xliffFile = Path.Combine(xliffDirectory, "output-smoke.de.xliff");
        Require(xliff.Succeeded && File.Exists(xliffFile) &&
            xliff.Value?.XliffExport?.Documents.Single().Path == xliffFile &&
            !Directory.Exists(Path.Combine(project, xliffDirectory.TrimStart('/', '\\'))),
            "export --interchange xliff did not write to the rooted --output directory.");

        string reviewFile = Path.Combine(outside, "review", "catalog.review.json");
        CommandOutcome<EditorCommandResult> review = await operations.ExecuteAsync(Export("review", reviewFile)).ConfigureAwait(false);
        Require(review.Succeeded && File.Exists(reviewFile) && review.Value?.ReviewExport?.Path == reviewFile,
            "export --interchange review did not write to the rooted --output path.");

        CommandOutcome<EditorCommandResult> relative = await operations.ExecuteAsync(Export("xliff", "exports/xliff")).ConfigureAwait(false);
        Require(relative.Succeeded && File.Exists(Path.Combine(project, "exports", "xliff", "output-smoke.de.xliff")) &&
            relative.Value?.XliffExport?.Documents.Single().Path == "exports/xliff/output-smoke.de.xliff",
            "export --output with a relative path did not resolve under the workspace.");

        string bridgeTarget = Path.Combine(outside, "bridge");
        using var session = new EditorSession(project);
        EditorXliffExportResult bridge = await session.ExportXliffAsync(bridgeTarget).ConfigureAwait(false);
        Require(!Directory.Exists(bridgeTarget) && (!bridge.Ok || bridge.Documents.All(document => !Path.IsPathRooted(document.Path))),
            "The UI export wrote outside the workspace.");
    }

    // Version 1 editor approvals move into the SDK review file (<catalog>.review.json beside runic.json) on first open:
    // an approval whose recorded base value still matches stays approved, the rest become needs-review, with one notice.
    // Afterwards "stale" is the SDK per-unit fingerprint (RTR0090): editing another key leaves an approval current,
    // editing its own source makes it stale, and marking it again records the current source.
    private static async Task ReviewStampJourneyAsync(string container)
    {
        // Values from the pre-0.9 review-model.ts sourceFingerprint, run with bun.
        Require(EditorWorkspace.EditorSourceFingerprint("Hello {$name}!") == "fnv1a64:6766928709d1ff53" &&
            EditorWorkspace.EditorSourceFingerprint("Zeile\n\"zwei\"\t\\ \u0001 😀 \ud800 Ä") == "fnv1a64:f1a8bb5b9e6f39e5",
            "The editor's former review fingerprint differs from the frontend's.");
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "stamp-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\nother = Other\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "title = Titel\nother = Andere\n").ConfigureAwait(false);
        string sidecar = Path.Combine(project, ".runic-translations", "stamp-smoke.editor-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        await File.WriteAllTextAsync(sidecar, "{\"$schema\":\"runic.translations.editor-state/1\",\"catalog\":\"stamp-smoke\",\"messages\":{" +
            "\"title\":{\"de\":{\"state\":\"approved\",\"note\":\"Checked\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Title") + "\"}}," +
            "\"other\":{\"de\":{\"state\":\"approved\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Previous") + "\"}}," +
            "\"gone\":{\"de\":{\"state\":\"approved\"}}}," +
            "\"terminology\":[{\"source\":\"Title\",\"preferred\":\"Titel\"}]}").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot opened = await session.LoadAsync().ConfigureAwait(false);
        EditorReviewSnapshot review = opened.Review ?? throw new InvalidOperationException("The migrated workspace has no review snapshot.");
        Require(review.Notice is { Code: "ui_backend_review_migrated" } notice && notice.Args.Single(arg => arg.Name == "count").Value == "2" &&
            notice.Args.Single(arg => arg.Name == "demoted").Value == "1" && notice.Args.Single(arg => arg.Name == "dropped").Value == "1" &&
            notice.Args.Single(arg => arg.Name == "kept").Value == "0" &&
            notice.Args.Single(arg => arg.Name == "backup").Value == ".runic-translations/stamp-smoke.editor-state.v1.json" && review.Path == "stamp-smoke.review.json",
            "The move of editor approvals into the review file was not announced once with its counts: " + review.Notice);
        Require((await File.ReadAllTextAsync(sidecar[..^".json".Length] + ".v1.json").ConfigureAwait(false)).Contains("editor-state/1", StringComparison.Ordinal),
            "The version 1 editor state was not kept as a backup.");
        Require(review.Entries.Single(entry => entry.Key == "title") is { State: "approved", Note: "Checked", Stale: false } &&
            review.Entries.Single(entry => entry.Key == "other") is { State: "needs-review", Stale: false } && review.Terminology.Single().Preferred == "Titel",
            "The current approval was not kept or the stale one was not demoted.");
        TranslationInterchangeReview file = TranslationReviewFile.Read(Path.Combine(project, "stamp-smoke.review.json"), "stamp-smoke")
            ?? throw new InvalidOperationException("The review file was not written.");
        Require(file.Entries.Count == 2 && file.Entries.All(entry => entry.Note is null && entry.SourceFingerprint?.StartsWith("sha256:", StringComparison.Ordinal) == true),
            "The review file entries do not carry the SDK fingerprint, or the editor note moved into the review file.");
        string rewritten = await File.ReadAllTextAsync(sidecar).ConfigureAwait(false);
        Require(rewritten.Contains("editor-state/2", StringComparison.Ordinal) && !rewritten.Contains("\"state\"", StringComparison.Ordinal) && rewritten.Contains("Checked", StringComparison.Ordinal),
            "The editor state was not rewritten as version 2 with the note kept: " + rewritten);
        Require((await session.LoadAsync().ConfigureAwait(false)).Review?.Notice is null, "The migration notice was repeated.");

        async Task<string> SegmentAsync()
        {
            EditorXliffExportResult export = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(export.Ok, "The review-stamp export failed.");
            XDocument document = XDocument.Load(Path.Combine(project, export.Documents.Single().Path));
            return document.Descendants().Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "title")
                .Elements().Single(element => element.Name.LocalName == "segment").ToString(SaveOptions.DisableFormatting);
        }
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "A current approval was not exported as final.");
        EditorReviewFileResult copy = await session.ExportReviewJsonAsync(null).ConfigureAwait(false);
        Require(copy.Ok && (await File.ReadAllTextAsync(Path.Combine(project, copy.Path!)).ConfigureAwait(false)).Contains("\"Checked\"", StringComparison.Ordinal),
            "The exported review data lost the editor's note.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\nother = Other text\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "Editing another key voided an approval.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title\nother = Other text\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("subState=\"runic:needs-review\"", StringComparison.Ordinal),
            "An approval made before the source changed was exported as current.");
        WorkspaceSnapshot changed = await session.LoadAsync().ConfigureAwait(false);
        Require(changed.Review!.Entries.Single(entry => entry.Key == "title").Stale &&
            changed.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0090", Key: "title", Locale: "de" }),
            "A stale approval was not marked stale or reported as RTR0090 on its row.");

        // Marking it again records the source the editor loaded (the snapshot's fingerprint), and the note moves onto the
        // review entry, so an edited or cleared note is what XLIFF exports.
        EditorReviewSnapshot before = changed.Review!;
        string loaded = before.Fingerprints!["title"];
        EditorReviewOperationResult saved = await session.SaveReviewAsync(new EditorReviewSaveRequest(before.Revision,
            before.Entries.Select(entry => entry.Key == "title" ? entry with { SourceFingerprint = loaded } : entry).ToArray(), before.Terminology)).ConfigureAwait(false);
        Require(saved.Ok && saved.Review!.Entries.Single(entry => entry.Key == "title") is { State: "approved", Stale: false, Note: "Checked" } && saved.Review.Notice is null,
            "Marking a stale approval again did not record the loaded source: " + saved.Message);
        Require(TranslationReviewFile.Read(Path.Combine(project, "stamp-smoke.review.json"), "stamp-smoke")!.Entries.Single(entry => entry.Key == "title").Note == "Checked",
            "The note did not move onto the review entry.");
        Require(!(await session.LoadAsync().ConfigureAwait(false)).Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0090", Key: "title" }), "RTR0090 remained after the review.");
        Require((await session.UndoAsync().ConfigureAwait(false)).Ok &&
            (await session.LoadAsync().ConfigureAwait(false)).Review!.Entries.Single(entry => entry.Key == "title").Stale,
            "Undo did not restore the earlier review file.");
        Require(saved.Review!.Revision != before.Revision, "Saving did not change the review revision.");

        // The base changes on disk after the editor loaded it: an approval made against the loaded text is saved for that
        // text, so it shows as stale instead of approving text the reviewer never saw.
        WorkspaceSnapshot current = await session.LoadAsync().ConfigureAwait(false);
        EditorReviewSnapshot seen = current.Review!;
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title, again\nother = Other text\n").ConfigureAwait(false);
        EditorReviewOperationResult unseen = await session.SaveReviewAsync(new EditorReviewSaveRequest(seen.Revision,
            seen.Entries.Select(entry => entry.Key == "title" ? entry with { State = "approved", SourceFingerprint = seen.Fingerprints!["title"], Note = null } : entry).ToArray(),
            seen.Terminology)).ConfigureAwait(false);
        Require(unseen.Ok && unseen.Review!.Notice is { Code: "ui_backend_review_saved_stale" } && unseen.Review.Entries.Single(entry => entry.Key == "title") is { Stale: true, Note: null },
            "An approval of base text that changed on disk since it was loaded was not saved as stale, or the cleared note came back: " + unseen.Message);
    }

    // A catalog being translated (a locale misses keys, RTR0010 is an error by default) still has review data: version 1
    // approvals move, notes alone create no review entry, a state is recorded with its fingerprint, the RTR0095 quick fix
    // is offered, and a review file another writer changes is reported and merged with unsaved editor changes.
    private static async Task IncompleteCatalogReviewJourneyAsync(string container)
    {
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "incomplete-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("cs", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"),
            "title = Title\nlater = Later\nother = Other\nitems =\n  .input {$n :integer}\n  .match $n\n  one {{One}}\n  * {{Many}}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "cs.rmf2"), "title = Titulek\n").ConfigureAwait(false);
        string sidecar = Path.Combine(project, ".runic-translations", "incomplete-smoke.editor-state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sidecar)!);
        await File.WriteAllTextAsync(sidecar, "{\"$schema\":\"runic.translations.editor-state/1\",\"catalog\":\"incomplete-smoke\",\"messages\":{" +
            "\"title\":{\"cs\":{\"state\":\"approved\",\"sourceFingerprint\":\"" + EditorWorkspace.EditorSourceFingerprint("Title") + "\"}}},\"terminology\":[]}").ConfigureAwait(false);
        using var session = new EditorSession(project);
        WorkspaceSnapshot opened = await session.LoadAsync().ConfigureAwait(false);
        Require(!opened.Success && opened.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0010", Severity: "error" }), "The fixture compiled without an RTR0010 error.");
        EditorReviewSnapshot review = opened.Review!;
        Require(review is { MigrationPending: false, Error: null, Fingerprints.Count: 4, Notice.Code: "ui_backend_review_migrated" } &&
            review.Entries.Single() is { Key: "title", Locale: "cs", State: "approved", Stale: false },
            "Version 1 approvals did not move while the catalog has errors: " + review.Error);

        // Notes and samples save on an incomplete catalog without creating a review entry; a state is recorded with its fingerprint.
        var noteOnly = new EditorReviewEntry("later", "cs", "draft", "Ask the author", null, new Dictionary<string, string> { ["n"] = "3" }, Implicit: true);
        var approved = new EditorReviewEntry("title", "cs", "approved", null, review.Fingerprints!["title"], new Dictionary<string, string>());
        EditorReviewOperationResult saved = await session.SaveReviewAsync(new EditorReviewSaveRequest(review.Revision, [noteOnly, approved],
            [new EditorTerminologyEntry("Title", "Titulek", "cs", null)], review.Entries, review.Terminology)).ConfigureAwait(false);
        Require(saved.Ok, "Review data was not saved on an incomplete catalog: " + saved.Message);
        string reviewPath = Path.Combine(project, "incomplete-smoke.review.json");
        TranslationInterchangeReview file = TranslationReviewFile.Read(reviewPath, "incomplete-smoke")!;
        Require(file.Entries.Single() is { Key: "title", State: TranslationReviewState.Approved } entry && entry.SourceFingerprint == review.Fingerprints["title"],
            "A note alone created a review entry, or the state was not recorded with its fingerprint.");
        Require(saved.Review!.Entries.Single(entry => entry.Key == "later") is { Implicit: true, Note: "Ask the author" } && saved.Review.Terminology.Count == 1,
            "The note, samples or terminology were not kept in the editor state.");

        // The RTR0095 quick fix is offered although the catalog does not compile.
        await File.WriteAllTextAsync(Path.Combine(project, "cs.rmf2"), "title = Titulek\nitems =\n  .input {$n :integer}\n  .match $n\n  * {{Mnoho}}\n  one {{Jedna}}\n").ConfigureAwait(false);
        WorkspaceSnapshot plural = await session.LoadAsync().ConfigureAwait(false);
        Require(!plural.Success && plural.Diagnostics.Any(diagnostic => diagnostic is { Id: "RTR0095", Key: "items", Locale: "cs", QuickFixes.Count: 1 }),
            "The RTR0095 quick fix was not offered while the catalog has errors.");

        // Another writer (runic-translations review mark) records a state: the external-change check reports the new
        // review data, and a save started from the older data keeps both sides' changes.
        EditorReviewSnapshot start = plural.Review!;
        TranslationReviewFile.Update(reviewPath, "incomplete-smoke", current => TranslationReviewFile.Mark(current, "incomplete-smoke", ["other"], "cs",
            TranslationReviewState.NeedsReview, "From the CLI", start.Fingerprints!));
        EditorExternalChanges external = new(false, [], []);
        for (int attempt = 0; attempt < 50 && external.Review is null; attempt++)
        {
            await Task.Delay(100).ConfigureAwait(false);
            external = await session.CheckExternalChangesAsync().ConfigureAwait(false);
        }
        Require(external.Review?.Entries.Any(entry => entry is { Key: "other", State: "needs-review", Note: "From the CLI" }) == true,
            "The external-change check did not report the review file another writer changed.");
        EditorReviewEntry[] mine = [.. start.Entries.Select(entry => entry.Key == "later" ? entry with { Note = "Ask again" } : entry)];
        EditorReviewOperationResult merged = await session.SaveReviewAsync(new EditorReviewSaveRequest(start.Revision, mine, start.Terminology, start.Entries, start.Terminology)).ConfigureAwait(false);
        Require(merged.Ok && merged.Review!.Entries.Single(entry => entry.Key == "other") is { State: "needs-review" } &&
            merged.Review.Entries.Single(entry => entry.Key == "later") is { Note: "Ask again" },
            "A save from older review data did not merge with the other writer's change: " + merged.Message);
        // The same entry changed on both sides is refused, naming it.
        EditorReviewSnapshot latest = merged.Review!;
        TranslationReviewFile.Update(reviewPath, "incomplete-smoke", current => TranslationReviewFile.Mark(current, "incomplete-smoke", ["other"], "cs",
            TranslationReviewState.Approved, null, latest.Fingerprints!));
        EditorReviewOperationResult conflict = await session.SaveReviewAsync(new EditorReviewSaveRequest(latest.Revision,
            [.. latest.Entries.Select(entry => entry.Key == "other" ? entry with { State = "translated", SourceFingerprint = latest.Fingerprints!["other"] } : entry)],
            latest.Terminology, latest.Entries, latest.Terminology)).ConfigureAwait(false);
        Require(!conflict.Ok && conflict.Message is { Code: "ui_backend_review_conflict" } message && message.Args.Single().Value == "other (cs)",
            "An entry changed on both sides was not refused: " + conflict.Message);
    }

    // An XLIFF import goes through the Tooling plan: a key the import adds to an existing group copies the group's
    // 4-space indentation, the approval lands in the review file with the per-unit fingerprint of the source the file
    // showed, and a unit whose source changed since export is imported as needs-review instead of refusing the file.
    private static async Task ImportedApprovalJourneyAsync(string container)
    {
        string project = Path.Combine(container, "workspace");
        await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "import-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Title\ncart {\n    empty = Empty\n    items = Items\n}\n").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "cart {\n    empty = Leer\n}\n").ConfigureAwait(false);
        // The German translation starts incomplete, so the import adds keys.
        JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(project, "runic.json")).ConfigureAwait(false))!.AsObject();
        manifest["validation"] = new JsonObject { ["translationCompleteness"] = "allow" };
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"), manifest.ToJsonString()).ConfigureAwait(false);
        using var session = new EditorSession(project);
        EditorXliffExportResult export = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
        Require(export.Ok, "The import-smoke export failed: " + export.Message?.Code + " " + export.Message?.Detail);
        string relative = export.Documents.Single().Path;
        XDocument xliff = XDocument.Load(Path.Combine(project, relative));
        foreach ((string key, string text, string state) in new[] { ("title", "Titel", "final"), ("cart_items", "Artikel", "translated") })
        {
            XElement segment = xliff.Descendants().Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == key)
                .Elements().Single(element => element.Name.LocalName == "segment");
            segment.SetAttributeValue("state", state);
            segment.Elements().SingleOrDefault(element => element.Name.LocalName == "target")?.Remove();
            XElement source = segment.Elements().Single(element => element.Name.LocalName == "source");
            source.AddAfterSelf(new XElement(source.Name.Namespace + "target", text));
        }
        xliff.Save(Path.Combine(project, relative), SaveOptions.DisableFormatting);
        EditorXliffImportPlan plan = await session.PreviewXliffImportAsync(relative).ConfigureAwait(false);
        Require(plan.Ok && plan.ConfirmationToken is not null,
            "The import-smoke XLIFF import was not planned: " + string.Join("; ", plan.Refusals.Select(refusal => refusal.Code)));
        Require((await session.ApplyXliffImportAsync(plan.ConfirmationToken!).ConfigureAwait(false)).Ok, "The import-smoke XLIFF import was not applied.");
        string german = await File.ReadAllTextAsync(Path.Combine(project, "de.rmf2")).ConfigureAwait(false);
        Require(german.StartsWith("cart {\n    empty = Leer\n    items = Artikel\n}\n", StringComparison.Ordinal) && german.Contains("title = Titel\n", StringComparison.Ordinal),
            "The editor import did not copy the group's indentation: " + german);
        EditorReviewEntry imported = (await session.LoadAsync().ConfigureAwait(false)).Review!.Entries.Single(entry => entry.Key == "title" && entry.Locale == "de");
        Require(imported is { State: "approved", Stale: false } && File.Exists(Path.Combine(project, "import-smoke.review.json")),
            "The imported approval was not recorded in the review file for the current source.");

        async Task<string> SegmentAsync()
        {
            EditorXliffExportResult again = await session.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(again.Ok, "The import-smoke re-export failed.");
            return XDocument.Load(Path.Combine(project, again.Documents.Single().Path)).Descendants()
                .Single(element => element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "title")
                .Elements().Single(element => element.Name.LocalName == "segment").ToString(SaveOptions.DisableFormatting);
        }
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("state=\"final\"", StringComparison.Ordinal), "A current imported approval was not exported as final.");
        await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "title = Main title\ncart {\n    empty = Empty\n    items = Items\n}\n").ConfigureAwait(false);
        Require((await SegmentAsync().ConfigureAwait(false)).Contains("subState=\"runic:needs-review\"", StringComparison.Ordinal),
            "An imported approval whose base text changed was exported as current.");

        // The exported file still shows "Title"; after the source changed, its unit is imported as needs-review.
        EditorXliffImportPlan changedSource = await session.PreviewXliffImportAsync(relative).ConfigureAwait(false);
        Require(changedSource.Ok && changedSource.Refusals.Count == 0 &&
            changedSource.Changes.Any(change => change is { Key: "title", Kind: "state-change", StateAfter: "needs-review" }),
            "A unit whose source changed since export refused the import or was not demoted: " + string.Join("; ", changedSource.Refusals.Select(refusal => refusal.Code)));
    }

    // commandLine is the command-line operations (Program.cs), which a frontend
    // bootstrap build does not compile.
    public static async Task<int> RunAsync(string workspacePath, IEditorCommandOperations commandLine)
    {
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        try
        {
            WorkspacePickerSelection();
            await AuthoringJourneyAsync(Path.Combine(container, "authoring")).ConfigureAwait(false);
            await MountedContextJourneyAsync(Path.Combine(container, "mounted-context")).ConfigureAwait(false);
            await MalformedManifestJourneyAsync(Path.Combine(container, "malformed-manifest")).ConfigureAwait(false);
            await InterchangeOutputJourneyAsync(Path.Combine(container, "interchange-output"), commandLine).ConfigureAwait(false);
            await ReviewStampJourneyAsync(Path.Combine(container, "review-stamp")).ConfigureAwait(false);
            await IncompleteCatalogReviewJourneyAsync(Path.Combine(container, "incomplete-review")).ConfigureAwait(false);
            await ImportedApprovalJourneyAsync(Path.Combine(container, "imported-approval")).ConfigureAwait(false);
            string project = Path.Combine(container, "translations");
            // Smoke tests exercise an intentionally minimal workspace. They must
            // never mutate the packaged example or a caller-provided directory.
            await TranslationProjectWriter.CreateAsync(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(project, "editor-smoke", "en", "Smoke.Translations", "SmokeText",
                    [new TranslationProjectLocale("de", "en")]))).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);

            using var session = new EditorSession(project);
            WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
            Require(initial.Success, "The RMF2 project did not load.");
            EditorDocument german = initial.Documents.Single(document => document.Path.EndsWith("de.rmf2", StringComparison.OrdinalIgnoreCase));
            EditorDocumentDraft draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", "Titel 🦊").ConfigureAwait(false);
            Require(draft.Success, "The RMF2 message edit did not compile.");
            Require((await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false)).Ok, "The RMF2 document was not saved.");
            var mutation = new EditorMutationRequest("create-key", null, null, null, null, null, "application.subtitle", "Untertitel");
            EditorMutationPreview preview = session.PreviewMutation(mutation);
            Require(preview.Ok, $"The RMF2 mutation could not be prepared: {preview.Message?.Detail ?? preview.Message?.Code ?? "unknown error"}");
            EditorOperationResult applied = await session.ApplyMutationAsync(mutation with { ConfirmationToken = preview.ConfirmationToken }).ConfigureAwait(false);
            Require(applied.Ok && applied.Snapshot?.Documents.Any(document =>
                string.Equals(document.Locale, initial.Catalog!.DefaultLocale, StringComparison.OrdinalIgnoreCase) &&
                document.Entries?.Any(entry => entry.Key == "application_subtitle" && entry.Content == "Untertitel") == true) == true,
                "The RMF2 create-key mutation did not produce the expected resource.");

            // Exercise each generated feature owner with a real compiler-backed
            // session. The hosted browser test covers routed document editing.
            // The ViewModel runs in a window scope of the SDK's window test host, as OpenWindowAsync opens one.
            var windowServices = new ServiceCollection();
            windowServices.AddRunicReactiveModelContext();
            windowServices.AddRunicWindowService();
            windowServices.AddRunicViews();
            await using var windowProvider = windowServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            await using var windowScope = windowProvider.CreateAsyncScope();
            var windowLocale = windowScope.ServiceProvider.GetRequiredService<IRunicWindowLocale>();
            using (var editor = new EditorViewModel(new EditorSession(project),
                windowScope.ServiceProvider.GetRequiredService<IRunicModelContext>(),
                windowScope.ServiceProvider.GetRequiredService<ISequencer>(), windowLocale))
            using (var window = RunicWindowTestHost.Create(windowScope.ServiceProvider, editor))
            {
                // The page's language switch sets the window's locale, which reaches the page as a locale push; a
                // malformed tag leaves it unchanged.
                const string pageConnection = "editor-page";
                HelloWithTranslatedText(window.Transport, pageConnection);
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("de"), CancellationToken.None).ConfigureAwait(false);
                Require(windowLocale.Locale == "de" && editor.UiLocale == "de" && window.Content.Locale == "de",
                    "The interface language did not set the window's locale.");
                Require(await TakeLocalePushAsync(window.Transport, pageConnection).ConfigureAwait(false) == "de",
                    "The window did not push the interface language to the page.");
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("not a tag"), CancellationToken.None).ConfigureAwait(false);
                Require(windowLocale.Locale == "de" && window.Content.Locale == "de",
                    "A malformed interface language changed the window's locale.");
                await Signal.ToTask(editor.SetUiLocaleCommand.Execute("en"), CancellationToken.None).ConfigureAwait(false);
                Require(await TakeLocalePushAsync(window.Transport, pageConnection).ConfigureAwait(false) == "en",
                    "A malformed interface language was pushed, or the next switch was not.");
                // A reloaded page connects again and is told the window's locale after its hello.
                HelloWithTranslatedText(window.Transport, pageConnection + "-reloaded");
                Require(await TakeLocalePushAsync(window.Transport, pageConnection + "-reloaded").ConfigureAwait(false) == "en",
                    "A reloaded page was not told the window's locale.");
                await ExecuteRouteAsync(editor.Workspace.LoadCommand, () => editor.Workspace.LoadResultJson, "{}");
                WorkspaceSnapshot loaded = editor.Workspace.LastLoad
                    ?? throw new InvalidOperationException("The workspace ViewModel did not publish a typed snapshot.");
                Require(loaded.Success && editor.Documents.Count > 0,
                    "The workspace ViewModel did not publish its typed snapshot and routed documents.");
                EditorDocument routedGerman = loaded.Documents.Single(document => document.Path == german.Path);
                await ExecuteRouteAsync(editor.DocumentTools.TransformDocumentCommand,
                    () => editor.DocumentTools.TransformDocumentResultJson,
                    JsonSerializer.Serialize(new { path = routedGerman.Path, content = routedGerman.Content,
                        key = "application_title", value = "ViewModel edit" }));
                Require(editor.DocumentTools.LastTransformDocument?.Success == true,
                    "The document tools ViewModel did not publish its typed draft.");
                var review = new EditorReviewSaveRequest(loaded.Review?.Revision,
                    loaded.Review?.Entries ?? [], loaded.Review?.Terminology ?? []);
                await ExecuteRouteAsync(editor.Review.SaveReviewCommand, () => editor.Review.SaveReviewResultJson,
                    JsonSerializer.Serialize(review, EditorJsonContext.Default.EditorReviewSaveRequest));
                Require(editor.Review.LastSaveReview?.Ok == true,
                    "The review ViewModel did not save through the compiler-backed session.");
                await ExecuteRouteAsync(editor.Interchange.ExportReviewJsonCommand,
                    () => editor.Interchange.ExportReviewJsonResultJson, "{}");
                Require(editor.Interchange.LastExportReviewJson?.Ok == true,
                    "The interchange ViewModel did not export review state.");
                await ExecuteRouteAsync(editor.Diagnostics.AboutCommand, () => editor.Diagnostics.AboutResultJson, "{}");
                Require(editor.Diagnostics.LastAbout?.Product is not null,
                    "The diagnostics ViewModel did not publish its typed product description.");
                await ExecuteRouteAsync(editor.LocalState.LoadLocalStateCommand,
                    () => editor.LocalState.LoadLocalStateResultJson, "{}");
                Require(editor.LocalState.LastLoadLocalState is not null,
                    "The local state ViewModel did not publish its typed snapshot.");
                var newProject = new EditorProjectCreationRequest(Path.Combine(container, "preview-project"),
                    "preview-project", "en", [], "Smoke.Translations", "SmokeText", false);
                await ExecuteRouteAsync(editor.Project.PreviewProjectCommand,
                    () => editor.Project.PreviewProjectResultJson,
                    JsonSerializer.Serialize(newProject, EditorJsonContext.Default.EditorProjectCreationRequest));
                Require(editor.Project.LastPreviewProject?.Ok == true,
                    "The project ViewModel did not publish its typed plan.");
            }

            string mountedRoot = Path.Combine(container, "mounted-direct");
            string mountedProject = Path.Combine(mountedRoot, "translations");
            string mountedFeature = Path.Combine(mountedRoot, "feature");
            Directory.CreateDirectory(mountedProject);
            Directory.CreateDirectory(Path.Combine(mountedFeature, "en"));
            await File.WriteAllTextAsync(Path.Combine(mountedProject, "runic.json"),
                "{\"schemaVersion\":1,\"catalog\":\"mounted-direct\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"validation\":{\"translationCompleteness\":\"allow\"},\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(mountedFeature, "en", "greeting.mf2"), "Hello\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(mountedFeature, "en", "farewell.mf2"), "Goodbye\n").ConfigureAwait(false);

            using var mountedSession = new EditorSession(mountedRoot);
            WorkspaceSnapshot mounted = await mountedSession.LoadAsync().ConfigureAwait(false);
            EditorDocument[] mountedGreeting = mounted.Documents.Where(document => document.Path.EndsWith("greeting.mf2", StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(mounted.Success && mountedGreeting.Length == 1 && mountedGreeting[0].Locale == "en" &&
                mountedGreeting[0].Entries?.Single().Key == "shop_greeting",
                "The editor did not preserve compiler identity for external mounted direct MF2 resources.");
            EditorDocumentDraft mountedGerman = await mountedSession.TransformDocumentAsync(
                "feature/de/greeting.mf2", "Hallo\n", "shop_greeting", "Hallo").ConfigureAwait(false);
            Require(mountedGerman.Success && mountedGerman.Entries.Single().Key == "shop_greeting",
                "The editor did not resolve a synthesized direct document against its external source root.");
            EditorOperationResult mountedSaved = await mountedSession.SaveAsync(
                "feature/de/greeting.mf2", mountedGerman.Content, EditorWorkspace.NewMf2DocumentRevision).ConfigureAwait(false);
            WorkspaceSnapshot mountedReloaded = await mountedSession.LoadAsync().ConfigureAwait(false);
            Require(mountedSaved.Ok && mountedReloaded.Documents.Single(document => document.Path == "feature/de/greeting.mf2").Locale == "de",
                "The editor did not save the synthesized locale beside its canonical mounted source.");

            EditorXliffExportResult xliffExport = await mountedSession.ExportXliffAsync("xliff").ConfigureAwait(false);
            Require(xliffExport.Ok && xliffExport.Documents.Count == 1,
                "The editor did not export the mounted direct MF2 project.");
            string xliffPath = Path.Combine(mountedRoot, xliffExport.Documents.Single().Path);
            XDocument xliff = XDocument.Load(xliffPath);
            XElement greetingUnit = xliff.Descendants().Single(element =>
                element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "shop_greeting");
            greetingUnit.Descendants().Single(element => element.Name.LocalName == "target").Value = "Guten Tag";
            XElement farewellUnit = xliff.Descendants().Single(element =>
                element.Name.LocalName == "unit" && element.Attribute("id")?.Value == "shop_farewell");
            XElement farewellSource = farewellUnit.Descendants().Single(element => element.Name.LocalName == "source");
            farewellSource.AddAfterSelf(new XElement(farewellSource.Name.Namespace + "target", "Auf Wiedersehen"));
            xliff.Save(xliffPath, SaveOptions.DisableFormatting);
            // An import planned while the manifest's source layout is unusable
            // (a whitespace mount path compiles with no sources) is refused as a
            // failed compilation, not planned against an empty project.
            string mountedManifest = Path.Combine(mountedProject, "runic.json");
            string mountedValid = await File.ReadAllTextAsync(mountedManifest).ConfigureAwait(false);
            await File.WriteAllTextAsync(mountedManifest, mountedValid.Replace("\"path\":\"../feature\"", "\"path\":\"   \"", StringComparison.Ordinal)).ConfigureAwait(false);
            EditorXliffImportPlan brokenImport = await mountedSession.PreviewXliffImportAsync(xliffExport.Documents.Single().Path).ConfigureAwait(false);
            Require(!brokenImport.Ok && brokenImport.ConfirmationToken is null && brokenImport.Refusals.Any(refusal => refusal.Code == "EDITOR-COMPILE"),
                "The editor planned an XLIFF import against a manifest with an unusable source root.");
            await File.WriteAllTextAsync(mountedManifest, mountedValid).ConfigureAwait(false);
            EditorXliffImportPlan xliffPreview = await mountedSession.PreviewXliffImportAsync(xliffExport.Documents.Single().Path).ConfigureAwait(false);
            Require(xliffPreview.Ok && xliffPreview.ChangedCount == 1 && xliffPreview.AddedCount == 1 && xliffPreview.ConfirmationToken is not null,
                "The editor did not plan the mounted direct MF2 XLIFF import.");
            EditorOperationResult xliffApplied = await mountedSession.ApplyXliffImportAsync(xliffPreview.ConfirmationToken!).ConfigureAwait(false);
            Require(xliffApplied.Ok && File.ReadAllText(Path.Combine(mountedFeature, "de", "greeting.mf2")).TrimEnd() == "Guten Tag" &&
                File.ReadAllText(Path.Combine(mountedFeature, "de", "farewell.mf2")).TrimEnd() == "Auf Wiedersehen" &&
                !File.Exists(Path.Combine(mountedProject, "de", "shop_greeting.mf2")) &&
                !File.Exists(Path.Combine(mountedProject, "de", "shop_farewell.mf2")),
                "The mounted direct MF2 XLIFF import wrote outside the canonical source root or changed the physical filename.");

            var rename = new EditorMutationRequest("rename-key", null, null, null, null, "shop_greeting", "shop.salutation", null);
            EditorMutationPreview renamePreview = mountedSession.PreviewMutation(rename);
            Require(renamePreview.Ok && renamePreview.Files.Any(file => file.Path == "feature/de/salutation.mf2"),
                "The editor did not plan mounted direct MF2 mutation beside the matched source root.");
            EditorOperationResult renamed = await mountedSession.ApplyMutationAsync(rename with { ConfirmationToken = renamePreview.ConfirmationToken }).ConfigureAwait(false);
            EditorDocument[] renamedSalutation = renamed.Snapshot?.Documents.Where(document => document.Path.EndsWith("salutation.mf2", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            Require(renamed.Ok && renamedSalutation.Length == 2 && renamedSalutation
                .All(document => document.Locale is "en" or "de" && document.Entries?.Single().Key == "shop_salutation"),
                "The editor did not apply compiler-consistent mounted direct MF2 mutation.");

            Console.WriteLine("PASS: editor feature ViewModels and grouped/mounted RMF2 v5 authoring journeys.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception}");
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(container)) Directory.Delete(container, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task ExecuteRouteAsync(ReactiveCommand<string, RxVoid> command,
        Func<string> response, string argumentJson)
    {
        string requestId = Guid.NewGuid().ToString("N");
        string request = "{\"requestId\":" + JsonSerializer.Serialize(requestId, EditorJsonContext.Default.String)
            + ",\"argument\":" + argumentJson + "}";
        await Signal.ToTask(command.Execute(request), CancellationToken.None).ConfigureAwait(false);
        using var result = JsonDocument.Parse(response());
        Require(result.RootElement.GetProperty("requestId").GetString() == requestId,
            "The routed feature result was not correlated with its command.");
        Require(result.RootElement.TryGetProperty("result", out _),
            "The routed feature did not publish a result.");
    }

    // A page connection that, like @runic-artifex/views 0.16, follows the window's locale (translation-text/1).
    private static void HelloWithTranslatedText(InMemoryBridgeTransport transport, string connection)
    {
        string reply = transport.Request(
            $$"""{"kind":"hello","protocol":4,"session":"session-{{connection}}","client":{"name":"editor-smoke","version":"1.0.0"},"features":["translation-text/1"]}""",
            connection, $"client-{connection}");
        Require(reply.StartsWith("{\"ok\":true", StringComparison.Ordinal), $"The window refused the page's hello: {reply}");
    }

    // The locale of the next locale push to the connection; other pushes are dropped.
    private static async Task<string?> TakeLocalePushAsync(InMemoryBridgeTransport transport, string connection)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            foreach (RunicBridgeMessage message in transport.TakeMessages())
            {
                if (message.ConnectionKey != connection || message.Kind != "locale") continue;
                using var push = JsonDocument.Parse(message.Json);
                return push.RootElement.GetProperty("locale").GetString();
            }
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new InvalidOperationException("The window did not push a locale to the page.");
    }

    // The Linux picker tries the portal first and falls back to zenity only when the portal showed no dialog. Checked
    // without opening a dialog.
    private static void WorkspacePickerSelection()
    {
        Require(EditorWorkspacePicker.PortalOutcome(
                new PickerResult<IDirectoryLease>.Unavailable(PlatformUnavailableReason.BackendUnavailable)) is null,
            "A missing FileChooser portal did not fall back to the system dialog.");
        Require(EditorWorkspacePicker.PortalOutcome(new PickerResult<IDirectoryLease>.Dismissed()) is { Ok: false, Cancelled: true, Message: null },
            "A dismissed portal dialog was not reported as cancelled.");
        EditorWorkspacePickerResult? failed = EditorWorkspacePicker.PortalOutcome(new PickerResult<IDirectoryLease>.Failed(
            PlatformFailureCode.PermissionDenied, PlatformDiagnostic.FromErrno(13)));
        Require(failed is { Ok: false, Cancelled: false, Message: { Code: "ui_backend_picker_failed" } notice } &&
            notice.Args.Single() is { Name: "reason", Value: "PermissionDenied" } && notice.Detail?.StartsWith("errno 13", StringComparison.Ordinal) == true,
            $"A folder the picker could not open was not reported with its reason: {failed?.Message}");
        Require(EditorWorkspacePicker.CreateZenityStartInfo(static _ => false) is null,
            "The fallback offered zenity although it is not installed.");
        var zenity = EditorWorkspacePicker.CreateZenityStartInfo(static path => path == "/usr/bin/zenity");
        Require(zenity is { FileName: "/usr/bin/zenity" } && zenity.ArgumentList.SequenceEqual(["--file-selection", "--directory"]),
            "The fallback does not open zenity's folder dialog.");
        if (OperatingSystem.IsLinux())
            Require(EditorWorkspacePicker.CreateStartInfo(static path => path == "/usr/bin/zenity")?.FileName == "/usr/bin/zenity" &&
                EditorWorkspacePicker.CreateStartInfo(static _ => false) is null,
                "The Linux system dialog is not the zenity fallback.");
    }
}
