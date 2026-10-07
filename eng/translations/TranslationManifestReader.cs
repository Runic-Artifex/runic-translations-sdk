using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Runic.Translations.Compiler;

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
    internal const string SourceRootInvalidPath = "Each source root path must be a valid file-system path.";
    internal const string MissingBaseLocale = "runic.json must declare a baseLocale string.";
    /// <summary>The configuration-discovery diagnostic, shared with MSBuild source discovery.</summary>
    internal const string DiagnosticId = "RTR0052";

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
                    return Invalid(SourceRootInvalidPath, baseLocale);
                }
            }
            return new TranslationManifestLayout(roots, baseLocale, null);
        }
    }

    /// <summary>
    /// Adds the manifest errors that the compiler cannot see to a compilation.
    /// The compiler validates the manifest text but not the file system, and
    /// some layouts the reader rejects (a whitespace or unusable path) compile
    /// cleanly with no sources. Without this a project that discovers nothing
    /// would validate successfully. A layout error is added only when the
    /// compiler reported no error on the manifest itself, so the more precise
    /// compiler diagnostic is not duplicated; discovery errors (a missing or
    /// unusable source root) are always added.
    /// </summary>
    internal static Rmf2ProjectCompilationV5 WithManifestErrors(
        Rmf2ProjectCompilationV5 compilation, string projectPath, string? layoutError, IReadOnlyList<string>? discoveryErrors = null)
    {
        var errors = new List<string>();
        if (layoutError is not null && !compilation.Diagnostics.Any(diagnostic =>
                diagnostic.Severity == TranslationDiagnosticSeverity.Error &&
                string.Equals(diagnostic.Location.Path, projectPath, StringComparison.Ordinal)))
            errors.Add(layoutError);
        if (discoveryErrors is not null) errors.AddRange(discoveryErrors);
        if (errors.Count == 0) return compilation;
        var diagnostics = new List<TranslationDiagnostic>(compilation.Diagnostics);
        foreach (string error in errors)
            diagnostics.Add(new TranslationDiagnostic(DiagnosticId, TranslationDiagnosticSeverity.Error, error,
                new TextSourceLocation(projectPath, 0, 0, 1, 1, 1, 1)));
        return new Rmf2ProjectCompilationV5(null, diagnostics);
    }

    private static TranslationManifestLayout Invalid(string error, string? baseLocale = null) => new([], baseLocale, error);
}
