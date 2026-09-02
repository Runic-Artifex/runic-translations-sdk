using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.Translations.Authoring;

namespace Runic.Translations.Editor;

internal sealed class EditorSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly EditorHistory _history = new();
    private EditorWorkspace _workspace;
    private PreparedDestructiveMutation? _preparedDestructiveMutation;
    private (string Token, PreparedInterchangeImport Prepared)? _preparedXliffImport;
    private (string Token, PreparedInterchangeImport Prepared)? _preparedReviewImport;
    private bool _disposed;

    public EditorSession(string workspacePath)
    {
        _workspace = new EditorWorkspace(workspacePath);
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return Decorate(await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<EditorExternalChanges> CheckExternalChangesAsync(CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(static (workspace, token) => workspace.CheckExternalChangesAsync(token), cancellationToken);

    public EditorMutationPreview PreviewMutation(EditorMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            TranslationWorkspaceTransactionPlan plan = PlanMutation(_workspace, request);
            // The authoring package deliberately does not expose construction of an
            // exact arbitrary transaction plan.  Re-planning an inverse can widen its
            // scope when layers changed, so catalog mutations are confirmation-bound
            // until that supported adapter exists.  Document and workflow edits have
            // exact, locally-owned inverses and remain undoable.
            string token = Guid.NewGuid().ToString("N");
            _preparedDestructiveMutation = new PreparedDestructiveMutation(
                token,
                request with { ConfirmationToken = null },
                PlanFingerprint(plan));
            return new EditorMutationPreview(true, null, MutationFiles(plan), true, token);
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorMutationPreview(false, exception.Message, []);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> ApplyMutationAsync(
        EditorMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceSnapshot current = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current.PendingTransaction is not null)
                return Failure("recovery-required", "Recover the interrupted transaction before making another change.");
            TranslationWorkspaceTransactionPlan plan = PlanMutation(_workspace, request);
            if (!MatchesPreparedDestructiveMutation(request, plan))
            {
                _preparedDestructiveMutation = null;
                InvalidatePreparedImports();
                return Failure("irreversible-confirmation", "Preview this destructive change again and confirm the exact affected files.");
            }
            // A validated token is one-use even if the transaction cannot commit.
            // It must never be replayed after a partial transaction/recovery path.
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            TranslationWorkspaceTransaction.Commit(plan);
            // Commit is the point of no return.  Reconcile state from this exact plan
            // before any filesystem read can observe an external writer.
            _history.Record(null);
            try
            {
                WorkspaceSnapshot snapshot = await _workspace.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                return Success("mutated", Decorate(snapshot));
            }
            catch (Exception exception) when (IsReloadFailure(exception))
            {
                return new EditorOperationResult(true, "mutated", $"The change was committed; reload the workspace to refresh it. {exception.Message}", null, null, _history.State);
            }
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorOperationResult(false, "workspace-mutation", exception.Message, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> RecoverTransactionAsync(
        EditorRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            TranslationWorkspaceRecoveryMode mode = request.Mode switch
            {
                "complete" => TranslationWorkspaceRecoveryMode.Complete,
                "rollback" => TranslationWorkspaceRecoveryMode.Rollback,
                _ => throw new ArgumentException("Recovery mode must be 'complete' or 'rollback'."),
            };
            _history.Clear();
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            TranslationWorkspaceTransaction.Recover(_workspace.Root, mode);
            try
            {
                WorkspaceSnapshot snapshot = await _workspace.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                return Success("recovered", Decorate(snapshot));
            }
            catch (Exception exception) when (IsReloadFailure(exception))
            {
                return new EditorOperationResult(true, "recovered", $"Recovery completed; reload the workspace to refresh it. {exception.Message}", null, null, _history.State);
            }
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorOperationResult(false, "workspace-recovery", exception.Message, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<ValidationResult> ValidateAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.ValidateAsync(relativePath, content, token),
            cancellationToken);

    public Task<EditorMessagePreview> PreviewMessageAsync(
        string relativePath,
        string content,
        string locale,
        string key,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.PreviewMessageAsync(relativePath, content, locale, key, token),
            cancellationToken);

    public async Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = _workspace.NormalizeDocumentPath(relativePath);
            WorkspaceSnapshot before = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (before.PendingTransaction is not null)
                return Failure("recovery-required", "Recover the interrupted transaction before saving a document.");
            EditorDocument? original = before.Documents.SingleOrDefault(document => document.Path == path);
            EditorOperationResult result = await _workspace.SaveAsync(path, content, expectedRevision, CancellationToken.None).ConfigureAwait(false);
            if (!result.Ok)
                return result with { History = _history.State };
            if (original is null)
            {
                _history.Record(null);
                _preparedDestructiveMutation = null;
                InvalidatePreparedImports();
                return result with { History = _history.State };
            }
            // SaveAsync returns only after its atomic replace.  Record the known
            // output revision now; do not obtain it by rereading the file.
            _history.Record(new EditorHistory.SaveEntry(path, original.Content, content, Revision(content), original.Revision));
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            try
            {
                WorkspaceSnapshot snapshot = await _workspace.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                return result with { Snapshot = Decorate(snapshot), History = _history.State };
            }
            catch (Exception exception) when (IsReloadFailure(exception))
            {
                return new EditorOperationResult(true, "saved", $"The document was saved; reload the workspace to refresh it. {exception.Message}", null, null, _history.State);
            }
        }
        catch (ArgumentException exception)
        {
            return Failure("invalid-request", exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorReviewOperationResult> SaveReviewAsync(
        EditorReviewSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceSnapshot before = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (before.PendingTransaction is not null)
                return new EditorReviewOperationResult(false, "Recover the interrupted transaction before saving workflow data.", null, _history.State);
            EditorReviewSnapshot? previous = before.Review;
            EditorReviewOperationResult result = await _workspace.SaveReviewAsync(request, CancellationToken.None).ConfigureAwait(false);
            if (result.Ok && result.Review is not null && previous is not null)
            {
                _history.Record(new EditorHistory.ReviewEntry(
                    ReviewRequest(previous, result.Review.Revision),
                    ReviewRequest(result.Review, previous.Revision),
                    result.Review.Revision,
                    previous.Revision,
                    previous.Revision is null));
            }
            else if (result.Ok) _history.Record(null);
            if (result.Ok) { _preparedDestructiveMutation = null; InvalidatePreparedImports(); }
            return result with { History = _history.State };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> UndoAsync(CancellationToken cancellationToken = default) =>
        await ApplyHistoryAsync(undo: true, cancellationToken).ConfigureAwait(false);

    public async Task<EditorOperationResult> RedoAsync(CancellationToken cancellationToken = default) =>
        await ApplyHistoryAsync(undo: false, cancellationToken).ConfigureAwait(false);

    // ---- Interchange operations (W03). Exposed as internal session methods so
    // bridge verbs (added later) and CLI lanes reuse one implementation. ----

    public Task<EditorXliffExportResult> ExportXliffAsync(
        string? directory,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync((workspace, token) => workspace.ExportXliffAsync(directory, token), cancellationToken);

    public Task<EditorReviewFileResult> ExportReviewJsonAsync(
        string? path,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync((workspace, token) => workspace.ExportReviewJsonAsync(path, token), cancellationToken);

    public async Task<EditorXliffImportPlan> PreviewXliffImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            (EditorXliffImportPlan plan, PreparedInterchangeImport? prepared) = await _workspace.PreviewXliffImportAsync(path, cancellationToken).ConfigureAwait(false);
            _preparedXliffImport = plan.Ok && prepared is not null ? (Guid.NewGuid().ToString("N"), prepared) : null;
            return plan with { ConfirmationToken = _preparedXliffImport?.Token };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> ApplyXliffImportAsync(
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_preparedXliffImport is not { } prepared || !string.Equals(prepared.Token, confirmationToken, StringComparison.Ordinal))
                return Failure("irreversible-confirmation", "Preview this import again to obtain a valid confirmation token.");
            // One-use even when the commit fails partway.
            _preparedXliffImport = null;
            EditorOperationResult result = await _workspace.CommitXliffImportAsync(prepared.Prepared, CancellationToken.None).ConfigureAwait(false);
            if (result.Ok) _history.Record(null);
            return result with { History = _history.State };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorReviewImportPlan> PreviewReviewJsonImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            (EditorReviewImportPlan plan, PreparedInterchangeImport? prepared) = await _workspace.PreviewReviewJsonImportAsync(path, cancellationToken).ConfigureAwait(false);
            _preparedReviewImport = plan.Ok && prepared is not null ? (Guid.NewGuid().ToString("N"), prepared) : null;
            return plan with { ConfirmationToken = _preparedReviewImport?.Token };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorReviewOperationResult> ApplyReviewJsonImportAsync(
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_preparedReviewImport is not { } prepared || !string.Equals(prepared.Token, confirmationToken, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, "Preview this import again to obtain a valid confirmation token.", null, _history.State);
            _preparedReviewImport = null;
            EditorReviewOperationResult result = await _workspace.CommitReviewImportAsync(prepared.Prepared, CancellationToken.None).ConfigureAwait(false);
            if (result.Ok && result.Review is not null)
                _history.Record(null);
            return result with { History = _history.State };
        }
        finally
        {
            _gate.Release();
        }
    }

    private void InvalidatePreparedImports()
    {
        _preparedXliffImport = null;
        _preparedReviewImport = null;
    }

    public EditorHistoryState ClearHistory()
    {
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            _history.Clear();
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            return _history.State;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<EditorDiagnosticBundleResult> CreateDiagnosticBundleAsync(
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            async (workspace, token) => EditorDiagnostics.CreateBundle(
                await workspace.LoadAsync(token).ConfigureAwait(false)),
            cancellationToken);

    public EditorDiagnosticBundleActionResult RevealDiagnosticBundle(string path)
    {
        ThrowIfDisposed();
        return EditorDiagnostics.RevealBundle(path);
    }

    public EditorDiagnosticBundleActionResult DeleteDiagnosticBundle(string path)
    {
        ThrowIfDisposed();
        return EditorDiagnostics.DeleteBundle(path);
    }

    // This state is deliberately not workspace state: it belongs to the local
    // desktop user and survives a browser-profile/origin change. The native
    // store performs the atomic publication and recovery handling.
    public EditorLocalStateSnapshot LoadLocalState()
    {
        ThrowIfDisposed();
        return EditorLocalStateStore.Load();
    }

    public EditorLocalStateSnapshot SaveLocalState(IReadOnlyList<EditorLocalStateEntry> entries)
    {
        ThrowIfDisposed();
        return EditorLocalStateStore.Save(entries);
    }

    public EditorLocalStateClearResult ClearLocalState()
    {
        ThrowIfDisposed();
        return EditorLocalStateStore.Clear();
    }

    public static EditorProjectPlan PreviewProject(EditorProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            TranslationProjectPlan plan = TranslationProjectScaffolder.Render(ToAuthoringRequest(request));
            return new EditorProjectPlan(
                true,
                null,
                Path.GetFullPath(plan.Request.Directory),
                plan.Request.CatalogId,
                plan.Locales.Select(static locale => new EditorLocale(locale.Tag, locale.Fallback)).ToArray(),
                plan.Files.Select(static file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException)
        {
            return new EditorProjectPlan(false, exception.Message, request.Directory, request.CatalogId, [], []);
        }
    }

    public async Task<EditorOperationResult> CreateProjectAsync(
        EditorProjectCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            TranslationProjectPlan plan = TranslationProjectScaffolder.Render(ToAuthoringRequest(request));
            string target = TranslationProjectWriter.Create(plan);
            var replacement = new EditorWorkspace(target);
            try
            {
                WorkspaceSnapshot snapshot = await replacement.LoadAsync(cancellationToken).ConfigureAwait(false);
                EditorWorkspace previous = _workspace;
                _workspace = replacement;
                previous.Dispose();
                _history.Clear();
                _preparedDestructiveMutation = null;
                InvalidatePreparedImports();
                return Success("created", Decorate(snapshot));
            }
            catch
            {
                replacement.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorOperationResult(false, "project-creation", exception.Message, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorOperationResult> OpenWorkspaceAsync(
        EditorOpenWorkspaceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var replacement = new EditorWorkspace(request.Directory);
            try
            {
                WorkspaceSnapshot snapshot = await replacement.LoadAsync(cancellationToken).ConfigureAwait(false);
                EditorWorkspace previous = _workspace;
                _workspace = replacement;
                previous.Dispose();
                _history.Clear();
                _preparedDestructiveMutation = null;
                InvalidatePreparedImports();
                return Success("opened", Decorate(snapshot));
            }
            catch
            {
                replacement.Dispose();
                throw;
            }
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorOperationResult(false, "workspace-open", exception.Message, null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _workspace.Dispose();
        _gate.Dispose();
    }

    private async Task<T> WithWorkspaceAsync<T>(
        Func<EditorWorkspace, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await operation(_workspace, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static TranslationProjectCreationRequest ToAuthoringRequest(EditorProjectCreationRequest request) => new(
        request.Directory,
        request.CatalogId,
        request.DefaultLocale,
        request.CodeNamespace,
        request.ClassName,
        request.AdditionalLocales.Select(static locale => new TranslationProjectLocale(locale.Tag, locale.Fallback)),
        request.IncludeStarterMessage);

    private static TranslationWorkspaceTransactionPlan PlanMutation(EditorWorkspace workspace, EditorMutationRequest request)
    {
        string catalogId = workspace.CatalogId
            ?? throw new TranslationAuthoringException("Select a catalog before changing locales or keys.");
        return request.Kind switch
        {
            "add-locale" => TranslationWorkspaceMutation.AddLocale(new TranslationAddLocaleRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.Fallback,
                request.CopyFromLocale ?? string.Empty)),
            "remove-locale" => TranslationWorkspaceMutation.RemoveLocale(new TranslationRemoveLocaleRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.ReplacementFallback)),
            "set-fallback" => TranslationWorkspaceMutation.SetFallback(new TranslationSetFallbackRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.Fallback)),
            "create-key" => TranslationWorkspaceMutation.CreateKey(new TranslationCreateKeyRequest(
                workspace.Root, catalogId, request.TargetKey ?? string.Empty, request.InitialValue ?? string.Empty)),
            "rename-key" => KeyMutation(TranslationKeyMutationKind.RenameOrMove),
            "duplicate-key" => KeyMutation(TranslationKeyMutationKind.Duplicate),
            "delete-key" => KeyMutation(TranslationKeyMutationKind.Delete),
            _ => throw new TranslationAuthoringException($"Unknown editor mutation '{request.Kind}'."),
        };

        TranslationWorkspaceTransactionPlan KeyMutation(TranslationKeyMutationKind kind) =>
            TranslationWorkspaceMutation.MutateKey(new TranslationKeyMutationRequest(
                workspace.Root, catalogId, kind, request.SourceKey ?? string.Empty, request.TargetKey));
    }

    private static EditorMutationFile[] MutationFiles(TranslationWorkspaceTransactionPlan plan)
    {
        TranslationWorkspaceEdit[] edits = plan.Edits
            .OrderBy(static edit => edit.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var result = new EditorMutationFile[edits.Length];
        for (int index = 0; index < result.Length; index++)
        {
            TranslationWorkspaceEdit edit = edits[index];
            string fullPath = Path.GetFullPath(edit.RelativePath.Replace('/', Path.DirectorySeparatorChar), plan.Root);
            byte[]? replacement = edit.GetUtf8Bytes();
            result[index] = new EditorMutationFile(
                edit.RelativePath,
                edit.Kind.ToString().ToLowerInvariant(),
                File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0,
                replacement?.LongLength ?? 0);
        }
        return result;
    }

    private async Task<EditorOperationResult> ApplyHistoryAsync(bool undo, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceSnapshot current = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            if (current.PendingTransaction is not null)
                return Failure("recovery-required", "Recover the interrupted transaction before changing history.");
            EditorHistory.Entry entry;
            bool found = undo ? _history.TryBeginUndo(out entry) : _history.TryBeginRedo(out entry);
            if (!found) return Failure(undo ? "nothing-to-undo" : "nothing-to-redo", undo ? "There is no saved change to undo." : "There is no saved change to redo.");

            return entry switch
            {
                EditorHistory.SaveEntry save => await ApplySaveHistoryAsync(save, undo, cancellationToken).ConfigureAwait(false),
                EditorHistory.ReviewEntry review => await ApplyReviewHistoryAsync(review, undo, cancellationToken).ConfigureAwait(false),
                _ => Failure("history", "The saved history entry is unsupported."),
            };
        }
        catch (Exception exception) when (exception is TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Failure("history", exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EditorOperationResult> ApplySaveHistoryAsync(
        EditorHistory.SaveEntry entry,
        bool undo,
        CancellationToken cancellationToken)
    {
        string content = undo ? entry.Before : entry.After;
        string expected = undo ? entry.UndoRevision : entry.RedoRevision;
        EditorOperationResult result = await _workspace.SaveAsync(entry.Path, content, expected, CancellationToken.None).ConfigureAwait(false);
        if (!result.Ok)
            return result.Kind == "conflict"
                ? HistoryConflict(result.Message ?? "The saved document changed after this operation; history was cleared.")
                : Failure("history", result.Message ?? "The saved document could not be changed.");
        string revision = Revision(content);
        if (undo)
        {
            entry.SetRedoRevision(revision);
            _history.CompleteUndo(entry);
        }
        else
        {
            entry.SetUndoRevision(revision);
            _history.CompleteRedo(entry);
        }
        _preparedDestructiveMutation = null;
        InvalidatePreparedImports();
        return await ReloadAfterCommittedHistoryAsync(undo ? "undone" : "redone").ConfigureAwait(false);
    }

    private async Task<EditorOperationResult> ApplyReviewHistoryAsync(
        EditorHistory.ReviewEntry entry,
        bool undo,
        CancellationToken cancellationToken)
    {
        if (undo && entry.DeleteOnUndo)
        {
            EditorReviewOperationResult deleted = await _workspace.DeleteReviewAsync(entry.UndoRevision, CancellationToken.None).ConfigureAwait(false);
            if (!deleted.Ok)
                return HistoryConflict(deleted.Message ?? "The workflow sidecar changed after this operation; history was cleared.");
            entry.SetRedoRevision(null);
            _history.CompleteUndo(entry);
            _preparedDestructiveMutation = null;
            InvalidatePreparedImports();
            return await ReloadAfterCommittedHistoryAsync("undone").ConfigureAwait(false);
        }

        EditorReviewSaveRequest source = undo ? entry.Undo : entry.Redo;
        string? expected = undo ? entry.UndoRevision : entry.RedoRevision;
        EditorReviewOperationResult result = await _workspace.SaveReviewAsync(source with { ExpectedRevision = expected }, CancellationToken.None).ConfigureAwait(false);
        if (!result.Ok || result.Review?.Revision is null)
            return HistoryConflict(result.Message ?? "The workflow sidecar changed after this operation; history was cleared.");
        if (undo)
        {
            entry.SetRedoRevision(result.Review.Revision);
            _history.CompleteUndo(entry);
        }
        else
        {
            entry.SetUndoRevision(result.Review.Revision);
            _history.CompleteRedo(entry);
        }
        _preparedDestructiveMutation = null;
        InvalidatePreparedImports();
        return await ReloadAfterCommittedHistoryAsync(undo ? "undone" : "redone").ConfigureAwait(false);
    }

    private bool MatchesPreparedDestructiveMutation(EditorMutationRequest request, TranslationWorkspaceTransactionPlan plan) =>
        _preparedDestructiveMutation is { } prepared &&
        string.Equals(request.ConfirmationToken, prepared.Token, StringComparison.Ordinal) &&
        prepared.Request == (request with { ConfirmationToken = null }) &&
        string.Equals(prepared.PlanFingerprint, PlanFingerprint(plan), StringComparison.Ordinal);

    private static string PlanFingerprint(TranslationWorkspaceTransactionPlan plan)
    {
        var builder = new StringBuilder();
        foreach (TranslationWorkspaceEdit edit in plan.Edits.OrderBy(edit => edit.RelativePath, StringComparer.Ordinal))
        {
            builder.Append(edit.RelativePath).Append('\0')
                .Append(edit.Kind).Append('\0')
                .Append(edit.ExpectedRevision).Append('\0');
            byte[]? bytes = edit.GetUtf8Bytes();
            if (bytes is not null) builder.Append(Convert.ToHexStringLower(SHA256.HashData(bytes)));
            builder.Append('\n');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private async Task<EditorOperationResult> ReloadAfterCommittedHistoryAsync(string kind)
    {
        try
        {
            WorkspaceSnapshot snapshot = await _workspace.LoadAsync(CancellationToken.None).ConfigureAwait(false);
            return Success(kind, Decorate(snapshot));
        }
        catch (Exception exception) when (IsReloadFailure(exception))
        {
            return new EditorOperationResult(true, kind, $"The change was committed; reload the workspace to refresh it. {exception.Message}", null, null, _history.State);
        }
    }

    private static string Revision(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(content)));

    private static bool IsReloadFailure(Exception exception) => exception is
        TranslationAuthoringException or
        TranslationEditorStateException or
        ArgumentException or
        JsonException or
        IOException or
        UnauthorizedAccessException;

    private static EditorReviewSaveRequest ReviewRequest(EditorReviewSnapshot review, string? expectedRevision) => new(
        expectedRevision,
        review.Entries.Select(entry => new EditorReviewEntry(
            entry.Key, entry.Locale, entry.State, entry.Note, entry.SourceFingerprint,
            new Dictionary<string, string>(entry.Samples, StringComparer.Ordinal))).ToArray(),
        review.Terminology.Select(term => new EditorTerminologyEntry(
            term.Source, term.Preferred, term.Locale, term.Note)).ToArray());

    private WorkspaceSnapshot Decorate(WorkspaceSnapshot snapshot) => snapshot with { History = _history.State };

    private EditorOperationResult Success(string kind, WorkspaceSnapshot snapshot) =>
        new(true, kind, null, snapshot, null, _history.State);

    private EditorOperationResult Failure(string kind, string message) =>
        new(false, kind, message, null, null, _history.State);

    private EditorOperationResult HistoryConflict(string message)
    {
        _history.Clear();
        _preparedDestructiveMutation = null;
        InvalidatePreparedImports();
        return Failure("conflict", message);
    }

    private sealed record PreparedDestructiveMutation(
        string Token,
        EditorMutationRequest Request,
        string PlanFingerprint);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
