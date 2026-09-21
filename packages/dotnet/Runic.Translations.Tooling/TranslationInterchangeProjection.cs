using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler;

namespace Runic.Translations.Tooling;

// This is deliberately an interchange-only carrier. In particular, a v5
// project is never squeezed through CompiledTextCatalog: that would erase its
// raw MF2 syntax, v5 caller contract, direct-resource identity, and freshness.
internal sealed record TranslationInterchangeProjection(
    string CatalogId,
    string SourceLocale,
    string Layer,
    int SchemaVersion,
    IReadOnlyList<TranslationInterchangeSourceUnit> CanonicalUnits,
    IReadOnlyList<TranslationInterchangeLocale> Locales,
    IReadOnlyList<TranslationDiagnostic> Diagnostics,
    string SourceFreshness,
    string TextProfileFingerprint)
{
    internal bool Success => !Diagnostics.Any(static diagnostic =>
        diagnostic.Severity == TranslationDiagnosticSeverity.Error);
}

internal sealed record TranslationInterchangeSourceUnit(
    string Key,
    string Text,
    bool Structured,
    TranslationInterchangeUnitMetadata Metadata);

internal sealed record TranslationInterchangeTargetUnit(string Key, string Text, bool Structured);

internal sealed record TranslationInterchangeLocale(
    string Tag,
    string? FallbackTag,
    IReadOnlyList<TranslationInterchangeTargetUnit> DirectResources);

internal sealed record TranslationInterchangeUnitMetadata(
    string? Description,
    string? Since,
    string? Deprecated,
    IReadOnlyList<string> Tags,
    IReadOnlyList<TranslationInterchangePlaceholder> Placeholders);

internal sealed record TranslationInterchangePlaceholder(string Name, string Type, string Format);

internal static class TranslationInterchangeProjectionAdapter
{
    internal static TranslationInterchangeProjection From(TranslationCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success)
            throw new TranslationInterchangeException("XLIFF21-COMPILATION", "XLIFF export requires a successful compiler result.");
        if (compilation.Catalogs.Count != 1)
            throw new TranslationInterchangeException("XLIFF21-CATALOG", "XLIFF export requires exactly one compiled catalog.");

        CompiledTextCatalog catalog = compilation.Catalogs[0];
        if (catalog.Layers.Count != 1)
            throw new TranslationInterchangeException("XLIFF21-LAYERS", "XLIFF export requires exactly one source layer so its identity can be preserved.");

        TranslationInterchangeSourceUnit[] canonical = catalog.CanonicalResources
            .OrderBy(static resource => resource.Key, StringComparer.Ordinal)
            .Select(static resource => new TranslationInterchangeSourceUnit(
                resource.Key,
                resource.Pattern,
                !resource.IsTextInterchangeLossless,
                new TranslationInterchangeUnitMetadata(
                    resource.Description,
                    resource.Since,
                    resource.DeprecatedReason,
                    resource.Tags.OrderBy(static tag => tag, StringComparer.Ordinal).ToArray(),
                    resource.Placeholders
                        .OrderBy(static placeholder => placeholder.Name, StringComparer.Ordinal)
                        .Select(static placeholder => new TranslationInterchangePlaceholder(
                            placeholder.Name, TypeName(placeholder.Type), placeholder.Format))
                        .ToArray())))
            .ToArray();
        TranslationInterchangeLocale[] locales = catalog.Locales
            .OrderBy(static locale => locale.Tag, StringComparer.Ordinal)
            .Select(static locale => new TranslationInterchangeLocale(
                locale.Tag,
                locale.FallbackTag,
                locale.DirectResources
                    .OrderBy(static resource => resource.Key, StringComparer.Ordinal)
                    .Select(static resource => new TranslationInterchangeTargetUnit(
                        resource.Key, resource.Pattern, !resource.IsTextInterchangeLossless))
                    .ToArray()))
            .ToArray();

