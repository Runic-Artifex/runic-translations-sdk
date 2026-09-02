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

            using var workspace = new EditorWorkspace(project);
            WorkspaceSnapshot initial = await workspace.LoadAsync().ConfigureAwait(false);
            Require(initial.Success && initial.Catalog?.Locales.Count == 2, "The MF2 project was not loaded.");
            EditorDocument german = initial.Documents.Single(document => document.Path == "de/application_title.mf2");

            ValidationResult validation = await workspace.ValidateAsync(german.Path, "Titel\n").ConfigureAwait(false);
            Require(validation.Success, "The MF2 draft did not validate.");
            EditorMessagePreview preview = await workspace.PreviewMessageAsync(
                german.Path, "Titel\n", "de", "application_title").ConfigureAwait(false);
            Require(preview.Success && preview.AstJson is not null, "The MF2 message preview was not produced.");

            EditorOperationResult saved = await workspace.SaveAsync(german.Path, "Titel\n", german.Revision).ConfigureAwait(false);
            Require(saved.Ok, saved.Message ?? "The MF2 message was not saved.");

            TranslationWorkspaceTransactionPlan addMessage = TranslationWorkspaceMutation.CreateKey(
                new TranslationCreateKeyRequest(project, "editor-smoke", "validation_required", "Required"));
            TranslationWorkspaceTransaction.Commit(addMessage);
            WorkspaceSnapshot mutated = await workspace.LoadAsync().ConfigureAwait(false);
            Require(mutated.Success && mutated.Documents.Any(document => document.Path == "de/validation_required.mf2"),
                "The transactional MF2 message creation was not visible to the editor.");

            const string missingPath = "de/only_source.mf2";
            await File.WriteAllTextAsync(Path.Combine(project, "en", "only_source.mf2"), "Source\n").ConfigureAwait(false);
            WorkspaceSnapshot incomplete = await workspace.LoadAsync().ConfigureAwait(false);
            Require(!incomplete.Success, "An incomplete locale was accepted.");
            ValidationResult missingValidation = await workspace.ValidateAsync(missingPath, "Ziel\n").ConfigureAwait(false);
            Require(missingValidation.Success, "A missing MF2 locale cell could not be validated as a new file.");
            EditorOperationResult created = await workspace.SaveAsync(
                missingPath, "Ziel\n", EditorWorkspace.NewMf2DocumentRevision).ConfigureAwait(false);
            Require(created.Ok && File.Exists(Path.Combine(project, "de", "only_source.mf2")),
                created.Message ?? "A missing MF2 locale file was not created.");

            EditorXliffExportResult exported = await workspace.ExportXliffAsync("interchange").ConfigureAwait(false);
            Require(exported.Ok && exported.Documents.Count == 1, exported.Message ?? "The MF2 project was not exported to XLIFF.");
            string xliffPath = Path.Combine(project, exported.Documents[0].Path);
            XDocument xliff = XDocument.Load(xliffPath);
            XNamespace xliffNamespace = "urn:oasis:names:tc:xliff:document:2.0";
            XElement target = xliff.Descendants(xliffNamespace + "unit")
                .Single(unit => unit.Attribute("id")?.Value == "application_title")
                .Descendants(xliffNamespace + "target")
                .Single();
            target.Value = "Importierter Titel";
            xliff.Save(xliffPath);
            (EditorXliffImportPlan importPlan, PreparedInterchangeImport? prepared) =
                await workspace.PreviewXliffImportAsync(xliffPath).ConfigureAwait(false);
            Require(importPlan.Ok && prepared is not null, importPlan.Message ?? "The XLIFF import was not prepared for MF2.");
            EditorOperationResult imported = await workspace.CommitXliffImportAsync(prepared!).ConfigureAwait(false);
            Require(imported.Ok, imported.Message ?? "The XLIFF import was not committed to MF2.");
            Require(await File.ReadAllTextAsync(Path.Combine(project, "de", "application_title.mf2")).ConfigureAwait(false) == "Importierter Titel\n",
                "The XLIFF import did not update the conventional MF2 message file.");
            Require(!Directory.EnumerateFiles(project, "*.json", SearchOption.AllDirectories)
                .Any(path => !string.Equals(Path.GetFileName(path), "runic.json", StringComparison.Ordinal) &&
                    !Normalize(path).Contains("/.runic-translations/", StringComparison.Ordinal)),
                "The XLIFF import created a legacy JSON translation document.");

            using var reloadedWorkspace = new EditorWorkspace(project);
            WorkspaceSnapshot complete = await reloadedWorkspace.LoadAsync().ConfigureAwait(false);
            Require(complete.Success,
                "The completed MF2 project did not compile: " +
                string.Join(" | ", complete.Diagnostics.Select(diagnostic => diagnostic.Id + " " + diagnostic.Message)));

            Console.WriteLine("PASS: editor created, loaded, validated, previewed, mutated, imported XLIFF, and atomically saved an MF2-only project.");
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

    private static string Normalize(string path) => path.Replace('\\', '/');
}
