using System.Text.Json;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace Runic.Translations.Editor;

/// <summary>Shared request correlation and lifetime handling for routed editor features.</summary>
public abstract class EditorFeatureViewModel : ReactiveObject, IDisposable
{
    private readonly EditorViewModel _owner;
    private readonly List<ReactiveCommand<string, RxVoid>> _commands = [];
    private readonly List<IDisposable> _errors = [];

    internal EditorFeatureViewModel(EditorSession session, EditorViewModel owner)
    {
        Session = session;
        _owner = owner;
    }

    private protected EditorSession Session { get; }

    protected ReactiveCommand<string, RxVoid> CreateCommand<TResult>(
        Func<JsonElement, Task<TResult>> operation, Action<TResult, string> publish)
        where TResult : notnull
    {
        var command = ReactiveCommand.CreateFromTask<string>(async request =>
        {
            using var parsed = JsonDocument.Parse(request);
            JsonElement root = parsed.RootElement;
            string requestId = Required(root, "requestId");
            TResult result = await operation(root.GetProperty("argument")).ConfigureAwait(false);
            if (result is WorkspaceSnapshot snapshot) _owner.SyncDocuments(snapshot);
            else if (result is EditorOperationResult { Snapshot: { } changed }) _owner.SyncDocuments(changed);
            string json = JsonSerializer.Serialize(result, result.GetType(), EditorJsonContext.Default);
            publish(result, "{\"requestId\":" + JsonSerializer.Serialize(requestId, EditorJsonContext.Default.String)
                + ",\"result\":" + json + "}");
        });
        _commands.Add(command);
        _errors.Add(command.ThrownExceptions.Subscribe(_ => { }));
        return command;
    }

    protected static string Required(JsonElement value, string property) =>
        value.GetProperty(property).GetString() ?? throw new ArgumentException($"{property} is required.");

    protected static string? Optional(JsonElement value, string property) =>
        value.TryGetProperty(property, out JsonElement item) && item.ValueKind != JsonValueKind.Null
            ? item.GetString() : null;

    protected static T Parse<T>(JsonElement value) =>
        value.Deserialize<T>(EditorJsonContext.Default.Options)
            ?? throw new ArgumentException("The editor request is invalid.");

