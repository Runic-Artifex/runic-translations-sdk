using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler;

// One block-level node of a variant skeleton (W220-003 section 6). Name is the
// skeleton name: the canonical name with the runic: prefix dropped, or "~" for an
// implicit paragraph. Container elements (list-items) always write their children.
internal sealed record Rmf2SkeletonNodeV5(string Name, string Options, IReadOnlyList<Rmf2SkeletonNodeV5> Children, bool Container)
{
    internal string Label => Options.Length == 0 ? Name : Name + "[" + Options + "]";
}

// Skeleton is null for a variant that is not a valid document (inline message, or a
// structural error already reported). An empty document variant has an empty skeleton.
internal sealed record Rmf2DocumentVariantV5(string Kind, IReadOnlyList<Rmf2SkeletonNodeV5>? Skeleton)
{
    internal string? Encoded => Skeleton is null ? null : Rmf2DocumentProfileV5.Encode(Skeleton);
}

internal sealed record Rmf2DocumentAnalysisV5(string Kind, Rmf2MessageV5 Message, IReadOnlyList<Rmf2DocumentVariantV5> Variants)
{
    internal IReadOnlyList<string> Skeletons => Kind != Rmf2DocumentProfileV5.Document ? Array.Empty<string>() :
        Variants.Select(variant => variant.Encoded).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}

// The RMF2 document profile v1 (specs/translations/rmf2-document-profile-v1.md):
// content-kind inference, nesting rules, limits, whitespace normalization of leaf
// literals, and the locked structure skeleton.
internal static class Rmf2DocumentProfileV5
{
    internal const string Inline = "inline";
    internal const string Document = "document";
    internal const string Empty = "empty";
    internal const int MaximumDepth = 16;
    internal const int MaximumNodes = 4096;

    internal static bool IsBlockLevel(Rmf2MarkupContractV5 contract) => contract.Placement is "block" or "list-item";

    internal static Rmf2DocumentAnalysisV5 Analyze(string key, Rmf2MessageV5 message, string? expectedKind,
        IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts, TextSourceLocation location, DiagnosticBag diagnostics)
    {
        string[] kinds = message.Variants.Select(variant => Classify(variant.Nodes, contracts)).ToArray();
        string kind;
        if (expectedKind is null)
        {
            bool document = kinds.Contains(Document), inline = kinds.Contains(Inline);
            if (document && inline) Report("RTR0070", "Message '" + key + "' mixes inline and document variants; every non-empty variant must have the same content kind.");
            kind = document ? Document : Inline;
        }
        else
        {
            kind = expectedKind;
            if (kinds.Any(variant => variant != Empty && variant != kind))
                Report("RTR0070", "Message '" + key + "' is " + Describe(kind) + " in the base locale, but this translation has " + Describe(kind == Document ? Inline : Document) + " variant.");
        }
        var variants = new List<Rmf2VariantV5>();
        var results = new List<Rmf2DocumentVariantV5>();
        for (int index = 0; index < message.Variants.Count; index++)
        {
            Rmf2VariantV5 variant = message.Variants[index];
            if (kind == Document && kinds[index] == Empty)
            {
                // Kind-neutral whitespace and bidi marks only: a document with zero blocks.
                variants.Add(variant with { Nodes = Array.Empty<Rmf2NodeV5>() });
                results.Add(new(Empty, Array.Empty<Rmf2SkeletonNodeV5>()));
            }
            else if (kind == Document && kinds[index] == Document)
            {
                var builder = new DocumentBuilder(key, contracts, location, diagnostics, expectedKind is null);
                IReadOnlyList<Rmf2SkeletonNodeV5>? skeleton = builder.Build(variant.Nodes);
                variants.Add(skeleton is null ? variant : variant with { Nodes = builder.Output.AsReadOnly() });
                results.Add(new(Document, skeleton));
            }
            else
            {
                if (kinds[index] == Inline) NestedBlocks(variant.Nodes);
                variants.Add(variant);
                results.Add(new(kinds[index], null));
            }
        }
        return new(kind, message with { Variants = variants.AsReadOnly() }, results.AsReadOnly());

        void NestedBlocks(IReadOnlyList<Rmf2NodeV5> nodes)
        {
            foreach (Rmf2MarkupV5 tag in nodes.OfType<Rmf2MarkupV5>())
                if (tag.MarkupKind != "close" && contracts.TryGetValue(tag.Name, out var contract) && IsBlockLevel(contract))
                {
                    Report("RTR0072", "Block element '" + SkeletonName(tag.Name) + "' cannot appear inside inline content in '" + key + "'.");
                    return;
                }
        }
        void Report(string id, string text) => diagnostics.Add(id, TranslationDiagnosticSeverity.Error, text, location);
        static string Describe(string value) => value == Document ? "a document" : "an inline";
    }

