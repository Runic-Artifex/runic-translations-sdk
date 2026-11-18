using System.Text;
using Runic.Translations.Tooling;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>Saves one document atomically against its expected revision, then promotes the review drafts it edited.</summary>
internal sealed class DocumentSaveService
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly DiagnosticProjector _diagnostics;
    private readonly ReviewService _review;

    internal DocumentSaveService(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, DiagnosticProjector diagnostics, ReviewService review)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _diagnostics = diagnostics;
        _review = review;
    }

    public async Task<EditorOperationResult> SaveAsync(
        string relativePath,
        string content,
        string expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        bool committed = false;
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            string fullPath = _files.ContainedPath(path);
            bool creatingMf2 = !File.Exists(fullPath) &&
                string.Equals(expectedRevision, EditorWorkspace.NewMf2DocumentRevision, StringComparison.Ordinal) &&
                IsSourcePath(path) &&
                _files.FindMf2ProjectConfig() is not null;
            if (!File.Exists(fullPath) && !creatingMf2)
                return Failure("not-found", EditorNotice.Create("ui_backend_document_missing", ("path", path)));

            if (!creatingMf2)
            {
                byte[] currentBytes = ReadSourceBytes(fullPath);
                string currentRevision = Revision(currentBytes);
                if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
                    return Failure("conflict", EditorNotice.Create("ui_backend_document_conflict", ("path", path)));
            }

            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    EditorNotice.Create("ui_backend_draft_validation"),
                    null,
                    new ValidationResult(false, _diagnostics.Diagnostics(state, cancellationToken)));
            }

            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(content);
            }
            catch (EncoderFallbackException)
            {
                return Failure("encoding", EditorNotice.Create("ui_backend_encoding"));
            }
            // Starter copies (draft review entries) this save edits become translated once it commits.
            var drafts = creatingMf2 ? null : _review.DraftsBeforeSave(state, path);

            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(fullPath)!,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
                if (!creatingMf2 && !string.Equals(Revision(ReadSourceBytes(fullPath)), expectedRevision, StringComparison.Ordinal))
                    return Failure("conflict", EditorNotice.Create("ui_backend_document_conflict", ("path", path)));
                File.Move(temporaryPath, fullPath, !creatingMf2);
                committed = true;
            }
            finally
            {
                try
                {
                    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
                }
                catch (Exception exception) when (committed && (exception is IOException or UnauthorizedAccessException))
                {
                    // The replace already committed.  A best-effort temp cleanup must
                    // not make the caller believe that its document was not saved.
                }
            }

            // The atomic replace is the point of no return.  The session records the
            // known output revision before it chooses to reload; do not reread here.
            EditorNotice? warning = null;
            if (drafts is { } edited)
            {
                try { _review.PromoteEditedDrafts(state, edited.Locale, edited.Before); }
                catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException)
                {
                    warning = EditorNotice.Create("ui_backend_review_drafts_not_updated", ("path", _review.ReviewDisplayPath() ?? "review.json")) with { Detail = exception.Message };
                }
            }
            return new EditorOperationResult(true, "saved", warning, null, null);
        }
        catch (ArgumentException exception)
        {
            return Failure("invalid-request", EditorNotice.FromException(exception));
        }
        catch (IOException exception) when (committed)
        {
            return new EditorOperationResult(true, "saved", EditorNotice.Create("ui_backend_saved_reload") with { Detail = exception.Message }, null, null);
        }
        catch (UnauthorizedAccessException exception) when (committed)
        {
            return new EditorOperationResult(true, "saved", EditorNotice.Create("ui_backend_saved_reload") with { Detail = exception.Message }, null, null);
        }
        catch (IOException exception)
        {
            return Failure("io", EditorNotice.FromException(exception));
        }
        finally
        {
            _gate.Release();
        }
    }
}
