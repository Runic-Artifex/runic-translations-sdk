namespace Runic.Translations;

/// <summary>Version numbers shared by generated code and the runtime.</summary>
public static class TranslationsCompatibility
{
    /// <summary>The portable message grammar version supported by this release.</summary>
    public const int MessageGrammarVersion = 2;

    /// <summary>The ABI version embedded into generated C#.</summary>
    public const int RuntimeAbiVersion = 1;

    /// <summary>The additive ABI for v4 markup and v5 typed expression evaluation.</summary>
    public const int Rmf2RuntimeAbiVersion = 2;

    /// <summary>Checks an embedded RMF2 ABI requirement against the executing runtime, without const inlining.</summary>
    public static bool SupportsRmf2RuntimeAbi(int requiredVersion) => requiredVersion == Rmf2RuntimeAbiVersion;

    /// <summary>Rejects an unsupported generated RMF2 ABI requirement before message construction.</summary>
    public static void EnsureRmf2RuntimeAbi(int requiredVersion)
    {
        if (!SupportsRmf2RuntimeAbi(requiredVersion))
            throw new System.NotSupportedException("The generated RMF2 message requires an unsupported runtime ABI.");
    }
}
