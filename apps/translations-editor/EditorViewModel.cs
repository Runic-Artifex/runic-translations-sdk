using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;

namespace Runic.Translations.Editor;

/// <summary>The Window's routed editor features and stable document selection.</summary>
public sealed class EditorViewModel : ReactiveObject, IDisposable
{
    private readonly EditorSession _session;
    private readonly IRunicModelContext _modelContext;
    private readonly IRunicModelContextLease _modelContextLease;
    private readonly ISequencer _scheduler;
    private readonly Dictionary<string, IRunicModelContextLease> _documentContextLeases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EditorDocumentViewModel> _documents = new(StringComparer.Ordinal);
    private IReadOnlyList<EditorDocumentViewModel> _documentViews = [];

    internal EditorViewModel(EditorSession session, IRunicModelContext modelContext, ISequencer scheduler)
    {
        _session = session;
        _modelContext = modelContext;
        _scheduler = scheduler;
        Workspace = new EditorWorkspaceViewModel(session, this, _scheduler);
        DocumentTools = new EditorDocumentToolsViewModel(session, this, _scheduler);
        Review = new EditorReviewViewModel(session, this, _scheduler);
        Interchange = new EditorInterchangeViewModel(session, this, _scheduler);
        Diagnostics = new EditorDiagnosticsViewModel(session, this, _scheduler);
        LocalState = new EditorLocalStateViewModel(session, this, _scheduler);
        Project = new EditorProjectViewModel(session, this, _scheduler);
        _modelContextLease = RunicModelContextRegistry.Shared.Bind(modelContext, this, Workspace,
            DocumentTools, Review, Interchange, Diagnostics, LocalState, Project);
    }

    public EditorWorkspaceViewModel Workspace { get; }
    public EditorDocumentToolsViewModel DocumentTools { get; }
    public EditorReviewViewModel Review { get; }
    public EditorInterchangeViewModel Interchange { get; }
    public EditorDiagnosticsViewModel Diagnostics { get; }
    public EditorLocalStateViewModel LocalState { get; }
    public EditorProjectViewModel Project { get; }
    public IReadOnlyList<EditorDocumentViewModel> Documents => _documentViews;
    internal IRunicModelContext ModelContext => _modelContext;

    internal void SyncDocuments(WorkspaceSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Loading, importing, and workspace mutations can replace dozens of
        // document values in one synchronous model-context turn. Batch each
        // already exposed model before raising those notifications so the
        // browser receives the final workspace and document snapshots instead
        // of capturing every intermediate document state.
        using var workspaceBatch = BridgeSnapshotBatch.Begin(this);
        IDisposable[] documentBatches = _documents.Values
            .Select(static document => BridgeSnapshotBatch.Begin(document))
            .ToArray();
        try
        {
            var current = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<EditorDocumentViewModel>(snapshot.Documents.Count);
            foreach (var document in snapshot.Documents)
            {
                current.Add(document.Path);
                if (!_documents.TryGetValue(document.Path, out var view))
                {
                    _documents.Add(document.Path, view = new EditorDocumentViewModel(_session, this, document, _scheduler, _modelContext));
                    _documentContextLeases.Add(document.Path,
                        RunicModelContextRegistry.Shared.Bind(_modelContextLease.Context, view));
                }
                else view.Update(document);
                ordered.Add(view);
            }
            foreach (var (path, view) in _documents.ToArray())
            {
                if (current.Contains(path)) continue;
                _documents.Remove(path);
                if (_documentContextLeases.Remove(path, out var lease)) lease.Dispose();
                view.Dispose();
            }
            _documentViews = ordered;
            this.RaisePropertyChanged(nameof(Documents));
        }
        finally
        {
            // Individual document batches must close before the workspace
            // batch, so routed document state is captured before the root
            // document list is captured.
            Exception? failure = null;
            for (var index = documentBatches.Length - 1; index >= 0; index--)
            {
                try { documentBatches[index].Dispose(); }
                catch (Exception error) { failure ??= error; }
            }
            if (failure is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    public void Dispose()
    {
        Workspace.Dispose();
        DocumentTools.Dispose();
        Review.Dispose();
        Interchange.Dispose();
        Diagnostics.Dispose();
        LocalState.Dispose();
        Project.Dispose();
        foreach (var document in _documents.Values) document.Dispose();
        _documents.Clear();
        foreach (var lease in _documentContextLeases.Values) lease.Dispose();
        _documentContextLeases.Clear();
        _modelContextLease.Dispose();
        _session.Dispose();
    }
}

public sealed partial class EditorWorkspaceView : ReactiveRunicView<EditorWorkspaceViewModel>;
public sealed partial class EditorDocumentToolsView : ReactiveRunicView<EditorDocumentToolsViewModel>;
public sealed partial class EditorReviewView : ReactiveRunicView<EditorReviewViewModel>;
public sealed partial class EditorInterchangeView : ReactiveRunicView<EditorInterchangeViewModel>;
public sealed partial class EditorDiagnosticsView : ReactiveRunicView<EditorDiagnosticsViewModel>;
public sealed partial class EditorLocalStateView : ReactiveRunicView<EditorLocalStateViewModel>;
public sealed partial class EditorProjectView : ReactiveRunicView<EditorProjectViewModel>;