    public void Dispose()
    {
        foreach (var command in _commands) command.Dispose();
        foreach (var errors in _errors) errors.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>Workspace operations exposed as individual generated View commands.</summary>
public sealed class EditorWorkspaceViewModel : EditorFeatureViewModel
{
    private string _loadResultJson = "null";
    private WorkspaceSnapshot? _lastLoad;
    private string _checkExternalChangesResultJson = "null";
    private EditorExternalChanges? _lastCheckExternalChanges;
    private string _pickWorkspaceResultJson = "null";
    private EditorWorkspacePickerResult? _lastPickWorkspace;
    private string _previewMutationResultJson = "null";
    private EditorMutationPreview? _lastPreviewMutation;
    private string _applyMutationResultJson = "null";
    private EditorOperationResult? _lastApplyMutation;
    private string _recoverTransactionResultJson = "null";
    private EditorOperationResult? _lastRecoverTransaction;
    private string _undoResultJson = "null";
    private EditorOperationResult? _lastUndo;
    private string _redoResultJson = "null";
    private EditorOperationResult? _lastRedo;

    internal EditorWorkspaceViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        LoadCommand = CreateCommand(async argument => await Session.LoadAsync(), (result, value) => { LastLoad = result; LoadResultJson = value; });
        CheckExternalChangesCommand = CreateCommand(async argument => await Session.CheckExternalChangesAsync(), (result, value) => { LastCheckExternalChanges = result; CheckExternalChangesResultJson = value; });
        PickWorkspaceCommand = CreateCommand(async argument => await EditorWorkspacePicker.PickAsync(CancellationToken.None), (result, value) => { LastPickWorkspace = result; PickWorkspaceResultJson = value; });
        PreviewMutationCommand = CreateCommand(argument => Task.FromResult(Session.PreviewMutation(Parse<EditorMutationRequest>(argument))), (result, value) => { LastPreviewMutation = result; PreviewMutationResultJson = value; });
        ApplyMutationCommand = CreateCommand(async argument => await Session.ApplyMutationAsync(Parse<EditorMutationRequest>(argument)), (result, value) => { LastApplyMutation = result; ApplyMutationResultJson = value; });
        RecoverTransactionCommand = CreateCommand(async argument => await Session.RecoverTransactionAsync(new EditorRecoveryRequest(Required(argument, "mode"))), (result, value) => { LastRecoverTransaction = result; RecoverTransactionResultJson = value; });
        UndoCommand = CreateCommand(async argument => await Session.UndoAsync(), (result, value) => { LastUndo = result; UndoResultJson = value; });
        RedoCommand = CreateCommand(async argument => await Session.RedoAsync(), (result, value) => { LastRedo = result; RedoResultJson = value; });
    }

    public string LoadResultJson { get => _loadResultJson; private set => this.RaiseAndSetIfChanged(ref _loadResultJson, value); }
    internal WorkspaceSnapshot? LastLoad { get => _lastLoad; private set => this.RaiseAndSetIfChanged(ref _lastLoad, value); }
    public string CheckExternalChangesResultJson { get => _checkExternalChangesResultJson; private set => this.RaiseAndSetIfChanged(ref _checkExternalChangesResultJson, value); }
    internal EditorExternalChanges? LastCheckExternalChanges { get => _lastCheckExternalChanges; private set => this.RaiseAndSetIfChanged(ref _lastCheckExternalChanges, value); }
    public string PickWorkspaceResultJson { get => _pickWorkspaceResultJson; private set => this.RaiseAndSetIfChanged(ref _pickWorkspaceResultJson, value); }
    internal EditorWorkspacePickerResult? LastPickWorkspace { get => _lastPickWorkspace; private set => this.RaiseAndSetIfChanged(ref _lastPickWorkspace, value); }
    public string PreviewMutationResultJson { get => _previewMutationResultJson; private set => this.RaiseAndSetIfChanged(ref _previewMutationResultJson, value); }
    internal EditorMutationPreview? LastPreviewMutation { get => _lastPreviewMutation; private set => this.RaiseAndSetIfChanged(ref _lastPreviewMutation, value); }
    public string ApplyMutationResultJson { get => _applyMutationResultJson; private set => this.RaiseAndSetIfChanged(ref _applyMutationResultJson, value); }
    internal EditorOperationResult? LastApplyMutation { get => _lastApplyMutation; private set => this.RaiseAndSetIfChanged(ref _lastApplyMutation, value); }
    public string RecoverTransactionResultJson { get => _recoverTransactionResultJson; private set => this.RaiseAndSetIfChanged(ref _recoverTransactionResultJson, value); }
    internal EditorOperationResult? LastRecoverTransaction { get => _lastRecoverTransaction; private set => this.RaiseAndSetIfChanged(ref _lastRecoverTransaction, value); }
    public string UndoResultJson { get => _undoResultJson; private set => this.RaiseAndSetIfChanged(ref _undoResultJson, value); }
    internal EditorOperationResult? LastUndo { get => _lastUndo; private set => this.RaiseAndSetIfChanged(ref _lastUndo, value); }
    public string RedoResultJson { get => _redoResultJson; private set => this.RaiseAndSetIfChanged(ref _redoResultJson, value); }
    internal EditorOperationResult? LastRedo { get => _lastRedo; private set => this.RaiseAndSetIfChanged(ref _lastRedo, value); }

    public ReactiveCommand<string, RxVoid> LoadCommand { get; }
    public ReactiveCommand<string, RxVoid> CheckExternalChangesCommand { get; }
    public ReactiveCommand<string, RxVoid> PickWorkspaceCommand { get; }
    public ReactiveCommand<string, RxVoid> PreviewMutationCommand { get; }
    public ReactiveCommand<string, RxVoid> ApplyMutationCommand { get; }
    public ReactiveCommand<string, RxVoid> RecoverTransactionCommand { get; }
    public ReactiveCommand<string, RxVoid> UndoCommand { get; }
    public ReactiveCommand<string, RxVoid> RedoCommand { get; }
}

/// <summary>DocumentTools operations exposed as individual generated View commands.</summary>
public sealed class EditorDocumentToolsViewModel : EditorFeatureViewModel
{
    private string _transformDocumentResultJson = "null";
    private EditorDocumentDraft? _lastTransformDocument;
    private string _previewMessageResultJson = "null";
    private EditorMessagePreview? _lastPreviewMessage;

