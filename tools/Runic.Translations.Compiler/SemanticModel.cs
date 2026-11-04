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
    internal bool ReferenceFallbackText { get; set; } = true;
    internal string DefaultLocale { get; set; } = string.Empty;
    internal List<LocaleModel> Locales { get; } = new();
    internal TranslationPolicy Completeness { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy ExtraKeys { get; set; } = TranslationPolicy.Error;
    internal TranslationPolicy EmptyValues { get; set; } = TranslationPolicy.Allow;
    // The QA checks of 0.9 (RTR0094-RTR0098) are warnings unless runic.json says otherwise.
    internal TranslationPolicy PluralCategories { get; set; } = TranslationPolicy.Warning;
    internal TranslationPolicy InputParity { get; set; } = TranslationPolicy.Warning;
    internal TranslationPolicy FormattedBooleans { get; set; } = TranslationPolicy.Warning;
    internal TranslationPolicy MaxLength { get; set; } = TranslationPolicy.Warning;

    /// <summary>The policy of a <c>validation</c> member for a locale: the locale entry's own value, else the project's.</summary>
    internal TranslationPolicy Policy(string locale, string member)
    {
        foreach (LocaleModel model in Locales)
            if (string.Equals(model.Tag, locale, System.StringComparison.OrdinalIgnoreCase) && model.Validation.TryGetValue(member, out TranslationPolicy own))
                return own;
        return member switch
        {
            ValidationMembers.TranslationCompleteness => Completeness,
            ValidationMembers.ExtraLocaleKeys => ExtraKeys,
            ValidationMembers.EmptyValues => EmptyValues,
            ValidationMembers.PluralCategories => PluralCategories,
            ValidationMembers.InputParity => InputParity,
            ValidationMembers.FormattedBooleans => FormattedBooleans,
            ValidationMembers.MaxLength => MaxLength,
            _ => throw new System.ArgumentOutOfRangeException(nameof(member), member, "Unknown validation member."),
        };
    }
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
    /// <summary>The locale entry's own <c>validation</c> policies, by member name; they override the project's.</summary>
    internal Dictionary<string, TranslationPolicy> Validation { get; } = new(System.StringComparer.Ordinal);
}

/// <summary>The members of a <c>validation</c> object, in runic.json and on a locale entry.</summary>
internal static class ValidationMembers
{
    internal const string TranslationCompleteness = "translationCompleteness";
    internal const string ExtraLocaleKeys = "extraLocaleKeys";
    internal const string EmptyValues = "emptyValues";
    internal const string PluralCategories = "pluralCategories";
    internal const string InputParity = "inputParity";
    internal const string FormattedBooleans = "formattedBooleans";
    internal const string MaxLength = "maxLength";
    internal static readonly string[] All = { TranslationCompleteness, ExtraLocaleKeys, EmptyValues, PluralCategories, InputParity, FormattedBooleans, MaxLength };
}

internal sealed class PlaceholderModel
{
    internal PlaceholderModel(string name, TranslationArgumentType type, string format)
    { Name = name; Type = type; Format = format; }
    internal string Name { get; }
    internal TranslationArgumentType Type { get; }
    internal string Format { get; }
}