    // Classifies one variant from its root: a block-level element at the root makes a
    // document; whitespace and bidi marks only make an empty, kind-neutral variant.
    internal static string Classify(IReadOnlyList<Rmf2NodeV5> nodes, IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts)
    {
        int depth = 0;
        bool block = false, content = false;
        foreach (Rmf2NodeV5 node in nodes)
        {
            switch (node)
            {
                case Rmf2TextV5 text:
                    if (!IsBlockWhitespace(text.Value)) content = true;
                    break;
                case Rmf2ExpressionNodeV5:
                    content = true;
                    break;
                case Rmf2MarkupV5 tag:
                    if (tag.MarkupKind == "close") { depth = Math.Max(0, depth - 1); break; }
                    content = true;
                    if (depth == 0 && contracts.TryGetValue(tag.Name, out var contract) && IsBlockLevel(contract)) block = true;
                    if (tag.MarkupKind == "open") depth++;
                    break;
            }
        }
        return block ? Document : content ? Inline : Empty;
    }

    // MF2 whitespace (space, tab, CR, LF, U+3000) and MF2 bidi marks.
    internal static bool IsBlockWhitespace(string text)
    {
        foreach (char value in text)
            if (value is not (' ' or '\t' or '\r' or '\n' or '\u3000' or '\u061C' or '\u200E' or '\u200F' or (>= '\u2066' and <= '\u2069'))) return false;
        return true;
    }

    internal static string SkeletonName(string canonical) => canonical.StartsWith("runic:", StringComparison.Ordinal) ? canonical.Substring(6) : canonical;

    internal static string Encode(IReadOnlyList<Rmf2SkeletonNodeV5> nodes) => string.Join(",", nodes.Select(Encode));

    private static string Encode(Rmf2SkeletonNodeV5 node) =>
        node.Label + (node.Container ? "(" + Encode(node.Children) + ")" : string.Empty);

