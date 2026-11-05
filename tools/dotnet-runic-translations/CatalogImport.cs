using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Runic.CommandLine;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>The values of <c>import</c>: the source format and path, the selected locales and the optional group.</summary>
internal sealed record CatalogImportRequest(string From, string Source, IReadOnlyList<string> Locales, string? Group, bool Overwrite = false);

/// <summary>
/// <c>runic-translations import --from resx|i18next</c>: migrates an existing catalog into the project's <c>.rmf2</c>
/// sources. Messages go through the same source writer as <c>xliff import</c>: a new one is added to
/// <c>&lt;root&gt;[/&lt;group&gt;]/&lt;locale&gt;.rmf2</c>. An existing message (base text or translation) whose text differs
/// is kept and reported as <c>kept</c>, because migration often runs again after the team edited the <c>.rmf2</c> sources;
/// with <c>--overwrite</c> it keeps its file and comments and only its body changes.
/// What cannot be mapped is skipped or refused per message and reported; only an unreadable source, an invalid
/// invocation or a project that does not compile fails the command.
/// </summary>
internal static class CatalogImport
{
    private const int MaximumEntries = 1000;
    private const int MaximumLines = 32;
    private const int MaximumRounds = 4;
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    internal static int Run(string projectPath, CatalogImportRequest request, bool dryRun, ToolOperationResult result)
    {
        if (request.From is not ("resx" or "i18next"))
            throw new ToolUsageException($"--from expects resx or i18next, not '{request.From}'. PO files: convert them to XLIFF and use xliff import.");
        if (request.Group is not null && !Identifier.IsMatch(request.Group))
            throw new ToolUsageException($"--group must be an RMF2 identifier ([A-Za-z_][A-Za-z0-9_]*), not '{request.Group}'.");

        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        if (inputs.Messages.Any(static source => source.Path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)))
            throw new ToolUsageException("import writes grouped .rmf2 resources; this project uses direct .mf2 files.");
        Rmf2ProjectCompilation current = Compile(inputs.Project, inputs.Messages);
        current = TranslationManifestReader.WithManifestErrors(current, inputs.Project.Path, inputs.ManifestError);
        if (!current.Success || current.Project is null)
        {
            Program.ReportDiagnostics(current.Diagnostics, result);
            result.WriteOutputLine("The project does not compile; fix it before importing. No files were written.");
            return 1;
        }
        Rmf2ProjectV5 project = current.Project;
        string catalog = current.CatalogId!;
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string configPath = Path.GetFullPath(inputs.Project.Path, currentDirectory);
        var manifest = TextCatalogImport.Manifest.Read(inputs.Project.GetUtf8Bytes(), Path.GetDirectoryName(configPath)!);
        (string rootPath, IReadOnlyList<string> rootNamespace) = manifest.Roots[0];
        string directory = request.Group is null ? rootPath : Path.Combine(rootPath, request.Group);
        IReadOnlyList<string> prefix = request.Group is null ? rootNamespace : [.. rootNamespace, request.Group];
        var locales = new ImportLocales(project.DefaultLocale, project.Locales.Select(static locale => locale.Tag).ToArray(), request.Locales);
        foreach (string requested in request.Locales)
            if (locales.Declared(requested) is null) throw new ToolUsageException($"--locale '{requested}' is not declared in runic.json.");

