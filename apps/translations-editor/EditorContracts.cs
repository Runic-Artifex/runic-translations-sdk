using System.Text.Json.Serialization;
using Runic.Translations.Authoring;

namespace Runic.Translations.Editor;

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
    EditorPendingTransaction? PendingTransaction,
    EditorReviewSnapshot? Review,
    EditorHistoryState? History);

internal sealed record EditorPendingTransaction(string CatalogId, IReadOnlyList<string> Paths);

internal sealed record EditorReviewEntry(
    string Key,
    string Locale,
    string State,
    string? Note,
    string? SourceFingerprint,
    IReadOnlyDictionary<string, string> Samples);

internal sealed record EditorTerminologyEntry(string Source, string Preferred, string? Locale, string? Note);

internal sealed record EditorReviewSnapshot(
    string Path,
    string? Revision,
    string? Error,
    IReadOnlyList<EditorReviewEntry> Entries,
    IReadOnlyList<EditorTerminologyEntry> Terminology);

internal sealed record EditorReviewSaveRequest(
    string? ExpectedRevision,
    IReadOnlyList<EditorReviewEntry> Entries,
    IReadOnlyList<EditorTerminologyEntry> Terminology);

internal sealed record EditorReviewOperationResult(bool Ok, string? Message, EditorReviewSnapshot? Review, EditorHistoryState? History);

internal sealed record EditorHistoryState(bool CanUndo, bool CanRedo, string? UndoLabel, string? RedoLabel);

internal sealed record EditorAbout(
    string Product,
    string Version,
    string UpdateChannel,
    string? Commit,
    string Runtime,
    string RuntimeIdentifier,
    string OperatingSystem,
    string Architecture);

internal sealed record EditorDiagnosticBundleResult(bool Ok, string? Path, string? Message);

internal sealed record EditorDiagnosticBundleActionResult(bool Ok, string? Message);

// Application-owned, per-user state stays outside the browser profile so a
// packaged desktop window and the loopback server do not get different
// preferences or recovery drafts merely because their origins differ.
internal sealed record EditorLocalStateEntry(string Key, string Value);

internal sealed record EditorLocalStateSnapshot(
    IReadOnlyList<EditorLocalStateEntry> Entries,
    bool Recovered);

internal sealed record EditorLocalStateClearResult(int RemovedEntries, bool Recovered);

internal sealed record EditorDiagnosticGroup(string Id, string Severity, int Count);

internal sealed record EditorDiagnosticWorkspace(
    string? CatalogId,
    int? SchemaVersion,
    int LocaleCount,
    int DocumentCount,
    int MessageCount,
    bool CompilerSuccess,
    bool ReviewStateAvailable,
    bool PendingTransaction,
    int PendingTransactionPathCount,
    IReadOnlyList<EditorDiagnosticGroup> Diagnostics);

internal sealed record EditorDiagnosticBundle(
    string Schema,
    DateTimeOffset GeneratedAt,
    EditorAbout Application,
    EditorDiagnosticWorkspace Workspace);

