using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>Physical source selection. Line and character positions are zero-based UTF-16.</summary>
public sealed record Rmf2DiagnosticSpan(int StartByte, int LengthBytes, int StartUtf16, int LengthUtf16,
    int StartLine, int StartCharacter, int EndLine, int EndCharacter);

/// <summary>A compiler-authorized physical source edit tied to the complete source revision.</summary>
public sealed record Rmf2DiagnosticQuickFix(string Id, string Title, string DiagnosticId,
    TextSourceLocation Location, string ExpectedRevision, int StartByte, int LengthBytes, string NewText);

/// <summary>Shared source navigation and conservative diagnostic repairs for authoring transports.</summary>
public static class Rmf2DiagnosticActions
{
    private const string EmptyAssignment = "An assignment without a body is incomplete; use {{}} for empty text.";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    /// <summary>Converts the compiler's physical byte extent without relying on display columns.</summary>
    public static Rmf2DiagnosticSpan GetSpan(TranslationSource source, TextSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(location);
        if (!string.Equals(source.Path, location.Path.Replace('\\', '/'), StringComparison.Ordinal))
            throw new ArgumentException("The diagnostic belongs to a different source.", nameof(location));
        byte[] bytes = source.Bytes;
        if (location.StartByte < 0 || location.LengthBytes < 0 || location.StartByte > bytes.Length - location.LengthBytes)
            throw new ArgumentException("The diagnostic is outside its source.", nameof(location));
        string text = Utf8.GetString(bytes);
        int start = Utf8.GetCharCount(bytes.AsSpan(0, location.StartByte));
        int length = Utf8.GetCharCount(bytes.AsSpan(location.StartByte, location.LengthBytes));
        var from = Position(text, start);
        var to = Position(text, start + length);
        return new(location.StartByte, location.LengthBytes, start, length, from.Line, from.Character, to.Line, to.Character);
    }

