namespace RunicTranslations.Authoring;

public sealed record TranslationAddLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback,
    string Layer,
    string CopyFromLocale);

public sealed record TranslationRemoveLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? ReplacementFallback);

public sealed record TranslationSetFallbackRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback);

public sealed record TranslationCreateKeyRequest(
    string Root,
    string CatalogId,
    string Key,
    string InitialValue,
    string Layer);

public enum TranslationKeyMutationKind
{
    RenameOrMove,
    Delete,
    Duplicate,
}

public sealed record TranslationKeyMutationRequest(
    string Root,
    string CatalogId,
    TranslationKeyMutationKind Kind,
    string SourceKey,
    string? TargetKey);
