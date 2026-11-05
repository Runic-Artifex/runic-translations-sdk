using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.CommandLine;
using Runic.Translations.Compiler;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>
/// The optional, SDK-owned review file <c>&lt;catalog&gt;.review.json</c> beside runic.json
/// (<c>runic.translations.interchange-review/2</c>). Each entry records the per-unit fingerprint of the source its
/// translation was made or approved for; <c>validate</c> warns when the source changed since (<c>RTR0090</c>).
/// </summary>
internal static class ReviewFile
{
    internal const string StaleCode = "RTR0090";
    internal const string InvalidCode = "RTR0091";

    internal static string PathFor(CompilerInputs inputs, string catalog)
    {
        if (catalog.IndexOfAny(['/', '\\']) >= 0 || catalog is "." or ".." || catalog.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ToolUsageException($"the catalog identifier '{catalog}' cannot name a review file.");
        string project = Path.GetFullPath(inputs.Project.Path, Environment.CurrentDirectory);
        return Path.Combine(Path.GetDirectoryName(project)!, catalog + ".review.json");
    }

    internal static TranslationInterchangeReview? Read(string path, string catalog) =>
        File.Exists(path) ? Parse(File.ReadAllBytes(path), catalog) : null;

    internal static TranslationInterchangeReview Parse(byte[] bytes, string catalog)
    {
        TranslationInterchangeReview review = TranslationInterchange.ImportReviewJson(bytes);
        if (!string.Equals(review.CatalogId, catalog, StringComparison.Ordinal))
            throw new TranslationInterchangeException("REVIEW-CATALOG", $"The review file is for catalog '{review.CatalogId}', not '{catalog}'.");
        return review;
    }

    internal static void Write(string path, TranslationInterchangeReview review) =>
        InterchangeOperations.WriteFile(path, TranslationInterchange.ExportReviewJson(review));

    /// <summary>The <c>validate</c> warnings of a project with a review file: stale translations and an unreadable file.</summary>
    internal static IReadOnlyList<TranslationDiagnostic> Check(CompilerInputs inputs, Rmf2ProjectCompilation compilation)
    {
        if (compilation.Project is not { } project) return [];
        string path = PathFor(inputs, project.Id);
        if (!File.Exists(path)) return [];
        string display = InputFiles.DisplayPath(path, Path.GetFullPath(Environment.CurrentDirectory));
        TranslationInterchangeReview review;
        try { review = Read(path, project.Id)!; }
        catch (TranslationInterchangeException exception)
        {
            return [new TranslationDiagnostic(InvalidCode, TranslationDiagnosticSeverity.Warning,
                $"The review file cannot be read ({exception.Code}): {exception.Message}", new TextSourceLocation(display, 0, 0, 1, 1, 1, 1))];
        }
        var baseResources = project.Locales.Single(locale => locale.Tag == project.DefaultLocale).DirectResources.ToDictionary(static resource => resource.Key, StringComparer.Ordinal);
        return Stale(project, compilation, review).Select(stale => new TranslationDiagnostic(StaleCode, TranslationDiagnosticSeverity.Warning,
            $"The '{stale.Entry.Locale}' translation of '{stale.Entry.Key}' ({TranslationReviewStatesText(stale.Entry.State)}) predates a change to its source; review it, then record it with runic-translations review mark --key {stale.Entry.Key} --locale {stale.Entry.Locale} --state <state>.",
            stale.Translation?.SourceLocation ?? baseResources[stale.Entry.Key].SourceLocation)).ToArray();
    }

    /// <summary>
    /// The review entries whose source changed since they were recorded: <c>validate</c> warns about each (RTR0090) and
    /// <c>status</c> counts them per locale. A removed translation still counts unless its entry is a draft.
    /// </summary>
    internal static IReadOnlyList<(TranslationInterchangeReviewEntry Entry, Rmf2TranslationV5? Translation)> Stale(Rmf2ProjectV5 project, Rmf2ProjectCompilation compilation, TranslationInterchangeReview review)
    {
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(compilation);
        var locales = project.Locales.ToDictionary(static locale => locale.Tag, StringComparer.Ordinal);
        var stale = new List<(TranslationInterchangeReviewEntry, Rmf2TranslationV5?)>();
        foreach (TranslationInterchangeReviewEntry entry in review.Entries.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ThenBy(static entry => entry.Locale, StringComparer.Ordinal))
        {
            if (entry.SourceFingerprint is null || !fingerprints.TryGetValue(entry.Key, out string? current) ||
                string.Equals(entry.SourceFingerprint, current, StringComparison.Ordinal) ||
                !locales.TryGetValue(entry.Locale, out Rmf2LocaleV5? locale) || entry.Locale == project.DefaultLocale) continue;
            Rmf2TranslationV5? translation = locale.DirectResources.FirstOrDefault(resource => resource.Key == entry.Key);
            if (translation is null && entry.State == TranslationReviewState.Draft) continue;
            stale.Add((entry, translation));
        }
        return stale;
    }

    internal static string TranslationReviewStatesText(TranslationReviewState state) => state switch
    {
        TranslationReviewState.Draft => "draft",
        TranslationReviewState.Translated => "translated",
        TranslationReviewState.NeedsReview => "needs-review",
        _ => "approved",
    };

    internal static bool TryParseState(string value, out TranslationReviewState state)
    {
        switch (value)
        {
            case "draft": state = TranslationReviewState.Draft; return true;
            case "translated": state = TranslationReviewState.Translated; return true;
            case "needs-review": state = TranslationReviewState.NeedsReview; return true;
            case "approved": state = TranslationReviewState.Approved; return true;
            default: state = default; return false;
        }
    }
}

/// <summary><c>xliff export</c>, <c>xliff import</c> and <c>review mark</c>.</summary>
internal static class InterchangeOperations
{
    private const int MaximumEntries = 1000;
    private const int MaximumUnitWarnings = 32;

