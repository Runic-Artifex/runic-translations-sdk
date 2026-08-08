using System;
using System.Collections.Generic;

namespace RunicTextResources.Authoring;

public sealed record TextResourceEditorStateEntry(
    string Key,
    string Locale,
    string State,
    string? Note,
    string? SourceFingerprint,
    IReadOnlyDictionary<string, string> Samples);

public sealed record TextResourceTerminologyEntry(
    string Source,
    string Preferred,
    string? Locale,
    string? Note);

public sealed record TextResourceEditorState(
    string CatalogId,
    IReadOnlyList<TextResourceEditorStateEntry> Entries,
    IReadOnlyList<TextResourceTerminologyEntry> Terminology);

public sealed record TextResourceEditorStateLoadResult(
    string Path,
    string? Revision,
    TextResourceEditorState State,
    string? Error);

public sealed class TextResourceEditorStateException : Exception
{
    public TextResourceEditorStateException(string message) : base(message) { }
}
