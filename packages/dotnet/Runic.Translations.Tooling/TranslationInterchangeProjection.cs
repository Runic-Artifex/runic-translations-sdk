using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

// This is deliberately an interchange-only carrier. A v5 project is never
// squeezed through a reduced catalog projection: that would erase its raw MF2
// syntax, caller contract, direct-resource identity, and freshness.
internal sealed record TranslationInterchangeProjection(
    string CatalogId,
    string SourceLocale,
    string ExecutionProfile,
    int MessageGrammarVersion,
    string InterchangeProfile,
    int InterchangeProfileVersion,
    IReadOnlyList<TranslationInterchangeSourceUnit> CanonicalUnits,
    IReadOnlyList<TranslationInterchangeLocale> Locales,
    IReadOnlyList<TranslationDiagnostic> Diagnostics,
    string SourceFreshness)
{
    internal bool Success => !Diagnostics.Any(static diagnostic =>
        diagnostic.Severity == TranslationDiagnosticSeverity.Error);

    /// <summary>The per-unit source fingerprint of every canonical key.</summary>
    internal IReadOnlyDictionary<string, string> SourceFingerprints =>
        CanonicalUnits.ToDictionary(static unit => unit.Key, static unit => unit.SourceFingerprint, StringComparer.Ordinal);
}

/// <param name="Pieces">The plain-message pieces, or <see langword="null"/> for a structured message.</param>
/// <param name="SourceFingerprint">The per-unit fingerprint of the key, the source text and the caller contract.</param>
internal sealed record TranslationInterchangeSourceUnit(
    string Key,
    string Text,
    bool Structured,
    TranslationInterchangeUnitMetadata Metadata,
    bool Document = false)
{
    internal IReadOnlyList<TranslationInterchangePiece>? Pieces { get; init; }
    internal string SourceFingerprint { get; init; } = string.Empty;
}

internal sealed record TranslationInterchangeTargetUnit(string Key, string Text, bool Structured)
{
    internal IReadOnlyList<TranslationInterchangePiece>? Pieces { get; init; }
    internal TextSourceLocation? Location { get; init; }
}

internal sealed record TranslationInterchangeLocale(
    string Tag,
    string? FallbackTag,
    IReadOnlyList<TranslationInterchangeTargetUnit> DirectResources);

internal sealed record TranslationInterchangeUnitMetadata(
    string? Description,
    string? Since,
    string? Deprecated,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TranslationInterchangePlaceholder> Placeholders)
{
    /// <summary>The documented parameters, each <c>$name - text</c>, shown in the readable description note.</summary>
    internal IReadOnlyList<string> Parameters { get; init; } = Array.Empty<string>();

    /// <summary>The message's <c>@maxLength</c> in Unicode code points, exported as an XLIFF size restriction.</summary>
    internal int? MaxLength { get; init; }
}

internal sealed record TranslationInterchangePlaceholder(string Name, string Type, string Format);

