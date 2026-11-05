using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Runic.Translations;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Editor;

internal sealed class EditorWorkspace : IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    internal const string NewMf2DocumentRevision = "new-mf2-document";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, byte> _pendingChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownRevisions = new(StringComparer.Ordinal);
    private string? _catalogId;
    private int _watcherOverflowed;
    private string? _knownReviewRevision;
    // The last compilation by the revision of its sources, so requests on unchanged sources (the preview and the
    // pseudo-locale preview of one edit, a samples change) reuse it instead of compiling again.
    private (string Revision, Rmf2ProjectCompilation Compilation)? _lastCompilation;
    // The pseudo-locale compilations of _lastCompilation's sources, by pseudo-locale.
    private readonly Dictionary<string, (string Revision, Rmf2ProjectCompilation Compilation)> _pseudoCompilations = new(StringComparer.Ordinal);
    // Guards the two caches: PlanRmf2Mutation reads the state outside _gate.
    private readonly Lock _compilationCache = new();
    private int _reconcileRequested;
    private bool _disposed;

    public EditorWorkspace(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        if (!Directory.Exists(_root))
            throw new EditorUserException(EditorNotice.Create("ui_backend_workspace_missing", ("path", _root)));
        ValidateNoReparseAncestors(_root);
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnWatcherChanged;
        _watcher.Created += OnWatcherChanged;
        _watcher.Deleted += OnWatcherChanged;
        _watcher.Renamed += OnWatcherRenamed;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    public string Root => _root;
    public string? CatalogId => _catalogId;

    public async Task<EditorExternalChanges> CheckExternalChangesAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            bool overflowed = Interlocked.Exchange(ref _watcherOverflowed, 0) != 0;
            bool reconcile = overflowed || Interlocked.Exchange(ref _reconcileRequested, 0) != 0;
            if (!reconcile && _pendingChanges.IsEmpty)
                return new EditorExternalChanges(false, [], []);

            // Take only the events observed before this scan. Events arriving
            // while the inventory is being read remain queued for the next
            // reconciliation and cannot be acknowledged by this baseline. If
            // the read fails, the taken events and flags are restored so the
            // next scan still reports them instead of silently losing them.
            var candidates = new HashSet<string>(_pendingChanges.Keys, StringComparer.Ordinal);
            foreach (string path in candidates) _pendingChanges.TryRemove(path, out _);
            Dictionary<string, byte[]> currentFiles;
            try
            {
                currentFiles = ReadCurrentTranslationFiles(cancellationToken);
            }
            catch
            {
                foreach (string path in candidates) _pendingChanges.TryAdd(path, 0);
                if (overflowed) Interlocked.Exchange(ref _watcherOverflowed, 1);
                if (reconcile) Interlocked.Exchange(ref _reconcileRequested, 1);
                throw;
            }
            Dictionary<string, string> current = currentFiles.ToDictionary(
                static pair => pair.Key,
                static pair => Revision(pair.Value),
                StringComparer.Ordinal);
            // A source event, manifest event, or directory membership event
            // all request a complete previous/current inventory comparison.
            // Comparing only queued paths loses delayed watcher events and can
            // silently acknowledge an unreported mounted add/delete/rename.
            if (reconcile || candidates.Count != 0)
            {
                candidates.UnionWith(_knownRevisions.Keys);
                candidates.UnionWith(current.Keys);
            }

            // FileSystemWatcher observes the containing workspace, so an edit in
            // an unrelated RMF2 file can be queued as well.  Only report a
            // path that is part of the previous or current configured source
            // inventory; otherwise an unrelated file would look like a deletion.
            string[] changed = candidates
                .Where(path => (_knownRevisions.ContainsKey(path) || current.ContainsKey(path)) &&
                    (!_knownRevisions.TryGetValue(path, out string? known) ||
                    !current.TryGetValue(path, out string? revision) ||
                    !string.Equals(known, revision, StringComparison.Ordinal)))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var changes = new EditorExternalFileChange[changed.Length];
            for (int index = 0; index < changed.Length; index++)
            {
                string path = changed[index];
                if (!currentFiles.TryGetValue(path, out byte[]? bytes))
                {
                    changes[index] = new EditorExternalFileChange(path, false, null, null);
                    continue;
                }
                changes[index] = new EditorExternalFileChange(path, true, StrictUtf8.GetString(bytes), Revision(bytes));
            }
            // The caller may acknowledge these changes without immediately
            // loading a full snapshot. Keep the physical inventory as the
            // baseline so a later rename/delete of a newly added mount file
            // still reports the old path as removed.
            ReplaceKnownRevisions(current);
            return new EditorExternalChanges(overflowed, changed, changes, await ExternalReviewAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<WorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            return await LoadCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // Callers that already hold _gate (commit paths) reload through this core.
    private async Task<WorkspaceSnapshot> LoadCoreAsync(CancellationToken cancellationToken)
    {
        TranslationPendingTransaction? pending = TranslationWorkspaceTransaction.GetPending(_root);
        if (pending is not null)
        {
            return new WorkspaceSnapshot(
                _root,
                null,
                [],
                [],
                [new EditorDiagnostic("RECOVERY", "error", string.Empty, string.Empty, 1, 1, 1, 1, EditorNotice.Create("ui_backend_recovery_required"))],
                false,
                new EditorPendingTransaction(pending.CatalogId, pending.Paths),
                null,
                null);
        }
        WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
        return CreateSnapshot(state);
    }

    public async Task<EditorDocumentDraft> TransformDocumentAsync(string relativePath, string content,
        string? key, string? value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            if (key is not null && value is not null)
            {
                if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
                {
                    var source = Source(path, content);
                    var nodes = Rmf2ResourceReader.Read(source, cancellationToken: cancellationToken).Nodes;
                    var workspace = Rmf2Catalog();
                    var existing = nodes.FirstOrDefault(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(path, node)) == key);
                    if (existing is not null) content = StrictUtf8.GetString(Rmf2ResourceWriter.SetMessage(source, existing.Key, value));
                    else
                    {
                        var current = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
                        IReadOnlyList<string>? logical = null;
                        foreach (var file in current.Files.Where(f => f.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)))
                            foreach (var node in Rmf2ResourceReader.Read(Source(file.Path, file.Content), cancellationToken: cancellationToken).Nodes.Where(n => !n.IsGroup))
                            {
                                var candidate = workspace.LogicalPath(file.Path, node);
                                if (string.Join('_', candidate) == key) { logical = candidate; break; }
                            }
                        // A new flat identifier stays flat; only an existing source symbol supplies hierarchy.
                        var local = logical is null ? new[] { key } : workspace.LocalPath(path, logical);
                        content = StrictUtf8.GetString(Rmf2ResourceWriter.AddMessage(source, local, value));
                    }
                }
                else if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
                    content = value.EndsWith('\n') ? value : value + "\n";
                else throw new EditorUserException(EditorNotice.Create("ui_backend_not_message_document"));
            }
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new EditorDocumentDraft(state.Compilation.Success, content, ReadEntries(path, content, state.Compilation), Diagnostics(state, cancellationToken));
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
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            var source = Source(path, content);
            if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
            {
                var workspace = Rmf2Catalog();
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
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new(state.Compilation.Success, content, ReadEntries(path, content, state.Compilation), Diagnostics(state, cancellationToken));
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationAuthoringException)
        {
            return new(false, content, [], [new EditorDiagnostic("EDITOR-AUTHORING", "error", exception.Message, relativePath, 1, 1, 1, 1)]);
        }
        finally { _gate.Release(); }
        static Rmf2AuthoringOperation Operation(EditorMessageOperation operation) => new(operation.Kind, operation.VariantId, operation.Pattern,
            operation.Name, operation.NewName, operation.Function, operation.Keys, operation.Selectors);
    }

    public async Task<EditorDocumentDraft> ApplyDiagnosticFixAsync(string relativePath, string content,
        Rmf2DiagnosticQuickFix fix, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            // A project repair (RTR0095) is re-derived from the catalog compiled with this draft.
            WorkspaceState before = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            content = StrictUtf8.GetString(Rmf2DiagnosticActions.ApplyQuickFix(Source(path, content), fix, before.Compilation).Bytes);
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new(state.Compilation.Success, content, ReadEntries(path, content, state.Compilation), Diagnostics(state, cancellationToken));
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
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            return new ValidationResult(state.Compilation.Success, Diagnostics(state, cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorMessagePreview> PreviewMessageAsync(
        string relativePath,
        string content,
        string locale,
        string key,
        string? samplesJson = null,
        string? requestedLocale = null,
        string? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            EditorDiagnostic[] diagnostics = Diagnostics(state, cancellationToken);
            if (!state.Compilation.Success)
                return new EditorMessagePreview(false, null, null, null, diagnostics);

            Rmf2ProjectV5 rmf2 = state.Compilation.Project!;
            // en-XA and ar-XB are generated from the base locale by the SDK's pseudo-locale generator, exactly as a
            // --pseudo-locales build does, also when runic.json does not configure them (the simulation toggle).
            if (!rmf2.Locales.Any(item => item.Tag == locale) && Rmf2PseudoLocalesV5.Supported.Contains(locale, StringComparer.Ordinal))
            {
                Rmf2ProjectCompilation pseudo = PseudoLocaleCompilation(state, Rmf2PseudoLocalesV5.Supported.First(tag => tag == locale), cancellationToken);
                if (!pseudo.Success || pseudo.Project is null)
                    return new EditorMessagePreview(false, null, null, null, [.. diagnostics, .. pseudo.Diagnostics
                        .Where(static item => item.Severity == TranslationDiagnosticSeverity.Error)
                        .Select(static item => new EditorDiagnostic(item.Id, "error", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn))]);
                rmf2 = pseudo.Project;
            }
            Rmf2LocaleV5? selectedLocale = rmf2.Locales.SingleOrDefault(item => item.Tag == locale);
            Rmf2TranslationV5? resource = selectedLocale?.ResolvedResources.SingleOrDefault(item => item.Key == key);
            if (resource is null)
                return MissingPreviewKey(path, locale, key);
            string ast = Rmf2MessageJsonV5.Serialize(resource.Message);
            JsonObject samples = samplesJson is null ? new JsonObject() : JsonNode.Parse(samplesJson)?.AsObject()
                ?? throw new JsonException("Preview samples must be a JSON object.");
            string? rendered = resource.Message.Inputs.All(input => samples.ContainsKey(input.Name))
                ? Rmf2Preview.Render(rmf2, key, locale, samples, requestedLocale, timeZone).ToJsonString()
                : null;
            return new EditorMessagePreview(true, locale, ast, rendered, diagnostics);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or TranslationAuthoringException or TranslationFormatException)
        {
            return new EditorMessagePreview(false, null, null, null,
                [new EditorDiagnostic("PREVIEW", "error", string.Empty, relativePath, 1, 1, 1, 1, EditorNotice.FromException(exception))]);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The state's sources compiled with the pseudo-locale generated, as --pseudo-locales builds compile them, also when
    // runic.json does not configure it. runic.json is read by the compiler as in every other compile (a BOM is skipped,
    // a duplicate member is its diagnostic), never re-serialized. One compilation per sources revision and pseudo-locale.
    private Rmf2ProjectCompilation PseudoLocaleCompilation(WorkspaceState state, string locale, CancellationToken cancellationToken)
    {
        lock (_compilationCache)
            if (_pseudoCompilations.TryGetValue(locale, out var cached) && string.Equals(cached.Revision, state.SourcesRevision, StringComparison.Ordinal))
                return cached.Compilation;
        WorkspaceFile manifest = state.Files.Single(static file => file.Kind == DocumentKind.Manifest);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray(),
            null, [locale], cancellationToken);
        lock (_compilationCache)
            // Only for the sources the cache holds; an older state's pseudo compilation is not kept.
            if (state.SourcesRevision is { } revision && string.Equals(_lastCompilation?.Revision, revision, StringComparison.Ordinal))
                _pseudoCompilations[locale] = (revision, compilation);
        return compilation;
    }

    private static EditorMessagePreview MissingPreviewKey(string path, string locale, string key) =>
        new(false, locale, null, null,
            [new EditorDiagnostic("PREVIEW", "error", string.Empty, path, 1, 1, 1, 1,
                EditorNotice.Create("ui_backend_preview_key_missing", ("key", key)))]);

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
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            string fullPath = ContainedPath(path);
            bool creatingMf2 = !File.Exists(fullPath) &&
                string.Equals(expectedRevision, NewMf2DocumentRevision, StringComparison.Ordinal) &&
                IsSourcePath(path) &&
                FindMf2ProjectConfig() is not null;
            if (!File.Exists(fullPath) && !creatingMf2)
                return Failure("not-found", EditorNotice.Create("ui_backend_document_missing", ("path", path)));

            if (!creatingMf2)
            {
                byte[] currentBytes = ReadSourceBytes(fullPath);
                string currentRevision = Revision(currentBytes);
                if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
                    return Failure("conflict", EditorNotice.Create("ui_backend_document_conflict", ("path", path)));
            }

            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    EditorNotice.Create("ui_backend_draft_validation"),
                    null,
                    new ValidationResult(false, Diagnostics(state, cancellationToken)));
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
            var drafts = creatingMf2 ? null : DraftsBeforeSave(state, path);

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
                try { PromoteEditedDrafts(state, edited.Locale, edited.Before); }
                catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException)
                {
                    warning = EditorNotice.Create("ui_backend_review_drafts_not_updated", ("path", ReviewDisplayPath() ?? "review.json")) with { Detail = exception.Message };
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

    public async Task<EditorReviewOperationResult> SaveReviewAsync(
        EditorReviewSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_catalogId is null) return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_select_catalog_review"), null, null);
            // Notes, samples and terminology always save. Review states carry the fingerprint of the source the user
            // reviewed (from the snapshot); the review compilation tells which of them predate a change on disk, and it
            // works while translations are incomplete or invalid (TranslationReviewFile.ForReview).
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            EditorReviewSnapshot saved = KnownReview(ReviewStore(_catalogId).Save(request, ReviewCompilation(state, cancellationToken)));
            return new EditorReviewOperationResult(true, null, saved, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new EditorReviewOperationResult(false, EditorNotice.FromException(exception), null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<EditorReviewOperationResult> DeleteReviewAsync(
        string? expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_catalogId is null) return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_select_catalog_review_change"), null, null);
            EditorReviewStore store = ReviewStore(_catalogId);
            if (!string.Equals(store.CurrentRevision(), expectedRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_sidecar_history_conflict"), null, null);
            cancellationToken.ThrowIfCancellationRequested();
            store.Delete(expectedRevision);
            // File.Delete is the point of no return.  Callers must reconcile their
            // history before any sidecar reload can fail or observe an external write.
            return new EditorReviewOperationResult(true, null, null, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new EditorReviewOperationResult(false, EditorNotice.FromException(exception), null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private EditorReviewStore ReviewStore(string catalogId) =>
        new(_root, FindMf2ProjectConfig() ?? throw new EditorUserException(EditorNotice.Create("ui_backend_project_required")), catalogId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _gate.Dispose();
    }

    // ---- XLIFF interchange (W03) ----
    //
    // The editor exports and imports through the same Tooling code as `runic-translations xliff export/import`:
    // TranslationInterchange (XLIFF profile v3, per-unit fingerprints), TranslationXliffImportPlan (which units are
    // added, changed, unchanged, skipped or refused), TranslationInterchangeSourceWriter (where translations go) and
    // TranslationReviewFile (the review entries after the import, in <catalog>.review.json). A unit whose source
    // changed since export is imported as needs-review, as the command line's default policy does; only document-level
    // problems (another catalog, an undeclared or the base locale, an unreadable file) refuse the whole import. The
    // editor keeps its preview/confirm step: the preview computes every write, and the commit checks that nothing
    // changed since.

    // allowRootedOutput is set only by the command line (`export --output`): a
    // rooted path there is the caller's chosen destination. The UI bridge keeps
    // every output inside the workspace, so a browser client cannot write
    // elsewhere on disk.
    internal async Task<EditorXliffExportResult> ExportXliffAsync(
        string? directory,
        bool allowRootedOutput = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return new EditorXliffExportResult(false, EditorNotice.Create("ui_backend_xliff_compile"), null, [], []);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            TranslationInterchangeReview review = ReviewStore(catalog.CatalogId).ExportReview();
            TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(state.Compilation, review);
            string targetDirectory = ResolveInterchangeOutputDirectory(directory, Path.Combine(".runic-translations", "export"), allowRootedOutput);
            Directory.CreateDirectory(targetDirectory);
            var documents = new List<EditorInterchangeFile>(export.Documents.Count);
            foreach (TranslationXliffDocument document in export.Documents)
            {
                string fullPath = Path.Combine(targetDirectory, $"{catalog.CatalogId}.{document.TargetLocale}.xliff");
                await WriteAtomicallyAsync(fullPath, document.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
                documents.Add(new EditorInterchangeFile(
                    ReportedOutputPath(fullPath), document.TargetLocale, (long)document.Bytes.Length));
            }
            return new EditorXliffExportResult(
                true,
                null,
                catalog.CatalogId,
                documents,
                export.Report.Losses.Select(static loss => new EditorInterchangeLoss(
                    loss.Code, loss.Location, loss.Message, loss.SemanticLoss)).ToArray());
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorXliffExportResult(false, InterchangeMessage(exception), null, [], []);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A copy of the SDK review file (with the editor's notes where it has none), for a reviewer outside the project.
    internal async Task<EditorReviewFileResult> ExportReviewJsonAsync(
        string? path,
        bool allowRootedOutput = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return new EditorReviewFileResult(false, EditorNotice.Create("ui_backend_review_compile"), null, 0);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            TranslationInterchangeReview review = ReviewStore(catalog.CatalogId).ExportReview();
            byte[] bytes = TranslationInterchange.ExportReviewJson(review);
            string targetPath = Path.Combine(
                ResolveInterchangeOutputDirectory(path is null ? null : Path.GetDirectoryName(path), Path.Combine(".runic-translations", "export"), allowRootedOutput),
                path is null ? $"{catalog.CatalogId}.review.json" : Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await WriteAtomicallyAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
            return new EditorReviewFileResult(
                true,
                null,
                ReportedOutputPath(targetPath),
                review.Entries.Count);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorReviewFileResult(false, InterchangeMessage(exception), null, 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<(EditorXliffImportPlan Plan, PreparedInterchangeImport? Prepared)> PreviewXliffImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return (RefuseXliff("EDITOR-COMPILE", EditorNotice.Create("ui_backend_import_compile")), null);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);

            // Document-level checks first, with the editor's own messages; units are then judged one by one.
            TranslationXliffImportResult header = TranslationInterchange.ImportXliff21(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            CollectCatalogRefusals(catalog, header, refusals);
            if (!string.Equals(header.SourceLocale, catalog.SourceLocale, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SOURCE-LOCALE-MISMATCH",
                    EditorNotice.Create("ui_backend_refusal_1", ("value1", header.SourceLocale!), ("value2", catalog.SourceLocale!))));
            EditorReviewStore store = ReviewStore(catalog.CatalogId);
            (TranslationInterchangeReview? existingReview, byte[]? existingReviewBytes, string? reviewRevision) = (null, null, null);
            try { (existingReview, existingReviewBytes, reviewRevision) = store.ReadReview(); }
            catch (Exception exception) when (exception is TranslationInterchangeException or TranslationEditorStateException)
            {
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", EditorNotice.External(exception.Message)));
            }
            if (refusals.Count > 0)
                return (new EditorXliffImportPlan(false, null, null, header.CatalogId, header.SourceLocale, header.TargetLocale, null,
                    [], 0, 0, 0, 0, 0, false, refusals.Order(InterchangeRefusalOrder.Instance).ToArray()), null);

            TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(bytes, state.Compilation,
                new TranslationXliffImportOptions { SourceChangePolicy = TranslationSourceChangePolicy.NeedsReview });
            TranslationXliffImportPlan plan = TranslationXliffImportPlan.Create(state.Compilation, import);
            var changes = new List<EditorKeyChange>();
            bool overflowed = false;
            foreach (TranslationXliffImportUnitPlan unit in plan.Units.OrderBy(static unit => unit.Key, StringComparer.Ordinal))
            {
                switch (unit.Outcome)
                {
                    case "added" or "changed":
                        Push(ref changes, ref overflowed, new EditorKeyChange(unit.Key, unit.Outcome, unit.Before, unit.After, null, null));
                        break;
                    case "refused" or "skipped":
                        // A refused unit is listed with its reason; the other units are still imported.
                        if (unit.Outcome == "refused")
                            Push(ref changes, ref overflowed, new EditorKeyChange(unit.Key, "refused", null, unit.Code + ": " + unit.Message, null, null));
                        break;
                }
            }

            // The shared Tooling writer: an existing translation changes in place, a missing one is added
            // beside its base resource (the target .rmf2 file, or the target locale's .mf2 directory).
            string configPath = FindMf2ProjectConfig()!;
            string manifestContent = state.Files.Single(file => file.Kind == DocumentKind.Manifest).Content;
            TranslationSource[] resources = state.Files
                .Where(static file => file.Kind == DocumentKind.Resource)
                .Select(static file => Source(file.Path, file.Content))
                .ToArray();
            IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(
                _root,
                Source(NormalizeRelativePath(Path.GetRelativePath(_root, configPath)), manifestContent),
                resources,
                state.Compilation,
                import.TargetLocale!,
                plan.Values,
                cancellationToken);
            var documents = edits.Select(edit =>
            {
                WorkspaceFile? original = state.Files.FirstOrDefault(file => file.Kind == DocumentKind.Resource && string.Equals(file.Path, edit.Path, StringComparison.Ordinal));
                return new PreparedInterchangeDocument(edit.Path, original?.Revision, original is null ? null : StrictUtf8.GetBytes(original.Content), edit.Bytes);
            }).ToList();

            Rmf2ProjectCompilation proposed = CompileWithInterchangeDocuments(state.Files, documents, cancellationToken);
            if (!proposed.Success)
            {
                string message = string.Join(" ", proposed.Diagnostics
                    .Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error)
                    .Select(static diagnostic => $"[{diagnostic.Id}] {diagnostic.Message}"));
                return (RefuseXliff("EDITOR-MF2-IMPORT", EditorNotice.External(message)), null);
            }

            // The review file after the import, as xliff import writes it.
            (TranslationInterchangeReview merged, bool reviewChanged) = TranslationReviewFile.MergeImport(existingReview, catalog.CatalogId, import, plan);
            var before = (existingReview?.Entries ?? []).ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
            int reviewUpdates = 0;
            foreach (TranslationInterchangeReviewEntry entry in merged.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                before.TryGetValue(Identity(entry.Key, entry.Locale), out TranslationInterchangeReviewEntry? existing);
                if (existing is not null && existing.State == entry.State) continue;
                reviewUpdates += 1;
                Push(ref changes, ref overflowed, new EditorKeyChange(entry.Key, "state-change", null, null,
                    existing is null ? null : TranslationReviewFile.StateText(existing.State), TranslationReviewFile.StateText(entry.State)));
            }
            byte[]? reviewBytes = reviewChanged ? TranslationReviewFile.Render(merged) : null;
            var prepared = new PreparedInterchangeImport(
                ResolveImportSourcePath(path),
                SHA256.HashData(bytes),
                import.CatalogId,
                catalog.SourceFreshness,
                documents,
                reviewBytes,
                reviewRevision);
            return (new EditorXliffImportPlan(true, null, null, import.CatalogId, import.SourceLocale, import.TargetLocale, null,
                changes.ToArray(), plan.Count("added"), plan.Count("changed"), 0, plan.Count("unchanged"), reviewUpdates, overflowed, []),
                prepared);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (RefuseXliff(InterchangeCode(exception), InterchangeMessage(exception)), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<EditorOperationResult> CommitXliffImportAsync(
        PreparedInterchangeImport prepared,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var staged = new List<(PreparedInterchangeDocument Document, string FullPath, string TemporaryPath)>();
        var committed = new List<(PreparedInterchangeDocument Document, string FullPath)>();
        try
        {
            ThrowIfDisposed();
            byte[] source = await ReadImportSourceAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return Failure("import-file-changed", EditorNotice.Create("ui_backend_import_file_changed"));
            WorkspaceState currentState = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success)
                return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
            TranslationInterchangeProjection currentCatalog = TranslationInterchange.PreflightXliff21(currentState.Compilation);
            if (!string.Equals(currentCatalog.CatalogId, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentCatalog.SourceFreshness, prepared.ExpectedSourceFreshness, StringComparison.Ordinal))
                return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
            EditorReviewStore store = ReviewStore(prepared.CatalogId);
            (string? reviewRevision, byte[]? reviewBytes) = store.CurrentState();
            if (!string.Equals(reviewRevision, prepared.ExpectedReviewRevision, StringComparison.Ordinal))
                return Failure("conflict", EditorNotice.Create("ui_backend_import_sidecar_changed"));

            foreach (PreparedInterchangeDocument document in prepared.Documents)
            {
                string fullPath = ContainedPath(document.Path);
                string? currentRevision = File.Exists(fullPath)
                    ? Revision(ReadSourceBytes(fullPath))
                    : null;
                if (!string.Equals(currentRevision, document.ExpectedRevision, StringComparison.Ordinal))
                    return Failure("conflict", EditorNotice.Create("ui_backend_import_document_conflict", ("path", document.Path)));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                string temporaryPath = Path.Combine(
                    Path.GetDirectoryName(fullPath)!,
                    $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
                await File.WriteAllBytesAsync(temporaryPath, document.Bytes, cancellationToken).ConfigureAwait(false);
                staged.Add((document, fullPath, temporaryPath));
            }

            try
            {
                foreach ((PreparedInterchangeDocument document, string fullPath, string temporaryPath) in staged)
                {
                    File.Move(temporaryPath, fullPath, true);
                    committed.Add((document, fullPath));
                }
                // Compare-and-swap: a review file another writer changed since the check is not overwritten.
                if (prepared.ReviewBytes is not null) store.WriteReview(prepared.ReviewBytes, reviewBytes);
            }
            catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or IOException or UnauthorizedAccessException)
            {
                bool rolledBack = await RollBackInterchangeDocumentsAsync(committed, CancellationToken.None).ConfigureAwait(false);
                return rolledBack
                    ? Failure("io", EditorNotice.Create("ui_backend_import_not_applied") with { Detail = exception.Message })
                    : new EditorOperationResult(false, "partial-commit",
                        EditorNotice.Create("ui_backend_import_partial") with { Detail = exception.Message },
                        null, null);
            }
            WorkspaceSnapshot snapshot = await LoadCoreAsync(CancellationToken.None).ConfigureAwait(false);
            return new EditorOperationResult(snapshot.Success, "imported",
                snapshot.Success ? null : EditorNotice.External(string.Join(" ", snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message))),
                snapshot.Success ? snapshot : null, null);
        }
        catch (TranslationInterchangeException)
        {
            return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Failure("io", EditorNotice.FromException(exception));
        }
        finally
        {
            foreach ((_, _, string temporaryPath) in staged)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            _gate.Release();
        }
    }

    // A review file from elsewhere (another checkout, a reviewer) merged into the project's review file: its entries
    // replace those with the same key and locale and keep their fingerprints, so an approval made for an older source
    // shows as stale (RTR0090) instead of being refused.
    internal async Task<(EditorReviewImportPlan Plan, PreparedInterchangeImport? Prepared)> PreviewReviewJsonImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return (RefuseReview("EDITOR-COMPILE", EditorNotice.Create("ui_backend_import_compile")), null);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);
            TranslationInterchangeReview import = TranslationInterchange.ImportReviewJson(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            if (!string.Equals(import.CatalogId, catalog.CatalogId, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                    EditorNotice.Create("ui_backend_refusal_3", ("value1", import.CatalogId!), ("value2", catalog.CatalogId!))));
            var canonicalKeys = new HashSet<string>(catalog.CanonicalUnits.Select(static value => value.Key), StringComparer.Ordinal);
            var localeTags = new HashSet<string>(catalog.Locales.Select(static value => value.Tag), StringComparer.Ordinal);
            foreach (TranslationInterchangeReviewEntry entry in import.Entries)
            {
                if (!canonicalKeys.Contains(entry.Key))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-KEY-NOT-IN-CATALOG", EditorNotice.Create("ui_backend_refusal_4", ("value1", entry.Key!), ("value2", catalog.CatalogId!))));
                if (!localeTags.Contains(entry.Locale))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG", EditorNotice.Create("ui_backend_refusal_5", ("value1", entry.Locale!))));
            }
            EditorReviewStore store = ReviewStore(catalog.CatalogId);
            (TranslationInterchangeReview? existing, byte[]? existingBytes, string? revision) = (null, null, null);
            try { (existing, existingBytes, revision) = store.ReadReview(); }
            catch (Exception exception) when (exception is TranslationInterchangeException or TranslationEditorStateException)
            {
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", EditorNotice.External(exception.Message)));
            }
            if (refusals.Count > 0)
                return (new EditorReviewImportPlan(false, null, null, import.CatalogId, [], 0, 0, 0, false,
                    refusals.Order(InterchangeRefusalOrder.Instance).ToArray()), null);

            var changes = new List<EditorReviewChange>();
            bool overflowed = false;
            int added = 0, changed = 0;
            Dictionary<string, TranslationInterchangeReviewEntry> merged = (existing?.Entries ?? [])
                .ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
            foreach (TranslationInterchangeReviewEntry entry in import.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                merged.TryGetValue(Identity(entry.Key, entry.Locale), out TranslationInterchangeReviewEntry? current);
                if (current is null)
                {
                    added += 1;
                    Push(ref changes, ref overflowed, new EditorReviewChange(entry.Key, entry.Locale, "added", null, TranslationReviewFile.StateText(entry.State)));
                }
                else if (current.State != entry.State || current.Note != entry.Note || current.SourceFingerprint != entry.SourceFingerprint)
                {
                    changed += 1;
                    Push(ref changes, ref overflowed, new EditorReviewChange(entry.Key, entry.Locale, "changed", TranslationReviewFile.StateText(current.State), TranslationReviewFile.StateText(entry.State)));
                }
                merged[Identity(entry.Key, entry.Locale)] = entry;
            }
            byte[] reviewBytes = TranslationReviewFile.Render(new TranslationInterchangeReview(catalog.CatalogId, merged.Values));
            var prepared = new PreparedInterchangeImport(
                ResolveImportSourcePath(path),
                SHA256.HashData(bytes),
                catalog.CatalogId,
                catalog.SourceFreshness,
                [],
                existingBytes is not null && existingBytes.AsSpan().SequenceEqual(reviewBytes) ? null : reviewBytes,
                revision);
            return (new EditorReviewImportPlan(true, null, null, catalog.CatalogId, changes.ToArray(), added, changed, 0, overflowed, []), prepared);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return (RefuseReview(InterchangeCode(exception), InterchangeMessage(exception)), null);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<EditorReviewOperationResult> CommitReviewImportAsync(
        PreparedInterchangeImport prepared,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            byte[] source = await ReadImportSourceAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_review_file_changed"), null, null);
            WorkspaceState currentState = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success)
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
            TranslationInterchangeProjection currentCatalog = TranslationInterchange.PreflightXliff21(currentState.Compilation);
            if (!string.Equals(currentCatalog.CatalogId, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentCatalog.SourceFreshness, prepared.ExpectedSourceFreshness, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
            EditorReviewStore store = ReviewStore(prepared.CatalogId);
            (string? reviewRevision, byte[]? reviewBytes) = store.CurrentState();
            if (!string.Equals(reviewRevision, prepared.ExpectedReviewRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_sidecar_changed"), null, null);
            if (prepared.ReviewBytes is not null) store.WriteReview(prepared.ReviewBytes, reviewBytes);
            return new EditorReviewOperationResult(true, null, KnownReview(store.Load(currentState.Compilation)), null);
        }
        catch (TranslationInterchangeException exception) when (exception.Code == TranslationReviewFile.ChangedCode)
        {
            return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_sidecar_changed"), null, null);
        }
        catch (TranslationInterchangeException)
        {
            return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorReviewOperationResult(false, EditorNotice.FromException(exception), null, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private const int MaximumDiffRows = 256;

    private static void Push(ref List<EditorKeyChange> changes, ref bool overflow, EditorKeyChange change)
    {
        if (changes.Count < MaximumDiffRows) changes.Add(change);
        else overflow = true;
    }

    private static void Push(ref List<EditorReviewChange> changes, ref bool overflow, EditorReviewChange change)
    {
        if (changes.Count < MaximumDiffRows) changes.Add(change);
        else overflow = true;
    }

    private static string Identity(string key, string locale) => key + "\0" + locale;

    private static EditorXliffImportPlan RefuseXliff(string code, EditorNotice message) =>
        new(false, message, null, null, null, null, null, [], 0, 0, 0, 0, 0, false, [new EditorInterchangeRefusal(code, message)]);

    private static EditorReviewImportPlan RefuseReview(string code, EditorNotice message) =>
        new(false, message, null, null, [], 0, 0, 0, false, [new EditorInterchangeRefusal(code, message)]);

    private static string InterchangeCode(Exception exception) => exception is TranslationInterchangeException interchange
        ? interchange.Code
        : exception is TranslationAuthoringException ? "EDITOR-AUTHORING" : "EDITOR-IO";

    private static EditorNotice InterchangeMessage(Exception exception) => EditorNotice.FromException(exception);

    private sealed class InterchangeRefusalOrder : IComparer<EditorInterchangeRefusal>
    {
        internal static readonly InterchangeRefusalOrder Instance = new();
        public int Compare(EditorInterchangeRefusal? left, EditorInterchangeRefusal? right) =>
            string.CompareOrdinal(left?.Code, right?.Code) is var byCode && byCode != 0 ? byCode : string.CompareOrdinal(left?.Message.ToString(), right?.Message.ToString());
    }

    private Task<byte[]> ReadImportSourceAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        string fullPath = ResolveImportSourcePath(path);
        return Task.FromResult(ReadSourceBytes(fullPath));
    }

    private string ResolveImportSourcePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : ContainedPath(NormalizeRelativePath(path));
    }

    private string ResolveInterchangeOutputDirectory(string? directory, string defaultRelative, bool allowRootedOutput)
    {
        if (directory is null or "") return ContainedPath(defaultRelative);
        if (allowRootedOutput && Path.IsPathRooted(directory)) return Path.GetFullPath(directory);
        return ContainedPath(NormalizeRelativePath(directory));
    }

    // Exported files inside the workspace are reported relative to it, as
    // before; a rooted command-line output outside it is reported as given.
    private string ReportedOutputPath(string fullPath) =>
        IsWithinSourceRoot(_root, fullPath) ? Path.GetRelativePath(_root, fullPath).Replace('\\', '/') : fullPath;

    // The base-locale value of every key as the editor's frontend fingerprinted it before 0.9: an .rmf2 entry's
    // message, a direct .mf2 file's content. Used only to move version 1 editor approvals (EditorReviewStore).
    // The compilation review data is computed from: the state's own when it succeeded, else the same sources compiled
    // without validation policies, else the base locale alone (TranslationReviewFile.ForReview). Cached per state.
    private static Rmf2ProjectCompilation ReviewCompilation(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        if (state.ReviewCompilation is { } cached) return cached;
        WorkspaceFile? manifest = state.Files.Find(static file => file.Kind == DocumentKind.Manifest);
        Rmf2ProjectCompilation review = manifest is null ? state.Compilation : TranslationReviewFile.ForReview(state.Compilation, Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray(), null, cancellationToken);
        state.ReviewCompilation = review;
        return review;
    }

    // The review data when another writer (runic-translations review mark or xliff import, the language server's
    // "Mark reviewed") changed the review file since the frontend received it; null when unchanged. Read without
    // moving the external-change baseline, so source changes are still reported on their own.
    private async Task<EditorReviewSnapshot?> ExternalReviewAsync(CancellationToken cancellationToken)
    {
        if (_catalogId is null || FindMf2ProjectConfig() is not { } projectPath) return null;
        var store = new EditorReviewStore(_root, projectPath, _catalogId);
        if (string.Equals(store.CurrentRevision(), _knownReviewRevision, StringComparison.Ordinal)) return null;
        try
        {
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken, resetBaseline: false).ConfigureAwait(false);
            return KnownReview(store.Load(ReviewCompilation(state, cancellationToken)));
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or EditorUserException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Remembers the review revision the frontend has, so the external-change poll reports another writer's change.
    private EditorReviewSnapshot KnownReview(EditorReviewSnapshot snapshot)
    {
        _knownReviewRevision = snapshot.Revision;
        return snapshot;
    }

    private static Dictionary<string, string> BaseValues(WorkspaceState state, Rmf2ProjectCompilation reviewCompilation)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (reviewCompilation.Project is not { } project) return values;
        var files = state.Files.Where(static file => file.Kind == DocumentKind.Resource).ToDictionary(static file => file.Path, StringComparer.Ordinal);
        var nodes = new Dictionary<string, IReadOnlyList<Rmf2ResourceNode>>(StringComparer.Ordinal);
        foreach (Rmf2TranslationV5 resource in project.Locales.Single(locale => locale.Tag == project.DefaultLocale).DirectResources)
        {
            if (!files.TryGetValue(resource.SourceLocation.Path, out WorkspaceFile? file)) continue;
            if (file.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)) { values[resource.Key] = file.Content; continue; }
            if (!nodes.TryGetValue(file.Path, out IReadOnlyList<Rmf2ResourceNode>? parsed))
                nodes[file.Path] = parsed = Rmf2ResourceReader.Read(Source(file.Path, file.Content)).Nodes;
            if (parsed.FirstOrDefault(node => !node.IsGroup && node.NameLocation.StartByte == resource.SourceLocation.StartByte) is { Message: { } message })
                values[resource.Key] = message;
        }
        return values;
    }

    /// <summary>
    /// The editor's former review fingerprint (<c>sourceFingerprint</c> in the pre-0.9 review-model.ts): FNV-1a 64 over the
    /// UTF-16 code units of <c>JSON.stringify(value)</c>. Read only when version 1 editor approvals move to the review file.
    /// </summary>
    internal static string EditorSourceFingerprint(string value)
    {
        var json = new StringBuilder(value.Length + 2).Append('"');
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"': json.Append("\\\""); break;
                case '\\': json.Append("\\\\"); break;
                case '\b': json.Append("\\b"); break;
                case '\f': json.Append("\\f"); break;
                case '\n': json.Append("\\n"); break;
                case '\r': json.Append("\\r"); break;
                case '\t': json.Append("\\t"); break;
                default:
                    if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) json.Append(character).Append(value[++index]);
                    else if (character < 0x20 || char.IsSurrogate(character)) json.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else json.Append(character);
                    break;
            }
        }
        json.Append('"');
        ulong hash = 0xcbf29ce484222325;
        foreach (char unit in json.ToString()) hash = unchecked((hash ^ unit) * 0x100000001b3);
        return "fnv1a64:" + hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    private static Rmf2ProjectCompilation CompileWithInterchangeDocuments(
        IReadOnlyList<WorkspaceFile> files,
        IReadOnlyList<PreparedInterchangeDocument> documents,
        CancellationToken cancellationToken)
    {
        WorkspaceFile project = files.Single(static file => file.Kind == DocumentKind.Manifest);
        var messages = files
            .Where(static file => file.Kind == DocumentKind.Resource)
            .ToDictionary(static file => file.Path, static file => file.Content, StringComparer.Ordinal);
        foreach (PreparedInterchangeDocument document in documents)
            messages[document.Path] = StrictUtf8.GetString(document.Bytes);
        return TranslationCompiler.CompileRmf2ProjectV5(
            Source(project.Path, project.Content),
            messages.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => Source(pair.Key, pair.Value)),
            null,
            cancellationToken);
    }

    private static async Task<bool> RollBackInterchangeDocumentsAsync(
        List<(PreparedInterchangeDocument Document, string FullPath)> committed,
        CancellationToken cancellationToken)
    {
        bool succeeded = true;
        for (int index = committed.Count - 1; index >= 0; index--)
        {
            (PreparedInterchangeDocument document, string fullPath) = committed[index];
            try
            {
                if (document.OriginalBytes is null) File.Delete(fullPath);
                else await WriteAtomicallyAsync(fullPath, document.OriginalBytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                succeeded = false;
            }
        }
        return succeeded;
    }

    private static void CollectCatalogRefusals(
        TranslationInterchangeProjection catalog,
        TranslationXliffImportResult import,
        List<EditorInterchangeRefusal> refusals)
    {
        if (!string.Equals(import.CatalogId, catalog.CatalogId, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                EditorNotice.Create("ui_backend_refusal_7", ("value1", import.CatalogId!), ("value2", catalog.CatalogId!))));
        if (!catalog.Locales.Any(locale => string.Equals(locale.Tag, import.TargetLocale, StringComparison.Ordinal)))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG",
                EditorNotice.Create("ui_backend_refusal_8", ("value1", import.TargetLocale!))));
        if (string.Equals(import.TargetLocale, catalog.SourceLocale, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-TARGET-DEFAULT-LOCALE",
                EditorNotice.Create("ui_backend_refusal_9", ("value1", catalog.SourceLocale!))));
    }

    private static void Flatten(JsonElement node, string prefix, Dictionary<string, string> values)
    {
        foreach (System.Text.Json.JsonProperty property in node.EnumerateObject())
        {
            string key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[key] = property.Value.GetString() ?? string.Empty;
                continue;
            }
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (property.Value.TryGetProperty("$value", out JsonElement leaf) && leaf.ValueKind == JsonValueKind.String)
                {
                    values[key] = leaf.GetString() ?? string.Empty;
                    continue;
                }
                Flatten(property.Value, key, values);
            }
        }
    }

    private static async Task WriteAtomicallyAsync(string fullPath, byte[] bytes, CancellationToken cancellationToken)
    {
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best-effort cleanup must not mask the committed write.
            }
        }
    }

    // A read of the files on disk (no replacement) becomes the baseline of the external-change check, unless
    // resetBaseline is false (the check itself reads the catalog to report a review file another writer changed).
    private Task<WorkspaceState> ReadStateAsync(
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken,
        bool resetBaseline = true)
    {
        string? projectConfig = FindMf2ProjectConfig();
        if (projectConfig is null)
            throw new EditorUserException(EditorNotice.Create("ui_backend_project_required"));
        return ReadMf2StateAsync(projectConfig, replacementPath, replacementContent, cancellationToken, resetBaseline);
    }

    private Task<WorkspaceState> ReadMf2StateAsync(
        string configPath,
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken,
        bool resetBaseline = true)
    {
        string projectRoot = Path.GetDirectoryName(configPath)!;
        string configRelativePath = NormalizeRelativePath(Path.GetRelativePath(_root, configPath));
        var paths = new List<string> { configPath };
        // A malformed or half-saved manifest contributes no source roots. The
        // compiler then reports the located configuration diagnostic, and the
        // next load after the manifest is fixed discovers the sources again.
        TranslationManifestLayout layout = TranslationManifestReader.Read(
            replacementPath == configRelativePath && replacementContent is not null ? StrictUtf8.GetBytes(replacementContent) : File.ReadAllBytes(configPath),
            projectRoot);
        var discoveryErrors = new List<string>();
        IReadOnlyList<string> sourceRoots = UsableSourceRoots(layout.SourceRoots, discoveryErrors);
        foreach (string sourceRoot in sourceRoots)
        {
            paths.AddRange(EnumerateSourceFiles(sourceRoot, ".mf2").Order(StringComparer.Ordinal));
            paths.AddRange(EnumerateSourceFiles(sourceRoot, ".rmf2").Order(StringComparer.Ordinal));
        }
        var files = new List<WorkspaceFile>(paths.Count);
        TranslationSource? projectSource = null;
        var messageSources = new List<TranslationSource>();
        string? catalogId = null;
        foreach (string fullPath in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
            byte[] bytes = ReadSourceBytes(ContainedPath(relativePath));
            string content = StrictUtf8.GetString(bytes);
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                content = replacementContent ?? string.Empty;
            bool isProject = string.Equals(relativePath, configRelativePath, StringComparison.Ordinal);
            if (isProject)
            {
                catalogId = ReadCatalogId(content);
                projectSource = Source(relativePath, content);
                files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), DocumentKind.Manifest, catalogId, null, null));
            }
            else
            {
                string? locale = SourceLocale(fullPath, sourceRoots);
                messageSources.Add(Source(relativePath, content));
                files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), DocumentKind.Resource, catalogId, locale, "base"));
            }
        }
        if (projectSource is null) throw new EditorUserException(EditorNotice.Create("ui_backend_project_config_missing"));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
        {
            string replacementFullPath = ContainedPath(replacementPath);
            if (!sourceRoots.Any(root => IsWithinSourceRoot(root, replacementFullPath)) ||
                !IsSourcePath(replacementPath))
                throw new EditorUserException(EditorNotice.Create("ui_backend_source_outside_project", ("path", replacementPath)));
            string? locale = SourceLocale(replacementFullPath, sourceRoots);
            string content = replacementContent ?? string.Empty;
            messageSources.Add(Source(replacementPath, content));
            files.Add(new WorkspaceFile(replacementPath, content, NewMf2DocumentRevision, DocumentKind.Resource, catalogId, locale, "base"));
            files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        }

        string sourcesRevision = SourcesRevision(projectSource, messageSources);
        Rmf2ProjectCompilation? compiled = null;
        lock (_compilationCache)
            if (_lastCompilation is { } last && string.Equals(last.Revision, sourcesRevision, StringComparison.Ordinal)) compiled = last.Compilation;
        if (compiled is null)
        {
            compiled = TranslationCompiler.CompileRmf2ProjectV5(projectSource, messageSources, null, cancellationToken);
            lock (_compilationCache)
            {
                _lastCompilation = (sourcesRevision, compiled);
                _pseudoCompilations.Clear();
            }
        }
        Rmf2ProjectCompilation compilation = TranslationManifestReader.WithManifestErrors(compiled, projectSource.Path, layout.Error, discoveryErrors);
        Rmf2ProjectV5? rmf2 = compilation.Project;
        (string Tag, string? Fallback)[] compiledLocales = rmf2?.Locales.Select(static locale => (locale.Tag, locale.FallbackTag)).ToArray() ?? [];
        if (compiledLocales.Length != 0)
        {
            for (int index = 0; index < files.Count; index++)
            {
                WorkspaceFile file = files[index];
                string? canonicalLocale = compiledLocales.FirstOrDefault(locale =>
                    string.Equals(locale.Tag, file.Locale, StringComparison.OrdinalIgnoreCase)).Tag;
                if (canonicalLocale is not null) files[index] = file with { Locale = canonicalLocale };
            }
        }
        _catalogId = rmf2?.Id ?? catalogId;
        if (replacementPath is null && resetBaseline)
        {
            _knownRevisions.Clear();
            foreach (WorkspaceFile file in files) _knownRevisions[file.Path] = file.Revision;
            _pendingChanges.Clear();
            Interlocked.Exchange(ref _watcherOverflowed, 0);
        }
        int errors = compilation.Diagnostics.Count(item => item.Severity == TranslationDiagnosticSeverity.Error);
        var summaries = new[]
        {
            new EditorCatalogSummary(
                _catalogId ?? string.Empty,
                new[] { configRelativePath },
                messageSources.Count,
                compiledLocales.Length,
                rmf2?.CanonicalMessages.Count ?? 0,
                errors,
                compilation.Diagnostics.Count - errors,
                compilation.Success),
        };
        return Task.FromResult(new WorkspaceState(files, compilation, summaries) { SourcesRevision = sourcesRevision });
    }

    // The revision of everything a compile reads: runic.json and every message source, by path and content.
    private static string SourcesRevision(TranslationSource project, IEnumerable<TranslationSource> messages)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[8];
        foreach (TranslationSource source in messages.Prepend(project))
            foreach (byte[] part in (byte[][])[StrictUtf8.GetBytes(source.Path), source.GetUtf8Bytes()])
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, part.Length);
                hash.AppendData(length);
                hash.AppendData(part);
            }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = _catalogId is null
            ? null
            : state.Files.Find(file => file.Kind == DocumentKind.Manifest && string.Equals(file.CatalogId, _catalogId, StringComparison.Ordinal));
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);
        if (catalog is not null && state.Compilation.Project is { } rmf2)
        {
            catalog = catalog with
            {
                DefaultLocale = rmf2.DefaultLocale,
                Locales = rmf2.Locales.Select(static locale => new EditorLocale(locale.Tag, locale.FallbackTag)).ToArray(),
            };
        }

        var documents = new List<EditorDocument>(state.Files.Count);
        foreach (WorkspaceFile file in state.Files)
        {
            bool malformed = file.Kind == DocumentKind.Resource && state.Compilation.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == TranslationDiagnosticSeverity.Error &&
                string.Equals(diagnostic.Location.Path, file.Path, StringComparison.Ordinal));
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                malformed,
                file.Locale,
                file.Layer,
                ReadEntries(file.Path, file.Content, state.Compilation)));
        }
        EditorReviewSnapshot? review = null;
        if (_catalogId is not null)
        {
            EditorReviewStore store = ReviewStore(_catalogId);
            Rmf2ProjectCompilation reviewCompilation = ReviewCompilation(state);
            try { review = KnownReview(store.Load(reviewCompilation, BaseValues(state, reviewCompilation))); }
            catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or IOException or UnauthorizedAccessException)
            {
                review = new EditorReviewSnapshot(store.DisplayPath, null, exception.Message, [], []);
            }
        }
        return new WorkspaceSnapshot(_root, catalog, state.Catalogs, documents, Diagnostics(state), state.Compilation.Success, null, review, null, Status(state));
    }

    // The status report of runic-translations status, from the same compilation (relaxed while translations are
    // incomplete) and review file. The editor has no --min-coverage thresholds.
    private TranslationsStatusProject? Status(WorkspaceState state)
    {
        WorkspaceFile? manifest = state.Files.Find(static file => file.Kind == DocumentKind.Manifest);
        if (manifest is null || _catalogId is null) return null;
        Rmf2ProjectCompilation? counted = TranslationStatus.ForStatus(state.Compilation, Source(manifest.Path, manifest.Content),
            state.Files.Where(static file => file.Kind == DocumentKind.Resource).Select(static file => Source(file.Path, file.Content)).ToArray());
        if (counted is null) return null;
        string reviewPath = TranslationReviewFile.PathFor(ContainedPath(manifest.Path), _catalogId);
        TranslationInterchangeReview? review = null;
        try { review = TranslationReviewFile.Read(reviewPath, _catalogId); }
        catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException) { /* The review snapshot reports it. */ }
        return TranslationStatus.Report(manifest.Path, counted, review, review is null ? null : ReportedOutputPath(reviewPath), new Dictionary<string, double>());
    }

    // The compiler's diagnostics and, with a review file, RTR0090 for stale review entries (or RTR0091 when the file
    // cannot be read), each attached to its message row where the check names one: the per-message checks carry their
    // key and locale; any other diagnostic at a message name belongs to that message. Quick fixes are the compiler's
    // document repairs (RTR0050, RTR0078) and project repairs (RTR0095: add the missing plural variants).
    private EditorDiagnostic[] Diagnostics(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var compilation = state.Compilation;
        List<TranslationDiagnostic> diagnostics = [.. compilation.Diagnostics];
        // RTR0090 and the message rows come from the review compilation, so they stay while translations are incomplete.
        Rmf2ProjectCompilation reviewCompilation = ReviewCompilation(state, cancellationToken);
        if (reviewCompilation.Success && _catalogId is not null && FindMf2ProjectConfig() is { } projectPath)
        {
            EditorReviewStore store = new(_root, projectPath, _catalogId);
            try
            {
                if (store.ReadReview().Review is { } review) diagnostics.AddRange(TranslationReviewFile.StaleDiagnostics(reviewCompilation, review));
            }
            catch (TranslationInterchangeException exception) { diagnostics.Add(TranslationReviewFile.InvalidDiagnostic(store.DisplayPath, exception)); }
            catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException) { /* The review snapshot reports it. */ }
        }
        var rows = new Dictionary<(string Path, int Start), (string Key, string Locale)>();
        foreach (Rmf2LocaleV5 locale in (compilation.Project ?? reviewCompilation.Project)?.Locales ?? [])
            foreach (Rmf2TranslationV5 resource in locale.DirectResources)
                rows.TryAdd((resource.SourceLocation.Path, resource.SourceLocation.StartByte), (resource.Key, locale.Tag));
        var sources = state.Files.ToDictionary(file => file.Path, file => Source(file.Path, file.Content), StringComparer.Ordinal);
        var sourceFixes = new Dictionary<string, IReadOnlyList<Rmf2DiagnosticQuickFix>>(StringComparer.Ordinal);
        var sourceRevisions = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new EditorDiagnostic[diagnostics.Count];
        for (int index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TranslationDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            sources.TryGetValue(location.Path, out TranslationSource? source);
            Rmf2DiagnosticSpan? span = null;
            IReadOnlyList<Rmf2DiagnosticQuickFix> fixes = [];
            if (source is not null)
            {
                try
                {
                    span = Rmf2DiagnosticActions.GetSpan(source, location);
                    if (!sourceRevisions.ContainsKey(source.Path)) sourceRevisions[source.Path] = Rmf2DiagnosticActions.Revision(source);
                    bool documentFix = diagnostic.Id == "RTR0050" && source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
                    if (documentFix || diagnostic.Id == "RTR0095")
                    {
                        if (!sourceFixes.TryGetValue(source.Path, out var available))
                        {
                            IEnumerable<Rmf2DiagnosticQuickFix> all = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)
                                ? Rmf2DiagnosticActions.GetQuickFixes(Rmf2ResourceReader.Analyze(source, cancellationToken: cancellationToken), cancellationToken) : [];
                            sourceFixes[source.Path] = available = all.Concat(Rmf2DiagnosticActions.GetProjectQuickFixes(compilation, source, cancellationToken)).ToArray();
                        }
                        fixes = available.Where(fix => fix.DiagnosticId == diagnostic.Id && fix.Location.Path == location.Path && fix.Location.StartByte == location.StartByte && fix.Location.LengthBytes == location.LengthBytes).ToArray();
                    }
                }
                catch (ArgumentException) { /* Project-level diagnostics may not address a complete physical slice. */ }
            }
            (string Key, string Locale)? row = diagnostic.MessageKey is { } key && diagnostic.Locale is { } tag ? (key, tag)
                : rows.TryGetValue((location.Path, location.StartByte), out var named) ? named : null;
            result[index] = new EditorDiagnostic(
                diagnostic.Id,
                diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning",
                diagnostic.Message,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn, null, span, source is null ? null : sourceRevisions.GetValueOrDefault(source.Path), fixes,
                row?.Key, row?.Locale);
        }
        return result;
    }

    private static ValidationResult MalformedValidation(string path) => new(
        false,
        [new EditorDiagnostic("JSON", "error", string.Empty, path, 1, 1, 1, 1, EditorNotice.Create("ui_backend_invalid_json"))]);

    private static EditorCatalog? ReadCatalog(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutBom(content));
            JsonElement root = document.RootElement;
            string id = root.GetProperty("catalog").GetString() ?? string.Empty;
            int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            string defaultLocale = root.GetProperty("baseLocale").GetString() ?? string.Empty;
            var locales = new List<EditorLocale>();
            if (root.TryGetProperty("locales", out JsonElement localeValues))
            {
                foreach (JsonElement locale in localeValues.EnumerateArray())
                {
                    if (locale.ValueKind == JsonValueKind.String)
                    {
                        string tag = locale.GetString() ?? string.Empty;
                        locales.Add(new EditorLocale(tag, string.Equals(tag, defaultLocale, StringComparison.OrdinalIgnoreCase) ? null : defaultLocale));
                    }
                    else
                    {
                        locales.Add(new EditorLocale(
                            locale.GetProperty("tag").GetString() ?? string.Empty,
                            locale.TryGetProperty("fallback", out JsonElement fallback) ? fallback.GetString() : null));
                    }
                }
            }
            if (locales.Count == 0 && defaultLocale.Length != 0) locales.Add(new EditorLocale(defaultLocale, null));
            string[] pseudoLocales = root.TryGetProperty("pseudoLocales", out JsonElement pseudo) && pseudo.ValueKind == JsonValueKind.Array
                ? Rmf2PseudoLocalesV5.Supported.Where(tag => pseudo.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String &&
                    string.Equals(item.GetString(), tag, StringComparison.OrdinalIgnoreCase))).ToArray()
                : [];
            return new EditorCatalog(id, schemaVersion, defaultLocale, locales, [new EditorLayer("base", 0)], pseudoLocales.Length == 0 ? null : pseudoLocales);
        }
        // A manifest with a catalog but missing or mistyped members is shown
        // without catalog metadata; the compiler diagnostics explain why.
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ReadCatalogId(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(WithoutBom(content));
            return StringProperty(document.RootElement, "catalog");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string NormalizeKnownPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string normalized = NormalizeRelativePath(path);
        _ = ContainedPath(normalized);
        return normalized;
    }

    // Session-level history must use exactly the same canonical path as the
    // workspace write boundary; otherwise an accepted alias can commit without
    // recording its inverse or invalidating redo.
    internal string NormalizeDocumentPath(string path) => NormalizeKnownPath(path);

    private string ContainedPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new EditorUserException(EditorNotice.Create("ui_backend_path_relative"));
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
        string boundary = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal))
            throw new EditorUserException(EditorNotice.Create("ui_backend_path_escape"));
        for (string? current = fullPath; current is not null && current.StartsWith(boundary, StringComparison.Ordinal);
            current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
        }
        return fullPath;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static TranslationSource Source(string path, string content) => new(path, StrictUtf8.GetBytes(content));

    // runic.json as the compiler reads it: a leading UTF-8 BOM is not part of the JSON.
    private static string WithoutBom(string content) => content.StartsWith('\uFEFF') ? content[1..] : content;

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private string? FindMf2ProjectConfig()
    {
        string direct = Path.Combine(_root, "runic.json");
        if (File.Exists(direct)) return direct;
        string conventional = Path.Combine(_root, "translations", "runic.json");
        return File.Exists(conventional) ? conventional : null;
    }

    private Dictionary<string, byte[]> ReadCurrentTranslationFiles(CancellationToken cancellationToken)
    {
        string? config = FindMf2ProjectConfig();
        // A deleted manifest is itself a meaningful inventory transition.  A
        // subsequent watcher event will repopulate the inventory when the
        // project is recreated, while this scan can still report removals.
        if (config is null) return new Dictionary<string, byte[]>(StringComparer.Ordinal);
        // A malformed manifest, or a source root that is missing or outside the
        // workspace (common while a path is being typed), must not fail the
        // scan: it would fail again on every check. The load reports those as
        // manifest diagnostics. The manifest itself stays in the inventory, so
        // fixing it is reported and rediscovers the sources.
        IReadOnlyList<string> sourceRoots = UsableSourceRoots(
            TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!).SourceRoots, null);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [NormalizeRelativePath(Path.GetRelativePath(_root, config))] = ReadSourceBytes(ContainedPath(NormalizeRelativePath(Path.GetRelativePath(_root, config)))),
        };
        foreach (string sourceRoot in sourceRoots)
            foreach (string path in EnumerateSourceFiles(sourceRoot, ".mf2").Concat(EnumerateSourceFiles(sourceRoot, ".rmf2")))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = NormalizeRelativePath(Path.GetRelativePath(_root, path));
                result[relative] = ReadSourceBytes(ContainedPath(relative));
            }
        return result;
    }

    private void ReplaceKnownRevisions(Dictionary<string, string> revisions)
    {
        _knownRevisions.Clear();
        foreach (KeyValuePair<string, string> revision in revisions)
            _knownRevisions.Add(revision.Key, revision.Value);
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs eventArgs) =>
        QueueChange(eventArgs.FullPath, eventArgs.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Deleted);

    private void OnWatcherRenamed(object sender, RenamedEventArgs eventArgs)
    {
        QueueChange(eventArgs.OldFullPath, true);
        QueueChange(eventArgs.FullPath, true);
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs) =>
        Interlocked.Exchange(ref _watcherOverflowed, 1);

    private void QueueChange(string fullPath, bool membershipChanged = false)
    {
        if (_disposed) return;
        bool relevant = fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || IsSourcePath(fullPath);
        if (!relevant && !membershipChanged) return;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal)) return;
        if (relativePath.StartsWith(".runic-translations/", StringComparison.Ordinal)) return;
        _pendingChanges.TryAdd(relativePath, 0);
        Interlocked.Exchange(ref _reconcileRequested, 1);
    }

    private static bool IsSourcePath(string path) =>
        path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);

    // Source roots the Editor can enumerate. A root outside the opened
    // workspace folder cannot be edited or watched here, and a missing root is
    // usually a path still being typed; both are skipped and, when errors is
    // supplied, reported as manifest diagnostics instead of failing the load.
    private List<string> UsableSourceRoots(IReadOnlyList<string> sourceRoots, List<string>? errors)
    {
        var usable = new List<string>(sourceRoots.Count);
        foreach (string sourceRoot in sourceRoots)
        {
            string display = NormalizeRelativePath(Path.GetRelativePath(_root, sourceRoot));
            if (!IsWithinSourceRoot(_root, sourceRoot) &&
                !string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot)), Path.TrimEndingDirectorySeparator(_root), StringComparison.Ordinal))
                errors?.Add(SourceRootOutsideWorkspace(display));
            else if (!Directory.Exists(sourceRoot))
                errors?.Add(SourceRootMissing(display));
            else usable.Add(sourceRoot);
        }
        return usable;
    }

    internal static string SourceRootOutsideWorkspace(string root) => $"Source root '{root}' is outside the workspace folder opened in the Translations Editor.";
    internal static string SourceRootMissing(string root) => $"Source root '{root}' is not an existing directory.";

    private static IEnumerable<string> EnumerateSourceFiles(string root, string extension)
    {
        ValidateNoReparseAncestors(root);
        foreach (string path in Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (Path.GetFileName(path) == ".runic-translations") continue;
                foreach (string child in EnumerateSourceFiles(path, extension)) yield return child;
            }
            else if (string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)) yield return path;
        }
    }

    private static void ValidateNoReparseAncestors(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new EditorUserException(EditorNotice.Create("ui_backend_path_link"));
            current = Path.GetDirectoryName(current);
        }
    }

    private static byte[] ReadSourceBytes(string path)
    {
        if (new FileInfo(path).Length > new TranslationCompilerOptions().MaximumDocumentBytes)
            throw new EditorUserException(EditorNotice.Create("ui_backend_document_size"));
        return File.ReadAllBytes(path);
    }

    private static string? SourceLocale(string path) => path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)
        ? Path.GetFileNameWithoutExtension(path)
        : path.Contains('/', StringComparison.Ordinal) ? path[..path.IndexOf('/')] : null;

    private static string? SourceLocale(string fullPath, IReadOnlyList<string> sourceRoots)
    {
        string[] matches = sourceRoots.Where(root => IsWithinSourceRoot(root, fullPath)).ToArray();
        return matches.Length != 1
            ? null
            : SourceLocale(NormalizeRelativePath(Path.GetRelativePath(matches[0], fullPath)));
    }

    private static bool IsWithinSourceRoot(string root, string path)
    {
        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(boundary, StringComparison.Ordinal);
    }

    private EditorMessageEntry[] ReadEntries(string path, string content, Rmf2ProjectCompilation compilation)
    {
        var resources = compilation.Project?.Locales.SelectMany(locale => locale.DirectResources).ToArray() ?? [];
        EditorMessageEntry Entry(string key, string message, int start, int length, Rmf2ResourceNode? node)
        {
            var linked = resources.FirstOrDefault(resource => resource.Key == key && resource.SourceLocation.Path == path);
            var source = Source(path, message);
            string locale = linked?.ContentLocale ?? Rmf2Catalog().Locale(path);
            var projection = Rmf2AuthoringService.Project(source, locale, linked?.Message);
            var semantic = Rmf2AuthoringService.Semantic(source, linked?.Message);
            var examples = new List<IReadOnlyDictionary<string, string>>();
            foreach (string property in node?.Properties ?? [])
            {
                if (!property.StartsWith("example ", StringComparison.Ordinal)) continue;
                try
                {
                    using var json = JsonDocument.Parse(property[8..]);
                    if (json.RootElement.ValueKind == JsonValueKind.Object)
                        examples.Add(json.RootElement.EnumerateObject().ToDictionary(item => item.Name,
                            item => item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString()! : item.Value.GetRawText(), StringComparer.Ordinal));
                }
                catch (JsonException) { /* Compiler diagnostics retain invalid metadata; no invented sample is emitted. */ }
            }
            var context = new EditorMessageContext(node?.Comments ?? [],
                (node?.Properties ?? []).Where(property => property.StartsWith("tag ", StringComparison.Ordinal) || property.StartsWith("tags ", StringComparison.Ordinal))
                    .SelectMany(property => property[(property.IndexOf(' ') + 1)..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)).Distinct(StringComparer.Ordinal).ToArray(), examples);
            return new(key, message, start, length,
                new EditorMessageProjection(projection.Revision, projection.Supported, projection.Reason,
                    projection.Inputs.Select(input => new EditorMessageInput(input.Name, input.Type, input.Function, input.Declared)).ToArray(),
                    projection.Selectors, projection.SelectorFunctions, projection.Variants.Select(variant => new EditorMessageVariant(variant.Id, variant.Keys, variant.Pattern, variant.StartByte, variant.LengthBytes)).ToArray(),
                    projection.PluralCategories, projection.SelectorPluralCategories, projection.CldrVersion), context,
                new EditorMessageSemantic(semantic.Text, semantic.Placeholders, semantic.Slots, semantic.Supported, semantic.HasBoundaryWhitespace));
        }
        if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = Rmf2Catalog();
            return Rmf2ResourceReader.Read(Source(path, content)).Nodes.Where(node => !node.IsGroup)
                .Select(node => Entry(string.Join('_', workspace.LogicalPath(path, node)), node.Message!, node.MessageByteMap[0], node.MessageByteMap[^1] - node.MessageByteMap[0], node)).ToArray();
        }
        if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
        {
            string key = resources.FirstOrDefault(resource => resource.SourceLocation.Path == path)?.Key ?? Path.GetFileNameWithoutExtension(path);
            return [Entry(key, content, 0, StrictUtf8.GetByteCount(content), null)];
        }
        return [];
    }

    internal TranslationWorkspaceTransactionPlan? PlanRmf2Mutation(EditorMutationRequest request)
    {
        string? config = FindMf2ProjectConfig();
        if (config is null) return null;
        TranslationManifestLayout layout = TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!);
        if (!layout.IsValid) throw new TranslationAuthoringException(layout.Error!);
        var state = ReadStateAsync(null, null, CancellationToken.None).GetAwaiter().GetResult();
        // A source root that is missing or outside the workspace would plan
        // against a partial inventory; refuse with the load's manifest diagnostic.
        string configRelative = NormalizeRelativePath(Path.GetRelativePath(_root, config));
        if (state.Compilation.Diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.Id == TranslationManifestReader.DiagnosticId && diagnostic.Severity == TranslationDiagnosticSeverity.Error &&
                string.Equals(diagnostic.Location.Path, configRelative, StringComparison.Ordinal)) is { } discovery)
            throw new TranslationAuthoringException(discovery.Message);
        var sources = state.Files.Where(file => file.Kind == DocumentKind.Resource && IsSourcePath(file.Path)).Select(file => Source(file.Path, file.Content)).ToArray();
        if (sources.Length == 0) return null;
        if (request.Kind is "add-locale" or "remove-locale" or "set-fallback")
            return PlanLocaleChange(request, config, layout, sources);
        var workspace = new Rmf2Workspace(_root, new TranslationSource(Path.GetRelativePath(_root, config).Replace('\\', '/'), File.ReadAllBytes(config)), sources);
        string[] Target() => (request.TargetKey ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (request.Kind == "create-key")
        {
            string locale = layout.BaseLocale ?? throw new TranslationAuthoringException(TranslationManifestReader.MissingBaseLocale);
            string path = sources.Where(source => string.Equals(workspace.Locale(source.Path), locale, StringComparison.OrdinalIgnoreCase)).Select(source => source.Path).Order(StringComparer.Ordinal).FirstOrDefault(path => {
                try { workspace.LocalPath(path, Target()); return true; }
                catch (TranslationAuthoringException) { return false; }
            })
                ?? throw new TranslationAuthoringException("No base-locale source can contain this logical path. Choose a path within an existing source namespace.");
            return workspace.CreateResource(path, Target(), request.InitialValue ?? "");
        }
        var origin = workspace.Documents.SelectMany(file => file.Nodes.Where(node => !node.IsGroup).Select(node => (file, node)))
            .FirstOrDefault(item => string.Join('_', workspace.LogicalPath(item.file.Source.Path, item.node)) == request.SourceKey);
        if (origin.node is null) throw new TranslationAuthoringException("Select an existing RMF2 resource.");
        var logical = workspace.LogicalPath(origin.file.Source.Path, origin.node);
        return request.Kind switch {
            "rename-key" => workspace.MutateResource(logical, Target()),
            "duplicate-key" => workspace.MutateResource(logical, Target(), duplicate: true),
            "delete-key" => workspace.MutateResource(logical, null),
            _ => throw new TranslationAuthoringException("This operation is not available for RMF2 resources."),
        };
    }

    // Locale changes go through Tooling's planner, the code of runic-translations locale add|remove: runic.json keeps its
    // formatting and only the changed entry's bytes change. The editor seeds a new locale from a declared one (copyFrom)
    // and gives the copied messages the plural and ordinal variants the new locale needs (RTR0095's repair: copies of
    // the * variant); removing a locale redirects the locales that fall back to it (the base locale by default).
    private TranslationWorkspaceTransactionPlan PlanLocaleChange(EditorMutationRequest request, string config, TranslationManifestLayout layout, TranslationSource[] sources)
    {
        string baseLocale = layout.BaseLocale ?? throw new TranslationAuthoringException(TranslationManifestReader.MissingBaseLocale);
        var project = new TranslationSource(NormalizeRelativePath(Path.GetRelativePath(_root, config)), File.ReadAllBytes(config));
        var inputs = new TranslationProjectSources(_root, project, sources, layout.SourceRoots);
        TranslationLocalePlan plan;
        try
        {
            plan = request.Kind switch
            {
                "add-locale" => TranslationLocaleChanges.PlanAdd(inputs, request.Locale ?? "", request.Fallback, request.CopyFromLocale ?? baseLocale),
                "remove-locale" => TranslationLocaleChanges.PlanRemove(inputs, request.Locale ?? "", request.ReplacementFallback ?? baseLocale),
                _ => TranslationLocaleChanges.PlanSetFallback(inputs, request.Locale ?? "", request.Fallback ?? baseLocale),
            };
        }
        catch (TranslationToolingUsageException exception) { throw new TranslationAuthoringException(exception.Message, exception); }
        // Removing a locale reads the review file to drop its entries; a corrupt one is refused with what to repair.
        catch (TranslationInterchangeException exception)
        {
            throw new EditorUserException(EditorNotice.Create("ui_backend_locale_review_invalid", ("locale", request.Locale?.Trim() ?? ""),
                ("path", ReviewDisplayPath() ?? "review.json")) with { Detail = exception.Message });
        }

        var created = plan.CreatedFiles.Select(file => new TranslationSource(inputs.DisplayPath(file.Path), file.Bytes)).ToList();
        if (created.Count != 0) created = SeedPluralCategories(plan, inputs, sources, created);
        var deleted = plan.DeletedFiles.Select(inputs.DisplayPath).ToHashSet(StringComparer.Ordinal);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource(project.Path, plan.ProjectBytes),
            sources.Where(source => !deleted.Contains(source.Path)).Concat(created).ToArray());
        if (!compilation.Success || compilation.Project is null)
            throw new TranslationAuthoringException("The complete edited catalog is invalid: " + string.Join("; ", compilation.Diagnostics
                .Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Message)));

        var edits = new List<TranslationWorkspaceEdit>
        {
            new(project.Path, TranslationWorkspaceEditKind.Replace, WorkspaceRevision(project.GetUtf8Bytes()), plan.ProjectBytes),
        };
        foreach (TranslationSource source in created) edits.Add(new(source.Path, TranslationWorkspaceEditKind.Create, null, source.GetUtf8Bytes()));
        foreach (TranslationSource source in sources.Where(source => deleted.Contains(source.Path)))
            edits.Add(new(source.Path, TranslationWorkspaceEditKind.Delete, WorkspaceRevision(source.GetUtf8Bytes()), null));
        return new TranslationWorkspaceTransactionPlan(_root, compilation.Project.Id, edits, compilation);
    }

    private static string WorkspaceRevision(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    // Each copied message whose plural or ordinal selector lacks a category the new locale needs gets it, as the RTR0095
    // quick fix adds it: a copy of the * variant. Repairs within one file never overlap, so they apply back to front.
    private static List<TranslationSource> SeedPluralCategories(TranslationLocalePlan plan, TranslationProjectSources inputs,
        TranslationSource[] sources, List<TranslationSource> created)
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource(inputs.Project.Path, plan.ProjectBytes),
            sources.Concat(created).ToArray());
        var seeded = new List<TranslationSource>(created.Count);
        foreach (TranslationSource source in created)
        {
            byte[] bytes = source.GetUtf8Bytes();
            foreach (Rmf2DiagnosticQuickFix fix in Rmf2DiagnosticActions.GetProjectQuickFixes(compilation, source).OrderByDescending(static fix => fix.StartByte))
                bytes = [.. bytes.AsSpan(0, fix.StartByte), .. Encoding.UTF8.GetBytes(fix.NewText), .. bytes.AsSpan(fix.StartByte + fix.LengthBytes)];
            seeded.Add(new TranslationSource(source.Path, bytes));
        }
        return seeded;
    }

    private const int MaximumImportEntries = 500;

    /// <summary>
    /// Plans importing a .resx or i18next catalog with Tooling's importer (runic-translations import) against the saved
    /// files: the outcome of each message and the files it writes, as a workspace transaction that commits only when
    /// none of them changed since. A relative source is resolved against the workspace root.
    /// </summary>
    internal async Task<(EditorCatalogImportPlan Plan, TranslationWorkspaceTransactionPlan? Transaction)> PreviewCatalogImportAsync(
        EditorCatalogImportRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EditorCatalogImportPlan Refused(EditorNotice message, IReadOnlyList<EditorDiagnostic>? diagnostics = null) =>
                new(false, message, null, request.From, request.Source, [], [], false, [], 0, 0, diagnostics ?? []);
            string? config = FindMf2ProjectConfig();
            if (config is null || string.IsNullOrWhiteSpace(request.Source)) return (Refused(EditorNotice.Create("ui_backend_import_source_required")), null);
            TranslationManifestLayout layout = TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!);
            if (!layout.IsValid) return (Refused(EditorNotice.FromException(new TranslationAuthoringException(layout.Error!))), null);
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            var project = new TranslationSource(NormalizeRelativePath(Path.GetRelativePath(_root, config)), File.ReadAllBytes(config));
            TranslationSource[] sources = state.Files.Where(file => file.Kind == DocumentKind.Resource && IsSourcePath(file.Path)).Select(file => Source(file.Path, file.Content)).ToArray();
            var inputs = new TranslationProjectSources(_root, project, sources, layout.SourceRoots);
            string sourcePath = Path.GetFullPath(request.Source.Trim(), _root);
            TranslationCatalogImportPlan plan;
            try { plan = TranslationCatalogImport.Plan(inputs, new TranslationCatalogImportRequest(request.From, sourcePath, [], null, request.Overwrite)); }
            catch (TranslationToolingUsageException exception) { return (Refused(EditorNotice.FromException(new TranslationAuthoringException(exception.Message, exception))), null); }
            EditorDiagnostic[] diagnostics = plan.Diagnostics.Where(static item => item.Severity == TranslationDiagnosticSeverity.Error)
                .Select(static item => new EditorDiagnostic(item.Id, "error", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn)).ToArray();
            switch (plan.Failure)
            {
                case TranslationCatalogImportFailure.ProjectDoesNotCompile: return (Refused(EditorNotice.Create("ui_backend_import_project_errors"), diagnostics), null);
                case TranslationCatalogImportFailure.SourceUnreadable: return (Refused(EditorNotice.Create("ui_backend_import_source_unreadable", ("code", plan.FailureCode ?? ""), ("message", plan.FailureMessage ?? ""))), null);
                case TranslationCatalogImportFailure.MessagesDoNotCompile: return (Refused(EditorNotice.Create("ui_backend_import_messages_invalid"), diagnostics), null);
            }

            var entries = plan.Applied.Concat(plan.Entries).Select(static entry => new EditorCatalogImportEntry(entry.Locale, entry.Key, entry.Outcome, entry.Code, entry.Message)).ToArray();
            var result = new EditorCatalogImportPlan(true, null, null, request.From, request.Source,
                plan.Locales.Select(static locale => new EditorCatalogImportLocale(locale.Locale, locale.Added, locale.Changed, locale.Unchanged, locale.Kept)).ToArray(),
                entries.Take(MaximumImportEntries).ToArray(), entries.Length > MaximumImportEntries,
                plan.Writes.Select(write => NormalizeRelativePath(Path.GetRelativePath(_root, write.FullPath))).ToArray(), plan.Skipped, plan.Refused,
                // The catalog's own errors from before the import, which it neither causes nor fixes.
                plan.Preexisting.Select(static item => new EditorDiagnostic(item.Id, "warning", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn)).ToArray());
            if (plan.Writes.Count == 0) return (result, null);

            // The writes as one transaction: each replaced file must still have the bytes the import read.
            var edits = new List<TranslationWorkspaceEdit>();
            var after = sources.ToDictionary(static source => source.Path, StringComparer.Ordinal);
            foreach (TranslationImportSourceWrite write in plan.Writes)
            {
                string relative = NormalizeRelativePath(Path.GetRelativePath(_root, write.FullPath));
                if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                    return (Refused(EditorNotice.Create("ui_backend_source_outside_project", ("path", write.DisplayPath))), null);
                edits.Add(write.OriginalBytes is null
                    ? new TranslationWorkspaceEdit(relative, TranslationWorkspaceEditKind.Create, null, write.Bytes)
                    : new TranslationWorkspaceEdit(relative, TranslationWorkspaceEditKind.Replace, WorkspaceRevision(write.OriginalBytes), write.Bytes));
                after[relative] = new TranslationSource(relative, write.Bytes);
            }
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, after.Values.ToArray(), null, cancellationToken);
            Rmf2ProjectCompilation counted = TranslationStatus.ForStatus(compilation, project, after.Values.ToArray(), cancellationToken) ?? compilation;
            return (result, new TranslationWorkspaceTransactionPlan(_root, counted.Project?.Id ?? _catalogId ?? "", edits, counted));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The open catalog's review file relative to the workspace, or null without a catalog.</summary>
    internal string? ReviewDisplayPath() =>
        _catalogId is null || FindMf2ProjectConfig() is not { } config || !TranslationReviewFile.IsFileName(_catalogId) ? null
            : NormalizeRelativePath(Path.GetRelativePath(_root, TranslationReviewFile.PathFor(config, _catalogId)));

    /// <summary>The compiled catalog's locales, default locale and unsupportedLocale policy; null when it does not compile.</summary>
    internal async Task<EditorLocaleContext?> LocaleContextAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            // No baseline reset: reading the state for a description must not hide an external change.
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken, resetBaseline: false).ConfigureAwait(false);
            if (state.Compilation.Project is not { } project) return null;
            UnsupportedLocalePolicy policy = project.UnsupportedLocale switch
            {
                TranslationUnsupportedLocalePolicy.Exact => UnsupportedLocalePolicy.Exact,
                TranslationUnsupportedLocalePolicy.Default => UnsupportedLocalePolicy.Default,
                _ => UnsupportedLocalePolicy.ParentsThenDefault,
            };
            return new EditorLocaleContext(project.Locales.Select(static locale => locale.Tag).ToArray(), project.DefaultLocale, policy, project.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// After a locale's addition: its starter messages, copies of another locale's text, are recorded as <c>draft</c> in
    /// the review file (XLIFF's <c>initial</c>), so status and the language list count them as missing, not as 100 %
    /// translated, until they are translated: saving an edited one records it <c>translated</c>, as
    /// <c>runic-translations review mark</c> does. An existing entry of the locale's messages (left by an earlier
    /// removal) is replaced.
    /// </summary>
    internal void RecordStarterDrafts(string locale, IReadOnlyCollection<string> createdFiles)
    {
        if (FindMf2ProjectConfig() is not { } config) return;
        WorkspaceState state = ReadStateAsync(null, null, CancellationToken.None, resetBaseline: false).GetAwaiter().GetResult();
        Rmf2ProjectCompilation review = ReviewCompilation(state);
        if (_catalogId is null || review.Project is not { } project ||
            project.Locales.FirstOrDefault(item => string.Equals(item.Tag, locale, StringComparison.OrdinalIgnoreCase)) is not { } added || added.Tag == project.DefaultLocale) return;
        // Only the messages of the files the addition created: a file that was already there is the locale's own text.
        string[] keys = added.DirectResources.Where(resource => createdFiles.Contains(resource.SourceLocation.Path))
            .Select(static resource => resource.Key).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) return;
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        string catalog = _catalogId;
        TranslationReviewFile.Update(TranslationReviewFile.PathFor(config, catalog), catalog,
            existing => TranslationReviewFile.Mark(existing, catalog, keys.Where(fingerprints.ContainsKey), added.Tag, TranslationReviewState.Draft, null, fingerprints));
    }

    // The draft review entries of a translated locale's messages that one saved file contains, and their text before the
    // save; null when there are none (the usual case, which needs no extra compilation).
    private (string Locale, Dictionary<string, string> Before)? DraftsBeforeSave(WorkspaceState saved, string path)
    {
        if (_catalogId is null || FindMf2ProjectConfig() is not { } config || saved.Compilation.Project is not { } project ||
            saved.Files.Find(file => file.Path == path)?.Locale is not { } locale || locale == project.DefaultLocale) return null;
        TranslationInterchangeReview? review;
        try { review = TranslationReviewFile.Read(TranslationReviewFile.PathFor(config, _catalogId), _catalogId); }
        catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException) { return null; }
        var drafts = (review?.Entries ?? []).Where(entry => entry.State == TranslationReviewState.Draft && entry.Locale == locale).Select(static entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        if (drafts.Count == 0) return null;
        // The sources on disk, before this save.
        WorkspaceState before = ReadStateAsync(null, null, CancellationToken.None, resetBaseline: false).GetAwaiter().GetResult();
        Rmf2ProjectCompilation compiled = ReviewCompilation(before);
        if (compiled.Project?.Locales.FirstOrDefault(item => item.Tag == locale) is not { } previous) return null;
        var texts = previous.DirectResources.Where(resource => drafts.Contains(resource.Key) && resource.SourceLocation.Path == path)
            .ToDictionary(static resource => resource.Key, static resource => Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparer.Ordinal);
        return texts.Count == 0 ? null : (locale, texts);
    }

    // After a save: each draft message of the file whose text changed is recorded translated, for its current source.
    private void PromoteEditedDrafts(WorkspaceState saved, string locale, Dictionary<string, string> before)
    {
        if (_catalogId is null || FindMf2ProjectConfig() is not { } config) return;
        Rmf2ProjectCompilation review = ReviewCompilation(saved);
        if (review.Project?.Locales.FirstOrDefault(item => item.Tag == locale) is not { } current) return;
        string[] edited = current.DirectResources.Where(resource => before.TryGetValue(resource.Key, out string? text) &&
            !string.Equals(text, Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparison.Ordinal)).Select(static resource => resource.Key).ToArray();
        if (edited.Length == 0) return;
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        string catalog = _catalogId;
        // Compare-and-swap; only entries that are still drafts change.
        TranslationReviewFile.Update(TranslationReviewFile.PathFor(config, catalog), catalog, existing =>
        {
            string[] drafts = edited.Where(key => fingerprints.ContainsKey(key) && existing?.Entries.Any(entry => entry.Key == key && entry.Locale == locale && entry.State == TranslationReviewState.Draft) == true).ToArray();
            return drafts.Length == 0 ? existing : TranslationReviewFile.Mark(existing, catalog, drafts, locale, TranslationReviewState.Translated, null, fingerprints);
        });
    }

    /// <summary>After a locale's removal: its entries leave the review file, as runic-translations locale remove drops them.</summary>
    internal void CompleteLocaleRemoval(string locale)
    {
        if (_catalogId is null || FindMf2ProjectConfig() is not { } config) return;
        string reviewPath = TranslationReviewFile.PathFor(config, _catalogId);
        // Compare-and-swap: a write by the command line or the language server in between is kept.
        TranslationReviewFile.Update(reviewPath, _catalogId, existing => existing is null || !existing.Entries.Any(entry => string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))
            ? existing
            : new TranslationInterchangeReview(existing.CatalogId, existing.Entries.Where(entry => !string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))));
    }

    private Rmf2Workspace Rmf2Catalog()
    {
        string config = FindMf2ProjectConfig() ?? throw new TranslationAuthoringException("No project configuration found.");
        return new Rmf2Workspace(_root, new TranslationSource(config, File.ReadAllBytes(config)), []);
    }

    private static EditorOperationResult Failure(string kind, EditorNotice message) => new(false, kind, message, null, null);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum DocumentKind
    {
        Manifest,
        Resource,
    }

    private sealed record WorkspaceFile(
        string Path,
        string Content,
        string Revision,
        DocumentKind Kind,
        string? CatalogId,
        string? Locale,
        string? Layer);

    private sealed record WorkspaceState(
        List<WorkspaceFile> Files,
        Rmf2ProjectCompilation Compilation,
        IReadOnlyList<EditorCatalogSummary> Catalogs)
    {
        internal Rmf2ProjectCompilation? ReviewCompilation { get; set; }
        /// <summary>The revision of the compiled sources (runic.json and the message sources), which keys cached compilations.</summary>
        internal string? SourcesRevision { get; init; }
    }
}
