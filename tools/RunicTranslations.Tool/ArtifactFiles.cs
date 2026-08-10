using System;
using System.Collections.Generic;
using System.IO;

namespace RunicTranslations.Tool;

internal sealed record ToolArtifact(string RelativePath, byte[] Content);

internal static class ArtifactFiles
{
    private static readonly HashSet<string> WindowsDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    internal static IReadOnlyList<ToolArtifact> Normalize(IReadOnlyList<ToolArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ToolArtifact>(artifacts.Count);
        for (int index = 0; index < artifacts.Count; index++)
        {
            ToolArtifact artifact = artifacts[index];
            string path = NormalizeRelativePath(artifact.RelativePath);
            if (!paths.Add(path))
            {
                throw new ToolOutputException($"RTR0020: generated output path collision for '{path}'.");
            }

            result.Add(new ToolArtifact(path, (byte[])artifact.Content.Clone()));
        }

        result.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return result;
    }

    internal static void WriteAtomically(string outputPath, IReadOnlyList<ToolArtifact> artifacts)
    {
        IReadOnlyList<ToolArtifact> normalizedArtifacts = Normalize(artifacts);
        string outputRoot = Path.GetFullPath(outputPath);
        string? parent = Path.GetDirectoryName(outputRoot);
        if (parent is null)
        {
            throw new ToolOutputException($"RTR0020: output root '{NormalizePath(outputPath)}' is invalid.");
        }

        EnsureNoReparsePoint(outputRoot, NormalizePath(outputPath));
        Directory.CreateDirectory(parent);
        string temporaryRoot = Path.Combine(parent, $".{Path.GetFileName(outputRoot)}.textresources-{Guid.NewGuid():N}.tmp");
        var sourcePaths = new string[normalizedArtifacts.Count];
        var destinationPaths = new string[normalizedArtifacts.Count];
        var backupPaths = new string[normalizedArtifacts.Count];
        var hadExistingDestination = new bool[normalizedArtifacts.Count];
        int processed = 0;
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            string renderRoot = Path.Combine(temporaryRoot, "render");
            string backupRoot = Path.Combine(temporaryRoot, "backup");

            // Preflight every declared path before touching any live output file.
            for (int index = 0; index < normalizedArtifacts.Count; index++)
            {
                ToolArtifact artifact = normalizedArtifacts[index];
                sourcePaths[index] = ResolveContainedPath(renderRoot, artifact.RelativePath);
                destinationPaths[index] = ResolveContainedPath(outputRoot, artifact.RelativePath);
                backupPaths[index] = ResolveContainedPath(backupRoot, artifact.RelativePath);
                if (Directory.Exists(destinationPaths[index]))
                {
                    throw new ToolOutputException(
                        $"RTR0020: declared output path '{artifact.RelativePath}' is an existing directory.");
                }
            }

            // Render the complete validated set before beginning declared-file replacement.
            for (int index = 0; index < normalizedArtifacts.Count; index++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sourcePaths[index])!);
                File.WriteAllBytes(sourcePaths[index], normalizedArtifacts[index].Content);
            }

            Directory.CreateDirectory(outputRoot);
            EnsureNoReparsePoint(outputRoot, NormalizePath(outputPath));
            for (int index = 0; index < normalizedArtifacts.Count; index++)
            {
                string destination = destinationPaths[index];
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                EnsureNoReparsePoint(destination, normalizedArtifacts[index].RelativePath);
                hadExistingDestination[index] = File.Exists(destination);
                if (hadExistingDestination[index])
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPaths[index])!);
                    File.Copy(destination, backupPaths[index], true);
                }

                // Mark first so rollback also covers an exception with ambiguous move state.
                processed = index + 1;
                File.Move(sourcePaths[index], destination, true);
            }
        }
        catch
        {
            for (int index = processed - 1; index >= 0; index--)
            {
                if (hadExistingDestination[index] && File.Exists(backupPaths[index]))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPaths[index])!);
                    File.Move(backupPaths[index], destinationPaths[index], true);
                }
                else if (!hadExistingDestination[index] && File.Exists(destinationPaths[index]))
                {
                    File.Delete(destinationPaths[index]);
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, true);
            }
        }
    }

    internal static IReadOnlyList<string> Verify(string outputPath, IReadOnlyList<ToolArtifact> artifacts)
    {
        string outputRoot = Path.GetFullPath(outputPath);
        var differences = new List<string>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < artifacts.Count; index++)
        {
            ToolArtifact artifact = artifacts[index];
            declared.Add(artifact.RelativePath);
            string path = ResolveContainedPath(outputRoot, artifact.RelativePath);
            if (!File.Exists(path))
            {
                differences.Add($"missing: {artifact.RelativePath}");
            }
            else if (!FileEquals(path, artifact.Content))
            {
                differences.Add($"changed: {artifact.RelativePath}");
            }
        }

        if (Directory.Exists(outputRoot))
        {
            foreach (string file in EnumerateOutputFiles(outputRoot))
            {
                string relative = NormalizePath(Path.GetRelativePath(outputRoot, file));
                if (!declared.Contains(relative))
                {
                    differences.Add($"extra: {relative}");
                }
            }
        }

        differences.Sort(StringComparer.Ordinal);
        return differences;
    }

    internal static string ResolveContainedPath(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), fullRoot);
        string prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!fullPath.StartsWith(prefix, comparison))
        {
            throw new ToolOutputException($"RTR0020: output path '{relativePath}' escapes the configured output root.");
        }

        EnsureNoReparsePoint(fullPath, relativePath);
        return fullPath;
    }

    private static bool FileEquals(string path, byte[] expected)
    {
        var information = new FileInfo(path);
        if (information.Length != expected.LongLength)
        {
            return false;
        }

        using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buffer = new byte[64 * 1024];
        int offset = 0;
        while (offset < expected.Length)
        {
            int requested = Math.Min(buffer.Length, expected.Length - offset);
            int read = stream.Read(buffer, 0, requested);
            if (read == 0)
            {
                return false;
            }

            if (!buffer.AsSpan(0, read).SequenceEqual(expected.AsSpan(offset, read)))
            {
                return false;
            }

            offset += read;
        }

        return stream.ReadByte() == -1;
    }

    private static IEnumerable<string> EnumerateOutputFiles(string outputRoot)
    {
        var pending = new SortedSet<string>(StringComparer.Ordinal) { Path.GetFullPath(outputRoot) };
        while (pending.Count != 0)
        {
            string directory = pending.Min!;
            pending.Remove(directory);
            EnsureNoReparsePoint(directory, NormalizePath(Path.GetRelativePath(outputRoot, directory)));

            var entries = new List<string>(Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly));
            entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(NormalizePath(left), NormalizePath(right)));
            for (int index = 0; index < entries.Count; index++)
            {
                string entry = entries[index];
                FileAttributes attributes = File.GetAttributes(entry);
                string relative = NormalizePath(Path.GetRelativePath(outputRoot, entry));
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ToolOutputException(
                        $"RTR0020: output path '{relative}' traverses a symbolic link or reparse point.");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Add(Path.GetFullPath(entry));
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void EnsureNoReparsePoint(string path, string displayPath)
    {
        string fullPath = Path.GetFullPath(path);
        string pathRoot = Path.GetPathRoot(fullPath) ?? throw new ToolOutputException(
            $"RTR0020: output path '{displayPath}' has no filesystem root.");
        string relative = Path.GetRelativePath(pathRoot, fullPath);
        string current = pathRoot;
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (int index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                break;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ToolOutputException(
                    $"RTR0020: output path '{displayPath}' traverses a symbolic link or reparse point.");
            }
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ToolOutputException("RTR0020: generated output path must not be empty.");
        }

        string normalized = NormalizePath(path);
        if (Path.IsPathRooted(normalized) || normalized[0] == '/')
        {
            throw new ToolOutputException($"RTR0020: generated output path '{normalized}' must be relative.");
        }

        string[] segments = normalized.Split('/');
        for (int index = 0; index < segments.Length; index++)
        {
            if (segments[index] is "" or "." or "..")
            {
                throw new ToolOutputException($"RTR0020: generated output path '{normalized}' is not canonical and contained.");
            }

            ValidatePortableSegment(segments[index], normalized);
        }

        return normalized;
    }

    private static void ValidatePortableSegment(string segment, string relativePath)
    {
        if (segment[^1] is ' ' or '.')
        {
            throw new ToolOutputException(
                $"RTR0020: generated output path '{relativePath}' is not portable because a component ends with a space or dot.");
        }

        for (int index = 0; index < segment.Length; index++)
        {
            char value = segment[index];
            if (value < ' ' || value is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                throw new ToolOutputException(
                    $"RTR0020: generated output path '{relativePath}' contains a non-portable character.");
            }
        }

        int extension = segment.IndexOf('.');
        string baseName = extension < 0 ? segment : segment[..extension];
        if (WindowsDeviceNames.Contains(baseName))
        {
            throw new ToolOutputException(
                $"RTR0020: generated output path '{relativePath}' uses reserved device name '{baseName}'.");
        }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}

internal sealed class ToolOutputException : Exception
{
    internal ToolOutputException(string message)
        : base(message)
    {
    }
}
