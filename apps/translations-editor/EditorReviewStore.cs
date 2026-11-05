using System.Security.Cryptography;
using System.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Editor;

/// <summary>
/// The editor's review data, read and written in two files: review states and per-unit source fingerprints in the
/// SDK review file <c>&lt;catalog&gt;.review.json</c> beside runic.json (through <see cref="TranslationReviewFile"/>,
/// the same file and rules as <c>runic-translations review mark</c>, <c>xliff import</c> and <c>validate</c>), and
/// notes, preview samples and terminology in the editor's own <c>.runic-translations/&lt;catalog&gt;.editor-state.json</c>.
/// "Stale" is the SDK definition: the entry's fingerprint differs from the message's current one (RTR0090).
/// The snapshot revision covers both files, so a write by the command line conflicts like an editor write.
/// </summary>
internal sealed class EditorReviewStore
{
    private readonly string _root;
    private readonly string _catalogId;

    internal EditorReviewStore(string root, string projectPath, string catalogId)
    {
        _root = root;
        _catalogId = catalogId;
        ReviewPath = TranslationReviewFile.PathFor(projectPath, catalogId);
    }

    /// <summary>The full path of the SDK review file.</summary>
    internal string ReviewPath { get; }

    internal string DisplayPath => Path.GetRelativePath(_root, ReviewPath).Replace('\\', '/');

    private sealed record Files(TranslationEditorStateLoadResult EditorState, byte[]? ReviewBytes, TranslationInterchangeReview? Review, string? ReviewError)
    {
        internal string? Revision => CombinedRevision(ReviewBytes, EditorState.Revision);
    }

    /// <summary>
    /// The review snapshot. <paramref name="compilation"/> supplies the current fingerprints; without a compiled catalog
    /// no entry is marked stale. A version 1 editor-state file is moved into the review file first when
    /// <paramref name="migrationBaseValues"/> is supplied (the editor's former per-value fingerprints of the base text).
    /// </summary>
    internal EditorReviewSnapshot Load(Rmf2ProjectCompilation? compilation, IReadOnlyDictionary<string, string>? migrationBaseValues = null)
    {
        Files files = Read();
        EditorNotice? notice = null;
        if (NeedsMigration(files))
        {
            if (compilation?.Success != true || compilation.Project is null || migrationBaseValues is null || files.ReviewError is not null)
                return Snapshot(files, [], "The editor's review states move to " + DisplayPath + " once the catalog compiles without errors.",
                    EditorNotice.Create("ui_backend_review_migration_pending", ("path", DisplayPath)));
            notice = Migrate(files, compilation, migrationBaseValues);
            files = Read();
        }
        string? error = files.EditorState.Error ?? files.ReviewError;
        return Snapshot(files, Entries(files, compilation), error, notice);
    }

    private EditorReviewSnapshot Snapshot(Files files, IReadOnlyList<EditorReviewEntry> entries, string? error, EditorNotice? notice) => new(
        DisplayPath,
        files.Revision,
        error,
        entries,
        files.EditorState.State.Terminology.Select(static term => new EditorTerminologyEntry(term.Source, term.Preferred, term.Locale, term.Note)).ToArray(),
        notice);

    /// <summary>Writes the review entries to the review file and notes, samples and terminology to the editor state.</summary>
    internal EditorReviewSnapshot Save(EditorReviewSaveRequest request, Rmf2ProjectCompilation compilation)
    {
        Files files = Read();
        if (!string.Equals(files.Revision, request.ExpectedRevision, StringComparison.Ordinal))
            throw new TranslationEditorStateException("The review data changed on disk. Reload before saving review data.");
        if (files.ReviewError is not null) throw new TranslationEditorStateException(files.ReviewError);
        if (NeedsMigration(files)) throw new TranslationEditorStateException("Open the catalog without errors once so the editor can move its review states into " + DisplayPath + ".");
        IReadOnlyDictionary<string, string> fingerprints = compilation.Project is null ? new Dictionary<string, string>() : TranslationInterchange.ComputeSourceFingerprints(compilation);
        var existing = (files.Review?.Entries ?? []).ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        var reviewEntries = new List<TranslationInterchangeReviewEntry>(request.Entries.Count);
        var stateEntries = new List<TranslationEditorStateEntry>();
        foreach (EditorReviewEntry entry in request.Entries)
        {
            if (!TranslationReviewFile.TryParseState(entry.State, out TranslationReviewState state))
                throw new TranslationEditorStateException($"Unknown review state '{entry.State}'.");
            existing.TryGetValue(TranslationReviewFile.Identity(entry.Key, entry.Locale), out TranslationInterchangeReviewEntry? current);
            // An entry the user set again carries no fingerprint and is recorded for the current source.
            string? fingerprint = entry.SourceFingerprint ?? fingerprints.GetValueOrDefault(entry.Key);
            reviewEntries.Add(new TranslationInterchangeReviewEntry(entry.Key, entry.Locale, state, current?.Note, fingerprint));
            // The review file keeps its own note (from XLIFF); the editor's note is kept where it differs.
            string? note = entry.Note == current?.Note ? null : entry.Note;
            if (note is not null || entry.Samples.Count > 0)
                stateEntries.Add(new TranslationEditorStateEntry(entry.Key, entry.Locale, null, note,
                    null, new SortedDictionary<string, string>(entry.Samples.ToDictionary(), StringComparer.Ordinal)));
        }
        var review = new TranslationInterchangeReview(_catalogId, reviewEntries);
        var editorState = new TranslationEditorState(_catalogId, stateEntries,
            request.Terminology.Select(static term => new TranslationTerminologyEntry(term.Source, term.Preferred, term.Locale, term.Note)).ToArray());
        byte[] reviewBytes = TranslationReviewFile.Render(review);
        if (files.ReviewBytes is null ? reviewEntries.Count != 0 : !files.ReviewBytes.AsSpan().SequenceEqual(reviewBytes))
            WriteReview(reviewBytes);
        bool stateExists = files.EditorState.Revision is not null;
        if (stateExists || stateEntries.Count != 0 || editorState.Terminology.Count != 0)
        {
            byte[] stateBytes = TranslationEditorStateStore.RenderBytes(editorState);
            if (!stateExists || Revision(stateBytes) != files.EditorState.Revision)
                TranslationEditorStateStore.Save(_root, editorState, files.EditorState.Revision);
        }
        return Load(compilation);
    }

