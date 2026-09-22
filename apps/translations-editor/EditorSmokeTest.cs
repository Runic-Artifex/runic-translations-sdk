using Runic.Translations.Authoring;

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

            string mountedRoot = Path.Combine(container, "mounted-direct");
            string mountedProject = Path.Combine(mountedRoot, "translations");
            string mountedFeature = Path.Combine(mountedRoot, "feature");
            Directory.CreateDirectory(mountedProject);
            Directory.CreateDirectory(Path.Combine(mountedFeature, "en"));
            await File.WriteAllTextAsync(Path.Combine(mountedProject, "runic.json"),
                "{\"schemaVersion\":1,\"catalog\":\"mounted-direct\",\"code\":{\"namespace\":\"Smoke.Translations\",\"className\":\"SmokeText\"},\"baseLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"validation\":{\"translationCompleteness\":\"allow\"},\"sourceRoots\":[{\"path\":\"../feature\",\"namespace\":[\"shop\"]}]}\n").ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(mountedFeature, "en", "greeting.mf2"), "Hello\n").ConfigureAwait(false);

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
            var rename = new EditorMutationRequest("rename-key", null, null, null, null, "shop_greeting", "shop.salutation", null);
            EditorMutationPreview renamePreview = mountedSession.PreviewMutation(rename);
            Require(renamePreview.Ok && renamePreview.Files.Any(file => file.Path == "feature/de/salutation.mf2"),
                "The editor did not plan mounted direct MF2 mutation beside the matched source root.");
            EditorOperationResult renamed = await mountedSession.ApplyMutationAsync(rename with { ConfirmationToken = renamePreview.ConfirmationToken }).ConfigureAwait(false);
            EditorDocument[] renamedSalutation = renamed.Snapshot?.Documents.Where(document => document.Path.EndsWith("salutation.mf2", StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            Require(renamed.Ok && renamedSalutation.Length == 2 && renamedSalutation
                .All(document => document.Locale is "en" or "de" && document.Entries?.Single().Key == "shop_salutation"),
                "The editor did not apply compiler-consistent mounted direct MF2 mutation.");

            Console.WriteLine("PASS: editor loads, edits, saves, and mutates grouped and mounted direct RMF2 v5 sources.");
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
}
