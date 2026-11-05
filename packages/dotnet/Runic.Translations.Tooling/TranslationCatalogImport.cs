using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

/// <summary>The values of <c>import</c>: the source format and path, the selected locales and the optional group.</summary>
/// <param name="Source">The <c>.resx</c> file, or the i18next file or directory; relative to the base directory of the project sources.</param>
internal sealed record TranslationCatalogImportRequest(string From, string Source, IReadOnlyList<string> Locales, string? Group, bool Overwrite = false);

/// <summary>Why an import plan cannot be applied.</summary>
internal enum TranslationCatalogImportFailure
{
    /// <summary>The plan can be applied.</summary>
    None,
    /// <summary>The project does not compile before the import; <see cref="TranslationCatalogImportPlan.Diagnostics"/> lists its diagnostics.</summary>
    ProjectDoesNotCompile,
    /// <summary>The source cannot be read at all; see <see cref="TranslationCatalogImportPlan.FailureCode"/>.</summary>
    SourceUnreadable,
    /// <summary>The imported messages do not compile; <see cref="TranslationCatalogImportPlan.Diagnostics"/> lists the errors.</summary>
    MessagesDoNotCompile,
}

/// <summary>A message the import adds, changes, keeps, skips or refuses.</summary>
/// <param name="Outcome"><c>added</c>, <c>changed</c>, <c>kept</c>, <c>skipped</c> or <c>refused</c>.</param>
/// <param name="Code">The reason, such as <c>RESX-NON-STRING</c>; absent for an added or changed message.</param>
internal sealed record TranslationImportEntry(string Locale, string Key, string Outcome, string? Code, string? Message);

/// <param name="Code">The notice code, such as <c>RESX-FORMAT</c>.</param>
/// <param name="Location">The source file, and the key where it applies to one.</param>
internal sealed record TranslationImportNotice(string Code, string Location, string Message);

/// <summary>A source file the import read: its locale, its path as reported and the messages read from it.</summary>
internal sealed record TranslationImportDocument(string Locale, string Path, int Read);

/// <summary>One file an import replaces: its full and displayed path, the bytes it was read with (none for a new file) and the new bytes.</summary>
internal sealed record TranslationImportSourceWrite(string FullPath, string DisplayPath, byte[]? OriginalBytes, byte[] Bytes);

/// <summary>The messages of one locale against the sources before the import.</summary>
internal sealed record TranslationImportLocaleCounts(string Locale, int Added, int Changed, int Unchanged, int Kept);

/// <summary>What <see cref="TranslationCatalogImport.Plan"/> computed; nothing is written.</summary>
/// <param name="From"><c>resx</c> or <c>i18next</c>.</param>
internal sealed record TranslationCatalogImportPlan(string From, TranslationCatalogImportFailure Failure)
{
    /// <summary>The catalog identifier; null when the project does not compile.</summary>
    internal string? Catalog { get; init; }

    /// <summary>The code of an unreadable source, such as <c>IMPORT-TOO-LARGE</c>.</summary>
    internal string? FailureCode { get; init; }

    /// <summary>Why the source cannot be read.</summary>
    internal string? FailureMessage { get; init; }

    /// <summary>The diagnostics of a project or of imported messages that do not compile.</summary>
    internal IReadOnlyList<TranslationDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>The source files read.</summary>
    internal IReadOnlyList<TranslationImportDocument> Documents { get; init; } = [];

    /// <summary>The files to write, in order.</summary>
    internal IReadOnlyList<TranslationImportSourceWrite> Writes { get; init; } = [];

    /// <summary>The added, changed and kept messages, base locale first.</summary>
    internal IReadOnlyList<TranslationImportEntry> Applied { get; init; } = [];

    /// <summary>The skipped and refused messages, in the order they were found.</summary>
    internal IReadOnlyList<TranslationImportEntry> Entries { get; init; } = [];

    internal IReadOnlyList<TranslationImportNotice> Notices { get; init; } = [];

    /// <summary>The counts per imported locale, base locale first.</summary>
    internal IReadOnlyList<TranslationImportLocaleCounts> Locales { get; init; } = [];

    internal int Skipped { get; init; }