    /// <summary>Offers an explicit empty message only for a currently diagnosed empty RMF2 assignment.</summary>
    public static IReadOnlyList<Rmf2DiagnosticQuickFix> GetQuickFixes(TranslationSource source, TranslationDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (!source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) || diagnostic.Id != "RTR0050" || diagnostic.Message != EmptyAssignment ||
            !string.Equals(source.Path, diagnostic.Location.Path.Replace('\\', '/'), StringComparison.Ordinal)) return Array.Empty<Rmf2DiagnosticQuickFix>();
        Rmf2ResourceDocument document = Rmf2ResourceReader.Analyze(source);
        if (!document.Diagnostics.Any(item => SameDiagnostic(item, diagnostic))) return Array.Empty<Rmf2DiagnosticQuickFix>();
        return Array.AsReadOnly(GetQuickFixes(document).Where(fix => SameLocation(fix.Location, diagnostic.Location)).ToArray());
    }

    /// <summary>Offers all authorized repairs from one immutable compiler analysis without reparsing each diagnostic.</summary>
    [SuppressMessage("ApiDesign", "RS0027:Public API with optional parameter(s) should have the most parameters amongst its public overloads", Justification = "The overloads take unrelated inputs; only the document overload has an optional cancellation token.")]
    public static IReadOnlyList<Rmf2DiagnosticQuickFix> GetQuickFixes(Rmf2ResourceDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        TranslationSource source = document.Source;
        if (!source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase)) return Array.Empty<Rmf2DiagnosticQuickFix>();
        var nodes = document.Nodes.Where(item => !item.IsGroup && item.Message == "" && item.MessageByteMap.Count == 1)
            .ToDictionary(item => item.NameLocation.StartByte);
        var fixes = new List<Rmf2DiagnosticQuickFix>();
        string? revision = null;
        foreach (TranslationDiagnostic diagnostic in document.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Id != "RTR0050" || diagnostic.Message != EmptyAssignment || diagnostic.Location.Path != source.Path ||
                !nodes.TryGetValue(diagnostic.Location.StartByte, out Rmf2ResourceNode? node)) continue;
            Rmf2DiagnosticQuickFix? fix = EmptyFix(source, node, diagnostic, revision ??= Revision(source));
            if (fix is not null) fixes.Add(fix);
        }
        // RTR0078 is a project diagnostic, but the condition is purely syntactic: a line break inside
        // a document leaf between two Thai, Lao, Khmer or Myanmar characters. Joining the lines
        // removes the break together with the continuation indentation.
        foreach (Rmf2ResourceNode node in document.Nodes.Where(item => !item.IsGroup && item.MessageSyntax is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach ((int start, int end) in Rmf2DocumentSyntax.SoutheastAsianBreaks(node.MessageSyntax!))
            {
                if (end >= node.MessageByteMap.Count) continue;
                int from = node.MessageByteMap[start], to = node.MessageByteMap[end];
                // One fix per break, all reported at the message name, so the title names the lines.
                int line = 1 + source.Bytes.AsSpan(0, from).Count((byte)'\n'), last = line + source.Bytes.AsSpan(from, to - from).Count((byte)'\n');
                fixes.Add(new Rmf2DiagnosticQuickFix("rmf2.join-lines:" + from.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Join lines " + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + " and " +
                    last.ToString(System.Globalization.CultureInfo.InvariantCulture) + " without a space",
                    "RTR0078", node.NameLocation, revision ??= Revision(source), from, to - from, string.Empty));
            }
        }
        return fixes.AsReadOnly();
    }

    private static Rmf2DiagnosticQuickFix? EmptyFix(TranslationSource source, Rmf2ResourceNode node, TranslationDiagnostic diagnostic, string revision)
    {
        byte[] bytes = source.Bytes;
        int end = node.MessageByteMap[0], at = end;
        while (at > node.NameLocation.StartByte && bytes[at - 1] == (byte)' ') at--;
        if (at == 0 || bytes[at - 1] != (byte)'=') return null;
        return new Rmf2DiagnosticQuickFix("rmf2.explicit-empty:" + diagnostic.Location.StartByte,
            "Make message explicitly empty", diagnostic.Id, diagnostic.Location, revision, at, end - at, " {{}}");
    }

    /// <summary>Refuses stale or altered repair data and applies only a newly authorized compiler edit.</summary>
    public static TranslationSource ApplyQuickFix(TranslationSource source, Rmf2DiagnosticQuickFix fix)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(fix.Location);
        if (!string.Equals(Revision(source), fix.ExpectedRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("The source changed since this quick fix was offered.");
        Rmf2DiagnosticQuickFix? current = GetQuickFixes(Rmf2ResourceReader.Analyze(source)).FirstOrDefault(candidate => candidate.Id == fix.Id);
        if (current is null || current.DiagnosticId != fix.DiagnosticId || !SameLocation(current.Location, fix.Location) ||
            current.StartByte != fix.StartByte || current.LengthBytes != fix.LengthBytes || current.NewText != fix.NewText)
            throw new InvalidOperationException("The quick fix no longer matches a compiler diagnostic.");
        byte[] bytes = source.Bytes, replacement = Utf8.GetBytes(current.NewText);
        byte[] result = new byte[bytes.Length - current.LengthBytes + replacement.Length];
        bytes.AsSpan(0, current.StartByte).CopyTo(result);
        replacement.CopyTo(result.AsSpan(current.StartByte));
        bytes.AsSpan(current.StartByte + current.LengthBytes).CopyTo(result.AsSpan(current.StartByte + replacement.Length));
        return new TranslationSource(source.Path, result);
    }

    /// <summary>Lowercase SHA-256 of the complete physical source, matching editor source revisions.</summary>
    public static string Revision(TranslationSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Convert.ToHexStringLower(SHA256.HashData(source.Bytes));
    }
    private static bool SameDiagnostic(TranslationDiagnostic left, TranslationDiagnostic right)
        => left.Id == right.Id && left.Message == right.Message && SameLocation(left.Location, right.Location);
    private static bool SameLocation(TextSourceLocation left, TextSourceLocation right)
        => left.Path == right.Path && left.StartByte == right.StartByte && left.LengthBytes == right.LengthBytes;
    private static (int Line, int Character) Position(string text, int offset)
    {
        int line = 0, start = 0;
        for (int index = 0; index < offset; index++) if (text[index] == '\n') { line++; start = index + 1; }
        return (line, offset - start);
    }
}
