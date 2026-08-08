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

internal sealed record EditorCatalogSummary(
    string Id,
    IReadOnlyList<string> ManifestPaths,
    int DocumentCount,
    int LocaleCount,
    int MessageCount,
    int ErrorCount,
    int WarningCount,
    bool Success);

internal sealed record EditorDocument(
    string Path,
    string Content,
    string Revision,
    bool IsManifest,
    bool IsMalformed,
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
    IReadOnlyList<EditorCatalogSummary> Catalogs,
    IReadOnlyList<EditorDocument> Documents,
    IReadOnlyList<EditorDiagnostic> Diagnostics,
    bool Success,
    EditorPendingTransaction? PendingTransaction);

internal sealed record EditorPendingTransaction(string CatalogId, IReadOnlyList<string> Paths);

internal sealed record ValidationResult(
    bool Success,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorOperationResult(
    bool Ok,
    string Kind,
    string? Message,
    WorkspaceSnapshot? Snapshot,
    ValidationResult? Validation);

internal sealed record EditorProjectLocaleRequest(string Tag, string? Fallback);

internal sealed record EditorProjectCreationRequest(
    string Directory,
    string CatalogId,
    string DefaultLocale,
    IReadOnlyList<EditorProjectLocaleRequest> AdditionalLocales,
    string CodeNamespace,
    string ClassName,
    string LayerName,
    bool GenerateEsm,
    bool IncludeStarterMessage);

internal sealed record EditorProjectPlan(
    bool Ok,
    string? Message,
    string Directory,
    string CatalogId,
    IReadOnlyList<EditorLocale> Locales,
    IReadOnlyList<string> Files);

internal sealed record EditorOpenWorkspaceRequest(string Directory, string? CatalogId);

internal sealed record EditorExternalFileChange(
    string Path,
    bool Exists,
    string? Content,
    string? Revision);

internal sealed record EditorExternalChanges(
    bool Overflowed,
    IReadOnlyList<string> Paths,
    IReadOnlyList<EditorExternalFileChange> Changes);

internal sealed record EditorWorkspacePickerResult(bool Ok, bool Cancelled, string? Directory, string? Message);

internal sealed record EditorMutationRequest(
    string Kind,
    string? Locale,
    string? Fallback,
    string? ReplacementFallback,
    string? Layer,
    string? CopyFromLocale,
    string? SourceKey,
    string? TargetKey,
    string? InitialValue);

internal sealed record EditorMutationFile(string Path, string Kind, long BeforeBytes, long AfterBytes);

internal sealed record EditorMutationPreview(
    bool Ok,
    string? Message,
    IReadOnlyList<EditorMutationFile> Files);

internal sealed record EditorRecoveryRequest(string Mode);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(EditorOperationResult))]
[JsonSerializable(typeof(EditorProjectCreationRequest))]
[JsonSerializable(typeof(EditorProjectPlan))]
[JsonSerializable(typeof(EditorOpenWorkspaceRequest))]
[JsonSerializable(typeof(EditorExternalChanges))]
[JsonSerializable(typeof(EditorExternalFileChange))]
[JsonSerializable(typeof(EditorWorkspacePickerResult))]
[JsonSerializable(typeof(EditorMutationRequest))]
[JsonSerializable(typeof(EditorMutationPreview))]
[JsonSerializable(typeof(EditorRecoveryRequest))]
internal sealed partial class EditorJsonContext : JsonSerializerContext;