internal static class TranslationInterchangeProjectionAdapter
{
    internal static TranslationInterchangeProjection From(Rmf2ProjectCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success)
            throw new TranslationInterchangeException("XLIFF21-COMPILATION", "XLIFF export requires a successful compiler result.");
        if (compilation.Project is not { } project)
            throw new TranslationInterchangeException("XLIFF21-CATALOG", "XLIFF export requires exactly one compiled catalog.");
        return From(project, compilation.Diagnostics);
    }

    private static TranslationInterchangeProjection From(
        Rmf2ProjectV5 project,
        IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        var canonicalKeys = new HashSet<string>(
            project.CanonicalMessages.Select(static contract => contract.Key), StringComparer.Ordinal);
        Rmf2LocaleV5 sourceLocale = project.Locales.Single(locale =>
            string.Equals(locale.Tag, project.DefaultLocale, StringComparison.Ordinal));
        var sourceByKey = sourceLocale.DirectResources.ToDictionary(
            static resource => resource.Key, StringComparer.Ordinal);
        TranslationInterchangeSourceUnit[] canonical = project.CanonicalMessages
            .OrderBy(static contract => contract.Key, StringComparer.Ordinal)
            .Select(contract =>
            {
                if (!sourceByKey.TryGetValue(contract.Key, out Rmf2TranslationV5? source))
                    throw new TranslationInterchangeException("XLIFF21-CANONICAL", "The compiled v5 project is missing a direct canonical source resource.");
                string raw = RawSyntax(source.Message);
                bool document = contract.Content == Rmf2DocumentProfileV5.Document;
                IReadOnlyList<TranslationInterchangePiece>? pieces = document || IsStructured(source.Message)
                    ? null
                    : TranslationInterchangePattern.Decompose(raw);
                TranslationInterchangePlaceholder[] placeholders = contract.Inputs
                    .OrderBy(static input => input.Name, StringComparer.Ordinal)
                    .Select(static input => new TranslationInterchangePlaceholder(input.Name, input.Type, string.Empty))
                    .ToArray();
                return new TranslationInterchangeSourceUnit(
                    contract.Key,
                    raw,
                    pieces is null,
                    new TranslationInterchangeUnitMetadata(
                        source.Description,
                        null,
                        null,
                        Array.Empty<string>(),
                        placeholders)
                    {
                        Parameters = source.Properties
                            .Where(static property => property.StartsWith("param ", StringComparison.Ordinal))
                            .Select(static property => property["param ".Length..].Trim())
                            .ToArray(),
                        MaxLength = MaxLength(source.Properties),
                    },
                    document)
                {
                    Pieces = pieces,
                    SourceFingerprint = TranslationInterchangeFingerprint.UnitSource(contract.Key, raw, pieces, placeholders),
                };
            })
            .ToArray();
        TranslationInterchangeLocale[] locales = project.Locales
            .OrderBy(static locale => locale.Tag, StringComparer.Ordinal)
            .Select(locale => new TranslationInterchangeLocale(
                locale.Tag,
                locale.FallbackTag,
                locale.DirectResources
                    .Where(resource => canonicalKeys.Contains(resource.Key))
                    .OrderBy(static resource => resource.Key, StringComparer.Ordinal)
                    .Select(static resource =>
                    {
                        string raw = RawSyntax(resource.Message);
                        IReadOnlyList<TranslationInterchangePiece>? pieces = IsStructured(resource.Message) ? null : TranslationInterchangePattern.Decompose(raw);
                        return new TranslationInterchangeTargetUnit(resource.Key, raw, pieces is null)
                        {
                            Pieces = pieces,
                            Location = resource.SourceLocation,
                        };
                    })
                    .ToArray()))
            .ToArray();

        return Create(project.Id, project.DefaultLocale, canonical, locales, diagnostics,
            project.SourceHash);
    }

    private static TranslationInterchangeProjection Create(
        string catalog,
        string sourceLocale,
        IReadOnlyList<TranslationInterchangeSourceUnit> canonical,
        IReadOnlyList<TranslationInterchangeLocale> locales,
        IReadOnlyList<TranslationDiagnostic> diagnostics,
        string? sourceFreshness)
    {
        var projection = new TranslationInterchangeProjection(
            catalog,
            sourceLocale,
            Rmf2ProjectCompilation.CurrentExecutionProfile,
            Rmf2ProjectCompilation.CurrentMessageGrammarVersion,
            TranslationInterchange.XliffProfile,
            TranslationInterchange.XliffProfileVersion,
            canonical,
            locales,
            diagnostics,
            sourceFreshness ?? string.Empty);
        return projection with
        {
            SourceFreshness = sourceFreshness ?? TranslationInterchangeFingerprint.ProjectionSource(projection),
        };
    }

    // "@maxLength N" with a positive N; the compiler reports any other spelling (RTR0051).
    private static int? MaxLength(IReadOnlyList<string> properties)
    {
        foreach (string property in properties)
            if (property.StartsWith("maxLength ", StringComparison.Ordinal) &&
                int.TryParse(property["maxLength ".Length..].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value) && value > 0)
                return value;
        return null;
    }

    private static string RawSyntax(Rmf2MessageV5 message) =>
        Encoding.UTF8.GetString(message.Syntax.Source.GetUtf8Bytes());

    // Profile v3: placeholders are plain (exchanged as <ph>); declarations, selectors and markup are structured.
    private static bool IsStructured(Rmf2MessageV5 message) =>
        message.Declarations.Count != 0 ||
        message.Selectors.Count != 0 ||
        message.Variants.Count != 1 ||
        message.Variants[0].Nodes.Any(static node => node is not Rmf2TextV5 and not Rmf2ExpressionNodeV5);

}

internal static class TranslationInterchangeFingerprint
{
    private const string TextProfileDomain = "runic.xliff21.closed-text-profile/2";
    private const string UnitSourceDomain = "runic.translations.unit-source/1";

