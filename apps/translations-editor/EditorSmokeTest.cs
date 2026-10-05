using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Text.Json;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;
using System.Xml.Linq;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    private static async Task AuthoringJourneyAsync(string project)
    {
        TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
            new TranslationProjectCreationRequest(project, "authoring-smoke", "en", "Smoke.Translations", "SmokeText",
                [new TranslationProjectLocale("de", "en")])));
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

    public static async Task<int> RunAsync(string workspacePath)
    {
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        try
        {
            await AuthoringJourneyAsync(Path.Combine(container, "authoring")).ConfigureAwait(false);
            await MountedContextJourneyAsync(Path.Combine(container, "mounted-context")).ConfigureAwait(false);
            string project = Path.Combine(container, "translations");
            // Smoke tests exercise an intentionally minimal workspace. They must
            // never mutate the packaged example or a caller-provided directory.
            TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(project, "editor-smoke", "en", "Smoke.Translations", "SmokeText",
                    [new TranslationProjectLocale("de", "en")])));
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
            await using var editorContext = new RunicModelContext();
            var scheduler = new RunicReactiveSchedulerProvider().For(editorContext);
            using (var editor = new EditorViewModel(new EditorSession(project), editorContext, scheduler))
            {
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
}
