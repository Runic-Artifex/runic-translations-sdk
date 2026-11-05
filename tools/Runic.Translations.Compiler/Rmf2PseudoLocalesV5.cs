using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

/// <summary>
/// The development pseudo-locales of <c>runic.json</c> <c>pseudoLocales</c>: each is generated from the base locale's
/// sources before linking, as if <c>&lt;tag&gt;.rmf2</c> files existed beside the base files, with the base locale as
/// fallback. Only the text of each pattern changes; placeholders, markup, declarations, selectors and keys keep their
/// source spelling, so the pseudo messages have the base caller contract and compile like any translation.
/// </summary>
/// <remarks>
/// <c>en-XA</c> accents ASCII letters, doubles vowels and pads with <c>~</c> so that each pattern's text is at least
/// 35% longer than the base text, and brackets it: <c>Save {$name}</c> becomes <c>[Śààṽéé {$name}~]</c>.
/// <c>ar-XB</c> is a right-to-left locale (its language is Arabic) whose text runs are reversed with a right-to-left
/// override (U+202E ... U+202C) and bracketed, so layout mirroring and hard-coded left-to-right text stand out.
/// </remarks>
internal static class Rmf2PseudoLocalesV5
{
    internal const string Accented = "en-XA";
    internal const string Mirrored = "ar-XB";
    internal static readonly string[] Supported = [Accented, Mirrored];

    private const char RightToLeftOverride = '‮';
    private const char PopDirectionalFormatting = '‬';
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Single precomposed code points, as in the editor's pseudo-localization preview.
    private static readonly Dictionary<char, char> Letters = new()
    {
        ['a'] = 'à', ['b'] = 'ḃ', ['c'] = 'ć', ['d'] = 'ď', ['e'] = 'é', ['f'] = 'ḟ', ['g'] = 'ĝ', ['h'] = 'ĥ', ['i'] = 'ï',
        ['j'] = 'ĵ', ['k'] = 'ķ', ['l'] = 'ĺ', ['m'] = 'ḿ', ['n'] = 'ń', ['o'] = 'ö', ['p'] = 'ṕ', ['q'] = 'ǫ', ['r'] = 'ŕ',
        ['s'] = 'ś', ['t'] = 'ť', ['u'] = 'ü', ['v'] = 'ṽ', ['w'] = 'ŵ', ['x'] = 'ẋ', ['y'] = 'ÿ', ['z'] = 'ź',
        ['A'] = 'À', ['B'] = 'Ḃ', ['C'] = 'Ć', ['D'] = 'Ď', ['E'] = 'É', ['F'] = 'Ḟ', ['G'] = 'Ĝ', ['H'] = 'Ĥ', ['I'] = 'Ï',
        ['J'] = 'Ĵ', ['K'] = 'Ķ', ['L'] = 'Ĺ', ['M'] = 'Ḿ', ['N'] = 'Ń', ['O'] = 'Ö', ['P'] = 'Ṕ', ['Q'] = 'Ǫ', ['R'] = 'Ŕ',
        ['S'] = 'Ś', ['T'] = 'Ť', ['U'] = 'Ü', ['V'] = 'Ṽ', ['W'] = 'Ŵ', ['X'] = 'Ẋ', ['Y'] = 'Ÿ', ['Z'] = 'Ź',
    };

