using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RunicTranslations.Compiler;

namespace RunicTranslations.Tool;

internal sealed record CompilerInputs(TranslationSource Catalog, IReadOnlyList<TranslationSource> Documents);

internal static class InputFiles
{
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;

    internal static CompilerInputs Read(string catalogPath, IReadOnlyList<string> documentPatterns)
    {
        ArgumentException.ThrowIfNullOrEmpty(catalogPath);
        ArgumentNullException.ThrowIfNull(documentPatterns);

        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string catalogFullPath = Path.GetFullPath(catalogPath, currentDirectory);
        if (!File.Exists(catalogFullPath))
        {
            throw new ToolUsageException($"catalog file '{NormalizePath(catalogPath)}' does not exist.");
        }

        var paths = new Dictionary<string, string>(PathComparer);
        for (int index = 0; index < documentPatterns.Count; index++)
        {
            string pattern = documentPatterns[index];
            string[] matches = ExpandPattern(pattern, currentDirectory);
            if (matches.Length == 0)
            {
                throw new ToolUsageException($"document path or glob '{NormalizePath(pattern)}' matched no files.");
            }

            for (int matchIndex = 0; matchIndex < matches.Length; matchIndex++)
            {
                string fullPath = matches[matchIndex];
                if (PathComparer.Equals(catalogFullPath, fullPath))
                {
                    throw new ToolUsageException("the catalog file must not also be supplied as a resource document.");
                }

                paths.TryAdd(fullPath, DisplayPath(fullPath, currentDirectory));
            }
        }

        var ordered = new List<KeyValuePair<string, string>>(paths);
        ordered.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Value, right.Value));
        var documents = new List<TranslationSource>(ordered.Count);
        for (int index = 0; index < ordered.Count; index++)
        {
            documents.Add(ReadSource(ordered[index].Key, ordered[index].Value));
        }

        return new CompilerInputs(
            ReadSource(catalogFullPath, DisplayPath(catalogFullPath, currentDirectory)),
            documents);
    }

    private static TranslationSource ReadSource(string fullPath, string displayPath)
    {
        var information = new FileInfo(fullPath);
        if (information.Length > MaximumDocumentBytes)
        {
            throw TooLarge(displayPath);
        }

        using FileStream input = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new MemoryStream((int)information.Length);
        var buffer = new byte[64 * 1024];
        int total = 0;
        while (true)
        {
            int read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > MaximumDocumentBytes)
            {
                throw TooLarge(displayPath);
            }

            output.Write(buffer, 0, read);
        }

        return new TranslationSource(displayPath, output.ToArray());
    }

    private static string[] ExpandPattern(string pattern, string currentDirectory)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ToolUsageException("document paths and globs must not be empty.");
        }

        string fullPattern = NormalizePath(Path.GetFullPath(pattern, currentDirectory));
        int wildcard = fullPattern.IndexOfAny(['*', '?']);
        if (wildcard < 0)
        {
            return File.Exists(fullPattern.Replace('/', Path.DirectorySeparatorChar))
                ? [Path.GetFullPath(fullPattern.Replace('/', Path.DirectorySeparatorChar))]
                : Array.Empty<string>();
        }

        int separator = fullPattern.LastIndexOf('/', wildcard);
        string pathRoot = NormalizePath(Path.GetPathRoot(fullPattern) ?? currentDirectory);
        string root = separator < pathRoot.Length ? pathRoot : fullPattern[..separator];
        string nativeRoot = root.Replace('/', Path.DirectorySeparatorChar);
        if (!Directory.Exists(nativeRoot))
        {
            return Array.Empty<string>();
        }

        Regex matcher = CreateGlobRegex(fullPattern);
        var matches = new List<string>();
        foreach (string candidate in EnumerateFilesWithoutReparsePoints(nativeRoot, pattern))
        {
            string normalized = NormalizePath(Path.GetFullPath(candidate));
            if (matcher.IsMatch(normalized))
            {
                matches.Add(Path.GetFullPath(candidate));
            }
        }

        matches.Sort((left, right) => StringComparer.Ordinal.Compare(NormalizePath(left), NormalizePath(right)));
        return matches.ToArray();
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root, string pattern)
    {
        var pending = new SortedSet<string>(StringComparer.Ordinal) { Path.GetFullPath(root) };
        while (pending.Count != 0)
        {
            string directory = pending.Min!;
            pending.Remove(directory);
            FileAttributes directoryAttributes = File.GetAttributes(directory);
            if ((directoryAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ToolUsageException(
                    $"document glob '{NormalizePath(pattern)}' traverses a symbolic link or reparse point at '{DisplayPath(directory, Environment.CurrentDirectory)}'.");
            }

            var entries = new List<string>(Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly));
            entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(NormalizePath(left), NormalizePath(right)));
            for (int index = 0; index < entries.Count; index++)
            {
                string entry = entries[index];
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ToolUsageException(
                        $"document glob '{NormalizePath(pattern)}' encounters a symbolic link or reparse point at '{DisplayPath(entry, Environment.CurrentDirectory)}'.");
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

    private static ToolDiagnosticException TooLarge(string displayPath) => new(
        $"{NormalizePath(displayPath)}(1,1,1,1): error RTR0022: Document exceeds the configured byte limit of {MaximumDocumentBytes} bytes.");

    private static Regex CreateGlobRegex(string pattern)
    {
        var expression = new StringBuilder(pattern.Length * 2).Append('^');
        for (int index = 0; index < pattern.Length; index++)
        {
            char value = pattern[index];
            if (value == '*')
            {
                if (index + 1 < pattern.Length && pattern[index + 1] == '*')
                {
                    index++;
                    if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                    {
                        index++;
                        expression.Append("(?:.*/)?");
                    }
                    else
                    {
                        expression.Append(".*");
                    }
                }
                else
                {
                    expression.Append("[^/]*");
                }
            }
            else if (value == '?')
            {
                expression.Append("[^/]");
            }
            else
            {
                expression.Append(Regex.Escape(value.ToString()));
            }
        }

        expression.Append('$');
        return new Regex(
            expression.ToString(),
            RegexOptions.CultureInvariant | (OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None),
            TimeSpan.FromSeconds(2));
    }

    private static string DisplayPath(string fullPath, string currentDirectory)
    {
        string relative = Path.GetRelativePath(currentDirectory, fullPath);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return NormalizePath(relative);
        }

        return NormalizePath(fullPath);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
