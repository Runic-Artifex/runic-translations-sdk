namespace RunicTranslations;

/// <summary>Identifies a generated translation within one catalog.</summary>
/// <param name="Catalog">The stable catalog identifier.</param>
/// <param name="Id">The build-local ordinal key identifier.</param>
/// <param name="Name">The stable dotted key name.</param>
public readonly record struct TranslationKey(string Catalog, int Id, string Name);
