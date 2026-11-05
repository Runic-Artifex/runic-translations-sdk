using System.Security.Cryptography;
using System.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Editor;

/// <summary>
/// The editor's review data, read and written in two files: review states, notes and per-unit source fingerprints in
/// the SDK review file <c>&lt;catalog&gt;.review.json</c> beside runic.json (through <see cref="TranslationReviewFile"/>,
/// the same file and rules as <c>runic-translations review mark</c>, <c>xliff import</c>, <c>validate</c> and the
/// language server), and preview samples, terminology and the notes of rows without a review entry in the editor's own
/// <c>.runic-translations/&lt;catalog&gt;.editor-state.json</c>.
/// "Stale" is the SDK definition: the entry's fingerprint differs from the message's current one (RTR0090).
/// The compilation passed in is the review compilation (<see cref="TranslationReviewFile.ForReview"/>), so review data
/// is read and written while translations are incomplete or invalid; only errors in runic.json or the base locale
/// leave it without fingerprints. The snapshot revision covers both files; a save whose revision is out of date
/// re-applies the user's changes onto the current files (other writers change the review file routinely).
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

    /// <summary>The per-unit fingerprints of a review compilation, or null when runic.json or the base locale has errors.</summary>
    internal static IReadOnlyDictionary<string, string>? Fingerprints(Rmf2ProjectCompilation? compilation) =>
        compilation is { Success: true, Project: not null } ? TranslationInterchange.ComputeSourceFingerprints(compilation) : null;

    /// <summary>
    /// The review snapshot. <paramref name="compilation"/> (a review compilation) supplies the current fingerprints;
    /// without them no entry is marked stale and no state can be recorded. A version 1 editor-state file is moved into
    /// the review file first when <paramref name="migrationBaseValues"/> is supplied (the editor's former per-value
    /// fingerprints of the base text); until it can move, its states are shown read-only.
    /// </summary>
    internal EditorReviewSnapshot Load(Rmf2ProjectCompilation? compilation, IReadOnlyDictionary<string, string>? migrationBaseValues = null)
    {
        Files files = Read();
        IReadOnlyDictionary<string, string>? fingerprints = Fingerprints(compilation);
        EditorNotice? notice = null;
        if (NeedsMigration(files))
        {
            if (fingerprints is null || migrationBaseValues is null || files.ReviewError is not null)
                return Snapshot(files, PendingEntries(files),
                    files.ReviewError ?? "The editor's review states move to " + DisplayPath + " once runic.json and the base locale have no errors.",
                    EditorNotice.Create("ui_backend_review_migration_pending", ("path", DisplayPath)), fingerprints, migrationPending: true);
            notice = Migrate(files, compilation!, fingerprints, migrationBaseValues);
            files = Read();
        }
        string? error = files.EditorState.Error ?? files.ReviewError;
        return Snapshot(files, Entries(files, compilation, fingerprints), error, notice, fingerprints);
    }

    private EditorReviewSnapshot Snapshot(Files files, IReadOnlyList<EditorReviewEntry> entries, string? error, EditorNotice? notice,
        IReadOnlyDictionary<string, string>? fingerprints, bool migrationPending = false) => new(
        DisplayPath,
        files.Revision,
        error,
        entries,
        Terminology(files),
        notice,
        fingerprints,
        migrationPending);

    private static EditorTerminologyEntry[] Terminology(Files files) =>
        files.EditorState.State.Terminology.Select(static term => new EditorTerminologyEntry(term.Source, term.Preferred, term.Locale, term.Note)).ToArray();

    /// <summary>
    /// Writes the review entries (state, note and the fingerprint of the source the user reviewed) to the review file,
    /// and samples, terminology and the notes of rows without a review entry to the editor state. Notes, samples and
    /// terminology always save; a state the user set needs its fingerprint, which the frontend takes from the snapshot
    /// it loaded. A state recorded for a source that changed on disk since is saved with that older fingerprint, so it
    /// shows as stale (RTR0090) instead of approving text nobody saw. When the files changed since
    /// <see cref="EditorReviewSaveRequest.ExpectedRevision"/>, the user's changes are re-applied onto them.
    /// </summary>
    internal EditorReviewSnapshot Save(EditorReviewSaveRequest request, Rmf2ProjectCompilation? compilation)
    {
        Files files = Read();
        if (files.ReviewError is not null) throw new TranslationEditorStateException(files.ReviewError);
        if (files.EditorState.Error is not null) throw new TranslationEditorStateException(files.EditorState.Error);
        if (NeedsMigration(files))
            throw new TranslationEditorStateException("Fix the errors in runic.json or the base locale once so the editor can move its review states into " + DisplayPath + ".");
        IReadOnlyDictionary<string, string>? fingerprints = Fingerprints(compilation);
        IReadOnlyList<EditorReviewEntry> entries = request.Entries;
        IReadOnlyList<EditorTerminologyEntry> terminology = request.Terminology;
        if (!string.Equals(files.Revision, request.ExpectedRevision, StringComparison.Ordinal))
        {
            if (request.Original is null || request.OriginalTerminology is null)
                throw new TranslationEditorStateException("The review data changed on disk. Reload before saving review data.");
            (entries, terminology) = Rebase(request, Entries(files, compilation, fingerprints), Terminology(files));
        }
        string? baseLocale = compilation is { Success: true, Project: { } project } ? project.DefaultLocale : null;
        var existing = (files.Review?.Entries ?? []).ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        var reviewEntries = new List<TranslationInterchangeReviewEntry>(entries.Count);
        var stateEntries = new List<TranslationEditorStateEntry>();
        var unstamped = new List<string>();
        int changedSince = 0;
        foreach (EditorReviewEntry entry in entries)
        {
            if (!TranslationReviewFile.TryParseState(entry.State, out TranslationReviewState state))
                throw new TranslationEditorStateException($"Unknown review state '{entry.State}'.");
            existing.TryGetValue(TranslationReviewFile.Identity(entry.Key, entry.Locale), out TranslationInterchangeReviewEntry? current);
            SortedDictionary<string, string>? samples = entry.Samples.Count == 0 ? null : new(entry.Samples.ToDictionary(), StringComparer.Ordinal);
            if (entry.Implicit && current is null)
            {
                // A note or samples alone: the row has no review state anybody chose, so the review file gets no entry.
                if (entry.Note is not null || samples is not null)
                    stateEntries.Add(new TranslationEditorStateEntry(entry.Key, entry.Locale, null, entry.Note, null, samples ?? new(StringComparer.Ordinal)));
                continue;
            }
            // The fingerprint of the source the user reviewed; an entry the user did not set again keeps its own.
            string? fingerprint = entry.SourceFingerprint ?? current?.SourceFingerprint;
            bool recorded = current is null || current.State != state || !string.Equals(current.SourceFingerprint, fingerprint, StringComparison.Ordinal);
            if (recorded && fingerprint is null) unstamped.Add(entry.Key + " (" + entry.Locale + ")");
            else if (recorded && entry.Locale != baseLocale && fingerprints is not null && fingerprints.TryGetValue(entry.Key, out string? now) &&
                !string.Equals(now, fingerprint, StringComparison.Ordinal)) changedSince++;
            // The note lives on the review entry, so an edited or cleared note (also one from XLIFF) is what is exported.
            reviewEntries.Add(new TranslationInterchangeReviewEntry(entry.Key, entry.Locale, state, entry.Note, fingerprint));
            if (samples is not null) stateEntries.Add(new TranslationEditorStateEntry(entry.Key, entry.Locale, null, null, null, samples));
        }
        if (unstamped.Count != 0)
            throw new EditorUserException(EditorNotice.Create("ui_backend_review_unstamped",
                ("count", unstamped.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)), ("entries", string.Join(", ", unstamped.Take(10)))));
        var review = new TranslationInterchangeReview(_catalogId, reviewEntries);
        var editorState = new TranslationEditorState(_catalogId, stateEntries,
            terminology.Select(static term => new TranslationTerminologyEntry(term.Source, term.Preferred, term.Locale, term.Note)).ToArray());
        byte[] reviewBytes = TranslationReviewFile.Render(review);
        if (files.ReviewBytes is null ? reviewEntries.Count != 0 : !files.ReviewBytes.AsSpan().SequenceEqual(reviewBytes))
            WriteReview(reviewBytes, files.ReviewBytes);
        bool stateExists = files.EditorState.Revision is not null;
        if (stateExists || stateEntries.Count != 0 || editorState.Terminology.Count != 0)
        {
            byte[] stateBytes = TranslationEditorStateStore.RenderBytes(editorState);
            if (!stateExists || Revision(stateBytes) != files.EditorState.Revision)
                TranslationEditorStateStore.Save(_root, editorState, files.EditorState.Revision);
        }
        EditorReviewSnapshot saved = Load(compilation);
        return changedSince == 0 ? saved : saved with
        {
            Notice = new EditorNotice("ui_backend_review_saved_stale",
                [new EditorNoticeArgument("count", changedSince.ToString(System.Globalization.CultureInfo.InvariantCulture), changedSince)]),
        };
    }

    // Three-way merge per review entry (key and locale) and for the terminology list: a side the user did not change
    // takes the files' current value; a side only the user changed keeps the user's; both changed alike is fine; both
    // changed differently refuses, naming the entries.
    private static (IReadOnlyList<EditorReviewEntry> Entries, IReadOnlyList<EditorTerminologyEntry> Terminology) Rebase(
        EditorReviewSaveRequest request, IReadOnlyList<EditorReviewEntry> current, IReadOnlyList<EditorTerminologyEntry> currentTerminology)
    {
        Dictionary<string, EditorReviewEntry> Map(IEnumerable<EditorReviewEntry> items) =>
            items.GroupBy(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Last(), StringComparer.Ordinal);
        Dictionary<string, EditorReviewEntry> original = Map(request.Original!), mine = Map(request.Entries), theirs = Map(current);
        var merged = new List<EditorReviewEntry>();
        var conflicts = new List<string>();
        foreach (string identity in original.Keys.Concat(mine.Keys).Concat(theirs.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            original.TryGetValue(identity, out EditorReviewEntry? o);
            mine.TryGetValue(identity, out EditorReviewEntry? m);
            theirs.TryGetValue(identity, out EditorReviewEntry? t);
            EditorReviewEntry? result;
            if (SameEntry(m, o)) result = t;
            else if (SameEntry(t, o) || SameEntry(m, t)) result = m;
            else
            {
                EditorReviewEntry named = (m ?? t ?? o)!;
                conflicts.Add(named.Key + " (" + named.Locale + ")");
                continue;
            }
            if (result is not null) merged.Add(result);
        }
        IReadOnlyList<EditorTerminologyEntry> terminology;
        if (SameTerms(request.Terminology, request.OriginalTerminology!)) terminology = currentTerminology;
        else if (SameTerms(currentTerminology, request.OriginalTerminology!) || SameTerms(request.Terminology, currentTerminology)) terminology = request.Terminology;
        else
        {
            conflicts.Add("terminology");
            terminology = request.Terminology;
        }
        if (conflicts.Count != 0)
            throw new EditorUserException(EditorNotice.Create("ui_backend_review_conflict", ("entries", string.Join(", ", conflicts.Take(10)))));
        return (merged, terminology);
    }

    // The same review data: state (unless both are inferred), note, fingerprint and samples; Stale is computed, not data.
    internal static bool SameEntry(EditorReviewEntry? left, EditorReviewEntry? right)
    {
        if (left is null || right is null) return left is null && right is null;
        if (left.Implicit != right.Implicit || (!left.Implicit && left.State != right.State)) return false;
        if (left.Note != right.Note || left.SourceFingerprint != right.SourceFingerprint || left.Samples.Count != right.Samples.Count) return false;
        foreach (KeyValuePair<string, string> sample in left.Samples)
            if (!right.Samples.TryGetValue(sample.Key, out string? value) || value != sample.Value) return false;
        return true;
    }

    private static bool SameTerms(IReadOnlyList<EditorTerminologyEntry> left, IReadOnlyList<EditorTerminologyEntry> right) =>
        left.Count == right.Count && left.Zip(right).All(static pair => pair.First == pair.Second);

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
        if (NeedsMigration(files))
            throw new TranslationEditorStateException("Fix the errors in runic.json or the base locale once so the editor can move its review states into " + DisplayPath + ".");
        return (files.Review, files.ReviewBytes, files.Revision);
    }

    internal string? CurrentRevision() => Read().Revision;

    /// <summary>The combined review revision and the review file's bytes it covers, for a compare-and-swap write.</summary>
    internal (string? Revision, byte[]? ReviewBytes) CurrentState()
    {
        Files files = Read();
        return (files.Revision, files.ReviewBytes);
    }

    /// <summary>
    /// Replaces the review file atomically, only while it still holds <paramref name="expected"/> (null: while it does
    /// not exist); another writer's change since is refused (<see cref="TranslationReviewFile.ChangedCode"/>).
    /// </summary>
    internal void WriteReview(byte[] bytes, byte[]? expected)
    {
        string directory = Path.GetDirectoryName(ReviewPath)!;
        Directory.CreateDirectory(directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
            (File.Exists(ReviewPath) && (File.GetAttributes(ReviewPath) & FileAttributes.ReparsePoint) != 0))
            throw new TranslationEditorStateException("The review file cannot be a link or reparse point.");
        TranslationReviewFile.WriteRendered(ReviewPath, bytes, expected);
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
                bytes = TranslationReviewFile.ReadBytes(ReviewPath);
                if (bytes is not null) review = TranslationReviewFile.Parse(bytes, _catalogId);
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

    // The version 1 file's own states, shown read-only until they can move.
    private static EditorReviewEntry[] PendingEntries(Files files) => files.EditorState.State.Entries
        .Select(static entry => new EditorReviewEntry(entry.Key, entry.Locale, entry.State ?? "draft", entry.Note, null, entry.Samples, Implicit: entry.State is null))
        .ToArray();

    private static EditorReviewEntry[] Entries(Files files, Rmf2ProjectCompilation? compilation, IReadOnlyDictionary<string, string>? fingerprints)
    {
        Rmf2ProjectV5? project = fingerprints is null ? null : compilation!.Project;
        var editorEntries = files.EditorState.State.Entries.ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        var result = new List<EditorReviewEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (TranslationInterchangeReviewEntry entry in files.Review?.Entries ?? [])
        {
            string identity = TranslationReviewFile.Identity(entry.Key, entry.Locale);
            seen.Add(identity);
            editorEntries.TryGetValue(identity, out TranslationEditorStateEntry? local);
            bool stale = project is not null && entry.Locale != project.DefaultLocale && TranslationReviewFile.IsStale(entry, fingerprints!);
            result.Add(new EditorReviewEntry(entry.Key, entry.Locale, TranslationReviewFile.StateText(entry.State), local?.Note ?? entry.Note,
                entry.SourceFingerprint, local?.Samples ?? new Dictionary<string, string>(StringComparer.Ordinal), stale));
        }
        // A note or samples without a review entry: the state the message has without one, inferred, not recorded.
        foreach (TranslationEditorStateEntry local in files.EditorState.State.Entries)
        {
            if (!seen.Add(TranslationReviewFile.Identity(local.Key, local.Locale))) continue;
            bool translated = project?.Locales.FirstOrDefault(locale => locale.Tag == local.Locale)?.DirectResources.Any(resource => resource.Key == local.Key) == true;
            result.Add(new EditorReviewEntry(local.Key, local.Locale, translated ? "translated" : "draft", local.Note, null, local.Samples, Implicit: true));
        }
        return result.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ThenBy(static entry => entry.Locale, StringComparer.Ordinal).ToArray();
    }

    // The one-time move of a version 1 editor-state file (D055.5): its review states go into the review file, each with the
    // current per-unit fingerprint. An approval is kept only where its recorded value still matches the current base text;
    // every other recorded state whose base text changed becomes needs-review. An entry the review file already has wins.
    // States of removed or renamed messages and undeclared locales are dropped. The version 1 file is copied to
    // .runic-translations/<catalog>.editor-state.v1.json first; notes, samples and terminology stay in the editor state,
    // which is rewritten as version 2. The notice counts what moved, what needs review, what was dropped and what the
    // review file already had.
    private EditorNotice Migrate(Files files, Rmf2ProjectCompilation compilation, IReadOnlyDictionary<string, string> fingerprints, IReadOnlyDictionary<string, string> baseValues)
    {
        Rmf2ProjectV5 project = compilation.Project!;
        var translatedLocales = new HashSet<string>(project.Locales.Select(static locale => locale.Tag).Where(tag => tag != project.DefaultLocale), StringComparer.Ordinal);
        var merged = (files.Review?.Entries ?? []).ToDictionary(static entry => TranslationReviewFile.Identity(entry.Key, entry.Locale), StringComparer.Ordinal);
        int moved = 0, demoted = 0, dropped = 0, kept = 0;
        foreach (TranslationEditorStateEntry entry in files.EditorState.State.Entries)
        {
            if (entry.State is null) continue;
            string identity = TranslationReviewFile.Identity(entry.Key, entry.Locale);
            if (!translatedLocales.Contains(entry.Locale) || !fingerprints.TryGetValue(entry.Key, out string? fingerprint) ||
                !TranslationReviewFile.TryParseState(entry.State, out TranslationReviewState state)) { dropped++; continue; }
            if (merged.ContainsKey(identity)) { kept++; continue; }
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
        string backup = TranslationEditorStateStore.BackupVersion1(_root, _catalogId, files.EditorState.Revision);
        byte[] reviewBytes = TranslationReviewFile.Render(new TranslationInterchangeReview(_catalogId, merged.Values));
        // The review file is written first: if the editor state cannot be rewritten, the next open moves the same
        // entries again, and the entries already in the review file win.
        if (files.ReviewBytes is null || !files.ReviewBytes.AsSpan().SequenceEqual(reviewBytes)) WriteReview(reviewBytes, files.ReviewBytes);
        TranslationEditorStateStore.Save(_root, files.EditorState.State, files.EditorState.Revision);
        return new EditorNotice("ui_backend_review_migrated",
            [Count("count", moved), Count("demoted", demoted), Count("dropped", dropped), Count("kept", kept),
             new EditorNoticeArgument("path", DisplayPath), new EditorNoticeArgument("backup", backup)]);

        static EditorNoticeArgument Count(string name, int value) => new(name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), value);
    }
}
