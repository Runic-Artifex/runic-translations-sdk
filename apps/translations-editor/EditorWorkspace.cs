using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

/// <summary>
/// The editor's workspace: one opened folder and its selected catalog. A coordinator that owns the workspace gate and
/// composes the services in <c>Workspace/</c>; each member delegates to the service that owns it. Every operation that
/// takes the gate takes this one <see cref="WorkspaceGate"/>, so workspace operations still run one at a time.
/// </summary>
internal sealed class EditorWorkspace : IDisposable
{
    internal const string NewMf2DocumentRevision = "new-mf2-document";
    private readonly WorkspaceGate _gate = new();
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceChangeTracker _changes;
    private readonly WorkspaceStateReader _reader;
    private readonly ReviewService _review;
    private readonly WorkspaceLoader _loader;
    private readonly DocumentEditService _edits;
    private readonly PreviewService _preview;
    private readonly DocumentSaveService _save;
    private readonly InterchangeService _interchange;
    private readonly MutationPlanner _mutations;

    public EditorWorkspace(string root, string? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string? requestedCatalog = string.IsNullOrWhiteSpace(catalog) ? null : catalog;
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
            throw new EditorUserException(EditorNotice.Create("ui_backend_workspace_missing", ("path", fullRoot)));
        // Links above the workspace, and the opened folder itself being one, are the user's own layout (#280): the
        // workspace works from the physical folder, and the link checks refuse only links inside it.
        fullRoot = WorkspaceFiles.PhysicalPath(fullRoot);
        WorkspaceFiles.ValidateNoReparseAncestors(fullRoot);
        _files = new WorkspaceFiles(fullRoot, requestedCatalog);
        _changes = new WorkspaceChangeTracker(_gate, _files);
        _reader = new WorkspaceStateReader(_files, _changes);
        var diagnostics = new DiagnosticProjector(_files, _reader);
        var entries = new EntryProjector(_files);
        _review = new ReviewService(_gate, _files, _reader);
        _loader = new WorkspaceLoader(_gate, _files, _reader, diagnostics, entries, _review);
        _edits = new DocumentEditService(_gate, _files, _reader, diagnostics, entries);
        _preview = new PreviewService(_gate, _files, _reader, diagnostics);
        _save = new DocumentSaveService(_gate, _files, _reader, diagnostics, _review);
        _interchange = new InterchangeService(_gate, _files, _reader, _review, _loader);
        _mutations = new MutationPlanner(_gate, _files, _reader, _review);
    }

    public string Root => _files.Root;
    public string? CatalogId => _reader.CatalogId;

    /// <summary>The absolute path of the selected project's runic.json, or null when the workspace has none selected.</summary>
    internal string? ProjectConfigPath => _files.FindMf2ProjectConfig();

    public Task<EditorExternalChanges> CheckExternalChangesAsync(CancellationToken cancellationToken = default) =>
        _changes.CheckExternalChangesAsync(_review, cancellationToken);