    internal static int Export(string projectPath, string output, IReadOnlyList<string> locales, string? reviewPath, ToolOperationResult result)
    {
        (CompilerInputs inputs, Rmf2ProjectCompilation compilation)? compiled = Compile(projectPath, result);
        if (compiled is not { } project) return 1;
        string catalog = project.compilation.CatalogId!;
        string path = reviewPath is null ? ReviewFile.PathFor(project.inputs, catalog) : Path.GetFullPath(reviewPath, Environment.CurrentDirectory);
        if (reviewPath is not null && !File.Exists(path)) throw new ToolUsageException($"the review file '{reviewPath}' does not exist.");
        TranslationInterchangeReview? review;
        TranslationXliffExportResult export;
        string[] declared = project.compilation.Locales.Where(locale => locale != project.compilation.DefaultLocale).ToArray();
        foreach (string locale in locales)
            if (!declared.Contains(locale, StringComparer.Ordinal))
                throw new ToolUsageException($"--locale {locale} is not a translated locale of catalog '{catalog}' ({string.Join(", ", declared)}).");
        try
        {
            review = ReviewFile.Read(path, catalog);
            // With --locale, only the selected locales' review data is exported, and reported.
            TranslationInterchangeReview? selectedReview = review is null || locales.Count == 0 ? review
                : new TranslationInterchangeReview(catalog, review.Entries.Where(entry => locales.Contains(entry.Locale, StringComparer.Ordinal)));
            export = TranslationInterchange.ExportXliff21(project.compilation, selectedReview);
        }
        catch (TranslationInterchangeException exception) { return Refuse(result, exception, "The XLIFF documents could not be exported; nothing was written."); }

        TranslationXliffDocument[] selected = export.Documents.Where(document => locales.Count == 0 || locales.Contains(document.TargetLocale, StringComparer.Ordinal)).ToArray();
        ArtifactFiles.WriteAtomically(output, selected.Select(document => new ToolArtifact(catalog + "." + document.TargetLocale + ".xliff", document.Bytes.ToArray())).ToArray());

        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        var documents = selected.Select(document => new TranslationsToolInterchangeDocument(document.TargetLocale,
            Display(Path.Combine(Path.GetFullPath(output, currentDirectory), catalog + "." + document.TargetLocale + ".xliff")),
            project.compilation.Project!.CanonicalMessages.Count)).ToArray();
        IReadOnlyList<TranslationInterchangeLoss> notices = export.Report.Losses;
        int demoted = notices.Count(static loss => loss.Code == "REVIEW-STALE");
        int structured = notices.Where(static loss => loss.Code == "XLIFF21-STRUCTURED-MESSAGE").Select(static loss => loss.Location).Distinct(StringComparer.Ordinal).Count();
        foreach (TranslationsToolInterchangeDocument document in documents)
            result.WriteOutputLine($"wrote {document.Path} ({document.Units} unit(s)).");
        if (structured != 0) result.WriteOutputLine($"{structured} message(s) with selectors, declarations or markup are exported as translate=\"no\"; translate them in the sources.");
        if (demoted != 0) result.WriteOutputLine($"{demoted} stale approval(s) were exported as needs-review.");
        result.Interchange = new TranslationsToolInterchange("export", catalog)
        {
            Documents = documents,
            Units = new TranslationsToolInterchangeCounts { Total = project.compilation.Project!.CanonicalMessages.Count, Demoted = demoted, Skipped = structured },
            Entries = notices.Where(static loss => loss.Code == "REVIEW-STALE").Select(static loss => new TranslationsToolInterchangeEntry(loss.Location.TrimStart('/'), "demoted", loss.Code, loss.Message) { State = "needs-review" }).Take(MaximumEntries).ToArray(),
            Notices = Notices(notices),
            ReviewFile = review is null ? null : Display(path),
        };
        return 0;
    }

