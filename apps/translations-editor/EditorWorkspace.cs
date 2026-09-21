using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
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

            Dictionary<string, byte[]> currentFiles = ReadCurrentTranslationFiles(cancellationToken);
            Dictionary<string, string> current = currentFiles.ToDictionary(
                static pair => pair.Key,
                static pair => Revision(pair.Value),
                StringComparer.Ordinal);
            // Remove only the events observed for this scan.  Events arriving
            // while the inventory is being read remain queued for the next
            // reconciliation and cannot be acknowledged by this baseline.
            var candidates = new HashSet<string>(_pendingChanges.Keys, StringComparer.Ordinal);
            foreach (string path in candidates) _pendingChanges.TryRemove(path, out _);
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
            // an unrelated RMF2/TOML file can be queued as well.  Only report a
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
            return new EditorExternalChanges(overflowed, changed, changes);
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
            string? locale = SourceLocale(path);
            if (key is not null && value is not null)
            {
                if (path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
                {
                    TranslationLocaleDocument document = TranslationLocaleReader.Read(Source(path, content), locale!, cancellationToken: cancellationToken);
                    bool exists = document.Entries.Any(entry => entry.Key == key);
                    content = StrictUtf8.GetString(TranslationLocaleWriter.Apply(Source(path, content), locale!,
                        [new TranslationLocaleEdit(exists ? TranslationLocaleEditKind.SetValue : TranslationLocaleEditKind.Add, key, value)]));
                }
                else if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
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
            return new EditorDocumentDraft(state.Compilation.Success, content, ReadEntries(path, content, locale), Diagnostics(state.Compilation));
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationAuthoringException)
        {
            return new EditorDocumentDraft(false, content, [],
                [new EditorDiagnostic("EDITOR-TRANSFORM", "error", string.Empty, relativePath, 1, 1, 1, 1, EditorNotice.FromException(exception))]);
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
            return new ValidationResult(state.Compilation.Success, Diagnostics(state.Compilation));
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
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            string path = NormalizeKnownPath(relativePath);
            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            EditorDiagnostic[] diagnostics = Diagnostics(state.Compilation);
            if (!state.Compilation.Success || state.Compilation.Catalogs.Count != 1)
                return new EditorMessagePreview(false, null, null, diagnostics);

            TranslationGeneratedOutput artifact = TranslationOutputRenderer.RenderLocaleJson(
                state.Compilation.Catalogs[0], locale);
            using JsonDocument document = JsonDocument.Parse(artifact.Text);
            JsonElement messages = document.RootElement.GetProperty("messages");
            if (!messages.TryGetProperty(key, out JsonElement message))
                return new EditorMessagePreview(false, locale, null,
                    [new EditorDiagnostic("PREVIEW", "error", string.Empty, path, 1, 1, 1, 1, EditorNotice.Create("ui_backend_preview_key_missing", ("key", key)))]);
            return new EditorMessagePreview(true, locale, message.GetRawText(), diagnostics);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return new EditorMessagePreview(false, null, null,
                [new EditorDiagnostic("PREVIEW", "error", string.Empty, relativePath, 1, 1, 1, 1, EditorNotice.FromException(exception))]);
        }
        finally
        {
            _gate.Release();
        }
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
                    new ValidationResult(false, Diagnostics(state.Compilation)));
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
            return new EditorOperationResult(true, "saved", null, null, null);
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
            var state = new TranslationEditorState(
                _catalogId,
                request.Entries.Select(static entry => new TranslationEditorStateEntry(
                    entry.Key, entry.Locale, entry.State, entry.Note, entry.SourceFingerprint, entry.Samples)).ToArray(),
                request.Terminology.Select(static term => new TranslationTerminologyEntry(
                    term.Source, term.Preferred, term.Locale, term.Note)).ToArray());
            TranslationEditorStateLoadResult saved = TranslationEditorStateStore.Save(_root, state, request.ExpectedRevision);
            return new EditorReviewOperationResult(true, null, Review(saved), null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
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
            TranslationEditorStateLoadResult current = TranslationEditorStateStore.Load(_root, _catalogId);
            if (current.Error is not null)
                return new EditorReviewOperationResult(false, EditorNotice.External(current.Error), null, null);
            if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_sidecar_history_conflict"), null, null);
            string fullPath = ContainedPath(current.Path);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Delete(fullPath);
            // File.Delete is the point of no return.  Callers must reconcile their
            // history before any sidecar reload can fail or observe an external write.
            return new EditorReviewOperationResult(true, null, null, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
        {
            return new EditorReviewOperationResult(false, EditorNotice.FromException(exception), null, null);
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
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _gate.Dispose();
    }

    // ---- XLIFF interchange (W03) ----
    //
    // Fingerprint reconciliation: the interchange profile validates approved
    // review entries against the compiler catalog fingerprint, while the editor
    // sidecar keeps per-entry fnv1a64 fingerprints as client-side staleness
    // markers. The reconciliation is stamp-on-export / strip-on-import:
    // approved entries are stamped with the live compilation fingerprint when
    // an interchange payload leaves the editor (satisfying REVIEW-FINGERPRINT),
    // and imported entries are stored without a fingerprint because interchange
    // already validated them; later exports re-stamp from the fresh catalog.
    // Non-approved entries never carry an interchange fingerprint.

    internal async Task<EditorXliffExportResult> ExportXliffAsync(
        string? directory,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success || state.Compilation.Catalogs.Count != 1)
                return new EditorXliffExportResult(false, EditorNotice.Create("ui_backend_xliff_compile"), null, [], []);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            TranslationInterchangeReview review = BuildExportReview(TranslationEditorStateStore.Load(_root, catalog.Id), catalog);
            TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(state.Compilation, review);
            string targetDirectory = ResolveInterchangeOutputDirectory(directory, Path.Combine(".runic-translations", "export"));
            Directory.CreateDirectory(targetDirectory);
            var documents = new List<EditorInterchangeFile>(export.Documents.Count);
            foreach (TranslationXliffDocument document in export.Documents)
            {
                string fullPath = Path.Combine(targetDirectory, $"{catalog.Id}.{document.TargetLocale}.xliff");
                await WriteAtomicallyAsync(fullPath, document.Bytes, cancellationToken).ConfigureAwait(false);
                documents.Add(new EditorInterchangeFile(
                    Path.GetRelativePath(_root, fullPath).Replace('\\', '/'), document.TargetLocale, document.Bytes.LongLength));
            }
            return new EditorXliffExportResult(
                true,
                null,
                catalog.Id,
                documents,
                export.Report.Losses.Select(static loss => new EditorInterchangeLoss(
                    loss.Code, loss.Location, loss.Message, loss.SemanticLoss)).ToArray());
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new EditorXliffExportResult(false, InterchangeMessage(exception), null, [], []);
        }
        finally
        {
            _gate.Release();
        }
    }

    internal async Task<EditorReviewFileResult> ExportReviewJsonAsync(
        string? path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success || state.Compilation.Catalogs.Count != 1)
                return new EditorReviewFileResult(false, EditorNotice.Create("ui_backend_review_compile"), null, 0);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            TranslationInterchangeReview review = BuildExportReview(TranslationEditorStateStore.Load(_root, catalog.Id), catalog);
            byte[] bytes = TranslationInterchange.ExportReviewJson(review);
            string targetPath = Path.Combine(
                ResolveInterchangeOutputDirectory(path is null ? null : Path.GetDirectoryName(path), Path.Combine(".runic-translations", "export")),
                path is null ? $"{catalog.Id}.review.json" : Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await WriteAtomicallyAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
            return new EditorReviewFileResult(
                true,
                null,
                Path.GetRelativePath(_root, targetPath).Replace('\\', '/'),
                review.Entries.Count);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
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
            if (!state.Compilation.Success || state.Compilation.Catalogs.Count != 1)
                return (RefuseXliff("EDITOR-COMPILE", EditorNotice.Create("ui_backend_import_compile")), null);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);
            TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            CollectCatalogRefusals(catalog, import, refusals);
            CollectXliffSourceRefusals(bytes, catalog, refusals);

            Dictionary<string, TranslationMf2Document> importedMessages = import.Messages
                .ToDictionary(static message => message.MessageId, StringComparer.Ordinal);
            Dictionary<string, string> importedValues = importedMessages.ToDictionary(
                static pair => pair.Key,
                static pair => StrictUtf8.GetString(pair.Value.Bytes).TrimEnd('\r', '\n'),
                StringComparer.Ordinal);
            if (!string.Equals(import.SourceLocale, catalog.DefaultLocale, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SOURCE-LOCALE-MISMATCH",
                    EditorNotice.Create("ui_backend_refusal_1", ("value1", import.SourceLocale!), ("value2", catalog.DefaultLocale!))));
            foreach (string key in importedValues.Keys.Where(key => !catalog.CanonicalResources.Any(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal))).Order(StringComparer.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-KEY-NOT-IN-CATALOG", EditorNotice.Create("ui_backend_refusal_2", ("value1", key!), ("value2", catalog.Id!))));

            var sidecar = TranslationEditorStateStore.Load(_root, catalog.Id);
            if (sidecar.Error is not null)
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", EditorNotice.External(sidecar.Error)));
            CollectApprovalFingerprintRefusals(catalog, import.Review.Entries, refusals);

            if (refusals.Count > 0)
                return (new EditorXliffImportPlan(false, null, null, import.CatalogId, import.SourceLocale, import.TargetLocale, null,
                    [], 0, 0, 0, 0, 0, false, refusals.Order(InterchangeRefusalOrder.Instance).ToArray()), null);

            const string layer = "base";
            CompiledTextLocale targetLocale = catalog.Locales.Single(locale => string.Equals(locale.Tag, import.TargetLocale, StringComparison.Ordinal));
            Dictionary<string, string> direct = targetLocale.DirectResources.ToDictionary(
                static resource => resource.Key,
                static resource => resource.Pattern.TrimEnd('\r', '\n'),
                StringComparer.Ordinal);
            string configPath = FindMf2ProjectConfig()!;
            string projectPrefix = NormalizeRelativePath(Path.GetRelativePath(_root, Path.GetDirectoryName(configPath)!));
            if (projectPrefix == ".") projectPrefix = string.Empty;
            else projectPrefix += "/";
            var documents = new List<PreparedInterchangeDocument>();
            var localeEdits = new List<TranslationLocaleEdit>();
            string manifestContent = state.Files.Single(file => file.Kind == DocumentKind.Manifest).Content;
            bool localeToml = UsesLocaleToml(manifestContent);
            bool rmf2 = UsesRmf2(manifestContent);
            Rmf2Workspace? rmf2Workspace = null;
            Dictionary<string, byte[]>? rmf2Sources = null;
            Dictionary<string, string>? rmf2ExpectedRevisions = null;
            if (rmf2)
            {
                rmf2Sources = state.Files
                    .Where(file => file.Kind == DocumentKind.Resource && file.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(static file => file.Path, static file => StrictUtf8.GetBytes(file.Content), StringComparer.Ordinal);
                rmf2ExpectedRevisions = rmf2Sources.ToDictionary(static pair => pair.Key, static pair => Revision(pair.Value), StringComparer.Ordinal);
                rmf2Workspace = new Rmf2Workspace(
                    _root,
                    Source(NormalizeRelativePath(Path.GetRelativePath(_root, configPath)), manifestContent),
                    rmf2Sources.Select(static pair => Source(pair.Key, StrictUtf8.GetString(pair.Value))),
                    cancellationToken);
            }
            var changes = new List<EditorKeyChange>();
            bool overflowed = false;
            int added = 0, changed = 0, removed = 0, unchanged = 0;
            foreach ((string key, string after) in importedValues.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                string? before = direct.GetValueOrDefault(key);
                if (!direct.ContainsKey(key)) added += 1;
                else if (string.Equals(before, after, StringComparison.Ordinal)) unchanged += 1;
                else changed += 1;
                Push(ref changes, ref overflowed, new EditorKeyChange(key, direct.ContainsKey(key) ? "changed" : "added", before, after, null, null));
                if (string.Equals(before, after, StringComparison.Ordinal)) continue;
                if (localeToml)
                {
                    localeEdits.Add(new TranslationLocaleEdit(direct.ContainsKey(key)
                        ? TranslationLocaleEditKind.SetValue : TranslationLocaleEditKind.Add, key, after));
                    continue;
                }
                if (rmf2)
                {
                    ApplyRmf2InterchangeValue(
                        rmf2Workspace!,
                        rmf2Sources!,
                        import.TargetLocale!,
                        key,
                        after);
                    continue;
                }
                string targetPath = $"{projectPrefix}{import.TargetLocale}/{key}.mf2";
                WorkspaceFile? target = state.Files.FirstOrDefault(file => string.Equals(file.Path, targetPath, StringComparison.Ordinal));
                byte[]? original = target is null ? null : StrictUtf8.GetBytes(target.Content);
                documents.Add(new PreparedInterchangeDocument(
                    targetPath,
                    target?.Revision,
                    original,
                    importedMessages[key].Bytes));
            }

            if (rmf2)
            {
                foreach (KeyValuePair<string, byte[]> pair in rmf2Sources!.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (rmf2ExpectedRevisions!.TryGetValue(pair.Key, out string? expected) &&
                        string.Equals(expected, Revision(pair.Value), StringComparison.Ordinal))
                        continue;
                    WorkspaceFile? original = state.Files.FirstOrDefault(file => file.Kind == DocumentKind.Resource && file.Path == pair.Key);
                    documents.Add(new PreparedInterchangeDocument(
                        pair.Key,
                        original?.Revision,
                        original is null ? null : StrictUtf8.GetBytes(original.Content),
                        pair.Value));
                }
            }

            if (localeEdits.Count != 0)
            {
                WorkspaceFile? target = state.Files.FirstOrDefault(file => file.Kind == DocumentKind.Resource &&
                    file.Path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(file.Locale, import.TargetLocale, StringComparison.OrdinalIgnoreCase));
                string targetPath = target?.Path ?? $"{projectPrefix}{import.TargetLocale}.toml";
                byte[]? original = target is null ? null : StrictUtf8.GetBytes(target.Content);
                byte[] updated = TranslationLocaleWriter.Apply(Source(targetPath, target?.Content ?? string.Empty),
                    import.TargetLocale!, localeEdits);
                documents.Add(new PreparedInterchangeDocument(targetPath, target?.Revision, original, updated));
            }
            TranslationCompilation proposed = CompileWithInterchangeDocuments(state.Files, documents, cancellationToken);
            if (!proposed.Success)
            {
                string message = string.Join(" ", proposed.Diagnostics
                    .Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error)
                    .Select(static diagnostic => $"[{diagnostic.Id}] {diagnostic.Message}"));
                return (RefuseXliff("EDITOR-MF2-IMPORT", EditorNotice.External(message)), null);
            }

            Dictionary<string, TranslationEditorStateEntry> currentEntries = sidecar.State.Entries
                .ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
            int reviewUpdates = 0;
            foreach (TranslationInterchangeReviewEntry entry in import.Review.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                TranslationEditorStateEntry? existing = currentEntries.GetValueOrDefault(Identity(entry.Key, entry.Locale));
                bool stateChanged = existing?.State != entry.State;
                bool noteChanged = existing?.Note != entry.Note;
                if (!stateChanged && !noteChanged) continue;
                reviewUpdates += 1;
                Push(ref changes, ref overflowed, new EditorKeyChange(entry.Key, "state-change", null, null, existing?.State ?? "draft", entry.State));
            }

            List<TranslationEditorStateEntry> merged = MergeImportedEntries(sidecar, import.Review.Entries);
            var prepared = new PreparedInterchangeImport(
                ResolveImportSourcePath(path),
                SHA256.HashData(bytes),
                import.CatalogId,
                catalog.Fingerprint,
                documents,
                merged,
                sidecar.Revision);
            return (new EditorXliffImportPlan(true, null, null, import.CatalogId, import.SourceLocale, import.TargetLocale, layer,
                changes.ToArray(), added, changed, removed, unchanged, reviewUpdates, overflowed, []),
                prepared);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
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
            if (!currentState.Compilation.Success || currentState.Compilation.Catalogs.Count != 1 ||
                !string.Equals(currentState.Compilation.Catalogs[0].Id, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentState.Compilation.Catalogs[0].Fingerprint, prepared.ExpectedCatalogFingerprint, StringComparison.Ordinal))
                return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
            string? sidecarRevision = TranslationEditorStateStore.Load(_root, prepared.CatalogId).Revision;
            if (!string.Equals(sidecarRevision, prepared.ExpectedSidecarRevision, StringComparison.Ordinal))
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
                var state = new TranslationEditorState(prepared.CatalogId, prepared.MergedEntries, TranslationEditorStateStore.Load(_root, prepared.CatalogId).State.Terminology);
                _ = TranslationEditorStateStore.Save(_root, state, prepared.ExpectedSidecarRevision);
            }
            catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
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

    internal async Task<(EditorReviewImportPlan Plan, PreparedInterchangeImport? Prepared)> PreviewReviewJsonImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success || state.Compilation.Catalogs.Count != 1)
                return (RefuseReview("EDITOR-COMPILE", EditorNotice.Create("ui_backend_import_compile")), null);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);
            TranslationInterchangeReview import = TranslationInterchange.ImportReviewJson(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            if (!string.Equals(import.CatalogId, catalog.Id, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                    EditorNotice.Create("ui_backend_refusal_3", ("value1", import.CatalogId!), ("value2", catalog.Id!))));
            var canonicalKeys = new HashSet<string>(catalog.CanonicalResources.Select(static value => value.Key), StringComparer.Ordinal);
            var localeTags = new HashSet<string>(catalog.Locales.Select(static value => value.Tag), StringComparer.Ordinal);
            foreach (TranslationInterchangeReviewEntry entry in import.Entries)
            {
                if (!canonicalKeys.Contains(entry.Key))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-KEY-NOT-IN-CATALOG", EditorNotice.Create("ui_backend_refusal_4", ("value1", entry.Key!), ("value2", catalog.Id!))));
                if (!localeTags.Contains(entry.Locale))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG", EditorNotice.Create("ui_backend_refusal_5", ("value1", entry.Locale!))));
            }
            TranslationEditorStateLoadResult sidecar = TranslationEditorStateStore.Load(_root, catalog.Id);
            if (sidecar.Error is not null)
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", EditorNotice.External(sidecar.Error)));
            CollectApprovalFingerprintRefusals(catalog, import.Entries, refusals);
            if (refusals.Count > 0)
                return (new EditorReviewImportPlan(false, null, null, import.CatalogId, [], 0, 0, 0, false,
                    refusals.Order(InterchangeRefusalOrder.Instance).ToArray()), null);

            var changes = new List<EditorReviewChange>();
            bool overflowed = false;
            int added = 0, changed = 0, removed = 0;
            Dictionary<string, TranslationEditorStateEntry> currentEntries = sidecar.State.Entries
                .ToDictionary(static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
            foreach (TranslationInterchangeReviewEntry entry in import.Entries.OrderBy(static item => item.Key, StringComparer.Ordinal).ThenBy(static item => item.Locale, StringComparer.Ordinal))
            {
                TranslationEditorStateEntry? existing = currentEntries.GetValueOrDefault(Identity(entry.Key, entry.Locale));
                if (existing is null)
                {
                    added += 1;
                    Push(ref changes, ref overflowed, new EditorReviewChange(entry.Key, entry.Locale, "added", null, entry.State));
                }
                else if (existing.State != entry.State || existing.Note != entry.Note)
                {
                    changed += 1;
                    Push(ref changes, ref overflowed, new EditorReviewChange(entry.Key, entry.Locale, "changed", existing.State, entry.State));
                }
            }
            List<TranslationEditorStateEntry> merged = MergeImportedEntries(sidecar, import.Entries);
            var prepared = new PreparedInterchangeImport(
                ResolveImportSourcePath(path),
                SHA256.HashData(bytes),
                catalog.Id,
                catalog.Fingerprint,
                [],
                merged,
                sidecar.Revision);
            return (new EditorReviewImportPlan(true, null, null, catalog.Id, changes.ToArray(), added, changed, removed, overflowed, []), prepared);
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationAuthoringException or ArgumentException or IOException or UnauthorizedAccessException)
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
            if (!currentState.Compilation.Success || currentState.Compilation.Catalogs.Count != 1 ||
                !string.Equals(currentState.Compilation.Catalogs[0].Id, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentState.Compilation.Catalogs[0].Fingerprint, prepared.ExpectedCatalogFingerprint, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
            TranslationEditorStateLoadResult sidecar = TranslationEditorStateStore.Load(_root, prepared.CatalogId);
            if (!string.Equals(sidecar.Revision, prepared.ExpectedSidecarRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_sidecar_changed"), null, null);
            var state = new TranslationEditorState(prepared.CatalogId, prepared.MergedEntries, sidecar.State.Terminology);
            TranslationEditorStateLoadResult saved = TranslationEditorStateStore.Save(_root, state, prepared.ExpectedSidecarRevision);
            return new EditorReviewOperationResult(true, null, Review(saved), null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
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

    private string ResolveInterchangeOutputDirectory(string? directory, string defaultRelative) =>
        ContainedPath(directory is null or "" ? defaultRelative : NormalizeRelativePath(directory));

    private static TranslationInterchangeReview BuildExportReview(TranslationEditorStateLoadResult sidecar, CompiledTextCatalog catalog)
    {
        var keys = new HashSet<string>(catalog.CanonicalResources.Select(static value => value.Key), StringComparer.Ordinal);
        var locales = new HashSet<string>(
            catalog.Locales.Where(locale => !string.Equals(locale.Tag, catalog.DefaultLocale, StringComparison.Ordinal)).Select(static locale => locale.Tag),
            StringComparer.Ordinal);
        var entries = sidecar.State.Entries
            .Where(entry => keys.Contains(entry.Key) && locales.Contains(entry.Locale))
            .Select(entry => new TranslationInterchangeReviewEntry(
                entry.Key,
                entry.Locale,
                entry.State,
                entry.Note,
                string.Equals(entry.State, "approved", StringComparison.Ordinal) ? catalog.Fingerprint : null))
            .ToArray();
        return new TranslationInterchangeReview(catalog.Id, entries);
    }

    // Imported entries replace matching identities wholesale (interchange cannot
    // express samples, so samples survive from the current sidecar); unrelated
    // identities and terminology are preserved.
    private static List<TranslationEditorStateEntry> MergeImportedEntries(
        TranslationEditorStateLoadResult sidecar,
        IReadOnlyList<TranslationInterchangeReviewEntry> imported)
    {
        Dictionary<string, TranslationEditorStateEntry> merged = sidecar.State.Entries.ToDictionary(
            static entry => Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        foreach (TranslationInterchangeReviewEntry entry in imported)
        {
            TranslationEditorStateEntry? existing = merged.GetValueOrDefault(Identity(entry.Key, entry.Locale));
            merged[Identity(entry.Key, entry.Locale)] = new TranslationEditorStateEntry(
                entry.Key,
                entry.Locale,
                entry.State,
                entry.Note,
                null,
                existing?.Samples ?? new SortedDictionary<string, string>(StringComparer.Ordinal));
        }
        return merged.Values
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Locale, StringComparer.Ordinal)
            .ToList();
    }

    private static TranslationCompilation CompileWithInterchangeDocuments(
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
        return TranslationCompiler.CompileProject(
            Source(project.Path, project.Content),
            messages.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => Source(pair.Key, pair.Value)),
            null,
            cancellationToken);
    }

    private static void ApplyRmf2InterchangeValue(
        Rmf2Workspace workspace,
        Dictionary<string, byte[]> sources,
        string locale,
        string logicalKey,
        string value)
    {
        string[] logicalPath = logicalKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (logicalPath.Length == 0)
            throw new TranslationAuthoringException("An imported RMF2 key is empty.");

        // Prefer the target locale's existing declaration. This keeps its
        // physical file, comments, and formatting intact while changing only
        // the message body.
        foreach (Rmf2ResourceDocument document in workspace.Documents
            .Where(document => string.Equals(Path.GetFileNameWithoutExtension(document.Source.Path), locale, StringComparison.OrdinalIgnoreCase)))
        {
            Rmf2ResourceNode? node = document.Nodes.FirstOrDefault(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(document.Source.Path, node)), logicalKey, StringComparison.Ordinal));
            if (node is null) continue;
            sources[document.Source.Path] = Rmf2ResourceWriter.SetMessage(
                new TranslationSource(document.Source.Path, sources[document.Source.Path]), node.Key, value);
            return;
        }

        // A missing target translation follows the base resource's namespace
        // and mount. Replace only the locale file name, then use the writer's
        // explicit path operation; never synthesize a legacy .mf2 path.
        Rmf2ResourceDocument? baseDocument = workspace.Documents
            .Where(document => string.Equals(Path.GetFileNameWithoutExtension(document.Source.Path), workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(document => document.Nodes.Any(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(document.Source.Path, node)), logicalKey, StringComparison.Ordinal)));
        IReadOnlyList<string> resolvedLogicalPath = logicalPath;
        string targetPath;
        if (baseDocument is not null)
        {
            Rmf2ResourceNode node = baseDocument.Nodes.First(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(baseDocument.Source.Path, node)), logicalKey, StringComparison.Ordinal));
            resolvedLogicalPath = workspace.LogicalPath(baseDocument.Source.Path, node);
            targetPath = Path.Combine(Path.GetDirectoryName(baseDocument.Source.Path) ?? string.Empty, locale + ".rmf2").Replace('\\', '/');
        }
        else
        {
            targetPath = locale + ".rmf2";
        }

        byte[] existing = sources.GetValueOrDefault(targetPath, []);
        string[] localPath = workspace.LocalPath(targetPath, resolvedLogicalPath).ToArray();
        sources[targetPath] = Rmf2ResourceWriter.AddMessage(
            new TranslationSource(targetPath, existing), localPath, value);
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

    private static void CollectXliffSourceRefusals(byte[] bytes, CompiledTextCatalog catalog, List<EditorInterchangeRefusal> refusals)
    {
        var liveSources = catalog.CanonicalResources.ToDictionary(static value => value.Key, static value => value.Pattern, StringComparer.Ordinal);
        using var stream = new MemoryStream(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        XNamespace xliff = "urn:oasis:names:tc:xliff:document:2.0";
        foreach (XElement unit in document.Descendants(xliff + "unit"))
        {
            string? key = unit.Attribute("id")?.Value;
            string? source = unit.Element(xliff + "segment")?.Element(xliff + "source")?.Value;
            if (key is null || source is null || !liveSources.TryGetValue(key, out string? liveSource) || string.Equals(source, liveSource, StringComparison.Ordinal)) continue;
            refusals.Add(new EditorInterchangeRefusal("EDITOR-SOURCE-MISMATCH",
                EditorNotice.Create("ui_backend_refusal_6", ("value1", key!))));
        }
    }

    private static void CollectCatalogRefusals(CompiledTextCatalog catalog, TranslationXliffImportResult import, List<EditorInterchangeRefusal> refusals)
    {
        if (!string.Equals(import.CatalogId, catalog.Id, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                EditorNotice.Create("ui_backend_refusal_7", ("value1", import.CatalogId!), ("value2", catalog.Id!))));
        if (!catalog.Locales.Any(locale => string.Equals(locale.Tag, import.TargetLocale, StringComparison.Ordinal)))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG",
                EditorNotice.Create("ui_backend_refusal_8", ("value1", import.TargetLocale!))));
        if (string.Equals(import.TargetLocale, catalog.DefaultLocale, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-TARGET-DEFAULT-LOCALE",
                EditorNotice.Create("ui_backend_refusal_9", ("value1", catalog.DefaultLocale!))));
    }

    private static void CollectApprovalFingerprintRefusals(
        CompiledTextCatalog catalog,
        IReadOnlyList<TranslationInterchangeReviewEntry> entries,
        List<EditorInterchangeRefusal> refusals)
    {
        foreach (TranslationInterchangeReviewEntry entry in entries.Where(static entry => string.Equals(entry.State, "approved", StringComparison.Ordinal)))
        {
            if (string.Equals(entry.SourceFingerprint, catalog.Fingerprint, StringComparison.Ordinal)) continue;
            refusals.Add(new EditorInterchangeRefusal("EDITOR-APPROVAL-FINGERPRINT",
                EditorNotice.Create("ui_backend_refusal_10", ("value1", entry.Key!), ("value2", entry.Locale!))));
        }
    }

    private static void Flatten(JsonElement node, string prefix, Dictionary<string, string> values)
    {
        foreach (JsonProperty property in node.EnumerateObject())
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

    private Task<WorkspaceState> ReadStateAsync(
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken)
    {
        string? projectConfig = FindMf2ProjectConfig();
        if (projectConfig is null)
            throw new EditorUserException(EditorNotice.Create("ui_backend_project_required"));
        return ReadMf2StateAsync(projectConfig, replacementPath, replacementContent, cancellationToken);
    }

    private Task<WorkspaceState> ReadMf2StateAsync(
        string configPath,
        string? replacementPath,
        string? replacementContent,
        CancellationToken cancellationToken)
    {
        string projectRoot = Path.GetDirectoryName(configPath)!;
        string configRelativePath = NormalizeRelativePath(Path.GetRelativePath(_root, configPath));
        var paths = new List<string> { configPath };
        var sourceRoots = new List<string>();
        using (JsonDocument config = JsonDocument.Parse(replacementPath == configRelativePath && replacementContent is not null ? StrictUtf8.GetBytes(replacementContent) : File.ReadAllBytes(configPath)))
        {
            string sourceLayout = StringProperty(config.RootElement, "sourceLayout") ?? string.Empty;
            string extension = sourceLayout switch
            {
                "rmf2-v1" => ".rmf2",
                "locale-toml" => ".toml",
                _ => ".mf2",
            };
            if (sourceLayout == "rmf2-v1" && config.RootElement.TryGetProperty("sourceRoots", out var mounts))
                foreach (var mount in mounts.EnumerateArray()) sourceRoots.Add(Path.GetFullPath(mount.GetProperty("path").GetString()!, projectRoot));
            else sourceRoots.Add(projectRoot);
            foreach (string sourceRoot in sourceRoots) paths.AddRange(EnumerateSourceFiles(sourceRoot, extension).Order(StringComparer.Ordinal));
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
                string localPath = NormalizeRelativePath(Path.GetRelativePath(projectRoot, fullPath));
                string? locale = SourceLocale(localPath);
                messageSources.Add(Source(relativePath, content));
                files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), DocumentKind.Resource, catalogId, locale, "base"));
            }
        }
        if (projectSource is null) throw new EditorUserException(EditorNotice.Create("ui_backend_project_config_missing"));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
        {
            string replacementFullPath = ContainedPath(replacementPath);
            string projectBoundary = projectRoot.EndsWith(Path.DirectorySeparatorChar)
                ? projectRoot
                : projectRoot + Path.DirectorySeparatorChar;
            if (!sourceRoots.Any(root => replacementFullPath.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) ||
                !IsSourcePath(replacementPath))
                throw new EditorUserException(EditorNotice.Create("ui_backend_source_outside_project", ("path", replacementPath)));
            string localPath = NormalizeRelativePath(Path.GetRelativePath(projectRoot, replacementFullPath));
            string? locale = SourceLocale(localPath);
            string content = replacementContent ?? string.Empty;
            messageSources.Add(Source(replacementPath, content));
            files.Add(new WorkspaceFile(replacementPath, content, NewMf2DocumentRevision, DocumentKind.Resource, catalogId, locale, "base"));
            files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        }

        TranslationCompilation compilation = TranslationCompiler.CompileProject(projectSource, messageSources, null, cancellationToken);
        CompiledTextCatalog? compiled = compilation.Catalogs.Count == 0 ? null : compilation.Catalogs[0];
        if (compiled is not null)
        {
            for (int index = 0; index < files.Count; index++)
            {
                WorkspaceFile file = files[index];
                string? canonicalLocale = compiled.Locales.FirstOrDefault(locale =>
                    string.Equals(locale.Tag, file.Locale, StringComparison.OrdinalIgnoreCase))?.Tag;
                if (canonicalLocale is not null) files[index] = file with { Locale = canonicalLocale };
            }
        }
        _catalogId = compiled?.Id ?? catalogId;
        if (replacementPath is null)
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
                compiled?.Locales.Count ?? 0,
                compiled?.CanonicalResources.Count ?? 0,
                errors,
                compilation.Diagnostics.Count - errors,
                compilation.Success),
        };
        return Task.FromResult(new WorkspaceState(files, compilation, summaries));
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = _catalogId is null
            ? null
            : state.Files.Find(file => file.Kind == DocumentKind.Manifest && string.Equals(file.CatalogId, _catalogId, StringComparison.Ordinal));
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);
        if (catalog is not null && state.Compilation.Catalogs.Count != 0)
        {
            CompiledTextCatalog compiledCatalog = state.Compilation.Catalogs[0];
            catalog = catalog with
            {
                DefaultLocale = compiledCatalog.DefaultLocale,
                Locales = compiledCatalog.Locales.Select(static locale => new EditorLocale(locale.Tag, locale.FallbackTag)).ToArray(),
            };
        }

        var documents = new List<EditorDocument>(state.Files.Count);
        foreach (WorkspaceFile file in state.Files)
        {
            TranslationLocaleDocument? localeDocument = file.Path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) && file.Locale is not null
                ? TranslationLocaleReader.Read(Source(file.Path, file.Content), file.Locale) : null;
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                localeDocument?.Success == false,
                file.Locale,
                file.Layer,
                localeDocument is null ? ReadEntries(file.Path, file.Content, file.Locale) : ReadEntries(localeDocument)));
        }
        EditorReviewSnapshot? review = _catalogId is null
            ? null
            : Review(TranslationEditorStateStore.Load(_root, _catalogId));
        return new WorkspaceSnapshot(_root, catalog, state.Catalogs, documents, Diagnostics(state.Compilation), state.Compilation.Success, null, review, null);
    }

    private static EditorReviewSnapshot Review(TranslationEditorStateLoadResult result) => new(
        result.Path,
        result.Revision,
        result.Error,
        result.State.Entries.Select(static entry => new EditorReviewEntry(
            entry.Key, entry.Locale, entry.State, entry.Note, entry.SourceFingerprint, entry.Samples)).ToArray(),
        result.State.Terminology.Select(static term => new EditorTerminologyEntry(
            term.Source, term.Preferred, term.Locale, term.Note)).ToArray());

    private static EditorDiagnostic[] Diagnostics(TranslationCompilation compilation)
    {
        var result = new EditorDiagnostic[compilation.Diagnostics.Count];
        for (int index = 0; index < result.Length; index++)
        {
            TranslationDiagnostic diagnostic = compilation.Diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            result[index] = new EditorDiagnostic(
                diagnostic.Id,
                diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning",
                diagnostic.Message,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn);
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
            using JsonDocument document = JsonDocument.Parse(content);
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
            return new EditorCatalog(id, schemaVersion, defaultLocale, locales, [new EditorLayer("base", 0)]);
        }
        catch (JsonException)
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
            using JsonDocument document = JsonDocument.Parse(content);
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
        string projectRoot = Path.GetDirectoryName(config)!;
        var sourceRoots = new List<string>();
        string extension;
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(config)))
        {
            string sourceLayout = StringProperty(document.RootElement, "sourceLayout") ?? string.Empty;
            extension = sourceLayout switch
            {
                "rmf2-v1" => ".rmf2",
                "locale-toml" => ".toml",
                _ => ".mf2",
            };
            if (sourceLayout == "rmf2-v1" &&
                document.RootElement.TryGetProperty("sourceRoots", out JsonElement mounts))
            {
                foreach (JsonElement mount in mounts.EnumerateArray())
                    sourceRoots.Add(Path.GetFullPath(mount.GetProperty("path").GetString()!, projectRoot));
            }
            else
            {
                sourceRoots.Add(projectRoot);
            }
        }
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [NormalizeRelativePath(Path.GetRelativePath(_root, config))] = ReadSourceBytes(ContainedPath(NormalizeRelativePath(Path.GetRelativePath(_root, config)))),
        };
        foreach (string sourceRoot in sourceRoots)
            foreach (string path in EnumerateSourceFiles(sourceRoot, extension))
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
        path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);

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

    private static string? SourceLocale(string path) => (path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
        ? Path.GetFileNameWithoutExtension(path)
        : path.Contains('/', StringComparison.Ordinal) ? path[..path.IndexOf('/')] : null;

    private static bool UsesLocaleToml(string config)
    {
        using JsonDocument document = JsonDocument.Parse(config);
        return StringProperty(document.RootElement, "sourceLayout") == "locale-toml";
    }

    private static bool UsesRmf2(string config)
    {
        using JsonDocument document = JsonDocument.Parse(config);
        return StringProperty(document.RootElement, "sourceLayout") == "rmf2-v1";
    }

    private EditorMessageEntry[] ReadEntries(string path, string content, string? locale)
    {
        if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
        {
            var workspace = Rmf2Catalog();
            return Rmf2ResourceReader.Read(Source(path, content)).Nodes.Where(node => !node.IsGroup)
                .Select(node => new EditorMessageEntry(string.Join('_', workspace.LogicalPath(path, node)), node.Message!, node.MessageByteMap[0], node.MessageByteMap[^1] - node.MessageByteMap[0])).ToArray();
        }
        if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase))
            return [new EditorMessageEntry(Path.GetFileNameWithoutExtension(path), content, 0, StrictUtf8.GetByteCount(content))];
        if (!path.EndsWith(".toml", StringComparison.OrdinalIgnoreCase) || locale is null) return [];
        TranslationLocaleDocument document = TranslationLocaleReader.Read(Source(path, content), locale);
        return ReadEntries(document);
    }

    internal TranslationWorkspaceTransactionPlan? PlanRmf2Mutation(EditorMutationRequest request)
    {
        string? config = FindMf2ProjectConfig();
        if (config is null) return null;
        using var document = JsonDocument.Parse(File.ReadAllBytes(config));
        if (StringProperty(document.RootElement, "sourceLayout") != "rmf2-v1") return null;
        var state = ReadStateAsync(null, null, CancellationToken.None).GetAwaiter().GetResult();
        var sources = state.Files.Where(file => file.Kind == DocumentKind.Resource && file.Path.EndsWith(".rmf2", StringComparison.Ordinal)).Select(file => Source(file.Path, file.Content)).ToArray();
        var workspace = new Rmf2Workspace(_root, new TranslationSource(Path.GetRelativePath(_root, config).Replace('\\', '/'), File.ReadAllBytes(config)), sources);
        if (request.Kind == "add-locale") return workspace.AddLocale(request.Locale ?? "", request.Fallback, request.CopyFromLocale);
        if (request.Kind == "remove-locale") return workspace.RemoveLocale(request.Locale ?? "", request.ReplacementFallback);
        if (request.Kind == "set-fallback") return workspace.SetFallback(request.Locale ?? "", request.Fallback);
        string[] Target() => (request.TargetKey ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (request.Kind == "create-key")
        {
            string locale = document.RootElement.GetProperty("baseLocale").GetString()!;
            string path = sources.Where(source => Path.GetFileNameWithoutExtension(source.Path) == locale).Select(source => source.Path).Order(StringComparer.Ordinal).FirstOrDefault(path => {
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

    private Rmf2Workspace Rmf2Catalog()
    {
        string config = FindMf2ProjectConfig() ?? throw new TranslationAuthoringException("No project configuration found.");
        return new Rmf2Workspace(_root, new TranslationSource(config, File.ReadAllBytes(config)), []);
    }

    private static EditorMessageEntry[] ReadEntries(TranslationLocaleDocument document) =>
        document.Entries.Select(entry => new EditorMessageEntry(entry.Key, StrictUtf8.GetString(entry.Message.GetUtf8Bytes()),
            entry.ValueLocation.StartByte, entry.ValueLocation.LengthBytes)).ToArray();

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
        TranslationCompilation Compilation,
        IReadOnlyList<EditorCatalogSummary> Catalogs);
}
