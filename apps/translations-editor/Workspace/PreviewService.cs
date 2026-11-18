using System.Text.Json;
using System.Text.Json.Nodes;
using Runic.Translations.Internal;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using static Runic.Translations.Editor.WorkspaceFiles;
using static Runic.Translations.Editor.WorkspaceStateReader;

namespace Runic.Translations.Editor;

/// <summary>The rendered preview of one message of a draft, also in a generated pseudo-locale, and its primary diagnostic.</summary>
internal sealed class PreviewService
{
    private readonly WorkspaceGate _gate;
    private readonly WorkspaceFiles _files;
    private readonly WorkspaceStateReader _reader;
    private readonly DiagnosticProjector _diagnostics;

    internal PreviewService(WorkspaceGate gate, WorkspaceFiles files, WorkspaceStateReader reader, DiagnosticProjector diagnostics)
    {
        _gate = gate;
        _files = files;
        _reader = reader;
        _diagnostics = diagnostics;
    }

    public async Task<EditorMessagePreview> PreviewMessageAsync(
        string relativePath,
        string content,
        string locale,
        string key,
        string? samplesJson = null,
        string? requestedLocale = null,
        string? timeZone = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _gate.ThrowIfDisposed();
            string path = _files.NormalizeKnownPath(relativePath);
            WorkspaceState state = await _reader.ReadStateAsync(path, content, cancellationToken).ConfigureAwait(false);
            EditorDiagnostic[] diagnostics = _diagnostics.Diagnostics(state, cancellationToken);
            EditorMessagePreview Result(EditorMessagePreview preview) =>
                preview with { PrimaryDiagnostic = PrimaryDiagnostic(preview.Diagnostics, path, content, key, locale) };
            if (!state.Compilation.Success)
                return Result(new EditorMessagePreview(false, null, null, null, diagnostics));

            Rmf2ProjectV5 rmf2 = state.Compilation.Project!;
            // en-XA and ar-XB are generated from the base locale by the SDK's pseudo-locale generator, exactly as a
            // --pseudo-locales build does, also when runic.json does not configure them (the simulation toggle).
            if (!rmf2.Locales.Any(item => item.Tag == locale) && Rmf2PseudoLocalesV5.Supported.Contains(locale, StringComparer.Ordinal))
            {
                Rmf2ProjectCompilation pseudo = _reader.PseudoLocaleCompilation(state, Rmf2PseudoLocalesV5.Supported.First(tag => tag == locale), cancellationToken);
                if (!pseudo.Success || pseudo.Project is null)
                    return Result(new EditorMessagePreview(false, null, null, null, [.. diagnostics, .. pseudo.Diagnostics
                        .Where(static item => item.Severity == TranslationDiagnosticSeverity.Error)
                        .Select(static item => new EditorDiagnostic(item.Id, "error", item.Message, item.Location.Path, item.Location.Line, item.Location.Column, item.Location.EndLine, item.Location.EndColumn))]));
                rmf2 = pseudo.Project;
            }
            Rmf2LocaleV5? selectedLocale = rmf2.Locales.SingleOrDefault(item => item.Tag == locale);
            Rmf2TranslationV5? resource = selectedLocale?.ResolvedResources.SingleOrDefault(item => item.Key == key);
            if (resource is null)
                return Result(MissingPreviewKey(path, locale, key));
            string ast = Rmf2MessageJsonV5.Serialize(resource.Message);
            JsonObject samples = samplesJson is null ? new JsonObject() : JsonNode.Parse(samplesJson)?.AsObject()
                ?? throw new JsonException("Preview samples must be a JSON object.");
            string? rendered = resource.Message.Inputs.All(input => samples.ContainsKey(input.Name))
                ? Rmf2Preview.Render(rmf2, key, locale, samples, requestedLocale, timeZone).ToJsonString()
                : null;
            return Result(new EditorMessagePreview(true, locale, ast, rendered, diagnostics));
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or TranslationAuthoringException or TranslationFormatException)
        {
            var failure = new EditorDiagnostic("PREVIEW", "error", string.Empty, relativePath, 1, 1, 1, 1, EditorNotice.FromException(exception));
            return new EditorMessagePreview(false, null, null, null, [failure], failure);
        }
        finally
        {
            _gate.Release();
        }
    }

    // The diagnostic the preview shows for the previewed message (EditorMessagePreview.PrimaryDiagnostic): of those that
    // belong to it, located in its source in the previewed document or naming its key and locale, the first error
    // (located ones first), else the preview's own failure, else the first warning. A syntax error in the message is
    // therefore shown instead of an unrelated catalog diagnostic that comes first in the list.
    private EditorDiagnostic? PrimaryDiagnostic(IReadOnlyList<EditorDiagnostic> diagnostics, string path, string content, string key, string locale)
    {
        (int Start, int End)? extent = null;
        if (path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)) extent = (0, StrictUtf8.GetByteCount(content));
        else if (path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase))
        {
            Rmf2Workspace workspace = _files.Rmf2Catalog();
            if (Rmf2ResourceReader.Read(Source(path, content)).Nodes.FirstOrDefault(node => !node.IsGroup && Rmf2MessageKeys.Canonical(workspace.LogicalPath(path, node)) == key) is { } node)
                extent = (node.NameLocation.StartByte, Math.Max(node.Location.StartByte + node.Location.LengthBytes, node.MessageByteMap.Count == 0 ? 0 : node.MessageByteMap[^1]));
        }
        bool Located(EditorDiagnostic diagnostic) => extent is { } range && diagnostic.Path == path && diagnostic.Span is { } span &&
            span.StartByte >= range.Start && span.StartByte <= range.End;
        bool Named(EditorDiagnostic diagnostic) => diagnostic.Key == key && diagnostic.Locale == locale;
        return diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == "error" && Located(diagnostic))
            ?? diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == "error" && Named(diagnostic))
            ?? diagnostics.FirstOrDefault(static diagnostic => diagnostic.Id == "PREVIEW")
            ?? diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity != "error" && (Located(diagnostic) || Named(diagnostic)));
    }

    private static EditorMessagePreview MissingPreviewKey(string path, string locale, string key) =>
        new(false, locale, null, null,
            [new EditorDiagnostic("PREVIEW", "error", string.Empty, path, 1, 1, 1, 1,
                EditorNotice.Create("ui_backend_preview_key_missing", ("key", key)))]);
}
