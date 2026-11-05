using System.Text.Json.Serialization;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;

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
    string? Layer,
    IReadOnlyList<EditorMessageEntry>? Entries = null);

// Logical messages share their containing document's physical path and revision.
internal sealed record EditorMessageEntry(string Key, string Content, int ValueStartByte, int ValueLengthBytes,
    EditorMessageProjection? Authoring = null, EditorMessageContext? Context = null, EditorMessageSemantic? Semantic = null);
internal sealed record EditorMessageInput(string Name, string Type, string? Function, bool Declared);
internal sealed record EditorMessageVariant(string Id, IReadOnlyList<string> Keys, string Pattern, int StartByte, int LengthBytes);
internal sealed record EditorMessageProjection(string Revision, bool Supported, string? Reason,
    IReadOnlyList<EditorMessageInput> Inputs, IReadOnlyList<string> Selectors, IReadOnlyList<string> SelectorFunctions, IReadOnlyList<EditorMessageVariant> Variants,
    IReadOnlyList<string> PluralCategories, IReadOnlyList<IReadOnlyList<string>> SelectorPluralCategories, string CldrVersion);
internal sealed record EditorMessageOperation(string Kind, string? VariantId = null, string? Pattern = null,
    string? Name = null, string? NewName = null, string? Function = null,
    IReadOnlyList<string>? Keys = null, IReadOnlyList<string>? Selectors = null);
internal sealed record EditorMessageContext(IReadOnlyList<string> Comments, IReadOnlyList<string> Tags,
    IReadOnlyList<IReadOnlyDictionary<string, string>> Examples);
internal sealed record EditorMessageSemantic(IReadOnlyList<string> Text, IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> Slots, bool Supported, bool HasBoundaryWhitespace);

internal sealed record EditorDocumentDraft(bool Success, string Content,
    IReadOnlyList<EditorMessageEntry> Entries, IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorDiagnostic(
    string Id,
    string Severity,
    string Message,
    string Path,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    EditorNotice? Notice = null,
    Rmf2DiagnosticSpan? Span = null, string? SourceRevision = null, IReadOnlyList<Rmf2DiagnosticQuickFix>? QuickFixes = null,
    // The message row a per-message check is about (RTR0010, RTR0090, RTR0094-RTR0098, or a diagnostic at a message name).
    string? Key = null, string? Locale = null);

internal sealed record WorkspaceSnapshot(
    string Root,
    EditorCatalog? Catalog,
    IReadOnlyList<EditorCatalogSummary> Catalogs,
    IReadOnlyList<EditorDocument> Documents,
    IReadOnlyList<EditorDiagnostic> Diagnostics,
    bool Success,
    EditorPendingTransaction? PendingTransaction,
    EditorReviewSnapshot? Review,
    EditorHistoryState? History)
{
    /// <summary>The library catalogs the open project can override (T5d).</summary>
    public IReadOnlyList<EditorLibrarySummary> Libraries { get; init; } = [];
}

internal sealed record EditorPendingTransaction(string CatalogId, IReadOnlyList<string> Paths);

internal sealed record EditorReviewEntry(
    string Key,
    string Locale,
    string State,
    string? Note,
    string? SourceFingerprint,
    IReadOnlyDictionary<string, string> Samples,
    // The SDK per-unit fingerprint (SourceFingerprint) no longer matches the current source (RTR0090). Ignored on save.
    // A state the user sets carries the fingerprint of the source the editor loaded (EditorReviewSnapshot.Fingerprints),
    // so an approval is recorded for the text the reviewer saw, never stamped with the source on disk at save time.
    bool Stale = false,
    // A row with a note or preview samples but no review-file entry: its state is inferred (translated or draft), and
    // saving it writes no review-file entry until the user sets a state.
    bool Implicit = false);

internal sealed record EditorTerminologyEntry(string Source, string Preferred, string? Locale, string? Note);

internal sealed record EditorReviewSnapshot(
    string Path,
    string? Revision,
    string? Error,
    IReadOnlyList<EditorReviewEntry> Entries,
    IReadOnlyList<EditorTerminologyEntry> Terminology,
    // A one-time notice, such as the move of version 1 editor approvals into the review file.
    EditorNotice? Notice = null,
    // The SDK per-unit fingerprint of each base message as loaded (null: runic.json or the base locale has errors, so
    // no review state can be recorded). Computed while translations are incomplete or invalid.
    IReadOnlyDictionary<string, string>? Fingerprints = null,
    // Version 1 editor approvals wait to move into the review file (the base locale has errors): Entries shows them
    // read-only, and review data cannot be saved until they have moved.
    bool MigrationPending = false);

internal sealed record EditorReviewSaveRequest(
    string? ExpectedRevision,
    IReadOnlyList<EditorReviewEntry> Entries,
    IReadOnlyList<EditorTerminologyEntry> Terminology,
    // The review entries and terminology the edits started from. When the review data changed on disk since (the
    // command line, the language server), the save re-applies only the entries and terminology the user changed onto
    // the current files and refuses only an entry changed on both sides. Without them a changed revision refuses.
    IReadOnlyList<EditorReviewEntry>? Original = null,
    IReadOnlyList<EditorTerminologyEntry>? OriginalTerminology = null);

internal sealed record EditorReviewOperationResult(bool Ok, EditorNotice? Message, EditorReviewSnapshot? Review, EditorHistoryState? History);

internal sealed record EditorHistoryState(bool CanUndo, bool CanRedo, EditorNotice? UndoLabel, EditorNotice? RedoLabel);

internal sealed record EditorAbout(
    string Product,
    string Version,
    string UpdateChannel,
    string? Commit,
    string Runtime,
    string RuntimeIdentifier,
    string OperatingSystem,
    string Architecture);

internal sealed record EditorDiagnosticBundleResult(bool Ok, string? Path, EditorNotice? Message);

internal sealed record EditorDiagnosticBundleActionResult(bool Ok, EditorNotice? Message);

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
    string? RenderedJson,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

internal sealed record EditorOperationResult(
    bool Ok,
    string Kind,
    EditorNotice? Message,
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
    EditorNotice? Message,
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
    IReadOnlyList<EditorExternalFileChange> Changes,
    // The review data after another writer (the command line, the language server) changed the review file; the
    // frontend installs it, or rebases its unsaved review edits onto it.
    EditorReviewSnapshot? Review = null);

internal sealed record EditorWorkspacePickerResult(bool Ok, bool Cancelled, string? Directory, EditorNotice? Message);

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
    EditorNotice? Message,
    IReadOnlyList<EditorMutationFile> Files,
    bool RequiresIrreversibleConfirmation = false,
    string? ConfirmationToken = null);

