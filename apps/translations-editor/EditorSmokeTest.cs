using Runic.Translations.Authoring;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    public static async Task<int> RunAsync(string workspacePath)
    {
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        try
        {
            string project = string.IsNullOrWhiteSpace(workspacePath) ? Path.Combine(container, "translations") : workspacePath;
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                    new TranslationProjectCreationRequest(project, "editor-smoke", "en", "Smoke.Translations", "SmokeText",
                        [new TranslationProjectLocale("de", "en")])));
                await File.WriteAllTextAsync(Path.Combine(project, "en.rmf2"), "application {\n  title = Title\n}\n").ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(project, "de.rmf2"), "application {\n  title = Titel\n}\n").ConfigureAwait(false);
            }

            using var session = new EditorSession(project);
            WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
            Require(initial.Success, "The RMF2 project did not load.");
            EditorDocument german = initial.Documents.Single(document => document.Path.EndsWith("de.rmf2", StringComparison.OrdinalIgnoreCase));
            EditorDocumentDraft draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", "Titel 🦊").ConfigureAwait(false);
            Require(draft.Success, "The RMF2 message edit did not compile.");
            Require((await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false)).Ok, "The RMF2 document was not saved.");
            EditorMutationPreview preview = session.PreviewMutation(new EditorMutationRequest("create-key", null, null, null, null, null, "application.subtitle", "Untertitel"));
            Require(preview.Ok && (await session.ApplyMutationAsync(new EditorMutationRequest("create-key", null, null, null, null, null, "application.subtitle", "Untertitel", preview.ConfirmationToken)).ConfigureAwait(false)).Ok,
                "The RMF2 mutation was not applied.");
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
            if (string.IsNullOrWhiteSpace(workspacePath))
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
