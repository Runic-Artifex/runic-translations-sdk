using System;
using System.Collections.Generic;

namespace Runic.Translations.Authoring;

public sealed record TranslationEditorStateEntry(
    string Key,
    string Locale,
    string State,
    string? Note,
    string? SourceFingerprint,
    IReadOnlyDictionary<string, string> Samples);

public sealed record TranslationTerminologyEntry(
    string Source,
    string Preferred,
    string? Locale,
    string? Note);

public sealed record TranslationEditorState(
    string CatalogId,
    IReadOnlyList<TranslationEditorStateEntry> Entries,
    IReadOnlyList<TranslationTerminologyEntry> Terminology);

public sealed record TranslationEditorStateLoadResult(
    string Path,
    string? Revision,
    TranslationEditorState State,
    string? Error);

public sealed class TranslationEditorStateException : Exception
{
    public TranslationEditorStateException(string message) : base(message) { }
}
