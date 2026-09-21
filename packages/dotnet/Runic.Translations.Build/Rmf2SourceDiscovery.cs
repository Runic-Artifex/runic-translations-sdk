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
    [Output]
    public string ExecutionProfile { get; set; } = string.Empty;
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
                if (json.RootElement.TryGetProperty("executionProfile", out JsonElement profile) && profile.ValueKind == JsonValueKind.String)
                    ExecutionProfile = profile.GetString() ?? string.Empty;
                if (json.RootElement.TryGetProperty("sourceLayout", out var layout) && layout.GetString() == "rmf2-v1" && json.RootElement.TryGetProperty("sourceRoots", out var mounts))
                    foreach (JsonElement mount in mounts.EnumerateArray()) Discover(Path.GetFullPath(mount.GetProperty("path").GetString()!, root), files);
                else Discover(root, files);
            }
            Sources = new List<string>(files).ToArray(); return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("translations", "RTR0052", "", 0, 0, 0, 0, exception.Message, "", nameof(Rmf2SourceDiscovery))); return false;
        }
    }
    private static void Discover(string root, SortedSet<string> files)
    {
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
                else if (Path.GetExtension(path).ToLowerInvariant() is ".rmf2" or ".toml" or ".mf2") files.Add(path);
            }
        }
    }
}
