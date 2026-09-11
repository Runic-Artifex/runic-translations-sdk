using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 flat split and explicit mounted catalogs agree", Composition);
        runner.Add("RMF2 preserves CRLF margins blanks Unicode metadata and empty patterns", Framing);
        runner.Add("RMF2 recovery retains later symbols and rejects malformed framing", Recovery);
        runner.Add("RMF2 rejects duplicate leaves namespace and generated identity collisions", Collisions);
        runner.Add("RMF2 caller contracts allow locale selectors and omitted inputs", Contracts);
        runner.Add("RMF2 rejects unsupported execution options without silently changing semantics", Capabilities);
        runner.Add("RMF2 metadata validates examples and parameter names", Metadata);
    }
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    internal static TranslationSource Project(string extra = "") => Source("translations/runic.json",
        "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\",\"sourceLayout\":\"rmf2-v1\"" + extra + "}");
    private static void Composition()
    {
        TranslationCompilation flat = TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", "checkout {\n  cart {\n    title = Cart\n  }\n}\n")]);
        TranslationCompilation split = TranslationCompiler.CompileProject(Project(), [Source("translations/checkout/cart/en.rmf2", "title = Cart\n")]);
        TranslationCompilation mounted = TranslationCompiler.CompileProject(Project(",\"sourceRoots\":[{\"path\":\"../src/features/shop/i18n\",\"namespace\":[\"checkout\"]}]"),
            [Source("src/features/shop/i18n/cart/en.rmf2", "title = Cart\n")]);
        foreach (var result in new[] { flat, split, mounted }) Assert.True(result.Success, Errors(result));
        Assert.Equal(flat.Catalogs[0].Fingerprint, split.Catalogs[0].Fingerprint);
        Assert.Equal(flat.Catalogs[0].Fingerprint, mounted.Catalogs[0].Fingerprint);
        Assert.Equal("checkout_cart_title", flat.Catalogs[0].CanonicalResources[0].Key);
    }
    private static void Framing()
    {
        const string text = "# 😀 context\r\n@param $name - Name\r\nhello = Hello {$name} # now = yes\r\nlines =\r\n\r\n  {{مرحبا 😀\r\n    Indent\r\n\r\n  }}\r\n\r\nempty = {{}}\r\n";
        var result = Rmf2ResourceReader.Read(Source("en.rmf2", text));
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.Equal("😀 context", result.Nodes[0].Comments[0]);
        Assert.Equal("Hello {$name} # now = yes", result.Nodes[0].Message);
        Assert.Equal("{{مرحبا 😀\n  Indent\n\n}}", result.Nodes[1].Message);
        var node = result.Nodes[1]; byte[] raw = Encoding.UTF8.GetBytes(text), extracted = Encoding.UTF8.GetBytes(node.Message!);
        Assert.Equal(extracted.Length + 1, node.MessageByteMap.Count);
        for (int i = 0; i < extracted.Length; i++)
            Assert.True(extracted[i] == raw[node.MessageByteMap[i]] || (extracted[i] == 10 && raw[node.MessageByteMap[i]] == 13), "Byte map lost original position.");
        Assert.True(TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", text)]).Success, "Valid framing failed compilation.");
    }
    private static void Recovery()
    {
        foreach (string invalid in new[] { "missing =\nlater = OK", "bad ???\nlater = OK", "a {\n\tx = X\n}\nlater = OK", "x =\n    First\n  Wrong margin\nlater = OK", "@example {}\n\nlater = OK" })
        {
            var result = Rmf2ResourceReader.Read(Source("en.rmf2", invalid));
            Assert.True(!result.Success && result.Nodes.Any(n => n.Key == "later"), "Recovery lost later entry: " + invalid);
        }
        var mf2 = Rmf2ResourceReader.Read(Source("en.rmf2", "bad = {unfinished\nlater = OK"));
        Assert.Equal(2, mf2.Nodes.Count);
        Assert.True(!TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", "bad = {unfinished\nlater = OK")]).Success, "Invalid MF2 compiled.");
    }
    private static void Collisions()
    {
        foreach (string text in new[] { "a = A\na {\n  b = B\n}", "a_b {\n  c = C\n}\na {\n  b_c = C\n}", "a = A\na = B" })
            Assert.True(!TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", text)]).Success, "Collision accepted.");
        var result = TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", "a {\n  b = B\n}"), Source("translations/a/en.rmf2", "b = B")]);
        Assert.True(result.Diagnostics.Count(d => d.Id == "RTR0054") >= 2, "Duplicate must report both locations.");
        Assert.True(TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", "a {\n  b = B\n}\na {\n  c = C\n}")]).Success, "Reopened group rejected.");
    }
    private static void Contracts()
    {
        const string source = "items =\n  .input {$count :integer}\n  .match $count\n  one {{One}}\n  * {{{$count} items}}\n";
        var result = TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", source), Source("translations/de.rmf2", "items = Artikel")]);
        Assert.True(result.Success, Errors(result));
        Assert.Equal("plural", result.Catalogs[0].CanonicalResources[0].Message.Selectors[0].Function);
        Assert.True(!TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", source), Source("translations/de.rmf2", "items = {$newInput}")]).Success, "New caller input accepted.");
    }
    private static void Capabilities()
    {
        foreach (string expression in new[] { "{$n :number style=currency}", "{$n :number maximumFractionDigits=99}", "{$n :number maximumFractionDigits=$digits}", "{$n :integer useGrouping=sometimes}", "{$n :number bogus=1}", "{$n :UNKNOWN}" })
        {
            var result = TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", "bad = " + expression)]);
            Assert.True(!result.Success && result.Diagnostics.Any(d => d.Id == "RTR0065"), "Unsupported execution semantics silently accepted: " + expression);
        }
        var aliases = TranslationCompiler.CompileProject(Project(), [Source("translations/Shop/en.rmf2", "x = X"), Source("translations/shop/de.rmf2", "y = Y")]);
        Assert.True(aliases.Diagnostics.Any(d => d.Id == "RTR0052"), "Case-only directory aliases accepted.");
    }
    private static void Metadata()
    {
        foreach (string metadata in new[] { "@param $missing - nope", "@example {\"count\":\"wrong\"}", "@example []" })
            Assert.True(!TranslationCompiler.CompileProject(Project(), [Source("translations/en.rmf2", metadata + "\nitems = {$count :integer}")]).Success, "Invalid metadata accepted.");
    }
    private static string Errors(TranslationCompilation result) => string.Join("\n", result.Diagnostics.Select(d => d.Location + " " + d.Message));
}
