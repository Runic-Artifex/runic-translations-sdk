using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Tool;

/// <summary>
/// Supplies a message's review state for hover. The language server has no source yet: the SDK
/// review file (<c>&lt;catalog&gt;.review.json</c>, T3c) is read through Tooling once it exists, and
/// its per-unit fingerprint decides whether an approval is stale. Until then
/// <see cref="Rmf2LanguageServer"/> uses <see cref="None"/> and hover shows no review line.
/// </summary>
internal interface IRmf2ReviewStateSource
{
    /// <summary>A short state such as <c>approved</c>, <c>needs-review</c> or <c>stale</c>, or null when unknown.</summary>
    string? Describe(string projectDirectory, string key, string locale);

    internal static IRmf2ReviewStateSource None { get; } = new NoReviewState();

    private sealed class NoReviewState : IRmf2ReviewStateSource
    {
        public string? Describe(string projectDirectory, string key, string locale) => null;
    }
}

/// <summary>
/// The plain-text hover of an RMF2 message: the language service's summary, then translator
/// context (the base-locale text with its comments and <c>@param</c> lines when the hovered
/// file is a translation, the review state), the compiled contract, and the entry's own metadata.
/// </summary>
internal sealed class Rmf2HoverContent
{
    internal required string Summary { get; init; }
    internal string? BaseLocale { get; init; }
    internal string? BaseText { get; init; }
    internal IReadOnlyList<string> BaseComments { get; init; } = [];
    internal IReadOnlyList<string> BaseParameters { get; init; } = [];
    internal string? ReviewState { get; init; }
    internal IReadOnlyList<string> Contract { get; init; } = [];
    internal IReadOnlyList<string> Comments { get; init; } = [];
    internal IReadOnlyList<string> Properties { get; init; } = [];

    internal string Render()
    {
        var text = new StringBuilder(Summary);
        if (BaseText is not null)
        {
            // A one-line base text stays on the label line; a multiline one is indented below it.
            string label = "\nBase text (" + BaseLocale + "):";
            if (BaseText.Contains('\n', StringComparison.Ordinal))
                text.Append(label).Append(string.Concat(BaseText.Split('\n').Select(line => "\n  " + line)));
            else text.Append(label).Append(' ').Append(BaseText);
            foreach (string comment in BaseComments) text.Append("\nDescription: ").Append(comment);
            foreach (string parameter in BaseParameters) text.Append("\n@").Append(parameter);
        }
        if (ReviewState is not null) text.Append("\nReview: ").Append(ReviewState);
        foreach (string line in Contract) text.Append('\n').Append(line);
        // The entry's own comments and properties close the hover, as before 0.9.
        text.Append('\n').Append(string.Join("\n", Comments)).Append('\n').Append(string.Join("\n", Properties));
        return text.ToString();
    }
}
