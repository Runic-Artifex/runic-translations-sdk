using Runic.Translations.Authoring;
using System.Xml.Linq;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    public static async Task<int> RunAsync(string workspacePath)
    {
        _ = workspacePath;
        string container = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        string project = Path.Combine(container, "translations");
        try
        {
            Directory.CreateDirectory(container);
            TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(
                    project,
                    "editor-smoke",
                    "en",
                    "Smoke.Translations",
                    "SmokeText",
                    [new TranslationProjectLocale("de", "en")])));

            // Use actual TOML source with comments, CRLF and Unicode so preserving
            // physical bytes is tested independently of the compiled MF2 pattern.
            string germanPath = Path.Combine(project, "DE.TOML");
            File.Move(Path.Combine(project, "de.toml"), germanPath);
            const string originalGerman = "# German messages\r\napplication_title = 'Titel' # retain this comment\r\n";
            await File.WriteAllTextAsync(germanPath, originalGerman).ConfigureAwait(false);
            using var workspace = new EditorWorkspace(project);
            await File.WriteAllTextAsync(germanPath, "broken = [\n").ConfigureAwait(false);
            WorkspaceSnapshot malformed = await workspace.LoadAsync().ConfigureAwait(false);
            Require(!malformed.Success && malformed.Documents.Single(document => document.Path == "DE.TOML").IsMalformed,
                "Malformed locale TOML was not exposed to the repair editor.");
            await File.WriteAllTextAsync(germanPath, originalGerman).ConfigureAwait(false);
            WorkspaceSnapshot initial = await workspace.LoadAsync().ConfigureAwait(false);
            Require(initial.Success && initial.Catalog?.Locales.Count == 2, "The TOML project was not loaded.");
            EditorDocument german = initial.Documents.Single(document => document.Path == "DE.TOML");
            Require(german.Entries?.Single().Key == "application_title", "Logical message identity was lost.");

            EditorDocumentDraft draft = await workspace.TransformDocumentAsync(german.Path, german.Content,
                "application_title", "Titel 🦊").ConfigureAwait(false);
            Require(draft.Success && draft.Content.StartsWith("# German messages\r\n", StringComparison.Ordinal) &&
                draft.Content.EndsWith(" # retain this comment\r\n", StringComparison.Ordinal),
                "The message transform did not preserve physical trivia.");
            EditorMessagePreview preview = await workspace.PreviewMessageAsync(
                german.Path, draft.Content, "de", "application_title").ConfigureAwait(false);
            Require(preview.Success && preview.AstJson is not null, "The TOML message preview was not produced.");
            EditorOperationResult saved = await workspace.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false);
            Require(saved.Ok, saved.Message?.ToString() ?? "The locale document was not saved.");
            EditorOperationResult stale = await workspace.SaveAsync(german.Path, german.Content, german.Revision).ConfigureAwait(false);
            Require(!stale.Ok && stale.Kind == "conflict", "A stale physical revision overwrote another edit.");

            TranslationWorkspaceTransaction.Commit(TranslationWorkspaceMutation.CreateKey(
                new TranslationCreateKeyRequest(project, "editor-smoke", "validation_required", "Required")));
            WorkspaceSnapshot mutated = await workspace.LoadAsync().ConfigureAwait(false);
            Require(mutated.Success && mutated.Documents.Single(document => document.Path == "DE.TOML").Entries?.Count == 2,
                "Creating a key did not share the physical locale document.");
            Require(!Directory.EnumerateFiles(project, "*.mf2", SearchOption.AllDirectories).Any(), "TOML mutation created legacy files.");

            // A missing logical message must be inserted into its existing file.
            string englishPath = Path.Combine(project, "en.toml");
            await File.AppendAllTextAsync(englishPath, "only_source = 'Source'\n").ConfigureAwait(false);
            WorkspaceSnapshot incomplete = await workspace.LoadAsync().ConfigureAwait(false);
            german = incomplete.Documents.Single(document => document.Path == "DE.TOML");
            EditorDocumentDraft missing = await workspace.TransformDocumentAsync(german.Path, german.Content,
                "only_source", "Ziel").ConfigureAwait(false);
            Require(missing.Success, "A missing logical locale entry could not be added.");
            Require((await workspace.SaveAsync(german.Path, missing.Content, german.Revision).ConfigureAwait(false)).Ok,
                "The missing logical message could not be saved.");

            EditorXliffExportResult exported = await workspace.ExportXliffAsync("interchange").ConfigureAwait(false);
            Require(exported.Ok && exported.Documents.Count == 1, exported.Message?.ToString() ?? "XLIFF export failed.");
            string xliffPath = Path.Combine(project, exported.Documents[0].Path);
            XDocument xliff = XDocument.Load(xliffPath);
            XNamespace xliffNamespace = "urn:oasis:names:tc:xliff:document:2.0";
            foreach (XElement unit in xliff.Descendants(xliffNamespace + "unit"))
            {
                string? key = unit.Attribute("id")?.Value;
                if (key is "application_title" or "validation_required")
                    unit.Descendants(xliffNamespace + "target").Single().Value = key == "application_title" ? "Importierter Titel" : "Erforderlich";
            }
            xliff.Save(xliffPath);
            (EditorXliffImportPlan importPlan, PreparedInterchangeImport? prepared) =
                await workspace.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
            Require(importPlan.Ok && prepared?.Documents.Count == 1 && prepared.Documents[0].Path == "DE.TOML",
                importPlan.Message?.ToString() ?? "XLIFF edits were not grouped into one physical write.");
            await File.AppendAllTextAsync(germanPath, "# external edit\r\n").ConfigureAwait(false);
            EditorOperationResult conflict = await workspace.CommitXliffImportAsync(prepared!).ConfigureAwait(false);
            Require(!conflict.Ok && conflict.Kind == "conflict", "XLIFF import ignored a comment-only external edit.");
            (importPlan, prepared) = await workspace.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
            Require(importPlan.Ok && prepared is not null, "XLIFF could not be re-previewed after conflict.");
            byte[] beforeFailedImport = await File.ReadAllBytesAsync(germanPath).ConfigureAwait(false);
            string? reviewBeforeFailure = TranslationEditorStateStore.Load(project, "editor-smoke").Revision;
            // The physical write happens before sidecar Save; an invalid staged
            // review entry deterministically exercises the rollback path without
            // filesystem timing, permission tricks, or a production test hook.
            PreparedInterchangeImport invalidSidecar = prepared! with
            {
                MergedEntries = [new TranslationEditorStateEntry("application_title", "de", "unsupported-state", null, null,
                    new Dictionary<string, string>())],
            };
            EditorOperationResult rolledBack = await workspace.CommitXliffImportAsync(invalidSidecar).ConfigureAwait(false);
            Require(!rolledBack.Ok && rolledBack.Kind == "io", "A sidecar failure did not report a rolled-back import.");
            byte[] afterFailedImport = await File.ReadAllBytesAsync(germanPath).ConfigureAwait(false);
            Require(beforeFailedImport.SequenceEqual(afterFailedImport),
                "The grouped locale document was not restored byte-for-byte after sidecar failure.");
            Require(TranslationEditorStateStore.Load(project, "editor-smoke").Revision == reviewBeforeFailure,
                "The failed import changed review state.");
            Require((await workspace.CommitXliffImportAsync(prepared!).ConfigureAwait(false)).Ok, "The grouped import failed.");
            string importedBytes = await File.ReadAllTextAsync(germanPath).ConfigureAwait(false);
            Require(importedBytes.Contains("# retain this comment", StringComparison.Ordinal) &&
                importedBytes.EndsWith("# external edit\r\n", StringComparison.Ordinal), "XLIFF discarded locale-file comments.");

            using var session = new EditorSession(project);
            WorkspaceSnapshot complete = await session.LoadAsync().ConfigureAwait(false);
            german = complete.Documents.Single(document => document.Path == "DE.TOML");
            Require(complete.Success && german.Entries?.Single(entry => entry.Key == "validation_required").Content == "Erforderlich",
                "The grouped imported messages did not compile.");
            draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", "Rückgängig").ConfigureAwait(false);
            Require((await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false)).Ok, "Session save failed.");
            Require((await session.UndoAsync().ConfigureAwait(false)).Ok && await File.ReadAllTextAsync(germanPath).ConfigureAwait(false) == german.Content,
                "Undo did not restore the complete physical file.");
            Require((await session.RedoAsync().ConfigureAwait(false)).Ok && await File.ReadAllTextAsync(germanPath).ConfigureAwait(false) == draft.Content,
                "Redo did not restore the complete physical file.");

            Console.WriteLine("PASS: editor TOML messages, trivia, revisions, grouped XLIFF conflicts, and physical undo/redo.");
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
