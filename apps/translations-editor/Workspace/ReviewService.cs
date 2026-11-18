using System.Globalization;
using System.Text;
using Runic.Translations.Tooling;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>
/// The open catalog's review file: saving and deleting review data, the revision the frontend has (for the
/// external-change check), and the review entries other operations update (starter drafts of an added locale, drafts a
/// save promotes to translated, a removed locale's entries).
/// </summary>
internal sealed class ReviewService
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly string _root;
    private string? _knownReviewRevision;

    internal ReviewService(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _root = files.Root;
    }

    public async Task<EditorReviewOperationResult> SaveReviewAsync(
        EditorReviewSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            if (_reader.CatalogId is null) return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_select_catalog_review"), null, null);
            // Notes, samples and terminology always save. Review states carry the fingerprint of the source the user
            // reviewed (from the snapshot); the review compilation tells which of them predate a change on disk, and it
            // works while translations are incomplete or invalid (TranslationReviewFile.ForReview).
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            EditorReviewSnapshot saved = KnownReview(ReviewStore(_reader.CatalogId).Save(request, ReviewCompilation(state, cancellationToken)));
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
            _gate.ThrowIfDisposed();
            if (_reader.CatalogId is null) return new EditorReviewOperationResult(false, EditorNotice.Create("ui_backend_select_catalog_review_change"), null, null);
            EditorReviewStore store = ReviewStore(_reader.CatalogId);
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

    internal EditorReviewStore ReviewStore(string catalogId) =>
        new(_root, _files.FindMf2ProjectConfig() ?? throw new EditorUserException(EditorNotice.Create("ui_backend_project_required")), catalogId);

    // The review data when another writer (runic-translations review mark or xliff import, the language server's
    // "Mark reviewed") changed the review file since the frontend received it; null when unchanged. Read without
    // moving the external-change baseline, so source changes are still reported on their own.
    internal async Task<EditorReviewSnapshot?> ExternalReviewAsync(CancellationToken cancellationToken)
    {
        if (_reader.CatalogId is null || _files.FindMf2ProjectConfig() is not { } projectPath) return null;
        var store = new EditorReviewStore(_root, projectPath, _reader.CatalogId);
        if (string.Equals(store.CurrentRevision(), _knownReviewRevision, StringComparison.Ordinal)) return null;
        try
        {
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken, resetBaseline: false).ConfigureAwait(false);
            return KnownReview(store.Load(ReviewCompilation(state, cancellationToken)));
        }
        catch (Exception exception) when (exception is TranslationEditorStateException or TranslationInterchangeException or EditorUserException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Remembers the review revision the frontend has, so the external-change poll reports another writer's change.
    internal EditorReviewSnapshot KnownReview(EditorReviewSnapshot snapshot)
    {
        _knownReviewRevision = snapshot.Revision;
        return snapshot;
    }

    // The base-locale value of every key as the editor's frontend fingerprinted it before 0.9: an .rmf2 entry's
    // message, a direct .mf2 file's content. Used only to move version 1 editor approvals (EditorReviewStore).

    internal static Dictionary<string, string> BaseValues(WorkspaceState state, Rmf2ProjectCompilation reviewCompilation)
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

    /// <summary>The open catalog's review file relative to the workspace, or null without a catalog.</summary>
    internal string? ReviewDisplayPath() =>
        _reader.CatalogId is null || _files.FindMf2ProjectConfig() is not { } config || !TranslationReviewFile.IsFileName(_reader.CatalogId) ? null
            : NormalizeRelativePath(Path.GetRelativePath(_root, TranslationReviewFile.PathFor(config, _reader.CatalogId)));

    /// <summary>
    /// After a locale's addition: its starter messages, copies of another locale's text, are recorded as <c>draft</c> in
    /// the review file (XLIFF's <c>initial</c>), so status and the language list count them as missing, not as 100 %
    /// translated, until they are translated: saving an edited one records it <c>translated</c>, as
    /// <c>runic-translations review mark</c> does. An existing entry of the locale's messages (left by an earlier
    /// removal) is replaced.
    /// </summary>
    internal void RecordStarterDrafts(string locale, IReadOnlyCollection<string> createdFiles)
    {
        if (_files.FindMf2ProjectConfig() is not { } config) return;
        WorkspaceState state = _reader.ReadStateAsync(null, null, CancellationToken.None, resetBaseline: false).GetAwaiter().GetResult();
        Rmf2ProjectCompilation review = ReviewCompilation(state);
        if (_reader.CatalogId is null || review.Project is not { } project ||
            project.Locales.FirstOrDefault(item => string.Equals(item.Tag, locale, StringComparison.OrdinalIgnoreCase)) is not { } added || added.Tag == project.DefaultLocale) return;
        // Only the messages of the files the addition created: a file that was already there is the locale's own text.
        string[] keys = added.DirectResources.Where(resource => createdFiles.Contains(resource.SourceLocation.Path))
            .Select(static resource => resource.Key).Distinct(StringComparer.Ordinal).ToArray();
        if (keys.Length == 0) return;
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        string catalog = _reader.CatalogId;
        TranslationReviewFile.Update(TranslationReviewFile.PathFor(config, catalog), catalog,
            existing => TranslationReviewFile.Mark(existing, catalog, keys.Where(fingerprints.ContainsKey), added.Tag, TranslationReviewState.Draft, null, fingerprints));
    }

    // The draft review entries of a translated locale's messages that one saved file contains, and their text before the
    // save; null when there are none (the usual case, which needs no extra compilation).
    internal (string Locale, Dictionary<string, string> Before)? DraftsBeforeSave(WorkspaceState saved, string path)
    {
        if (_reader.CatalogId is null || _files.FindMf2ProjectConfig() is not { } config || saved.Compilation.Project is not { } project ||
            saved.Files.Find(file => file.Path == path)?.Locale is not { } locale || locale == project.DefaultLocale) return null;
        TranslationInterchangeReview? review;
        try { review = TranslationReviewFile.Read(TranslationReviewFile.PathFor(config, _reader.CatalogId), _reader.CatalogId); }
        catch (Exception exception) when (exception is TranslationInterchangeException or IOException or UnauthorizedAccessException) { return null; }
        var drafts = (review?.Entries ?? []).Where(entry => entry.State == TranslationReviewState.Draft && entry.Locale == locale).Select(static entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        if (drafts.Count == 0) return null;
        // The sources on disk, before this save.
        WorkspaceState before = _reader.ReadStateAsync(null, null, CancellationToken.None, resetBaseline: false).GetAwaiter().GetResult();
        Rmf2ProjectCompilation compiled = ReviewCompilation(before);
        if (compiled.Project?.Locales.FirstOrDefault(item => item.Tag == locale) is not { } previous) return null;
        var texts = previous.DirectResources.Where(resource => drafts.Contains(resource.Key) && resource.SourceLocation.Path == path)
            .ToDictionary(static resource => resource.Key, static resource => Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparer.Ordinal);
        return texts.Count == 0 ? null : (locale, texts);
    }

    // After a save: each draft message of the file whose text changed is recorded translated, for its current source.
    internal void PromoteEditedDrafts(WorkspaceState saved, string locale, Dictionary<string, string> before)
    {
        if (_reader.CatalogId is null || _files.FindMf2ProjectConfig() is not { } config) return;
        Rmf2ProjectCompilation review = ReviewCompilation(saved);
        if (review.Project?.Locales.FirstOrDefault(item => item.Tag == locale) is not { } current) return;
        string[] edited = current.DirectResources.Where(resource => before.TryGetValue(resource.Key, out string? text) &&
            !string.Equals(text, Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparison.Ordinal)).Select(static resource => resource.Key).ToArray();
        if (edited.Length == 0) return;
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        string catalog = _reader.CatalogId;
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
        if (_reader.CatalogId is null || _files.FindMf2ProjectConfig() is not { } config) return;
        string reviewPath = TranslationReviewFile.PathFor(config, _reader.CatalogId);
        // Compare-and-swap: a write by the command line or the language server in between is kept.
        TranslationReviewFile.Update(reviewPath, _reader.CatalogId, existing => existing is null || !existing.Entries.Any(entry => string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))
            ? existing
            : new TranslationInterchangeReview(existing.CatalogId, existing.Entries.Where(entry => !string.Equals(entry.Locale, locale, StringComparison.OrdinalIgnoreCase))));
    }
}
