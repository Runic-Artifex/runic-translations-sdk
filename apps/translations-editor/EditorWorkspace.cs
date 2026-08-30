using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, byte> _pendingChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownRevisions = new(StringComparer.Ordinal);
    private string? _catalogId;
    private int _watcherOverflowed;
    private bool _disposed;

    public EditorWorkspace(string root, string? catalogId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _catalogId = catalogId;
        if (!Directory.Exists(_root))
            throw new DirectoryNotFoundException($"The translation workspace '{_root}' does not exist.");
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
            if (!overflowed && _pendingChanges.IsEmpty)
                return new EditorExternalChanges(false, [], []);

            TranslationWorkspaceDiscoveryResult discovery = TranslationWorkspaceDiscovery.Discover(_root, cancellationToken: cancellationToken);
            Dictionary<string, string> current = Fingerprints(discovery);
            var candidates = new HashSet<string>(_pendingChanges.Keys, StringComparer.Ordinal);
            _pendingChanges.Clear();
            if (overflowed)
            {
                candidates.UnionWith(_knownRevisions.Keys);
                candidates.UnionWith(current.Keys);
            }

            string[] changed = candidates
                .Where(path => !_knownRevisions.TryGetValue(path, out string? known) ||
                    !current.TryGetValue(path, out string? revision) ||
                    !string.Equals(known, revision, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var discoveredByPath = discovery.Files.ToDictionary(file => file.RelativePath, StringComparer.Ordinal);
            var changes = new EditorExternalFileChange[changed.Length];
            for (int index = 0; index < changed.Length; index++)
            {
                string path = changed[index];
                if (!discoveredByPath.TryGetValue(path, out TranslationWorkspaceFile? file))
                {
                    changes[index] = new EditorExternalFileChange(path, false, null, null);
                    continue;
                }
                byte[] bytes = file.GetUtf8Bytes();
                changes[index] = new EditorExternalFileChange(path, true, StrictUtf8.GetString(bytes), Revision(bytes));
            }
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
                [new EditorDiagnostic("RECOVERY", "error", "An interrupted workspace transaction requires recovery.", string.Empty, 1, 1, 1, 1)],
                false,
                new EditorPendingTransaction(pending.CatalogId, pending.Paths),
                null,
                null);
        }
        WorkspaceState state = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
        return CreateSnapshot(state);
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
            if (state.Files.Exists(file => string.Equals(file.Path, path, StringComparison.Ordinal) && file.Kind == DocumentKind.Malformed))
                return MalformedValidation(path);
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
                    [new EditorDiagnostic("PREVIEW", "error", $"The compiled locale has no message '{key}'.", path, 1, 1, 1, 1)]);
            return new EditorMessagePreview(true, locale, message.GetRawText(), diagnostics);
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException)
        {
            return new EditorMessagePreview(false, null, null,
                [new EditorDiagnostic("PREVIEW", "error", exception.Message, relativePath, 1, 1, 1, 1)]);
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
            if (!File.Exists(fullPath))
                return Failure("not-found", $"'{path}' no longer exists.");

            byte[] currentBytes = await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
            string currentRevision = Revision(currentBytes);
            if (!string.Equals(currentRevision, expectedRevision, StringComparison.Ordinal))
                return Failure("conflict", $"'{path}' changed on disk. Reload before saving your draft.");

            WorkspaceState state = await ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            if (state.Files.Exists(file => string.Equals(file.Path, path, StringComparison.Ordinal) && file.Kind == DocumentKind.Malformed))
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    "The draft is not valid JSON.",
                    null,
                    MalformedValidation(path));
            }
            if (!state.Compilation.Success)
            {
                return new EditorOperationResult(
                    false,
                    "validation",
                    "The draft contains validation errors.",
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
                return Failure("encoding", "The document contains text that cannot be encoded as UTF-8.");
            }

            string temporaryPath = Path.Combine(
                Path.GetDirectoryName(fullPath)!,
                $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, fullPath, true);
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
            return Failure("invalid-request", exception.Message);
        }
        catch (IOException exception) when (committed)
        {
            return new EditorOperationResult(true, "saved", $"The document was saved; reload the workspace to refresh it. {exception.Message}", null, null);
        }
        catch (UnauthorizedAccessException exception) when (committed)
        {
            return new EditorOperationResult(true, "saved", $"The document was saved; reload the workspace to refresh it. {exception.Message}", null, null);
        }
        catch (IOException exception)
        {
            return Failure("io", exception.Message);
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
            if (_catalogId is null) return new EditorReviewOperationResult(false, "Select a catalog before saving review data.", null, null);
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
            return new EditorReviewOperationResult(false, exception.Message, null, null);
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
            if (_catalogId is null) return new EditorReviewOperationResult(false, "Select a catalog before changing review data.", null, null);
            TranslationEditorStateLoadResult current = TranslationEditorStateStore.Load(_root, _catalogId);
            if (current.Error is not null)
                return new EditorReviewOperationResult(false, current.Error, null, null);
            if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, "The editor-state sidecar changed on disk. Reload before changing history.", null, null);
            string fullPath = ContainedPath(current.Path);
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(fullPath)) File.Delete(fullPath);
            // File.Delete is the point of no return.  Callers must reconcile their
            // history before any sidecar reload can fail or observe an external write.
            return new EditorReviewOperationResult(true, null, null, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
        {
            return new EditorReviewOperationResult(false, exception.Message, null, null);
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
                return new EditorXliffExportResult(false, "XLIFF export requires a successfully compiled catalog.", null, [], []);
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
                return new EditorReviewFileResult(false, "Review export requires a successfully compiled catalog.", null, 0);
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
                return (RefuseXliff("EDITOR-COMPILE", "The open catalog does not compile; fix the reported errors before importing."), null);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);
            TranslationXliffImportResult import = TranslationInterchange.ImportXliff21(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            CollectCatalogRefusals(catalog, import, refusals);
            CollectXliffSourceRefusals(bytes, catalog, refusals);

            int importedSchemaVersion;
            string? importedLayer;
            Dictionary<string, string> importedValues;
            using (JsonDocument parsed = JsonDocument.Parse(import.ResourceDocumentBytes))
            {
                importedSchemaVersion = parsed.RootElement.GetProperty("schemaVersion").GetInt32();
                importedLayer = parsed.RootElement.GetProperty("layer").GetString();
                importedValues = new Dictionary<string, string>(StringComparer.Ordinal);
                Flatten(parsed.RootElement.GetProperty("resources"), string.Empty, importedValues);
            }
            if (!string.Equals(import.SourceLocale, catalog.DefaultLocale, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SOURCE-LOCALE-MISMATCH",
                    $"The document source locale '{import.SourceLocale}' does not match the catalog default locale '{catalog.DefaultLocale}'."));
            if (importedLayer is null || !catalog.Layers.Any(layer => string.Equals(layer.Name, importedLayer, StringComparison.Ordinal)))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-LAYER-NOT-IN-CATALOG",
                    $"The document layer '{importedLayer ?? "(missing)"}' is not defined by catalog '{catalog.Id}'."));
            foreach (string key in importedValues.Keys.Where(key => !catalog.CanonicalResources.Any(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal))).Order(StringComparer.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-KEY-NOT-IN-CATALOG", $"The imported document defines '{key}', which is not part of catalog '{catalog.Id}'."));
            if (importedSchemaVersion != catalog.SchemaVersion)
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SCHEMA-MISMATCH",
                    $"The document declares schema version {importedSchemaVersion}; the open catalog uses version {catalog.SchemaVersion}."));

            var sidecar = TranslationEditorStateStore.Load(_root, catalog.Id);
            if (sidecar.Error is not null)
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", sidecar.Error));
            CollectApprovalFingerprintRefusals(catalog, import.Review.Entries, refusals);

            if (refusals.Count > 0)
                return (new EditorXliffImportPlan(false, null, null, import.CatalogId, import.SourceLocale, import.TargetLocale, null,
                    [], 0, 0, 0, 0, 0, false, refusals.Order(InterchangeRefusalOrder.Instance).ToArray()), null);

            string layer = importedLayer!;
            string targetDocumentPath = ResolveInterchangeTargetPath(state.Files, catalog.Id, import.TargetLocale, layer);
            WorkspaceFile? targetDocument = state.Files.FirstOrDefault(file => string.Equals(file.Path, targetDocumentPath, StringComparison.Ordinal));
            Dictionary<string, string> direct = targetDocument is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : ResourceValues(targetDocument.Content);
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
            }
            foreach ((string key, string before) in direct.Where(pair => !importedValues.ContainsKey(pair.Key)).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                removed += 1;
                Push(ref changes, ref overflowed, new EditorKeyChange(key, "removed", before, null, null, null));
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
            string? targetDocumentRevision = targetDocument?.Revision;
            byte[] documentBytes = MergeImportedResourceDocument(
                targetDocument?.Content,
                import.ResourceDocumentBytes);
            var prepared = new PreparedInterchangeImport(
                ResolveImportSourcePath(path),
                SHA256.HashData(bytes),
                import.CatalogId,
                catalog.Fingerprint,
                targetDocumentPath,
                targetDocumentRevision,
                documentBytes,
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
        bool documentCommitted = false;
        try
        {
            ThrowIfDisposed();
            byte[] source = await File.ReadAllBytesAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return Failure("import-file-changed", "The imported file changed after it was previewed. Review it again.");
            WorkspaceState currentState = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success || currentState.Compilation.Catalogs.Count != 1 ||
                !string.Equals(currentState.Compilation.Catalogs[0].Id, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentState.Compilation.Catalogs[0].Fingerprint, prepared.ExpectedCatalogFingerprint, StringComparison.Ordinal))
                return Failure("conflict", "The catalog changed on disk. Preview the import again.");
            string? sidecarRevision = TranslationEditorStateStore.Load(_root, prepared.CatalogId).Revision;
            if (!string.Equals(sidecarRevision, prepared.ExpectedSidecarRevision, StringComparison.Ordinal))
                return Failure("conflict", "The workflow sidecar changed on disk. Preview the import again.");

            if (prepared.DocumentBytes is { } documentBytes)
            {
                if (prepared.TargetDocumentPath is not { } targetDocumentPath)
                    return Failure("invalid-import", "The import preview did not identify a target translation document.");
                string fullPath = ContainedPath(targetDocumentPath);
                string? currentTargetRevision = File.Exists(fullPath)
                    ? Revision(await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false))
                    : null;
                if (!string.Equals(currentTargetRevision, prepared.ExpectedTargetDocumentRevision, StringComparison.Ordinal))
                    return Failure("conflict", "The target translation document changed on disk. Preview the import again.");
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await WriteAtomicallyAsync(fullPath, documentBytes, cancellationToken).ConfigureAwait(false);
                documentCommitted = true;
            }
            try
            {
                var state = new TranslationEditorState(prepared.CatalogId, prepared.MergedEntries, TranslationEditorStateStore.Load(_root, prepared.CatalogId).State.Terminology);
                _ = TranslationEditorStateStore.Save(_root, state, prepared.ExpectedSidecarRevision);
            }
            catch (Exception exception) when (documentCommitted && exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
            {
                return new EditorOperationResult(false, "partial-commit",
                    $"The translation document was updated but the workflow data could not be saved ({exception.Message}). Preview and apply the import again to finish it.",
                    null, null);
            }
            WorkspaceSnapshot snapshot = await LoadCoreAsync(CancellationToken.None).ConfigureAwait(false);
            return new EditorOperationResult(snapshot.Success, "imported",
                snapshot.Success ? null : string.Join(" ", snapshot.Diagnostics.Select(static diagnostic => diagnostic.Message)),
                snapshot.Success ? snapshot : null, null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Failure("io", exception.Message);
        }
        finally
        {
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
                return (RefuseReview("EDITOR-COMPILE", "The open catalog does not compile; fix the reported errors before importing."), null);
            CompiledTextCatalog catalog = state.Compilation.Catalogs[0];
            byte[] bytes = await ReadImportSourceAsync(path, cancellationToken).ConfigureAwait(false);
            TranslationInterchangeReview import = TranslationInterchange.ImportReviewJson(bytes);
            var refusals = new List<EditorInterchangeRefusal>();
            if (!string.Equals(import.CatalogId, catalog.Id, StringComparison.Ordinal))
                refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                    $"The review file targets catalog '{import.CatalogId}' but the open catalog is '{catalog.Id}'."));
            var canonicalKeys = new HashSet<string>(catalog.CanonicalResources.Select(static value => value.Key), StringComparer.Ordinal);
            var localeTags = new HashSet<string>(catalog.Locales.Select(static value => value.Tag), StringComparer.Ordinal);
            foreach (TranslationInterchangeReviewEntry entry in import.Entries)
            {
                if (!canonicalKeys.Contains(entry.Key))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-KEY-NOT-IN-CATALOG", $"The review file references '{entry.Key}', which is not part of catalog '{catalog.Id}'."));
                if (!localeTags.Contains(entry.Locale))
                    refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG", $"The review file references locale '{entry.Locale}', which the open catalog does not define."));
            }
            TranslationEditorStateLoadResult sidecar = TranslationEditorStateStore.Load(_root, catalog.Id);
            if (sidecar.Error is not null)
                refusals.Add(new EditorInterchangeRefusal("EDITOR-SIDECAR", sidecar.Error));
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
                null,
                null,
                null,
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
            byte[] source = await File.ReadAllBytesAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, "The review file changed after it was previewed. Import it again.", null, null);
            WorkspaceState currentState = await ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success || currentState.Compilation.Catalogs.Count != 1 ||
                !string.Equals(currentState.Compilation.Catalogs[0].Id, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentState.Compilation.Catalogs[0].Fingerprint, prepared.ExpectedCatalogFingerprint, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, "The catalog changed on disk. Preview the import again.", null, null);
            TranslationEditorStateLoadResult sidecar = TranslationEditorStateStore.Load(_root, prepared.CatalogId);
            if (!string.Equals(sidecar.Revision, prepared.ExpectedSidecarRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, "The workflow sidecar changed on disk. Preview the import again.", null, null);
            var state = new TranslationEditorState(prepared.CatalogId, prepared.MergedEntries, sidecar.State.Terminology);
            TranslationEditorStateLoadResult saved = TranslationEditorStateStore.Save(_root, state, prepared.ExpectedSidecarRevision);
            return new EditorReviewOperationResult(true, null, Review(saved), null);
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException)
        {
            return new EditorReviewOperationResult(false, exception.Message, null, null);
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

    private static EditorXliffImportPlan RefuseXliff(string code, string message) =>
        new(false, message, null, null, null, null, null, [], 0, 0, 0, 0, 0, false, [new EditorInterchangeRefusal(code, message)]);

    private static EditorReviewImportPlan RefuseReview(string code, string message) =>
        new(false, message, null, null, [], 0, 0, 0, false, [new EditorInterchangeRefusal(code, message)]);

    private static string InterchangeCode(Exception exception) => exception is TranslationInterchangeException interchange
        ? interchange.Code
        : exception is TranslationAuthoringException ? "EDITOR-AUTHORING" : "EDITOR-IO";

    private static string InterchangeMessage(Exception exception) => exception is TranslationInterchangeException interchange
        ? $"[{interchange.Code}] {interchange.Message}"
        : exception.Message;

    private sealed class InterchangeRefusalOrder : IComparer<EditorInterchangeRefusal>
    {
        internal static readonly InterchangeRefusalOrder Instance = new();
        public int Compare(EditorInterchangeRefusal? left, EditorInterchangeRefusal? right) =>
            string.CompareOrdinal(left?.Code, right?.Code) is var byCode && byCode != 0 ? byCode : string.CompareOrdinal(left?.Message, right?.Message);
    }

    private async Task<byte[]> ReadImportSourceAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = ResolveImportSourcePath(path);
        return await File.ReadAllBytesAsync(fullPath, cancellationToken).ConfigureAwait(false);
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

    private static Dictionary<string, string> ResourceValues(string content)
    {
        using JsonDocument document = JsonDocument.Parse(content);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("resources", out JsonElement resources))
            Flatten(resources, string.Empty, values);
        return values;
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
                $"The XLIFF source for '{key}' does not match the open catalog and cannot be applied."));
        }
    }

    private static void CollectCatalogRefusals(CompiledTextCatalog catalog, TranslationXliffImportResult import, List<EditorInterchangeRefusal> refusals)
    {
        if (!string.Equals(import.CatalogId, catalog.Id, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-CATALOG-MISMATCH",
                $"The document targets catalog '{import.CatalogId}' but the open catalog is '{catalog.Id}'."));
        if (!catalog.Locales.Any(locale => string.Equals(locale.Tag, import.TargetLocale, StringComparison.Ordinal)))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-LOCALE-NOT-IN-CATALOG",
                $"The document targets locale '{import.TargetLocale}', which the open catalog does not define."));
        if (string.Equals(import.TargetLocale, catalog.DefaultLocale, StringComparison.Ordinal))
            refusals.Add(new EditorInterchangeRefusal("EDITOR-TARGET-DEFAULT-LOCALE",
                $"The document targets default locale '{catalog.DefaultLocale}', which is canonical source text and cannot be imported through XLIFF."));
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
                $"The approved review entry '{entry.Key}' ({entry.Locale}) was created for a different source catalog revision."));
        }
    }

    private static byte[] MergeImportedResourceDocument(string? existingContent, byte[] importedBytes)
    {
        if (existingContent is null) return importedBytes;
        JsonObject? existing = JsonNode.Parse(existingContent) as JsonObject;
        using JsonDocument imported = JsonDocument.Parse(importedBytes);
        if (existing?["resources"] is not JsonObject existingResources ||
            !imported.RootElement.TryGetProperty("resources", out JsonElement importedResources))
            return importedBytes;
        MergeImportedResources(existingResources, importedResources);
        return JsonSerializer.SerializeToUtf8Bytes(existing);
    }

    private static void MergeImportedResources(JsonObject destination, JsonElement source)
    {
        foreach (JsonProperty property in source.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object &&
                destination[property.Name] is JsonObject existingChild)
            {
                MergeImportedResources(existingChild, property.Value);
                continue;
            }
            destination[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
    }

    private static string ResolveInterchangeTargetPath(IReadOnlyList<WorkspaceFile> files, string catalogId, string localeTag, string layer)
    {
        WorkspaceFile? existing = files
            .Where(file => file.Kind == DocumentKind.Resource &&
                string.Equals(file.CatalogId, catalogId, StringComparison.Ordinal) &&
                string.Equals(file.Locale, localeTag, StringComparison.Ordinal) &&
                string.Equals(file.Layer, layer, StringComparison.Ordinal))
            .OrderBy(static file => file.Path, StringComparer.Ordinal)
            .FirstOrDefault();
        return existing?.Path ?? $"{catalogId}.{localeTag}.json";
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
        TranslationWorkspaceDiscoveryResult discovery = TranslationWorkspaceDiscovery.Discover(_root, cancellationToken: cancellationToken);
        if (replacementPath is null)
        {
            ReplaceKnownRevisions(Fingerprints(discovery));
            _pendingChanges.Clear();
            Interlocked.Exchange(ref _watcherOverflowed, 0);
        }
        if (_catalogId is null && discovery.Catalogs.Count == 1)
            _catalogId = discovery.Catalogs[0].Id;
        if (_catalogId is not null && !discovery.Catalogs.Any(catalog => string.Equals(catalog.Id, _catalogId, StringComparison.Ordinal)))
            throw new ArgumentException($"Catalog '{_catalogId}' was not found in this workspace.");

        var files = new List<WorkspaceFile>(discovery.Files.Count);
        foreach (TranslationWorkspaceFile discoveredFile in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativePath = discoveredFile.RelativePath;
            byte[] bytes = discoveredFile.GetUtf8Bytes();
            string content = StrictUtf8.GetString(bytes);
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                content = replacementContent ?? string.Empty;
            DocumentKind kind = string.Equals(relativePath, replacementPath, StringComparison.Ordinal)
                ? DetectKind(content)
                : ToDocumentKind(discoveredFile.Kind);
            if (kind == DocumentKind.Unrelated) continue;
            string? catalogId = discoveredFile.CatalogId;
            string? locale = discoveredFile.Locale;
            string? layer = discoveredFile.Layer;
            if (string.Equals(relativePath, replacementPath, StringComparison.Ordinal))
                ReadFileIdentity(content, out catalogId, out locale, out layer);
            if (kind != DocumentKind.Malformed && _catalogId is not null && !string.Equals(catalogId, _catalogId, StringComparison.Ordinal))
                continue;
            files.Add(new WorkspaceFile(relativePath, content, Revision(bytes), kind, catalogId, locale, layer));
        }

        files.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        if (replacementPath is not null && !files.Exists(file => string.Equals(file.Path, replacementPath, StringComparison.Ordinal)))
            throw new ArgumentException($"'{replacementPath}' is not a translation file in this workspace.", nameof(replacementPath));

        TranslationSource[] manifests = (_catalogId is null ? Enumerable.Empty<WorkspaceFile>() : files)
            .Where(static file => file.Kind == DocumentKind.Manifest)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TranslationSource[] documents = (_catalogId is null ? Enumerable.Empty<WorkspaceFile>() : files)
            .Where(static file => file.Kind == DocumentKind.Resource)
            .Select(static file => Source(file.Path, file.Content))
            .ToArray();
        TranslationCompilation compilation = TranslationCompiler.Compile(manifests, documents, cancellationToken);
        return Task.FromResult(new WorkspaceState(files, compilation, CatalogSummaries(discovery)));
    }

    private WorkspaceSnapshot CreateSnapshot(WorkspaceState state)
    {
        EditorCatalog? catalog = null;
        WorkspaceFile? manifest = _catalogId is null
            ? null
            : state.Files.Find(file => file.Kind == DocumentKind.Manifest && string.Equals(file.CatalogId, _catalogId, StringComparison.Ordinal));
        if (manifest is not null)
            catalog = ReadCatalog(manifest.Content);

        var documents = new List<EditorDocument>(state.Files.Count);
        foreach (WorkspaceFile file in state.Files)
        {
            documents.Add(new EditorDocument(
                file.Path,
                file.Content,
                file.Revision,
                file.Kind == DocumentKind.Manifest,
                file.Kind == DocumentKind.Malformed,
                file.Locale,
                file.Layer));
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
        [new EditorDiagnostic("JSON", "error", "The document is not valid JSON.", path, 1, 1, 1, 1)]);

    private static EditorCatalog? ReadCatalog(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            string id = root.GetProperty("catalog").GetString() ?? string.Empty;
            int schemaVersion = root.GetProperty("schemaVersion").GetInt32();
            string defaultLocale = root.GetProperty("defaultLocale").GetString() ?? string.Empty;
            var locales = new List<EditorLocale>();
            foreach (JsonElement locale in root.GetProperty("locales").EnumerateArray())
            {
                locales.Add(new EditorLocale(
                    locale.GetProperty("tag").GetString() ?? string.Empty,
                    locale.TryGetProperty("fallback", out JsonElement fallback) ? fallback.GetString() : null));
            }
            var layers = new List<EditorLayer>();
            foreach (JsonElement layer in root.GetProperty("layers").EnumerateArray())
                layers.Add(new EditorLayer(layer.GetProperty("name").GetString() ?? string.Empty, layer.GetProperty("priority").GetInt32()));
            layers.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
            return new EditorCatalog(id, schemaVersion, defaultLocale, locales, layers);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ReadFileIdentity(string content, out string? catalog, out string? locale, out string? layer)
    {
        catalog = null;
        locale = null;
        layer = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            catalog = StringProperty(root, "catalog");
            locale = StringProperty(root, "locale");
            layer = StringProperty(root, "layer");
        }
        catch (JsonException)
        {
        }
    }

    private static string? StringProperty(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out JsonElement property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DocumentKind DetectKind(string content)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return DocumentKind.Unrelated;
            if (root.TryGetProperty("defaultLocale", out _) && root.TryGetProperty("locales", out _) && root.TryGetProperty("layers", out _))
                return DocumentKind.Manifest;
            if (root.TryGetProperty("locale", out _) && root.TryGetProperty("layer", out _) && root.TryGetProperty("resources", out _))
                return DocumentKind.Resource;
            return DocumentKind.Unrelated;
        }
        catch (JsonException)
        {
            return DocumentKind.Malformed;
        }
    }

    private static DocumentKind ToDocumentKind(TranslationWorkspaceFileKind kind) => kind switch
    {
        TranslationWorkspaceFileKind.CatalogManifest => DocumentKind.Manifest,
        TranslationWorkspaceFileKind.ResourceDocument => DocumentKind.Resource,
        TranslationWorkspaceFileKind.MalformedJson => DocumentKind.Malformed,
        _ => DocumentKind.Unrelated,
    };

    private static EditorCatalogSummary[] CatalogSummaries(TranslationWorkspaceDiscoveryResult discovery)
    {
        var summaries = new EditorCatalogSummary[discovery.Catalogs.Count];
        for (int index = 0; index < summaries.Length; index++)
        {
            TranslationDiscoveredCatalog catalog = discovery.Catalogs[index];
            int errors = catalog.Compilation.Diagnostics.Count(diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error);
            int warnings = catalog.Compilation.Diagnostics.Count - errors;
            CompiledTextCatalog? compiled = catalog.Compilation.Catalogs.Count == 0 ? null : catalog.Compilation.Catalogs[0];
            summaries[index] = new EditorCatalogSummary(
                catalog.Id,
                catalog.ManifestPaths,
                catalog.DocumentPaths.Count,
                compiled?.Locales.Count ?? 0,
                compiled?.CanonicalResources.Count ?? 0,
                errors,
                warnings,
                catalog.Compilation.Success);
        }
        return summaries;
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
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("Workspace paths must be relative.", nameof(relativePath));
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), _root);
        string boundary = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.Ordinal))
            throw new ArgumentException("The requested path escapes the workspace.", nameof(relativePath));
        return fullPath;
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static TranslationSource Source(string path, string content) => new(path, StrictUtf8.GetBytes(content));

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static Dictionary<string, string> Fingerprints(TranslationWorkspaceDiscoveryResult discovery)
    {
        var result = new Dictionary<string, string>(discovery.Files.Count, StringComparer.Ordinal);
        foreach (TranslationWorkspaceFile file in discovery.Files)
            result[file.RelativePath] = Revision(file.GetUtf8Bytes());
        return result;
    }

    private void ReplaceKnownRevisions(Dictionary<string, string> revisions)
    {
        _knownRevisions.Clear();
        foreach (KeyValuePair<string, string> revision in revisions)
            _knownRevisions.Add(revision.Key, revision.Value);
    }

    private void OnWatcherChanged(object sender, FileSystemEventArgs eventArgs) => QueueChange(eventArgs.FullPath);

    private void OnWatcherRenamed(object sender, RenamedEventArgs eventArgs)
    {
        QueueChange(eventArgs.OldFullPath);
        QueueChange(eventArgs.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs eventArgs) =>
        Interlocked.Exchange(ref _watcherOverflowed, 1);

    private void QueueChange(string fullPath)
    {
        if (_disposed) return;
        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
        string relativePath = NormalizeRelativePath(Path.GetRelativePath(_root, fullPath));
        if (relativePath == ".." || relativePath.StartsWith("../", StringComparison.Ordinal)) return;
        if (relativePath.StartsWith(".runic-translations/", StringComparison.Ordinal)) return;
        _pendingChanges.TryAdd(relativePath, 0);
    }

    private static EditorOperationResult Failure(string kind, string message) => new(false, kind, message, null, null);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum DocumentKind
    {
        Unrelated,
        Manifest,
        Resource,
        Malformed,
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
