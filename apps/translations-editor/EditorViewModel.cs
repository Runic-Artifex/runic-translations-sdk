using System.Text.Json;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Runic.Translations.Editor;

/// <summary>The application Window's editor commands, owned by its DI scope.</summary>
public sealed class EditorViewModel : ReactiveObject, IDisposable
{
    private readonly EditorSession _session;
    private readonly IDisposable _commandErrors;
    private string _resultJson = "null";

    internal EditorViewModel(EditorSession session)
    {
        _session = session;
        ExecuteCommand = ReactiveCommand.CreateFromTask<string>(ExecuteAsync);
        // The Bridge awaits and returns command failures to its caller. ReactiveUI
        // also requires an observer on ThrownExceptions for handled failures.
        _commandErrors = ExecuteCommand.ThrownExceptions.Subscribe(_ => { });
    }

    public ReactiveCommand<string, RxVoid> ExecuteCommand { get; }

    public string ResultJson
    {
        get => _resultJson;
        private set => this.RaiseAndSetIfChanged(ref _resultJson, value);
    }

    private async Task ExecuteAsync(string request)
    {
        using JsonDocument document = JsonDocument.Parse(request);
        JsonElement root = document.RootElement;
        string requestId = root.GetProperty("requestId").GetString()
            ?? throw new ArgumentException("A request ID is required.");
        string operation = root.GetProperty("operation").GetString()
            ?? throw new ArgumentException("An editor operation is required.");
        JsonElement argument = root.GetProperty("argument");
        object result = await ExecuteOperationAsync(operation, argument).ConfigureAwait(false);
        ResultJson = "{\"requestId\":" + JsonSerializer.Serialize(requestId, EditorJsonContext.Default.String)
            + ",\"result\":" + JsonSerializer.Serialize(result, result.GetType(), EditorJsonContext.Default) + "}";
    }

    private async Task<object> ExecuteOperationAsync(string operation, JsonElement argument)
    {
        static string Required(JsonElement value, string property) =>
            value.GetProperty(property).GetString() ?? throw new ArgumentException($"{property} is required.");
        static string? Optional(JsonElement value, string property) =>
            value.TryGetProperty(property, out JsonElement item) && item.ValueKind != JsonValueKind.Null
                ? item.GetString() : null;
        static T Parse<T>(JsonElement value) =>
            value.Deserialize<T>(EditorJsonContext.Default.Options)
                ?? throw new ArgumentException("The editor request is invalid.");

        return operation switch
        {
            "LoadWorkspace" => await _session.LoadAsync(),
            "CheckExternalChanges" => await _session.CheckExternalChangesAsync(),
            "PickWorkspace" => await EditorWorkspacePicker.PickAsync(CancellationToken.None),
            "PreviewMutation" => _session.PreviewMutation(Parse<EditorMutationRequest>(argument)),
            "ApplyMutation" => await _session.ApplyMutationAsync(Parse<EditorMutationRequest>(argument)),
            "RecoverTransaction" => await _session.RecoverTransactionAsync(new EditorRecoveryRequest(Required(argument, "mode"))),
            "Undo" => await _session.UndoAsync(),
            "Redo" => await _session.RedoAsync(),
            "ValidateDocument" => await _session.ValidateAsync(Required(argument, "path"), Required(argument, "content")),
            "TransformDocument" => await _session.TransformDocumentAsync(Required(argument, "path"), Required(argument, "content"), Optional(argument, "key"), Optional(argument, "value")),
            "PreviewMessage" => await _session.PreviewMessageAsync(Required(argument, "path"), Required(argument, "content"), Required(argument, "locale"), Required(argument, "key"), Optional(argument, "samplesJson")),
            "SaveDocument" => await _session.SaveAsync(Required(argument, "path"), Required(argument, "content"), Required(argument, "revision")),
            "SaveReview" => await _session.SaveReviewAsync(Parse<EditorReviewSaveRequest>(argument)),
            "About" => EditorDiagnostics.About(),
            "CreateDiagnosticBundle" => await _session.CreateDiagnosticBundleAsync(),
            "RevealDiagnosticBundle" => _session.RevealDiagnosticBundle(Required(argument, "path")),
            "DeleteDiagnosticBundle" => _session.DeleteDiagnosticBundle(Required(argument, "path")),
            "LoadLocalState" => _session.LoadLocalState(),
            "SaveLocalState" => _session.SaveLocalState(Parse<EditorLocalStateEntry[]>(argument)),
            "ClearLocalState" => _session.ClearLocalState(),
            "PreviewProject" => EditorSession.PreviewProject(Parse<EditorProjectCreationRequest>(argument)),
            "CreateProject" => await _session.CreateProjectAsync(Parse<EditorProjectCreationRequest>(argument)),
            "OpenWorkspace" => await _session.OpenWorkspaceAsync(Parse<EditorOpenWorkspaceRequest>(argument)),
            "ExportXliff" => await _session.ExportXliffAsync(Optional(argument, "directory")),
            "PreviewXliffImport" => await _session.PreviewXliffImportAsync(Required(argument, "path")),
            "ApplyXliffImport" => await _session.ApplyXliffImportAsync(Required(argument, "confirmationToken")),
            "ExportReviewJson" => await _session.ExportReviewJsonAsync(Optional(argument, "path")),
            "PreviewReviewJsonImport" => await _session.PreviewReviewJsonImportAsync(Required(argument, "path")),
            "ApplyReviewJsonImport" => await _session.ApplyReviewJsonImportAsync(Required(argument, "confirmationToken")),
            _ => throw new ArgumentException($"Unknown editor operation: {operation}.")
        };
    }

    public void Dispose()
    {
        ExecuteCommand.Dispose();
        _commandErrors.Dispose();
        _session.Dispose();
    }
}
