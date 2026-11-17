// Linked into Runic.Translations and Runic.Translations.Compiler; each assembly gets its own internal copy.
// The ESM runtime implements the same rule (Rmf2EsmRuntimeV5.js withoutPseudoLocales, with the pseudoLocales constant that
// Rmf2EsmOutputRendererV5 emits from IsPseudo).
#if RUNIC_TRANSLATIONS_COMPILER
namespace Runic.Translations.Compiler;
#else
namespace Runic.Translations;
#endif

/// <summary>
/// The development pseudo-locales of <c>runic.json</c> <c>pseudoLocales</c>, <c>en-XA</c> and <c>ar-XB</c>, are chosen only
/// when they are requested by their own tag. Locale fallback and negotiation of every other tag skip them, so a development
/// build that generates them resolves <c>en-US</c>, <c>en</c> or <c>ar-EG</c> exactly as a release build without them does.
/// </summary>
internal static class PseudoLocaleTags
{
    /// <summary>Whether a canonical tag is one of the pseudo-locale tags.</summary>
    internal static bool IsPseudo(string canonical) => canonical is "en-XA" or "ar-XB";

    /// <summary>The candidates without the pseudo-locales; the same array when it contains none.</summary>
    internal static string[] WithoutPseudo(string[] candidates)
    {
        int count = 0;
        foreach (string candidate in candidates)
            if (IsPseudo(candidate)) count++;
        if (count == 0) return candidates;
        string[] result = new string[candidates.Length - count];
        int index = 0;
        foreach (string candidate in candidates)
            if (!IsPseudo(candidate)) result[index++] = candidate;
        return result;
    }
}
