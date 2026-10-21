using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Runic.Translations.Authoring;

/// <summary>Writes a rendered <see cref="TranslationProjectPlan"/> to disk.</summary>
public static class TranslationProjectWriter
{
    // The synchronous form for the command-line tool, which has no synchronization context.
    internal static string Create(TranslationProjectPlan plan) => CreateAsync(plan).GetAwaiter().GetResult();

    /// <summary>
    /// Writes the plan's files to a staging directory beside the target and moves it into place, so either the
    /// complete project appears or nothing does. The target must not exist.
    /// </summary>
    /// <returns>The full path of the created project directory.</returns>
    /// <exception cref="TranslationAuthoringException">The target exists, a parent is a link, or writing failed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; no project was committed.</exception>
    public static async Task<string> CreateAsync(TranslationProjectPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        string target = Path.GetFullPath(plan.Request.Directory);
        if (Directory.Exists(target) || File.Exists(target))
        {
            throw new TranslationAuthoringException($"Target path '{target}' already exists; no files were written.");
        }

        string? parent = Path.GetDirectoryName(target);
        if (parent is null)
        {
            throw new TranslationAuthoringException($"Target path '{target}' has no parent directory.");
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
                cancellationToken.ThrowIfCancellationRequested();
                TranslationProjectFile file = plan.Files[index];
                string destination = ResolveContainedPath(staging, file.RelativePath);
                string? destinationParent = Path.GetDirectoryName(destination);
                if (destinationParent is not null)
                {
                    Directory.CreateDirectory(destinationParent);
                }

                await using (var stream = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 16 * 1024,
                    FileOptions.WriteThrough | FileOptions.Asynchronous))
                {
                    await stream.WriteAsync(file.Bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(staging, target);
            return target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDeleteStaging(staging);
            throw new TranslationAuthoringException(
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
            throw new TranslationAuthoringException($"Project file path '{relativePath}' must be relative.");
        }

        string destination = Path.GetFullPath(relativePath.Replace('/', Path.DirectorySeparatorChar), root);
        string rootPrefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(rootPrefix, PathComparison))
        {
            throw new TranslationAuthoringException($"Project file path '{relativePath}' escapes the target directory.");
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
            throw new TranslationAuthoringException(
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
