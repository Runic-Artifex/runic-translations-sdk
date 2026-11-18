using System.Security.Cryptography;
using System.Text.Json;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>XLIFF and review JSON interchange: exports, and imports as a preview the session confirms and a checked commit.</summary>
internal sealed class InterchangeService
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly ReviewService _review;
    private readonly WorkspaceLoader _loader;
    private readonly string _root;

    internal InterchangeService(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, ReviewService review, WorkspaceLoader loader)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _review = review;
        _loader = loader;
        _root = files.Root;
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
            _gate.ThrowIfDisposed();
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return new EditorXliffExportResult(false, EditorNotice.Create("ui_backend_xliff_compile"), null, [], []);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            TranslationInterchangeReview review = _review.ReviewStore(catalog.CatalogId).ExportReview();
            TranslationXliffExportResult export = TranslationInterchange.ExportXliff21(state.Compilation, review);
            string targetDirectory = ResolveInterchangeOutputDirectory(directory, Path.Combine(".runic-translations", "export"), allowRootedOutput);
            Directory.CreateDirectory(targetDirectory);
            var documents = new List<EditorInterchangeFile>(export.Documents.Count);
            foreach (TranslationXliffDocument document in export.Documents)
            {
                string fullPath = Path.Combine(targetDirectory, $"{catalog.CatalogId}.{document.TargetLocale}.xliff");
                await WriteAtomicallyAsync(fullPath, document.Bytes.ToArray(), cancellationToken).ConfigureAwait(false);
                documents.Add(new EditorInterchangeFile(
                    _files.ReportedOutputPath(fullPath), document.TargetLocale, (long)document.Bytes.Length));
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
            _gate.ThrowIfDisposed();
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!state.Compilation.Success)
                return new EditorReviewFileResult(false, EditorNotice.Create("ui_backend_review_compile"), null, 0);
            TranslationInterchangeProjection catalog = TranslationInterchange.PreflightXliff21(state.Compilation);
            TranslationInterchangeReview review = _review.ReviewStore(catalog.CatalogId).ExportReview();
            byte[] bytes = TranslationInterchange.ExportReviewJson(review);
            string targetPath = Path.Combine(
                ResolveInterchangeOutputDirectory(path is null ? null : Path.GetDirectoryName(path), Path.Combine(".runic-translations", "export"), allowRootedOutput),
                path is null ? $"{catalog.CatalogId}.review.json" : Path.GetFileName(path));
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await WriteAtomicallyAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
            return new EditorReviewFileResult(
                true,
                null,
                _files.ReportedOutputPath(targetPath),
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
            _gate.ThrowIfDisposed();
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
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
            EditorReviewStore store = _review.ReviewStore(catalog.CatalogId);
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
            string configPath = _files.FindMf2ProjectConfig()!;
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
            _gate.ThrowIfDisposed();
            byte[] source = await ReadImportSourceAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return Failure("import-file-changed", EditorNotice.Create("ui_backend_import_file_changed"));
            WorkspaceState currentState = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success)
                return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
            TranslationInterchangeProjection currentCatalog = TranslationInterchange.PreflightXliff21(currentState.Compilation);
            if (!string.Equals(currentCatalog.CatalogId, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentCatalog.SourceFreshness, prepared.ExpectedSourceFreshness, StringComparison.Ordinal))
                return Failure("conflict", EditorNotice.Create("ui_backend_import_catalog_changed"));
            EditorReviewStore store = _review.ReviewStore(prepared.CatalogId);
            (string? reviewRevision, byte[]? reviewBytes) = store.CurrentState();
            if (!string.Equals(reviewRevision, prepared.ExpectedReviewRevision, StringComparison.Ordinal))
                return Failure("conflict", EditorNotice.Create("ui_backend_import_sidecar_changed"));

            foreach (PreparedInterchangeDocument document in prepared.Documents)
            {
                string fullPath = _files.ContainedPath(document.Path);
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
            WorkspaceSnapshot snapshot = await _loader.LoadCoreAsync(CancellationToken.None).ConfigureAwait(false);
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
            _gate.ThrowIfDisposed();
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
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
            EditorReviewStore store = _review.ReviewStore(catalog.CatalogId);
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
            _gate.ThrowIfDisposed();
            byte[] source = await ReadImportSourceAsync(prepared.SourcePath, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(SHA256.HashData(source)).Equals(Convert.ToHexStringLower(prepared.SourceHash), StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_review_file_changed"), null, null);
            WorkspaceState currentState = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            if (!currentState.Compilation.Success)
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
            TranslationInterchangeProjection currentCatalog = TranslationInterchange.PreflightXliff21(currentState.Compilation);
            if (!string.Equals(currentCatalog.CatalogId, prepared.CatalogId, StringComparison.Ordinal) ||
                !string.Equals(currentCatalog.SourceFreshness, prepared.ExpectedSourceFreshness, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_catalog_changed"), null, null);
            EditorReviewStore store = _review.ReviewStore(prepared.CatalogId);
            (string? reviewRevision, byte[]? reviewBytes) = store.CurrentState();
            if (!string.Equals(reviewRevision, prepared.ExpectedReviewRevision, StringComparison.Ordinal))
                return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_import_sidecar_changed"), null, null);
            if (prepared.ReviewBytes is not null) store.WriteReview(prepared.ReviewBytes, reviewBytes);
            return new EditorReviewOperationResult(true, null, _review.KnownReview(store.Load(currentState.Compilation)), null);
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
        return Path.IsPathRooted(path) ? Path.GetFullPath(path) : _files.ContainedPath(NormalizeRelativePath(path));
    }

    private string ResolveInterchangeOutputDirectory(string? directory, string defaultRelative, bool allowRootedOutput)
    {
        if (directory is null or "") return _files.ContainedPath(defaultRelative);
        if (allowRootedOutput && Path.IsPathRooted(directory)) return Path.GetFullPath(directory);
        return _files.ContainedPath(NormalizeRelativePath(directory));
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
}
