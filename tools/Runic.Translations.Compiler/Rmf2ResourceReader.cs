using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>A resource node with attached documentation and exact physical UTF-8 locations.</summary>
public sealed class Rmf2ResourceNode
{
    internal Rmf2ResourceNode(string[] path, string? message, string[] comments, string[] properties,
        TextSourceLocation name, TextSourceLocation extent, int[] map)
    { Path = Array.AsReadOnly(path); Message = message; Comments = Array.AsReadOnly(comments);
      Properties = Array.AsReadOnly(properties); NameLocation = name; Location = extent; MessageByteMap = Array.AsReadOnly(map); }
    public IReadOnlyList<string> Path { get; }
    public string Key => string.Join("_", Path);
    public string? Message { get; }
    /// <summary>The MF2 source/data model before execution-profile lowering. Null for groups.</summary>
    public Mf2SyntaxDocument? MessageSyntax { get; internal set; }
    public bool IsGroup => Message is null;
    public IReadOnlyList<string> Comments { get; }
    public IReadOnlyList<string> Properties { get; }
    public TextSourceLocation NameLocation { get; }
    public TextSourceLocation Location { get; internal set; }
    /// <summary>Maps every extracted UTF-8 boundary, including EOF, into the original file.</summary>
    public IReadOnlyList<int> MessageByteMap { get; }
}

/// <summary>Lossless source plus a recoverable RMF2 outline; invalid documents cannot compile.</summary>
public sealed class Rmf2ResourceDocument
{
    internal Rmf2ResourceDocument(TranslationSource source, List<Rmf2ResourceNode> nodes, IReadOnlyList<TranslationDiagnostic> diagnostics)
    { Source = source; Nodes = nodes.AsReadOnly(); Diagnostics = diagnostics; }
    public TranslationSource Source { get; }
    public IReadOnlyList<Rmf2ResourceNode> Nodes { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public bool Success => !Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
}

/// <summary>RMF2 resource syntax version 1. Message execution is validated separately.</summary>
public static class Rmf2ResourceReader
{
    internal static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly Regex Structural = new("^([A-Za-z_][A-Za-z0-9_]*)[ ]*(=|\\{)(.*)$", RegexOptions.CultureInvariant);

    /// <summary>Analyzes each extracted MF2 body while retaining recoverable resource symbols.</summary>
    public static Rmf2ResourceDocument Analyze(TranslationSource source, TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TranslationCompilerOptions();
        Rmf2ResourceDocument syntax = Read(source, options, cancellationToken);
        var diagnostics = new DiagnosticBag();
        foreach (var item in syntax.Diagnostics) diagnostics.Add(item.Id, item.Severity, item.Message, item.Location);
        foreach (var node in syntax.Nodes)
        {
            if (node.Message is null || node.MessageByteMap.Count == 0) continue;
            var messageDiagnostics = new DiagnosticBag();
            Mf2MessageParser.Parse(new TranslationSource(source.Path, Encoding.UTF8.GetBytes(node.Message)), messageDiagnostics, options, cancellationToken, rmf2: true, sourceSyntax: node.MessageSyntax);
            foreach (var item in messageDiagnostics.Items)
            {
                int from = node.MessageByteMap[Math.Min(item.Location.StartByte, node.MessageByteMap.Count - 1)];
                int to = node.MessageByteMap[Math.Min(item.Location.StartByte + item.Location.LengthBytes, node.MessageByteMap.Count - 1)];
                diagnostics.Add(item.Id, item.Severity, item.Message, source, new ByteSpan(from, Math.Max(0, to - from)));
            }
        }
        return new Rmf2ResourceDocument(source, syntax.Nodes.ToList(), diagnostics.ToSortedArray());
    }