    internal EditorDocumentToolsViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        TransformDocumentCommand = CreateCommand(async argument => await Session.TransformDocumentAsync(Required(argument, "path"), Required(argument, "content"), Optional(argument, "key"), Optional(argument, "value")), (result, value) => { LastTransformDocument = result; TransformDocumentResultJson = value; });
        PreviewMessageCommand = CreateCommand(async argument => await Session.PreviewMessageAsync(Required(argument, "path"), Required(argument, "content"), Required(argument, "locale"), Required(argument, "key"), Optional(argument, "samplesJson")), (result, value) => { LastPreviewMessage = result; PreviewMessageResultJson = value; });
    }

    public string TransformDocumentResultJson { get => _transformDocumentResultJson; private set => this.RaiseAndSetIfChanged(ref _transformDocumentResultJson, value); }
    internal EditorDocumentDraft? LastTransformDocument { get => _lastTransformDocument; private set => this.RaiseAndSetIfChanged(ref _lastTransformDocument, value); }
    public string PreviewMessageResultJson { get => _previewMessageResultJson; private set => this.RaiseAndSetIfChanged(ref _previewMessageResultJson, value); }
    internal EditorMessagePreview? LastPreviewMessage { get => _lastPreviewMessage; private set => this.RaiseAndSetIfChanged(ref _lastPreviewMessage, value); }

    public ReactiveCommand<string, RxVoid> TransformDocumentCommand { get; }
    public ReactiveCommand<string, RxVoid> PreviewMessageCommand { get; }
}

/// <summary>Review operations exposed as individual generated View commands.</summary>
public sealed class EditorReviewViewModel : EditorFeatureViewModel
{
    private string _saveReviewResultJson = "null";
    private EditorReviewOperationResult? _lastSaveReview;

    internal EditorReviewViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        SaveReviewCommand = CreateCommand(async argument => await Session.SaveReviewAsync(Parse<EditorReviewSaveRequest>(argument)), (result, value) => { LastSaveReview = result; SaveReviewResultJson = value; });
    }

    public string SaveReviewResultJson { get => _saveReviewResultJson; private set => this.RaiseAndSetIfChanged(ref _saveReviewResultJson, value); }
    internal EditorReviewOperationResult? LastSaveReview { get => _lastSaveReview; private set => this.RaiseAndSetIfChanged(ref _lastSaveReview, value); }

    public ReactiveCommand<string, RxVoid> SaveReviewCommand { get; }
}

/// <summary>Interchange operations exposed as individual generated View commands.</summary>
public sealed class EditorInterchangeViewModel : EditorFeatureViewModel
{
    private string _exportXliffResultJson = "null";
    private EditorXliffExportResult? _lastExportXliff;
    private string _previewXliffImportResultJson = "null";
    private EditorXliffImportPlan? _lastPreviewXliffImport;
    private string _applyXliffImportResultJson = "null";
    private EditorOperationResult? _lastApplyXliffImport;
    private string _exportReviewJsonResultJson = "null";
    private EditorReviewFileResult? _lastExportReviewJson;
    private string _previewReviewJsonImportResultJson = "null";
    private EditorReviewImportPlan? _lastPreviewReviewJsonImport;
    private string _applyReviewJsonImportResultJson = "null";
    private EditorReviewOperationResult? _lastApplyReviewJsonImport;

