using System.Text.Json;
using CsWebUi;

namespace RunicTranslations.Editor;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (Array.Exists(args, static argument => argument is "--help" or "-h") ||
            args is ["help", ..])
        {
            PrintHelp();
            return 0;
        }

        if (Array.Exists(args, static argument => argument == "--version"))
        {
            EditorAbout about = EditorDiagnostics.About();
            Console.WriteLine($"{about.Product} {about.Version}");
            Console.WriteLine($"Channel: {about.UpdateChannel}");
            Console.WriteLine($"Commit: {about.Commit ?? "development"}");
            Console.WriteLine($"Runtime: {about.RuntimeIdentifier}");
            return 0;
        }

        string? command = args.FirstOrDefault() is string first &&
            (string.Equals(first, "edit", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(first, "validate", StringComparison.OrdinalIgnoreCase))
            ? first.ToLowerInvariant()
            : null;
        bool validate = Array.Exists(args, static argument => argument == "--validate") ||
            string.Equals(command, "validate", StringComparison.OrdinalIgnoreCase);
        bool edit = command is null || string.Equals(command, "edit", StringComparison.OrdinalIgnoreCase);
        if (!validate && !edit && !Array.Exists(args, static argument => argument == "--smoke-test"))
        {
            Console.Error.WriteLine($"Unknown command '{command}'. Run with --help for usage.");
            return 2;
        }

        string workspacePath = ArgumentValue(args, "--workspace")
            ?? PositionalWorkspace(args, command)
            ?? (validate || command == "edit"
                ? Environment.CurrentDirectory
                : Path.Combine(AppContext.BaseDirectory, "ExampleWorkspace"));
        string? catalogId = ArgumentValue(args, "--catalog");

        if (Array.Exists(args, static argument => argument == "--smoke-test"))
            return await EditorSmokeTest.RunAsync(workspacePath).ConfigureAwait(false);

        if (validate)
            return await ValidateWorkspaceAsync(workspacePath, catalogId).ConfigureAwait(false);

        string webRoot = Path.Combine(AppContext.BaseDirectory, "www");
        if (!Directory.Exists(webRoot))
            throw new DirectoryNotFoundException($"The SvelteKit build was not copied to '{webRoot}'.");

        using var session = new EditorSession(workspacePath, catalogId);
        WebUiApplication.SetConnectionTimeout(15);
        WebUiApplication.SetLogger(static (level, message) => Console.WriteLine($"[WebUI:{level}] {message}"));
        WebUiApplication.UnhandledCallbackException += static (_, eventArgs) =>
            Console.Error.WriteLine($"[Editor callback] {eventArgs.Exception}");

        using (var window = new WebUiWindow())
        {
            window.SetSize(1440, 900);
            window.SetMinimumSize(980, 680);
            window.SetResizable(true);
            window.SetHighContrast(WebUiApplication.IsHighContrast);
            window.Center();
            window.SetRootFolder(webRoot);

            window.BindAsync("runicEditorLoad", async (_, cancellationToken) =>
                WebUiResult.FromString(Serialize(await session.LoadAsync(cancellationToken).ConfigureAwait(false))));

            window.BindAsync("runicEditorCheckExternalChanges", async (_, cancellationToken) =>
                WebUiResult.FromString(Serialize(await session.CheckExternalChangesAsync(cancellationToken).ConfigureAwait(false))));

            window.BindAsync("runicEditorPickWorkspace", async (_, cancellationToken) =>
                WebUiResult.FromString(Serialize(await EditorWorkspacePicker.PickAsync(cancellationToken).ConfigureAwait(false))));

            window.Bind("runicEditorPreviewMutation", webUiEvent =>
                WebUiResult.FromString(Serialize(session.PreviewMutation(DeserializeMutationRequest(webUiEvent.GetString())))));

            window.BindAsync("runicEditorApplyMutation", async (webUiEvent, cancellationToken) =>
                WebUiResult.FromString(Serialize(await session.ApplyMutationAsync(
                    DeserializeMutationRequest(webUiEvent.GetString()), cancellationToken).ConfigureAwait(false))));

            window.BindAsync("runicEditorRecoverTransaction", async (webUiEvent, cancellationToken) =>
                WebUiResult.FromString(Serialize(await session.RecoverTransactionAsync(
                    DeserializeRecoveryRequest(webUiEvent.GetString()), cancellationToken).ConfigureAwait(false))));

            window.BindAsync("runicEditorValidate", async (webUiEvent, cancellationToken) =>
            {
                ValidationResult result = await session.ValidateAsync(
                    webUiEvent.GetString(),
                    webUiEvent.GetString(1),
                    cancellationToken).ConfigureAwait(false);
                return WebUiResult.FromString(Serialize(result));
            });

            window.BindAsync("runicEditorPreviewMessage", async (webUiEvent, cancellationToken) =>
            {
                EditorMessagePreview result = await session.PreviewMessageAsync(
                    webUiEvent.GetString(),
                    webUiEvent.GetString(1),
                    webUiEvent.GetString(2),
                    webUiEvent.GetString(3),
                    cancellationToken).ConfigureAwait(false);
                return WebUiResult.FromString(Serialize(result));
            });

            window.BindAsync("runicEditorSave", async (webUiEvent, cancellationToken) =>
            {
                EditorOperationResult result = await session.SaveAsync(
                    webUiEvent.GetString(),
                    webUiEvent.GetString(1),
                    webUiEvent.GetString(2),
                    cancellationToken).ConfigureAwait(false);
                return WebUiResult.FromString(Serialize(result));
            });

            window.BindAsync("runicEditorSaveReview", async (webUiEvent, cancellationToken) =>
            {
                EditorReviewSaveRequest request = DeserializeReviewRequest(webUiEvent.GetString());
                return WebUiResult.FromString(Serialize(
                    await session.SaveReviewAsync(request, cancellationToken).ConfigureAwait(false)));
            });

            window.Bind("runicEditorAbout", _ =>
                WebUiResult.FromString(Serialize(EditorDiagnostics.About())));

            window.BindAsync("runicEditorCreateDiagnosticBundle", async (_, cancellationToken) =>
                WebUiResult.FromString(Serialize(
                    await session.CreateDiagnosticBundleAsync(cancellationToken).ConfigureAwait(false))));

            window.Bind("runicEditorPreviewProject", webUiEvent =>
            {
                EditorProjectCreationRequest request = DeserializeProjectRequest(webUiEvent.GetString());
                return WebUiResult.FromString(Serialize(EditorSession.PreviewProject(request)));
            });

            window.BindAsync("runicEditorCreateProject", async (webUiEvent, cancellationToken) =>
            {
                EditorProjectCreationRequest request = DeserializeProjectRequest(webUiEvent.GetString());
                EditorOperationResult result = await session.CreateProjectAsync(request, cancellationToken).ConfigureAwait(false);
                return WebUiResult.FromString(Serialize(result));
            });

            window.BindAsync("runicEditorOpenWorkspace", async (webUiEvent, cancellationToken) =>
            {
                EditorOpenWorkspaceRequest request = DeserializeOpenRequest(webUiEvent.GetString());
                EditorOperationResult result = await session.OpenWorkspaceAsync(request, cancellationToken).ConfigureAwait(false);
                return WebUiResult.FromString(Serialize(result));
            });

            if (Array.Exists(args, static argument => argument == "--webview"))
                window.ShowWebView("index.html");
            else
                window.Show("index.html");

            Console.WriteLine($"Runic Translations Editor is serving '{Path.GetFullPath(workspacePath)}' at {window.Url}");
            WebUiApplication.Wait();
        }

        WebUiApplication.Clean();
        return 0;
    }

    private static string Serialize(WorkspaceSnapshot value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.WorkspaceSnapshot);

    private static string Serialize(ValidationResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.ValidationResult);

    private static string Serialize(EditorMessagePreview value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorMessagePreview);

    private static string Serialize(EditorReviewOperationResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorReviewOperationResult);

    private static string Serialize(EditorAbout value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorAbout);

    private static string Serialize(EditorDiagnosticBundleResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorDiagnosticBundleResult);

    private static string Serialize(EditorOperationResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorOperationResult);

    private static string Serialize(EditorProjectPlan value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorProjectPlan);

    private static string Serialize(EditorExternalChanges value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorExternalChanges);

    private static string Serialize(EditorWorkspacePickerResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorWorkspacePickerResult);

    private static string Serialize(EditorMutationPreview value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorMutationPreview);

    private static EditorProjectCreationRequest DeserializeProjectRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorProjectCreationRequest)
        ?? throw new ArgumentException("The project creation request is required.", nameof(value));

    private static EditorOpenWorkspaceRequest DeserializeOpenRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorOpenWorkspaceRequest)
        ?? throw new ArgumentException("The open-workspace request is required.", nameof(value));

    private static EditorMutationRequest DeserializeMutationRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorMutationRequest)
        ?? throw new ArgumentException("The mutation request is required.", nameof(value));

    private static EditorRecoveryRequest DeserializeRecoveryRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorRecoveryRequest)
        ?? throw new ArgumentException("The recovery request is required.", nameof(value));

    private static EditorReviewSaveRequest DeserializeReviewRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorReviewSaveRequest)
        ?? throw new ArgumentException("The review save request is required.", nameof(value));

    private static string? ArgumentValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a value.", nameof(args));
        return args[index + 1];
    }

    private static string? PositionalWorkspace(string[] args, string? command)
    {
        int start = command is "edit" or "validate" ? 1 : 0;
        for (int index = start; index < args.Length; index++)
        {
            if (args[index] is "--workspace" or "--catalog")
            {
                index++;
                continue;
            }
            if (!args[index].StartsWith('-')) return args[index];
        }
        return null;
    }

    private static async Task<int> ValidateWorkspaceAsync(string workspacePath, string? catalogId)
    {
        try
        {
            using var workspace = new EditorWorkspace(workspacePath, catalogId);
            WorkspaceSnapshot snapshot = await workspace.LoadAsync().ConfigureAwait(false);
            foreach (EditorDiagnostic diagnostic in snapshot.Diagnostics)
            {
                string path = string.IsNullOrWhiteSpace(diagnostic.Path) ? "workspace" : diagnostic.Path;
                Console.WriteLine($"{path}({diagnostic.Line},{diagnostic.Column}): {diagnostic.Severity} {diagnostic.Id}: {diagnostic.Message}");
            }

            if (snapshot.Catalog is null && snapshot.Catalogs.Count > 1)
            {
                Console.Error.WriteLine("The workspace contains multiple catalogs. Select one with --catalog <id>:");
                foreach (EditorCatalogSummary catalog in snapshot.Catalogs.OrderBy(static value => value.Id, StringComparer.Ordinal))
                    Console.Error.WriteLine($"  {catalog.Id}");
                return 1;
            }

            if (!snapshot.Success)
            {
                Console.Error.WriteLine($"Validation failed with {snapshot.Diagnostics.Count} diagnostic(s).");
                return 1;
            }

            if (snapshot.Catalog is null)
            {
                Console.Error.WriteLine("Validation found no catalog in the workspace.");
                return 1;
            }

            string catalogName = snapshot.Catalog.Id;
            Console.WriteLine($"Validation passed for '{catalogName}' ({snapshot.Documents.Count} document(s)).");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Validation could not start: {exception.Message}");
            return 2;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Runic Translations Editor");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  runic-translations-editor edit [workspace] [--catalog <id>] [--webview]");
        Console.WriteLine("  runic-translations-editor validate [workspace] [--catalog <id>]");
        Console.WriteLine("  runic-translations-editor --version");
        Console.WriteLine();
        Console.WriteLine("The packaged launcher opens the current directory when no workspace is given.");
        Console.WriteLine("Validation uses the same compiler path and diagnostics as editor load and save.");
    }
}
