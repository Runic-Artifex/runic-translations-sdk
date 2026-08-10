using RunicTranslations.Authoring;

namespace RunicTranslations.Editor;

internal sealed class EditorSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EditorWorkspace _workspace;
    private bool _disposed;

    public EditorSession(string workspacePath, string? catalogId = null)
    {
        _workspace = new EditorWorkspace(workspacePath, catalogId);
    }

    public Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(static (workspace, token) => workspace.LoadAsync(token), cancellationToken);

    public Task<EditorExternalChanges> CheckExternalChangesAsync(CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(static (workspace, token) => workspace.CheckExternalChangesAsync(token), cancellationToken);

    public EditorMutationPreview PreviewMutation(EditorMutationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _gate.Wait();
        try
        {
            ThrowIfDisposed();
            TranslationWorkspaceTransactionPlan plan = PlanMutation(_workspace, request);
            return new EditorMutationPreview(true, null, MutationFiles(plan));
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
            TranslationWorkspaceTransactionPlan plan = PlanMutation(_workspace, request);
            TranslationWorkspaceTransaction.Commit(plan);
            WorkspaceSnapshot snapshot = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            return new EditorOperationResult(true, "mutated", null, snapshot, null);
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
            TranslationWorkspaceTransaction.Recover(_workspace.Root, mode);
            WorkspaceSnapshot snapshot = await _workspace.LoadAsync(cancellationToken).ConfigureAwait(false);
            return new EditorOperationResult(true, "recovered", null, snapshot, null);
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

    public Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.SaveAsync(relativePath, content, expectedRevision, token),
            cancellationToken);

    public Task<EditorReviewOperationResult> SaveReviewAsync(
        EditorReviewSaveRequest request,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.SaveReviewAsync(request, token),
            cancellationToken);

    public Task<EditorDiagnosticBundleResult> CreateDiagnosticBundleAsync(
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            async (workspace, token) => EditorDiagnostics.CreateBundle(
                await workspace.LoadAsync(token).ConfigureAwait(false)),
            cancellationToken);

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
                return new EditorOperationResult(true, "created", null, snapshot, null);
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
            var replacement = new EditorWorkspace(request.Directory, request.CatalogId);
            try
            {
                WorkspaceSnapshot snapshot = await replacement.LoadAsync(cancellationToken).ConfigureAwait(false);
                EditorWorkspace previous = _workspace;
                _workspace = replacement;
                previous.Dispose();
                return new EditorOperationResult(true, "opened", null, snapshot, null);
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
        request.LayerName,
        request.GenerateEsm,
        request.IncludeStarterMessage);

    private static TranslationWorkspaceTransactionPlan PlanMutation(EditorWorkspace workspace, EditorMutationRequest request)
    {
        string catalogId = workspace.CatalogId
            ?? throw new TranslationAuthoringException("Select a catalog before changing locales or keys.");
        return request.Kind switch
        {
            "add-locale" => TranslationWorkspaceMutation.AddLocale(new TranslationAddLocaleRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.Fallback,
                request.Layer ?? string.Empty, request.CopyFromLocale ?? string.Empty)),
            "remove-locale" => TranslationWorkspaceMutation.RemoveLocale(new TranslationRemoveLocaleRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.ReplacementFallback)),
            "set-fallback" => TranslationWorkspaceMutation.SetFallback(new TranslationSetFallbackRequest(
                workspace.Root, catalogId, request.Locale ?? string.Empty, request.Fallback)),
            "create-key" => TranslationWorkspaceMutation.CreateKey(new TranslationCreateKeyRequest(
                workspace.Root, catalogId, request.TargetKey ?? string.Empty, request.InitialValue ?? string.Empty, request.Layer ?? string.Empty)),
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

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
