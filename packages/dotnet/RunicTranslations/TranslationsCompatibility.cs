namespace RunicTranslations;

/// <summary>Version numbers shared by generated code, source documents, and the runtime.</summary>
public static class TranslationsCompatibility
{
    /// <summary>The catalog manifest schema version supported by this release.</summary>
    public const int CatalogSchemaVersion = 2;

    /// <summary>The resource document schema version supported by this release.</summary>
    public const int ResourceSchemaVersion = 2;

    /// <summary>The portable message grammar version supported by this release.</summary>
    public const int MessageGrammarVersion = 2;

    /// <summary>The ABI version embedded into generated C#.</summary>
    public const int RuntimeAbiVersion = 1;
}
