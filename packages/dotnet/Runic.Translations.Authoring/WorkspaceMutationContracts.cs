namespace Runic.Translations.Authoring;

internal sealed record TranslationAddLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback,
    string CopyFromLocale);

internal sealed record TranslationRemoveLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? ReplacementFallback);

internal sealed record TranslationSetFallbackRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback);

internal sealed record TranslationCreateKeyRequest(
    string Root,
    string CatalogId,
    string Key,
    string InitialValue);

internal enum TranslationKeyMutationKind
{
    RenameOrMove,
    Delete,
    Duplicate,
}

internal sealed record TranslationKeyMutationRequest(
    string Root,
    string CatalogId,
    TranslationKeyMutationKind Kind,
    string SourceKey,
    string? TargetKey);