    /// <summary>
    /// The sources with a generated file for each pseudo-locale beside every base-locale file: <c>&lt;dir&gt;/&lt;tag&gt;.rmf2</c>
    /// for grouped resources, <c>&lt;root&gt;/&lt;tag&gt;/&lt;id&gt;.mf2</c> for direct ones. A base file that cannot be
    /// read is skipped; linking reports it.
    /// </summary>
    internal static TranslationSource[] Synthesize(TranslationSource[] sources, string baseLocale, IReadOnlyList<string> pseudoLocales,
        TranslationCompilerOptions options)
    {
        var existing = new HashSet<string>(sources.Select(static source => source.Path), StringComparer.OrdinalIgnoreCase);
        var result = new List<TranslationSource>(sources);
        foreach (TranslationSource source in sources)
        {
            string path = source.Path.Replace('\\', '/');
            int slash = path.LastIndexOf('/');
            string directory = slash < 0 ? string.Empty : path[..(slash + 1)];
            string file = path[(slash + 1)..];
            if (file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(file[..^".rmf2".Length], baseLocale, StringComparison.OrdinalIgnoreCase))
            {
                Rmf2ResourceDocument document = Rmf2ResourceReader.Read(source, options);
                if (!document.Success) continue;
                foreach (string tag in pseudoLocales)
                {
                    string target = directory + tag + ".rmf2";
                    if (existing.Add(target)) result.Add(new TranslationSource(target, StrictUtf8.GetBytes(Render(document, tag, options))));
                }
            }
            else if (file.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) && slash > 0)
            {
                int parent = path.LastIndexOf('/', slash - 1);
                string localeDirectory = path[(parent + 1)..slash];
                if (!string.Equals(localeDirectory, baseLocale, StringComparison.OrdinalIgnoreCase)) continue;
                string message = StrictUtf8.GetString(source.GetUtf8Bytes());
                // One terminal newline of a direct file is storage framing.
                bool newline = message.EndsWith('\n');
                string body = newline ? message[..^1].TrimEnd('\r') : message;
                foreach (string tag in pseudoLocales)
                {
                    string target = path[..(parent + 1)] + tag + "/" + file;
                    if (existing.Add(target))
                        result.Add(new TranslationSource(target, StrictUtf8.GetBytes(Transform(body, tag, options) + (newline ? "\n" : string.Empty))));
                }
            }
        }
        result.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return result.ToArray();
    }

    /// <summary>
    /// The pseudo-locale message of a base message: every pattern's text transformed and bracketed. In a document message
    /// (block markup such as <c>{#p}</c> at the root) each leaf block (<c>p</c>, <c>h</c>, <c>li</c>) is bracketed and
    /// padded instead of the pattern, because text at the document root is not allowed (RTR0070); an empty block or
    /// variant stays empty.
    /// </summary>
    internal static string Transform(string message, string tag, TranslationCompilerOptions? options = null)
    {
        Mf2SyntaxDocument syntax = Mf2SyntaxReader.Read(new TranslationSource("pseudo.mf2", StrictUtf8.GetBytes(message)), options);
        if (!syntax.Success) return message;
        bool mirrored = string.Equals(tag, Mirrored, StringComparison.OrdinalIgnoreCase);
        if (syntax.Expressions.Any(static expression => Rmf2DocumentSyntax.BlockName(expression.MarkupName) is not null))
            return TransformDocument(syntax, mirrored);
        bool quoted = syntax.Tokens.Any(static token => token.Kind == Mf2SyntaxTokenKind.PatternStart);
        var output = new StringBuilder(message.Length * 2);
        var pattern = new PatternState();
        if (!quoted) output.Append('[');
        bool inPattern = !quoted;
        foreach (Mf2SyntaxToken token in syntax.Tokens)
        {
            if (quoted && token.Kind == Mf2SyntaxTokenKind.PatternStart)
            {
                output.Append(token.Raw).Append('[');
                pattern = new PatternState();
                inPattern = true;
            }
            else if (quoted && token.Kind == Mf2SyntaxTokenKind.PatternEnd)
            {
                output.Append(pattern.Padding(mirrored)).Append(']').Append(token.Raw);
                inPattern = false;
            }
            else if (inPattern && token.Kind == Mf2SyntaxTokenKind.Text)
                output.Append(mirrored ? Mirror(token.Raw) : Accent(token.Raw, pattern));
            else
                output.Append(token.Raw);
        }
        if (!quoted) output.Append(pattern.Padding(mirrored)).Append(']');
        return output.ToString();
    }

    // A document message: the text of each leaf block is transformed and bracketed inside the block; text outside the
    // leaf blocks (whitespace between blocks and list items) and everything else keep their source spelling.
    private static string TransformDocument(Mf2SyntaxDocument syntax, bool mirrored)
    {
        // The leaf block tags by the byte offset where an open tag ends and where a close tag starts.
        var opens = new HashSet<int>();
        var closes = new HashSet<int>();
        foreach (Mf2ExpressionSyntax expression in syntax.Expressions)
        {
            if (Rmf2DocumentSyntax.BlockName(expression.MarkupName) is not ("runic:p" or "runic:h" or "runic:li")) continue;
            if (expression.MarkupKind == Mf2MarkupKind.Open) opens.Add(expression.Location.StartByte + expression.Location.LengthBytes);
            else if (expression.MarkupKind == Mf2MarkupKind.Close) closes.Add(expression.Location.StartByte);
        }

        var output = new StringBuilder();
        PatternState? leaf = null;
        int leafStart = 0;
        bool content = false;
        foreach (Mf2SyntaxToken token in syntax.Tokens)
        {
            if (leaf is not null && token.Kind == Mf2SyntaxTokenKind.ExpressionStart && closes.Contains(token.Location.StartByte))
            {
                if (content)
                {
                    output.Insert(leafStart, '[');
                    output.Append(leaf.Padding(mirrored)).Append(']');
                }
                leaf = null;
            }

            if (leaf is not null && token.Kind == Mf2SyntaxTokenKind.Text)
            {
                if (!Rmf2DocumentProfileV5.IsBlockWhitespace(token.Raw)) content = true;
                output.Append(mirrored ? Mirror(token.Raw) : Accent(token.Raw, leaf));
            }
            else
            {
                // A placeholder or inline markup inside the leaf is content too, as for the document profile.
                if (leaf is not null && token.Kind == Mf2SyntaxTokenKind.ExpressionStart) content = true;
                output.Append(token.Raw);
            }

            if (leaf is null && token.Kind == Mf2SyntaxTokenKind.ExpressionEnd &&
                opens.Contains(token.Location.StartByte + token.Location.LengthBytes))
            {
                leaf = new PatternState();
                leafStart = output.Length;
                content = false;
            }
        }
        return output.ToString();
    }

    private sealed class PatternState
    {
        internal int Characters;
        internal int Added;

        // At least 35% longer than the base text, the brackets included.
        internal string Padding(bool mirrored)
        {
            if (mirrored || Characters == 0) return string.Empty;
            int target = (int)Math.Ceiling(Characters * 1.35);
            int missing = target - (Characters + Added + 2);
            return missing > 0 ? new string('~', missing) : string.Empty;
        }
    }

    private static string Accent(string raw, PatternState pattern)
    {
        var text = new StringBuilder(raw.Length * 2);
        for (int index = 0; index < raw.Length; index++)
        {
            char value = raw[index];
            if (value == '\\' && index + 1 < raw.Length)
            {
                // An escape (\{, \}, \\, \|) stays as written and counts as one character.
                text.Append(value).Append(raw[index + 1]);
                index++;
                pattern.Characters++;
                continue;
            }
            pattern.Characters++;
            if (!Letters.TryGetValue(value, out char accented)) { text.Append(value); continue; }
            text.Append(accented);
            if (value is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U')
            {
                text.Append(accented);
                pattern.Added++;
            }
        }
        return text.ToString();
    }

    // A run of text displays reversed under a right-to-left override; whitespace between placeholders stays as it is.
    private static string Mirror(string raw) =>
        raw.Trim().Length == 0 ? raw : RightToLeftOverride + raw + PopDirectionalFormatting;

    private static string Render(Rmf2ResourceDocument document, string tag, TranslationCompilerOptions options)
    {
        var root = new Group();
        foreach (Rmf2ResourceNode node in document.Nodes)
        {
            if (node.IsGroup || node.Message is null) continue;
            Group current = root;
            for (int index = 0; index < node.Path.Count - 1; index++) current = current.Child(node.Path[index]);
            current.Entries.Add((node.Path[^1], Transform(node.Message, tag, options), null));
        }
        var text = new StringBuilder("# Generated pseudo-locale ").Append(tag).Append(" (runic.json pseudoLocales); not a source file.\n\n");
        Write(root, 0, text);
        return text.ToString();
    }

    private sealed class Group
    {
        internal List<(string Name, string? Message, Group? Group)> Entries { get; } = [];

        internal Group Child(string name)
        {
            foreach ((string entry, _, Group? group) in Entries)
                if (group is not null && entry == name) return group;
            var child = new Group();
            Entries.Add((name, null, child));
            return child;
        }
    }

    private static void Write(Group group, int depth, StringBuilder text)
    {
        string padding = new(' ', depth * 2);
        foreach ((string name, string? message, Group? child) in group.Entries)
        {
            if (child is not null)
            {
                text.Append(padding).Append(name).Append(" {\n");
                Write(child, depth + 1, text);
                text.Append(padding).Append("}\n");
                continue;
            }
            string body = message!.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!body.Contains('\n')) text.Append(padding).Append(name).Append(" = ").Append(body).Append('\n');
            else
            {
                text.Append(padding).Append(name).Append(" =\n");
                foreach (string line in body.Split('\n')) text.Append(padding).Append("  ").Append(line).Append('\n');
            }
        }
    }
}
