using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Runic.Translations.Compiler;

namespace Runic.Translations.Authoring;

/// <summary>Source-preserving resource edits shared by the editor, CLI and language server.</summary>
public static class Rmf2ResourceWriter
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

    public static byte[] SetMessage(TranslationSource source, string key, string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var document = Require(source);
        var node = document.Nodes.SingleOrDefault(n => !n.IsGroup && n.Key == key);
        if (node is null) throw new TranslationAuthoringException("Unknown RMF2 message '" + key + "'.");
        byte[] raw = source.GetUtf8Bytes();
        int from = node.NameLocation.StartByte, end = node.Location.StartByte + node.Location.LengthBytes;
        string newline = Utf8.GetString(raw).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string replacement = Entry(node.Path[^1], message, node.NameLocation.Column - 1, newline).TrimStart(' ');
        return Replace(raw, [(from, end - from, Utf8.GetBytes(replacement))]);
    }

    /// <summary>Adds an explicit segment path without inferring hierarchy from underscore IDs.</summary>
    public static byte[] AddMessage(TranslationSource source, IReadOnlyList<string> path, string message)
    {
        ArgumentNullException.ThrowIfNull(path); ArgumentNullException.ThrowIfNull(message);
        if (path.Count == 0 || path.Any(segment => !Identifier.IsMatch(segment))) throw new TranslationAuthoringException("A message path requires identifier segments.");
        var document = Require(source);
        if (document.Nodes.Any(node => node.Path.SequenceEqual(path, StringComparer.Ordinal))) throw new TranslationAuthoringException("The resource path already exists.");
        var output = new StringBuilder(Utf8.GetString(source.GetUtf8Bytes()));
        string newline = output.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (output.Length > 0 && output[^1] != '\n') output.Append(newline);
        for (int i = 0; i < path.Count - 1; i++) output.Append(' ', i * 2).Append(path[i]).Append(" {").Append(newline);
        output.Append(Entry(path[^1], message, (path.Count - 1) * 2, newline));
        for (int i = path.Count - 2; i >= 0; i--) output.Append(' ', i * 2).Append('}').Append(newline);
        byte[] result = Utf8.GetBytes(output.ToString()); Require(new TranslationSource(source.Path, result)); return result;
    }

    public static byte[] Rename(TranslationSource source, IReadOnlyList<string> path, string name)
    {
        if (!Identifier.IsMatch(name)) throw new TranslationAuthoringException("RMF2 names must be identifiers.");
        var document = Require(source);
        var changes = document.Nodes.Where(n => n.Path.SequenceEqual(path, StringComparer.Ordinal))
            .Select(n => (n.NameLocation.StartByte, n.NameLocation.LengthBytes, Utf8.GetBytes(name))).ToArray();
        if (changes.Length == 0) throw new TranslationAuthoringException("The resource path was not found.");
        byte[] result = Replace(source.GetUtf8Bytes(), changes);
        Require(new TranslationSource(source.Path, result));
        return result;
    }

    /// <summary>Normalizes structural indentation while preserving message text, margins, comments and variant order.</summary>
    public static byte[] Format(TranslationSource source)
    {
        var document = Require(source);
        string text = Utf8.GetString(source.GetUtf8Bytes());
        string[] lines = text.Split('\n');
        var replacement = new Dictionary<int, int>();
        foreach (var node in document.Nodes)
        {
            int desired = (node.Path.Count - 1) * 2;
            for (int line = node.Location.Line - 1; line < node.NameLocation.Line; line++) replacement[line] = desired;
            if (!node.IsGroup)
            {
                int end = node.Location.EndLine - (node.Location.EndColumn == 1 ? 1 : 0);
                for (int line = node.NameLocation.Line; line < end; line++)
                    if (line < lines.Length && lines[line].Trim().Length != 0)
                        replacement[line] = Leading(lines[line]) + desired - (node.NameLocation.Column - 1);
            }
            else
            {
                int close = node.Location.EndLine - (node.Location.EndColumn == 1 ? 2 : 1);
                if (close >= 0 && close < lines.Length && lines[close].Trim() == "}") replacement[close] = desired;
            }
        }
        foreach (var pair in replacement)
            if (pair.Key < lines.Length) lines[pair.Key] = new string(' ', Math.Max(0, pair.Value)) + lines[pair.Key].TrimStart(' ');
        byte[] result = Utf8.GetBytes(string.Join('\n', lines));
        var formatted = Require(new TranslationSource(source.Path, result));
        if (!document.Nodes.Where(n => !n.IsGroup).Select(n => n.Message).SequenceEqual(formatted.Nodes.Where(n => !n.IsGroup).Select(n => n.Message)))
            throw new TranslationAuthoringException("Formatting would change message content.");
        return result;
    }

    /// <summary>Imports the compatible TOML profile; returns explicit notes for trivia that cannot be attached reliably.</summary>
    public static byte[] ImportToml(TranslationSource source, string locale, out IReadOnlyList<string> notes)
    {
        TranslationLocaleDocument document = TranslationLocaleReader.Read(source, locale);
        if (!document.Success) throw new TranslationAuthoringException("Cannot migrate invalid TOML.");
        var output = new StringBuilder();
        var messages = new List<string>();
        string original = Utf8.GetString(source.GetUtf8Bytes());
        if (original.Contains('#', StringComparison.Ordinal)) messages.Add("TOML comment ownership cannot be inferred; the transaction retains the complete original as .toml.bak.");
        foreach (var entry in document.Entries)
        {
            string[] path = entry.TablePath.Concat(entry.InlinePath).Concat(entry.KeyPath).ToArray();
            for (int i = 0; i < path.Length - 1; i++) output.Append(' ', i * 2).Append(path[i]).Append(" {\n");
            output.Append(Entry(path[^1], Utf8.GetString(entry.Message.GetUtf8Bytes()), (path.Length - 1) * 2, "\n"));
            for (int i = path.Length - 2; i >= 0; i--) output.Append(' ', i * 2).Append("}\n");
        }
        notes = messages.AsReadOnly();
        return Utf8.GetBytes(output.ToString());
    }

    internal static string Entry(string name, string message, int indent, string newline = "\n")
    {
        message = message.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (message.Length == 0) message = "{{}}";
        string padding = new(' ', indent);
        if (!message.Contains('\n')) return padding + name + " = " + message + newline;
        return padding + name + " =" + newline + string.Join(newline, message.Split('\n').Select(line => padding + "  " + line)) + newline;
    }
    internal static Rmf2ResourceDocument Require(TranslationSource source)
    {
        var document = Rmf2ResourceReader.Read(source);
        if (!document.Success) throw new TranslationAuthoringException("Invalid RMF2 source: " + string.Join("; ", document.Diagnostics.Select(d => d.Message)));
        return document;
    }
    internal static byte[] Replace(byte[] source, IEnumerable<(int Start, int Length, byte[] Bytes)> changes)
    {
        using var output = new MemoryStream(); int at = 0;
        foreach (var change in changes.OrderBy(c => c.Start))
        {
            if (change.Start < at) throw new TranslationAuthoringException("Overlapping resource edits.");
            output.Write(source, at, change.Start - at); output.Write(change.Bytes); at = change.Start + change.Length;
        }
        output.Write(source, at, source.Length - at); return output.ToArray();
    }
    private static int Leading(string value) => value.Length - value.TrimStart(' ').Length;
}
