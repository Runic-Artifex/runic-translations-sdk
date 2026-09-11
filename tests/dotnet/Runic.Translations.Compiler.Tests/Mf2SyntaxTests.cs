using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class Mf2SyntaxTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 language service uses project contracts and semantic variables", LanguageService);
        runner.Add("RMF2 literal locals and aliases remain internal to the caller contract", LiteralLocals);
        runner.Add("MF2 syntax preserves unsupported expressions and exact Unicode/CRLF token spans", Lossless);
        runner.Add("MF2 syntax preserves declarations selectors and variant order", Declarations);
        runner.Add("MF2 overlapping markup is valid syntax and a separate inline profile error", Profile);
        runner.Add("MF2 syntax distinguishes literal/variable options and valueless attributes", Properties);
    }
    private static Mf2SyntaxDocument Read(string text) => Mf2SyntaxReader.Read(new TranslationSource("message.mf2", Encoding.UTF8.GetBytes(text)));
    private static void LanguageService()
    {
        const string project = """
        {"markup":{"contracts":[{"name":"app:badge","kind":"standalone","children":"none","plainText":"omit","options":{"tone":{"type":"enum","values":["good","bad"],"literalOnly":true}}}],"aliases":{"badge":"app:badge"}}}
        """;
        var service = Rmf2LanguageService.Create(new TranslationSource("runic.json", Encoding.UTF8.GetBytes(project)));
        Assert.Equal(0, service.Diagnostics.Count);
        var syntax = Read(".local $label = {|$fake|}\n{{{#badge tone=good/} {$label} {$name}}}");
        int position = Encoding.UTF8.GetByteCount(Encoding.UTF8.GetString(syntax.Source.GetUtf8Bytes()).Split("tone=")[0]);
        var items = service.Complete(syntax, position);
        Assert.True(items.Any(i => i.Label == "#badge") && items.All(i => i.Label != "/badge"), "Standalone alias completions are incorrect.");
        Assert.True(items.Any(i => i.Label == "$label") && items.All(i => i.Label != "$fake"), "Literal text leaked into symbol completion.");
        Assert.True(items.All(i => i.Label != "tone="), "An existing option was suggested twice.");
        Assert.True(service.Hover(syntax, position)!.Contains("literal only", StringComparison.Ordinal), "Option hover lost its contract.");
        var incomplete = Read("{#badge }");
        Assert.True(service.Complete(incomplete, 3).Any(i => i.Label == "tone=" && i.Detail.Contains("good, bad", StringComparison.Ordinal)), "Missing custom enum option.");
    }
    private static void LiteralLocals()
    {
        var compilation = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x =\n  .local $literal = {|Hello|}\n  .local $alias = {$literal}\n  {{{$alias} {$name}}}"))]);
        Assert.True(compilation.Success, string.Join(";", compilation.Diagnostics.Select(d => d.Message)));
        Assert.Equal(1, compilation.Catalogs[0].CanonicalResources[0].Placeholders.Count);
        Assert.Equal("name", compilation.Catalogs[0].CanonicalResources[0].Placeholders[0].Name);
        var output = Runic.Translations.Compiler.Generation.TranslationOutputRenderer.RenderLocaleJson(compilation.Catalogs[0], "en").Text;
        Assert.True(output.Contains("Hello", StringComparison.Ordinal), "Literal local value was lost.");
        var unsupported = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x =\n  .local $literal = {42}\n  .local $formatted = {$literal :number}\n  {{{$formatted}}}"))]);
        Assert.True(!unsupported.Success && unsupported.Diagnostics.Any(d => d.Id == "RTR0065"), "Formatting a constant alias must not create a caller input.");
    }
    private static void Lossless()
    {
        const string text = ".local $value = {|😀\\|雪| :vendor:unknown dynamic=$other @note=|safe|}\r\n{{Value {$value}}}";
        var syntax = Read(text);
        Assert.True(syntax.Success, string.Join(";", syntax.Diagnostics.Select(d => d.Message)));
        Assert.Equal(text, string.Concat(syntax.Tokens.Select(t => t.Raw)));
        byte[] raw = Encoding.UTF8.GetBytes(text); int offset = 0;
        foreach (var token in syntax.Tokens)
        {
            Assert.Equal(offset, token.Location.StartByte);
            Assert.Equal(token.Raw, Encoding.UTF8.GetString(raw, token.Location.StartByte, token.Location.LengthBytes));
            offset += token.Location.LengthBytes;
        }
        Assert.Equal(raw.Length, offset);
        var expression = syntax.Expressions[0];
        Assert.Equal("vendor:unknown", expression.Function);
        Assert.Equal("😀|雪", expression.Operand!.Value);
        Assert.Equal(Mf2OperandKind.Variable, expression.Options[0].Value!.Kind);
    }
    private static void Declarations()
    {
        var syntax = Read(".input {$count :integer}\n.local $word = {|one|}\n.match $count $word\none |one| {{First}}\n* * {{Second}}");
        Assert.True(syntax.Success, "Valid syntax failed.");
        Assert.Equal(2, syntax.Declarations.Count);
        Assert.Equal("word", syntax.Declarations[1].Name);
        Assert.Equal("count", syntax.Match!.Selectors[0]);
        Assert.Equal(2, syntax.Variants.Count);
        Assert.Equal("|one|", syntax.Variants[0].Keys[1]);
        Assert.Equal("*", syntax.Variants[1].Keys[0]);
        Assert.True(Read("{{\n.match $text\n}}").Match is null, "Pattern text was misclassified as a selector.");
    }
    private static void Profile()
    {
        var syntax = Read("{{{#strong}{#em}Hi{/strong}{/em}}}");
        Assert.True(syntax.Success, "MF2 syntax rejected overlapping markup.");
        Assert.True(Mf2SyntaxReader.ValidateInlineProfile(syntax).Any(d => d.Id == "RTR0061"), "Inline profile accepted overlapping tags.");
        var compilation = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x = {#strong}Open"))]);
        Assert.True(compilation.Diagnostics.Any(d => d.Id == "RTR0061") && compilation.Diagnostics.All(d => d.Id != "RTR0041"), "Profile restriction reported as MF2 syntax error.");
    }
    private static void Properties()
    {
        var syntax = Read("{#vendor:tag literal=|$name| dynamic=$name @vendor:note @raw=|a b|/}");
        Assert.True(syntax.Success, string.Join(";", syntax.Diagnostics.Select(d => d.Message)));
        var expression = syntax.Expressions[0];
        Assert.Equal(Mf2MarkupKind.Standalone, expression.MarkupKind);
        Assert.Equal(Mf2OperandKind.String, expression.Options[0].Value!.Kind);
        Assert.Equal(Mf2OperandKind.Variable, expression.Options[1].Value!.Kind);
        Assert.True(expression.Attributes[0].Value is null, "Valueless attribute lost.");
        Assert.Equal("a b", expression.Attributes[1].Value!.Value);
        Assert.True(!Read("{$name :number option=1 option=2}").Success, "Duplicate option accepted.");
    }
}
