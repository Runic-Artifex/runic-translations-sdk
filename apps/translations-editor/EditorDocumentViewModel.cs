using ReactiveUI;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;

namespace Runic.Translations.Editor;

/// <summary>Typed browser input for compiler-backed validation of one document draft.</summary>
public sealed record EditorDocumentValidationRequest(string Content);

/// <summary>One diagnostic returned by document validation without serializing an opaque JSON envelope.</summary>
public sealed record EditorDocumentValidationDiagnostic(
    string Id,
    string Severity,
    string Message,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

/// <summary>Typed validation result retained by the bridge operation.</summary>
public sealed record EditorDocumentValidationResult(
    bool Success,
    IReadOnlyList<EditorDocumentValidationDiagnostic> Diagnostics);

/// <summary>Typed browser input for a revision-checked document save.</summary>
public sealed record EditorDocumentSaveRequest(string Content, string Revision);

/// <summary>One localized, structured notice returned by a typed document operation.</summary>
public sealed record EditorDocumentNoticeArgument(string Name, string? Value, double? Number);

/// <summary>A localized, structured notice returned by a typed document operation.</summary>
public sealed record EditorDocumentNotice(
    string Code,
    IReadOnlyList<EditorDocumentNoticeArgument> Args,
    string? Detail);

/// <summary>Typed save result. The fresh document state arrives through the normal bridge publication.</summary>
public sealed record EditorDocumentSaveResult(bool Ok, string Kind, EditorDocumentNotice? Message);

/// <summary>A stable routed document with revision-checked validation and save commands.</summary>
public sealed class EditorDocumentViewModel : ReactiveObject, IDisposable
{
    private readonly EditorSession _session;
    private readonly EditorViewModel _owner;
    private readonly IRunicModelContext _modelContext;
    private readonly IDisposable _validationErrors;
    private readonly IDisposable _saveErrors;
    private string _content;
    private string _fileRevision;
    private bool _isMalformed;
    private ValidationResult? _lastValidation;
    private EditorOperationResult? _lastSave;

    internal EditorDocumentViewModel(EditorSession session, EditorViewModel owner, EditorDocument document, ISequencer scheduler,
        IRunicModelContext modelContext)
    {
        _session = session;
        _owner = owner;
        _modelContext = modelContext;
        Path = document.Path;
        _content = document.Content;
        _fileRevision = document.Revision;
        _isMalformed = document.IsMalformed;
        ValidateCommand = ReactiveCommand.CreateFromTask<EditorDocumentValidationRequest, EditorDocumentValidationResult>(ValidateAsync, scheduler);
        SaveCommand = ReactiveCommand.CreateFromTask<EditorDocumentSaveRequest, EditorDocumentSaveResult>(SaveAsync, scheduler);
        _validationErrors = ValidateCommand.ThrownExceptions.Subscribe(_ => { });
        _saveErrors = SaveCommand.ThrownExceptions.Subscribe(_ => { });
    }

    public string Path { get; }
    public string Content { get => _content; private set => this.RaiseAndSetIfChanged(ref _content, value); }
    public string FileRevision { get => _fileRevision; private set => this.RaiseAndSetIfChanged(ref _fileRevision, value); }
    public bool IsMalformed { get => _isMalformed; private set => this.RaiseAndSetIfChanged(ref _isMalformed, value); }
    internal ValidationResult? LastValidation { get => _lastValidation; private set => this.RaiseAndSetIfChanged(ref _lastValidation, value); }
    internal EditorOperationResult? LastSave { get => _lastSave; private set => this.RaiseAndSetIfChanged(ref _lastSave, value); }
    public ReactiveCommand<EditorDocumentValidationRequest, EditorDocumentValidationResult> ValidateCommand { get; }
    public ReactiveCommand<EditorDocumentSaveRequest, EditorDocumentSaveResult> SaveCommand { get; }

    internal void Update(EditorDocument document)
    {
        Content = document.Content;
        FileRevision = document.Revision;
        IsMalformed = document.IsMalformed;
    }

    private async Task<EditorDocumentValidationResult> ValidateAsync(EditorDocumentValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult result = await _session.ValidateAsync(Path, request.Content).ConfigureAwait(false);
        await _modelContext.InvokeAsync(() => LastValidation = result).ConfigureAwait(false);
        return new EditorDocumentValidationResult(result.Success, result.Diagnostics.Select(diagnostic =>
            new EditorDocumentValidationDiagnostic(diagnostic.Id, diagnostic.Severity, diagnostic.Message,
                diagnostic.Path, diagnostic.Line, diagnostic.Column, diagnostic.EndLine, diagnostic.EndColumn)).ToArray());
    }

    private async Task<EditorDocumentSaveResult> SaveAsync(EditorDocumentSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EditorOperationResult result = await _session.SaveAsync(Path, request.Content, request.Revision).ConfigureAwait(false);
        await _modelContext.InvokeAsync(() =>
        {
            if (result.Snapshot is { } snapshot) _owner.SyncDocuments(snapshot);
            LastSave = result;
        }).ConfigureAwait(false);
        return new EditorDocumentSaveResult(result.Ok, result.Kind, ToDocumentNotice(result.Message));
    }

    private static EditorDocumentNotice? ToDocumentNotice(EditorNotice? notice) => notice is null
        ? null
        : new(notice.Code, notice.Args.Select(argument => new EditorDocumentNoticeArgument(
            argument.Name, argument.Value, argument.Number)).ToArray(), notice.Detail);

    public void Dispose()
    {
        ValidateCommand.Dispose();
        SaveCommand.Dispose();
        _validationErrors.Dispose();
        _saveErrors.Dispose();
    }
}

public sealed partial class EditorDocumentView : ReactiveRunicView<EditorDocumentViewModel>;