    /// <summary>
    /// The per-unit source fingerprint: the key, the source text and the caller contract (input names and types).
    /// The text is hashed in canonical form (<see cref="TranslationInterchangePattern.CanonicalMessage"/>): equivalent
    /// escapes, spacing inside an expression, literal quoting and option order do not change it; a different
    /// placeholder, function, option value, selector or pattern text does.
    /// </summary>
    internal static string UnitSource(
        string key,
        string rawSource,
        IReadOnlyList<TranslationInterchangePiece>? pieces,
        IEnumerable<TranslationInterchangePlaceholder> contract)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", UnitSourceDomain);
            writer.WriteString("key", key);
            writer.WriteString("source", pieces is null
                ? TranslationInterchangePattern.CanonicalMessage(rawSource)
                : TranslationInterchangePattern.Compose(TranslationInterchangePattern.Canonical(pieces)));
            writer.WriteStartArray("inputs");
            foreach (TranslationInterchangePlaceholder input in contract.OrderBy(static input => input.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", input.Name);
                writer.WriteString("type", input.Type);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Hash(stream.ToArray());
    }
    private const string ProjectionSourceDomain = "runic.xliff21.projection-source/2";

    internal static string TextProfile(
        string catalog,
        string sourceLocale,
        string executionProfile,
        int messageGrammarVersion,
        string interchangeProfile,
        int interchangeProfileVersion,
        IEnumerable<TranslationInterchangeSourceUnit> units)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", TextProfileDomain);
            writer.WriteString("catalog", catalog);
            writer.WriteString("sourceLocale", sourceLocale);
            writer.WriteString("executionProfile", executionProfile);
            writer.WriteNumber("messageGrammarVersion", messageGrammarVersion);
            writer.WriteString("interchangeProfile", interchangeProfile);
            writer.WriteNumber("interchangeProfileVersion", interchangeProfileVersion);
            writer.WriteStartArray("units");
            foreach (TranslationInterchangeSourceUnit unit in units.OrderBy(static unit => unit.Key, StringComparer.Ordinal))
                WriteSourceUnit(writer, unit);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Hash(stream.ToArray());
    }

    internal static string ProjectionSource(TranslationInterchangeProjection projection)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", ProjectionSourceDomain);
            writer.WriteString("catalog", projection.CatalogId);
            writer.WriteString("sourceLocale", projection.SourceLocale);
            writer.WriteString("executionProfile", projection.ExecutionProfile);
            writer.WriteNumber("messageGrammarVersion", projection.MessageGrammarVersion);
            writer.WriteString("interchangeProfile", projection.InterchangeProfile);
            writer.WriteNumber("interchangeProfileVersion", projection.InterchangeProfileVersion);
            writer.WriteStartArray("canonicalUnits");
            foreach (TranslationInterchangeSourceUnit unit in projection.CanonicalUnits.OrderBy(static unit => unit.Key, StringComparer.Ordinal))
                WriteSourceUnit(writer, unit);
            writer.WriteEndArray();
            writer.WriteStartArray("locales");
            foreach (TranslationInterchangeLocale locale in projection.Locales.OrderBy(static locale => locale.Tag, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("tag", locale.Tag);
                if (locale.FallbackTag is not null) writer.WriteString("fallback", locale.FallbackTag);
                writer.WriteStartArray("directResources");
                foreach (TranslationInterchangeTargetUnit resource in locale.DirectResources.OrderBy(static resource => resource.Key, StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", resource.Key);
                    writer.WriteString("text", resource.Text);
                    writer.WriteBoolean("structured", resource.Structured);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Hash(stream.ToArray());
    }

    private static void WriteSourceUnit(Utf8JsonWriter writer, TranslationInterchangeSourceUnit unit)
    {
        writer.WriteStartObject();
        writer.WriteString("key", unit.Key);
        writer.WriteString("text", unit.Text);
        writer.WriteBoolean("structured", unit.Structured);
        if (unit.Metadata.Description is not null) writer.WriteString("description", unit.Metadata.Description);
        if (unit.Metadata.Since is not null) writer.WriteString("since", unit.Metadata.Since);
        if (unit.Metadata.Deprecated is not null) writer.WriteString("deprecated", unit.Metadata.Deprecated);
        writer.WriteStartArray("tags");
        foreach (string tag in unit.Metadata.Tags.OrderBy(static tag => tag, StringComparer.Ordinal))
            writer.WriteStringValue(tag);
        writer.WriteEndArray();
        writer.WriteStartArray("placeholders");
        foreach (TranslationInterchangePlaceholder placeholder in unit.Metadata.Placeholders
            .OrderBy(static placeholder => placeholder.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", placeholder.Name);
            writer.WriteString("type", placeholder.Type);
            writer.WriteString("format", placeholder.Format);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}