internal sealed record EditorRecoveryRequest(string Mode);

internal sealed record EditorInterchangeLoss(string Code, string Location, string Message, bool SemanticLoss);

internal sealed record EditorInterchangeRefusal(string Code, EditorNotice Message);

internal sealed record EditorInterchangeFile(string Path, string Locale, long ByteCount);

internal sealed record EditorXliffExportResult(
    bool Ok,
    EditorNotice? Message,
    string? CatalogId,
    IReadOnlyList<EditorInterchangeFile> Documents,
    IReadOnlyList<EditorInterchangeLoss> Losses);

internal sealed record EditorReviewFileResult(bool Ok, EditorNotice? Message, string? Path, int EntryCount);

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
    EditorNotice? Message,
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
    EditorNotice? Message,
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
    string ExpectedSourceFreshness,
    IReadOnlyList<PreparedInterchangeDocument> Documents,
    // The review file after the import (null: unchanged) and the review revision it was computed from.
    byte[]? ReviewBytes,
    string? ExpectedReviewRevision);

internal sealed record PreparedInterchangeDocument(
    string Path,
    string? ExpectedRevision,
    byte[]? OriginalBytes,
    byte[] Bytes);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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
[JsonSerializable(typeof(EditorLocalStateEntry[]))]
[JsonSerializable(typeof(EditorDocumentDraft))]
[JsonSerializable(typeof(EditorMessageOperation))]
[JsonSerializable(typeof(Rmf2DiagnosticQuickFix))]
[JsonSerializable(typeof(EditorLocalStateSnapshot))]
[JsonSerializable(typeof(EditorLocalStateClearResult))]
[JsonSerializable(typeof(EditorLibraryResult))]
[JsonSerializable(typeof(EditorLibraryRequest))]
[JsonSerializable(typeof(EditorLibraryOverrideRequest))]
[JsonSerializable(typeof(EditorLibraryLocaleRequest))]
[JsonSerializable(typeof(string))]
internal sealed partial class EditorJsonContext : JsonSerializerContext;
