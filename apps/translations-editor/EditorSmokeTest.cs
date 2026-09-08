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
            const string originalGerman = "# German messages\r\n[application]\r\ntitle = 'Titel' # retain this comment\r\n[validation.form]\r\npending = 'Pflicht'\r\n";
            await File.WriteAllTextAsync(germanPath, originalGerman).ConfigureAwait(false);
            string englishPath = Path.Combine(project, "en.toml");
            await File.WriteAllTextAsync(englishPath, "[application]\r\ntitle = 'Title'\r\n[validation.form]\r\npending = 'Required'\r\n").ConfigureAwait(false);
            using var workspace = new EditorWorkspace(project);
            await File.WriteAllTextAsync(germanPath, "broken = [\n").ConfigureAwait(false);
            WorkspaceSnapshot malformed = await workspace.LoadAsync().ConfigureAwait(false);
            Require(!malformed.Success && malformed.Documents.Single(document => document.Path == "DE.TOML").IsMalformed,
                "Malformed locale TOML was not exposed to the repair editor.");
            await File.WriteAllTextAsync(germanPath, originalGerman).ConfigureAwait(false);
            WorkspaceSnapshot initial = await workspace.LoadAsync().ConfigureAwait(false);
            Require(initial.Success && initial.Catalog?.Locales.Count == 2, "The TOML project was not loaded.");
            EditorDocument german = initial.Documents.Single(document => document.Path == "DE.TOML");
            Require(german.Entries is { } initialEntries &&
                initialEntries.Single(entry => entry.Key == "application_title").Content == "Titel" &&
                initialEntries.Any(entry => entry.Key == "validation_form_pending"), "Logical message identity was lost.");

            EditorDocumentDraft draft = await workspace.TransformDocumentAsync(german.Path, german.Content,
                "application_title", "Titel 🦊").ConfigureAwait(false);
            Require(draft.Success && draft.Content.StartsWith("# German messages\r\n[application]\r\n", StringComparison.Ordinal) &&
                draft.Content.EndsWith(" # retain this comment\r\n[validation.form]\r\npending = 'Pflicht'\r\n", StringComparison.Ordinal),
                "The message transform did not preserve physical trivia.");
            EditorMessagePreview preview = await workspace.PreviewMessageAsync(
                german.Path, draft.Content, "de", "application_title").ConfigureAwait(false);
            Require(preview.Success && preview.AstJson is not null, "The TOML message preview was not produced.");
            EditorOperationResult saved = await workspace.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false);
            Require(saved.Ok, saved.Message?.ToString() ?? "The locale document was not saved.");
            EditorOperationResult stale = await workspace.SaveAsync(german.Path, german.Content, german.Revision).ConfigureAwait(false);
            Require(!stale.Ok && stale.Kind == "conflict", "A stale physical revision overwrote another edit.");

            const string inlineGerman = "[inline]\r\nlabels = { nested = { primary = 'Inline', neighbor = 'Unverändert' } } # inline trivia\r\n";
            await File.AppendAllTextAsync(germanPath, inlineGerman).ConfigureAwait(false);
            await File.AppendAllTextAsync(englishPath,
                "[inline]\r\nlabels = { nested = { primary = 'Inline', neighbor = 'Unchanged' } }\r\n").ConfigureAwait(false);
            WorkspaceSnapshot inlineSnapshot = await workspace.LoadAsync().ConfigureAwait(false);
            german = inlineSnapshot.Documents.Single(document => document.Path == "DE.TOML");
            Require(inlineSnapshot.Success &&
                german.Entries?.Single(entry => entry.Key == "inline_labels_nested_primary").Content == "Inline",
                "The editor did not expose a nested inline-table leaf with its flattened identity.");
            EditorDocumentDraft inlineDraft = await workspace.TransformDocumentAsync(german.Path, german.Content,
                "inline_labels_nested_primary", "Inline geändert 🦊").ConfigureAwait(false);
            Require(inlineDraft.Success && inlineDraft.Content.StartsWith(draft.Content, StringComparison.Ordinal) &&
                inlineDraft.Content.Contains("labels = { nested = { primary = ", StringComparison.Ordinal) &&
                inlineDraft.Content.EndsWith(", neighbor = 'Unverändert' } } # inline trivia\r\n", StringComparison.Ordinal),
                "Editing an inline leaf changed its neighbors, enclosing tables, or physical trivia.");
            Require((await workspace.SaveAsync(german.Path, inlineDraft.Content, german.Revision).ConfigureAwait(false)).Ok,
                "The transformed inline-table document could not be saved.");
            WorkspaceSnapshot inlineSaved = await workspace.LoadAsync().ConfigureAwait(false);
            Require(inlineSaved.Success && inlineSaved.Documents.Single(document => document.Path == "DE.TOML")
                .Entries?.Single(entry => entry.Key == "inline_labels_nested_primary").Content == "Inline geändert 🦊",
                "The saved inline leaf lost its logical identity or value on reload.");

            using (var mutations = new EditorSession(project))
            {
                await mutations.LoadAsync().ConfigureAwait(false);
                var create = new EditorMutationRequest("create-key", null, null, null, null, null,
                    "validation_form_added", "Added");
                EditorMutationPreview createPreview = mutations.PreviewMutation(create);
                Require(createPreview.Ok && createPreview.Files.Count == 2,
                    "Creating a grouped key did not preview both locale documents.");
                Require((await mutations.ApplyMutationAsync(create with { ConfirmationToken = createPreview.ConfirmationToken })
                    .ConfigureAwait(false)).Ok, "Creating a grouped key through the editor failed.");
                var rename = new EditorMutationRequest("rename-key", null, null, null, null,
                    "validation_form_pending", "validation_form_required", null);
                EditorMutationPreview renamePreview = mutations.PreviewMutation(rename);
                Require(renamePreview.Ok, "Renaming a grouped key could not be previewed.");
                Require((await mutations.ApplyMutationAsync(rename with { ConfirmationToken = renamePreview.ConfirmationToken })
                    .ConfigureAwait(false)).Ok, "Renaming a grouped key through the editor failed.");
            }
            WorkspaceSnapshot mutated = await workspace.LoadAsync().ConfigureAwait(false);
            EditorDocument grouped = mutated.Documents.Single(document => document.Path == "DE.TOML");
            Require(mutated.Success && grouped.Entries is { Count: 5 } groupedEntries &&
                groupedEntries.Any(entry => entry.Key == "validation_form_required") &&
                groupedEntries.Any(entry => entry.Key == "validation_form_added") &&
                !groupedEntries.Any(entry => entry.Key == "validation_form_pending"),
                "Grouped create and rename did not preserve flattened logical identity.");
            Require(grouped.Content.Contains("[validation.form]", StringComparison.Ordinal) &&
                !grouped.Content.Contains("validation_form_required =", StringComparison.Ordinal),
                "The editor mutation did not retain readable nested TOML grouping.");
            Require(!Directory.EnumerateFiles(project, "*.mf2", SearchOption.AllDirectories).Any(), "TOML mutation created legacy files.");

            const string savedRow = "[[notifications]]\r\n_id = 'saved'\r\ntitle = 'Gespeichert' # stable row comment\r\n";
            const string dismissedRow = "[[notifications]]\r\n_id = 'dismissed'\r\ntitle = 'Verworfen'\r\n";
            await File.AppendAllTextAsync(germanPath, savedRow + dismissedRow).ConfigureAwait(false);
            await File.AppendAllTextAsync(englishPath,
                "[[notifications]]\r\n_id = 'saved'\r\ntitle = 'Saved'\r\n[[notifications]]\r\n_id = 'dismissed'\r\ntitle = 'Dismissed'\r\n").ConfigureAwait(false);
            WorkspaceSnapshot arraySnapshot = await workspace.LoadAsync().ConfigureAwait(false);
            EditorDocument arrayDocument = arraySnapshot.Documents.Single(document => document.Path == "DE.TOML");
            Require(arraySnapshot.Success && arrayDocument.Entries is { } arrayEntries &&
                arrayEntries.Single(entry => entry.Key == "notifications_saved_title").Content == "Gespeichert" &&
                !arrayEntries.Any(entry => entry.Key.EndsWith("__id", StringComparison.Ordinal)),
                "Array rows did not expose stable message IDs without metadata.");
            // Reorder actual rows on disk, then edit the same logical ID through
            // the editor: row position must never choose the target message.
            await File.WriteAllTextAsync(germanPath, grouped.Content + dismissedRow + savedRow).ConfigureAwait(false);
            WorkspaceSnapshot reordered = await workspace.LoadAsync().ConfigureAwait(false);
            german = reordered.Documents.Single(document => document.Path == "DE.TOML");
            EditorDocumentDraft arrayDraft = await workspace.TransformDocumentAsync(german.Path, german.Content,
                "notifications_saved_title", "Gespeichert nach Neuordnung").ConfigureAwait(false);
            Require(reordered.Success && arrayDraft.Success &&
                arrayDraft.Content.StartsWith(grouped.Content + dismissedRow, StringComparison.Ordinal) &&
                arrayDraft.Content.Contains("_id = 'saved'\r\n", StringComparison.Ordinal) &&
                arrayDraft.Content.EndsWith(" # stable row comment\r\n", StringComparison.Ordinal),
                "Editing a reordered array row changed its identity, neighbor, or comment.");
            Require((await workspace.SaveAsync(german.Path, arrayDraft.Content, german.Revision).ConfigureAwait(false)).Ok,
                "The reordered array leaf could not be saved.");
            WorkspaceSnapshot arraySaved = await workspace.LoadAsync().ConfigureAwait(false);
            Require(arraySaved.Success && arraySaved.Documents.Single(document => document.Path == "DE.TOML")
                .Entries?.Single(entry => entry.Key == "notifications_saved_title").Content == "Gespeichert nach Neuordnung",
                "The saved array row lost its stable logical identity.");

            // A missing logical message must be inserted into its existing file.
            await File.AppendAllTextAsync(englishPath, "[only]\r\nsource = 'Source'\r\n").ConfigureAwait(false);
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
                if (key is "application_title" or "validation_form_required")
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
            Require(complete.Success && german.Entries?.Single(entry => entry.Key == "validation_form_required").Content == "Erforderlich",
                "The grouped imported messages did not compile.");
            draft = await session.TransformDocumentAsync(german.Path, german.Content, "application_title", "Rückgängig").ConfigureAwait(false);
            Require((await session.SaveAsync(german.Path, draft.Content, german.Revision).ConfigureAwait(false)).Ok, "Session save failed.");
            Require((await session.UndoAsync().ConfigureAwait(false)).Ok && await File.ReadAllTextAsync(germanPath).ConfigureAwait(false) == german.Content,
                "Undo did not restore the complete physical file.");
            Require((await session.RedoAsync().ConfigureAwait(false)).Ok && await File.ReadAllTextAsync(germanPath).ConfigureAwait(false) == draft.Content,
                "Redo did not restore the complete physical file.");

            Console.WriteLine("PASS: editor nested TOML messages, grouped create/rename, trivia, revisions, grouped XLIFF conflicts, and physical undo/redo.");
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