    // A path below the working directory is shown relative to it, any other one in full.
    internal static string Display(string fullPath)
    {
        string relative = InputFiles.DisplayPath(fullPath, Path.GetFullPath(Environment.CurrentDirectory));
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) ? fullPath.Replace('\\', '/') : relative;
    }

    internal static int Import(string projectPath, string source, string? policyName, bool writeReview, bool dryRun, ToolOperationResult result, string? failOn = null)
    {
        TranslationSourceChangePolicy policy = policyName switch
        {
            null or "needs-review" => TranslationSourceChangePolicy.NeedsReview,
            "refuse" => TranslationSourceChangePolicy.Refuse,
            _ => throw new ToolUsageException("--source-changed expects needs-review or refuse."),
        };
        bool failOnRefused = failOn switch
        {
            null => false,
            "refused" => true,
            _ => throw new ToolUsageException("--fail-on expects refused."),
        };
        (CompilerInputs inputs, Rmf2ProjectCompilation compilation)? compiled = Compile(projectPath, result);
        if (compiled is not { } project) return 1;
        string catalog = project.compilation.CatalogId!;
        string currentDirectory = Path.GetFullPath(Environment.CurrentDirectory);
        string sourcePath = Path.GetFullPath(source, currentDirectory);
        if (!File.Exists(sourcePath)) throw new ToolUsageException($"the XLIFF document '{source}' does not exist.");
        if (new FileInfo(sourcePath).Length > 8 * 1024 * 1024) throw new ToolUsageException($"the XLIFF document '{source}' exceeds 8 MiB.");
        string reviewPath = ReviewFile.PathFor(project.inputs, catalog);

        TranslationXliffImportResult import;
        TranslationInterchangeReview? existingReview;
        byte[]? existingReviewBytes = File.Exists(reviewPath) ? File.ReadAllBytes(reviewPath) : null;
        try
        {
            import = TranslationInterchange.ImportXliff21(File.ReadAllBytes(sourcePath), project.compilation, new TranslationXliffImportOptions { SourceChangePolicy = policy });
            existingReview = existingReviewBytes is null ? null : ReviewFile.Parse(existingReviewBytes, catalog);
        }
        catch (TranslationInterchangeException exception) { return Refuse(result, exception, "The XLIFF document could not be imported; nothing was written."); }

        Rmf2ProjectV5 compiledProject = project.compilation.Project!;
        var existingTargets = compiledProject.Locales.Single(locale => locale.Tag == import.TargetLocale).DirectResources
            .ToDictionary(static resource => resource.Key, static resource => Encoding.UTF8.GetString(resource.Message.Syntax.Source.GetUtf8Bytes()), StringComparer.Ordinal);
        var messages = import.Messages.ToDictionary(static message => message.MessageId, static message => Encoding.UTF8.GetString(message.Bytes.Span).TrimEnd('\n'), StringComparer.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var entries = new List<TranslationsToolInterchangeEntry>();
        int added = 0, changed = 0, unchanged = 0, skipped = 0, refused = 0, sourceChanged = 0;
        foreach (TranslationXliffUnitResult unit in import.Units)
        {
            switch (unit.Outcome)
            {
                case TranslationXliffUnitOutcome.Skipped:
                    skipped++;
                    entries.Add(new(unit.Key, "skipped", unit.Code, unit.Message));
                    continue;
                case TranslationXliffUnitOutcome.Refused:
                    refused++;
                    entries.Add(new(unit.Key, "refused", unit.Code, unit.Message));
                    if (refused <= MaximumUnitWarnings)
                        result.AddDiagnostic(ToolCodes.InterchangeUnit, "xliff-unit", $"{unit.Key}: {unit.Code}: {unit.Message}", CommandDiagnosticSeverity.Warning);
                    continue;
            }
            if (unit.SourceChanged) sourceChanged++;
            string pattern = messages[unit.Key];
            string state = ReviewFile.TranslationReviewStatesText(unit.State ?? TranslationReviewState.Translated);
            if (!existingTargets.TryGetValue(unit.Key, out string? before)) { added++; values[unit.Key] = pattern; entries.Add(new(unit.Key, "added", unit.SourceChanged ? "XLIFF21-SOURCE-CHANGED" : null, null) { State = state }); }
            else if (TranslationInterchangePattern.Canonical(before) != TranslationInterchangePattern.Canonical(pattern)) { changed++; values[unit.Key] = pattern; entries.Add(new(unit.Key, "changed", unit.SourceChanged ? "XLIFF21-SOURCE-CHANGED" : null, null) { State = state }); }
            else
            {
                unchanged++;
                if (unit.SourceChanged) entries.Add(new(unit.Key, "unchanged", "XLIFF21-SOURCE-CHANGED", null) { State = state });
            }
        }

        IReadOnlyList<TranslationSourceEdit> edits = TranslationInterchangeSourceWriter.Plan(currentDirectory, project.inputs.Project, project.inputs.Messages, project.compilation, import.TargetLocale, values);
        if (edits.Count != 0)
        {
            Rmf2ProjectCompilation proposed = TranslationCompiler.CompileRmf2ProjectV5(project.inputs.Project, TranslationInterchangeSourceWriter.Apply(project.inputs.Messages, edits));
            if (!proposed.Success)
            {
                Program.ReportDiagnostics(proposed.Diagnostics.Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).ToArray(), result);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "The imported translations do not compile; nothing was written.");
                result.WriteOutputLine("No files were written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            }
            foreach (TranslationSourceEdit edit in edits) EnsureWithinSourceRoots(project.inputs, edit.Path);
        }

        // Review state: every applied unit whose translation or explicit state changed, with the fingerprint of the
        // source it was translated for. Unchanged text in the default "translated" state keeps its entry.
        TranslationInterchangeReview? review = null;
        bool reviewChanged = false;
        if (existingReview is not null || writeReview)
        {
            var merged = (existingReview?.Entries ?? []).ToDictionary(static entry => entry.Key + "\0" + entry.Locale, StringComparer.Ordinal);
            var explicitEntries = import.Review.Entries.ToDictionary(static entry => entry.Key, StringComparer.Ordinal);
            foreach (TranslationXliffUnitResult unit in import.Units.Where(static unit => unit.Outcome == TranslationXliffUnitOutcome.Applied))
            {
                bool explicitState = explicitEntries.TryGetValue(unit.Key, out TranslationInterchangeReviewEntry? stated);
                string identity = unit.Key + "\0" + import.TargetLocale;
                merged.TryGetValue(identity, out TranslationInterchangeReviewEntry? current);
                if (!explicitState && !values.ContainsKey(unit.Key) && current is not null) continue;
                // An approval is made for the source the file showed; unchanged text otherwise keeps the source it was made for.
                TranslationReviewState state = unit.State ?? TranslationReviewState.Translated;
                string? fingerprint = state != TranslationReviewState.Approved && !values.ContainsKey(unit.Key) && current?.SourceFingerprint is { } kept
                    ? kept : unit.SourceFingerprint;
                var entry = new TranslationInterchangeReviewEntry(unit.Key, import.TargetLocale, state, stated?.Note ?? current?.Note, fingerprint);
                if (current is null || current.State != entry.State || current.Note != entry.Note || current.SourceFingerprint != entry.SourceFingerprint) reviewChanged = true;
                merged[identity] = entry;
            }
            review = new TranslationInterchangeReview(catalog, merged.Values);
        }

        // --fail-on refused: a refused unit fails the command before anything is written.
        bool failed = failOnRefused && refused != 0;
        bool writeReviewFile = review is not null && (reviewChanged || existingReviewBytes is null);
        if (!dryRun && !failed)
        {
            var writes = edits.Select(edit => new SourceWrite(Path.GetFullPath(edit.Path, currentDirectory), edit.Path, edit.OriginalBytes, edit.Bytes)).ToList();
            if (writeReviewFile) writes.Add(new SourceWrite(reviewPath, Display(reviewPath), existingReviewBytes, TranslationInterchange.ExportReviewJson(review!)));
            try { WriteSources(writes); }
            catch (SourceChangedException exception)
            {
                result.AddDiagnostic(ToolCodes.InterchangeDocument, "xliff", exception.Message, CommandDiagnosticSeverity.Error);
                result.Fault = new CommandFault(ToolCodes.InterchangeRejected, "A file changed on disk during the import; nothing was written. Run the import again.");
                result.WriteOutputLine("A file changed on disk during the import; nothing was written.");
                result.ExitCategory = CommandExitCategory.Validation;
                return 1;
            }
        }

        string verb = dryRun ? "would write" : "wrote";
        result.WriteOutputLine($"{(dryRun ? "dry run: " : string.Empty)}imported {import.TargetLocale}: {added} added, {changed} changed, {unchanged} unchanged, {skipped} skipped, {refused} refused.");
        if (!failed)
            foreach (TranslationSourceEdit edit in edits) result.WriteOutputLine($"{verb} {edit.Path}.");
        if (sourceChanged != 0) result.WriteOutputLine($"{sourceChanged} translation(s) of changed sources were imported as needs-review.");
        foreach (TranslationsToolInterchangeEntry entry in entries.Where(static entry => entry.Outcome == "refused").Take(MaximumUnitWarnings))
            result.WriteOutputLine($"refused {entry.Key}: {entry.Code}: {entry.Message}");
        if (writeReviewFile && !failed)
            result.WriteOutputLine($"{verb} {Display(reviewPath)}.");
        result.Interchange = new TranslationsToolInterchange("import", catalog)
        {
            Locale = import.TargetLocale,
            DryRun = dryRun,
            Units = new TranslationsToolInterchangeCounts
            {
                Total = import.Units.Count, Applied = added + changed + unchanged, Added = added, Changed = changed, Unchanged = unchanged,
                Skipped = skipped, Refused = refused, SourceChanged = sourceChanged,
            },
            Entries = entries.Take(MaximumEntries).ToArray(),
            EntriesTruncated = entries.Count > MaximumEntries,
            Notices = Notices(import.Report.Losses),
            Files = failed ? [] : edits.Select(static edit => new TranslationsToolInterchangeFile(edit.Path, edit.OriginalBytes is null ? "added" : "changed")).ToArray(),
            ReviewFile = review is null ? null : Display(reviewPath),
        };
        if (failed)
        {
            result.Fault = new CommandFault(ToolCodes.InterchangeRejected, $"{refused} unit(s) were refused and --fail-on refused is set; nothing was written.");
            result.WriteOutputLine($"{refused} unit(s) were refused and --fail-on refused is set; nothing was written.");
            result.ExitCategory = CommandExitCategory.Validation;
            return 1;
        }
        return 0;
    }

    internal static int Mark(string projectPath, IReadOnlyList<string> keys, string locale, string stateName, string? note, ToolOperationResult result)
    {
        if (!ReviewFile.TryParseState(stateName, out TranslationReviewState state))
            throw new ToolUsageException("--state expects draft, translated, needs-review or approved.");
        (CompilerInputs inputs, Rmf2ProjectCompilation compilation)? compiled = Compile(projectPath, result);
        if (compiled is not { } project) return 1;
        Rmf2ProjectV5 compiledProject = project.compilation.Project!;
        string catalog = compiledProject.Id;
        if (locale == compiledProject.DefaultLocale || !compiledProject.Locales.Any(item => item.Tag == locale))
            throw new ToolUsageException($"--locale {locale} is not a translated locale of catalog '{catalog}'.");
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(project.compilation);
        foreach (string key in keys)
            if (!fingerprints.ContainsKey(key)) throw new ToolUsageException($"--key {key} is not a message of catalog '{catalog}'.");
        string path = ReviewFile.PathFor(project.inputs, catalog);
        TranslationInterchangeReview? existing;
        try { existing = ReviewFile.Read(path, catalog); }
        catch (TranslationInterchangeException exception) { return Refuse(result, exception, "The review file cannot be read; nothing was written."); }
        var merged = (existing?.Entries ?? []).ToDictionary(static entry => entry.Key + "\0" + entry.Locale, StringComparer.Ordinal);
        foreach (string key in keys.Distinct(StringComparer.Ordinal))
        {
            merged.TryGetValue(key + "\0" + locale, out TranslationInterchangeReviewEntry? current);
            merged[key + "\0" + locale] = new TranslationInterchangeReviewEntry(key, locale, state, note ?? current?.Note, fingerprints[key]);
        }
        ReviewFile.Write(path, new TranslationInterchangeReview(catalog, merged.Values));
        string display = Display(path);
        result.WriteOutputLine($"marked {keys.Distinct(StringComparer.Ordinal).Count()} translation(s) in {locale} as {stateName}; wrote {display}.");
        result.Interchange = new TranslationsToolInterchange("review-mark", catalog)
        {
            Locale = locale,
            Entries = keys.Distinct(StringComparer.Ordinal).Select(key => new TranslationsToolInterchangeEntry(key, "marked", null, null) { State = stateName }).ToArray(),
            ReviewFile = display,
        };
        return 0;
    }

    internal static void WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static (CompilerInputs, Rmf2ProjectCompilation)? Compile(string projectPath, ToolOperationResult result)
    {
        CompilerInputs inputs = InputFiles.ReadProject(projectPath);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(inputs.Project, inputs.Messages);
        compilation = TranslationManifestReader.WithManifestErrors(compilation, inputs.Project.Path, inputs.ManifestError);
        Program.ReportDiagnostics(compilation.Diagnostics, result);
        return compilation.Success && compilation.Project is not null ? (inputs, compilation) : null;
    }

    private static int Refuse(ToolOperationResult result, TranslationInterchangeException exception, string summary)
    {
        result.AddDiagnostic(ToolCodes.InterchangeDocument, "xliff", $"{exception.Code}: {exception.Message}", CommandDiagnosticSeverity.Error);
        result.Fault = new CommandFault(ToolCodes.InterchangeRejected, summary);
        result.WriteOutputLine(summary);
        result.ExitCategory = CommandExitCategory.Validation;
        return 1;
    }

    private static TranslationsToolInterchangeNotice[] Notices(IEnumerable<TranslationInterchangeLoss> losses) =>
        losses.Take(MaximumEntries).Select(static loss => new TranslationsToolInterchangeNotice(loss.Code, loss.Location, loss.Message)).ToArray();

    private static void EnsureWithinSourceRoots(CompilerInputs inputs, string path)
    {
        string full = Path.GetFullPath(path, Environment.CurrentDirectory);
        foreach (string root in inputs.SourceRoots ?? [])
        {
            string relative = Path.GetRelativePath(root, full);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                !relative.StartsWith("../", StringComparison.Ordinal)) return;
        }
        throw new ToolOutputException($"RTR0020: the imported translation path '{path}' is outside every source root.");
    }

