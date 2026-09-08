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
        var additions = new StringBuilder();
        string newline = Utf8.GetString(original).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (TranslationLocaleEdit edit in edits)
        {
            RequireKey(edit.Key);
            if (!touched.Add(edit.Key)) throw new TranslationAuthoringException($"Message '{edit.Key}' has multiple edits in one document.");
            if (edit.Kind == TranslationLocaleEditKind.Add)
            {
                if (!occupied.Add(edit.Key)) throw new TranslationAuthoringException($"Message '{edit.Key}' already exists.");
                additions.Append(edit.Key).Append(" = ").Append(EncodeValue(edit.Value ?? throw new TranslationAuthoringException("A message value is required."))).Append(newline);
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
                    location = entry.KeyLocation;
                    replacement = Utf8.GetBytes(edit.TargetKey!);
                    break;
                case TranslationLocaleEditKind.Delete:
                    // Leading comments and trailing inline comments belong to the document.
                    // Remove only key=value, retaining all indentation, comments and line endings.
                    location = entry.StatementLocation;
                    replacement = [];
                    break;
                default: throw new TranslationAuthoringException("Unknown locale edit kind.");
            }
            replacements.Add((location.StartByte, location.LengthBytes, replacement));
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
        if (additions.Length != 0)
        {
            if (original.Length != 0 && original[^1] != (byte)'\n') output.Write(Utf8.GetBytes(newline));
            output.Write(Utf8.GetBytes(additions.ToString()));
        }
        byte[] result = output.ToArray();
        var validated = TranslationLocaleReader.Read(new TranslationSource(source.Path, result), locale);
        if (!validated.Success) throw new TranslationAuthoringException("The proposed locale document is invalid: " + string.Join("; ", validated.Diagnostics.Select(item => item.Message)));
        return result;
    }

    public static string EncodeValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _ = Utf8.GetByteCount(value); // Reject unpaired surrogates rather than changing message bytes.
        if (!value.Any(character => character == '\'' || character < ' ' || character == '\u007f')) return "'" + value + "'";
        var result = new StringBuilder("\"");
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < ' ' || character == '\u007f') result.Append("\\u").Append(((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                    else result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    internal static void RequireKey(string key)
    {
        if (string.IsNullOrEmpty(key) || !(key[0] == '_' || char.IsAsciiLetter(key[0])) || key.Any(character => character != '_' && !char.IsAsciiLetterOrDigit(character)))
            throw new TranslationAuthoringException($"Message ID '{key}' must be an identifier.");
    }
}
