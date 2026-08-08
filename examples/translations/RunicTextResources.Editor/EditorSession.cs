using RunicTextResources.Authoring;

namespace RunicTextResources.Editor;

internal sealed class EditorSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private EditorWorkspace _workspace;
    private bool _disposed;

    public EditorSession(string workspacePath)
    {
        _workspace = new EditorWorkspace(workspacePath);
    }

    public Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(static (workspace, token) => workspace.LoadAsync(token), cancellationToken);

    public Task<ValidationResult> ValidateAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.ValidateAsync(relativePath, content, token),
            cancellationToken);

    public Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default) =>
        WithWorkspaceAsync(
            (workspace, token) => workspace.SaveAsync(relativePath, content, expectedRevision, token),
            cancellationToken);

    public static EditorProjectPlan PreviewProject(EditorProjectCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            TextResourceProjectPlan plan = TextResourceProjectScaffolder.Render(ToAuthoringRequest(request));
            return new EditorProjectPlan(
                true,
                null,
                Path.GetFullPath(plan.Request.Directory),
                plan.Request.CatalogId,
                plan.Locales.Select(static locale => new EditorLocale(locale.Tag, locale.Fallback)).ToArray(),
                plan.Files.Select(static file => file.RelativePath).ToArray());
        }
        catch (Exception exception) when (exception is TextResourceAuthoringException or ArgumentException or IOException)
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
            TextResourceProjectPlan plan = TextResourceProjectScaffolder.Render(ToAuthoringRequest(request));
            string target = TextResourceProjectWriter.Create(plan);
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
        catch (Exception exception) when (exception is TextResourceAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorOperationResult(false, "project-creation", exception.Message, null, null);
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

    private static TextResourceProjectCreationRequest ToAuthoringRequest(EditorProjectCreationRequest request) => new(
        request.Directory,
        request.CatalogId,
        request.DefaultLocale,
        request.CodeNamespace,
        request.ClassName,
        request.AdditionalLocales.Select(static locale => new TextResourceProjectLocale(locale.Tag, locale.Fallback)),
        request.LayerName,
        request.GenerateEsm,
        request.IncludeStarterMessage);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
