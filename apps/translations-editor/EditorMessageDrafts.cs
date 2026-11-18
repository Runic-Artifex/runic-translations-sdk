using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Editor;

// The starter text of a translation that does not exist yet (issue #225, P2): the base message's structure with every
// pattern empty, so the visual composer can edit it before anything is written to a document.
internal static class EditorMessageDrafts
{
    private static readonly string[] PluralCategoryNames = ["zero", "one", "two", "few", "many", "other"];

    /// <summary>
    /// The base message's <c>.input</c>/<c>.local</c> declarations and <c>.match</c> selectors, byte for byte, with one
    /// empty variant per key vector the target locale needs. A plural or ordinal selector column takes the base's
    /// exact-number keys, then the categories the target locale's CLDR rules can select (the same
    /// <see cref="Rmf2AuthoringService.PluralCategories"/> that seeds the starter copies of an added locale and repairs
    /// RTR0095), with <c>*</c> standing for <c>other</c>; any other selector column takes the base's keys. Every column
    /// ends with <c>*</c>, so the last variant is the catch-all. A message without a matcher gets one empty pattern:
    /// <c>{{}}</c> after its declarations, else the empty simple pattern.
    /// </summary>
    internal static string Starter(Rmf2MessageV5 baseMessage, string targetLocale)
    {
        Mf2SyntaxDocument syntax = baseMessage.Syntax;
        byte[] bytes = syntax.Source.Bytes;
        var text = new StringBuilder();
        foreach (Mf2DeclarationSyntax declaration in syntax.Declarations)
            text.Append(Encoding.UTF8.GetString(bytes, declaration.Location.StartByte, declaration.Location.LengthBytes)).Append('\n');
        if (syntax.Match is not { } match)
            return syntax.Declarations.Count == 0 ? string.Empty : text.Append("{{}}").ToString();

        text.Append(".match ").AppendJoin(' ', match.Selectors.Select(static selector => "$" + selector)).Append('\n');
        var columns = new List<IReadOnlyList<string>>(match.Selectors.Count);
        for (int column = 0; column < match.Selectors.Count; column++)
        {
            Rmf2SelectorV5? selector = column < baseMessage.Selectors.Count ? baseMessage.Selectors[column] : null;
            IEnumerable<string> baseKeys = syntax.Variants.Select(variant => variant.Keys[column]).Where(static key => key != "*");
            IEnumerable<string> keys = selector?.Function is "plural" or "ordinal"
                ? baseKeys.Where(static key => !PluralCategoryNames.Contains(key, StringComparer.Ordinal))
                    .Concat(Rmf2AuthoringService.PluralCategories(targetLocale, selector.Function == "ordinal", selector.Type == "int64")
                        .Where(static category => category != "other"))
                : baseKeys;
            columns.Add([.. keys.Distinct(StringComparer.Ordinal), "*"]);
        }
        IEnumerable<IReadOnlyList<string>> vectors = [[]];
        foreach (IReadOnlyList<string> column in columns)
            vectors = vectors.SelectMany(prefix => column.Select(key => (IReadOnlyList<string>)[.. prefix, key])).ToArray();
        return text.AppendJoin('\n', vectors.Select(static vector => string.Join(' ', vector) + " {{}}")).ToString();
    }

    /// <summary>Whether a message has no text yet: every variant's pattern is empty or white space.</summary>
    internal static bool IsEmpty(Rmf2AuthoringProjection projection) =>
        projection.Variants.All(static variant => string.IsNullOrWhiteSpace(variant.Pattern));
}