        var report = new ImportReport();
        List<ImportedFile> files;
        try
        {
            files = request.From == "resx" ? ResxImport.Read(request.Source, locales, report) : I18nextImport.Read(request.Source, locales, report);
        }
        catch (ImportSourceException exception)
        {
            result.AddDiagnostic(ToolCodes.InterchangeDocument, "import", $"{exception.Code}: {exception.Message}", CommandDiagnosticSeverity.Error);
            result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "The source could not be imported; nothing was written.");
            result.WriteOutputLine("The source could not be imported; nothing was written.");
            result.ExitCategory = CommandExitCategory.Validation;
            return 1;
        }
        if (files.Count == 0)
            throw new ToolUsageException($"'{request.Source}' has no file for a selected locale that runic.json declares.");

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
        IReadOnlyList<TranslationSource> planned = inputs.Messages;
        for (int round = 0; ; round++)
        {
            var noBase = new List<(string Locale, string Key)>();
            planned = PlanAll(currentDirectory, inputs, current, messages, refused, directory, noBase, out Rmf2ProjectCompilation proposed);
            if (proposed.Success && proposed.Project is not null)
            {
                foreach ((string locale, string key) in noBase)
                    report.Refuse(locale, key, "IMPORT-NO-BASE", $"There is no {project.DefaultLocale} message '{key}' to translate; import the base locale first.");
                break;
            }
            var errors = proposed.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).ToArray();
            var attributed = Attribute(currentDirectory, inputs.Project, planned, errors, messages, refused);
            if (attributed.Count == 0 || round == MaximumRounds)
            {
                Program.ReportDiagnostics(errors, result);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "The imported messages do not compile; nothing was written.");
                result.WriteOutputLine("The imported messages do not compile; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            }
            foreach (((string locale, string key), string reason) in attributed)
            {
                // A refused base message takes its translations with it (they are reported as having no base).
                refused.Add((locale, key));
                report.Refuse(locale, key, "IMPORT-INVALID", reason);
            }
        }

        // Counts against the sources before the import.
        var applied = new List<TranslationsToolInterchangeEntry>();
        var summary = new List<string>();
        int added = 0, changed = 0, unchanged = 0, keptCount = 0;
        foreach ((string locale, Dictionary<string, Planned> byKey) in messages.OrderBy(pair => pair.Key != project.DefaultLocale))
        {
            int localeAdded = 0, localeChanged = 0, localeUnchanged = 0, localeKept = 0;
            foreach ((string key, Planned message) in byKey)
            {
                if (message.Kept)
                {
                    localeKept++;
                    applied.Add(new(key, "kept", "IMPORT-EXISTS", "The message already exists with other text; it was kept. Use --overwrite to replace it.") { Locale = locale });
                    continue;
                }
                if (refused.Contains((locale, key)) || refused.Contains((project.DefaultLocale, key)) || message.NoBase) continue;
                if (!before[locale].TryGetValue(key, out string? existing)) { localeAdded++; applied.Add(new(key, "added", null, null) { Locale = locale }); }
                else if (TranslationInterchangePattern.Canonical(existing) != TranslationInterchangePattern.Canonical(message.Message.Pattern)) { localeChanged++; applied.Add(new(key, "changed", null, null) { Locale = locale }); }
                else localeUnchanged++;
            }
            summary.Add($"{locale} {localeAdded} added, {localeChanged} changed, {localeUnchanged} unchanged" + (localeKept != 0 ? $", {localeKept} kept" : string.Empty));
            added += localeAdded; changed += localeChanged; unchanged += localeUnchanged; keptCount += localeKept;
        }

        var originals = inputs.Messages.ToDictionary(static source => source.Path, static source => source.GetUtf8Bytes(), StringComparer.Ordinal);
        var writes = new List<InterchangeOperations.SourceWrite>();
        foreach (TranslationSource source in planned)
        {
            byte[] bytes = source.GetUtf8Bytes();
            byte[]? original = originals.GetValueOrDefault(source.Path);
            if (original is not null && original.AsSpan().SequenceEqual(bytes)) continue;
            writes.Add(new(Path.GetFullPath(source.Path, currentDirectory), source.Path, original, bytes));
        }
        if (!dryRun)
        {
            try { InterchangeOperations.WriteSources(writes); }
            catch (InterchangeOperations.SourceChangedException exception)
            {
                result.AddDiagnostic(ToolCodes.InterchangeDocument, "import", exception.Message, CommandDiagnosticSeverity.Error);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "A file changed on disk during the import; nothing was written. Run the import again.");
                result.WriteOutputLine("A file changed on disk during the import; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            }
        }

        string verb = dryRun ? "would write" : "wrote";
        result.WriteOutputLine($"{(dryRun ? "dry run: " : string.Empty)}imported {request.From}: {string.Join("; ", summary)}; {report.Skipped} skipped, {report.Refused} refused.");
        foreach (InterchangeOperations.SourceWrite write in writes) result.WriteOutputLine($"{verb} {write.DisplayPath}.");
        if (keptCount != 0)
            result.WriteOutputLine($"{keptCount} existing message(s) differ from the source and were kept; --overwrite replaces them.");
        foreach (TranslationsToolInterchangeEntry entry in report.Entries.Take(MaximumLines))
            result.WriteOutputLine($"{entry.Outcome} {entry.Locale} {entry.Key}: {entry.Code}: {entry.Message}");
        foreach (TranslationsToolInterchangeNotice notice in report.Notices.Take(MaximumLines))
            result.WriteOutputLine($"notice {notice.Location}: {notice.Code}: {notice.Message}");
        int hidden = Math.Max(0, report.Entries.Count - MaximumLines) + Math.Max(0, report.Notices.Count - MaximumLines);
        if (hidden != 0) result.WriteOutputLine($"{hidden} more line(s); --format json lists them.");
        foreach (TranslationsToolInterchangeEntry entry in report.Entries.Where(static entry => entry.Outcome == "refused").Take(MaximumLines))
            result.AddDiagnostic(ToolCodes.InterchangeUnit, "import-message", $"{entry.Locale} {entry.Key}: {entry.Code}: {entry.Message}", CommandDiagnosticSeverity.Warning);

        List<TranslationsToolInterchangeEntry> entries = [.. applied, .. report.Entries];
        result.Interchange = new TranslationsToolInterchange("import", catalog)
        {
            Format = request.From,
            Locale = messages.Count == 1 ? messages.Keys.Single() : null,
            DryRun = dryRun,
            Documents = files.Select(static file => new TranslationsToolInterchangeDocument(file.Locale, file.Display, file.Read)).ToArray(),
            Units = new TranslationsToolInterchangeCounts
            {
                Total = added + changed + unchanged + keptCount + report.Skipped + report.Refused,
                Applied = added + changed + unchanged, Added = added, Changed = changed, Unchanged = unchanged, Kept = keptCount,
                Skipped = report.Skipped, Refused = report.Refused,
            },
            Entries = entries.Take(MaximumEntries).ToArray(),
            EntriesTruncated = entries.Count > MaximumEntries,
            Notices = report.Notices.Take(MaximumEntries).ToArray(),
            Files = writes.Select(static write => new TranslationsToolInterchangeFile(write.DisplayPath, write.OriginalBytes is null ? "added" : "changed")).ToArray(),
        };
        return 0;
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
        string currentDirectory,
        CompilerInputs inputs,
        Rmf2ProjectCompilation current,
        SortedDictionary<string, Dictionary<string, Planned>> messages,
        HashSet<(string Locale, string Key)> refused,
        string directory,
        List<(string Locale, string Key)> noBase,
        out Rmf2ProjectCompilation compilation)
    {
        string baseLocale = current.Project!.DefaultLocale;
        IReadOnlyList<TranslationSource> sources = inputs.Messages;
        compilation = current;
        if (messages.TryGetValue(baseLocale, out Dictionary<string, Planned>? baseMessages))
        {
            string file = InputFiles.DisplayPath(Path.Combine(directory, baseLocale + ".rmf2"), currentDirectory);
            var values = baseMessages.Where(pair => !refused.Contains((baseLocale, pair.Key)) && !pair.Value.Kept)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value.Message.Pattern, StringComparer.Ordinal);
            var additions = baseMessages.ToDictionary(static pair => pair.Key,
                pair => new TranslationSourceAddition(pair.Value.LogicalPath, file, pair.Value.Message.Comments), StringComparer.Ordinal);
            IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(currentDirectory, inputs.Project, sources, compilation, baseLocale, values, additions);
            if (edits.Count != 0)
            {
                sources = TranslationInterchangeSourceWriter.Apply(sources, edits);
                compilation = Compile(inputs.Project, sources);
                if (!compilation.Success || compilation.Project is null) return sources;
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