    /// <summary>Removes both files (undoing the first review save) when they still have <paramref name="expectedRevision"/>.</summary>
    internal void Delete(string? expectedRevision)
    {
        Files files = Read();
        if (!string.Equals(files.Revision, expectedRevision, StringComparison.Ordinal))
            throw new TranslationEditorStateException("The review data changed on disk. Reload before saving review data.");
        if (files.ReviewBytes is not null) File.Delete(ReviewPath);
        TranslationEditorStateStore.Delete(_root, _catalogId, files.EditorState.Revision);
    }

    /// <summary>The review file's entries with the editor's notes where the file has none, for XLIFF and review-file export.</summary>
    internal TranslationInterchangeReview ExportReview()
    {
        Files files = Read();
        if (files.ReviewError is not null) throw new TranslationInterchangeException("REVIEW-INVALID", files.ReviewError);
        var notes = files.EditorState.State.Entries.Where(static entry => entry.Note is not null)
            .ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), static entry => entry.Note!, StringComparer.Ordinal);
        return new TranslationInterchangeReview(_catalogId, (files.Review?.Entries ?? []).Select(entry =>
            entry.Note is null && notes.TryGetValue(TranslationReviewFile.Identity(entry.Key, entry.Locale), out string? note)
                ? new TranslationInterchangeReviewEntry(entry.Key, entry.Locale, entry.State, note, entry.SourceFingerprint) : entry));
    }

    /// <summary>The review file as stored (null when missing) and the combined review revision.</summary>
    internal (TranslationInterchangeReview? Review, byte[]? Bytes, string? Revision) ReadReview()
    {
        Files files = Read();
        if (files.ReviewError is not null) throw new TranslationInterchangeException("REVIEW-INVALID", files.ReviewError);
        if (NeedsMigration(files)) throw new TranslationEditorStateException("Open the catalog without errors once so the editor can move its review states into " + DisplayPath + ".");
        return (files.Review, files.ReviewBytes, files.Revision);
    }

    internal string? CurrentRevision() => Read().Revision;

    internal void WriteReview(byte[] bytes)
    {
        string directory = Path.GetDirectoryName(ReviewPath)!;
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(ReviewPath) && (File.GetAttributes(ReviewPath) & FileAttributes.ReparsePoint) != 0))
            throw new TranslationEditorStateException("The review file cannot be a link or reparse point.");
        string temporary = Path.Combine(directory, $".{Path.GetFileName(ReviewPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, ReviewPath, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static string? CombinedRevision(byte[]? reviewBytes, string? editorStateRevision) =>
        reviewBytes is null && editorStateRevision is null ? null
            : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                (reviewBytes is null ? "-" : Revision(reviewBytes)) + ":" + (editorStateRevision ?? "-"))));

    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private Files Read()
    {
        TranslationEditorStateLoadResult editorState = TranslationEditorStateStore.Load(_root, _catalogId);
        byte[]? bytes = null;
        TranslationInterchangeReview? review = null;
        string? error = null;
        try
        {
            if (File.Exists(ReviewPath))
            {
                if ((File.GetAttributes(ReviewPath) & FileAttributes.ReparsePoint) != 0)
                    throw new TranslationEditorStateException("The review file cannot be a link or reparse point.");
                if (new FileInfo(ReviewPath).Length > TranslationReviewFile.MaximumBytes)
                    throw new TranslationEditorStateException("The review file exceeds 8 MiB.");
                bytes = File.ReadAllBytes(ReviewPath);
                review = TranslationReviewFile.Parse(bytes, _catalogId);
            }
        }
        catch (Exception exception) when (exception is TranslationInterchangeException or TranslationEditorStateException or IOException or UnauthorizedAccessException)
        {
            error = DisplayPath + ": " + exception.Message;
            review = null;
        }
        return new Files(editorState, bytes, review, error);
    }

    private static bool NeedsMigration(Files files) =>
        files.EditorState.Error is null && files.EditorState.SchemaVersion == 1 && files.EditorState.State.Entries.Any(static entry => entry.State is not null);

    private static EditorReviewEntry[] Entries(Files files, Rmf2ProjectCompilation? compilation)
    {
        Rmf2ProjectV5? project = compilation?.Success == true ? compilation.Project : null;
        IReadOnlyDictionary<string, string> fingerprints = project is null ? new Dictionary<string, string>() : TranslationInterchange.ComputeSourceFingerprints(compilation!);
        var editorEntries = files.EditorState.State.Entries.ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        var result = new List<EditorReviewEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TranslationInterchangeReviewEntry entry in files.Review?.Entries ?? [])
        {
            string identity = TranslationReviewFile.Identity(entry.Key, entry.Locale);
            seen.Add(identity);
            editorEntries.TryGetValue(identity, out TranslationEditorStateEntry? local);
            bool stale = project is not null && entry.Locale != project.DefaultLocale && TranslationReviewFile.IsStale(entry, fingerprints);
            result.Add(new EditorReviewEntry(entry.Key, entry.Locale, TranslationReviewFile.StateText(entry.State), local?.Note ?? entry.Note,
                entry.SourceFingerprint, local?.Samples ?? new Dictionary<string, string>(StringComparer.Ordinal), stale));
        }
        // A note or samples without a review entry: the state the message has without one.
        foreach (TranslationEditorStateEntry local in files.EditorState.State.Entries)
        {
            if (!seen.Add(TranslationReviewFile.Identity(local.Key, local.Locale))) continue;
            bool translated = project?.Locales.FirstOrDefault(locale => locale.Tag == local.Locale)?.DirectResources.Any(resource => resource.Key == local.Key) == true;
            result.Add(new EditorReviewEntry(local.Key, local.Locale, translated ? "translated" : "draft", local.Note, null, local.Samples));
        }
        return result.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ThenBy(static entry => entry.Locale, StringComparer.Ordinal).ToArray();
    }

    // The one-time move of a version 1 editor-state file (D055.5): its review states go into the review file, each with the
    // current per-unit fingerprint. An approval is kept only where its recorded value still matches the current base text;
    // every other recorded state whose base text changed becomes needs-review. An entry the review file already has wins.
    // Notes, samples and terminology stay in the editor state, which is rewritten as version 2.
    private EditorNotice Migrate(Files files, Rmf2ProjectCompilation compilation, IReadOnlyDictionary<string, string> baseValues)
    {
        Rmf2ProjectV5 project = compilation.Project!;
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
        var translatedLocales = new HashSet<string>(project.Locales.Select(static locale => locale.Tag).Where(tag => tag != project.DefaultLocale), StringComparer.Ordinal);
        var merged = (files.Review?.Entries ?? []).ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        int moved = 0, demoted = 0;
        foreach (TranslationEditorStateEntry entry in files.EditorState.State.Entries)
        {
            string identity = TranslationReviewFile.Identity(entry.Key, entry.Locale);
            if (entry.State is null || merged.ContainsKey(identity) || !translatedLocales.Contains(entry.Locale) ||
                !fingerprints.TryGetValue(entry.Key, out string? fingerprint) || !TranslationReviewFile.TryParseState(entry.State, out TranslationReviewState state)) continue;
            bool current = entry.SourceFingerprint is not null && baseValues.TryGetValue(entry.Key, out string? value) &&
                string.Equals(entry.SourceFingerprint, EditorWorkspace.EditorSourceFingerprint(value), StringComparison.Ordinal);
            if (!current && (state == TranslationReviewState.Approved || (state != TranslationReviewState.Draft && entry.SourceFingerprint is not null)))
            {
                state = TranslationReviewState.NeedsReview;
                demoted++;
            }
            merged[identity] = new TranslationInterchangeReviewEntry(entry.Key, entry.Locale, state, null, fingerprint);
            moved++;
        }
        byte[] reviewBytes = TranslationReviewFile.Render(new TranslationInterchangeReview(_catalogId, merged.Values));
        // The review file is written first: if the editor state cannot be rewritten, the next open moves the same
        // entries again, and the entries already in the review file win.
        if (files.ReviewBytes is null || !files.ReviewBytes.AsSpan().SequenceEqual(reviewBytes)) WriteReview(reviewBytes);
        TranslationEditorStateStore.Save(_root, files.EditorState.State, files.EditorState.Revision);
        return new EditorNotice("ui_backend_review_migrated",
            [new EditorNoticeArgument("count", moved.ToString(System.Globalization.CultureInfo.InvariantCulture), moved),
             new EditorNoticeArgument("demoted", demoted.ToString(System.Globalization.CultureInfo.InvariantCulture), demoted),
             new EditorNoticeArgument("path", DisplayPath)]);
    }
}