    internal EditorInterchangeViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        ExportXliffCommand = CreateCommand(async argument => await Session.ExportXliffAsync(Optional(argument, "directory")), (result, value) => { LastExportXliff = result; ExportXliffResultJson = value; });
        PreviewXliffImportCommand = CreateCommand(async argument => await Session.PreviewXliffImportAsync(Required(argument, "path")), (result, value) => { LastPreviewXliffImport = result; PreviewXliffImportResultJson = value; });
        ApplyXliffImportCommand = CreateCommand(async argument => await Session.ApplyXliffImportAsync(Required(argument, "confirmationToken")), (result, value) => { LastApplyXliffImport = result; ApplyXliffImportResultJson = value; });
        ExportReviewJsonCommand = CreateCommand(async argument => await Session.ExportReviewJsonAsync(Optional(argument, "path")), (result, value) => { LastExportReviewJson = result; ExportReviewJsonResultJson = value; });
        PreviewReviewJsonImportCommand = CreateCommand(async argument => await Session.PreviewReviewJsonImportAsync(Required(argument, "path")), (result, value) => { LastPreviewReviewJsonImport = result; PreviewReviewJsonImportResultJson = value; });
        ApplyReviewJsonImportCommand = CreateCommand(async argument => await Session.ApplyReviewJsonImportAsync(Required(argument, "confirmationToken")), (result, value) => { LastApplyReviewJsonImport = result; ApplyReviewJsonImportResultJson = value; });
    }

    public string ExportXliffResultJson { get => _exportXliffResultJson; private set => this.RaiseAndSetIfChanged(ref _exportXliffResultJson, value); }
    internal EditorXliffExportResult? LastExportXliff { get => _lastExportXliff; private set => this.RaiseAndSetIfChanged(ref _lastExportXliff, value); }
    public string PreviewXliffImportResultJson { get => _previewXliffImportResultJson; private set => this.RaiseAndSetIfChanged(ref _previewXliffImportResultJson, value); }
    internal EditorXliffImportPlan? LastPreviewXliffImport { get => _lastPreviewXliffImport; private set => this.RaiseAndSetIfChanged(ref _lastPreviewXliffImport, value); }
    public string ApplyXliffImportResultJson { get => _applyXliffImportResultJson; private set => this.RaiseAndSetIfChanged(ref _applyXliffImportResultJson, value); }
    internal EditorOperationResult? LastApplyXliffImport { get => _lastApplyXliffImport; private set => this.RaiseAndSetIfChanged(ref _lastApplyXliffImport, value); }
    public string ExportReviewJsonResultJson { get => _exportReviewJsonResultJson; private set => this.RaiseAndSetIfChanged(ref _exportReviewJsonResultJson, value); }
    internal EditorReviewFileResult? LastExportReviewJson { get => _lastExportReviewJson; private set => this.RaiseAndSetIfChanged(ref _lastExportReviewJson, value); }
    public string PreviewReviewJsonImportResultJson { get => _previewReviewJsonImportResultJson; private set => this.RaiseAndSetIfChanged(ref _previewReviewJsonImportResultJson, value); }
    internal EditorReviewImportPlan? LastPreviewReviewJsonImport { get => _lastPreviewReviewJsonImport; private set => this.RaiseAndSetIfChanged(ref _lastPreviewReviewJsonImport, value); }
    public string ApplyReviewJsonImportResultJson { get => _applyReviewJsonImportResultJson; private set => this.RaiseAndSetIfChanged(ref _applyReviewJsonImportResultJson, value); }
    internal EditorReviewOperationResult? LastApplyReviewJsonImport { get => _lastApplyReviewJsonImport; private set => this.RaiseAndSetIfChanged(ref _lastApplyReviewJsonImport, value); }

    public ReactiveCommand<string, RxVoid> ExportXliffCommand { get; }
    public ReactiveCommand<string, RxVoid> PreviewXliffImportCommand { get; }
    public ReactiveCommand<string, RxVoid> ApplyXliffImportCommand { get; }
    public ReactiveCommand<string, RxVoid> ExportReviewJsonCommand { get; }
    public ReactiveCommand<string, RxVoid> PreviewReviewJsonImportCommand { get; }
    public ReactiveCommand<string, RxVoid> ApplyReviewJsonImportCommand { get; }
}

/// <summary>Diagnostics operations exposed as individual generated View commands.</summary>
public sealed class EditorDiagnosticsViewModel : EditorFeatureViewModel
{
    private string _aboutResultJson = "null";
    private EditorAbout? _lastAbout;
    private string _createDiagnosticBundleResultJson = "null";
    private EditorDiagnosticBundleResult? _lastCreateDiagnosticBundle;
    private string _revealDiagnosticBundleResultJson = "null";
    private EditorDiagnosticBundleActionResult? _lastRevealDiagnosticBundle;
    private string _deleteDiagnosticBundleResultJson = "null";
    private EditorDiagnosticBundleActionResult? _lastDeleteDiagnosticBundle;

