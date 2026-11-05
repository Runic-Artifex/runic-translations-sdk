using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>
/// A message's review state for hover, from the SDK review file (<c>&lt;catalog&gt;.review.json</c> beside runic.json)
/// through Tooling: the recorded state, and whether the source changed since it was recorded (the per-unit fingerprint,
/// RTR0090). The fingerprints come from the review compilation (<see cref="TranslationReviewFile.ForReview"/>), so the
/// state shows while translations are incomplete.
/// </summary>
internal static class Rmf2ReviewFileState
{
    /// <summary>A short state such as <c>approved</c> or <c>needs-review, stale …</c>, or null when unknown.</summary>
    internal static string? Describe(string? baseLocale, IReadOnlyDictionary<string, string>? fingerprints, TranslationInterchangeReview? file, string? error,
        string key, string locale)
    {
        if (fingerprints is null || baseLocale is null || locale == baseLocale) return null;
        if (error is not null) return "unknown (the review file cannot be read: " + error + ")";
        TranslationInterchangeReviewEntry? entry = file?.Entries.FirstOrDefault(item => item.Key == key && item.Locale == locale);
        if (entry is null) return null;
        string state = TranslationReviewFile.StateText(entry.State);
        return TranslationReviewFile.IsStale(entry, fingerprints)
            ? state + ", stale: the source changed since it was recorded (RTR0090)" : state;
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
