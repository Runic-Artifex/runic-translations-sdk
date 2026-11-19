using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>A resource node with attached documentation and exact physical UTF-8 locations.</summary>
internal sealed class Rmf2ResourceNode
{
    internal Rmf2ResourceNode(string[] path, string? message, string[] comments, string[] properties,
        TextSourceLocation name, TextSourceLocation extent, IReadOnlyList<int> map)
    { Path = Array.AsReadOnly(path); Message = message; Comments = comments.Length == 0 ? NoStrings : Array.AsReadOnly(comments);
      Properties = properties.Length == 0 ? NoStrings : Array.AsReadOnly(properties); NameLocation = name; Location = extent; MessageByteMap = map; }
    private static readonly IReadOnlyList<string> NoStrings = Array.AsReadOnly(Array.Empty<string>());
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
internal sealed class Rmf2ResourceDocument
{
    internal Rmf2ResourceDocument(TranslationSource source, List<Rmf2ResourceNode> nodes, IReadOnlyList<TranslationDiagnostic> diagnostics)
    { Source = source; Nodes = nodes.AsReadOnly(); Diagnostics = diagnostics; }
    public TranslationSource Source { get; }
    public IReadOnlyList<Rmf2ResourceNode> Nodes { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public bool Success => !Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error);
}

/// <summary>RMF2 resource syntax version 1. Message execution is validated separately.</summary>
internal static class Rmf2ResourceReader
{
    internal static readonly Regex Identifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);

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
            Mf2MessageParser.Parse(new TranslationSource(source.Path, Encoding.UTF8.GetBytes(node.Message)), messageDiagnostics, options, cancellationToken, sourceSyntax: node.MessageSyntax);
            foreach (var item in messageDiagnostics.Items)
            {
                int from = node.MessageByteMap[Math.Min(item.Location.StartByte, node.MessageByteMap.Count - 1)];
                int to = node.MessageByteMap[Math.Min(item.Location.StartByte + item.Location.LengthBytes, node.MessageByteMap.Count - 1)];
                diagnostics.Add(item.Id, item.Severity, item.Message, source, new ByteSpan(from, Math.Max(0, to - from)));
            }
        }
        return new Rmf2ResourceDocument(source, syntax.Nodes.ToList(), diagnostics.ToSortedArray());
    }

    // Direct MF2 files have one implicit resource whose key is the filename.
    // Keep this adapter internal: it gives authoring hosts one physical syntax
    // model without making the compiler's normalized source representation a
    // second public contract.
    internal static Rmf2ResourceDocument AnalyzeDirect(TranslationSource source, TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new TranslationCompilerOptions();
        Rmf2ResourceDocument syntax = ReadDirect(source, options, cancellationToken);
        var diagnostics = new DiagnosticBag();
        foreach (var item in syntax.Diagnostics) diagnostics.Add(item.Id, item.Severity, item.Message, item.Location);
        if (syntax.Nodes.SingleOrDefault()?.MessageSyntax is { } message)
        {
            foreach (var item in Mf2SyntaxReader.ValidateDataModel(message).Concat(Mf2SyntaxReader.ValidateInlineProfile(message)))
                diagnostics.Add(item.Id, item.Severity, item.Message, item.Location);
        }
        return new Rmf2ResourceDocument(source, syntax.Nodes.ToList(), diagnostics.ToSortedArray());
    }

    internal static Rmf2ResourceDocument ReadDirect(TranslationSource source, TranslationCompilerOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        string key = System.IO.Path.GetFileNameWithoutExtension(source.Path);
        if (!Identifier.IsMatch(key))
            diagnostics.Add("RTR0050", TranslationDiagnosticSeverity.Error, "Direct MF2 filenames must be identifiers.", source, new ByteSpan(0, 0));
        var syntax = Mf2SyntaxReader.Read(source, options, cancellationToken);
        foreach (var item in syntax.Diagnostics) diagnostics.Add(item.Id, item.Severity, item.Message, item.Location);
        string message;
        try { message = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
        catch (DecoderFallbackException) { message = string.Empty; }
        TextSourceLocation name = DiagnosticBag.Location(source, new ByteSpan(0, 0));
        TextSourceLocation location = DiagnosticBag.Location(source, new ByteSpan(0, source.Bytes.Length));
        var node = new Rmf2ResourceNode([key], message, [], [], name, location,
            Rmf2ByteMap.Identity(source.Bytes.Length + 1)) { MessageSyntax = syntax };
        return new Rmf2ResourceDocument(source, [node], diagnostics.ToSortedArray());
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
        // Each line as its start, its length without a trailing CR, and the start of the next line.
        var lines = new List<(int Start, int Length, int End)>();
        int start = text.StartsWith('\uFEFF') ? 1 : 0;
        for (int i = start; i <= text.Length; i++)
        {
            if (i != text.Length && text[i] != '\n') continue;
            int end = i > start && text[i - 1] == '\r' ? i - 1 : i;
            lines.Add((start, end - start, i < text.Length ? i + 1 : i)); start = i + 1;
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
            int line = LastLineAtOrBefore(from), last = LastLineAtOrBefore(to);
            line = Math.Max(line, 0); last = Math.Max(last, 0);
            return new(source.Path, bytes[from], bytes[to] - bytes[from], line + 1, from - lines[line].Start + 1, last + 1, to - lines[last].Start + 1);
        }
        // The last line that starts at or before a character offset (-1 when none): lines are in ascending start order.
        int LastLineAtOrBefore(int offset)
        {
            int low = 0, high = lines.Count - 1, found = -1;
            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                if (lines[middle].Start <= offset) { found = middle; low = middle + 1; }
                else high = middle - 1;
            }
            return found;
        }
        void Error(string message, int from, int to) => diagnostics.Add("RTR0050", TranslationDiagnosticSeverity.Error, message, Location(from, to));
        // Each open group with its logical path, the parent path of its entries.
        var groups = new Stack<(Rmf2ResourceNode Node, int Indent, int Start, string Logical)>();
        // The extracted message text and its byte map, reused from node to node.
        var body = new StringBuilder();
        var map = new Rmf2ByteMap.Builder();
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
            var line = lines[i]; ReadOnlySpan<char> lineText = text.AsSpan(line.Start, line.Length);
            int indent = LeadingSpaces(lineText); ReadOnlySpan<char> trim = lineText[indent..];
            if (trim.Length == 0) { ClearAttachment(true); continue; }
            int at = line.Start + indent;
            if (trim[0] == '\t') { Error("Tabs are not allowed in structural indentation.", at, at + 1); ClearAttachment(true); continue; }
            if (attachedIndent >= 0 && attachedIndent != indent) ClearAttachment(true);
            if (trim[0] is '#' or '@')
            {
                if (attachedStart < 0) { attachedStart = line.Start; attachedIndent = indent; }
                if (trim[0] == '#')
                { if (properties.Count > 0) Error("Comments must precede attached properties.", at, line.Start + line.Length); comments.Add(trim[1..].TrimStart().ToString()); }
                else properties.Add(trim[1..].ToString());
                continue;
            }
            if (trim is "}")
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
            // name = message, or name {: an identifier, spaces, then '=' or '{' (the Structural pattern).
            int nameLength = StructuralName(trim, out int operatorIndex);
            if (nameLength == 0) { Error("Expected 'name = message', 'name {' or '}'.", at, line.Start + line.Length); ClearAttachment(true); continue; }
            if (groups.Count > 0 && indent <= groups.Peek().Indent) Error("Group contents must be indented deeper than their opening.", at, at);
            string[] path = new string[groups.Count + 1];
            int segment = groups.Count - 1;
            foreach (var group in groups) path[segment--] = group.Node.Path[^1];
            path[^1] = trim[..nameLength].ToString();
            string parent = groups.Count == 0 ? "" : groups.Peek().Logical;
            if (indents.TryGetValue(parent, out int expected) && expected != indent) Error("Siblings require consistent structural indentation.", at, at);
            else indents[parent] = indent;
            bool isGroup = trim[operatorIndex] == '{';
            int restIndex = operatorIndex + 1;
            ReadOnlySpan<char> rest = trim[restIndex..];
            int statementStart = attachedStart < 0 ? line.Start : attachedStart;
            int extentEnd = line.End;
            body.Clear(); map.Clear();
            if (isGroup)
            { if (rest.Trim().Length != 0) Error("Group opening must occupy its own structural line.", at, line.Start + line.Length); }
            else if (rest.Trim(' ').Length != 0)
            {
                int bodyStart = at + restIndex;
                // One conventional separator space is framing; additional spaces are content.
                if (text[bodyStart] == ' ') bodyStart++;
                Append(bodyStart, line.Start + line.Length);
            }
            else
            {
                int margin = -1, lastContent = i, firstContent = -1, endLine = i + 1;
                for (; endLine < lines.Count; endLine++)
                {
                    ReadOnlySpan<char> content = text.AsSpan(lines[endLine].Start, lines[endLine].Length);
                    if (content.Trim(' ').Length == 0) continue;
                    int leading = LeadingSpaces(content);
                    if (leading <= indent) break;
                    if (margin < 0) { margin = leading; firstContent = endLine; }
                    if (leading < margin) Error("Multiline content falls between the entry indentation and its margin.", lines[endLine].Start, lines[endLine].Start + leading);
                    lastContent = endLine;
                }
                if (firstContent < 0) Error("An assignment without a body is incomplete; use {{}} for empty text.", at, line.Start + line.Length);
                else
                {
                    for (int j = firstContent; j <= lastContent; j++)
                    {
                        if (j > firstContent) { body.Append('\n'); map.Add(bytes[lines[j - 1].Start + lines[j - 1].Length]); }
                        Append(lines[j].Start + Math.Min(margin, lines[j].Length), lines[j].Start + lines[j].Length);
                    }
                    extentEnd = lines[lastContent].End;
                }
                i = endLine - 1;
            }
            if (!isGroup) map.Add(map.Count == 0 ? bytes[at + trim.Length] : bytes[extentEnd > 0 && text[extentEnd - 1] == '\n' ? (extentEnd > 1 && text[extentEnd - 2] == '\r' ? extentEnd - 2 : extentEnd - 1) : extentEnd]);
            var node = new Rmf2ResourceNode(path, isGroup ? null : body.ToString(), comments.ToArray(), properties.ToArray(),
                Location(at, at + nameLength), Location(statementStart, extentEnd), map.Build());
            // The message's bytes are the UTF-8 encoding of its text, which the syntax shares.
            if (!isGroup) node.MessageSyntax = Mf2SyntaxReader.Read(TranslationSource.Owned(source.Path, Encoding.UTF8.GetBytes(node.Message!)), options, cancellationToken, node.Message);
            nodes.Add(node);
            foreach (string property in properties)
            {
                int space = property.IndexOf(' ');
                string name = space < 0 ? property : property[..space];
                if (isGroup && (name == "example" || name == "param" || name == "maxLength")) Error("@example, @param and @maxLength attach only to messages.", statementStart, at);
                if (name != "example" && name != "param" && name != "maxLength") diagnostics.Add("RTR0051", TranslationDiagnosticSeverity.Warning, "Unknown metadata @" + name + " is preserved without execution.", Location(statementStart, at));
            }
            ClearAttachment(false);
            if (isGroup)
            {
                if (groups.Count >= options.MaximumDepth) { Error("RMF2 group depth exceeds configured limit.", at, at); break; }
                groups.Push((node, indent, statementStart, string.Join(".", path)));
            }
            if (nodes.Count > options.MaximumKeysPerCatalog) { Error("RMF2 node count exceeds configured limit.", at, at); break; }
            if (map.Count > options.MaximumValueBytes + 1) Error("RMF2 message exceeds configured byte limit.", at, at);
            void Append(int from, int to)
            { body.Append(text, from, to - from); map.AddRange(bytes[from], bytes[to]); }
        }
        ClearAttachment(true);
        foreach (var group in groups) Error("Unclosed RMF2 group.", group.Start, group.Start);
        return Result();
    }

    private static int LeadingSpaces(ReadOnlySpan<char> line)
    {
        int count = 0;
        while (count < line.Length && line[count] == ' ') count++;
        return count;
    }

    // The length of the name of a structural line (^([A-Za-z_][A-Za-z0-9_]*)[ ]*(=|\{)(.*)$), with the index of its '=' or
    // '{'; 0 when the line is not structural.
    private static int StructuralName(ReadOnlySpan<char> line, out int operatorIndex)
    {
        operatorIndex = -1;
        if (line.Length == 0 || !(char.IsAsciiLetter(line[0]) || line[0] == '_')) return 0;
        int length = 1;
        while (length < line.Length && (char.IsAsciiLetterOrDigit(line[length]) || line[length] == '_')) length++;
        int index = length;
        while (index < line.Length && line[index] == ' ') index++;
        if (index == line.Length || line[index] is not ('=' or '{')) return 0;
        operatorIndex = index;
        return length;
    }
}
