using System.Collections.Generic;

namespace Runic.Translations.Compiler;

internal sealed class ManifestModel
{
    internal ManifestModel(TranslationSource source) { Source = source; }
    internal TranslationSource Source { get; }
    internal string Id { get; set; } = string.Empty;
    internal string CodeNamespace { get; set; } = string.Empty;
    internal string ClassName { get; set; } = string.Empty;
    internal TranslationVisibility Visibility { get; set; } = TranslationVisibility.Public;
    internal string DefaultLocale { get; set; } = string.Empty;
    internal List<LocaleModel> Locales { get; } = new();
    internal TranslationPolicy Completeness { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy ExtraKeys { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy EmptyValues { get; set; } = TranslationPolicy.Allow;
    internal TranslationUnsupportedLocalePolicy UnsupportedLocale { get; set; } = TranslationUnsupportedLocalePolicy.ParentsThenDefault;
    internal TranslationMissingKeyPolicy MissingKey { get; set; } = TranslationMissingKeyPolicy.Throw;
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

internal sealed class PlaceholderModel
{
    internal PlaceholderModel(string name, TranslationArgumentType type, string format)
    { Name = name; Type = type; Format = format; }
    internal string Name { get; }
    internal TranslationArgumentType Type { get; }
    internal string Format { get; }
}
