using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Runic.Translations.Compiler.Generation;

// messages.d.ts.map: a source map v3 declaration map from each message member (and each of its inputs) in messages.d.ts
// to the message's entry in the base locale's source, so Go to Definition on m.key() in a TypeScript-aware editor opens
// the .rmf2/.mf2 line. The target is the resource the base locale resolves for the key (the one whose text the hover
// shows): with application overrides of a library catalog, the override that replaces it.
//
// tsserver looks a position up by binary search and, without an exact hit, takes the next mapping; a generated-only
// segment there stops it. So every line starts with a generated-only segment, each name has a mapped segment followed by a
// generated-only one, and positions other than a mapped name stay in messages.d.ts instead of jumping to the next message.
internal static class Rmf2DeclarationMapV5
{
    /// <summary>A message key or input name in the declarations: its 0-based line and column, its length, and its message.</summary>
    internal readonly record struct Name(int Line, int Column, int Length, string Key);

    /// <summary>
    /// The map JSON, or null when no message has a source <paramref name="source"/> can name. <paramref name="source"/> turns a
    /// source location path into the map's <c>sources</c> entry (relative to the map), or null to leave that source out.
    /// </summary>
    internal static string? Render(Rmf2ProjectV5 project, string file, string declarations, IReadOnlyList<Name> names, Func<string, string?> source)
    {
        Rmf2LocaleV5? locale = project.Locales.FirstOrDefault(item => item.Tag == project.DefaultLocale);
        if (locale is null) return null;
        var resources = new Dictionary<string, Rmf2TranslationV5>(StringComparer.Ordinal);
        foreach (Rmf2TranslationV5 resource in locale.ResolvedResources) resources.TryAdd(resource.Key, resource);

        var paths = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? Source(string path)
        {
            if (!paths.TryGetValue(path, out string? mapped)) paths[path] = mapped = source(path);
            return mapped;
        }
        var targets = new List<(Name Name, string Source, int Line, int Column)>();
        foreach (Name name in names)
            if (resources.TryGetValue(name.Key, out Rmf2TranslationV5? resource) && resource.SourceLocation.Line > 0 && Source(resource.SourceLocation.Path) is { } mapped)
                targets.Add((name, mapped, resource.SourceLocation.Line - 1, Math.Max(0, resource.SourceLocation.Column - 1)));
        if (targets.Count == 0) return null;

        string[] sources = targets.Select(item => item.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var sourceIndex = sources.Select((item, index) => (item, index)).ToDictionary(pair => pair.item, pair => pair.index, StringComparer.Ordinal);
        var byLine = targets.GroupBy(item => item.Name.Line).ToDictionary(group => group.Key, group => group.OrderBy(item => item.Name.Column).ToArray());

        string[] lines = declarations.Split('\n');
        int lineCount = declarations.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        var mappings = new StringBuilder();
        int previousSource = 0, previousSourceLine = 0, previousSourceColumn = 0;
        for (int line = 0; line < lineCount; line++)
        {
            if (line != 0) mappings.Append(';');
            int previousColumn = 0; bool first = true;
            void Segment(int column)
            {
                if (!first) mappings.Append(',');
                first = false;
                Vlq(mappings, column - previousColumn); previousColumn = column;
            }
            byLine.TryGetValue(line, out var named);
            if (named is null || named[0].Name.Column != 0) Segment(0);
            foreach (var target in named ?? [])
            {
                Segment(target.Name.Column);
                int index = sourceIndex[target.Source];
                Vlq(mappings, index - previousSource); previousSource = index;
                Vlq(mappings, target.Line - previousSourceLine); previousSourceLine = target.Line;
                Vlq(mappings, target.Column - previousSourceColumn); previousSourceColumn = target.Column;
                int end = target.Name.Column + target.Name.Length;
                if (end < lines[line].Length) Segment(end);
            }
        }

        var json = new StringBuilder("{\"version\":3,\"file\":").Append(GenerationSupport.JsonString(file)).Append(",\"sourceRoot\":\"\",\"sources\":[");
        for (int index = 0; index < sources.Length; index++) json.Append(index == 0 ? "" : ",").Append(GenerationSupport.JsonString(sources[index]));
        return json.Append("],\"names\":[],\"mappings\":").Append(GenerationSupport.JsonString(mappings.ToString())).Append("}\n").ToString();
    }

    private const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    // Base64 VLQ: the sign in the lowest bit, then 5-bit groups, least significant first, with bit 6 as continuation.
    private static void Vlq(StringBuilder builder, int value)
    {
        long remaining = value < 0 ? ((-(long)value) << 1) | 1 : (long)value << 1;
        do
        {
            int digit = (int)(remaining & 31);
            remaining >>= 5;
            if (remaining > 0) digit |= 32;
            builder.Append(Base64[digit]);
        }
        while (remaining > 0);
    }
}
