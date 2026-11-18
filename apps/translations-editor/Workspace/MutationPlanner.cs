using System.Text;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>
/// Plans structural changes as workspace transactions the session commits: creating, renaming, duplicating and deleting
/// RMF2 resources, adding and removing locales and changing fallbacks, and importing .resx or i18next catalogs.
/// </summary>
internal sealed class MutationPlanner
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly ReviewService _review;
    private readonly string _root;

    internal MutationPlanner(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, ReviewService review)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _review = review;
        _root = files.Root;
    }

    internal TranslationWorkspaceTransactionPlan? PlanRmf2Mutation(EditorMutationRequest request)
    {
        string? config = _files.FindMf2ProjectConfig();
        if (config is null) return null;
        TranslationManifestLayout layout = TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!);
        if (!layout.IsValid) throw new TranslationAuthoringException(layout.Error!);
        var state = _reader.ReadStateAsync(null, null, CancellationToken.None).GetAwaiter().GetResult();
        // A source root that is missing or outside the workspace would plan
        // against a partial inventory; refuse with the load's manifest diagnostic.
        string configRelative = NormalizeRelativePath(Path.GetRelativePath(_root, config));
        if (state.Compilation.Diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.Id == TranslationManifestReader.DiagnosticId && diagnostic.Severity == TranslationDiagnosticSeverity.Error &&
                string.Equals(diagnostic.Location.Path, configRelative, StringComparison.Ordinal)) is { } discovery)
            throw new TranslationAuthoringException(discovery.Message);
        var sources = state.Files.Where(file => file.Kind == DocumentKind.Resource && IsSourcePath(file.Path)).Select(file => Source(file.Path, file.Content)).ToArray();
        if (sources.Length == 0) return null;
        if (request.Kind is "add-locale" or "remove-locale" or "set-fallback")
            return PlanLocaleChange(request, config, layout, sources);
        var workspace = new Rmf2Workspace(_root, new TranslationSource(Path.GetRelativePath(_root, config).Replace('\\', '/'), File.ReadAllBytes(config)), sources);
        string[] Target() => (request.TargetKey ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (request.Kind == "create-key")
        {
            string locale = layout.BaseLocale ?? throw new TranslationAuthoringException(TranslationManifestReader.MissingBaseLocale);
            string path = sources.Where(source => string.Equals(workspace.Locale(source.Path), locale, StringComparison.OrdinalIgnoreCase)).Select(source => source.Path).Order(StringComparer.Ordinal).FirstOrDefault(path => {
                try { workspace.LocalPath(path, Target()); return true; }
                catch (TranslationAuthoringException) { return false; }
            })
                ?? throw new TranslationAuthoringException("No base-locale source can contain this logical path. Choose a path within an existing source namespace.");
            return workspace.CreateResource(path, Target(), request.InitialValue ?? "");
        }
        var origin = workspace.Documents.SelectMany(file => file.Nodes.Where(node => !node.IsGroup).Select(node => (file, node)))
            .FirstOrDefault(item => string.Join('_', workspace.LogicalPath(item.file.Source.Path, item.node)) == request.SourceKey);
        if (origin.node is null) throw new TranslationAuthoringException("Select an existing RMF2 resource.");
        var logical = workspace.LogicalPath(origin.file.Source.Path, origin.node);
        return request.Kind switch {
            "rename-key" => workspace.MutateResource(logical, Target()),
            "duplicate-key" => workspace.MutateResource(logical, Target(), duplicate: true),
            "delete-key" => workspace.MutateResource(logical, null),
            _ => throw new TranslationAuthoringException("This operation is not available for RMF2 resources."),
        };
    }

    // Locale changes go through Tooling's planner, the code of runic-translations locale add|remove: runic.json keeps its
    // formatting and only the changed entry's bytes change. The editor seeds a new locale from a declared one (copyFrom)
    // and gives the copied messages the plural and ordinal variants the new locale needs (RTR0095's repair: copies of
    // the * variant); removing a locale redirects the locales that fall back to it (the base locale by default).
    private TranslationWorkspaceTransactionPlan PlanLocaleChange(EditorMutationRequest request, string config, TranslationManifestLayout layout, TranslationSource[] sources)
    {
        string baseLocale = layout.BaseLocale ?? throw new TranslationAuthoringException(TranslationManifestReader.MissingBaseLocale);
        var project = new TranslationSource(NormalizeRelativePath(Path.GetRelativePath(_root, config)), File.ReadAllBytes(config));
        var inputs = new TranslationProjectSources(_root, project, sources, layout.SourceRoots);
        TranslationLocalePlan plan;
        try
        {
            plan = request.Kind switch
            {
                "add-locale" => TranslationLocaleChanges.PlanAdd(inputs, request.Locale ?? "", request.Fallback, request.CopyFromLocale ?? baseLocale),
                "remove-locale" => TranslationLocaleChanges.PlanRemove(inputs, request.Locale ?? "", request.ReplacementFallback ?? baseLocale),
                _ => TranslationLocaleChanges.PlanSetFallback(inputs, request.Locale ?? "", request.Fallback ?? baseLocale),
            };
        }
        catch (TranslationToolingUsageException exception) { throw new TranslationAuthoringException(exception.Message, exception); }
        // Removing a locale reads the review file to drop its entries; a corrupt one is refused with what to repair.
        catch (TranslationInterchangeException exception)
        {
            throw new EditorUserException(EditorNotice.Create("ui_backend_locale_review_invalid", ("locale", request.Locale?.Trim() ?? ""),
                ("path", _review.ReviewDisplayPath() ?? "review.json")) with { Detail = exception.Message });
        }

        var created = plan.CreatedFiles.Select(file => new TranslationSource(inputs.DisplayPath(file.Path), file.Bytes)).ToList();
        if (created.Count != 0) created = SeedPluralCategories(plan, inputs, sources, created);
        var deleted = plan.DeletedFiles.Select(inputs.DisplayPath).ToHashSet(StringComparer.Ordinal);
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource(project.Path, plan.ProjectBytes),
            sources.Where(source => !deleted.Contains(source.Path)).Concat(created).ToArray());
        if (!compilation.Success || compilation.Project is null)
            throw new TranslationAuthoringException("The complete edited catalog is invalid: " + string.Join("; ", compilation.Diagnostics
                .Where(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error).Select(static diagnostic => diagnostic.Message)));

        var edits = new List<TranslationWorkspaceEdit>
        {
            new(project.Path, TranslationWorkspaceEditKind.Replace, WorkspaceRevision(project.GetUtf8Bytes()), plan.ProjectBytes),
        };
        foreach (TranslationSource source in created) edits.Add(new(source.Path, TranslationWorkspaceEditKind.Create, null, source.GetUtf8Bytes()));
        foreach (TranslationSource source in sources.Where(source => deleted.Contains(source.Path)))
            edits.Add(new(source.Path, TranslationWorkspaceEditKind.Delete, WorkspaceRevision(source.GetUtf8Bytes()), null));
        return new TranslationWorkspaceTransactionPlan(_root, compilation.Project.Id, edits, compilation);
    }

    private static string WorkspaceRevision(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    // Each copied message whose plural or ordinal selector lacks a category the new locale needs gets it, as the RTR0095
    // quick fix adds it: a copy of the * variant. Repairs within one file never overlap, so they apply back to front.
    private static List<TranslationSource> SeedPluralCategories(TranslationLocalePlan plan, TranslationProjectSources inputs,
        TranslationSource[] sources, List<TranslationSource> created)
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(new TranslationSource(inputs.Project.Path, plan.ProjectBytes),
            sources.Concat(created).ToArray());
        var seeded = new List<TranslationSource>(created.Count);
        foreach (TranslationSource source in created)
        {
            byte[] bytes = source.GetUtf8Bytes();
            foreach (Rmf2DiagnosticQuickFix fix in Rmf2DiagnosticActions.GetProjectQuickFixes(compilation, source).OrderByDescending(static fix => fix.StartByte))
                bytes = [.. bytes.AsSpan(0, fix.StartByte), .. Encoding.UTF8.GetBytes(fix.NewText), .. bytes.AsSpan(fix.StartByte + fix.LengthBytes)];
            seeded.Add(new TranslationSource(source.Path, bytes));
        }
        return seeded;
    }

    private const int MaximumImportEntries = 500;

    /// <summary>
    /// Plans importing a .resx or i18next catalog with Tooling's importer (runic-translations import) against the saved
    /// files: the outcome of each message and the files it writes, as a workspace transaction that commits only when
    /// none of them changed since. A relative source is resolved against the workspace root.
    /// </summary>
    internal async Task<(EditorCatalogImportPlan Plan, TranslationWorkspaceTransactionPlan? Transaction)> PreviewCatalogImportAsync(
        EditorCatalogImportRequest request, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            EditorCatalogImportPlan Refused(EditorNotice message, IReadOnlyList<EditorDiagnostic>? diagnostics = null) =>
                new(false, message, null, request.From, request.Source, [], [], false, [], 0, 0, diagnostics ?? []);
            string? config = _files.FindMf2ProjectConfig();
            if (config is null || string.IsNullOrWhiteSpace(request.Source)) return (Refused(EditorNotice.Create("ui_backend_import_source_required")), null);
            TranslationManifestLayout layout = TranslationManifestReader.Read(File.ReadAllBytes(config), Path.GetDirectoryName(config)!);
            if (!layout.IsValid) return (Refused(EditorNotice.FromException(new TranslationAuthoringException(layout.Error!))), null);
            WorkspaceState state = await _reader.ReadStateAsync(null, null, cancellationToken).ConfigureAwait(false);
            var project = new TranslationSource(NormalizeRelativePath(Path.GetRelativePath(_root, config)), File.ReadAllBytes(config));
            TranslationSource[] sources = state.Files.Where(file => file.Kind == DocumentKind.Resource && IsSourcePath(file.Path)).Select(file => Source(file.Path, file.Content)).ToArray();
            var inputs = new TranslationProjectSources(_root, project, sources, layout.SourceRoots);
            string sourcePath = Path.GetFullPath(request.Source.Trim(), _root);
            TranslationCatalogImportPlan plan;
            try { plan = TranslationCatalogImport.Plan(inputs, new TranslationCatalogImportRequest(request.From, sourcePath, [], null, request.Overwrite)); }
            catch (TranslationToolingUsageException exception) { return (Refused(EditorNotice.FromException(new TranslationAuthoringException(exception.Message, exception))), null); }
            EditorDiagnostic[] diagnostics = plan.Diagnostics.Where(static item => item.Severity == TranslationDiagnosticSeverity.Error)
                .Select(static item => new EditorDiagnostic(item.Id, "error", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn)).ToArray();
            switch (plan.Failure)
            {
                case TranslationCatalogImportFailure.ProjectDoesNotCompile: return (Refused(EditorNotice.Create("ui_backend_import_project_errors"), diagnostics), null);
                case TranslationCatalogImportFailure.SourceUnreadable: return (Refused(EditorNotice.Create("ui_backend_import_source_unreadable", ("code", plan.FailureCode ?? ""), ("message", plan.FailureMessage ?? ""))), null);
                case TranslationCatalogImportFailure.MessagesDoNotCompile: return (Refused(EditorNotice.Create("ui_backend_import_messages_invalid"), diagnostics), null);
            }

            var entries = plan.Applied.Concat(plan.Entries).Select(static entry => new EditorCatalogImportEntry(entry.Locale, entry.Key, entry.Outcome, entry.Code, entry.Message)).ToArray();
            var result = new EditorCatalogImportPlan(true, null, null, request.From, request.Source,
                plan.Locales.Select(static locale => new EditorCatalogImportLocale(locale.Locale, locale.Added, locale.Changed, locale.Unchanged, locale.Kept)).ToArray(),
                entries.Take(MaximumImportEntries).ToArray(), entries.Length > MaximumImportEntries,
                plan.Writes.Select(write => NormalizeRelativePath(Path.GetRelativePath(_root, write.FullPath))).ToArray(), plan.Skipped, plan.Refused,
                // The catalog's own errors from before the import, which it neither causes nor fixes.
                plan.Preexisting.Select(static item => new EditorDiagnostic(item.Id, "warning", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn)).ToArray());
            if (plan.Writes.Count == 0) return (result, null);

            // The writes as one transaction: each replaced file must still have the bytes the import read.
            var edits = new List<TranslationWorkspaceEdit>();
            var after = sources.ToDictionary(static source => source.Path, StringComparer.Ordinal);
            foreach (TranslationImportSourceWrite write in plan.Writes)
            {
                string? within = PathContainment.RelativeWithin(_root, write.FullPath);
                if (within is null)
                    return (Refused(EditorNotice.Create("ui_backend_source_outside_project", ("path", write.DisplayPath))), null);
                string relative = NormalizeRelativePath(within);
                edits.Add(write.OriginalBytes is null
                    ? new TranslationWorkspaceEdit(relative, TranslationWorkspaceEditKind.Create, null, write.Bytes)
                    : new TranslationWorkspaceEdit(relative, TranslationWorkspaceEditKind.Replace, WorkspaceRevision(write.OriginalBytes), write.Bytes));
                after[relative] = new TranslationSource(relative, write.Bytes);
            }
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, after.Values.ToArray(), null, cancellationToken);
            Rmf2ProjectCompilation counted = TranslationStatus.ForStatus(compilation, project, after.Values.ToArray(), cancellationToken) ?? compilation;
            return (result, new TranslationWorkspaceTransactionPlan(_root, counted.Project?.Id ?? _reader.CatalogId ?? "", edits, counted));
        }
        finally
        {
            _gate.Release();
        }
    }
}
