using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Runic.Translations.Compiler;

/// <summary>Physical source selection. Line and character positions are zero-based UTF-16.</summary>
internal sealed record Rmf2DiagnosticSpan(int StartByte, int LengthBytes, int StartUtf16, int LengthUtf16,
    int StartLine, int StartCharacter, int EndLine, int EndCharacter);

/// <summary>A compiler-authorized physical source edit tied to the complete source revision.</summary>
internal sealed record Rmf2DiagnosticQuickFix(string Id, string Title, string DiagnosticId,
    TextSourceLocation Location, string ExpectedRevision, int StartByte, int LengthBytes, string NewText);

/// <summary>Shared source navigation and conservative diagnostic repairs for authoring transports.</summary>
internal static class Rmf2DiagnosticActions
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

    /// <summary>
    /// Repairs of project diagnostics, for the diagnostics of <paramref name="compilation"/> located in
    /// <paramref name="source"/>: RTR0095 adds the missing plural or ordinal variants of a single-selector message as
    /// copies of its catch-all variant, inserted before it with the separator its variants use. The categories come from
    /// the message itself (its selector's function, <c>select</c> option and <c>:integer</c> type) and the CLDR rules of
    /// the diagnostic's locale, not from the compiled project, so the repair is offered while the catalog has errors
    /// elsewhere, and when <c>pluralCategories</c> makes RTR0095 an error. Messages with several selectors get no repair.
    /// <paramref name="compilation"/> must be compiled from <paramref name="source"/>'s current bytes.
    /// </summary>
    public static IReadOnlyList<Rmf2DiagnosticQuickFix> GetProjectQuickFixes(Rmf2ProjectCompilation compilation, TranslationSource source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        ArgumentNullException.ThrowIfNull(source);
        Rmf2ResourceDocument? document = null;
        string? revision = null;
        var fixes = new List<Rmf2DiagnosticQuickFix>();
        foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (diagnostic.Id != "RTR0095" || diagnostic.MessageKey is null || diagnostic.Locale is not { } locale ||
                !string.Equals(diagnostic.Location.Path.Replace('\\', '/'), source.Path, StringComparison.Ordinal)) continue;
            bool rmf2 = source.Path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase);
            document ??= rmf2 ? Rmf2ResourceReader.Analyze(source, cancellationToken: cancellationToken) : Rmf2ResourceReader.AnalyzeDirect(source, cancellationToken: cancellationToken);
            Rmf2ResourceNode? node = rmf2
                ? document.Nodes.FirstOrDefault(item => !item.IsGroup && item.NameLocation.StartByte == diagnostic.Location.StartByte)
                : document.Nodes.Count == 1 ? document.Nodes[0] : null;
            if (node is not { Message: { } text, MessageSyntax: { Match: { } match } syntax }) continue;
            // The message's own semantics; a translation whose selector is typed only by the base's caller contract
            // takes it from the project when the project compiled.
            Rmf2MessageV5? message = Rmf2SemanticCompilerV5.Compile(new TranslationSource(source.Path, Utf8.GetBytes(text)), cancellationToken: cancellationToken).Message
                ?? compilation.Project?.Locales.FirstOrDefault(item => item.Tag == locale)?.DirectResources
                    .FirstOrDefault(item => item.Key == diagnostic.MessageKey && item.SourceLocation.StartByte == node.NameLocation.StartByte &&
                        string.Equals(item.SourceLocation.Path.Replace('\\', '/'), source.Path, StringComparison.Ordinal))?.Message;
            if (message is null || message.Selectors.Count != 1 || message.Selectors[0].Function is not ("plural" or "ordinal") ||
                syntax.Variants.Count != message.Variants.Count) continue;
            bool ordinal = message.Selectors[0].Function == "ordinal";
            IReadOnlyList<string> categories = Rmf2AuthoringService.PluralCategories(locale, ordinal, message.Selectors[0].Type == "int64");
            var used = new HashSet<string>(message.Variants.Select(static variant => variant.Keys[0])
                .Where(static item => item.Kind == "literal" && item.Canonical is null && item.Value is not null).Select(static item => item.Value!), StringComparer.Ordinal);
            string[] missing = categories.Where(category => category != "other" && !used.Contains(category)).ToArray();
            int catchAll = -1;
            for (int index = 0; index < syntax.Variants.Count; index++)
                if (syntax.Variants[index].Keys.Count == 1 && syntax.Variants[index].Keys[0] == "*") { catchAll = index; break; }
            if (missing.Length == 0 || catchAll < 0) continue;
            IReadOnlyList<int> map = node.MessageByteMap;
            int Map(int offset) => map[Math.Min(offset, map.Count - 1)];
            Mf2VariantSyntax variant = syntax.Variants[catchAll];
            int insertAt = Map(variant.Location.StartByte);
            // The separator between two variants: before the catch-all, else after it when it comes first.
            string separator = catchAll > 0 ? Between(syntax.Variants[catchAll - 1], variant)
                : syntax.Variants.Count > 1 ? Between(variant, syntax.Variants[1]) : string.Empty;
            if (separator.Length == 0 || separator.Any(static character => !char.IsWhiteSpace(character))) separator = " ";
            int patternFrom = Map(variant.PatternLocation.StartByte), patternTo = Map(End(variant.PatternLocation));
            string pattern = Utf8.GetString(source.Bytes, patternFrom, patternTo - patternFrom);
            fixes.Add(new Rmf2DiagnosticQuickFix("rmf2.plural-variants:" + diagnostic.Location.StartByte.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Add the missing " + (ordinal ? "ordinal" : "plural") + " " + (missing.Length == 1 ? "variant " : "variants ") + string.Join(", ", missing) + " (copies of the * variant)",
                diagnostic.Id, diagnostic.Location, revision ??= Revision(source), insertAt, 0,
                string.Concat(missing.Select(category => category + " " + pattern + separator))));

            string Between(Mf2VariantSyntax first, Mf2VariantSyntax second)
            {
                int from = Map(End(first.Location)), to = Map(second.Location.StartByte);
                return from < to ? Utf8.GetString(source.Bytes, from, to - from) : string.Empty;
            }
        }
        return fixes.AsReadOnly();
        static int End(TextSourceLocation location) => location.StartByte + location.LengthBytes;
    }

    /// <summary>Refuses stale or altered repair data and applies only a newly authorized compiler edit.</summary>
    public static TranslationSource ApplyQuickFix(TranslationSource source, Rmf2DiagnosticQuickFix fix) => ApplyQuickFix(source, fix, null);

    /// <summary>
    /// As <see cref="ApplyQuickFix(TranslationSource, Rmf2DiagnosticQuickFix)"/>, also accepting the project repairs of
    /// <see cref="GetProjectQuickFixes"/>; <paramref name="compilation"/> must be compiled from <paramref name="source"/>.
    /// </summary>
    public static TranslationSource ApplyQuickFix(TranslationSource source, Rmf2DiagnosticQuickFix fix, Rmf2ProjectCompilation? compilation)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(fix.Location);
        if (!string.Equals(Revision(source), fix.ExpectedRevision, StringComparison.Ordinal))
            throw new InvalidOperationException("The source changed since this quick fix was offered.");
        IEnumerable<Rmf2DiagnosticQuickFix> candidates = GetQuickFixes(Rmf2ResourceReader.Analyze(source));
        if (compilation is not null) candidates = candidates.Concat(GetProjectQuickFixes(compilation, source));
        Rmf2DiagnosticQuickFix? current = candidates.FirstOrDefault(candidate => candidate.Id == fix.Id);
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
