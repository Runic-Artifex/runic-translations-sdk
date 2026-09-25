using Runic.Translations.Authoring;
using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Text.Json;
using System.Xml.Linq;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    public static async Task<int> RunAsync(string workspacePath)
    {
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        try
        {
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
            using (var editor = new EditorViewModel(new EditorSession(project)))
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
            Console.Error.WriteLine($"FAIL: {exception.Message}");
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
