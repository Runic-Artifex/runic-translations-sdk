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
            Console.WriteLine("PASS: editor loads, edits, saves, and mutates RMF2 sources.");
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
