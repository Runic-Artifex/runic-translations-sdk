using System.Text.Json.Nodes;
using RunicTextResources.Authoring;

namespace RunicTextResources.Editor;

internal static class EditorSmokeTest
{
    public static async Task<int> RunAsync(string workspacePath)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"runic-editor-smoke-{Guid.NewGuid():N}");
        CopyDirectory(workspacePath, temporaryRoot);
        try
        {
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

            ValidationResult invalid = await workspace.ValidateAsync(document.Path, document.Content + ",").ConfigureAwait(false);
            Require(!invalid.Success, "Invalid JSON unexpectedly validated.");

            string edited = document.Content.TrimEnd() + Environment.NewLine + Environment.NewLine;
            EditorOperationResult saved = await workspace.SaveAsync(document.Path, edited, document.Revision).ConfigureAwait(false);
            Require(saved.Ok && saved.Snapshot is not null, saved.Message ?? "The valid document was not saved.");

            EditorOperationResult conflict = await workspace.SaveAsync(document.Path, edited + Environment.NewLine, document.Revision).ConfigureAwait(false);
            Require(!conflict.Ok && conflict.Kind == "conflict", "An obsolete revision overwrote a newer document.");

            string secondProjectPath = Path.Combine(temporaryRoot, "SecondCatalog");
            TextResourceProjectWriter.Create(TextResourceProjectScaffolder.Render(
                new TextResourceProjectCreationRequest(
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
                EditorOperationResult mutated = await multiCatalogSession.ApplyMutationAsync(createMessage).ConfigureAwait(false);
                Require(mutated.Ok && mutated.Snapshot?.Success == true, mutated.Message ?? "The key creation transaction failed.");
                WorkspaceSnapshot mutatedSnapshot = mutated.Snapshot
                    ?? throw new InvalidOperationException("Key creation returned no workspace snapshot.");
                Require(mutatedSnapshot.Documents.Count(document => document.Content.Contains("\"Added\"", StringComparison.Ordinal)) == 3,
                    "The new key was not committed across all locale documents.");

                var deleteMessage = new EditorMutationRequest(
                    "delete-key", null, null, null, null, null, "Integration.Added", null, null);
                EditorOperationResult deletedMessage = await multiCatalogSession.ApplyMutationAsync(deleteMessage).ConfigureAwait(false);
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

            Console.WriteLine($"PASS: editor loaded {catalog.Locales.Count} locales, selected one of multiple catalogs, repaired malformed JSON, previewed and committed structural key transactions, handled a single-locale catalog, created a compiler-valid project, validated drafts, saved atomically, and rejected a stale write.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
