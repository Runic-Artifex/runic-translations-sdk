using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Tomlyn;
using Tomlyn.Parsing;
using Tomlyn.Syntax;

namespace Runic.Translations.Compiler;

/// <summary>A logical MF2 message in an immutable physical locale document.</summary>
public sealed class TranslationLocaleEntry
{
    internal TranslationLocaleEntry(string key, TranslationSource message, TextSourceLocation keyLocation,
        TextSourceLocation valueLocation, TextSourceLocation statementLocation, string[] tablePath, string[] keyPath, string[] inlinePath)
    {
        Key = key;
        Message = message;
        KeyLocation = keyLocation;
        ValueLocation = valueLocation;
        StatementLocation = statementLocation;
        TablePath = Array.AsReadOnly((string[])tablePath.Clone());
        KeyPath = Array.AsReadOnly((string[])keyPath.Clone());
        InlinePath = Array.AsReadOnly((string[])inlinePath.Clone());
    }

    public string Key { get; }
    /// <summary>Effective enclosing-table segments, including stable array identities; empty for root entries.</summary>
    public IReadOnlyList<string> TablePath { get; }
    /// <summary>Decoded segments of the local key, before flattening with underscores.</summary>
    public IReadOnlyList<string> KeyPath { get; }
    /// <summary>Inline-container path relative to the enclosing declared table; empty outside inline tables.</summary>
    public IReadOnlyList<string> InlinePath { get; }
    public TranslationSource Message { get; }
    public TextSourceLocation KeyLocation { get; }
    public TextSourceLocation ValueLocation { get; }
    /// <summary>Key through value token, excluding leading indentation and trailing comments/newline.</summary>
    public TextSourceLocation StatementLocation { get; }
}

/// <summary>A declared table or array element and its physical insertion boundary.</summary>
public sealed class TranslationLocaleTable
{
    internal TranslationLocaleTable(string[] path, string[] declaredPath, TextSourceLocation headerLocation, int insertionByte, bool isArrayElement)
    {
        Path = Array.AsReadOnly((string[])path.Clone());
        DeclaredPath = Array.AsReadOnly((string[])declaredPath.Clone());
        IsArrayElement = isArrayElement;
        HeaderLocation = headerLocation;
        InsertionByte = insertionByte;
    }

    public IReadOnlyList<string> Path { get; }
    public IReadOnlyList<string> DeclaredPath { get; }
    public bool IsArrayElement { get; }
    /// <summary>Opening through closing bracket, excluding comments and newline.</summary>
    public TextSourceLocation HeaderLocation { get; }
    /// <summary>UTF-8 offset of the next table opening bracket, or EOF.</summary>
    public int InsertionByte { get; }
}

/// <summary>An inline table member with exact token and separator spans.</summary>
public sealed class TranslationLocaleInlineMember
{
    internal TranslationLocaleInlineMember(string[] keyPath, TextSourceLocation keyLocation, TextSourceLocation valueLocation,
        TextSourceLocation statementLocation, TextSourceLocation? separatorLocation)
    {
        KeyPath = Array.AsReadOnly((string[])keyPath.Clone());
        KeyLocation = keyLocation;
        ValueLocation = valueLocation;
        StatementLocation = statementLocation;
        SeparatorLocation = separatorLocation;
    }

    public IReadOnlyList<string> KeyPath { get; }
    public TextSourceLocation KeyLocation { get; }
    public TextSourceLocation ValueLocation { get; }
    public TextSourceLocation StatementLocation { get; }
    /// <summary>The trailing comma token, or null when this member has none.</summary>
    public TextSourceLocation? SeparatorLocation { get; }
}

/// <summary>An inline table container; its path includes the enclosing declared table.</summary>
public sealed class TranslationLocaleInlineTable
{
    internal TranslationLocaleInlineTable(string[] path, TextSourceLocation valueLocation, List<TranslationLocaleInlineMember> members, bool isArrayElement)
    {
        Path = Array.AsReadOnly((string[])path.Clone());
        ValueLocation = valueLocation;
        Members = members.AsReadOnly();
        IsArrayElement = isArrayElement;
    }

    public IReadOnlyList<string> Path { get; }
    /// <summary>Opening through closing brace, including all contents.</summary>
    public TextSourceLocation ValueLocation { get; }
    public IReadOnlyList<TranslationLocaleInlineMember> Members { get; }
    public bool IsArrayElement { get; }
}

public sealed class TranslationLocaleDocument
{
    internal TranslationLocaleDocument(TranslationSource source, string locale, List<TranslationLocaleEntry> entries,
        IReadOnlyList<TranslationDiagnostic> diagnostics, int rootInsertionByte, List<TranslationLocaleTable> tables, List<TranslationLocaleInlineTable> inlineTables)
    {
        Source = source;
        Locale = locale;
        Entries = entries.AsReadOnly();
        Diagnostics = diagnostics;
        RootInsertionByte = rootInsertionByte;
        Tables = tables.AsReadOnly();
        InlineTables = inlineTables.AsReadOnly();
    }

