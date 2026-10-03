using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace Runic.Translations.Build;

/// <summary>Discovers configured source mounts without executing application code.</summary>
public sealed class Rmf2SourceDiscovery : ITask
{
    [Required]
    public ITaskItem[] Projects { get; set; } = Array.Empty<ITaskItem>();
    [Output]
    public string[] Sources { get; set; } = Array.Empty<string>();
    public IBuildEngine BuildEngine { get; set; } = null!;
    public ITaskHost HostObject { get; set; } = null!;
    public bool Execute()
    {
        try
        {
            var files = new SortedSet<string>(StringComparer.Ordinal);
            foreach (ITaskItem project in Projects)
            {
                string config = Path.GetFullPath(project.ItemSpec), root = Path.GetDirectoryName(config)!;
                if (new FileInfo(config).Length > 8 * 1024 * 1024) throw new IOException("Translation project exceeds byte limit.");
                using var json = JsonDocument.Parse(File.ReadAllBytes(config));
                if (json.RootElement.TryGetProperty("sourceRoots", out JsonElement mounts))
                {
                    if (mounts.ValueKind != JsonValueKind.Array) throw new InvalidOperationException("sourceRoots must be an array.");
                    foreach (JsonElement mount in mounts.EnumerateArray())
                    {
                        if (mount.ValueKind != JsonValueKind.Object ||
                            !mount.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(path.GetString()))
                            throw new InvalidOperationException("Each source root must declare a non-empty path.");
                        Discover(Path.GetFullPath(path.GetString()!, root), root, files);
                    }
                }
                else Discover(root, root, files);
            }
            Sources = new List<string>(files).ToArray(); return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("translations", "RTR0052", "", 0, 0, 0, 0, exception.Message, "", nameof(Rmf2SourceDiscovery))); return false;
        }
    }
    private static void Discover(string root, string projectRoot, SortedSet<string> files)
    {
        ValidateNoReparseAncestors(root, projectRoot);
        var directories = new Stack<string>(); directories.Push(root); int visited = 0;
        while (directories.Count > 0)
        {
            string directory = directories.Pop();
            if (++visited > 100_000) throw new IOException("Translation discovery directory limit exceeded.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("Translation source roots must not traverse symbolic links.");
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Translation sources must not traverse symbolic links.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                else if (string.Equals(Path.GetExtension(path), ".mf2", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Path.GetExtension(path), ".rmf2", StringComparison.OrdinalIgnoreCase)) files.Add(path);
            }
        }
    }

    // Checks the ancestors of a source root below the deepest directory it shares with the project.
    // Components above that belong to the environment: macOS /tmp and /var, or a symlinked home or
    // checkout, are links a project cannot avoid. The traversal checks the root and its contents.
    private static void ValidateNoReparseAncestors(string path, string projectRoot)
    {
        string fullPath = Path.GetFullPath(path);
        string? boundary = Path.GetFullPath(projectRoot);
        while (boundary is not null && !IsWithin(boundary, fullPath)) boundary = Path.GetDirectoryName(boundary);
        string? current = Path.GetDirectoryName(fullPath);
        while (current is not null && boundary is not null && IsWithin(boundary, current) &&
               Path.GetRelativePath(boundary, current) != ".")
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Translation source roots must not traverse symbolic links.");
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool IsWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
