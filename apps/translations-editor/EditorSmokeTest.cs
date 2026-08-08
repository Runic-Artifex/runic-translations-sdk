using System.Text.Json.Nodes;

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

            EditorDocument document = snapshot.Documents.First(static candidate => !candidate.IsManifest);
            ValidationResult unchanged = await workspace.ValidateAsync(document.Path, document.Content).ConfigureAwait(false);
            Require(unchanged.Success, "An unchanged resource document did not validate.");

            ValidationResult invalid = await workspace.ValidateAsync(document.Path, document.Content + ",").ConfigureAwait(false);
            Require(!invalid.Success, "Invalid JSON unexpectedly validated.");

            string edited = document.Content.TrimEnd() + Environment.NewLine + Environment.NewLine;
            EditorOperationResult saved = await workspace.SaveAsync(document.Path, edited, document.Revision).ConfigureAwait(false);
            Require(saved.Ok && saved.Snapshot is not null, saved.Message ?? "The valid document was not saved.");

            EditorOperationResult conflict = await workspace.SaveAsync(document.Path, edited + Environment.NewLine, document.Revision).ConfigureAwait(false);
            Require(!conflict.Ok && conflict.Kind == "conflict", "An obsolete revision overwrote a newer document.");

            File.Delete(Path.Combine(temporaryRoot, "product.en.json"));
            File.Delete(Path.Combine(temporaryRoot, "product.fr.json"));
            string manifestPath = Path.Combine(temporaryRoot, "product.catalog.json");
            JsonObject manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false))!.AsObject();
            manifest["locales"] = new JsonArray(new JsonObject { ["tag"] = "de" });
            await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(new() { WriteIndented = true }) + Environment.NewLine).ConfigureAwait(false);
            WorkspaceSnapshot singleLocale = await workspace.LoadAsync().ConfigureAwait(false);
            Require(singleLocale.Success, Diagnostics(singleLocale));
            Require(singleLocale.Catalog?.Locales.Count == 1, "A single-locale catalog was not represented correctly.");

            Console.WriteLine($"PASS: editor loaded {catalog.Locales.Count} locales and a single-locale catalog, validated drafts, saved atomically, and rejected a stale write.");
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

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
