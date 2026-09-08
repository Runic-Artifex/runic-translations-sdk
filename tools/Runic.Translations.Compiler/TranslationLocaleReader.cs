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
        TextSourceLocation valueLocation, TextSourceLocation statementLocation)
    {
        Key = key;
        Message = message;
        KeyLocation = keyLocation;
        ValueLocation = valueLocation;
        StatementLocation = statementLocation;
    }

    public string Key { get; }
    public TranslationSource Message { get; }
    public TextSourceLocation KeyLocation { get; }
    public TextSourceLocation ValueLocation { get; }
    /// <summary>Key through value token, excluding leading indentation and trailing comments/newline.</summary>
    public TextSourceLocation StatementLocation { get; }
}

public sealed class TranslationLocaleDocument
{
    internal TranslationLocaleDocument(TranslationSource source, string locale, List<TranslationLocaleEntry> entries,
        IReadOnlyList<TranslationDiagnostic> diagnostics)
    {
        Source = source;
        Locale = locale;
        Entries = entries.AsReadOnly();
        Diagnostics = diagnostics;
    }

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

/// <summary>Reads the flat Runic locale profile of TOML 1.1 without object serialization.</summary>
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
        foreach (TableSyntaxBase table in syntax.Tables)
            diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "Locale TOML permits only flat string entries; tables are unsupported.", Location(table.Span));
        if (syntax.KeyValues.ChildrenCount > options.MaximumKeysPerCatalog)
        {
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "TOML key count exceeds the configured limit.", source, new ByteSpan(0, 0));
            return Result();
        }
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValueSyntax item in syntax.KeyValues)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = item.Key?.Key switch
            {
                BareKeySyntax bare => bare.Key?.Text ?? string.Empty,
                StringValueSyntax quoted => quoted.Value ?? string.Empty,
                _ => string.Empty,
            };
            if (item.Key is null || item.Key.DotKeys.ChildrenCount != 0 || !TranslationCompiler.IsIdentifier(key))
            {
                diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "TOML message keys must be flat identifiers [A-Za-z_][A-Za-z0-9_]*; dots are unsupported even in quoted keys.", Location(item.Key?.Span ?? item.Span));
                continue;
            }
            if (!keys.Add(key))
            {
                diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error, "Duplicate TOML message key '" + key + "'.", Location(item.Key.Span));
                continue;
            }
            if (item.Value is not StringValueSyntax value || value.Value is null || value.Token is null)
            {
                diagnostics.Add("RTR0042", TranslationDiagnosticSeverity.Error, "TOML message values must be strings containing MF2.", Location(item.Value?.Span ?? item.Span));
                continue;
            }
            byte[] decoded = StrictJsonParser.StrictUtf8.GetBytes(value.Value);
            if (decoded.Length > options.MaximumValueBytes)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Decoded TOML message exceeds the configured byte limit.", Location(value.Token.Span));
                continue;
            }
            TextSourceLocation keyLocation = Location(item.Key.Span);
            TextSourceLocation valueLocation = Location(value.Token.Span);
            entries.Add(new TranslationLocaleEntry(key, new TranslationSource(source.Path, decoded), keyLocation, valueLocation,
                new TextSourceLocation(source.Path, keyLocation.StartByte, valueLocation.StartByte + valueLocation.LengthBytes - keyLocation.StartByte,
                    keyLocation.Line, keyLocation.Column, valueLocation.EndLine, valueLocation.EndColumn)));
        }
        return Result();

        TranslationLocaleDocument Result() => new(source, locale, entries, Array.AsReadOnly(diagnostics.ToSortedArray()));
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
