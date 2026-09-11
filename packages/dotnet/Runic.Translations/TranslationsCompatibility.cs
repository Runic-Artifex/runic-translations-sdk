namespace Runic.Translations;

/// <summary>Version numbers shared by generated code and the runtime.</summary>
public static class TranslationsCompatibility
{
    /// <summary>The portable message grammar version supported by this release.</summary>
    public const int MessageGrammarVersion = 2;

    /// <summary>The ABI version embedded into generated C#.</summary>
    public const int RuntimeAbiVersion = 1;

    /// <summary>The additive ABI for RMF2 standalone markup, options, and caller contracts.</summary>
    public const int Rmf2RuntimeAbiVersion = 1;
}
