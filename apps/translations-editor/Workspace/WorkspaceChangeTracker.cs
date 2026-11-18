using System.Collections.Concurrent;
using Runic.Translations.Internal;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>
/// The external-change check: the file system watcher's queued events and the inventory baseline (the revision of each
/// configured source the editor last read or wrote), reconciled against the files on disk under the workspace gate.
/// </summary>
internal sealed class WorkspaceChangeTracker : IDisposable
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly string _root;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, byte> _pendingChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _knownRevisions = new(StringComparer.Ordinal);
    private int _watcherOverflowed;
    private int _reconcileRequested;

    internal WorkspaceChangeTracker(WorkspaceGate gate, WorkspaceFiles files)
    {
        _gate = gate;
        _files = files;
        _root = files.Root;
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

    // Stops and releases the watcher; EditorWorkspace.Dispose marks the gate disposed first, so late events are dropped.
    public void Dispose()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
    }

    // A full read of the files on disk becomes the baseline of the external-change check (WorkspaceStateReader).
    internal void ResetBaseline(IReadOnlyList<WorkspaceFile> files)
    {
        _knownRevisions.Clear();
        foreach (WorkspaceFile file in files) _knownRevisions[file.Path] = file.Revision;
        _pendingChanges.Clear();
        Interlocked.Exchange(ref _watcherOverflowed, 0);
    }

    internal async Task<EditorExternalChanges> CheckExternalChangesAsync(ReviewService review, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
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
            return new EditorExternalChanges(overflowed, changed, changes, await review.ExternalReviewAsync(cancellationToken).ConfigureAwait(false));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a write of the editor's own outside the document save path (library overrides listing a catalog in runic.json)
    /// and records the files it wrote with their new bytes (null: deleted) as known revisions, so the external-change check
    /// does not report the editor's own change. Holds the workspace gate, so no check runs between the write and the record.
    /// </summary>
    internal void WriteOwnFiles(Action write, IReadOnlyList<(string Path, byte[]? Bytes)> written)
    {
        ArgumentNullException.ThrowIfNull(write);
        _gate.Wait();
        try
        {
            _gate.ThrowIfDisposed();
            write();
            foreach ((string path, byte[]? bytes) in written)
            {
                string relative = _files.RelativePath(path);
                if (!_knownRevisions.ContainsKey(relative)) continue;
                if (bytes is null) _knownRevisions.Remove(relative);
                else _knownRevisions[relative] = Revision(bytes);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private Dictionary<string, byte[]> ReadCurrentTranslationFiles(CancellationToken cancellationToken)
    {
        string? config = _files.FindMf2ProjectConfig();
        // A deleted manifest is itself a meaningful inventory transition.  A
        // subsequent watcher event will repopulate the inventory when the
        // project is recreated, while this scan can still report removals.
        if (config is null) return new Dictionary<string, byte[]>(StringComparer.Ordinal);
        // A malformed manifest, or a source root that is missing or outside the
        // workspace (common while a path is being typed), must not fail the
        // scan: it would fail again on every check. The load reports those as
        // manifest diagnostics. The manifest itself stays in the inventory, so
        // fixing it is reported and rediscovers the sources.
        TranslationManifestLayout inventoryLayout = TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!);
        IReadOnlyList<string> sourceRoots = _files.UsableSourceRoots(inventoryLayout.SourceRoots, null);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [NormalizeRelativePath(Path.GetRelativePath(_root, config))] = ReadSourceBytes(_files.ContainedPath(NormalizeRelativePath(Path.GetRelativePath(_root, config)))),
        };
        foreach (string sourceRoot in sourceRoots)
            foreach (string path in EnumerateSourceFiles(sourceRoot, ".mf2").Concat(EnumerateSourceFiles(sourceRoot, ".rmf2")).Where(path => !inventoryLayout.IsOverrideSource(path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = NormalizeRelativePath(Path.GetRelativePath(_root, path));
                result[relative] = ReadSourceBytes(_files.ContainedPath(relative));
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
        if (_gate.Disposed) return;
        bool relevant = fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || IsSourcePath(fullPath);
        if (!relevant && !membershipChanged) return;
        string? within = PathContainment.RelativeWithin(_root, fullPath);
        if (within is null) return;
        string relativePath = NormalizeRelativePath(within);
        if (relativePath.StartsWith(".runic-translations/", StringComparison.Ordinal)) return;
        if (membershipChanged || string.Equals(Path.GetFileName(fullPath), "runic.json", StringComparison.OrdinalIgnoreCase))
            _files.InvalidateDiscoveredProjects();
        _pendingChanges.TryAdd(relativePath, 0);
        Interlocked.Exchange(ref _reconcileRequested, 1);
    }
}