    // Every option after defaulting, in ordinal key order; literals use their canonical
    // text with the skeleton metacharacters escaped, variables are written as $input.
    internal static string EncodeOptions(IReadOnlyList<Rmf2OptionV5> options)
    {
        var builder = new StringBuilder();
        foreach (Rmf2OptionV5 option in options.OrderBy(option => option.Name, StringComparer.Ordinal))
        {
            if (builder.Length != 0) builder.Append(';');
            builder.Append(option.Name).Append('=');
            if (option.Value.Kind is "input" or "local") { builder.Append('$').Append(option.Value.Value); continue; }
            string value = option.Value.Canonical ?? option.Value.Value;
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (current is '\\' or '[' or ']' or '(' or ')' or ',' or ';' or '=' || (index == 0 && current == '$')) builder.Append('\\');
                builder.Append(current);
            }
        }
        return builder.ToString();
    }

    // The first differing skeleton path (section 6), searched against every source
    // skeleton; the closest source is the one that matches the longest preorder prefix.
    internal static (string Path, string Expected, string Found) FirstDifference(
        IEnumerable<IReadOnlyList<Rmf2SkeletonNodeV5>> sources, IReadOnlyList<Rmf2SkeletonNodeV5> translated)
    {
        (string Path, string Expected, string Found) best = ("", "", "");
        int bestPosition = -1;
        foreach (IReadOnlyList<Rmf2SkeletonNodeV5> source in sources)
        {
            int position = 0;
            var difference = Compare(source, translated, "", ref position);
            if (difference is { } found && position > bestPosition) { best = found; bestPosition = position; }
        }
        return best;

        static (string, string, string)? Compare(IReadOnlyList<Rmf2SkeletonNodeV5> expected, IReadOnlyList<Rmf2SkeletonNodeV5> actual, string parent, ref int position)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int index = 0; index < Math.Max(expected.Count, actual.Count); index++)
            {
                Rmf2SkeletonNodeV5? left = index < expected.Count ? expected[index] : null, right = index < actual.Count ? actual[index] : null;
                string name = (left ?? right)!.Name;
                counts[name] = counts.GetValueOrDefault(name) + 1;
                string path = (parent.Length == 0 ? "" : parent + "/") + name + "[" + counts[name] + "]";
                if (left is null || right is null || left.Name != right.Name || left.Options != right.Options || left.Container != right.Container)
                    return (path, left?.Label ?? "nothing", right?.Label ?? "nothing");
                position++;
                var nested = Compare(left.Children, right.Children, path, ref position);
                if (nested is not null) return nested;
            }
            return null;
        }
    }

    private sealed class Frame(string? name, string model, string path, SkeletonBuilder? skeleton)
    {
        internal string? Name { get; } = name;
        // root | list (list-items) | leaf (inline children of p, h, li) | inline (inline markup)
        internal string Model { get; } = model;
        internal string Path { get; } = path;
        internal SkeletonBuilder? Skeleton { get; } = skeleton;
        internal Dictionary<string, int> Counts { get; } = new(StringComparer.Ordinal);
    }

    private sealed class SkeletonBuilder(string name, string options, bool container)
    {
        internal List<SkeletonBuilder> Children { get; } = new();
        internal Rmf2SkeletonNodeV5 Build() => new(name, options, Children.Select(child => child.Build()).ToArray(), container);
    }

    // source is true for the base locale. Heading levels are locked with the structure, so
    // RTR0077 is reported for the source only: a translator could not fix it.
    private sealed class DocumentBuilder(string key, IReadOnlyDictionary<string, Rmf2MarkupContractV5> contracts,
        TextSourceLocation location, DiagnosticBag diagnostics, bool source)
    {
        internal List<Rmf2NodeV5> Output { get; } = new();

        internal IReadOnlyList<Rmf2SkeletonNodeV5>? Build(IReadOnlyList<Rmf2NodeV5> nodes)
        {
            var root = new Frame(null, "root", "", null);
            var roots = new List<SkeletonBuilder>();
            var frames = new Stack<Frame>();
            frames.Push(root);
            List<Rmf2NodeV5>? leaf = null;
            int count = 0, heading = 0;
            foreach (Rmf2NodeV5 node in nodes)
            {
                Frame top = frames.Peek();
                bool blockLevel = top.Model is "root" or "list";
                if (node is Rmf2TextV5 text)
                {
                    count++;
                    if (!blockLevel) leaf!.Add(text);
                    else if (!IsBlockWhitespace(text.Value)) return Fail(top, "Text must be inside a block such as p in '" + key + "'" + At(top) + ".");
                }
                else if (node is Rmf2ExpressionNodeV5)
                {
                    count++;
                    if (blockLevel) return Fail(top, "A placeholder must be inside a block such as p in '" + key + "'" + At(top) + ".");
                    leaf!.Add(node);
                }
                else if (node is Rmf2MarkupV5 tag)
                {
                    if (!contracts.TryGetValue(tag.Name, out var contract)) return null;
                    if (tag.MarkupKind == "close")
                    {
                        if (top.Name != tag.Name || frames.Count == 1) return null;
                        frames.Pop();
                        if (top.Model == "inline") { leaf!.Add(tag); continue; }
                        if (top.Model == "leaf")
                        {
                            if (!Normalize(leaf!, top)) Warning("RTR0076", "Empty block '" + SkeletonName(tag.Name) + "' at '" + top.Path + "' in '" + key + "'.");
                            leaf = null;
                        }
                        else if (top.Skeleton!.Children.Count == 0) Warning("RTR0076", "Empty list '" + SkeletonName(tag.Name) + "' at '" + top.Path + "' in '" + key + "'.");
                        Output.Add(tag);
                        continue;
                    }
                    count++;
                    if (contract.Placement == "inline")
                    {
                        if (blockLevel) return Fail(top, "Inline markup '" + SkeletonName(tag.Name) + "' must be inside a block such as p in '" + key + "'" + At(top) + ".");
                        leaf!.Add(tag);
                        if (tag.MarkupKind == "open") frames.Push(new Frame(tag.Name, "inline", top.Path, null));
                    }
                    else
                    {
                        string name = SkeletonName(tag.Name);
                        if (contract.Placement == "list-item" && top.Model != "list")
                            return Fail(top, "'" + name + "' can only appear directly inside ul or ol in '" + key + "'" + At(top) + ".", "RTR0072");
                        if (contract.Placement == "block" && top.Model != "root")
                            return Fail(top, (top.Model == "list" ? "Only li can appear inside a list" : "Block element '" + name + "' cannot appear inside inline content or a list item") + " in '" + key + "'" + At(top) + ".", "RTR0072");
                        top.Counts[name] = top.Counts.GetValueOrDefault(name) + 1;
                        string path = (top.Path.Length == 0 ? "" : top.Path + "/") + name + "[" + top.Counts[name] + "]";
                        bool list = contract.Children == "list-items";
                        var skeleton = new SkeletonBuilder(name, EncodeOptions(tag.Options), list);
                        (top.Skeleton?.Children ?? roots).Add(skeleton);
                        if (tag.Name == "runic:h" && tag.Options.FirstOrDefault(option => option.Name == "level")?.Value is { Kind: "number-literal" } level &&
                            int.TryParse(level.Value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int value))
                        {
                            if (source && value > heading + 1)
                                Warning("RTR0077", heading == 0
                                    ? "The first heading in '" + key + "' has level " + value + " at '" + path + "'; heading levels are relative to the message and should start at 1."
                                    : "Heading level skips from " + heading + " to " + value + " at '" + path + "' in '" + key + "'.");
                            heading = value;
                        }
                        Output.Add(tag);
                        if (tag.MarkupKind == "open")
                        {
                            frames.Push(new Frame(tag.Name, list ? "list" : "leaf", path, skeleton));
                            if (!list) leaf = new List<Rmf2NodeV5>();
                        }
                    }
                    if (frames.Count - 1 + (tag.MarkupKind == "standalone" ? 1 : 0) > MaximumDepth)
                        return Fail(frames.Peek(), "Document nesting exceeds " + MaximumDepth + " element levels in '" + key + "'" + At(frames.Peek()) + ".", "RTR0073");
                }
                if (count > MaximumNodes) return Fail(frames.Peek(), "Document exceeds " + MaximumNodes + " nodes in '" + key + "'" + At(frames.Peek()) + ".", "RTR0073");
            }
            if (frames.Count != 1) return null;
            return roots.Select(item => item.Build()).ToArray();
        }

        // Section 5, rule 2, over the leaf's flattened literal sequence. Returns false for
        // an empty leaf (no atom and no character left).
        private bool Normalize(List<Rmf2NodeV5> leaf, Frame frame)
        {
            var tokens = new List<(int Node, int Index, bool Atom, bool Break)>();
            for (int node = 0; node < leaf.Count; node++)
            {
                if (leaf[node] is Rmf2TextV5 text) for (int index = 0; index < text.Value.Length; index++) tokens.Add((node, index, false, false));
                else if (leaf[node] is Rmf2ExpressionNodeV5) tokens.Add((node, -1, true, false));
                else if (leaf[node] is Rmf2MarkupV5 { MarkupKind: "standalone" } atom) tokens.Add((node, -1, true, atom.Name == "runic:br"));
            }
            var removed = new bool[tokens.Count];
            var spaced = new bool[tokens.Count];
            bool southeastAsian = false;
            for (int position = 0; position < tokens.Count;)
            {
                if (!LineSpace(position)) { position++; continue; }
                int start = position;
                bool newline = false;
                while (position < tokens.Count && LineSpace(position))
                {
                    newline |= Character(position) is '\r' or '\n';
                    position++;
                }
                int end = position - 1;
                // Leading and trailing runs go; runs without a line break stay verbatim; a
                // line break next to br goes; any other line break is a segment break.
                if (start != 0 && end != tokens.Count - 1 && !newline) continue;
                bool delete = start == 0 || end == tokens.Count - 1 || tokens[start - 1].Break || tokens[end + 1].Break;
                if (!delete)
                {
                    int? left = tokens[start - 1].Atom ? null : CodePointBefore(start - 1), right = tokens[end + 1].Atom ? null : CodePointAt(end + 1);
                    delete = left == 0x200B || right == 0x200B ||
                        (left is { } l && right is { } r && Rmf2EastAsianWidthV1.IsFullWidthWideOrHalfWidth(l) && Rmf2EastAsianWidthV1.IsFullWidthWideOrHalfWidth(r) && !Hangul(l) && !Hangul(r));
                    if (!delete)
                    {
                        // The replacement goes in the first segment of the run.
                        spaced[start] = true;
                        for (int index = start + 1; index <= end; index++) removed[index] = true;
                        if (left is { } a && right is { } b && SoutheastAsian(a) && SoutheastAsian(b)) southeastAsian = true;
                        continue;
                    }
                }
                for (int index = start; index <= end; index++) removed[index] = true;
            }
            if (southeastAsian)
                Warning("RTR0078", "A line break between Thai, Lao, Khmer or Myanmar characters became a space at '" + frame.Path + "' in '" + key + "'; join the lines if no space is intended.");
            var values = new StringBuilder?[leaf.Count];
            for (int index = 0; index < tokens.Count; index++)
            {
                var token = tokens[index];
                if (token.Atom || removed[index]) continue;
                (values[token.Node] ??= new StringBuilder()).Append(spaced[index] ? ' ' : Character(index));
            }
            bool content = tokens.Where((token, index) => token.Atom || !removed[index]).Any();
            for (int node = 0; node < leaf.Count; node++)
            {
                if (leaf[node] is not Rmf2TextV5) { Output.Add(leaf[node]); continue; }
                if (values[node] is { Length: > 0 } value) Output.Add(new Rmf2TextV5(value.ToString()));
            }
            return content;

            char Character(int index) => ((Rmf2TextV5)leaf[tokens[index].Node]).Value[tokens[index].Index];
            bool LineSpace(int index) => !tokens[index].Atom && Character(index) is ' ' or '\t' or '\r' or '\n';
            int CodePointBefore(int index)
            {
                string value = ((Rmf2TextV5)leaf[tokens[index].Node]).Value;
                int offset = tokens[index].Index;
                return char.IsLowSurrogate(value[offset]) && offset > 0 && char.IsHighSurrogate(value[offset - 1]) ? char.ConvertToUtf32(value[offset - 1], value[offset]) : value[offset];
            }
            int CodePointAt(int index)
            {
                string value = ((Rmf2TextV5)leaf[tokens[index].Node]).Value;
                int offset = tokens[index].Index;
                return char.IsHighSurrogate(value[offset]) && offset + 1 < value.Length && char.IsLowSurrogate(value[offset + 1]) ? char.ConvertToUtf32(value[offset], value[offset + 1]) : value[offset];
            }
        }

        private IReadOnlyList<Rmf2SkeletonNodeV5>? Fail(Frame frame, string message, string? id = null)
        {
            diagnostics.Add(id ?? (frame.Model == "root" ? "RTR0070" : "RTR0072"), TranslationDiagnosticSeverity.Error, message, location);
            return null;
        }

        private void Warning(string id, string message) => diagnostics.Add(id, TranslationDiagnosticSeverity.Warning, message, location);

        private static string At(Frame frame) => frame.Path.Length == 0 ? " at the document root" : " at '" + frame.Path + "'";
    }

    // Unicode Script=Hangul (Scripts.txt, Unicode 16.0.0).
    internal static bool Hangul(int value) =>
        value is (>= 0x1100 and <= 0x11FF) or (>= 0x302E and <= 0x302F) or (>= 0x3131 and <= 0x318E) or (>= 0x3200 and <= 0x321E) or
            (>= 0x3260 and <= 0x327E) or (>= 0xA960 and <= 0xA97C) or (>= 0xAC00 and <= 0xD7A3) or (>= 0xD7B0 and <= 0xD7C6) or
            (>= 0xD7CB and <= 0xD7FB) or (>= 0xFFA0 and <= 0xFFBE) or (>= 0xFFC2 and <= 0xFFC7) or (>= 0xFFCA and <= 0xFFCF) or
            (>= 0xFFD2 and <= 0xFFD7) or (>= 0xFFDA and <= 0xFFDC);

    // Unicode Script (not Script_Extensions) Thai, Lao, Khmer or Myanmar (Scripts.txt, Unicode 16.0.0).
    internal static bool SoutheastAsian(int value) =>
        value is (>= 0x0E01 and <= 0x0E3A) or (>= 0x0E40 and <= 0x0E5B) or
            (>= 0x0E81 and <= 0x0E82) or 0x0E84 or (>= 0x0E86 and <= 0x0E8A) or (>= 0x0E8C and <= 0x0EA3) or 0x0EA5 or
            (>= 0x0EA7 and <= 0x0EBD) or (>= 0x0EC0 and <= 0x0EC4) or 0x0EC6 or (>= 0x0EC8 and <= 0x0ECE) or (>= 0x0ED0 and <= 0x0ED9) or
            (>= 0x0EDC and <= 0x0EDF) or
            (>= 0x1780 and <= 0x17DD) or (>= 0x17E0 and <= 0x17E9) or (>= 0x17F0 and <= 0x17F9) or (>= 0x19E0 and <= 0x19FF) or
            (>= 0x1000 and <= 0x109F) or (>= 0xA9E0 and <= 0xA9FE) or (>= 0xAA60 and <= 0xAA7F) or (>= 0x116D0 and <= 0x116E3);
}
