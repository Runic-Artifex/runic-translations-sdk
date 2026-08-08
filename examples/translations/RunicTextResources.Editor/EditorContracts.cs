using System.Text.Json.Serialization;

namespace RunicTextResources.Editor;

internal sealed record EditorCatalog(
    string Id,
    int SchemaVersion,
    string DefaultLocale,
    IReadOnlyList<EditorLocale> Locales,
    IReadOnlyList<EditorLayer> Layers);

internal sealed record EditorLocale(string Tag, string? Fallback);

internal sealed record EditorLayer(string Name, int Priority);

internal sealed record EditorDocument(
    string Path,
    string Content,
    string Revision,
    bool IsManifest,
    string? Locale,
    string? Layer);

internal sealed record EditorDiagnostic(
    string Id,
    string Severity,
    string Message,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn);

internal sealed record WorkspaceSnapshot(
    string Root,
    EditorCatalog? Catalog,
    IReadOnlyList<EditorDocument> Documents,
    IReadOnlyList<EditorDiagnostic> Diagnostics,
    bool Success);

internal sealed record ValidationResult(
    bool Success,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorOperationResult(
    bool Ok,
    string Kind,
    string? Message,
    WorkspaceSnapshot? Snapshot,
    ValidationResult? Validation);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(EditorOperationResult))]
internal sealed partial class EditorJsonContext : JsonSerializerContext;
