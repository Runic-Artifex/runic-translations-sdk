using ReactiveUI;
using ReactiveUI.Primitives.Concurrency;
using Runic.Application.Views;
using Runic.Application.Views.ReactiveUI;
using Runic.Navigation;
using Runic.Platform;
using ReactiveUI.Primitives;

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
    private readonly IRunicWindowLocale _windowLocale;
    private readonly TimeSpan _closeQuestionTimeout;

    /// <summary>How long the page may take to answer whether the window may close; it answers at once (see
    /// <see cref="ConfirmClose"/>), so this only bounds a page that does not respond.</summary>
    internal static readonly TimeSpan DefaultCloseQuestionTimeout = TimeSpan.FromSeconds(30);

    internal EditorViewModel(EditorSession session, IRunicModelContext modelContext, ISequencer scheduler,
        IRunicWindowLocale windowLocale, IRunicWindowContext window, IFileDialogs files, TimeSpan? closeQuestionTimeout = null)
    {
        _session = session;
        _modelContext = modelContext;
        _scheduler = scheduler;
        _windowLocale = windowLocale;
        // No page that can answer (none has mounted its handler yet, or it is gone) holds no unsaved work: the window
        // may close. The work the page had is in the recovery copy of the native editor state.
        _closeQuestionTimeout = closeQuestionTimeout ?? DefaultCloseQuestionTimeout;
        ConfirmClose = new ViewQuestion<EditorCloseQuestion, bool>(static (_, _) => ValueTask.FromResult(true), _closeQuestionTimeout);
        // The page's Close window command, and the page itself once the user saved or discarded the work it asked about:
        // the same close as the window's close button, so the page is asked again (now with nothing unsaved).
        CloseWindowCommand = ReactiveCommand.CreateFromTask<RxVoid, bool>(
            async (_, cancellationToken) => await window.RequestCloseAsync(cancellationToken).ConfigureAwait(false), outputScheduler: _scheduler);
        // The page negotiates the interface locale against the editor catalog and the stored choice, then sets it
        // here; the window's pages and its native surfaces follow the window's locale.
        SetUiLocaleCommand = ReactiveCommand.Create<string>(SetUiLocale, outputScheduler: _scheduler);
        Workspace = new EditorWorkspaceViewModel(session, this, _scheduler, files);
        DocumentTools = new EditorDocumentToolsViewModel(session, this, _scheduler);
        Review = new EditorReviewViewModel(session, this, _scheduler);
        Interchange = new EditorInterchangeViewModel(session, this, _scheduler);
        Diagnostics = new EditorDiagnosticsViewModel(session, this, _scheduler);
        LocalState = new EditorLocalStateViewModel(session, this, _scheduler);
        Project = new EditorProjectViewModel(session, this, _scheduler);
        Libraries = new EditorLibrariesViewModel(session, this, _scheduler);
        _modelContextLease = RunicModelContextRegistry.Shared.Bind(modelContext, this, Workspace,
            DocumentTools, Review, Interchange, Diagnostics, LocalState, Project, Libraries);
    }

    public EditorWorkspaceViewModel Workspace { get; }
    public EditorDocumentToolsViewModel DocumentTools { get; }
    public EditorReviewViewModel Review { get; }
    public EditorInterchangeViewModel Interchange { get; }
    public EditorDiagnosticsViewModel Diagnostics { get; }
    public EditorLocalStateViewModel LocalState { get; }
    public EditorProjectViewModel Project { get; }
    public EditorLibrariesViewModel Libraries { get; }
    public IReadOnlyList<EditorDocumentViewModel> Documents => _documentViews;
    /// <summary>Sets the window's locale to one of the editor catalog's locales, such as <c>de</c>.</summary>
    public ReactiveCommand<string, RxVoid> SetUiLocaleCommand { get; }

    /// <summary>
    /// Asks the page whether the window may close now. The page answers at once: <see langword="true"/> when nothing is
    /// unsaved, else <see langword="false"/>, and it then shows its own Save, Discard and Cancel question. Save or Discard
    /// closes the window afterwards with <see cref="CloseWindowCommand"/>. The page holds the unsaved work (drafts,
    /// review changes, a repair), so it is the one to ask; no answer waits for a person.
    /// </summary>
    public ViewQuestion<EditorCloseQuestion, bool> ConfirmClose { get; }

    /// <summary>Closes the window as its close button does (<c>RequestCloseAsync</c>): the page is asked first. The
    /// result is whether it closed.</summary>
    public ReactiveCommand<RxVoid, bool> CloseWindowCommand { get; }
    internal IRunicModelContext ModelContext => _modelContext;

    internal string? UiLocale => _windowLocale.Locale;

    // The page lists only the editor catalog's locales; a tag the window rejects leaves the locale as it is.
    private void SetUiLocale(string locale)
    {
        try { _windowLocale.Locale = string.IsNullOrWhiteSpace(locale) ? null : locale; }
        catch (ArgumentException) { }
    }

    /// <summary>
    /// The window's close guard (<see cref="EditorWindow"/>): whether the page agrees. A page that does not answer in
    /// time, cancels, or answers with something else keeps the window open; the user can close it again.
    /// </summary>
    internal async ValueTask<bool> CanCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ConfirmClose.AskAsync(
                new EditorCloseQuestion((int)_closeQuestionTimeout.TotalMilliseconds), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is RunicInteractionTimeoutException or RunicInteractionCancelledException
            or RunicInteractionOutputException)
        {
            return false;
        }
    }

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
        Libraries.Dispose();
        foreach (var document in _documents.Values) document.Dispose();
        _documents.Clear();
        foreach (var lease in _documentContextLeases.Values) lease.Dispose();
        _documentContextLeases.Clear();
        SetUiLocaleCommand.Dispose();
        CloseWindowCommand.Dispose();
        _modelContextLease.Dispose();
        _session.Dispose();
    }
}

/// <summary>The question <see cref="EditorViewModel.ConfirmClose"/> asks the page before the window closes.</summary>
/// <param name="AnswerWithinMilliseconds">How long the page has to answer; without an answer by then the window stays
/// open.</param>
public sealed record EditorCloseQuestion(int AnswerWithinMilliseconds);

public sealed partial class EditorWorkspaceView : ReactiveRunicView<EditorWorkspaceViewModel>;
public sealed partial class EditorDocumentToolsView : ReactiveRunicView<EditorDocumentToolsViewModel>;
public sealed partial class EditorReviewView : ReactiveRunicView<EditorReviewViewModel>;
public sealed partial class EditorInterchangeView : ReactiveRunicView<EditorInterchangeViewModel>;
public sealed partial class EditorDiagnosticsView : ReactiveRunicView<EditorDiagnosticsViewModel>;
public sealed partial class EditorLocalStateView : ReactiveRunicView<EditorLocalStateViewModel>;
public sealed partial class EditorProjectView : ReactiveRunicView<EditorProjectViewModel>;
public sealed partial class EditorLibrariesView : ReactiveRunicView<EditorLibrariesViewModel>;
