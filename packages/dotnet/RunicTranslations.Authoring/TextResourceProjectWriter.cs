using System;
using System.IO;

namespace RunicTranslations.Authoring;

public static class TextResourceProjectWriter
{
    public static string Create(TextResourceProjectPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        string target = Path.GetFullPath(plan.Request.Directory);
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new TextResourceAuthoringException($"Target path '{target}' already exists; no files were written.");
        }

        string? parent = Path.GetDirectoryName(target);
        if (parent is null)
        {
            throw new TextResourceAuthoringException($"Target path '{target}' has no parent directory.");
        }

        RejectReparsePoints(parent);
        Directory.CreateDirectory(parent);
        RejectReparsePoints(parent);
        string targetName = Path.GetFileName(target);
        string staging = Path.Combine(parent, $".{targetName}.runic-create-{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(staging);
        try
        {
            for (int index = 0; index < plan.Files.Count; index++)
            {
                TextResourceProjectFile file = plan.Files[index];
                string destination = ResolveContainedPath(staging, file.RelativePath);
                string? destinationParent = Path.GetDirectoryName(destination);
                if (destinationParent is not null)
                {
                    Directory.CreateDirectory(destinationParent);
                }

                using var stream = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough);
                stream.Write(file.Bytes);
                stream.Flush(flushToDisk: true);
            }

            Directory.Move(staging, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteStaging(staging);
            throw new TextResourceAuthoringException(
                $"Could not create translation project at '{target}'; no project was committed.",
                exception);
        }
        catch
        {
            TryDeleteStaging(staging);
            throw;
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw new TextResourceAuthoringException($"Project file path '{relativePath}' must be relative.");
        }

        string destination = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), root);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPrefix, PathComparison))
        {
            throw new TextResourceAuthoringException($"Project file path '{relativePath}' escapes the target directory.");
        }

        return destination;
    }

    private static void RejectReparsePoints(string path)
    {
        DirectoryInfo? current = new(path);
        while (current is not null && !current.Exists)
        {
            current = current.Parent;
        }

        if (current is not null && (current.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new TextResourceAuthoringException(
                $"Target parent '{current.FullName}' is a symbolic link or reparse point.");
        }
    }

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