    /// <summary>One file an import replaces: its full and displayed path, the bytes it was read with (none for a new file) and the new bytes.</summary>
    internal sealed record SourceWrite(string FullPath, string DisplayPath, byte[]? OriginalBytes, byte[] Bytes);

    /// <summary>A file an import replaces no longer holds the bytes the import was computed from.</summary>
    internal sealed class SourceChangedException(string path)
        : Exception($"'{path}' changed on disk after the import read it (an editor save or another import); nothing was written.");

    // Stages every file, checks that none changed since it was read, then replaces them; a failure restores the
    // files already replaced. The check closes the window between compiling and writing to the staging time.
    internal static void WriteSources(IReadOnlyList<SourceWrite> writes)
    {
        var staged = new List<(string Path, string Temporary, byte[]? Original)>();
        var committed = new List<(string Path, byte[]? Original)>();
        try
        {
            foreach (SourceWrite write in writes)
            {
                string full = write.FullPath;
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                if (File.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new ToolOutputException($"RTR0020: the translation source '{write.DisplayPath}' is a symbolic link or reparse point.");
                string temporary = Path.Combine(Path.GetDirectoryName(full)!, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
                File.WriteAllBytes(temporary, write.Bytes);
                staged.Add((full, temporary, write.OriginalBytes));
            }
            foreach (SourceWrite write in writes)
            {
                byte[]? current = File.Exists(write.FullPath) ? File.ReadAllBytes(write.FullPath) : null;
                bool unchanged = current is null ? write.OriginalBytes is null : write.OriginalBytes is not null && current.AsSpan().SequenceEqual(write.OriginalBytes);
                if (!unchanged) throw new SourceChangedException(write.DisplayPath);
            }
            foreach ((string path, string temporary, byte[]? original) in staged)
            {
                File.Move(temporary, path, overwrite: true);
                committed.Add((path, original));
            }
        }
        catch (Exception) when (committed.Count != 0)
        {
            for (int index = committed.Count - 1; index >= 0; index--)
            {
                try
                {
                    if (committed[index].Original is null) File.Delete(committed[index].Path);
                    else File.WriteAllBytes(committed[index].Path, committed[index].Original!);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            foreach ((_, string temporary, _) in staged)
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