    internal EditorDiagnosticsViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        AboutCommand = CreateCommand(argument => Task.FromResult(EditorDiagnostics.About()), (result, value) => { LastAbout = result; AboutResultJson = value; });
        CreateDiagnosticBundleCommand = CreateCommand(async argument => await Session.CreateDiagnosticBundleAsync(), (result, value) => { LastCreateDiagnosticBundle = result; CreateDiagnosticBundleResultJson = value; });
        RevealDiagnosticBundleCommand = CreateCommand(argument => Task.FromResult(Session.RevealDiagnosticBundle(Required(argument, "path"))), (result, value) => { LastRevealDiagnosticBundle = result; RevealDiagnosticBundleResultJson = value; });
        DeleteDiagnosticBundleCommand = CreateCommand(argument => Task.FromResult(Session.DeleteDiagnosticBundle(Required(argument, "path"))), (result, value) => { LastDeleteDiagnosticBundle = result; DeleteDiagnosticBundleResultJson = value; });
    }

    public string AboutResultJson { get => _aboutResultJson; private set => this.RaiseAndSetIfChanged(ref _aboutResultJson, value); }
    internal EditorAbout? LastAbout { get => _lastAbout; private set => this.RaiseAndSetIfChanged(ref _lastAbout, value); }
    public string CreateDiagnosticBundleResultJson { get => _createDiagnosticBundleResultJson; private set => this.RaiseAndSetIfChanged(ref _createDiagnosticBundleResultJson, value); }
    internal EditorDiagnosticBundleResult? LastCreateDiagnosticBundle { get => _lastCreateDiagnosticBundle; private set => this.RaiseAndSetIfChanged(ref _lastCreateDiagnosticBundle, value); }
    public string RevealDiagnosticBundleResultJson { get => _revealDiagnosticBundleResultJson; private set => this.RaiseAndSetIfChanged(ref _revealDiagnosticBundleResultJson, value); }
    internal EditorDiagnosticBundleActionResult? LastRevealDiagnosticBundle { get => _lastRevealDiagnosticBundle; private set => this.RaiseAndSetIfChanged(ref _lastRevealDiagnosticBundle, value); }
    public string DeleteDiagnosticBundleResultJson { get => _deleteDiagnosticBundleResultJson; private set => this.RaiseAndSetIfChanged(ref _deleteDiagnosticBundleResultJson, value); }
    internal EditorDiagnosticBundleActionResult? LastDeleteDiagnosticBundle { get => _lastDeleteDiagnosticBundle; private set => this.RaiseAndSetIfChanged(ref _lastDeleteDiagnosticBundle, value); }

    public ReactiveCommand<string, RxVoid> AboutCommand { get; }
    public ReactiveCommand<string, RxVoid> CreateDiagnosticBundleCommand { get; }
    public ReactiveCommand<string, RxVoid> RevealDiagnosticBundleCommand { get; }
    public ReactiveCommand<string, RxVoid> DeleteDiagnosticBundleCommand { get; }
}

/// <summary>LocalState operations exposed as individual generated View commands.</summary>
public sealed class EditorLocalStateViewModel : EditorFeatureViewModel
{
    private string _loadLocalStateResultJson = "null";
    private EditorLocalStateSnapshot? _lastLoadLocalState;
    private string _saveLocalStateResultJson = "null";
    private EditorLocalStateSnapshot? _lastSaveLocalState;
    private string _clearLocalStateResultJson = "null";
    private EditorLocalStateClearResult? _lastClearLocalState;

