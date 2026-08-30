using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json.Nodes;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

internal static class EditorSmokeTest
{
    public static async Task<int> RunAsync(string workspacePath)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, temporaryRoot);
        try
        {
            await VerifyDeterministicDiffsAsync(workspacePath).ConfigureAwait(false);
            await VerifyHistorySafetyAsync(workspacePath).ConfigureAwait(false);
            await VerifyRecoveryEvidenceAsync(workspacePath).ConfigureAwait(false);
            await VerifyLayeredHistoryScopeAsync(workspacePath).ConfigureAwait(false);
            await VerifyInterchangeRoundTripAsync(workspacePath).ConfigureAwait(false);

            using var workspace = new EditorWorkspace(temporaryRoot);
            WorkspaceSnapshot snapshot = await workspace.LoadAsync().ConfigureAwait(false);
            Require(snapshot.Success, Diagnostics(snapshot));
            EditorCatalog catalog = snapshot.Catalog
                ?? throw new InvalidOperationException("The editor did not discover the catalog.");
            Require(catalog.Locales.Count >= 1, "The editor did not discover any locales.");

            EditorDocument document = snapshot.Documents.Single(static candidate => candidate.Path == "product.de.json");
            ValidationResult unchanged = await workspace.ValidateAsync(document.Path, document.Content).ConfigureAwait(false);
            Require(unchanged.Success, "An unchanged resource document did not validate.");

            EditorMessagePreview messagePreview = await workspace.PreviewMessageAsync(
                document.Path, document.Content, "de", "Files.Selected").ConfigureAwait(false);
            Require(messagePreview.Success && messagePreview.AstJson is not null,
                "The compiler-backed schema-v2 message preview was not produced.");
            string previewJson = messagePreview.AstJson
                ?? throw new InvalidOperationException("The compiler-backed preview returned no AST.");
            JsonNode previewAst = JsonNode.Parse(previewJson)
                ?? throw new InvalidOperationException("The preview AST was not valid JSON.");
            Require(previewAst["astVersion"]?.GetValue<int>() == 2,
                "The editor preview did not use normalized message AST v2.");
            Require(previewAst["selectors"]?.AsArray().Count == 1 && previewAst["variants"]?.AsArray().Count == 2,
                "The editor preview lost selectors or variants.");

            var reviewRequest = new EditorReviewSaveRequest(
                null,
                [new EditorReviewEntry("Files.Selected", "de", "approved", "Checked in smoke test", "source:smoke", new Dictionary<string, string> { ["count"] = "2" })],
                [new EditorTerminologyEntry("Datei", "Datei", "de", "Product term")]);
            EditorReviewOperationResult reviewSaved = await workspace.SaveReviewAsync(reviewRequest).ConfigureAwait(false);
            Require(reviewSaved.Ok && reviewSaved.Review?.Revision is not null,
                reviewSaved.Message ?? "The editor-state sidecar was not saved.");
            EditorReviewOperationResult reviewConflict = await workspace.SaveReviewAsync(reviewRequest).ConfigureAwait(false);
            Require(!reviewConflict.Ok, "A stale editor-state revision overwrote review data.");
            WorkspaceSnapshot reviewedSnapshot = await workspace.LoadAsync().ConfigureAwait(false);
            Require(reviewedSnapshot.Review?.Entries.Count == 1 && reviewedSnapshot.Success,
                "Review state did not round-trip independently of compiler inputs.");
            EditorAbout about = EditorDiagnostics.About();
            Require(about.Product == "Runic Translations Editor" && about.UpdateChannel.Length > 0,
                "The editor did not expose version and update-channel information.");
            EditorDiagnosticBundleResult diagnosticBundle = EditorDiagnostics.CreateBundle(reviewedSnapshot);
            Require(diagnosticBundle.Ok && File.Exists(diagnosticBundle.Path),
                diagnosticBundle.Message ?? "The sanitized diagnostic bundle was not created.");
            string diagnosticPath = diagnosticBundle.Path
                ?? throw new InvalidOperationException("The diagnostic bundle returned no path.");
            using (ZipArchive archive = ZipFile.OpenRead(diagnosticPath))
            {
                Require(archive.GetEntry("LICENSE.txt") is not null && archive.GetEntry("THIRD-PARTY-NOTICES.md") is not null,
                    "The diagnostic bundle omitted legal notices.");
                using StreamReader reader = new(archive.GetEntry("diagnostics.json")!.Open());
                string diagnosticJson = await reader.ReadToEndAsync().ConfigureAwait(false);
                Require(diagnosticJson.Contains("runic.translations.editor-diagnostics/1", StringComparison.Ordinal),
                    "The diagnostic bundle schema was not versioned.");
                Require(!diagnosticJson.Contains(temporaryRoot, StringComparison.Ordinal) &&
                    !diagnosticJson.Contains("product.de.json", StringComparison.Ordinal) &&
                    !diagnosticJson.Contains("Speichern", StringComparison.Ordinal),
                    "The sanitized diagnostic bundle leaked a workspace path, file name, or translation.");
                Require(new FileInfo(diagnosticPath).Length <= 2_097_152 && archive.Entries.Count <= 3,
                    "The sanitized diagnostic bundle exceeded its distribution resource bounds.");
            }
            Require(EditorDiagnostics.DeleteBundle(diagnosticPath).Ok && !File.Exists(diagnosticPath),
                "The editor could not delete its owned diagnostic bundle.");

            ValidationResult invalid = await workspace.ValidateAsync(document.Path, document.Content + ",").ConfigureAwait(false);
            Require(!invalid.Success, "Invalid JSON unexpectedly validated.");

            string edited = document.Content.TrimEnd() + Environment.NewLine + Environment.NewLine;
            EditorOperationResult saved = await workspace.SaveAsync(document.Path, edited, document.Revision).ConfigureAwait(false);
            Require(saved.Ok, saved.Message ?? "The valid document was not saved.");
            WorkspaceSnapshot savedSnapshot = await workspace.LoadAsync().ConfigureAwait(false);
            Require(savedSnapshot.Documents.Single(candidate => candidate.Path == document.Path).Content == edited,
                "The direct workspace save did not persist its planned bytes.");

            EditorOperationResult conflict = await workspace.SaveAsync(document.Path, edited + Environment.NewLine, document.Revision).ConfigureAwait(false);
            Require(!conflict.Ok && conflict.Kind == "conflict", "An obsolete revision overwrote a newer document.");

            string secondProjectPath = Path.Combine(temporaryRoot, "SecondCatalog");
            TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(
                    secondProjectPath,
                    "backoffice",
                    "en",
                    "Customer.Backoffice",
                    "BackofficeText")));
            using (var multiCatalogSession = new EditorSession(temporaryRoot))
            {
                WorkspaceSnapshot choice = await multiCatalogSession.LoadAsync().ConfigureAwait(false);
                Require(choice.Catalog is null, "A multi-catalog workspace was selected without an explicit choice.");
                Require(choice.Catalogs.Count == 2, "The editor did not expose both discovered catalogs.");

                EditorOperationResult selected = await multiCatalogSession.OpenWorkspaceAsync(
                    new EditorOpenWorkspaceRequest(temporaryRoot, "customer-product")).ConfigureAwait(false);
                Require(selected.Ok && selected.Snapshot?.Catalog?.Id == "customer-product", selected.Message ?? "The requested catalog was not selected.");
                WorkspaceSnapshot selectedSnapshot = selected.Snapshot
                    ?? throw new InvalidOperationException("Catalog selection returned no workspace snapshot.");
                Require(selectedSnapshot.Documents.All(static candidate =>
                    candidate.IsMalformed || !candidate.Path.StartsWith("SecondCatalog/", StringComparison.Ordinal)),
                    "Documents from a different catalog leaked into the selected catalog.");

                EditorDocument externallyEdited = selectedSnapshot.Documents.Single(static candidate => candidate.Path == "product.fr.json");
                await File.AppendAllTextAsync(Path.Combine(temporaryRoot, externallyEdited.Path), Environment.NewLine).ConfigureAwait(false);
                EditorExternalChanges externalChanges = await WaitForExternalChangesAsync(multiCatalogSession).ConfigureAwait(false);
                Require(externalChanges.Paths.Contains(externallyEdited.Path, StringComparer.Ordinal), "The file watcher did not report an external edit.");
                EditorOperationResult externalConflict = await multiCatalogSession.SaveAsync(
                    externallyEdited.Path,
                    externallyEdited.Content,
                    externallyEdited.Revision).ConfigureAwait(false);
                Require(!externalConflict.Ok && externalConflict.Kind == "conflict", "An external edit was silently replaced by a local draft.");
                _ = await multiCatalogSession.LoadAsync().ConfigureAwait(false);

                string damagedPath = Path.Combine(temporaryRoot, "product.de.json");
                string repairedContent = await File.ReadAllTextAsync(damagedPath).ConfigureAwait(false);
                await File.WriteAllTextAsync(damagedPath, "{").ConfigureAwait(false);
                WorkspaceSnapshot damaged = await multiCatalogSession.LoadAsync().ConfigureAwait(false);
                EditorDocument malformed = damaged.Documents.Single(static candidate => candidate.Path == "product.de.json");
                Require(malformed.IsMalformed, "Malformed JSON was not preserved as a repairable document.");
                ValidationResult repairedValidation = await multiCatalogSession.ValidateAsync(malformed.Path, repairedContent).ConfigureAwait(false);
                Require(repairedValidation.Success, "A repaired resource document did not validate.");
                EditorOperationResult repaired = await multiCatalogSession.SaveAsync(
                    malformed.Path,
                    repairedContent,
                    malformed.Revision).ConfigureAwait(false);
                Require(repaired.Ok && repaired.Snapshot is not null, repaired.Message ?? "The malformed resource document was not repaired.");
                WorkspaceSnapshot repairedSnapshot = repaired.Snapshot
                    ?? throw new InvalidOperationException("Repair returned no workspace snapshot.");
                Require(repairedSnapshot.Documents.All(static candidate => !candidate.IsMalformed), "The repaired document remained malformed.");

                var createMessage = new EditorMutationRequest(
                    "create-key", null, null, null, "base", null, null, "Integration.Added", "Added by smoke test");
                EditorMutationPreview mutationPreview = multiCatalogSession.PreviewMutation(createMessage);
                Require(mutationPreview.Ok && mutationPreview.Files.Count == 3, mutationPreview.Message ?? "The key creation preview was incomplete.");
                EditorOperationResult mutated = await ApplyConfirmedAsync(multiCatalogSession, createMessage).ConfigureAwait(false);
                Require(mutated.Ok && mutated.Snapshot?.Success == true, mutated.Message ?? "The key creation transaction failed.");
                WorkspaceSnapshot mutatedSnapshot = mutated.Snapshot
                    ?? throw new InvalidOperationException("Key creation returned no workspace snapshot.");
                Require(mutatedSnapshot.Documents.Count(document => document.Content.Contains("\"Added\"", StringComparison.Ordinal)) == 3,
                    "The new key was not committed across all locale documents.");

                var deleteMessage = new EditorMutationRequest(
                    "delete-key", null, null, null, null, null, "Integration.Added", null, null);
                EditorMutationPreview deletePreview = multiCatalogSession.PreviewMutation(deleteMessage);
                Require(deletePreview.Ok && deletePreview.ConfirmationToken is not null,
                    deletePreview.Message ?? "The destructive change did not produce a confirmation token.");
                EditorOperationResult deletedMessage = await multiCatalogSession.ApplyMutationAsync(
                    deleteMessage with { ConfirmationToken = deletePreview.ConfirmationToken }).ConfigureAwait(false);
                Require(deletedMessage.Ok && deletedMessage.Snapshot?.Success == true, deletedMessage.Message ?? "The key deletion transaction failed.");
            }

            File.Delete(Path.Combine(temporaryRoot, "product.en.json"));
            File.Delete(Path.Combine(temporaryRoot, "product.fr.json"));
            string manifestPath = Path.Combine(temporaryRoot, "product.catalog.json");
            JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false))!.AsObject();
            manifest["locales"] = new JsonArray(new JsonObject { ["tag"] = "de" });
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine).ConfigureAwait(false);
            WorkspaceSnapshot singleLocale = await workspace.LoadAsync().ConfigureAwait(false);
            Require(singleLocale.Success, Diagnostics(singleLocale));
            Require(singleLocale.Catalog?.Locales.Count == 1, "A single-locale catalog was not represented correctly.");

            string createdPath = Path.Combine(temporaryRoot, "CreatedProject");
            using var session = new EditorSession(temporaryRoot);
            var request = new EditorProjectCreationRequest(
                createdPath,
                "created-product",
                "de-de",
                [new EditorProjectLocaleRequest("en-us", null), new EditorProjectLocaleRequest("fr", "en-US")],
                "Customer.Created",
                "CreatedText",
                "base",
                true,
                true);
            EditorProjectPlan preview = EditorSession.PreviewProject(request);
            Require(preview.Ok && preview.Files.Count == 4, preview.Message ?? "Project preview failed.");
            EditorOperationResult created = await session.CreateProjectAsync(request).ConfigureAwait(false);
            Require(created.Ok && created.Snapshot?.Catalog?.Locales.Count == 3, created.Message ?? "Project creation failed.");
            WorkspaceSnapshot activeCreated = await session.LoadAsync().ConfigureAwait(false);
            Require(activeCreated.Root == Path.GetFullPath(createdPath), "The editor did not switch to the newly created project.");
            Require(activeCreated.Success, Diagnostics(activeCreated));

            Console.WriteLine($"PASS: editor loaded {catalog.Locales.Count} locales, selected one of multiple catalogs, repaired malformed JSON, previewed and committed deterministic structural diffs, round-tripped isolated review metadata, exported and re-imported XLIFF with a reviewable diff and surfaced interchange losses/refusals, round-tripped portable review JSON across the compiler fingerprint boundary, produced privacy-bounded diagnostics, handled a single-locale catalog, created a compiler-valid project, validated drafts, saved atomically, and rejected stale writes.");
            Console.WriteLine("RECOVERY-EVIDENCE: complete=1 rollback=1 blocked=2 stale-session=2 diagnostics=sanitized-counts");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL: {exception.Message}");
            return 1;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    // W40: the Editor consumes the exact portable tooling artifact.
    // The deliberate unsupported-metadata case is the structured plural message:
    // its export loss must surface and its import must refuse visibly instead of
    // being swallowed as a generic failure.
    private static async Task VerifyInterchangeRoundTripAsync(string workspacePath)
    {
        string structuredRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-interchange-structured-{Guid.NewGuid():N}");
        string textRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-interchange-text-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(workspacePath, structuredRoot);
            using (var session = new EditorSession(structuredRoot))
            {
                WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
                Require(loaded.Success && loaded.Catalog?.Id == "customer-product", Diagnostics(loaded));

                EditorXliffExportResult export = await session.ExportXliffAsync(null).ConfigureAwait(false);
                Require(export.Ok && export.Documents.Count == 2, export.Message ?? "The XLIFF export did not produce one document per non-default locale.");
                Require(export.Losses.Any(loss =>
                        loss.Code == "XLIFF21-STRUCTURED-MESSAGE" &&
                        loss.Location == "/Files.Selected" &&
                        loss.SemanticLoss),
                    "The export loss report did not surface the structured message as a semantic loss.");
                Require(export.Documents.All(document => File.Exists(Path.Combine(structuredRoot, document.Path.Replace('/', Path.DirectorySeparatorChar)))),
                    "The exported XLIFF documents were not written inside the workspace boundary.");

                string englishPath = Path.Combine(structuredRoot, ".runic-translations", "export", "customer-product.en.xliff");
                EditorXliffImportPlan refusal = await session.PreviewXliffImportAsync(englishPath).ConfigureAwait(false);
                Require(!refusal.Ok && refusal.Refusals.Any(item => item.Code == "XLIFF21-STRUCTURED-IMPORT"),
                    "A structured message import was refused without surfacing the interchange rejection code.");

                EditorReviewFileResult reviewExport = await session.ExportReviewJsonAsync(null).ConfigureAwait(false);
                Require(reviewExport.Ok && reviewExport.EntryCount == 0,
                    reviewExport.Message ?? "The portable review export failed for an empty sidecar.");
            }

            TranslationProjectWriter.Create(TranslationProjectScaffolder.Render(
                new TranslationProjectCreationRequest(
                    textRoot,
                    "interchange-product",
                    "de",
                    "Customer.Interchange",
                    "InterchangeText",
                    [new TranslationProjectLocale("en", null)],
                    "base",
                    true,
                    true)));
            using (var session = new EditorSession(textRoot))
            {
                WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
                Require(loaded.Success && loaded.Catalog?.Id == "interchange-product", Diagnostics(loaded));
                EditorDocument english = loaded.Documents.Single(static candidate => candidate.Path == "interchange-product.en.json");
                const string englishContent = """{"schemaVersion":2,"catalog":"interchange-product","locale":"en","layer":"base","resources":{"Application":{"Name":"Pipeline ready"}}}""";
                EditorOperationResult saved = await session.SaveAsync(english.Path, englishContent, english.Revision).ConfigureAwait(false);
                Require(saved.Ok, saved.Message ?? "The interchange fixture document was not saved.");

                EditorXliffExportResult lossless = await session.ExportXliffAsync(null).ConfigureAwait(false);
                Require(lossless.Ok && lossless.Documents.Count == 1 && lossless.Losses.Count == 0,
                    lossless.Message ?? "A text-only catalog did not export losslessly.");
                string xliffPath = Path.Combine(textRoot, ".runic-translations", "export", "interchange-product.en.xliff");
                Require(File.Exists(xliffPath), "The lossless export did not write its XLIFF document.");
                TranslationCompilation portableCompilation = TranslationsTooling.Compile(
                    [new TranslationSource("interchange-product.catalog.json", await File.ReadAllBytesAsync(Path.Combine(textRoot, "interchange-product.catalog.json")).ConfigureAwait(false))],
                    [new TranslationSource("interchange-product.de.json", await File.ReadAllBytesAsync(Path.Combine(textRoot, "interchange-product.de.json")).ConfigureAwait(false)),
                     new TranslationSource("interchange-product.en.json", await File.ReadAllBytesAsync(Path.Combine(textRoot, "interchange-product.en.json")).ConfigureAwait(false))]);
                Require(portableCompilation.Success && portableCompilation.Catalogs.Count == 1,
                    "The portable tooling did not compile the Editor's text fixture.");
                TranslationXliffExportResult portableExport = TranslationInterchange.ExportXliff21(portableCompilation);
                byte[] editorXliffBytes = await File.ReadAllBytesAsync(xliffPath).ConfigureAwait(false);
                Require(portableExport.Documents.Count == 1 && portableExport.Report.IsLossless &&
                        portableExport.Documents[0].Bytes.SequenceEqual(editorXliffBytes),
                    "The Editor did not export the canonical bytes and diagnostics from the portable tooling artifact.");
                string xliff = await File.ReadAllTextAsync(xliffPath).ConfigureAwait(false);
                Require(xliff.Contains("Pipeline ready", StringComparison.Ordinal), "The exported XLIFF omitted the target text.");
                await File.WriteAllTextAsync(xliffPath, xliff.Replace("Pipeline ready", "Imported pipeline", StringComparison.Ordinal)).ConfigureAwait(false);

                var previewed = await session.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
                Require(previewed.Ok && previewed.ConfirmationToken is not null,
                    previewed.Message ?? "The XLIFF import preview did not produce a confirmation token.");
                Require(previewed.ChangedCount == 1 && previewed.AddedCount == 0 &&
                        previewed.RemovedCount == 0 && previewed.UnchangedCount == 0,
                    "The import diff miscounted changed, added, removed, or unchanged keys.");
                EditorKeyChange change = previewed.Changes.Single(static item => item.Kind == "changed");
                Require(change.Key == "Application.Name" && change.Before == "Pipeline ready" && change.After == "Imported pipeline",
                    "The import diff lost the before/after values for the changed key.");

                string targetDocumentPath = Path.Combine(textRoot, "interchange-product.en.json");
                await File.WriteAllTextAsync(targetDocumentPath, englishContent.Replace("Pipeline ready", "Local update", StringComparison.Ordinal)).ConfigureAwait(false);
                EditorOperationResult staleTarget = await session.ApplyXliffImportAsync(
                    previewed.ConfirmationToken!).ConfigureAwait(false);
                Require(!staleTarget.Ok && staleTarget.Kind == "conflict" &&
                        (await File.ReadAllTextAsync(targetDocumentPath).ConfigureAwait(false)).Contains("Local update", StringComparison.Ordinal),
                    "An import preview overwrote a target translation document that changed after preview.");

                previewed = await session.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
                Require(previewed.Ok && previewed.ConfirmationToken is not null,
                    previewed.Message ?? "The XLIFF import could not be re-previewed after a target conflict.");

                EditorOperationResult applied = await session.ApplyXliffImportAsync(
                    previewed.ConfirmationToken!).ConfigureAwait(false);
                Require(applied.Ok && applied.Snapshot?.Success == true, applied.Message ?? "The confirmed XLIFF import failed to commit.");
                Require((await File.ReadAllTextAsync(Path.Combine(textRoot, "interchange-product.en.json")).ConfigureAwait(false))
                        .Contains("Imported pipeline", StringComparison.Ordinal),
                    "The applied import did not persist the imported translation.");
                EditorOperationResult replayed = await session.ApplyXliffImportAsync(previewed.ConfirmationToken!).ConfigureAwait(false);
                Require(!replayed.Ok, "An import confirmation token was accepted twice.");

                WorkspaceSnapshot afterImport = await session.LoadAsync().ConfigureAwait(false);
                var approved = new EditorReviewSaveRequest(
                    afterImport.Review?.Revision,
                    [new EditorReviewEntry("Application.Name", "en", "approved", "Checked by interchange", null, new Dictionary<string, string>())],
                    []);
                EditorReviewOperationResult reviewSaved = await session.SaveReviewAsync(approved).ConfigureAwait(false);
                Require(reviewSaved.Ok, reviewSaved.Message ?? "The approved review entry could not be saved.");

                // The approval carries no interchange fingerprint in the sidecar;
                // exporting must stamp it with the compiler fingerprint or fail.
                EditorXliffExportResult stamped = await session.ExportXliffAsync(null).ConfigureAwait(false);
                Require(stamped.Ok, stamped.Message ?? "Exporting an approved entry without a stored interchange fingerprint failed; stamp-on-export is broken.");
                EditorXliffImportPlan restamped = await session.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
                Require(restamped.Ok, restamped.Refusals.Count > 0 ? restamped.Refusals[0].Message :
                    "Re-importing an exported document with an approved entry failed the fingerprint reconciliation.");

                EditorReviewFileResult reviewJson = await session.ExportReviewJsonAsync(null).ConfigureAwait(false);
                Require(reviewJson.Ok && reviewJson.EntryCount == 1 && reviewJson.Path is not null,
                    reviewJson.Message ?? "The portable review export failed.");
                string reviewPath = Path.Combine(textRoot, reviewJson.Path!.Replace('/', Path.DirectorySeparatorChar));
                string reviewJsonText = await File.ReadAllTextAsync(reviewPath).ConfigureAwait(false);
                Require(reviewJsonText.Contains("\"sourceFingerprint\"", StringComparison.Ordinal),
                    "The exported review JSON did not stamp the approved entry with the compiler fingerprint.");
                byte[] reviewBytes = await File.ReadAllBytesAsync(reviewPath).ConfigureAwait(false);
                Require(reviewBytes.SequenceEqual(TranslationInterchange.ExportReviewJson(TranslationInterchange.ImportReviewJson(reviewBytes))),
                    "The Editor did not write the canonical portable review sidecar bytes.");
                await File.WriteAllTextAsync(reviewPath,
                    reviewJsonText.Replace("\"sourceFingerprint\":\"", "\"sourceFingerprint\":\"stale-", StringComparison.Ordinal)).ConfigureAwait(false);
                EditorReviewImportPlan staleApproval = await session.PreviewReviewJsonImportAsync(reviewPath).ConfigureAwait(false);
                Require(!staleApproval.Ok && staleApproval.Refusals.Any(item => item.Code == "EDITOR-APPROVAL-FINGERPRINT"),
                    "A review import accepted an approved entry whose source fingerprint did not match the open catalog.");
                await File.WriteAllTextAsync(reviewPath,
                    reviewJsonText.Replace("\"state\":\"approved\"", "\"state\":\"translated\"", StringComparison.Ordinal)).ConfigureAwait(false);

                var reviewPreview = await session.PreviewReviewJsonImportAsync(reviewPath).ConfigureAwait(false);
                Require(reviewPreview.Ok && reviewPreview.ConfirmationToken is not null,
                    reviewPreview.Message ?? "The review JSON import preview failed.");
                Require(reviewPreview.ChangedCount == 1 && reviewPreview.AddedCount == 0 && reviewPreview.RemovedCount == 0 &&
                        reviewPreview.Changes[0].StateBefore == "approved" && reviewPreview.Changes[0].StateAfter == "translated",
                    "The review import diff did not describe the state transition.");
                EditorReviewOperationResult reviewApplied = await session.ApplyReviewJsonImportAsync(
                    reviewPreview.ConfirmationToken!).ConfigureAwait(false);
                Require(reviewApplied.Ok, reviewApplied.Message ?? "The confirmed review import failed.");
                WorkspaceSnapshot reloaded = await session.LoadAsync().ConfigureAwait(false);
                EditorReviewEntry importedEntry = reloaded.Review?.Entries.SingleOrDefault() ?? throw new InvalidOperationException("The imported review entry vanished from the sidecar.");
                Require(importedEntry.State == "translated" && importedEntry.SourceFingerprint is null,
                    "Strip-on-import did not leave the imported entry unstamped while preserving its state.");
            }
        }
        finally
        {
            if (Directory.Exists(structuredRoot)) Directory.Delete(structuredRoot, true);
            if (Directory.Exists(textRoot)) Directory.Delete(textRoot, true);
        }
    }

    private static async Task VerifyDeterministicDiffsAsync(string workspacePath)
    {
        string firstRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-diff-a-{Guid.NewGuid():N}");
        string secondRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-diff-b-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, firstRoot);
        CopyDirectory(workspacePath, secondRoot);
        try
        {
            Dictionary<string, byte[]> before = ReadFiles(firstRoot);
            var mutation = new EditorMutationRequest(
                "create-key", null, null, null, "base", null, null, "Diff.Deterministic", "Reviewable output");
            var review = new EditorReviewSaveRequest(
                null,
                [new EditorReviewEntry("Diff.Deterministic", "de", "approved", "Deterministic review", "source:diff", new Dictionary<string, string>())],
                []);
            var reviewPaths = new List<string>();

            foreach (string root in new[] { firstRoot, secondRoot })
            {
                using var session = new EditorSession(root);
                WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
                Require(loaded.Success && loaded.Catalog is not null,
                    "The deterministic diff fixture did not load a catalog.");
                EditorMutationPreview preview = session.PreviewMutation(mutation);
                Require(preview.Ok, preview.Message ?? "The deterministic mutation could not be previewed.");
                Require(preview.Files.Select(static file => file.Path).SequenceEqual(
                    preview.Files.Select(static file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal),
                    "Mutation preview paths were not deterministically ordered.");
                EditorOperationResult applied = await ApplyConfirmedAsync(session, mutation).ConfigureAwait(false);
                Require(applied.Ok && applied.Snapshot?.Success == true,
                    applied.Message ?? "The deterministic mutation could not be applied.");
                EditorReviewOperationResult reviewSaved = await session.SaveReviewAsync(review).ConfigureAwait(false);
                Require(reviewSaved.Ok, reviewSaved.Message ?? "The deterministic review state could not be saved.");
                string reviewPath = reviewSaved.Review?.Path
                    ?? throw new InvalidOperationException("The deterministic review save returned no sidecar path.");
                reviewPaths.Add((Path.IsPathRooted(reviewPath) ? Path.GetRelativePath(root, reviewPath) : reviewPath)
                    .Replace('\\', '/'));
            }

            Dictionary<string, byte[]> first = ReadFiles(firstRoot);
            Dictionary<string, byte[]> second = ReadFiles(secondRoot);
            Require(first.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                second.Keys.Order(StringComparer.Ordinal), StringComparer.Ordinal),
                "Equivalent edits produced different file sets.");
            foreach (string path in first.Keys.Order(StringComparer.Ordinal))
                Require(first[path].AsSpan().SequenceEqual(second[path]), $"Equivalent edits produced different bytes for '{path}'.");

            string[] changedSourceFiles = before.Keys
                .Where(path => !before[path].AsSpan().SequenceEqual(first[path]))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Require(changedSourceFiles.SequenceEqual(
                new[] { "product.de.json", "product.en.json", "product.fr.json" },
                StringComparer.Ordinal),
                $"The key edit changed an unexpected source-file set: {string.Join(", ", changedSourceFiles)}.");
            string addedFile = first.Keys.Except(before.Keys, StringComparer.Ordinal).Single();
            Require(reviewPaths.Count == 2 && string.Equals(reviewPaths[0], reviewPaths[1], StringComparison.Ordinal) &&
                string.Equals(addedFile, reviewPaths[0], StringComparison.Ordinal),
                "Review-only data was not isolated in one deterministic editor-state sidecar.");
        }
        finally
        {
            if (Directory.Exists(firstRoot)) Directory.Delete(firstRoot, true);
            if (Directory.Exists(secondRoot)) Directory.Delete(secondRoot, true);
        }
    }

    private static async Task VerifyHistorySafetyAsync(string workspacePath)
    {
        string root = Path.Combine(Path.GetTempPath(), $"runic-editor-history-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, root);
        try
        {
            using var session = new EditorSession(root);
            WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
            Require(loaded.History is { CanUndo: false, CanRedo: false },
                "A new editor session unexpectedly has history.");

            var create = new EditorMutationRequest(
                "create-key", null, null, null, "base", null, null, "History.Created", "Undoable message");
            EditorOperationResult created = await ApplyConfirmedAsync(session, create).ConfigureAwait(false);
            Require(created.Ok && created.Snapshot?.History?.CanUndo == false,
                created.Message ?? "A confirmation-bound catalog mutation incorrectly advertised undo.");
            Require(created.Snapshot!.Documents.Count(document => document.Content.Contains("History", StringComparison.Ordinal)) == 3,
                "The reversible mutation did not update every locale.");

            EditorDocument savedDocument = created.Snapshot!.Documents.Single(document => document.Path == "product.de.json");
            EditorOperationResult savedHistory = await session.SaveAsync("/product.de.json", savedDocument.Content + Environment.NewLine, savedDocument.Revision).ConfigureAwait(false);
            Require(savedHistory.Ok && savedHistory.Snapshot?.History?.CanUndo == true,
                savedHistory.Message ?? "A leading-slash document save was not recorded for undo.");
            EditorOperationResult undone = await session.UndoAsync().ConfigureAwait(false);
            Require(undone.Ok && undone.Snapshot?.History?.CanRedo == true,
                undone.Message ?? "Undo did not restore the document state.");
            EditorOperationResult redone = await session.RedoAsync().ConfigureAwait(false);
            Require(redone.Ok && redone.Snapshot?.History?.CanUndo == true,
                redone.Message ?? "Redo did not restore the document state.");
            EditorDocument backslashDocument = redone.Snapshot!.Documents.Single(document => document.Path == "product.de.json");
            EditorOperationResult backslashSaved = await session.SaveAsync("\\product.de.json", backslashDocument.Content + Environment.NewLine, backslashDocument.Revision).ConfigureAwait(false);
            Require(backslashSaved.Ok && backslashSaved.Snapshot?.History is { CanUndo: true, CanRedo: false },
                backslashSaved.Message ?? "A backslash document save did not record history or invalidate redo.");

            string externalPath = Path.Combine(root, "product.de.json");
            await File.AppendAllTextAsync(externalPath, Environment.NewLine).ConfigureAwait(false);
            EditorOperationResult staleUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(!staleUndo.Ok && staleUndo.Kind == "conflict" &&
                (await File.ReadAllTextAsync(externalPath).ConfigureAwait(false)).Contains("\"History\"", StringComparison.Ordinal),
                "A stale undo changed externally edited catalog files.");

            WorkspaceSnapshot beforeInterrupt = await session.LoadAsync().ConfigureAwait(false);
            TranslationWorkspaceTransactionPlan undoPlan = TranslationWorkspaceMutation.MutateKey(
                new TranslationKeyMutationRequest(root, beforeInterrupt.Catalog!.Id,
                    TranslationKeyMutationKind.Delete, "History.Created", null));
            Interrupt(undoPlan);
            EditorOperationResult blockedUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(!blockedUndo.Ok && blockedUndo.Kind == "recovery-required",
                "An interrupted undo was not blocked behind transaction recovery.");
            EditorOperationResult recoveredUndo = await session.RecoverTransactionAsync(new EditorRecoveryRequest("rollback")).ConfigureAwait(false);
            Require(recoveredUndo.Ok && recoveredUndo.Snapshot?.History?.CanUndo == false &&
                recoveredUndo.Snapshot.Documents.Count(document => document.Content.Contains("\"History\"", StringComparison.Ordinal)) == 3,
                recoveredUndo.Message ?? "Recovery after an interrupted undo corrupted the catalog or retained unsafe history.");

            EditorOperationResult deletedWithoutConfirmation = await session.ApplyMutationAsync(
                new EditorMutationRequest("delete-key", null, null, null, null, null, "History.Created", null, null)).ConfigureAwait(false);
            Require(!deletedWithoutConfirmation.Ok && deletedWithoutConfirmation.Kind == "irreversible-confirmation",
                "A destructive mutation bypassed the irreversible confirmation boundary.");

            var deleteCreated = new EditorMutationRequest("delete-key", null, null, null, null, null, "History.Created", null, null);
            EditorMutationPreview staleDeletePreview = session.PreviewMutation(deleteCreated);
            Require(staleDeletePreview.Ok && staleDeletePreview.ConfirmationToken is not null,
                staleDeletePreview.Message ?? "The destructive preview did not produce a confirmation token.");
            await File.AppendAllTextAsync(Path.Combine(root, "product.en.json"), Environment.NewLine).ConfigureAwait(false);
            EditorOperationResult staleDelete = await session.ApplyMutationAsync(
                deleteCreated with { ConfirmationToken = staleDeletePreview.ConfirmationToken }).ConfigureAwait(false);
            Require(!staleDelete.Ok && staleDelete.Kind == "irreversible-confirmation" &&
                (await File.ReadAllTextAsync(externalPath).ConfigureAwait(false)).Contains("\"History\"", StringComparison.Ordinal),
                "A destructive confirmation was accepted after its previewed plan changed.");

            EditorOperationResult invalidationCreated = await ApplyConfirmedAsync(session,
                new EditorMutationRequest("create-key", null, null, null, "base", null, null, "History.Invalidate", "Invalidate redo")).ConfigureAwait(false);
            Require(invalidationCreated.Ok, invalidationCreated.Message ?? "The redo invalidation fixture could not be created.");
            WorkspaceSnapshot invalidationSnapshot = await session.LoadAsync().ConfigureAwait(false);
            EditorDocument invalidationDocument = invalidationSnapshot.Documents.Single(document => document.Path == "product.de.json");
            EditorOperationResult invalidationSaved = await session.SaveAsync(invalidationDocument.Path, invalidationDocument.Content + Environment.NewLine, invalidationDocument.Revision).ConfigureAwait(false);
            Require(invalidationSaved.Ok, invalidationSaved.Message ?? "The redo invalidation document fixture could not be saved.");
            EditorOperationResult invalidationUndone = await session.UndoAsync().ConfigureAwait(false);
            Require(invalidationUndone.Ok && invalidationUndone.Snapshot?.History?.CanRedo == true,
                invalidationUndone.Message ?? "The redo invalidation fixture could not be undone.");
            EditorMutationPreview confirmedDeletePreview = session.PreviewMutation(deleteCreated);
            EditorOperationResult replacingMutation = await session.ApplyMutationAsync(
                deleteCreated with { ConfirmationToken = confirmedDeletePreview.ConfirmationToken }).ConfigureAwait(false);
            Require(replacingMutation.Ok, replacingMutation.Message ?? "A destructive mutation could not replace the redo branch.");
            EditorOperationResult invalidatedRedo = await session.RedoAsync().ConfigureAwait(false);
            Require(!invalidatedRedo.Ok && invalidatedRedo.Kind == "nothing-to-redo",
                "A destructive mutation did not invalidate the redo branch.");

            WorkspaceSnapshot redoSaveBase = await session.LoadAsync().ConfigureAwait(false);
            EditorDocument redoDocument = redoSaveBase.Documents.Single(document => document.Path == "product.de.json");
            EditorOperationResult redoCreated = await session.SaveAsync(redoDocument.Path, redoDocument.Content + Environment.NewLine, redoDocument.Revision).ConfigureAwait(false);
            Require(redoCreated.Ok, redoCreated.Message ?? "The redo interruption fixture could not be created.");
            EditorOperationResult redoPrepared = await session.UndoAsync().ConfigureAwait(false);
            Require(redoPrepared.Ok && redoPrepared.Snapshot?.History?.CanRedo == true,
                redoPrepared.Message ?? "The redo interruption fixture could not be undone.");
            WorkspaceSnapshot redoBase = await session.LoadAsync().ConfigureAwait(false);
            TranslationWorkspaceTransactionPlan redoPlan = TranslationWorkspaceMutation.CreateKey(
                new TranslationCreateKeyRequest(root, redoBase.Catalog!.Id, "History.Redo", "Redo safety", "base"));
            Interrupt(redoPlan);
            EditorOperationResult blockedRedo = await session.RedoAsync().ConfigureAwait(false);
            Require(!blockedRedo.Ok && blockedRedo.Kind == "recovery-required",
                "An interrupted redo was not blocked behind transaction recovery.");
            EditorOperationResult recoveredRedo = await session.RecoverTransactionAsync(new EditorRecoveryRequest("rollback")).ConfigureAwait(false);
            Require(recoveredRedo.Ok && recoveredRedo.Snapshot?.History?.CanRedo == false &&
                recoveredRedo.Snapshot.Documents.All(document => !document.Content.Contains("\"Redo\"", StringComparison.Ordinal)),
                recoveredRedo.Message ?? "Recovery after an interrupted redo corrupted the catalog or retained unsafe history.");

            WorkspaceSnapshot current = await session.LoadAsync().ConfigureAwait(false);
            EditorDocument document = current.Documents.Single(candidate => candidate.Path == "product.de.json");
            string content = document.Content;
            string revision = document.Revision;
            for (int index = 0; index < 65; index++)
            {
                content += Environment.NewLine;
                EditorOperationResult saved = await session.SaveAsync(document.Path, content, revision).ConfigureAwait(false);
                Require(saved.Ok && saved.Snapshot is not null, saved.Message ?? "A history-bound save failed.");
                revision = saved.Snapshot.Documents.Single(candidate => candidate.Path == document.Path).Revision;
            }
            for (int index = 0; index < 64; index++)
            {
                EditorOperationResult savedUndo = await session.UndoAsync().ConfigureAwait(false);
                Require(savedUndo.Ok, savedUndo.Message ?? "A bounded history save could not be undone.");
            }
            EditorOperationResult exhausted = await session.UndoAsync().ConfigureAwait(false);
            Require(!exhausted.Ok && exhausted.Kind == "nothing-to-undo",
                "Undo history did not discard its oldest entry at the configured bound.");

            WorkspaceSnapshot reviewBase = await session.LoadAsync().ConfigureAwait(false);
            var firstReview = new EditorReviewSaveRequest(
                reviewBase.Review?.Revision,
                [new EditorReviewEntry("History.Created", "de", "translated", "first", "history:first", new Dictionary<string, string>())],
                []);
            EditorReviewOperationResult firstReviewSaved = await session.SaveReviewAsync(firstReview).ConfigureAwait(false);
            Require(firstReviewSaved.Ok && firstReviewSaved.Review?.Revision is not null,
                firstReviewSaved.Message ?? "The initial review sidecar could not be saved.");
            string firstReviewPath = firstReviewSaved.Review.Path;
            // The redo-invalidation fixture already deleted History.Created, so the
            // review token needs its own live destructive target.
            EditorOperationResult reviewKeyCreated = await ApplyConfirmedAsync(session,
                new EditorMutationRequest("create-key", null, null, null, "base", null, null, "History.Review", "Review fixture")).ConfigureAwait(false);
            Require(reviewKeyCreated.Ok, reviewKeyCreated.Message ?? "The review-token fixture could not create its target key.");
            var reviewTokenRequest = new EditorMutationRequest("delete-key", null, null, null, null, null, "History.Review", null, null);
            EditorMutationPreview reviewTokenPreview = session.PreviewMutation(reviewTokenRequest);
            Require(reviewTokenPreview.Ok && reviewTokenPreview.ConfirmationToken is not null,
                reviewTokenPreview.Message ?? "The review-token fixture did not produce a confirmation token.");
            EditorOperationResult firstReviewUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(firstReviewUndo.Ok && !File.Exists(Path.Combine(root, firstReviewPath)) &&
                firstReviewUndo.Snapshot?.Review?.Entries.Count == 0,
                firstReviewUndo.Message ?? "Undoing the initial review save did not remove the sidecar.");
            EditorOperationResult consumedReviewToken = await session.ApplyMutationAsync(
                reviewTokenRequest with { ConfirmationToken = reviewTokenPreview.ConfirmationToken }).ConfigureAwait(false);
            Require(!consumedReviewToken.Ok && consumedReviewToken.Kind == "irreversible-confirmation",
                "Undoing review history did not consume the prepared destructive mutation token.");
            EditorOperationResult firstReviewRedo = await session.RedoAsync().ConfigureAwait(false);
            Require(firstReviewRedo.Ok && firstReviewRedo.Snapshot?.Review?.Entries.Single().State == "translated",
                firstReviewRedo.Message ?? "Redoing the initial review save did not restore its data.");
            var secondReview = firstReview with
            {
                ExpectedRevision = firstReviewRedo.Snapshot?.Review?.Revision,
                Entries = [new EditorReviewEntry("History.Created", "de", "approved", "second", "history:second", new Dictionary<string, string>())],
            };
            EditorReviewOperationResult secondReviewSaved = await session.SaveReviewAsync(secondReview).ConfigureAwait(false);
            Require(secondReviewSaved.Ok && secondReviewSaved.History?.CanUndo == true,
                secondReviewSaved.Message ?? "A review update was not recorded for undo.");
            EditorOperationResult reviewUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(reviewUndo.Ok && reviewUndo.Snapshot?.Review?.Entries.Single().State == "translated",
                reviewUndo.Message ?? "Review undo did not restore the previous sidecar state.");
            EditorOperationResult reviewRedo = await session.RedoAsync().ConfigureAwait(false);
            Require(reviewRedo.Ok && reviewRedo.Snapshot?.Review?.Entries.Single().State == "approved",
                reviewRedo.Message ?? "Review redo did not restore the newer sidecar state.");
            string reviewPath = reviewRedo.Snapshot.Review?.Path
                ?? throw new InvalidOperationException("The review redo returned no sidecar path.");
            await File.AppendAllTextAsync(Path.Combine(root, reviewPath), Environment.NewLine).ConfigureAwait(false);
            EditorOperationResult staleReviewUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(!staleReviewUndo.Ok && staleReviewUndo.Kind == "conflict",
                "A stale review revision overwrote workflow data during undo.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task VerifyRecoveryEvidenceAsync(string workspacePath)
    {
        string root = Path.Combine(Path.GetTempPath(), $"runic-editor-recovery-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, root);
        try
        {
            using var session = new EditorSession(root);
            WorkspaceSnapshot initial = await session.LoadAsync().ConfigureAwait(false);
            Require(initial.Success && initial.Catalog is not null, Diagnostics(initial));

            var staleCompleteRequest = new EditorMutationRequest(
                "create-key", null, null, null, "base", null, null, "Recovery.StaleComplete", "Stale complete");
            EditorMutationPreview staleComplete = session.PreviewMutation(staleCompleteRequest);
            Require(staleComplete.Ok && staleComplete.ConfirmationToken is not null,
                staleComplete.Message ?? "The complete-recovery stale confirmation fixture was not prepared.");
            TranslationWorkspaceTransactionPlan completePlan = TranslationWorkspaceMutation.CreateKey(
                new TranslationCreateKeyRequest(root, initial.Catalog.Id, "Recovery.Completed", "Complete recovery", "base"));
            Interrupt(completePlan);
            EditorOperationResult blockedComplete = await session.ApplyMutationAsync(
                staleCompleteRequest with { ConfirmationToken = staleComplete.ConfirmationToken }).ConfigureAwait(false);
            Require(!blockedComplete.Ok && blockedComplete.Kind == "recovery-required",
                "A pending transaction did not block mutation before complete recovery.");
            EditorOperationResult completed = await session.RecoverTransactionAsync(new EditorRecoveryRequest("complete")).ConfigureAwait(false);
            WorkspaceSnapshot completeSnapshot = completed.Snapshot
                ?? throw new InvalidOperationException(completed.Message ?? "Complete recovery returned no workspace snapshot.");
            EditorDocument[] completedResources = completeSnapshot.Documents.Where(document => !document.IsManifest).ToArray();
            int completedDocumentCount = completedResources.Count(document =>
                document.Content.Contains("Complete recovery", StringComparison.Ordinal));
            Require(completed.Ok && completeSnapshot.PendingTransaction is null &&
                completedDocumentCount == completedResources.Length,
                $"Complete recovery did not finish every planned document edit (ok={completed.Ok}, pending={completeSnapshot.PendingTransaction is not null}, changed={completedDocumentCount}/{completedResources.Length}).");
            EditorOperationResult staleCompleteReplay = await session.ApplyMutationAsync(
                staleCompleteRequest with { ConfirmationToken = staleComplete.ConfirmationToken }).ConfigureAwait(false);
            Require(!staleCompleteReplay.Ok && staleCompleteReplay.Kind == "irreversible-confirmation" &&
                staleCompleteReplay.Snapshot is null,
                "Complete recovery resumed a stale prepared editor session mutation.");

            WorkspaceSnapshot afterComplete = await session.LoadAsync().ConfigureAwait(false);
            var staleRollbackRequest = new EditorMutationRequest(
                "create-key", null, null, null, "base", null, null, "Recovery.StaleRollback", "Stale rollback");
            EditorMutationPreview staleRollback = session.PreviewMutation(staleRollbackRequest);
            Require(staleRollback.Ok && staleRollback.ConfirmationToken is not null,
                staleRollback.Message ?? "The rollback-recovery stale confirmation fixture was not prepared.");
            TranslationWorkspaceTransactionPlan rollbackPlan = TranslationWorkspaceMutation.CreateKey(
                new TranslationCreateKeyRequest(root, afterComplete.Catalog!.Id, "Recovery.RolledBack", "Rollback recovery", "base"));
            Interrupt(rollbackPlan);
            EditorOperationResult blockedRollback = await session.ApplyMutationAsync(
                staleRollbackRequest with { ConfirmationToken = staleRollback.ConfirmationToken }).ConfigureAwait(false);
            Require(!blockedRollback.Ok && blockedRollback.Kind == "recovery-required",
                "A pending transaction did not block mutation before rollback recovery.");
            EditorOperationResult rolledBack = await session.RecoverTransactionAsync(new EditorRecoveryRequest("rollback")).ConfigureAwait(false);
            WorkspaceSnapshot rollbackSnapshot = rolledBack.Snapshot
                ?? throw new InvalidOperationException(rolledBack.Message ?? "Rollback recovery returned no workspace snapshot.");
            Require(rolledBack.Ok && rollbackSnapshot.PendingTransaction is null &&
                rollbackSnapshot.Documents.All(document => !document.Content.Contains("Rollback recovery", StringComparison.Ordinal)),
                rolledBack.Message ?? "Rollback recovery did not restore every interrupted document edit.");
            EditorOperationResult staleRollbackReplay = await session.ApplyMutationAsync(
                staleRollbackRequest with { ConfirmationToken = staleRollback.ConfirmationToken }).ConfigureAwait(false);
            Require(!staleRollbackReplay.Ok && staleRollbackReplay.Kind == "irreversible-confirmation" &&
                staleRollbackReplay.Snapshot is null,
                "Rollback recovery resumed a stale prepared editor session mutation.");

            EditorDiagnosticBundleResult diagnostics = await session.CreateDiagnosticBundleAsync().ConfigureAwait(false);
            Require(diagnostics.Ok && diagnostics.Path is not null, diagnostics.Message ?? "Recovery diagnostics were not created.");
            using (ZipArchive archive = ZipFile.OpenRead(diagnostics.Path))
            using (StreamReader reader = new(archive.GetEntry("diagnostics.json")!.Open()))
            {
                string summary = await reader.ReadToEndAsync().ConfigureAwait(false);
                Require(!summary.Contains(root, StringComparison.Ordinal) &&
                    !summary.Contains("Complete recovery", StringComparison.Ordinal) &&
                    !summary.Contains("Rollback recovery", StringComparison.Ordinal),
                    "Recovery diagnostics leaked workspace or translation text.");
            }
            Require(EditorDiagnostics.DeleteBundle(diagnostics.Path).Ok && !File.Exists(diagnostics.Path),
                "The editor could not delete recovery diagnostics from its owned bundle directory.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static async Task<EditorOperationResult> ApplyConfirmedAsync(
        EditorSession session,
        EditorMutationRequest request)
    {
        EditorMutationPreview preview = session.PreviewMutation(request);
        Require(preview.Ok && preview.ConfirmationToken is not null,
            preview.Message ?? "The catalog mutation did not produce a scoped confirmation token.");
        return await session.ApplyMutationAsync(request with { ConfirmationToken = preview.ConfirmationToken }).ConfigureAwait(false);
    }

    private static async Task VerifyLayeredHistoryScopeAsync(string workspacePath)
    {
        string root = Path.Combine(Path.GetTempPath(), $"runic-editor-history-layered-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, root);
        try
        {
            string manifestPath = Path.Combine(root, "product.catalog.json");
            JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false))!.AsObject();
            manifest["layers"]!.AsArray().Add(new JsonObject { ["name"] = "override", ["priority"] = 1 });
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine).ConfigureAwait(false);
            foreach (string source in Directory.EnumerateFiles(root, "product.*.json"))
            {
                if (Path.GetFileName(source) == "product.catalog.json") continue;
                JsonObject document = JsonNode.Parse(await File.ReadAllTextAsync(source).ConfigureAwait(false))!.AsObject();
                string locale = document["locale"]!.GetValue<string>();
                document["layer"] = "override";
                document["resources"] = new JsonObject { ["History"] = new JsonObject { ["Created"] = "Pre-existing override" } };
                await File.WriteAllTextAsync(Path.Combine(root, $"override.{locale}.json"),
                    document.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine).ConfigureAwait(false);
            }

            using var session = new EditorSession(root);
            WorkspaceSnapshot loaded = await session.LoadAsync().ConfigureAwait(false);
            Require(loaded.Success, Diagnostics(loaded));
            EditorOperationResult created = await ApplyConfirmedAsync(session,
                new EditorMutationRequest("create-key", null, null, null, "base", null, null, "History.Created", "Base value")).ConfigureAwait(false);
            Require(created.Ok, created.Message ?? "The layered history fixture could not create its base key.");
            EditorOperationResult undo = await session.UndoAsync().ConfigureAwait(false);
            Require(!undo.Ok && undo.Kind == "nothing-to-undo" &&
                (await File.ReadAllTextAsync(Path.Combine(root, "override.de.json")).ConfigureAwait(false)).Contains("Pre-existing override", StringComparison.Ordinal),
                "A layered catalog mutation advertised an unsafe broad inverse.");

            EditorOperationResult externalCreated = await ApplyConfirmedAsync(session,
                new EditorMutationRequest("create-key", null, null, null, "base", null, null, "History.External", "Base value")).ConfigureAwait(false);
            Require(externalCreated.Ok, externalCreated.Message ?? "The external-addition history fixture could not create its base key.");
            string overridePath = Path.Combine(root, "override.de.json");
            JsonObject overrideDocument = JsonNode.Parse(await File.ReadAllTextAsync(overridePath).ConfigureAwait(false))!.AsObject();
            overrideDocument["resources"]!.AsObject()["History"]!.AsObject()["External"] = "External addition";
            await File.WriteAllTextAsync(overridePath, overrideDocument.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine).ConfigureAwait(false);
            EditorOperationResult externalUndo = await session.UndoAsync().ConfigureAwait(false);
            Require(!externalUndo.Ok && externalUndo.Kind == "nothing-to-undo" &&
                (await File.ReadAllTextAsync(overridePath).ConfigureAwait(false)).Contains("External addition", StringComparison.Ordinal),
                "A catalog mutation retained a broad inverse after an external layered addition.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    // No public-surface interruption exists: TranslationWorkspaceTransaction.Commit
    // rolls back and removes the journal on every recoverable failure, so a retained
    // journal is only reachable through the authoring assembly's internal test hook.
    // Reflection stays confined to this helper so recovery coverage survives the
    // Authoring package being folded into Runic.Translations.Tooling.
    private static void Interrupt(TranslationWorkspaceTransactionPlan plan)
    {
        MethodInfo method = typeof(TranslationWorkspaceTransaction).GetMethod(
            "CommitForTesting", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The transaction interruption test hook is unavailable.");
        try
        {
            method.Invoke(null, [plan, 1]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            // The retained journal is the behavior under test.
        }
    }

    private static Dictionary<string, byte[]> ReadFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    private static string Diagnostics(WorkspaceSnapshot snapshot) =>
        string.Join(Environment.NewLine, snapshot.Diagnostics.Select(static diagnostic =>
            $"{diagnostic.Path}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Id} {diagnostic.Message}"));

    private static async Task<EditorExternalChanges> WaitForExternalChangesAsync(EditorSession session)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            EditorExternalChanges changes = await session.CheckExternalChangesAsync().ConfigureAwait(false);
            if (changes.Paths.Count > 0 || changes.Overflowed) return changes;
            await Task.Delay(25).ConfigureAwait(false);
        }
        return new EditorExternalChanges(false, [], []);
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
