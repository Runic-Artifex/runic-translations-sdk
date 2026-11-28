using System;
using System.Collections.Generic;

namespace Runic.Translations.Editor;

/// <summary>How the text of a spelling check is read: a pattern (the text of one variant) or a whole message's MF2
/// source.</summary>
internal enum EditorSpellingTextKind
{
    /// <summary>A pattern: text with placeholders and markup, as the pattern box edits it.</summary>
    Pattern,

    /// <summary>A message's MF2 source: a simple message (a pattern), or a complex one, whose declarations, selectors
    /// and keys are syntax and whose quoted patterns (<c>{{…}}</c>) hold its text.</summary>
    Message,
}

/// <summary>The ranges of an MF2 text that are syntax, not words, and so are not spell checked (D074.10): placeholders
/// and markup (<c>{$name}</c>, <c>{#b}</c>, <c>{/b}</c>, <c>{|literal| :function}</c>), escapes, and in a complex
/// message everything outside its quoted patterns.</summary>
/// <remarks>A lexical scan, not the MF2 parser: it reads a text that is being typed, so an unclosed placeholder skips
/// the rest of the text rather than failing. Offsets are UTF-16 code units, as the page's strings count them. The
/// ranges are in order and do not overlap.</remarks>
internal static class EditorSpellingRanges
{
    internal static IReadOnlyList<(int Start, int Length)> Skip(string text, EditorSpellingTextKind kind)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ranges = new List<(int Start, int Length)>();
        if (kind == EditorSpellingTextKind.Message && IsComplexMessage(text)) SkipComplexMessage(text, ranges);
        else SkipPattern(text, 0, text.Length, closeQuoted: false, ranges);
        return Merge(ranges);
    }

    // MF2: a complex message starts, after optional whitespace, with a declaration or keyword (.input, .local, .match)
    // or a quoted pattern.
    private static bool IsComplexMessage(string text)
    {
        int index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        if (index >= text.Length) return false;
        return text[index] == '.' || (text[index] == '{' && index + 1 < text.Length && text[index + 1] == '{');
    }

    private static void SkipComplexMessage(string text, List<(int Start, int Length)> ranges)
    {
        int syntaxStart = 0;
        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '{' && index + 1 < text.Length && text[index + 1] == '{')
            {
                ranges.Add((syntaxStart, index + 2 - syntaxStart));
                int end = SkipPattern(text, index + 2, text.Length, closeQuoted: true, ranges);
                // The closing }} (when there is one) is syntax again.
                syntaxStart = end;
                index = end;
                continue;
            }
            if (current == '{') { index = ExpressionEnd(text, index); continue; }
            if (current == '|') { index = LiteralEnd(text, index); continue; }
            index++;
        }
        if (syntaxStart < text.Length) ranges.Add((syntaxStart, text.Length - syntaxStart));
    }

    /// <summary>Adds the syntax ranges of the pattern from <paramref name="start"/>; returns where it ends: the end of
    /// the text, or for a quoted pattern the index of its closing <c>}}</c>.</summary>
    private static int SkipPattern(string text, int start, int end, bool closeQuoted, List<(int Start, int Length)> ranges)
    {
        int index = start;
        while (index < end)
        {
            char current = text[index];
            if (current == '\\')
            {
                int length = Math.Min(2, end - index);
                ranges.Add((index, length));
                index += length;
                continue;
            }
            if (closeQuoted && current == '}' && index + 1 < end && text[index + 1] == '}') return index;
            if (current == '{')
            {
                int expressionEnd = ExpressionEnd(text, index);
                ranges.Add((index, expressionEnd - index));
                index = expressionEnd;
                continue;
            }
            index++;
        }
        return end;
    }

    /// <summary>The index after the <c>}</c> that closes the placeholder or markup at <paramref name="start"/>, or the
    /// end of the text when it is not closed yet.</summary>
    private static int ExpressionEnd(string text, int start)
    {
        int index = start + 1;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '\\') { index += 2; continue; }
            if (current == '|') { index = LiteralEnd(text, index); continue; }
            if (current == '}') return index + 1;
            index++;
        }
        return text.Length;
    }

    private static int LiteralEnd(string text, int start)
    {
        int index = start + 1;
        while (index < text.Length)
        {
            if (text[index] == '\\') { index += 2; continue; }
            if (text[index] == '|') return index + 1;
            index++;
        }
        return text.Length;
    }

    private static List<(int Start, int Length)> Merge(List<(int Start, int Length)> ranges)
    {
        var merged = new List<(int Start, int Length)>(ranges.Count);
        foreach (var (start, length) in ranges)
        {
            if (length <= 0) continue;
            if (merged.Count > 0 && merged[^1].Start + merged[^1].Length >= start)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Start + last.Length, start + length) - last.Start);
            }
            else merged.Add((start, length));
        }
        return merged;
    }
}
