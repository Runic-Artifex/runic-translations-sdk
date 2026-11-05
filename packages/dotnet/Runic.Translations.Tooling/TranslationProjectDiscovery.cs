using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Runic.Translations.Tooling;

/// <summary>
/// Finds the translation projects (<c>runic.json</c> files) under a workspace root, by the language server's project-index
/// rules: a bounded breadth-first walk that does not follow links and skips build output, dependency, VCS and tool
/// directories. The Translations Editor uses it to offer every catalog of a workspace (L5-4).
/// </summary>
internal static class TranslationProjectDiscovery
{
    /// <summary>The directories that are never searched for projects.</summary>
    internal static readonly IReadOnlySet<string> SkippedDirectories = new HashSet<string>(StringComparer.Ordinal)
    {
        ".git", ".worktrees", "node_modules", "bin", "obj", "dist", "artifacts", ".cache", ".direnv", ".vs", ".runic", ".runic-translations",
    };

    /// <summary>Whether a directory of this name is skipped (<see cref="SkippedDirectories"/>).</summary>
    internal static bool IsSkipped(string name) => SkippedDirectories.Contains(name);

    /// <summary>The absolute paths of the runic.json files below <paramref name="root"/>, in path order.</summary>
    /// <exception cref="InvalidOperationException">The walk visits more than <paramref name="entryLimit"/> entries.</exception>
    internal static IReadOnlyList<string> Discover(string root, int entryLimit = 100_000, CancellationToken cancellationToken = default)
    {
        string start = Path.GetFullPath(root);
        var found = new List<string>();
        if (!Directory.Exists(start)) return found;
        var pending = new SortedSet<string>(StringComparer.Ordinal) { start };
        int visited = 1;
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = pending.Min!;
            pending.Remove(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (++visited > entryLimit) throw new InvalidOperationException($"Workspace project discovery exceeds the bounded entry limit of {entryLimit}.");
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                string name = Path.GetFileName(entry);
                if ((attributes & FileAttributes.Directory) == 0)
                {
                    if (name.Equals("runic.json", StringComparison.Ordinal)) found.Add(entry);
                }
                else if (!IsSkipped(name)) pending.Add(entry);
            }
        }
        found.Sort(StringComparer.Ordinal);
        return found;
    }
}