    /// <summary>UTF-8 offset of the first table header, or EOF when no tables exist.</summary>
    public int RootInsertionByte { get; }
    public IReadOnlyList<TranslationLocaleTable> Tables { get; }
    public IReadOnlyList<TranslationLocaleInlineTable> InlineTables { get; }
    public TranslationSource Source { get; }
    public string Locale { get; }
    public IReadOnlyList<TranslationLocaleEntry> Entries { get; }
    public IReadOnlyList<TranslationDiagnostic> Diagnostics { get; }
    public bool Success
    {
        get
        {
            foreach (TranslationDiagnostic diagnostic in Diagnostics)
                if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) return false;
            return true;
        }
    }
}

/// <summary>Reads the grouped Runic locale profile of TOML 1.1 without object serialization.</summary>
public static class TranslationLocaleReader
{
    public static TranslationLocaleDocument Read(TranslationSource source, string locale,
        TranslationCompilerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(locale);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        var entries = new List<TranslationLocaleEntry>();
        int rootInsertionByte = source.Bytes.Length;
        var tables = new List<TranslationLocaleTable>();
        var inlineTables = new List<TranslationLocaleInlineTable>();
        if (source.Bytes.Length > options.MaximumDocumentBytes)
        {
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML document exceeds the configured byte limit.", source, new ByteSpan(0, 0));
            return Result();
        }
        string text;
        try { text = StrictJsonParser.StrictUtf8.GetString(source.Bytes); }
        catch (DecoderFallbackException)
        {
            diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "TOML document is not valid UTF-8.", source, new ByteSpan(0, 0));
            return Result();
        }
        // SyntaxParser string offsets are UTF-16 code units; SourceSpan.End is inclusive.
        var offsets = new int[text.Length + 1];
        var lineStarts = new List<int> { 0 };
        int byteOffset = 0;
        for (int i = 0; i < text.Length; i++)
        {
            offsets[i] = byteOffset;
            if (text[i] == '\n') lineStarts.Add(i + 1);
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                offsets[++i] = byteOffset;
                byteOffset += 4;
            }
            else byteOffset += text[i] < 0x80 ? 1 : text[i] < 0x800 ? 2 : 3;
        }
        offsets[text.Length] = byteOffset;
        DocumentSyntax syntax;
        try { syntax = SyntaxParser.Parse(text, new TomlSerializerOptions { MaxDepth = options.MaximumDepth }, source.Path); }
        catch (TomlException exception)
        {
            // Tomlyn reports its depth guard by exception rather than a returned syntax diagnostic.
            foreach (DiagnosticMessage diagnostic in exception.Diagnostics)
                diagnostics.Add("RTR0041", TranslationDiagnosticSeverity.Error, diagnostic.Message, Location(diagnostic.Span));
            if (diagnostics.Count == 0)
                diagnostics.Add("RTR0041", TranslationDiagnosticSeverity.Error, exception.Message, source, new ByteSpan(0, 0));
            return Result();
        }
        cancellationToken.ThrowIfCancellationRequested();
        foreach (DiagnosticMessage diagnostic in syntax.Diagnostics)
            diagnostics.Add("RTR0041", diagnostic.Kind == DiagnosticMessageKind.Error ? TranslationDiagnosticSeverity.Error : TranslationDiagnosticSeverity.Warning,
                diagnostic.Message, Location(diagnostic.Span));
        if (syntax.HasErrors) return Result();
        long keyCount = syntax.KeyValues.ChildrenCount;
        foreach (TableSyntaxBase table in syntax.Tables)
        {
            keyCount += table.Items.ChildrenCount;
            rootInsertionByte = Math.Min(rootInsertionByte, Location(table.OpenBracket?.Span ?? table.Span).StartByte);
        }
        if (keyCount > options.MaximumKeysPerCatalog)
        {
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML key count exceeds the configured limit.", source, new ByteSpan(0, 0));
            return Result();
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        int visitedKeys = 0;
        var activeArrays = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var arrayIdentities = new HashSet<string>(StringComparer.Ordinal);
        ReadEntries(syntax.KeyValues, Array.Empty<string>());
        for (int tableIndex = 0; tableIndex < syntax.Tables.ChildrenCount; tableIndex++)
        {
            TableSyntaxBase table = syntax.Tables.GetChild(tableIndex)!;
            cancellationToken.ThrowIfCancellationRequested();
            string[]? declaredPath = ReadPath(table.Name);
            if (declaredPath is null) continue;
            bool isArrayElement = table is TableArraySyntax;
            string[] tablePath = ResolveTablePath(declaredPath, isArrayElement);
            if (isArrayElement)
            {
                string declared = string.Join(".", declaredPath);
                var expired = new List<string>();
                foreach (string active in activeArrays.Keys)
                    if (active == declared || active.StartsWith(declared + ".", StringComparison.Ordinal)) expired.Add(active);
                foreach (string active in expired) activeArrays.Remove(active);
                string? identity = ReadIdentity(table.Items, tablePath, table.Name!.Span);
                if (identity is null) continue;
                tablePath = Combine(tablePath, new[] { identity });
                activeArrays.Add(declared, tablePath);
            }
            if (tablePath.Length > options.MaximumDepth)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Effective TOML path exceeds the configured depth limit.", Location(table.Name!.Span));
                continue;
            }
            TextSourceLocation start = Location(table.OpenBracket!.Span);
            TextSourceLocation end = Location(table.CloseBracket!.Span);
            TableSyntaxBase? next = tableIndex + 1 < syntax.Tables.ChildrenCount ? syntax.Tables.GetChild(tableIndex + 1) : null;
            int insertionByte = next is null ? source.Bytes.Length : Location(next.OpenBracket!.Span).StartByte;
            tables.Add(new TranslationLocaleTable(tablePath, declaredPath, new TextSourceLocation(source.Path, start.StartByte,
                end.StartByte + end.LengthBytes - start.StartByte, start.Line, start.Column, end.EndLine, end.EndColumn), insertionByte, isArrayElement));
            ReadEntries(table.Items, tablePath, isArrayElement);

        }
        return Result();

        string[]? ReadPath(KeySyntax? key)
        {
            if (key is null) return null;
            var segments = new List<string> { Segment(key.Key) };
            foreach (DottedKeyItemSyntax part in key.DotKeys) segments.Add(Segment(part.Key));
            foreach (string segment in segments)
            {
                if (TranslationCompiler.IsIdentifier(segment)) continue;
                diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error,
                    "Every TOML key/table segment must be an identifier [A-Za-z_][A-Za-z0-9_]*; literal dots inside quoted segments are unsupported.", Location(key.Span));
                return null;
            }
            if (segments.Count > options.MaximumDepth)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML path exceeds the configured depth limit.", Location(key.Span));
                return null;
            }
            return segments.ToArray();
        }

        static string Segment(BareKeyOrStringValueSyntax? key) => key switch
        {
            BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
            StringValueSyntax quoted => quoted.Value ?? string.Empty,
            _ => string.Empty,
        };

        string[] ResolveTablePath(string[] declaredPath, bool arrayElement)
        {
            for (int count = declaredPath.Length - (arrayElement ? 1 : 0); count > 0; count--)
            {
                string prefix = string.Join(".", declaredPath, 0, count);
                if (!activeArrays.TryGetValue(prefix, out string[]? effective)) continue;
                var suffix = new string[declaredPath.Length - count];
                Array.Copy(declaredPath, count, suffix, 0, suffix.Length);
                return Combine(effective, suffix);
            }
            return declaredPath;
        }

        static bool IsIdentity(KeyValueSyntax item) => item.Key is not null &&
            item.Key.DotKeys.ChildrenCount == 0 && Segment(item.Key.Key) == "_id";

        string? ReadIdentity(IEnumerable<KeyValueSyntax> items, string[] containerPath, SourceSpan span)
        {
            foreach (KeyValueSyntax item in items)
            {
                if (!IsIdentity(item)) continue;
                if (item.Value is not StringValueSyntax value || value.Value is null || !TranslationCompiler.IsIdentifier(value.Value) ||
                    StrictJsonParser.StrictUtf8.GetByteCount(value.Value) > options.MaximumValueBytes)
                {
                    diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "Array-table _id must be a string identifier within the value byte limit.", Location(item.Value?.Span ?? item.Span));
                    return null;
                }
                string identity = value.Value;
                if (!arrayIdentities.Add(string.Join(".", containerPath) + "." + identity))
                {
                    diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error, "Duplicate array-table _id '" + identity + "' in the same parent array.", Location(item.Value.Span));
                    return null;
                }
                return identity;
            }
            diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "Every array-table element requires a direct _id string identifier.", Location(span));
            return null;
        }

        static IEnumerable<KeyValueSyntax> InlineMembers(InlineTableSyntax inline)
        {
            foreach (InlineTableItemSyntax member in inline.Items) yield return member.KeyValue!;
        }

        void ReadEntries(SyntaxList<KeyValueSyntax> items, string[] tablePath, bool isArrayElement = false)
        {
            foreach (KeyValueSyntax item in items) ReadEntry(item, tablePath, Array.Empty<string>(), isArrayElement);
        }

        void ReadEntry(KeyValueSyntax item, string[] tablePath, string[] inlinePath, bool isArrayElement = false)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++visitedKeys > options.MaximumKeysPerCatalog)
            {
                if (visitedKeys == options.MaximumKeysPerCatalog + 1)
                    diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML key count exceeds the configured limit.", Location(item.Span));
                return;
            }
            if (isArrayElement && IsIdentity(item)) return;
            string[]? keyPath = ReadPath(item.Key);
            if (keyPath is null) return;
            if (tablePath.Length + inlinePath.Length + keyPath.Length > options.MaximumDepth)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML path exceeds the configured depth limit.", Location(item.Key!.Span));
                return;
            }
            if (item.Value is InlineTableSyntax inline)
            {
                ReadInline(inline, tablePath, Combine(inlinePath, keyPath), false);
                return;
            }
            if (item.Value is ArraySyntax array)
            {
                string[] containerPath = Combine(inlinePath, keyPath);
                foreach (ArrayItemSyntax element in array.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (element.Value is not InlineTableSyntax row)
                    {
                        diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "Arrays may contain only inline-table elements with stable _id values.", Location(element.Value?.Span ?? element.Span));
                        continue;
                    }
                    string? identity = ReadIdentity(InlineMembers(row), Combine(tablePath, containerPath), row.Span);
                    if (identity is not null) ReadInline(row, tablePath, Combine(containerPath, new[] { identity }), true);
                }
                return;
            }
            string key = string.Join("_", Combine(Combine(tablePath, inlinePath), keyPath));
            if (!keys.Add(key))
            {
                diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error,
                    "TOML paths collide after flattening to message identifier '" + key + "'.", Location(item.Key!.Span));
                return;
            }
            if (item.Value is not StringValueSyntax value || value.Value is null || value.Token is null)
            {
                diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "TOML message values must be MF2 strings or inline-table containers.", Location(item.Value?.Span ?? item.Span));
                return;
            }
            byte[] decoded = StrictJsonParser.StrictUtf8.GetBytes(value.Value);
            if (decoded.Length > options.MaximumValueBytes)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Decoded TOML message exceeds the configured byte limit.", Location(value.Token.Span));
                return;
            }
            TextSourceLocation keyLocation = Location(item.Key!.Span);
            TextSourceLocation valueLocation = Location(value.Token.Span);
            entries.Add(new TranslationLocaleEntry(key, new TranslationSource(source.Path, decoded), keyLocation, valueLocation,
                Statement(keyLocation, valueLocation), tablePath, keyPath, inlinePath));
        }

        void ReadInline(InlineTableSyntax inline, string[] tablePath, string[] nestedPath, bool isArrayElement)
        {
            if (tablePath.Length + nestedPath.Length > options.MaximumDepth)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Effective TOML path exceeds the configured depth limit.", Location(inline.Span));
                return;
            }
            var members = new List<TranslationLocaleInlineMember>();
            inlineTables.Add(new TranslationLocaleInlineTable(Combine(tablePath, nestedPath), Location(inline.Span), members, isArrayElement));
            foreach (InlineTableItemSyntax child in inline.Items)
            {
                KeyValueSyntax member = child.KeyValue!;
                string[]? memberPath = ReadPath(member.Key);
                if (memberPath is null) continue;
                TextSourceLocation memberKey = Location(member.Key!.Span);
                TextSourceLocation memberValue = Location(member.Value!.Span);
                members.Add(new TranslationLocaleInlineMember(memberPath, memberKey, memberValue,
                    Statement(memberKey, memberValue), child.Comma is null ? null : Location(child.Comma.Span)));
                ReadEntry(member, tablePath, nestedPath, isArrayElement);
            }
        }

        TextSourceLocation Statement(TextSourceLocation key, TextSourceLocation value) => new(source.Path, key.StartByte,
            value.StartByte + value.LengthBytes - key.StartByte, key.Line, key.Column, value.EndLine, value.EndColumn);

        static string[] Combine(string[] first, string[] second)
        {
            var combined = new string[first.Length + second.Length];
            first.CopyTo(combined, 0);
            second.CopyTo(combined, first.Length);
            return combined;
        }

        TranslationLocaleDocument Result() => new(source, locale, entries, Array.AsReadOnly(diagnostics.ToSortedArray()), rootInsertionByte, tables, inlineTables);
        TextSourceLocation Location(SourceSpan span)
        {
            int start = Math.Clamp(span.Start.Offset, 0, text.Length);
            int end = Math.Clamp(span.End.Offset + 1, start, text.Length);
            int line = lineStarts.BinarySearch(start);
            if (line < 0) line = ~line - 1;
            int endLine = lineStarts.BinarySearch(end);
            if (endLine < 0) endLine = ~endLine - 1;
            return new TextSourceLocation(source.Path, offsets[start], offsets[end] - offsets[start],
                line + 1, start - lineStarts[line] + 1, endLine + 1, end - lineStarts[endLine] + 1);
        }
    }
}
