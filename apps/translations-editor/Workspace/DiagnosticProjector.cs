using Runic.Translations.Compiler;
using Runic.Translations.Tooling;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>The compiler's and the review file's diagnostics of a state as the bridge contract's diagnostics.</summary>
internal sealed class DiagnosticProjector
{
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly string _root;

    internal DiagnosticProjector(WorkspaceFiles files, WorkspaceStateReader reader)
    {
        _files = files;
        _reader = reader;
        _root = files.Root;
    }

    // The compiler's diagnostics and, with a review file, RTR0090 for stale review entries (or RTR0091 when the file
    // cannot be read), each attached to its message row where the check names one: the per-message checks carry their
    // key and locale (and the row its logical path); any other diagnostic at a message name belongs to that message. Quick fixes are the compiler's
    // document repairs (RTR0050, RTR0078) and project repairs (RTR0095: add the missing plural variants).
    internal EditorDiagnostic[] Diagnostics(WorkspaceState state, CancellationToken cancellationToken = default)
    {
        var compilation = state.Compilation;
        List<TranslationDiagnostic> diagnostics = [.. compilation.Diagnostics];
        // RTR0090 and the message rows come from the review compilation, so they stay while translations are incomplete.
        Rmf2ProjectCompilation reviewCompilation = ReviewCompilation(state, cancellationToken);
        if (reviewCompilation.Success && _reader.CatalogId is not null && _files.FindMf2ProjectConfig() is { } projectPath)
        {
            EditorReviewStore store = new(_root, projectPath, _reader.CatalogId);
            try
            {
                if (store.ReadReview().Review is { } review) diagnostics.AddRange(TranslationReviewFile.StaleDiagnostics(reviewCompilation, review));
            }
            catch (TranslationInterchangeException exception) { diagnostics.Add(TranslationReviewFile.InvalidDiagnostic(store.DisplayPath, exception)); }
            catch (Exception exception) when (exception is TranslationEditorStateException or IOException or UnauthorizedAccessException) { /* The review snapshot reports it. */ }
        }
        var rows = new Dictionary<(string Path, int Start), (string Key, string Locale)>();
        Rmf2ProjectV5? rowProject = compilation.Project ?? reviewCompilation.Project;
        IReadOnlyDictionary<string, IReadOnlyList<string>> messagePaths = rowProject is null ? new Dictionary<string, IReadOnlyList<string>>() : Rmf2MessageKeys.Paths(rowProject);
        foreach (Rmf2LocaleV5 locale in rowProject?.Locales ?? [])
            foreach (Rmf2TranslationV5 resource in locale.DirectResources)
                rows.TryAdd((resource.SourceLocation.Path, resource.SourceLocation.StartByte), (resource.Key, locale.Tag));
        var sources = state.Files.ToDictionary(file => file.Path, file => Source(file.Path, file.Content), StringComparer.Ordinal);
        var sourceFixes = new Dictionary<string, IReadOnlyList<Rmf2DiagnosticQuickFix>>(StringComparer.Ordinal);
        var sourceRevisions = new Dictionary<string, string>(StringComparer.Ordinal);
        var result = new EditorDiagnostic[diagnostics.Count];
        for (int index = 0; index < result.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TranslationDiagnostic diagnostic = diagnostics[index];
            TextSourceLocation location = diagnostic.Location;
            sources.TryGetValue(location.Path, out TranslationSource? source);
            Rmf2DiagnosticSpan? span = null;
            IReadOnlyList<Rmf2DiagnosticQuickFix> fixes = [];
            if (source is not null)
            {
                try
                {
                    span = Rmf2DiagnosticActions.GetSpan(source, location);
                    if (!sourceRevisions.ContainsKey(source.Path)) sourceRevisions[source.Path] = Rmf2DiagnosticActions.Revision(source);
                    bool documentFix = diagnostic.Id == "RTR0050" && source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
                    if (documentFix || diagnostic.Id == "RTR0095")
                    {
                        if (!sourceFixes.TryGetValue(source.Path, out var available))
                        {
                            IEnumerable<Rmf2DiagnosticQuickFix> all = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)
                                ? Rmf2DiagnosticActions.GetQuickFixes(Rmf2ResourceReader.Analyze(source, cancellationToken: cancellationToken), cancellationToken) : [];
                            sourceFixes[source.Path] = available = all.Concat(Rmf2DiagnosticActions.GetProjectQuickFixes(compilation, source, cancellationToken)).ToArray();
                        }
                        fixes = available.Where(fix => fix.DiagnosticId == diagnostic.Id && fix.Location.Path == location.Path && fix.Location.StartByte == location.StartByte && fix.Location.LengthBytes == location.LengthBytes).ToArray();
                    }
                }
                catch (ArgumentException) { /* Project-level diagnostics may not address a complete physical slice. */ }
            }
            (string Key, string Locale)? row = diagnostic.MessageKey is { } key && diagnostic.Locale is { } tag ? (key, tag)
                : rows.TryGetValue((location.Path, location.StartByte), out var named) ? named : null;
            result[index] = new EditorDiagnostic(
                diagnostic.Id,
                diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning",
                diagnostic.Message,
                location.Path,
                location.Line,
                location.Column,
                location.EndLine,
                location.EndColumn, null, span, source is null ? null : sourceRevisions.GetValueOrDefault(source.Path), fixes,
                row?.Key, row?.Locale, row is { } about && messagePaths.TryGetValue(about.Key, out var messagePath) ? messagePath : null);
        }
        return result;
    }

    private static ValidationResult MalformedValidation(string path) => new(
        false,
        [new EditorDiagnostic("JSON", "error", string.Empty, path, 1, 1, 1, 1, EditorNotice.Create("ui_backend_invalid_json"))]);
}
