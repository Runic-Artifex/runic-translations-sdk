using System;
using System.Collections.Generic;
using System.IO;
using Runic.Translations.Internal;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tool;

/// <param name="ManifestError">Why runic.json yields no source layout, or <see langword="null"/> when it is usable.</param>
/// <param name="OverrideSources">The sources under translations/overrides when runic.json declares <c>overrides</c>, which belong to library catalogs.</param>
internal sealed record CompilerInputs(TranslationSource Project, IReadOnlyList<TranslationSource> Messages, IReadOnlyList<string>? SourceRoots = null, string? ManifestError = null,
    IReadOnlyList<TranslationSource>? OverrideSources = null);

internal static class InputFiles
{
    private const int MaximumDocumentBytes = 8 * 1024 * 1024;

    internal static CompilerInputs ReadProject(string projectPath, TranslationSource? projectOverride = null)
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
        TranslationSource project = projectOverride ?? ReadSource(configPath, DisplayPath(configPath, currentDirectory));
        // A malformed manifest (invalid JSON, a non-object root, or a malformed
        // sourceRoots entry) yields no sources. The compiler reports the located
        // configuration diagnostic; ManifestError covers layouts it accepts.
        TranslationManifestLayout layout = TranslationManifestReader.Read(project.GetUtf8Bytes(), root);
        if (!layout.IsValid) return new CompilerInputs(project, messages, null, layout.Error);
        IReadOnlyList<string> roots = layout.SourceRoots;
        foreach (string sourceRoot in roots)
        foreach (string candidate in EnumerateFilesWithoutReparsePoints(sourceRoot, root, projectPath))
            if (IsSourceFile(candidate) && !layout.IsOverrideSource(Path.GetFullPath(candidate)))
                messages.Add(ReadSource(candidate, DisplayPath(candidate, currentDirectory)));
        messages.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        var overrides = new List<TranslationSource>();
        if (layout.OverridesRoot is { } overridesRoot && Directory.Exists(overridesRoot))
        {
            foreach (string candidate in EnumerateFilesWithoutReparsePoints(overridesRoot, root, projectPath))
                if (IsSourceFile(candidate)) overrides.Add(ReadSource(candidate, DisplayPath(candidate, currentDirectory)));
            overrides.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        }
        return new CompilerInputs(project, messages, roots, null, overrides);

        static bool IsSourceFile(string path) =>
            string.Equals(Path.GetExtension(path), ".mf2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetExtension(path), ".rmf2", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The <c>&lt;locale&gt;.rmf2</c> override sources of a directory (generate --overrides); none for a missing directory or null.</summary>
    internal static IReadOnlyList<TranslationSource> ReadOverrides(string? directory)
    {
        if (directory is null) return [];
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string root = Path.GetFullPath(directory, currentDirectory);
        if (!Directory.Exists(root)) return [];
        var sources = new List<TranslationSource>();
        foreach (string candidate in EnumerateFilesWithoutReparsePoints(root, root, directory))
            if (string.Equals(Path.GetExtension(candidate), ".rmf2", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(candidate), ".mf2", StringComparison.OrdinalIgnoreCase))
                sources.Add(ReadSource(candidate, DisplayPath(candidate, currentDirectory)));
        sources.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return sources;
    }

    private static TranslationSource ReadSource(string fullPath, string displayPath)
    {
        var information = new FileInfo(fullPath);
        if (information.Length > MaximumDocumentBytes) throw TooLarge(displayPath);
        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.Length > MaximumDocumentBytes) throw TooLarge(displayPath);
        return new TranslationSource(displayPath, bytes);
    }

    private static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string root, string projectRoot, string suppliedPath)
    {
        ValidateNoReparseAncestors(root, projectRoot, suppliedPath);
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

    // Checks the ancestors of a source root below the deepest directory it shares with the project.
    // Components above that belong to the environment: macOS /tmp and /var, or a symlinked home or
    // checkout, are links a project cannot avoid. The enumeration checks the root and its contents.
    private static void ValidateNoReparseAncestors(string path, string projectRoot, string suppliedPath)
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
                throw new ToolUsageException($"translation project '{NormalizePath(suppliedPath)}' traverses a symbolic link or reparse point.");
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

    private static ToolDiagnosticException TooLarge(string displayPath) => new(
        $"{NormalizePath(displayPath)}(1,1,1,1): error RTR0022: Document exceeds the configured byte limit of {MaximumDocumentBytes} bytes.");

    internal static string DisplayPath(string fullPath, string currentDirectory)
    {
        string relative = Path.GetRelativePath(currentDirectory, fullPath);
        // Keep all inputs in the same coordinate system as the project path.
        // RMF2 mounts are explicitly allowed to be siblings/ancestors of the
        // project, so replacing `../feature` with an absolute path would make
        // the compiler's normalized mount identity impossible to match when
        // the CLI is launched from inside the project directory.
        return NormalizePath(relative);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