    public static Rmf2ResourceDocument Read(TranslationSource source, TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        var nodes = new List<Rmf2ResourceNode>();
        Rmf2ResourceDocument Result() => new(source, nodes, diagnostics.ToSortedArray());
        if (source.Bytes.Length > options.MaximumDocumentBytes)
        { diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "RMF2 document exceeds byte limit.", source, new ByteSpan(0, 0)); return Result(); }
        string text;
        try { text = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
        catch (DecoderFallbackException)
        { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "RMF2 requires valid UTF-8.", source, new ByteSpan(0, 0)); return Result(); }
        var lines = new List<(int Start, string Text, int End)>();
        int start = text.StartsWith('\uFEFF') ? 1 : 0;
        for (int i = start; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add((start, text.Substring(start, end - start), i < text.Length ? i + 1 : i)); start = i + 1;
        }
        var bytes = new int[text.Length + 1];
        int count = 0;
        for (int i = 0; i < text.Length; i++)
        {
            bytes[i] = count;
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { bytes[++i] = count; count += 4; }
            else count += text[i] < 128 ? 1 : text[i] < 2048 ? 2 : 3;
        }
        bytes[text.Length] = count;
        TextSourceLocation Location(int from, int to)
        {
            int line = lines.FindLastIndex(l => l.Start <= from), last = lines.FindLastIndex(l => l.Start <= to);
            line = Math.Max(line, 0); last = Math.Max(last, 0);
            return new(source.Path, bytes[from], bytes[to] - bytes[from], line + 1, from - lines[line].Start + 1, last + 1, to - lines[last].Start + 1);
        }
        void Error(string message, int from, int to) => diagnostics.Add("RTR0050", TranslationDiagnosticSeverity.Error, message, Location(from, to));
        var groups = new Stack<(Rmf2ResourceNode Node, int Indent, int Start)>();
        var indents = new Dictionary<string, int>(StringComparer.Ordinal);
        var comments = new List<string>(); var properties = new List<string>();
        int attachedStart = -1, attachedIndent = -1;
        void ClearAttachment(bool orphan)
        {
            if (orphan && properties.Count > 0) Error("Orphan RMF2 properties must attach to the next entry or group without a blank line.", attachedStart, attachedStart);
            comments.Clear(); properties.Clear(); attachedStart = -1; attachedIndent = -1;
        }
        for (int i = 0; i < lines.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = lines[i]; string trim = line.Text.TrimStart(' '); int indent = line.Text.Length - trim.Length;
            if (trim.Length == 0) { ClearAttachment(true); continue; }
            int at = line.Start + indent;
            if (trim.StartsWith('\t')) { Error("Tabs are not allowed in structural indentation.", at, at + 1); ClearAttachment(true); continue; }
            if (attachedIndent >= 0 && attachedIndent != indent) ClearAttachment(true);
            if (trim.StartsWith('#') || trim.StartsWith('@'))
            {
                if (attachedStart < 0) { attachedStart = line.Start; attachedIndent = indent; }
                if (trim.StartsWith('#'))
                { if (properties.Count > 0) Error("Comments must precede attached properties.", at, line.Start + line.Text.Length); comments.Add(trim.Substring(1).TrimStart()); }
                else properties.Add(trim.Substring(1));
                continue;
            }
            if (trim == "}")
            {
                ClearAttachment(true);
                if (groups.Count == 0) Error("Unexpected group closing brace.", at, at + 1);
                else
                {
                    var group = groups.Pop();
                    if (indent != group.Indent) Error("Group closing indentation must match its opening.", at, at + 1);
                    group.Node.Location = Location(group.Start, line.End);
                }
                continue;
            }
            Match match = Structural.Match(trim);
            if (!match.Success) { Error("Expected 'name = message', 'name {' or '}'.", at, line.Start + line.Text.Length); ClearAttachment(true); continue; }
            if (groups.Count > 0 && indent <= groups.Peek().Indent) Error("Group contents must be indented deeper than their opening.", at, at);
            string[] path = groups.Reverse().Select(g => g.Node.Path[^1]).Append(match.Groups[1].Value).ToArray();
            string parent = string.Join(".", path.Take(path.Length - 1));
            if (indents.TryGetValue(parent, out int expected) && expected != indent) Error("Siblings require consistent structural indentation.", at, at);
            else indents[parent] = indent;
            bool isGroup = match.Groups[2].Value == "{";
            string rest = match.Groups[3].Value;
            int statementStart = attachedStart < 0 ? line.Start : attachedStart;
            int extentEnd = line.End;
            var map = new List<int>(); var body = new StringBuilder();
            if (isGroup)
            { if (rest.Trim().Length != 0) Error("Group opening must occupy its own structural line.", at, line.Start + line.Text.Length); }
            else if (rest.Trim(' ').Length != 0)
            {
                int bodyStart = at + match.Groups[3].Index;
                // One conventional separator space is framing; additional spaces are content.
                if (text[bodyStart] == ' ') bodyStart++;
                Append(bodyStart, line.Start + line.Text.Length);
            }
            else
            {
                int margin = -1, lastContent = i, firstContent = -1, endLine = i + 1;
                for (; endLine < lines.Count; endLine++)
                {
                    string content = lines[endLine].Text;
                    if (content.Trim(' ').Length == 0) continue;
                    int leading = content.Length - content.TrimStart(' ').Length;
                    if (leading <= indent) break;
                    if (margin < 0) { margin = leading; firstContent = endLine; }
                    if (leading < margin) Error("Multiline content falls between the entry indentation and its margin.", lines[endLine].Start, lines[endLine].Start + leading);
                    lastContent = endLine;
                }
                if (firstContent < 0) Error("An assignment without a body is incomplete; use {{}} for empty text.", at, line.Start + line.Text.Length);
                else
                {
                    for (int j = firstContent; j <= lastContent; j++)
                    {
                        if (j > firstContent) { body.Append('\n'); map.Add(bytes[lines[j - 1].Start + lines[j - 1].Text.Length]); }
                        Append(lines[j].Start + Math.Min(margin, lines[j].Text.Length), lines[j].Start + lines[j].Text.Length);
                    }
                    extentEnd = lines[lastContent].End;
                }
                i = endLine - 1;
            }
            if (!isGroup) map.Add(map.Count == 0 ? bytes[at + trim.Length] : bytes[extentEnd > 0 && text[extentEnd - 1] == '\n' ? (extentEnd > 1 && text[extentEnd - 2] == '\r' ? extentEnd - 2 : extentEnd - 1) : extentEnd]);
            var node = new Rmf2ResourceNode(path, isGroup ? null : body.ToString(), comments.ToArray(), properties.ToArray(),
                Location(at, at + match.Groups[1].Length), Location(statementStart, extentEnd), map.ToArray());
            if (!isGroup) node.MessageSyntax = Mf2SyntaxReader.Read(new TranslationSource(source.Path, Encoding.UTF8.GetBytes(node.Message!)), options, cancellationToken);
            nodes.Add(node);
            foreach (string property in properties)
            {
                string name = property.Split(' ', 2)[0];
                if (isGroup && (name == "example" || name == "param")) Error("@example and @param attach only to messages.", statementStart, at);
                if (name != "example" && name != "param") diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Warning, "Unknown metadata @" + name + " is preserved without execution.", Location(statementStart, at));
            }
            ClearAttachment(false);
            if (isGroup)
            {
                if (groups.Count >= options.MaximumDepth) { Error("RMF2 group depth exceeds configured limit.", at, at); break; }
                groups.Push((node, indent, statementStart));
            }
            if (nodes.Count > options.MaximumKeysPerCatalog) { Error("RMF2 node count exceeds configured limit.", at, at); break; }
            if (map.Count > options.MaximumValueBytes + 1) Error("RMF2 message exceeds configured byte limit.", at, at);
            void Append(int from, int to)
            { body.Append(text, from, to - from); for (int b = bytes[from]; b < bytes[to]; b++) map.Add(b); }
        }
        ClearAttachment(true);
        foreach (var group in groups) Error("Unclosed RMF2 group.", group.Start, group.Start);
        return Result();
    }
}