    internal int Refused { get; init; }

    internal int Added => Locales.Sum(static locale => locale.Added);

    internal int Changed => Locales.Sum(static locale => locale.Changed);

    internal int Unchanged => Locales.Sum(static locale => locale.Unchanged);

    internal int Kept => Locales.Sum(static locale => locale.Kept);
}

/// <summary>
/// <c>runic-translations import --from resx|i18next</c> and the Translations Editor: migrates an existing catalog into the project's <c>.rmf2</c>
/// sources. Messages go through the same source writer as <c>xliff import</c>: a new one is added to
/// <c>&lt;root&gt;[/&lt;group&gt;]/&lt;locale&gt;.rmf2</c>. An existing message (base text or translation) whose text differs
/// is kept and reported as <c>kept</c>, because migration often runs again after the team edited the <c>.rmf2</c> sources;
/// with <c>--overwrite</c> it keeps its file and comments and only its body changes.
/// What cannot be mapped is skipped or refused per message and reported; only an unreadable source, an invalid
/// invocation or a project that does not compile fails the command. This plans the import; the caller writes the files.
/// </summary>
internal static class TranslationCatalogImport
{
    private const int MaximumRounds = 4;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    /// <summary>Plans the import into <paramref name="sources"/>.</summary>
    /// <param name="withProjectErrors">Adds diagnostics to the compilation of the project before the import, such as runic.json layout errors.</param>
    /// <exception cref="TranslationToolingUsageException">The request is invalid for the project or the source.</exception>
    internal static TranslationCatalogImportPlan Plan(TranslationProjectSources sources, TranslationCatalogImportRequest request,
        Func<Rmf2ProjectCompilation, Rmf2ProjectCompilation>? withProjectErrors = null)
    {
        Validate(request);

        if (sources.Messages.Any(static source => source.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)))
            throw new TranslationToolingUsageException("import writes grouped .rmf2 resources; this project uses direct .mf2 files.");
        Rmf2ProjectCompilation strict = Compile(sources.Project, sources.Messages);
        if (withProjectErrors is not null) strict = withProjectErrors(strict);
        // A catalog being translated (RTR0010 and the other validation policies) imports like a complete one; only
        // runic.json, base-locale or syntax errors stop the import, as they stop status.
        if (TranslationStatus.ForStatus(strict, sources.Project, sources.Messages) is not { Project: not null } current)
            return new TranslationCatalogImportPlan(request.From, TranslationCatalogImportFailure.ProjectDoesNotCompile) { Diagnostics = strict.Diagnostics };
        Rmf2ProjectV5 project = current.Project;
        string catalog = current.CatalogId!;
        string currentDirectory = Path.GetFullPath(sources.BaseDirectory);
        string configPath = Path.GetFullPath(sources.Project.Path, currentDirectory);
        var manifest = ImportManifest.Read(sources.Project.GetUtf8Bytes(), Path.GetDirectoryName(configPath)!);
        (string rootPath, IReadOnlyList<string> rootNamespace) = manifest.Roots[0];
        string directory = request.Group is null ? rootPath : Path.Combine(rootPath, request.Group);
        IReadOnlyList<string> prefix = request.Group is null ? rootNamespace : [.. rootNamespace, request.Group];
        var locales = new ImportLocales(project.DefaultLocale, project.Locales.Select(static locale => locale.Tag).ToArray(), request.Locales);
        foreach (string requested in request.Locales)
            if (locales.Declared(requested) is null) throw new TranslationToolingUsageException($"--locale '{requested}' is not declared in runic.json.");

        var report = new ImportReport(currentDirectory);
        List<ImportedFile> files;
        try
        {
            files = request.From == "resx" ? ResxImport.Read(request.Source, locales, report) : I18nextImport.Read(request.Source, locales, report);
        }
        catch (ImportSourceException exception)
        {
            return new TranslationCatalogImportPlan(request.From, TranslationCatalogImportFailure.SourceUnreadable)
            {
                Catalog = catalog, FailureCode = exception.Code, FailureMessage = exception.Message,
            };
        }
        if (files.Count == 0)
            throw new TranslationToolingUsageException($"'{request.Source}' has no file for a selected locale that runic.json declares.");

