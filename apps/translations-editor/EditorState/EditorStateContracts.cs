using System;
using System.Collections.Generic;

namespace Runic.Translations.Editor;

internal sealed record TranslationEditorStateEntry(
    string Key,
    string Locale,
    string State,
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
    string? Error);

internal sealed class TranslationEditorStateException : Exception
{
    public TranslationEditorStateException(string message) : base(message) { }
}
