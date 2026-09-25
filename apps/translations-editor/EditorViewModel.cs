using ReactiveUI;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;

namespace Runic.Translations.Editor;

/// <summary>The Window's routed editor features and stable document selection.</summary>
public sealed class EditorViewModel : ReactiveObject, IDisposable
{
    private readonly EditorSession _session;
    private readonly Dictionary<string, EditorDocumentViewModel> _documents = new(StringComparer.Ordinal);
    private IReadOnlyList<EditorDocumentViewModel> _documentViews = [];

    internal EditorViewModel(EditorSession session)
    {
        _session = session;
        Workspace = new EditorWorkspaceViewModel(session, this);
        DocumentTools = new EditorDocumentToolsViewModel(session, this);
        Review = new EditorReviewViewModel(session, this);
        Interchange = new EditorInterchangeViewModel(session, this);
        Diagnostics = new EditorDiagnosticsViewModel(session, this);
        LocalState = new EditorLocalStateViewModel(session, this);
        Project = new EditorProjectViewModel(session, this);
    }

    public EditorWorkspaceViewModel Workspace { get; }
    public EditorDocumentToolsViewModel DocumentTools { get; }
    public EditorReviewViewModel Review { get; }
    public EditorInterchangeViewModel Interchange { get; }
    public EditorDiagnosticsViewModel Diagnostics { get; }
    public EditorLocalStateViewModel LocalState { get; }
    public EditorProjectViewModel Project { get; }
    public IReadOnlyList<EditorDocumentViewModel> Documents => _documentViews;

    internal void SyncDocuments(WorkspaceSnapshot snapshot)
    {
        var current = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<EditorDocumentViewModel>(snapshot.Documents.Count);
        foreach (var document in snapshot.Documents)
        {
            current.Add(document.Path);
            if (!_documents.TryGetValue(document.Path, out var view))
                _documents.Add(document.Path, view = new EditorDocumentViewModel(_session, this, document));
            else view.Update(document);
            ordered.Add(view);
        }
        foreach (var (path, view) in _documents.ToArray())
        {
            if (current.Contains(path)) continue;
            _documents.Remove(path);
            view.Dispose();
        }
        _documentViews = ordered;
        this.RaisePropertyChanged(nameof(Documents));
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
