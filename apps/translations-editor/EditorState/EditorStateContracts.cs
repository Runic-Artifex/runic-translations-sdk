using System;
using System.Collections.Generic;

namespace Runic.Translations.Editor;

// State and SourceFingerprint are read only from a version 1 file (for the move to the SDK review file); version 2
// keeps notes and samples, and Save ignores both.
internal sealed record TranslationEditorStateEntry(
    string Key,
    string Locale,
    string? State,
    string? Note,
    string? SourceFingerprint,
    IReadOnlyDictionary<string, string> Samples);

internal sealed record TranslationTerminologyEntry(
    string Source,
    string Preferred,
    string? Locale,
    string? Note);

internal sealed record TranslationEditorState(
    string CatalogId,
    IReadOnlyList<TranslationEditorStateEntry> Entries,
    IReadOnlyList<TranslationTerminologyEntry> Terminology);

internal sealed record TranslationEditorStateLoadResult(
    string Path,
    string? Revision,
    TranslationEditorState State,
    string? Error,
    int SchemaVersion = 0);

internal sealed class TranslationEditorStateException : Exception
{
    public TranslationEditorStateException(string message) : base(message) { }
}