internal sealed record ValidationResult(
    bool Success,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorMessagePreview(
    bool Success,
    string? Locale,
    string? AstJson,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorOperationResult(
    bool Ok,
    string Kind,
    string? Message,
    WorkspaceSnapshot? Snapshot,
    ValidationResult? Validation,
    EditorHistoryState? History = null);

internal sealed record EditorProjectLocaleRequest(string Tag, string? Fallback);

internal sealed record EditorProjectCreationRequest(
    string Directory,
    string CatalogId,
    string DefaultLocale,
    IReadOnlyList<EditorProjectLocaleRequest> AdditionalLocales,
    string CodeNamespace,
    string ClassName,
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
    string? CopyFromLocale,
    string? SourceKey,
    string? TargetKey,
    string? InitialValue,
    string? ConfirmationToken = null);

internal sealed record EditorMutationFile(string Path, string Kind, long BeforeBytes, long AfterBytes);

internal sealed record EditorMutationPreview(
    bool Ok,
    string? Message,
    IReadOnlyList<EditorMutationFile> Files,
    bool RequiresIrreversibleConfirmation = false,
    string? ConfirmationToken = null);

internal sealed record EditorRecoveryRequest(string Mode);

internal sealed record EditorInterchangeLoss(string Code, string Location, string Message, bool SemanticLoss);

internal sealed record EditorInterchangeRefusal(string Code, string Message);

internal sealed record EditorInterchangeFile(string Path, string Locale, long ByteCount);

internal sealed record EditorXliffExportResult(
    bool Ok,
    string? Message,
    string? CatalogId,
    IReadOnlyList<EditorInterchangeFile> Documents,
    IReadOnlyList<EditorInterchangeLoss> Losses);

internal sealed record EditorReviewFileResult(bool Ok, string? Message, string? Path, int EntryCount);

// One reviewable diff row. 'added'/'changed' describe target text the import
// writes; 'removed' rows are keys present in the workspace locale but absent
// from the imported document (they stay untouched); 'state-change' rows carry
// only review-state transitions.
internal sealed record EditorKeyChange(
    string Key,
    string Kind,
    string? Before,
    string? After,
    string? StateBefore,
    string? StateAfter);

internal sealed record EditorXliffImportPlan(
    bool Ok,
    string? Message,
    string? ConfirmationToken,
    string? CatalogId,
    string? SourceLocale,
    string? TargetLocale,
    string? Layer,
    IReadOnlyList<EditorKeyChange> Changes,
    int AddedCount,
    int ChangedCount,
    int RemovedCount,
    int UnchangedCount,
    int ReviewUpdateCount,
    bool ChangesOverflowed,
    IReadOnlyList<EditorInterchangeRefusal> Refusals);

internal sealed record EditorReviewChange(string Key, string Locale, string Kind, string? StateBefore, string? StateAfter);

internal sealed record EditorReviewImportPlan(
    bool Ok,
    string? Message,
    string? ConfirmationToken,
    string? CatalogId,
    IReadOnlyList<EditorReviewChange> Changes,
    int AddedCount,
    int ChangedCount,
    int RemovedCount,
    bool ChangesOverflowed,
    IReadOnlyList<EditorInterchangeRefusal> Refusals);

// Session-held commit payload for a previewed interchange import. It never
// crosses the bridge; the confirmation token stays with the session.
internal sealed record PreparedInterchangeImport(
    string SourcePath,
    byte[] SourceHash,
    string CatalogId,
    string ExpectedCatalogFingerprint,
    IReadOnlyList<PreparedInterchangeDocument> Documents,
    IReadOnlyList<TranslationEditorStateEntry> MergedEntries,
    string? ExpectedSidecarRevision);

internal sealed record PreparedInterchangeDocument(
    string Path,
    string? ExpectedRevision,
    byte[]? OriginalBytes,
    byte[] Bytes);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(EditorMessagePreview))]
[JsonSerializable(typeof(EditorReviewSnapshot))]
[JsonSerializable(typeof(EditorReviewSaveRequest))]
[JsonSerializable(typeof(EditorReviewOperationResult))]
[JsonSerializable(typeof(EditorHistoryState))]
[JsonSerializable(typeof(EditorAbout))]
[JsonSerializable(typeof(EditorDiagnosticBundleResult))]
[JsonSerializable(typeof(EditorDiagnosticBundle))]
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
[JsonSerializable(typeof(EditorXliffExportResult))]
[JsonSerializable(typeof(EditorInterchangeFile))]
[JsonSerializable(typeof(EditorInterchangeLoss))]
[JsonSerializable(typeof(EditorReviewFileResult))]
[JsonSerializable(typeof(EditorXliffImportPlan))]
[JsonSerializable(typeof(EditorKeyChange))]
[JsonSerializable(typeof(EditorInterchangeRefusal))]
[JsonSerializable(typeof(EditorReviewImportPlan))]
[JsonSerializable(typeof(EditorReviewChange))]
internal sealed partial class EditorJsonContext : JsonSerializerContext;
