using System.Text.Json;
using ReactiveUI;
using ReactiveUI.Primitives;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;

namespace Runic.Translations.Editor;

/// <summary>A stable routed document with revision-checked validation and save commands.</summary>
public sealed class EditorDocumentViewModel : ReactiveObject, IDisposable
{
    private readonly EditorSession _session;
    private readonly EditorViewModel _owner;
    private readonly IDisposable _validationErrors;
    private readonly IDisposable _saveErrors;
    private string _content;
    private string _fileRevision;
    private bool _isMalformed;
    private ValidationResult? _lastValidation;
    private EditorOperationResult? _lastSave;
    private string _validationResultJson = "null";
    private string _saveResultJson = "null";

    internal EditorDocumentViewModel(EditorSession session, EditorViewModel owner, EditorDocument document)
    {
        _session = session;
        _owner = owner;
        Path = document.Path;
        _content = document.Content;
        _fileRevision = document.Revision;
        _isMalformed = document.IsMalformed;
        ValidateCommand = ReactiveCommand.CreateFromTask<string>(ValidateAsync);
        SaveCommand = ReactiveCommand.CreateFromTask<string>(SaveAsync);
        _validationErrors = ValidateCommand.ThrownExceptions.Subscribe(_ => { });
        _saveErrors = SaveCommand.ThrownExceptions.Subscribe(_ => { });
    }

    public string Path { get; }
    public string Content { get => _content; private set => this.RaiseAndSetIfChanged(ref _content, value); }
    public string FileRevision { get => _fileRevision; private set => this.RaiseAndSetIfChanged(ref _fileRevision, value); }
    public bool IsMalformed { get => _isMalformed; private set => this.RaiseAndSetIfChanged(ref _isMalformed, value); }
    public string ValidationResultJson { get => _validationResultJson; private set => this.RaiseAndSetIfChanged(ref _validationResultJson, value); }
    public string SaveResultJson { get => _saveResultJson; private set => this.RaiseAndSetIfChanged(ref _saveResultJson, value); }
    internal ValidationResult? LastValidation { get => _lastValidation; private set => this.RaiseAndSetIfChanged(ref _lastValidation, value); }
    internal EditorOperationResult? LastSave { get => _lastSave; private set => this.RaiseAndSetIfChanged(ref _lastSave, value); }
    public ReactiveCommand<string, RxVoid> ValidateCommand { get; }
    public ReactiveCommand<string, RxVoid> SaveCommand { get; }

    internal void Update(EditorDocument document)
    {
        Content = document.Content;
        FileRevision = document.Revision;
        IsMalformed = document.IsMalformed;
    }

    private async Task ValidateAsync(string request)
    {
        using var parsed = JsonDocument.Parse(request);
        var root = parsed.RootElement;
        string requestId = Required(root, "requestId");
        ValidationResult result = await _session.ValidateAsync(Path, Required(root, "content")).ConfigureAwait(false);
        LastValidation = result;
        ValidationResultJson = Envelope(requestId, JsonSerializer.Serialize(result, EditorJsonContext.Default.ValidationResult));
    }

    private async Task SaveAsync(string request)
    {
        using var parsed = JsonDocument.Parse(request);
        var root = parsed.RootElement;
        string requestId = Required(root, "requestId");
        EditorOperationResult result = await _session.SaveAsync(Path, Required(root, "content"), Required(root, "revision")).ConfigureAwait(false);
        if (result.Snapshot is { } snapshot) _owner.SyncDocuments(snapshot);
        LastSave = result;
        SaveResultJson = Envelope(requestId, JsonSerializer.Serialize(result, EditorJsonContext.Default.EditorOperationResult));
    }

    private static string Required(JsonElement root, string name) =>
        root.GetProperty(name).GetString() ?? throw new ArgumentException($"{name} is required.");

    private static string Envelope(string requestId, string result) =>
        "{\"requestId\":" + JsonSerializer.Serialize(requestId, EditorJsonContext.Default.String) + ",\"result\":" + result + "}";

    public void Dispose()
    {
        ValidateCommand.Dispose();
        SaveCommand.Dispose();
        _validationErrors.Dispose();
        _saveErrors.Dispose();
    }
}

public sealed partial class EditorDocumentView : ReactiveRunicView<EditorDocumentViewModel>;
