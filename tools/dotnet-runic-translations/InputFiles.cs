using System;
using System.Collections.Generic;
using System.IO;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

internal sealed record CompilerInputs(TranslationSource Project, IReadOnlyList<TranslationSource> Messages);

internal static class InputFiles
{
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;

    internal static CompilerInputs ReadProject(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string supplied = Path.GetFullPath(projectPath, currentDirectory);
        string configPath = Directory.Exists(supplied) ? Path.Combine(supplied, "runic.json") : supplied;
        if (!File.Exists(configPath))
            throw new ToolUsageException($"Runic translation project '{NormalizePath(projectPath)}' does not contain runic.json.");
        if (!string.Equals(Path.GetFileName(configPath), "runic.json", StringComparison.Ordinal))
            throw new ToolUsageException("--project must name a translations directory or its runic.json file.");

        string root = Path.GetDirectoryName(configPath)!;
        var messages = new List<TranslationSource>();
        foreach (string candidate in EnumerateFilesWithoutReparsePoints(root, projectPath))
            if (string.Equals(Path.GetExtension(candidate), ".mf2", StringComparison.OrdinalIgnoreCase))
                messages.Add(ReadSource(candidate, DisplayPath(candidate, currentDirectory)));
        messages.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return new CompilerInputs(ReadSource(configPath, DisplayPath(configPath, currentDirectory)), messages);
    }

    private static TranslationSource ReadSource(string fullPath, string displayPath)
    {
        var information = new FileInfo(fullPath);
        if (information.Length > MaximumDocumentBytes) throw TooLarge(displayPath);
        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length > MaximumDocumentBytes) throw TooLarge(displayPath);
        return new TranslationSource(displayPath, bytes);
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root, string suppliedPath)
    {
        var pending = new SortedSet<string>(StringComparer.Ordinal) { Path.GetFullPath(root) };
        while (pending.Count != 0)
        {
            string directory = pending.Min!;
            pending.Remove(directory);
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new ToolUsageException($"translation project '{NormalizePath(suppliedPath)}' traverses a symbolic link or reparse point.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ToolUsageException($"translation project '{NormalizePath(suppliedPath)}' contains a symbolic link or reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Add(Path.GetFullPath(entry));
                else yield return entry;
            }
        }
    }

    private static ToolDiagnosticException TooLarge(string displayPath) => new(
        $"{NormalizePath(displayPath)}(1,1,1,1): error RTR0022: Document exceeds the configured byte limit of {MaximumDocumentBytes} bytes.");

    private static string DisplayPath(string fullPath, string currentDirectory)
    {
        string relative = Path.GetRelativePath(currentDirectory, fullPath);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? NormalizePath(relative)
            : NormalizePath(fullPath);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
