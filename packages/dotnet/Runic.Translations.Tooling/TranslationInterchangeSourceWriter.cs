using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>One source file an interchange import writes: its path in the inputs' coordinates, its bytes before (none for a new file) and after.</summary>
internal sealed record TranslationSourceEdit(string Path, byte[]? OriginalBytes, byte[] Bytes);

/// <summary>
/// Writes imported translations into a project's <c>.rmf2</c> and <c>.mf2</c> sources, as the Translations Editor and
/// <c>runic-translations xliff import</c> do. An existing translation keeps its file, comments and formatting and only
/// its message body changes; a missing one is added beside the base resource: in the target locale's <c>.rmf2</c> file
/// next to the base file, or as the same file name in the target locale's directory for direct <c>.mf2</c> sources.
/// </summary>
internal static class TranslationInterchangeSourceWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Plans the edits for <paramref name="values"/> (key and MF2 pattern) in <paramref name="locale"/>.</summary>
    /// <param name="root">The directory that relative source paths resolve against.</param>
    /// <param name="project">runic.json, with a path in the same coordinates as the sources.</param>
    /// <param name="sources">Every <c>.rmf2</c> and <c>.mf2</c> source of the project.</param>
    /// <param name="compilation">The successful compilation of exactly these sources.</param>
    internal static IReadOnlyList<TranslationSourceEdit> Plan(
        string root,
        TranslationSource project,
        IReadOnlyList<TranslationSource> sources,
        Rmf2ProjectCompilation compilation,
        string locale,
        IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken = default)
    {
        Rmf2ProjectV5 compiled = compilation.Project ?? throw new TranslationAuthoringException("The RMF2 v5 project is unavailable.");
        Rmf2LocaleV5 target = compiled.Locales.SingleOrDefault(item => string.Equals(item.Tag, locale, StringComparison.Ordinal))
            ?? throw new TranslationAuthoringException("The catalog does not declare the locale '" + locale + "'.");
        Rmf2LocaleV5 baseLocale = compiled.Locales.Single(item => string.Equals(item.Tag, compiled.DefaultLocale, StringComparison.Ordinal));
        var targetByKey = target.DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        var baseByKey = baseLocale.DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        var originals = sources.ToDictionary(static source => source.Path, static source => source.GetUtf8Bytes(), StringComparer.Ordinal);
        var rmf2Sources = sources
            .Where(static source => source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(static source => source.Path, static source => source.GetUtf8Bytes(), StringComparer.Ordinal);
        Rmf2Workspace? workspace = null;
        var direct = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);

        foreach ((string key, string pattern) in values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? existingPath = targetByKey.TryGetValue(key, out Rmf2TranslationV5? existing) ? existing.SourceLocation.Path : null;
            Rmf2TranslationV5 source = baseByKey.TryGetValue(key, out Rmf2TranslationV5? found) ? found
                : throw new TranslationAuthoringException("The imported key '" + key + "' has no base-locale resource.");
            bool isDirect = (existingPath ?? source.SourceLocation.Path).EndsWith(".mf2", StringComparison.OrdinalIgnoreCase);
            if (isDirect)
            {
                direct[existingPath ?? DirectTargetPath(source.SourceLocation.Path, compiled.DefaultLocale, locale)] = StrictUtf8.GetBytes(pattern + "\n");
                continue;
            }
            workspace ??= new Rmf2Workspace(
                root,
                project,
                rmf2Sources.Select(static pair => new TranslationSource(pair.Key, pair.Value)),
                cancellationToken);
            ApplyRmf2Value(workspace, rmf2Sources, locale, key, pattern);
        }

        var edits = new List<TranslationSourceEdit>();
        foreach ((string path, byte[] bytes) in rmf2Sources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            byte[]? original = originals.GetValueOrDefault(path);
            if (original is not null && original.AsSpan().SequenceEqual(bytes)) continue;
            edits.Add(new TranslationSourceEdit(path, original, bytes));
        }
        foreach ((string path, byte[] bytes) in direct)
        {
            byte[]? original = originals.GetValueOrDefault(path);
            if (original is not null && original.AsSpan().SequenceEqual(bytes)) continue;
            edits.Add(new TranslationSourceEdit(path, original, bytes));
        }
        return edits.OrderBy(static edit => edit.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The sources with the edits applied, in path order.</summary>
    internal static IReadOnlyList<TranslationSource> Apply(IReadOnlyList<TranslationSource> sources, IReadOnlyList<TranslationSourceEdit> edits)
    {
        var result = sources.ToDictionary(static source => source.Path, static source => source.GetUtf8Bytes(), StringComparer.Ordinal);
        foreach (TranslationSourceEdit edit in edits) result[edit.Path] = edit.Bytes;
        return result.OrderBy(static pair => pair.Key, StringComparer.Ordinal).Select(static pair => new TranslationSource(pair.Key, pair.Value)).ToArray();
    }

    // A direct MF2 translation keeps the base file's name in the target locale's directory: <root>/en/greeting.mf2
    // becomes <root>/de/greeting.mf2.
    private static string DirectTargetPath(string basePath, string baseLocale, string locale)
    {
        string normalized = basePath.Replace('\\', '/');
        int file = normalized.LastIndexOf('/');
        string directory = file < 0 ? string.Empty : normalized[..file];
        int parent = directory.LastIndexOf('/');
        string localeDirectory = parent < 0 ? directory : directory[(parent + 1)..];
        if (file < 0 || !string.Equals(localeDirectory, baseLocale, StringComparison.OrdinalIgnoreCase))
            throw new TranslationAuthoringException("Direct MF2 resources require a locale directory and physical message filename.");
        return (parent < 0 ? string.Empty : directory[..(parent + 1)]) + locale + normalized[file..];
    }

    private static void ApplyRmf2Value(
        Rmf2Workspace workspace,
        Dictionary<string, byte[]> sources,
        string locale,
        string logicalKey,
        string value)
    {
        string[] logicalPath = logicalKey.Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (logicalPath.Length == 0)
            throw new TranslationAuthoringException("An imported RMF2 key is empty.");

        // Prefer the target locale's existing declaration. This keeps its
        // physical file, comments, and formatting intact while changing only
        // the message body.
        foreach (Rmf2ResourceDocument document in workspace.Documents
            .Where(document => string.Equals(Path.GetFileNameWithoutExtension(document.Source.Path), locale, StringComparison.OrdinalIgnoreCase)))
        {
            Rmf2ResourceNode? node = document.Nodes.FirstOrDefault(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(document.Source.Path, node)), logicalKey, StringComparison.Ordinal));
            if (node is null) continue;
            sources[document.Source.Path] = Rmf2ResourceWriter.SetMessage(
                new TranslationSource(document.Source.Path, sources[document.Source.Path]), node.Key, value);
            return;
        }

        // A missing target translation follows the base resource's namespace
        // and mount. Replace only the locale file name, then use the writer's
        // explicit path operation; never synthesize a legacy .mf2 path.
        Rmf2ResourceDocument? baseDocument = workspace.Documents
            .Where(document => string.Equals(Path.GetFileNameWithoutExtension(document.Source.Path), workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(document => document.Nodes.Any(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(document.Source.Path, node)), logicalKey, StringComparison.Ordinal)));
        IReadOnlyList<string> resolvedLogicalPath = logicalPath;
        string targetPath;
        if (baseDocument is not null)
        {
            Rmf2ResourceNode node = baseDocument.Nodes.First(node =>
                !node.IsGroup && string.Equals(string.Join('_', workspace.LogicalPath(baseDocument.Source.Path, node)), logicalKey, StringComparison.Ordinal));
            resolvedLogicalPath = workspace.LogicalPath(baseDocument.Source.Path, node);
            targetPath = Path.Combine(Path.GetDirectoryName(baseDocument.Source.Path) ?? string.Empty, locale + ".rmf2").Replace('\\', '/');
        }
        else
        {
            targetPath = locale + ".rmf2";
        }

        byte[] existing = sources.GetValueOrDefault(targetPath, []);
        string[] localPath = workspace.LocalPath(targetPath, resolvedLogicalPath).ToArray();
        sources[targetPath] = Rmf2ResourceWriter.InsertMessage(
            new TranslationSource(targetPath, existing), localPath, value);
    }
}