    public Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default) => _loader.LoadAsync(cancellationToken);

    public Task<EditorDocumentDraft> TransformDocumentAsync(string relativePath, string content,
        string? key, string? value, CancellationToken cancellationToken = default) =>
        _edits.TransformDocumentAsync(relativePath, content, key, value, cancellationToken);

    public Task<EditorDocumentDraft> ApplyAuthoringOperationAsync(string relativePath, string content,
        string key, string expectedRevision, EditorMessageOperation operation, CancellationToken cancellationToken = default) =>
        _edits.ApplyAuthoringOperationAsync(relativePath, content, key, expectedRevision, operation, cancellationToken);

    /// <inheritdoc cref="DocumentEditService.CreateMessageDraftAsync"/>
    public Task<EditorMessageDraft> CreateMessageDraftAsync(string key, string locale, CancellationToken cancellationToken = default) =>
        _edits.CreateMessageDraftAsync(key, locale, cancellationToken);

    /// <inheritdoc cref="DocumentEditService.ApplyMessageDraftOperation"/>
    public static EditorMessageDraft ApplyMessageDraftOperation(string key, string locale, string text, string expectedRevision, EditorMessageOperation operation) =>
        DocumentEditService.ApplyMessageDraftOperation(key, locale, text, expectedRevision, operation);

    public Task<EditorDocumentDraft> ApplyDiagnosticFixAsync(string relativePath, string content,
        Rmf2DiagnosticQuickFix fix, CancellationToken cancellationToken = default) =>
        _edits.ApplyDiagnosticFixAsync(relativePath, content, fix, cancellationToken);

    public Task<ValidationResult> ValidateAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default) =>
        _edits.ValidateAsync(relativePath, content, cancellationToken);

    public Task<EditorMessagePreview> PreviewMessageAsync(
        string relativePath,
        string content,
        string locale,
        string key,
        string? samplesJson = null,
        string? requestedLocale = null,
        string? timeZone = null,
        CancellationToken cancellationToken = default) =>
        _preview.PreviewMessageAsync(relativePath, content, locale, key, samplesJson, requestedLocale, timeZone, cancellationToken);

    public Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default) =>
        _save.SaveAsync(relativePath, content, expectedRevision, cancellationToken);

    public Task<EditorReviewOperationResult> SaveReviewAsync(
        EditorReviewSaveRequest request,
        CancellationToken cancellationToken = default) =>
        _review.SaveReviewAsync(request, cancellationToken);

    public Task<EditorReviewOperationResult> DeleteReviewAsync(
        string? expectedRevision,
        CancellationToken cancellationToken = default) =>
        _review.DeleteReviewAsync(expectedRevision, cancellationToken);

    public void Dispose()
    {
        if (_gate.Disposed) return;
        _gate.MarkDisposed();
        _changes.Dispose();
        _gate.Dispose();
    }

    internal Task<EditorXliffExportResult> ExportXliffAsync(
        string? directory,
        bool allowRootedOutput = false,
        CancellationToken cancellationToken = default) =>
        _interchange.ExportXliffAsync(directory, allowRootedOutput, cancellationToken);

    internal Task<EditorReviewFileResult> ExportReviewJsonAsync(
        string? path,
        bool allowRootedOutput = false,
        CancellationToken cancellationToken = default) =>
        _interchange.ExportReviewJsonAsync(path, allowRootedOutput, cancellationToken);

    internal Task<(EditorXliffImportPlan Plan, PreparedInterchangeImport? Prepared)> PreviewXliffImportAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        _interchange.PreviewXliffImportAsync(path, cancellationToken);

    internal Task<EditorOperationResult> CommitXliffImportAsync(
        PreparedInterchangeImport prepared,
        CancellationToken cancellationToken = default) =>
        _interchange.CommitXliffImportAsync(prepared, cancellationToken);

    internal Task<(EditorReviewImportPlan Plan, PreparedInterchangeImport? Prepared)> PreviewReviewJsonImportAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        _interchange.PreviewReviewJsonImportAsync(path, cancellationToken);

    internal Task<EditorReviewOperationResult> CommitReviewImportAsync(
        PreparedInterchangeImport prepared,
        CancellationToken cancellationToken = default) =>
        _interchange.CommitReviewImportAsync(prepared, cancellationToken);

    /// <inheritdoc cref="ReviewService.EditorSourceFingerprint"/>
    internal static string EditorSourceFingerprint(string value) => ReviewService.EditorSourceFingerprint(value);

    /// <inheritdoc cref="WorkspaceFiles.NormalizeDocumentPath"/>
    internal string NormalizeDocumentPath(string path) => _files.NormalizeDocumentPath(path);

    /// <inheritdoc cref="WorkspaceFiles.EnsureWritable"/>
    internal void EnsureWritable(string fullPath) => _files.EnsureWritable(fullPath);

    /// <inheritdoc cref="WorkspaceFiles.RelativePath"/>
    internal string RelativePath(string fullPath) => _files.RelativePath(fullPath);

    /// <inheritdoc cref="WorkspaceChangeTracker.WriteOwnFiles"/>
    internal void WriteOwnFiles(Action write, IReadOnlyList<(string Path, byte[]? Bytes)> written) => _changes.WriteOwnFiles(write, written);

    internal static string SourceRootOutsideWorkspace(string root) => WorkspaceFiles.SourceRootOutsideWorkspace(root);
    internal static string SourceRootMissing(string root) => WorkspaceFiles.SourceRootMissing(root);

    internal TranslationWorkspaceTransactionPlan? PlanRmf2Mutation(EditorMutationRequest request) => _mutations.PlanRmf2Mutation(request);

    /// <inheritdoc cref="MutationPlanner.PreviewCatalogImportAsync"/>
    internal Task<(EditorCatalogImportPlan Plan, TranslationWorkspaceTransactionPlan? Transaction)> PreviewCatalogImportAsync(
        EditorCatalogImportRequest request, CancellationToken cancellationToken = default) =>
        _mutations.PreviewCatalogImportAsync(request, cancellationToken);

    /// <inheritdoc cref="ReviewService.ReviewDisplayPath"/>
    internal string? ReviewDisplayPath() => _review.ReviewDisplayPath();

    /// <inheritdoc cref="WorkspaceLoader.LocaleContextAsync"/>
    internal Task<EditorLocaleContext?> LocaleContextAsync(CancellationToken cancellationToken = default) => _loader.LocaleContextAsync(cancellationToken);

    /// <inheritdoc cref="ReviewService.RecordStarterDrafts"/>
    internal void RecordStarterDrafts(string locale, IReadOnlyCollection<string> createdFiles) => _review.RecordStarterDrafts(locale, createdFiles);

    /// <inheritdoc cref="ReviewService.CompleteLocaleRemoval"/>
    internal void CompleteLocaleRemoval(string locale) => _review.CompleteLocaleRemoval(locale);
}
