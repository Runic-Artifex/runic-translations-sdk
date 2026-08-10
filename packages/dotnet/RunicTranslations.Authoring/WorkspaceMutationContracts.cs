namespace RunicTranslations.Authoring;

public sealed record TextResourceAddLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback,
    string Layer,
    string CopyFromLocale);

public sealed record TextResourceRemoveLocaleRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? ReplacementFallback);

public sealed record TextResourceSetFallbackRequest(
    string Root,
    string CatalogId,
    string Locale,
    string? Fallback);

public sealed record TextResourceCreateKeyRequest(
    string Root,
    string CatalogId,
    string Key,
    string InitialValue,
    string Layer);

public enum TextResourceKeyMutationKind
{
    RenameOrMove,
    Delete,
    Duplicate,
}

public sealed record TextResourceKeyMutationRequest(
    string Root,
    string CatalogId,
    TextResourceKeyMutationKind Kind,
    string SourceKey,
    string? TargetKey);
