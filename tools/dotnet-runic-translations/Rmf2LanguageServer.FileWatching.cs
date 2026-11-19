using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

// The server's own file watching, for a client that cannot register watchers dynamically (Visual Studio 2026, which
// sends `didChangeWatchedFiles: {}`, SPIKE-VS-0) or refuses the registration. The server watches its workspace roots for
// the files WatchedFilePatterns names and queues the changes as one workspace/didChangeWatchedFiles notification, so an
// external edit, a restore or a review-file update reaches the diagnostics as it does with a registering client.
internal sealed partial class Rmf2LanguageServer
{
    // Serializes queueing between the reader and the watcher (Run's Enqueue), so the queue stays in revision order.
    private readonly object _enqueueGate = new();
    private Action<JsonArray>? _enqueueWatchedFiles;
    private readonly object _watchGate = new();
    private readonly List<FileSystemWatcher> _fileWatchers = new();
    // Changes not yet queued, the latest change type per path, in the order first seen.
    private readonly Dictionary<string, int> _watchedChanges = new(PathComparer);
    private readonly List<string> _watchedChangeOrder = new();
    private Timer? _watchFlush;
    private bool _watchStarted;
    private bool _watchStopped;
    // Changes within this delay are queued together: a restore or a branch switch rescans once, not per file.
    internal static TimeSpan WatchDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<string> WatchedLockFiles = new(StringComparer.Ordinal) {
        "package-lock.json", "pnpm-lock.yaml", "yarn.lock", "bun.lock", "bun.lockb",
    };
    private static readonly HashSet<string> WatchedInstallStateFiles = new(StringComparer.Ordinal) {
        ".package-lock.json", ".modules.yaml", ".yarn-state.yml",
    };

    private void StartFileWatching()
    {
        lock (_watchGate)
        {
            if (_watchStarted || _watchStopped) return;
            _watchStarted = true;
            string[] roots = _workspaceRoots.Select(Path.GetFullPath).Distinct(PathComparer).ToArray();
            // A root inside another root is covered by the outer watcher.
            foreach (string root in roots.Where(root => !roots.Any(other => !PathComparer.Equals(other, root) && IsWithin(other, root))))
            {
                if (!Directory.Exists(root)) continue;
                FileSystemWatcher? watcher = null;
                try
                {
                    watcher = new FileSystemWatcher(root) {
                        IncludeSubdirectories = true, InternalBufferSize = 64 * 1024,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    string watchedRoot = root;
                    watcher.Created += (_, change) => Observe(watchedRoot, change.FullPath, 1);
                    watcher.Changed += (_, change) => Observe(watchedRoot, change.FullPath, 2);
                    watcher.Deleted += (_, change) => Observe(watchedRoot, change.FullPath, 3);
                    watcher.Renamed += (_, change) => { Observe(watchedRoot, change.OldFullPath, 3); Observe(watchedRoot, change.FullPath, 1); };
                    // Events were lost (a full buffer): rescan as for any change below the root.
                    watcher.Error += (_, _) => Observe(watchedRoot, watchedRoot, 2, always: true);
                    watcher.EnableRaisingEvents = true;
                    _fileWatchers.Add(watcher);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
                {
                    // For example an exhausted inotify watch limit: the client's own events, if any, still arrive.
                    watcher?.Dispose();
                    Console.Error.WriteLine($"runic-rmf2: cannot watch '{root}' for changes: {error.Message}");
                }
            }
        }
    }

    private void StopFileWatching()
    {
        lock (_watchGate)
        {
            _watchStopped = true;
            foreach (FileSystemWatcher watcher in _fileWatchers) watcher.Dispose();
            _fileWatchers.Clear();
            _watchFlush?.Dispose();
            _watchFlush = null;
            _watchedChanges.Clear();
            _watchedChangeOrder.Clear();
        }
    }

    private void Observe(string root, string path, int type, bool always = false)
    {
        if (!always && !IsWatchedChange(root, path, type)) return;
        lock (_watchGate)
        {
            if (_watchStopped) return;
            if (!_watchedChanges.ContainsKey(path)) _watchedChangeOrder.Add(path);
            _watchedChanges[path] = type;
            _watchFlush ??= new Timer(_ => FlushWatchedChanges(), null, WatchDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void FlushWatchedChanges()
    {
        JsonArray changes;
        lock (_watchGate)
        {
            _watchFlush?.Dispose();
            _watchFlush = null;
            if (_watchStopped || _watchedChangeOrder.Count == 0) return;
            changes = new JsonArray(_watchedChangeOrder.Select(path => (JsonNode)new JsonObject {
                ["uri"] = new Uri(path).AbsoluteUri, ["type"] = _watchedChanges[path] }).ToArray());
            _watchedChanges.Clear();
            _watchedChangeOrder.Clear();
        }
        // The connection may close meanwhile; the change no longer matters then.
        try { _enqueueWatchedFiles?.Invoke(changes); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    // The files WatchedFilePatterns names. A directory that is deleted, renamed or created (a moved resource folder)
    // counts too, unless it is below a directory catalog discovery skips (node_modules, bin, obj, .git and others).
    internal static bool IsWatchedChange(string root, string path, int type)
    {
        string name = Path.GetFileName(path);
        string parent = Path.GetFileName(Path.GetDirectoryName(path) ?? "");
        string extension = Path.GetExtension(name);
        if (name.Equals("runic.json", StringComparison.Ordinal) || name.EndsWith(".review.json", StringComparison.Ordinal) ||
            extension.Equals(".rmf2", StringComparison.OrdinalIgnoreCase) || extension.Equals(".mf2", StringComparison.OrdinalIgnoreCase) ||
            WatchedLockFiles.Contains(name) || (parent == "obj" && name == "project.assets.json") ||
            (parent == "node_modules" && WatchedInstallStateFiles.Contains(name)))
            return true;
        if (extension.Length != 0 || type == 2) return false;
        string relative = Path.GetRelativePath(root, path);
        return !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(TranslationProjectDiscovery.IsSkipped);
    }
}
