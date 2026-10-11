using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

[Category("rmf2-semantic-v5")]
internal sealed class Rmf2DiagnosticActionTests
{
    private static TranslationSource Source(string text) => new("../feature/en.rmf2", Encoding.UTF8.GetBytes(text));
    [Test, DisplayName("Diagnostic selections map mounted Unicode multiline source to physical UTF16")]
    public void PhysicalSpans()
    {
        const string text = "# 😀 context\r\nblock =\r\n    😀 }\r\n";
        TranslationSource source = Source(text);
        TranslationDiagnostic diagnostic = Rmf2ResourceReader.Analyze(source).Diagnostics.Single(item => item.Message == "This pattern character must be escaped or removed.");
        Rmf2DiagnosticSpan span = Rmf2DiagnosticActions.GetSpan(source, diagnostic.Location);
        Assert.Equal(text.IndexOf('}'), span.StartUtf16);
        Assert.Equal(Encoding.UTF8.GetByteCount(text.AsSpan(0, text.IndexOf('}'))), span.StartByte);
        Assert.Equal(1, span.LengthUtf16);
        Assert.Equal(2, span.StartLine);
        Assert.Equal(7, span.StartCharacter);
        Assert.Equal(2, span.EndLine);
        Assert.Equal(8, span.EndCharacter);
        string selected = text.Substring(span.StartUtf16, span.LengthUtf16);
        Assert.Equal("}", selected);
        int from = text.IndexOf("😀 }", StringComparison.Ordinal), to = text.Length;
        var multiline = new TextSourceLocation(source.Path, Encoding.UTF8.GetByteCount(text.AsSpan(0, from)),
            Encoding.UTF8.GetByteCount(text.AsSpan(from, to - from)), 99, 99, 99, 99);
        span = Rmf2DiagnosticActions.GetSpan(source, multiline);
        Assert.Equal(2, span.StartLine);
        Assert.Equal(4, span.StartCharacter);
        Assert.Equal(3, span.EndLine);
        Assert.Equal(0, span.EndCharacter);
        Assert.Equal(text[from..], text.Substring(span.StartUtf16, span.LengthUtf16));
        Refuses(() => Rmf2DiagnosticActions.GetSpan(Source("changed"), diagnostic.Location));
        Refuses(() => Rmf2DiagnosticActions.GetSpan(source, new TextSourceLocation("other.rmf2", 0, 1, 1, 1, 1, 2)));
        int emoji = Encoding.UTF8.GetByteCount(text.AsSpan(0, text.IndexOf("😀", StringComparison.Ordinal)));
        Refuses(() => Rmf2DiagnosticActions.GetSpan(source, new TextSourceLocation(source.Path, emoji + 1, 1, 1, 1, 1, 2)));
    }
    [Test, DisplayName("Empty-assignment quick fix preserves source and refuses stale or altered evidence")]
    public void EmptyAssignmentFix()
    {
        const string before = "# 😀 preserved\r\nsection {\r\n    empty =   \r\n    other = Keep {$name}\r\n}\r\n";
        TranslationSource source = Source(before);
        TranslationDiagnostic diagnostic = Rmf2ResourceReader.Analyze(source).Diagnostics.Single(item => item.Message.Contains("assignment without a body", StringComparison.Ordinal));
        Rmf2DiagnosticQuickFix fix = Assert.Single(Rmf2DiagnosticActions.GetQuickFixes(source, diagnostic));
        TranslationSource result = Rmf2DiagnosticActions.ApplyQuickFix(source, fix);
        Assert.Equal(before.Replace("empty =   ", "empty = {{}}", StringComparison.Ordinal), Encoding.UTF8.GetString(result.GetUtf8Bytes()));
        Assert.True(Rmf2ResourceReader.Analyze(result).Success, "Fix left an invalid message.");
        Assert.Equal(Rmf2DiagnosticActions.Revision(source), fix.ExpectedRevision);
        Refuses(() => Rmf2DiagnosticActions.ApplyQuickFix(Source(before.Replace("Keep", "New", StringComparison.Ordinal)), fix));
        Refuses(() => Rmf2DiagnosticActions.ApplyQuickFix(source, fix with { NewText = " injected" }));
        Refuses(() => Rmf2DiagnosticActions.ApplyQuickFix(source, fix with { StartByte = 0 }));
        Refuses(() => Rmf2DiagnosticActions.ApplyQuickFix(source, fix with { Location = null! }));
        Assert.Equal(0, Rmf2DiagnosticActions.GetQuickFixes(Source(before + "    body\n"),
            new TranslationDiagnostic(diagnostic.Id, diagnostic.Severity, diagnostic.Message,
                new TextSourceLocation(source.Path, 0, 1, 1, 1, 1, 2))).Count);
        var direct = new TranslationSource("empty.mf2", []);
        Assert.Equal(0, Rmf2DiagnosticActions.GetQuickFixes(direct, diagnostic).Count);
    }
    private static void Refuses(Action action)
    {
        bool refused = false;
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException) { refused = true; }
        Assert.True(refused, "Invalid or stale source evidence was accepted.");
    }
}
