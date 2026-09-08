using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

public enum TranslationLocaleEditKind { SetValue, Add, Rename, Delete }
public sealed record TranslationLocaleEdit(TranslationLocaleEditKind Kind, string Key, string? Value = null, string? TargetKey = null);
public sealed record TranslationLocaleFileEdit(string RelativePath, string Locale, string ExpectedRevision, IReadOnlyList<TranslationLocaleEdit> Edits);

/// <summary>Edits validated UTF-8 value/key spans without reserializing the containing document.</summary>
public static class TranslationLocaleWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static byte[] Apply(TranslationSource source, string locale, IEnumerable<TranslationLocaleEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(edits);
        var document = TranslationLocaleReader.Read(source, locale);
        if (!document.Success) throw new TranslationAuthoringException("The locale document is invalid: " + string.Join("; ", document.Diagnostics.Select(item => item.Message)));
        byte[] original = source.GetUtf8Bytes();
        var entries = document.Entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        var touched = new HashSet<string>(StringComparer.Ordinal);
        var occupied = entries.Keys.ToHashSet(StringComparer.Ordinal);
        var replacements = new List<(int Start, int Length, byte[] Bytes)>();
        var additions = new SortedDictionary<int, StringBuilder>();
        var inlineAdditions = new Dictionary<int, List<string>>();
        var inlineRemovals = new Dictionary<int, HashSet<int>>();
        string newline = Utf8.GetString(original).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (TranslationLocaleEdit edit in edits)
        {
            RequireKey(edit.Key);
            if (!touched.Add(edit.Key)) throw new TranslationAuthoringException($"Message '{edit.Key}' has multiple edits in one document.");
            if (edit.Kind == TranslationLocaleEditKind.Add)
            {
                if (!occupied.Add(edit.Key)) throw new TranslationAuthoringException($"Message '{edit.Key}' already exists.");
                AddEntry(edit.Key, EncodeValue(edit.Value ?? throw new TranslationAuthoringException("A message value is required.")));
                continue;
            }
            if (!entries.TryGetValue(edit.Key, out var entry)) throw new TranslationAuthoringException($"Message '{edit.Key}' does not exist.");
            var location = entry.ValueLocation;
            byte[] replacement;
            switch (edit.Kind)
            {
                case TranslationLocaleEditKind.SetValue:
                    string value = edit.Value ?? throw new TranslationAuthoringException("A message value is required.");
                    replacement = string.Equals(Utf8.GetString(entry.Message.GetUtf8Bytes()), value, StringComparison.Ordinal)
                        ? original.AsSpan(location.StartByte, location.LengthBytes).ToArray()
                        : Utf8.GetBytes(EncodeValue(value));
                    break;
                case TranslationLocaleEditKind.Rename:
                    RequireKey(edit.TargetKey ?? string.Empty);
                    if (!occupied.Add(edit.TargetKey!)) throw new TranslationAuthoringException($"Message '{edit.TargetKey}' already exists.");
                    var target = FindTarget(edit.TargetKey!);
                    if (target.Path.SequenceEqual(entry.TablePath.Concat(entry.InlinePath), StringComparer.Ordinal))
                    {
                        string parentPrefix = string.Join('_', entry.TablePath.Concat(entry.InlinePath).Concat(entry.KeyPath.Take(entry.KeyPath.Count - 1)));
                        if (parentPrefix.Length != 0) parentPrefix += "_";
                        location = entry.KeyLocation;
                        replacement = Utf8.GetBytes(edit.TargetKey!.StartsWith(parentPrefix, StringComparison.Ordinal) && IsKey(edit.TargetKey[parentPrefix.Length..])
                            ? string.Join('.', entry.KeyPath.Take(entry.KeyPath.Count - 1).Append(edit.TargetKey[parentPrefix.Length..]))
                            : target.LocalKey);
                    }
                    else
                    {
                        // Moves retain the exact value token; comments stay in their original section.
                        location = entry.StatementLocation;
                        replacement = [];
                        MarkInlineRemoval(entry);
                        AddEntry(edit.TargetKey!, Utf8.GetString(original.AsSpan(entry.ValueLocation.StartByte, entry.ValueLocation.LengthBytes)));
                    }
                    break;
                case TranslationLocaleEditKind.Delete:
                    // Leading comments and trailing inline comments belong to the document.
                    // Remove only key=value, retaining all indentation, comments and line endings.
                    location = entry.StatementLocation;
                    replacement = [];
                    MarkInlineRemoval(entry);
                    break;
                default: throw new TranslationAuthoringException("Unknown locale edit kind.");
            }
            replacements.Add((location.StartByte, location.LengthBytes, replacement));
        }
        foreach (var container in document.InlineTables)
        {
            int identity = container.ValueLocation.StartByte;
            inlineRemovals.TryGetValue(identity, out var removed);
            inlineAdditions.TryGetValue(identity, out var inserted);
            if (removed is null && inserted is null) continue;
            var surviving = container.Members.Where(member => removed is null || !removed.Contains(member.StatementLocation.StartByte)).ToArray();
            foreach (var member in container.Members)
            {
                if (removed is null || !removed.Contains(member.StatementLocation.StartByte) || member.SeparatorLocation is null) continue;
                var comma = member.SeparatorLocation;
                replacements.Add((comma.StartByte, comma.LengthBytes, []));
            }
            if (inserted is not null && inserted.Count != 0)
            {
                int insertion = container.ValueLocation.StartByte + container.ValueLocation.LengthBytes - 1;
                // Keep whitespace preceding the closing brace in its original position.
                while (insertion > container.ValueLocation.StartByte + 1 && (original[insertion - 1] == (byte)' ' || original[insertion - 1] == (byte)'\t')) insertion--;
                bool needsComma = surviving.Length != 0 && surviving[^1].SeparatorLocation is null;
                string text = (needsComma ? ", " : " ") + string.Join(", ", inserted);
                replacements.Add((insertion, 0, Utf8.GetBytes(text)));
            }
        }
        foreach (var addition in additions)
        {
            int insertion = addition.Key;
            string separator = insertion != 0 && original[insertion - 1] != (byte)'\n' ? newline : string.Empty;
            replacements.Add((insertion, 0, Utf8.GetBytes(separator + addition.Value)));
        }
        using var output = new MemoryStream();
        int cursor = 0;
        foreach (var edit in replacements.OrderBy(edit => edit.Start))
        {
            if (edit.Start < cursor || edit.Start + edit.Length > original.Length) throw new TranslationAuthoringException("Locale edit ranges overlap or exceed the document.");
            output.Write(original.AsSpan(cursor, edit.Start - cursor));
            output.Write(edit.Bytes);
            cursor = edit.Start + edit.Length;
        }
        output.Write(original.AsSpan(cursor));
        byte[] result = output.ToArray();
        var validated = TranslationLocaleReader.Read(new TranslationSource(source.Path, result), locale);
        if (!validated.Success) throw new TranslationAuthoringException("The proposed locale document is invalid: " + string.Join("; ", validated.Diagnostics.Select(item => item.Message)));
        return result;

        (IReadOnlyList<string> Path, string LocalKey, int Insertion, TranslationLocaleInlineTable? Inline) FindTarget(string key)
        {
            var candidates = new List<(IReadOnlyList<string> Path, string Prefix, int Insertion, TranslationLocaleInlineTable? Inline, bool IsArrayElement)>();
            foreach (var table in document.Tables)
                candidates.Add((table.Path, string.Join('_', table.Path) + "_", LineStart(table.InsertionByte), null, table.IsArrayElement));
            foreach (var table in document.InlineTables)
                candidates.Add((table.Path, string.Join('_', table.Path) + "_", table.ValueLocation.StartByte + table.ValueLocation.LengthBytes - 1, table, table.IsArrayElement));
            var matches = candidates.Where(item => key.StartsWith(item.Prefix, StringComparison.Ordinal) && IsKey(key[item.Prefix.Length..]))
                .OrderByDescending(item => item.Prefix.Length).ThenByDescending(item => item.Path.Count).ToArray();
            if (matches.Length != 0)
            {
                var best = matches[0];
                // Distinct paths can have the same flattened prefix (for example a_b and a.b).
                if (!matches.Skip(1).Any(item => item.Prefix == best.Prefix && !item.Path.SequenceEqual(best.Path, StringComparer.Ordinal)))
                {
                    string localKey = key[best.Prefix.Length..];
                    if (best.IsArrayElement && localKey == "_id")
                        throw new TranslationAuthoringException("Array-table row _id metadata cannot be edited as a message.");
                    return (best.Path, localKey, best.Insertion, best.Inline);
                }
            }
            return (Array.Empty<string>(), key, LineStart(document.RootInsertionByte), null);
        }

        void MarkInlineRemoval(TranslationLocaleEntry entry)
        {
            if (entry.InlinePath.Count == 0) return;
            var container = document.InlineTables.Single(table => table.Path.SequenceEqual(entry.TablePath.Concat(entry.InlinePath), StringComparer.Ordinal));
            int identity = container.ValueLocation.StartByte;
            if (!inlineRemovals.TryGetValue(identity, out var removed)) inlineRemovals.Add(identity, removed = []);
            removed.Add(entry.StatementLocation.StartByte);
        }

        int LineStart(int insertion)
        {
            int start = insertion;
            while (start > 0 && (original[start - 1] == (byte)' ' || original[start - 1] == (byte)'\t')) start--;
            return start == 0 || original[start - 1] == (byte)'\n' ? start : insertion;
        }

        void AddEntry(string key, string valueToken)
        {
            var target = FindTarget(key);
            if (target.Inline is not null)
            {
                int identity = target.Inline.ValueLocation.StartByte;
                if (!inlineAdditions.TryGetValue(identity, out var inserted)) inlineAdditions.Add(identity, inserted = []);
                inserted.Add(target.LocalKey + " = " + valueToken);
                return;
            }
            if (!additions.TryGetValue(target.Insertion, out var buffer)) additions.Add(target.Insertion, buffer = new StringBuilder());
            buffer.Append(target.LocalKey).Append(" = ").Append(valueToken).Append(newline);
        }
    }

    /// <summary>Renders a new document, grouping identifier prefixes without changing flattened message IDs.</summary>
    public static byte[] Render(IEnumerable<KeyValuePair<string, string>> entries, bool groupByPrefix = true)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var messages = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            RequireKey(entry.Key);
            if (!messages.TryAdd(entry.Key, entry.Value)) throw new TranslationAuthoringException($"Message '{entry.Key}' already exists.");
        }
        var root = new List<KeyValuePair<string, string>>();
        var groups = new SortedDictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        foreach (var entry in messages)
        {
            int split = groupByPrefix ? entry.Key.IndexOf('_') : -1;
            string prefix = split > 0 ? entry.Key[..split] : string.Empty;
            string suffix = split > 0 ? entry.Key[(split + 1)..] : string.Empty;
            bool validSuffix = suffix.Length != 0 && (suffix[0] == '_' || char.IsAsciiLetter(suffix[0]));
            if (prefix.Length == 0 || !validSuffix || messages.ContainsKey(prefix)) root.Add(entry);
            else
            {
                if (!groups.TryGetValue(prefix, out var group)) groups.Add(prefix, group = []);
                group.Add(new(suffix, entry.Value));
            }
        }
        var result = new StringBuilder();
        foreach (var entry in root) Append(entry);
        foreach (var group in groups)
        {
            if (result.Length != 0) result.Append('\n');
            result.Append('[').Append(group.Key).Append("]\n");
            foreach (var entry in group.Value) Append(entry);
        }
        return Utf8.GetBytes(result.ToString());

        void Append(KeyValuePair<string, string> entry) => result.Append(entry.Key).Append(" = ").Append(EncodeValue(entry.Value)).Append('\n');
    }

    public static string EncodeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = Utf8.GetByteCount(value); // Reject unpaired surrogates rather than changing message bytes.
        bool multiline = value.Contains('\n');
        bool literal = !value.Contains(multiline ? "'''" : "'", StringComparison.Ordinal);
        for (int index = 0; index < value.Length && literal; index++)
        {
            char character = value[index];
            if (character == '\u007f' || character < ' ' && character != '\t' &&
                !(multiline && (character == '\n' || character == '\r' && index + 1 < value.Length && value[index + 1] == '\n')))
                literal = false;
        }
        if (literal)
            // The one framing newline is removed by TOML, so leading message newlines survive.
            return multiline ? "'''\n" + value + "'''" : "'" + value + "'";

        var result = new StringBuilder(multiline ? "\"\"\"\n" : "\"");
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\n': result.Append(multiline ? "\n" : "\\n"); break;
                case '\r': result.Append(multiline && index + 1 < value.Length && value[index + 1] == '\n' ? "\r" : "\\r"); break;
                case '\t': result.Append('\t'); break;
                default:
                    if (character < ' ' || character == '\u007f') result.Append("\\u").Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    else result.Append(character);
                    break;
            }
        }
        return result.Append(multiline ? "\"\"\"" : "\"").ToString();
    }

    private static bool IsKey(string key) => !string.IsNullOrEmpty(key) && (key[0] == '_' || char.IsAsciiLetter(key[0])) &&
        key.All(character => character == '_' || char.IsAsciiLetterOrDigit(character));

    internal static void RequireKey(string key)
    {
        if (!IsKey(key))
            throw new TranslationAuthoringException($"Message ID '{key}' must be an identifier.");
    }
}
