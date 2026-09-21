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

            await RunRmf2Async(Path.Combine(container, "rmf2")).ConfigureAwait(false);
            await RunMountedWatchReconciliationAsync(Path.Combine(container, "mounted-watch")).ConfigureAwait(false);
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

    private static async Task RunRmf2Async(string root)
    {
        Directory.CreateDirectory(Path.Combine(root, "shop"));
        Directory.CreateDirectory(Path.Combine(root, "accounts"));
        Directory.CreateDirectory(Path.Combine(root, "payments"));
        await File.WriteAllTextAsync(Path.Combine(root, "runic.json"), """
        {"schemaVersion":1,"catalog":"rmf2-smoke","sourceLayout":"rmf2-v1","baseLocale":"en","code":{"namespace":"Smoke","className":"Text"},"validation":{"translationCompleteness":"allow"},"sourceRoots":[{"path":"shop","namespace":["shop"]},{"path":"accounts","namespace":["account"]},{"path":"payments","namespace":["payment"]}]}
        """);
        await File.WriteAllTextAsync(Path.Combine(root, "shop", "en.rmf2"), "title = Shop\n");
        await File.WriteAllTextAsync(Path.Combine(root, "accounts", "en.rmf2"), "# Profile heading\ntitle = Account\n");
        await File.WriteAllTextAsync(Path.Combine(root, "payments", "en.rmf2"), "title = Payment\n");
        using var session = new EditorSession(root);
        Require((await session.LoadAsync()).Success, "Mounted RMF2 editor catalog failed to load.");
        await Apply(new("create-key", null, null, null, null, null, "account.profile", "Profile"));
        Require((await File.ReadAllTextAsync(Path.Combine(root, "accounts", "en.rmf2"))).Contains("profile = Profile", StringComparison.Ordinal), "RMF2 creation chose the wrong mount.");
        await Apply(new("rename-key", null, null, null, null, "account_title", "account.heading", null));
        Require((await File.ReadAllTextAsync(Path.Combine(root, "accounts", "en.rmf2"))).Contains("# Profile heading", StringComparison.Ordinal), "RMF2 rename discarded metadata.");
        await Apply(new("add-locale", "de", "en", null, "en", null, null, null));
        Require(File.Exists(Path.Combine(root, "accounts", "de.rmf2")) && File.Exists(Path.Combine(root, "shop", "de.rmf2")), "RMF2 locale creation missed a mount.");
        await Apply(new("set-fallback", "de", "en", null, null, null, null, null));
        await Apply(new("duplicate-key", null, null, null, null, "account_heading", "account.copy", null));
        await Apply(new("delete-key", null, null, null, null, "account_copy", null, null));

        string interchangeDirectory = Path.Combine(root, "interchange");
        EditorXliffExportResult exported = await session.ExportXliffAsync(interchangeDirectory).ConfigureAwait(false);
        Require(exported.Ok && exported.Documents.Count == 1, exported.Message?.ToString() ?? "RMF2 XLIFF export failed.");
        string interchangePath = Path.Combine(root, exported.Documents[0].Path);
        XDocument xliff = XDocument.Load(interchangePath);
        XNamespace xliffNamespace = "urn:oasis:names:tc:xliff:document:2.0";
        foreach (XElement unit in xliff.Descendants(xliffNamespace + "unit"))
        {
            string? key = unit.Attribute("id")?.Value;
            if (key is "shop_title" or "account_heading" or "payment_title")
            {
                XElement target = unit.Descendants(xliffNamespace + "target").Single();
                target.Value = key switch
                {
                    "shop_title" => "Importierter Shop",
                    "account_heading" => "Importiertes Konto",
                    _ => "Importierte Zahlung",
                };
            }
        }
        xliff.Save(interchangePath);
        File.Delete(Path.Combine(root, "payments", "de.rmf2"));
        EditorXliffImportPlan importPlan = await session.PreviewXliffImportAsync(interchangePath).ConfigureAwait(false);
        Require(importPlan.Ok, importPlan.Message?.ToString() ?? "RMF2 XLIFF import preview failed.");
        Require((await session.ApplyXliffImportAsync(importPlan.ConfirmationToken!).ConfigureAwait(false)).Ok,
            "RMF2 XLIFF import failed.");
        Require(File.ReadAllText(Path.Combine(root, "shop", "de.rmf2")).Contains("title = Importierter Shop", StringComparison.Ordinal),
            "RMF2 XLIFF import did not update the target resource.");
        Require(File.ReadAllText(Path.Combine(root, "accounts", "de.rmf2")).Contains("heading = Importiertes Konto", StringComparison.Ordinal),
            "RMF2 XLIFF import did not update the mounted target resource.");
        Require(File.Exists(Path.Combine(root, "payments", "de.rmf2")) &&
            File.ReadAllText(Path.Combine(root, "payments", "de.rmf2")).Contains("title = Importierte Zahlung", StringComparison.Ordinal),
            "RMF2 XLIFF import did not recreate the missing mounted target resource.");
        Require(!Directory.EnumerateFiles(root, "*.mf2", SearchOption.AllDirectories).Any(),
            "RMF2 XLIFF import created a legacy MF2 file.");
        await Apply(new("remove-locale", "de", null, "en", null, null, null, null));
        Require(!File.Exists(Path.Combine(root, "accounts", "de.rmf2")), "RMF2 locale removal left a source behind.");
        Console.WriteLine("PASS: editor mounted RMF2 create, rename, duplicate, delete, locale and fallback transactions.");
        async Task Apply(EditorMutationRequest request)
        {
            var preview = session.PreviewMutation(request);
            Require(preview.Ok, "RMF2 mutation preview failed: " + request.Kind + " " + preview.Message);
            var result = await session.ApplyMutationAsync(request with { ConfirmationToken = preview.ConfirmationToken });
            Require(result.Ok, "RMF2 mutation failed: " + request.Kind + " " + result.Message);
        }
    }

    private static async Task RunMountedWatchReconciliationAsync(string root)
    {
        string project = Path.Combine(root, "translations");
        string feature = Path.Combine(root, "feature");
        Directory.CreateDirectory(project);
        Directory.CreateDirectory(feature);
        await File.WriteAllTextAsync(Path.Combine(project, "runic.json"), """
            {"schemaVersion":1,"catalog":"mounted-watch","sourceLayout":"rmf2-v1","baseLocale":"en","code":{"namespace":"Smoke","className":"Text"},"sourceRoots":[{"path":"../feature","namespace":["shop"]}]}
            """).ConfigureAwait(false);
        string english = Path.Combine(feature, "en.rmf2");
        await File.WriteAllTextAsync(english, "title = Shop\n").ConfigureAwait(false);

        // The documented host shape is a common containing workspace: the
        // project config is nested while its explicitly mounted root is a
        // sibling. This keeps every watched path inside the workspace boundary.
        using var workspace = new EditorWorkspace(root);
        WorkspaceSnapshot loaded = await workspace.LoadAsync().ConfigureAwait(false);
        Require(loaded.Success, "Mounted editor watch fixture did not load: " +
            string.Join(" | ", loaded.Diagnostics.Select(static diagnostic => diagnostic.Message)));
        await File.WriteAllTextAsync(english, "title = Store\n").ConfigureAwait(false);
        EditorExternalChanges changed = await WaitForExternalChangesAsync(workspace, "change",
            change => change.Changes.Any(item => item.Path == "feature/en.rmf2" && item.Exists)).ConfigureAwait(false);
        Require(changed.Changes.Any(item => item.Path == "feature/en.rmf2" && item.Exists), "Mounted source change was not reconciled.");

        string german = Path.Combine(feature, "de.rmf2");
        await File.WriteAllTextAsync(german, "title = Laden\n").ConfigureAwait(false);
        EditorExternalChanges added = await WaitForExternalChangesAsync(workspace, "add",
            change => change.Changes.Any(item => item.Path == "feature/de.rmf2" && item.Exists)).ConfigureAwait(false);
        Require(added.Changes.Any(item => item.Path == "feature/de.rmf2" && item.Exists), "Mounted source addition was not reconciled.");

        string french = Path.Combine(feature, "fr.rmf2");
        File.Move(german, french);
        EditorExternalChanges renamed = await WaitForExternalChangesAsync(workspace, "rename",
            change => change.Changes.Any(item => item.Path == "feature/de.rmf2" && !item.Exists) &&
                      change.Changes.Any(item => item.Path == "feature/fr.rmf2" && item.Exists)).ConfigureAwait(false);
        Require(renamed.Changes.Any(item => item.Path == "feature/de.rmf2" && !item.Exists) &&
                renamed.Changes.Any(item => item.Path == "feature/fr.rmf2" && item.Exists), "Mounted source rename was not reconciled.");

        File.Delete(french);
        EditorExternalChanges deleted = await WaitForExternalChangesAsync(workspace, "delete",
            change => change.Changes.Any(item => item.Path == "feature/fr.rmf2" && !item.Exists)).ConfigureAwait(false);
        Require(deleted.Changes.Any(item => item.Path == "feature/fr.rmf2" && !item.Exists), "Mounted source deletion was not reconciled.");

        string nested = Path.Combine(feature, "nested");
        Directory.CreateDirectory(nested);
        string nestedSource = Path.Combine(nested, "it.rmf2");
        await File.WriteAllTextAsync(nestedSource, "title = Ciao\n").ConfigureAwait(false);
        await WaitForExternalChangesAsync(workspace, "directory-add",
            change => change.Changes.Any(item => item.Path == "feature/nested/it.rmf2" && item.Exists)).ConfigureAwait(false);
        string moved = Path.Combine(feature, "moved");
        Directory.Move(nested, moved);
        EditorExternalChanges directoryRenamed = await WaitForExternalChangesAsync(workspace, "directory-rename",
            change => change.Changes.Any(item => item.Path == "feature/nested/it.rmf2" && !item.Exists) &&
                      change.Changes.Any(item => item.Path == "feature/moved/it.rmf2" && item.Exists)).ConfigureAwait(false);
        Require(directoryRenamed.Changes.Any(item => item.Path == "feature/nested/it.rmf2" && !item.Exists) &&
                directoryRenamed.Changes.Any(item => item.Path == "feature/moved/it.rmf2" && item.Exists),
            "Mounted directory rename was not reconciled as an inventory change.");
        Directory.Delete(moved, recursive: true);
        EditorExternalChanges directoryDeleted = await WaitForExternalChangesAsync(workspace, "directory-delete",
            change => change.Changes.Any(item => item.Path == "feature/moved/it.rmf2" && !item.Exists)).ConfigureAwait(false);
        Require(directoryDeleted.Changes.Any(item => item.Path == "feature/moved/it.rmf2" && !item.Exists),
            "Mounted directory deletion was not reconciled as an inventory change.");
    }

    private static async Task<EditorExternalChanges> WaitForExternalChangesAsync(
        EditorWorkspace workspace,
        string operation,
        Func<EditorExternalChanges, bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            EditorExternalChanges changes = await workspace.CheckExternalChangesAsync().ConfigureAwait(false);
            if (predicate(changes)) return changes;
            await Task.Delay(50).ConfigureAwait(false);
        }
        throw new InvalidOperationException("Timed out waiting for mounted source watcher reconciliation (" + operation + ").");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

}