        // The sources before the import: the text of each existing message per locale.
        var before = project.Locales.ToDictionary(static locale => locale.Tag,
            static locale => locale.DirectResources.ToDictionary(static resource => resource.Key, static resource => Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparer.Ordinal),
            StringComparer.Ordinal);

        // One message per locale and key. The generated key joins the source-root namespace, the group and the path.
        var messages = new SortedDictionary<string, Dictionary<string, Planned>>(StringComparer.Ordinal);
        foreach (ImportedFile file in files)
        {
            if (!messages.TryGetValue(file.Locale, out Dictionary<string, Planned>? byKey)) messages[file.Locale] = byKey = new(StringComparer.Ordinal);
            var leaves = byKey.Values.Select(static planned => planned.LogicalPath).ToList();
            foreach (ImportedMessage message in file.Messages)
            {
                IReadOnlyList<string> logical = [.. prefix, .. message.Segments];
                string key = string.Join('_', logical);
                if (byKey.ContainsKey(key) || Overlaps(leaves, logical))
                {
                    report.Refuse(file.Locale, key, "IMPORT-KEY-COLLISION", $"'{message.SourceKey}' maps to '{key}', which another imported message already uses or contains.");
                    continue;
                }
                if (!ImportMapping.ReadsBack(message.Pattern))
                {
                    report.Refuse(file.Locale, key, "IMPORT-PATTERN", $"'{message.SourceKey}' cannot be written as an RMF2 message.");
                    continue;
                }
                leaves.Add(logical);
                // Without --overwrite an existing message whose text differs is kept: not planned, reported once below.
                bool kept = !request.Overwrite && before[file.Locale].TryGetValue(key, out string? existingText) &&
                    TranslationInterchangePattern.Canonical(existingText) != TranslationInterchangePattern.Canonical(message.Pattern);
                byKey[key] = new Planned(message, logical, file.Display) { Kept = kept };
            }
        }

