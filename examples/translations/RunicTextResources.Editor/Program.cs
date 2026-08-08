using System.Text.Json;
using CsWebUi;

namespace RunicTextResources.Editor;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string workspacePath = ArgumentValue(args, "--workspace")
            ?? Path.Combine(AppContext.BaseDirectory, "ExampleWorkspace");

        if (Array.Exists(args, static argument => argument == "--smoke-test"))
            return await EditorSmokeTest.RunAsync(workspacePath).ConfigureAwait(false);

        string webRoot = Path.Combine(AppContext.BaseDirectory, "www");
        if (!Directory.Exists(webRoot))
            throw new DirectoryNotFoundException($"The SvelteKit build was not copied to '{webRoot}'.");

        using var session = new EditorSession(workspacePath);
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

            window.BindAsync("runicEditorValidate", async (webUiEvent, cancellationToken) =>
            {
                ValidationResult result = await session.ValidateAsync(
                    webUiEvent.GetString(),
                    webUiEvent.GetString(1),
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

    private static string Serialize(EditorOperationResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorOperationResult);

    private static string Serialize(EditorProjectPlan value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorProjectPlan);

    private static string Serialize(EditorExternalChanges value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorExternalChanges);

    private static string Serialize(EditorWorkspacePickerResult value) =>
        JsonSerializer.Serialize(value, EditorJsonContext.Default.EditorWorkspacePickerResult);

    private static EditorProjectCreationRequest DeserializeProjectRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorProjectCreationRequest)
        ?? throw new ArgumentException("The project creation request is required.", nameof(value));

    private static EditorOpenWorkspaceRequest DeserializeOpenRequest(string value) =>
        JsonSerializer.Deserialize(value, EditorJsonContext.Default.EditorOpenWorkspaceRequest)
        ?? throw new ArgumentException("The open-workspace request is required.", nameof(value));

    private static string? ArgumentValue(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        if (index < 0) return null;
        if (index + 1 == args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{name} requires a directory path.", nameof(args));
        return args[index + 1];
    }
}
