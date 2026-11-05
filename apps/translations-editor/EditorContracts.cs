using System.Text.Json.Serialization;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Editor;

internal sealed record EditorCatalog(
    string Id,
    int SchemaVersion,
    string DefaultLocale,
    IReadOnlyList<EditorLocale> Locales,
    IReadOnlyList<EditorLayer> Layers,
    // runic.json pseudoLocales (en-XA, ar-XB): generated from the base locale for previews, never edited.
    IReadOnlyList<string>? PseudoLocales = null);

internal sealed record EditorLocale(string Tag, string? Fallback);

internal sealed record EditorLayer(string Name, int Priority);

// The add-language picker's suggestions: the SDK's pinned CLDR locales by data ID (EditorLocales).
internal sealed record EditorLocaleCatalog(string CldrVersion, IReadOnlyList<string> Locales);

// A typed locale tag: canonical form, CLDR data ID through likely subtags (zh-TW -> zh-Hant-TW), the CLDR locale its
// data comes from (null: none, only root), text direction, and the plural and ordinal categories it needs. Suggested is
// the script-level locale for a region tag (zh-TW -> zh-Hant); with a compiled catalog, UnsupportedLocale is its policy
// and Matches/SuggestedMatches the requested tags the runtime would resolve to the typed and the suggested locale.
internal sealed record EditorLocaleDescription(bool Valid, string? Canonical, string? DataId, string? CldrLocale,
    string Direction, IReadOnlyList<string> Cardinal, IReadOnlyList<string> Ordinal, EditorNotice? Message)
{
    public string? Suggested { get; init; }
    public string? UnsupportedLocale { get; init; }
    public EditorLocaleMatches? Matches { get; init; }
    public EditorLocaleMatches? SuggestedMatches { get; init; }
}

// Requested tags that resolve to a locale: the first few (the tag itself, then the shortest) and how many in all.
internal sealed record EditorLocaleMatches(IReadOnlyList<string> Tags, int Count);

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
    EditorHistoryState? History,
    // runic-translations status for the saved files (TranslationStatus): the same numbers and per-locale completeness
    // policy as the command line. Null when runic.json, the base locale or a translation's syntax has errors.
    TranslationsStatusProject? Status = null)
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

// Import from .resx or i18next through Tooling's importer (runic-translations import): a dry run first.
internal sealed record EditorCatalogImportRequest(string From, string Source, bool Overwrite = false);

internal sealed record EditorCatalogImportLocale(string Locale, int Added, int Changed, int Unchanged, int Kept);

// One message the import adds, changes, keeps (it exists with other text), skips or refuses, with the reason.
internal sealed record EditorCatalogImportEntry(string Locale, string Key, string Outcome, string? Code, string? Message);

internal sealed record EditorCatalogImportPlan(
    bool Ok,
    EditorNotice? Message,
    string? ConfirmationToken,
    string From,
    string Source,
    IReadOnlyList<EditorCatalogImportLocale> Locales,
    IReadOnlyList<EditorCatalogImportEntry> Entries,
    bool EntriesOverflowed,
    IReadOnlyList<string> Files,
    int Skipped,
    int Refused,
    IReadOnlyList<EditorDiagnostic> Diagnostics);

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
[JsonSerializable(typeof(EditorLocaleCatalog))]
[JsonSerializable(typeof(EditorLocaleDescription))]
[JsonSerializable(typeof(EditorLocaleMatches))]
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
[JsonSerializable(typeof(EditorCatalogImportRequest))]
[JsonSerializable(typeof(EditorCatalogImportPlan))]
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