        // Plan, compile, and refuse the messages a compiler error points at; repeat until the project compiles.
        var refused = new HashSet<(string Locale, string Key)>();
        IReadOnlyList<TranslationSource> planned = sources.Messages;
        for (int round = 0; ; round++)
        {
            var noBase = new List<(string Locale, string Key)>();
            planned = PlanAll(sources, current, messages, refused, directory, noBase, out Rmf2ProjectCompilation proposed);
            // Imported base messages that a locale does not translate yet (RTR0010) are what a migration leaves to
            // translate, as runic-translations locale add leaves them; they do not refuse a message.
            var errors = proposed.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error && diagnostic.Id != "RTR0010").ToArray();
            if (errors.Length == 0)
            {
                foreach ((string locale, string key) in noBase)
                    report.Refuse(locale, key, "IMPORT-NO-BASE", $"There is no {project.DefaultLocale} message '{key}' to translate; import the base locale first.");
                break;
            }
            var attributed = Attribute(currentDirectory, sources.Project, planned, errors, messages, refused);
            if (attributed.Count == 0 || round == MaximumRounds)
                return new TranslationCatalogImportPlan(request.From, TranslationCatalogImportFailure.MessagesDoNotCompile) { Catalog = catalog, Diagnostics = errors };
            foreach (((string locale, string key), string reason) in attributed)
            {
                // A refused base message takes its translations with it (they are reported as having no base).
                refused.Add((locale, key));
                report.Refuse(locale, key, "IMPORT-INVALID", reason);
            }
        }

        // Counts against the sources before the import.
        var applied = new List<TranslationImportEntry>();
        var counts = new List<TranslationImportLocaleCounts>();
        foreach ((string locale, Dictionary<string, Planned> byKey) in messages.OrderBy(pair => pair.Key != project.DefaultLocale))
        {
            int localeAdded = 0, localeChanged = 0, localeUnchanged = 0, localeKept = 0;
            foreach ((string key, Planned message) in byKey)
            {
                if (message.Kept)
                {
                    localeKept++;
                    applied.Add(new(locale, key, "kept", "IMPORT-EXISTS", "The message already exists with other text; it was kept. Use --overwrite to replace it."));
                    continue;
                }
                if (refused.Contains((locale, key)) || refused.Contains((project.DefaultLocale, key)) || message.NoBase) continue;
                if (!before[locale].TryGetValue(key, out string? existing)) { localeAdded++; applied.Add(new(locale, key, "added", null, null)); }
                else if (TranslationInterchangePattern.Canonical(existing) != TranslationInterchangePattern.Canonical(message.Message.Pattern)) { localeChanged++; applied.Add(new(locale, key, "changed", null, null)); }
                else localeUnchanged++;
            }
            counts.Add(new(locale, localeAdded, localeChanged, localeUnchanged, localeKept));
        }

        var originals = sources.Messages.ToDictionary(static source => source.Path, static source => source.GetUtf8Bytes(), StringComparer.Ordinal);
        var writes = new List<TranslationImportSourceWrite>();
        foreach (TranslationSource source in planned)
        {
            byte[] bytes = source.GetUtf8Bytes();
            byte[]? original = originals.GetValueOrDefault(source.Path);
            if (original is not null && original.AsSpan().SequenceEqual(bytes)) continue;
            writes.Add(new(Path.GetFullPath(source.Path, currentDirectory), source.Path, original, bytes));
        }
        return new TranslationCatalogImportPlan(request.From, TranslationCatalogImportFailure.None)
        {
            Catalog = catalog,
            Documents = files.Select(static file => new TranslationImportDocument(file.Locale, file.Display, file.Read)).ToArray(),
            Writes = writes,
            Applied = applied,
            Entries = report.Entries,
            Notices = report.Notices,
            Locales = counts,
            Skipped = report.Skipped,
            Refused = report.Refused,
        };
    }

    /// <summary>Checks the format and the group of a request before the project is read.</summary>
    /// <exception cref="TranslationToolingUsageException">The format or the group is invalid.</exception>
    internal static void Validate(TranslationCatalogImportRequest request)
    {
        if (request.From is not ("resx" or "i18next"))
            throw new TranslationToolingUsageException($"--from expects resx or i18next, not '{request.From}'. PO files: convert them to XLIFF and use xliff import.");
        if (request.Group is not null && !Identifier.IsMatch(request.Group))
            throw new TranslationToolingUsageException($"--group must be an RMF2 identifier ([A-Za-z_][A-Za-z0-9_]*), not '{request.Group}'.");
    }

    private sealed class Planned(ImportedMessage message, IReadOnlyList<string> logicalPath, string display)
    {
        internal ImportedMessage Message { get; } = message;
        internal IReadOnlyList<string> LogicalPath { get; } = logicalPath;
        internal string Display { get; } = display;
        /// <summary>Set by a planning round: a translation whose key has no base message.</summary>
        internal bool NoBase { get; set; }
        /// <summary>An existing message with other text that the import keeps (no <c>--overwrite</c>).</summary>
        internal bool Kept { get; init; }
    }

    // A path that equals, contains or lies inside an earlier one would make a leaf and a group of the same name.
    private static bool Overlaps(List<IReadOnlyList<string>> leaves, IReadOnlyList<string> path) =>
        leaves.Any(leaf => leaf.Count != path.Count &&
            leaf.Take(Math.Min(leaf.Count, path.Count)).SequenceEqual(path.Take(Math.Min(leaf.Count, path.Count)), StringComparer.Ordinal));

    private static Rmf2ProjectCompilation Compile(TranslationSource project, IReadOnlyList<TranslationSource> sources) =>
        TranslationCompiler.CompileRmf2ProjectV5(project, sources);

    // The base locale first (adding its new messages), then each translation against the base it now has.
    private static IReadOnlyList<TranslationSource> PlanAll(
        TranslationProjectSources inputs,
        Rmf2ProjectCompilation current,
        SortedDictionary<string, Dictionary<string, Planned>> messages,
        HashSet<(string Locale, string Key)> refused,
        string directory,
        List<(string Locale, string Key)> noBase,
        out Rmf2ProjectCompilation compilation)
    {
        string currentDirectory = Path.GetFullPath(inputs.BaseDirectory);
        string baseLocale = current.Project!.DefaultLocale;
        IReadOnlyList<TranslationSource> sources = inputs.Messages;
        compilation = current;
        if (messages.TryGetValue(baseLocale, out Dictionary<string, Planned>? baseMessages))
        {
            string file = inputs.DisplayPath(Path.Combine(directory, baseLocale + ".rmf2"));
            var values = baseMessages.Where(pair => !refused.Contains((baseLocale, pair.Key)) && !pair.Value.Kept)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value.Message.Pattern, StringComparer.Ordinal);
            var additions = baseMessages.ToDictionary(static pair => pair.Key,
                pair => new TranslationSourceAddition(pair.Value.LogicalPath, file, pair.Value.Message.Comments), StringComparer.Ordinal);
            IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(currentDirectory, inputs.Project, sources, compilation, baseLocale, values, additions);
            if (edits.Count != 0)
            {
                sources = TranslationInterchangeSourceWriter.Apply(sources, edits);
                compilation = Compile(inputs.Project, sources);
                // New base messages are untranslated in the other locales (RTR0010) until their translations are
                // planned below; only other errors stop here, and the caller attributes them.
                if (TranslationStatus.ForStatus(compilation, inputs.Project, sources) is not { Project: not null } relaxed) return sources;
                compilation = relaxed;
            }
        }

        var baseKeys = compilation.Project!.Locales.Single(locale => locale.Tag == baseLocale).DirectResources
            .Select(static resource => resource.Key).ToHashSet(StringComparer.Ordinal);
        Rmf2ProjectCompilation withBase = compilation;
        foreach ((string locale, Dictionary<string, Planned> byKey) in messages)
        {
            if (locale == baseLocale) continue;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, Planned message) in byKey)
            {
                // A translation of a refused base message is not applied either.
                message.NoBase = !baseKeys.Contains(key) || refused.Contains((baseLocale, key));
                if (refused.Contains((locale, key)) || message.Kept) continue;
                if (message.NoBase) { noBase.Add((locale, key)); continue; }
                values[key] = message.Message.Pattern;
            }
            // Each locale's edits touch only its own files, so the base compilation stays valid for the next locale.
            IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(currentDirectory, inputs.Project, sources, withBase, locale, values);
            sources = TranslationInterchangeSourceWriter.Apply(sources, edits);
        }
        compilation = Compile(inputs.Project, sources);
        return sources;
    }

    // The imported (locale, key) that each error lies in, by the error's byte offset in the planned source.
    private static Dictionary<(string Locale, string Key), string> Attribute(
        string currentDirectory,
        TranslationSource project,
        IReadOnlyList<TranslationSource> sources,
        IReadOnlyList<TranslationDiagnostic> errors,
        SortedDictionary<string, Dictionary<string, Planned>> messages,
        HashSet<(string Locale, string Key)> refused)
    {
        var attributed = new Dictionary<(string, string), string>();
        var rmf2 = sources.Where(static source => source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)).ToArray();
        Rmf2Workspace workspace;
        try { workspace = new Rmf2Workspace(currentDirectory, project, rmf2); }
        catch (TranslationAuthoringException) { return attributed; }
        foreach (TranslationDiagnostic error in errors)
        {
            string path = error.Location.Path.Replace('\\', '/');
            Rmf2ResourceDocument? document = workspace.Documents.FirstOrDefault(document => document.Source.Path.Replace('\\', '/') == path);
            if (document is null) return [];
            Rmf2ResourceNode? node = document.Nodes.FirstOrDefault(node => !node.IsGroup &&
                error.Location.StartByte >= node.NameLocation.StartByte && error.Location.StartByte < node.Location.StartByte + Math.Max(node.Location.LengthBytes, 1));
            if (node is null) return [];
            string locale = Path.GetFileNameWithoutExtension(path);
            string key = string.Join('_', workspace.LogicalPath(document.Source.Path, node));
            string? declared = messages.Keys.FirstOrDefault(item => string.Equals(item, locale, StringComparison.OrdinalIgnoreCase));
            if (declared is null || !messages[declared].ContainsKey(key) || refused.Contains((declared, key))) return [];
            attributed.TryAdd((declared, key), $"{error.Id}: {error.Message}");
        }
        return attributed;
    }
}
