using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Runic.Translations.Internal;

/// <summary>Source layout read from a runic.json manifest without trusting its shape.</summary>
/// <param name="SourceRoots">Absolute source roots; the project directory when none are configured, empty when <paramref name="Error"/> is set.</param>
/// <param name="BaseLocale">The declared base locale, or <see langword="null"/> when it is missing or not a string.</param>
/// <param name="Error">Why the source layout could not be read, or <see langword="null"/> when it is usable.</param>
internal sealed record TranslationManifestLayout(IReadOnlyList<string> SourceRoots, string? BaseLocale, string? Error)
{
    internal bool IsValid => Error is null;
}

/// <summary>
/// Reads only the source layout of a runic.json manifest. Hand-edited or
/// half-saved manifests are common while a tool is watching them, so this
/// never throws for malformed content: it reports an error and no source
/// roots, and the compiler reports the located configuration diagnostic.
/// Shared by the command-line tool and the Translations Editor so both
/// discover the same sources.
/// </summary>
internal static class TranslationManifestReader
{
    internal const string InvalidJson = "runic.json is not valid JSON.";
    internal const string NotAnObject = "Runic project root must be an object.";
    internal const string SourceRootsNotArray = "sourceRoots must be an array.";
    internal const string SourceRootWithoutPath = "Each source root must declare a non-empty path.";

    internal static TranslationManifestLayout Read(ReadOnlyMemory<byte> utf8, string projectDirectory)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(utf8); }
        catch (JsonException) { return Invalid(InvalidJson); }
        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Invalid(NotAnObject);
            string? baseLocale = root.TryGetProperty("baseLocale", out JsonElement locale) && locale.ValueKind == JsonValueKind.String
                ? locale.GetString() : null;
            if (!root.TryGetProperty("sourceRoots", out JsonElement mounts))
                return new TranslationManifestLayout([projectDirectory], baseLocale, null);
            if (mounts.ValueKind != JsonValueKind.Array) return Invalid(SourceRootsNotArray, baseLocale);
            var roots = new List<string>();
            foreach (JsonElement mount in mounts.EnumerateArray())
            {
                if (mount.ValueKind != JsonValueKind.Object ||
                    !mount.TryGetProperty("path", out JsonElement path) || path.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(path.GetString()))
                    return Invalid(SourceRootWithoutPath, baseLocale);
                try { roots.Add(Path.GetFullPath(path.GetString()!, projectDirectory)); }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return Invalid(SourceRootWithoutPath, baseLocale);
                }
            }
            return new TranslationManifestLayout(roots, baseLocale, null);
        }
    }

    private static TranslationManifestLayout Invalid(string error, string? baseLocale = null) => new([], baseLocale, error);
}