    internal EditorLocalStateViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        LoadLocalStateCommand = CreateCommand(argument => Task.FromResult(Session.LoadLocalState()), (result, value) => { LastLoadLocalState = result; LoadLocalStateResultJson = value; });
        SaveLocalStateCommand = CreateCommand(argument => Task.FromResult(Session.SaveLocalState(Parse<EditorLocalStateEntry[]>(argument))), (result, value) => { LastSaveLocalState = result; SaveLocalStateResultJson = value; });
        ClearLocalStateCommand = CreateCommand(argument => Task.FromResult(Session.ClearLocalState()), (result, value) => { LastClearLocalState = result; ClearLocalStateResultJson = value; });
    }

    public string LoadLocalStateResultJson { get => _loadLocalStateResultJson; private set => this.RaiseAndSetIfChanged(ref _loadLocalStateResultJson, value); }
    internal EditorLocalStateSnapshot? LastLoadLocalState { get => _lastLoadLocalState; private set => this.RaiseAndSetIfChanged(ref _lastLoadLocalState, value); }
    public string SaveLocalStateResultJson { get => _saveLocalStateResultJson; private set => this.RaiseAndSetIfChanged(ref _saveLocalStateResultJson, value); }
    internal EditorLocalStateSnapshot? LastSaveLocalState { get => _lastSaveLocalState; private set => this.RaiseAndSetIfChanged(ref _lastSaveLocalState, value); }
    public string ClearLocalStateResultJson { get => _clearLocalStateResultJson; private set => this.RaiseAndSetIfChanged(ref _clearLocalStateResultJson, value); }
    internal EditorLocalStateClearResult? LastClearLocalState { get => _lastClearLocalState; private set => this.RaiseAndSetIfChanged(ref _lastClearLocalState, value); }

    public ReactiveCommand<string, RxVoid> LoadLocalStateCommand { get; }
    public ReactiveCommand<string, RxVoid> SaveLocalStateCommand { get; }
    public ReactiveCommand<string, RxVoid> ClearLocalStateCommand { get; }
}

/// <summary>Project operations exposed as individual generated View commands.</summary>
public sealed class EditorProjectViewModel : EditorFeatureViewModel
{
    private string _previewProjectResultJson = "null";
    private EditorProjectPlan? _lastPreviewProject;
    private string _createProjectResultJson = "null";
    private EditorOperationResult? _lastCreateProject;
    private string _openWorkspaceResultJson = "null";
    private EditorOperationResult? _lastOpenWorkspace;

    internal EditorProjectViewModel(EditorSession session, EditorViewModel owner) : base(session, owner)
    {
        PreviewProjectCommand = CreateCommand(argument => Task.FromResult(EditorSession.PreviewProject(Parse<EditorProjectCreationRequest>(argument))), (result, value) => { LastPreviewProject = result; PreviewProjectResultJson = value; });
        CreateProjectCommand = CreateCommand(async argument => await Session.CreateProjectAsync(Parse<EditorProjectCreationRequest>(argument)), (result, value) => { LastCreateProject = result; CreateProjectResultJson = value; });
        OpenWorkspaceCommand = CreateCommand(async argument => await Session.OpenWorkspaceAsync(Parse<EditorOpenWorkspaceRequest>(argument)), (result, value) => { LastOpenWorkspace = result; OpenWorkspaceResultJson = value; });
    }

    public string PreviewProjectResultJson { get => _previewProjectResultJson; private set => this.RaiseAndSetIfChanged(ref _previewProjectResultJson, value); }
    internal EditorProjectPlan? LastPreviewProject { get => _lastPreviewProject; private set => this.RaiseAndSetIfChanged(ref _lastPreviewProject, value); }
    public string CreateProjectResultJson { get => _createProjectResultJson; private set => this.RaiseAndSetIfChanged(ref _createProjectResultJson, value); }
    internal EditorOperationResult? LastCreateProject { get => _lastCreateProject; private set => this.RaiseAndSetIfChanged(ref _lastCreateProject, value); }
    public string OpenWorkspaceResultJson { get => _openWorkspaceResultJson; private set => this.RaiseAndSetIfChanged(ref _openWorkspaceResultJson, value); }
    internal EditorOperationResult? LastOpenWorkspace { get => _lastOpenWorkspace; private set => this.RaiseAndSetIfChanged(ref _lastOpenWorkspace, value); }

    public ReactiveCommand<string, RxVoid> PreviewProjectCommand { get; }
    public ReactiveCommand<string, RxVoid> CreateProjectCommand { get; }
    public ReactiveCommand<string, RxVoid> OpenWorkspaceCommand { get; }
}
