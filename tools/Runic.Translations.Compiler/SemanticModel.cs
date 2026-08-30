using System;
using System.Collections.Generic;

namespace Runic.Translations.Compiler;

internal sealed class ManifestModel
{
    internal ManifestModel(TranslationSource source) { Source = source; }
    internal TranslationSource Source { get; }
    internal int SchemaVersion { get; set; } = 1;
    internal string Id { get; set; } = string.Empty;
    internal string CodeNamespace { get; set; } = string.Empty;
    internal string ClassName { get; set; } = string.Empty;
    internal ByteSpan ClassNameSpan { get; set; }
    internal TranslationVisibility Visibility { get; set; } = TranslationVisibility.Public;
    internal string DefaultLocale { get; set; } = string.Empty;
    internal List<LocaleModel> Locales { get; } = new List<LocaleModel>();
    internal List<LayerModel> Layers { get; } = new List<LayerModel>();
    internal TranslationPolicy Completeness { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy ExtraKeys { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy EmptyValues { get; set; } = TranslationPolicy.Allow;
    internal TranslationUnsupportedLocalePolicy UnsupportedLocale { get; set; } = TranslationUnsupportedLocalePolicy.ParentsThenDefault;
    internal TranslationMissingKeyPolicy MissingKey { get; set; } = TranslationMissingKeyPolicy.Throw;
    internal ByteSpan IdSpan { get; set; }
    internal ByteSpan DefaultLocaleSpan { get; set; }
}

internal sealed class LocaleModel
{
    internal LocaleModel(string tag, string? fallback, ByteSpan span, ByteSpan fallbackSpan)
    { Tag = tag; Fallback = fallback; Span = span; FallbackSpan = fallbackSpan; }
    internal string Tag { get; }
    internal string? Fallback { get; }
    internal ByteSpan Span { get; }
    internal ByteSpan FallbackSpan { get; }
}

internal sealed class LayerModel
{
    internal LayerModel(string name, int priority, ByteSpan nameSpan, ByteSpan prioritySpan)
    { Name = name; Priority = priority; NameSpan = nameSpan; PrioritySpan = prioritySpan; }
    internal string Name { get; }
    internal int Priority { get; }
    internal ByteSpan NameSpan { get; }
    internal ByteSpan PrioritySpan { get; }
}

internal sealed class DocumentModel
{
    internal DocumentModel(TranslationSource source) { Source = source; }
    internal TranslationSource Source { get; }
    internal int SchemaVersion { get; set; } = 1;
    internal string Catalog { get; set; } = string.Empty;
    internal string Locale { get; set; } = string.Empty;
    internal string Layer { get; set; } = string.Empty;
    internal ByteSpan CatalogSpan { get; set; }
    internal ByteSpan LocaleSpan { get; set; }
    internal ByteSpan LayerSpan { get; set; }
    internal List<ResourceModel> Resources { get; } = new List<ResourceModel>();
    internal bool HadLimitError { get; set; }
}

internal sealed class ResourceModel
{
    internal ResourceModel(string key, string pattern, CompiledMessagePattern message, string? description, string? since, string? deprecatedReason,
        string[] tags, PlaceholderModel[] placeholders, TranslationSource source, ByteSpan keySpan, ByteSpan pathSpan, ByteSpan valueSpan)
    {
        Key = key; Pattern = pattern; Message = message; Description = description; Since = since; DeprecatedReason = deprecatedReason;
        Tags = tags; Placeholders = placeholders; Source = source; KeySpan = keySpan; PathSpan = pathSpan; ValueSpan = valueSpan;
    }
    internal string Key { get; }
    internal string Pattern { get; }
    internal CompiledMessagePattern Message { get; }
    internal string? Description { get; }
    internal string? Since { get; }
    internal string? DeprecatedReason { get; }
    internal string[] Tags { get; }
    internal PlaceholderModel[] Placeholders { get; }
    internal TranslationSource Source { get; }
    internal ByteSpan KeySpan { get; }
    internal ByteSpan PathSpan { get; }
    internal ByteSpan ValueSpan { get; }
}

internal sealed class PlaceholderModel
{
    internal PlaceholderModel(string name, TranslationArgumentType type, string format, ByteSpan span, ByteSpan typeSpan, ByteSpan formatSpan)
    { Name = name; Type = type; Format = format; Span = span; TypeSpan = typeSpan; FormatSpan = formatSpan; }
    internal string Name { get; }
    internal TranslationArgumentType Type { get; }
    internal string Format { get; }
    internal ByteSpan Span { get; }
    internal ByteSpan TypeSpan { get; }
    internal ByteSpan FormatSpan { get; }
}
