using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>
/// Draft transforms of one document or one message, compiled with the rest of the saved workspace: the composer's text
/// and authoring operations, starter drafts of missing messages, quick fixes and validation. Writes nothing.
/// </summary>
internal sealed class DocumentEditService
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly DiagnosticProjector _diagnostics;
    private readonly EntryProjector _entries;

    internal DocumentEditService(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, DiagnosticProjector diagnostics, EntryProjector entries)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _diagnostics = diagnostics;
        _entries = entries;
    }

    public async Task<EditorDocumentDraft> TransformDocumentAsync(string relativePath, string content,
        string? key, string? value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            if (key is not null && value is not null)
            {
                if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
                {
                    var source = Source(path, content);
                    var nodes = Rmf2ResourceReader.Read(source, cancellationToken: cancellationToken).Nodes;
                    var workspace = _files.Rmf2Catalog();
                    var existing = nodes.FirstOrDefault(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(path, node)) == key);
                    if (existing is not null) content = StrictUtf8.GetString(Rmf2ResourceWriter.SetMessage(source, existing.Key, value));
                    else
                    {
                        var current = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
                        IReadOnlyList<string>? logical = null;
                        foreach (var file in current.Files.Where(f => f.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)))
                            foreach (var node in Rmf2ResourceReader.Read(Source(file.Path, file.Content), cancellationToken: cancellationToken).Nodes.Where(n => !n.IsGroup))
                            {
                                var candidate = workspace.LogicalPath(file.Path, node);
                                if (string.Join('_', candidate) == key) { logical = candidate; break; }
                            }
                        // A new flat identifier stays flat; only an existing source symbol supplies hierarchy.
                        var local = logical is null ? new[] { key } : workspace.LocalPath(path, logical);
                        content = StrictUtf8.GetString(Rmf2ResourceWriter.InsertMessage(source, local, value));
                    }
                }
                else if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
                    content = value.EndsWith('\n') ? value : value + "\n";
                else throw new EditorUserException(EditorNotice.Create("ui_backend_not_message_document"));
            }
            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new EditorDocumentDraft(state.Compilation.Success, content, _entries.ReadEntries(path, content, state), _diagnostics.Diagnostics(state, cancellationToken));
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationAuthoringException)
        {
            return new EditorDocumentDraft(false, content, [],
                [new EditorDiagnostic("EDITOR-TRANSFORM", "error", string.Empty, relativePath, 1, 1, 1, 1, EditorNotice.FromException(exception))]);
        }
        finally { _gate.Release(); }
    }

    public async Task<EditorDocumentDraft> ApplyAuthoringOperationAsync(string relativePath, string content,
        string key, string expectedRevision, EditorMessageOperation operation, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            var source = Source(path, content);
            if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
            {
                var workspace = _files.Rmf2Catalog();
                var node = Rmf2ResourceReader.Read(source, cancellationToken: cancellationToken).Nodes.SingleOrDefault(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(path, node)) == key)
                    ?? throw new ArgumentException("The selected message no longer exists.");
                var changed = Rmf2AuthoringService.Apply(Source(path, node.Message!), expectedRevision, Operation(operation));
                content = operation.Kind == "rename-input"
                    ? StrictUtf8.GetString(Rmf2ResourceWriter.RenameInput(source, node.Key, operation.Name!, operation.NewName!))
                    : StrictUtf8.GetString(Rmf2ResourceWriter.SetMessage(source, node.Key, StrictUtf8.GetString(changed.Bytes)));
            }
            else if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
                content = StrictUtf8.GetString(Rmf2AuthoringService.Apply(source, expectedRevision, Operation(operation)).Bytes);
            else throw new ArgumentException("Choose an MF2 message document.");
            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new(state.Compilation.Success, content, _entries.ReadEntries(path, content, state), _diagnostics.Diagnostics(state, cancellationToken));
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationAuthoringException)
        {
            return new(false, content, [], [new EditorDiagnostic("EDITOR-AUTHORING", "error", exception.Message, relativePath, 1, 1, 1, 1)]);
        }
        finally { _gate.Release(); }
    }

    private static Rmf2AuthoringOperation Operation(EditorMessageOperation operation) => new(operation.Kind, operation.VariantId, operation.Pattern,
        operation.Name, operation.NewName, operation.Function, operation.Keys, operation.Selectors);

    /// <summary>
    /// The starter text of <paramref name="key"/> in <paramref name="locale"/>, which has no translation yet: the base
    /// message's structure with empty patterns and the variants the locale needs (<see cref="EditorMessageDrafts.Starter"/>),
    /// and its composer projection. Reads the saved files; writes nothing.
    /// </summary>
    public async Task<EditorMessageDraft> CreateMessageDraftAsync(string key, string locale, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            Rmf2ProjectV5? project = state.Compilation.Project ?? ReviewCompilation(state, cancellationToken).Project;
            if (project is null)
                return new(false, key, locale, string.Empty, null, _diagnostics.Diagnostics(state, cancellationToken)
                    .Where(static diagnostic => diagnostic.Severity == "error").ToArray());
            if (!project.Locales.Any(item => item.Tag == locale) || !BaseResources(state, cancellationToken).TryGetValue(key, out Rmf2TranslationV5? baseResource))
                return new(false, key, locale, string.Empty, null, [new EditorDiagnostic("EDITOR-DRAFT", "error", string.Empty, string.Empty, 1, 1, 1, 1,
                    EditorNotice.Create("ui_backend_preview_key_missing", ("key", Rmf2MessageKeys.Paths(project).TryGetValue(key, out var logical) ? string.Join('.', logical) : key)))]);
            string text = EditorMessageDrafts.Starter(baseResource.Message, locale);
            return new(true, key, locale, text, DraftProjection(key, locale, text), [], TranslationCompiler.MaxLengthV5(baseResource.Properties));
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Applies a composer operation to the standalone text of a draft (<see cref="CreateMessageDraftAsync"/>), as
    /// <see cref="ApplyAuthoringOperationAsync"/> does to a document's message: no document, no file I/O. A stale
    /// revision or an operation the compiler refuses keeps <paramref name="text"/> and reports why.
    /// </summary>
    public static EditorMessageDraft ApplyMessageDraftOperation(string key, string locale, string text, string expectedRevision, EditorMessageOperation operation)
    {
        try
        {
            string changed = StrictUtf8.GetString(Rmf2AuthoringService.Apply(Source(DraftPath(key, locale), text), expectedRevision, Operation(operation)).Bytes);
            return new(true, key, locale, changed, DraftProjection(key, locale, changed), []);
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationAuthoringException)
        {
            return new(false, key, locale, text, DraftProjection(key, locale, text),
                [new EditorDiagnostic("EDITOR-AUTHORING", "error", exception.Message, DraftPath(key, locale), 1, 1, 1, 1, Key: key, Locale: locale)]);
        }
    }

    private static string DraftPath(string key, string locale) => locale + "/" + key + ".mf2";

    private static EditorMessageProjection DraftProjection(string key, string locale, string text) =>
        EntryProjector.EditorProjection(Rmf2AuthoringService.Project(Source(DraftPath(key, locale), text), locale));

    public async Task<EditorDocumentDraft> ApplyDiagnosticFixAsync(string relativePath, string content,
        Rmf2DiagnosticQuickFix fix, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            // A project repair (RTR0095) is re-derived from the catalog compiled with this draft.
            WorkspaceState before = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            content = StrictUtf8.GetString(Rmf2DiagnosticActions.ApplyQuickFix(Source(path, content), fix, before.Compilation).Bytes);
            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new(state.Compilation.Success, content, _entries.ReadEntries(path, content, state), _diagnostics.Diagnostics(state, cancellationToken));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return new(false, content, [], [new EditorDiagnostic("EDITOR-FIX", "error", exception.Message, relativePath, 1, 1, 1, 1)]);
        }
        finally { _gate.Release(); }
    }

    public async Task<ValidationResult> ValidateAsync(
        string relativePath,
        string content,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new ValidationResult(state.Compilation.Success, _diagnostics.Diagnostics(state, cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }
}