        return Create(catalog.Id, catalog.DefaultLocale, catalog.Layers[0].Name, catalog.SchemaVersion,
            canonical, locales, compilation.Diagnostics, sourceFreshness: null);
    }

    internal static TranslationInterchangeProjection From(TranslationProfileCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        if (!compilation.Success)
            throw new TranslationInterchangeException("XLIFF21-COMPILATION", "XLIFF export requires a successful compiler result.");
        if (compilation.Current is not null) return From(compilation.Current);
        if (compilation.Rmf2?.Project is not { } project)
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
                return new TranslationInterchangeSourceUnit(
                    contract.Key,
                    RawSyntax(source.Message),
                    IsStructured(source.Message),
                    new TranslationInterchangeUnitMetadata(
                        source.Description,
                        null,
                        null,
                        Array.Empty<string>(),
                        contract.Inputs
                            .OrderBy(static input => input.Name, StringComparer.Ordinal)
                            .Select(static input => new TranslationInterchangePlaceholder(input.Name, input.Type, string.Empty))
                            .ToArray()));
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
                    .Select(static resource => new TranslationInterchangeTargetUnit(
                        resource.Key, RawSyntax(resource.Message), IsStructured(resource.Message)))
                    .ToArray()))
            .ToArray();

        return Create(project.Id, project.DefaultLocale, "base", 2, canonical, locales, diagnostics,
            project.SourceHash);
    }

    private static TranslationInterchangeProjection Create(
        string catalog,
        string sourceLocale,
        string layer,
        int schemaVersion,
        IReadOnlyList<TranslationInterchangeSourceUnit> canonical,
        IReadOnlyList<TranslationInterchangeLocale> locales,
        IReadOnlyList<TranslationDiagnostic> diagnostics,
        string? sourceFreshness)
    {
        string textFingerprint = TranslationInterchangeFingerprint.TextProfile(
            catalog, sourceLocale, layer, schemaVersion, canonical);
        var projection = new TranslationInterchangeProjection(
            catalog,
            sourceLocale,
            layer,
            schemaVersion,
            canonical,
            locales,
            diagnostics,
            sourceFreshness ?? string.Empty,
            textFingerprint);
        return projection with
        {
            SourceFreshness = sourceFreshness ?? TranslationInterchangeFingerprint.ProjectionSource(projection),
        };
    }

    private static string RawSyntax(Rmf2MessageV5 message) =>
        Encoding.UTF8.GetString(message.Syntax.Source.GetUtf8Bytes());

    private static bool IsStructured(Rmf2MessageV5 message) =>
        message.Declarations.Count != 0 ||
        message.Selectors.Count != 0 ||
        message.Variants.Count != 1 ||
        message.Variants[0].Nodes.Any(static node => node is not Rmf2TextV5);

    private static string TypeName(TranslationArgumentType type) => type switch
    {
        TranslationArgumentType.Int => "int",
        TranslationArgumentType.Number => "number",
        TranslationArgumentType.Boolean => "bool",
        TranslationArgumentType.Date => "date",
        TranslationArgumentType.Time => "time",
        TranslationArgumentType.DateTime => "datetime",
        TranslationArgumentType.Guid => "guid",
        _ => "string",
    };
}

internal static class TranslationInterchangeFingerprint
{
    private const string TextProfileDomain = "runic.xliff21.closed-text-profile/1";
    private const string ProjectionSourceDomain = "runic.xliff21.projection-source/1";

    internal static string TextProfile(
        string catalog,
        string sourceLocale,
        string layer,
        int schemaVersion,
        IEnumerable<TranslationInterchangeSourceUnit> units)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("profile", TextProfileDomain);
            writer.WriteString("catalog", catalog);
            writer.WriteString("sourceLocale", sourceLocale);
            writer.WriteString("layer", layer);
            writer.WriteNumber("schemaVersion", schemaVersion);
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
            writer.WriteString("layer", projection.Layer);
            writer.WriteNumber("schemaVersion", projection.SchemaVersion);
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
