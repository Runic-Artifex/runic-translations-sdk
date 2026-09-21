using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class Mf2SyntaxTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("MF2 grammar and data-model errors remain separate from backend limits", Grammar);
        runner.Add("MF2 declarations reject prior-variable bindings and input self-options", DeclarationBindings);
        runner.Add("RMF2 lowers multiline expressions and quoted variant keys through the shared model", Lowering);
        runner.Add("RMF2 language service uses project contracts and semantic variables", LanguageService);
        runner.Add("RMF2 literal locals and aliases remain internal to the caller contract", LiteralLocals);
        runner.Add("MF2 syntax preserves unsupported expressions and exact Unicode/CRLF token spans", Lossless);
        runner.Add("MF2 syntax preserves declarations selectors and variant order", Declarations);
        runner.Add("MF2 overlapping markup is valid syntax and a separate inline profile error", Profile);
        runner.Add("MF2 syntax distinguishes literal/variable options and valueless attributes", Properties);
    }
    private static Mf2SyntaxDocument Read(string text) => Mf2SyntaxReader.Read(new TranslationSource("message.mf2", Encoding.UTF8.GetBytes(text)));
    private static void Grammar()
    {
        foreach (string value in new[] { "{{unterminated", "{{ok}} trailing", ".bogus {{x}}", ".local $a = {|x|}", "{$1bad}", "{$a:number}", "{$a :number@note}", "{#}", "text }", "{x option=1}", "{$a @note=$b}", "{|bad\\q|}" })
            Assert.True(!Read(value).Success, "Invalid MF2 syntax accepted: " + value);
        foreach (string value in new[] { "{$😀}", "{$a.b}", "{+literal}", ".local\n$word = {\n|Hi|\n} {{Hello {$word}}}", "{#x @note}{/x @note}" })
            Assert.True(Read(value).Success, "Valid MF2 syntax rejected: " + value);
        Assert.Equal(0, Mf2SyntaxReader.ValidateDataModel(Read(".match $name\n|*| {{Literal}}\n* {{Other}}")).Count);
        var variants = Read(".match $name\n|first last| {{😀}}\n* {{Other}}");
        var variant = variants.Variants[0];
        Assert.Equal("|first last| {{😀}}", Encoding.UTF8.GetString(variants.Source.GetUtf8Bytes(), variant.Location.StartByte, variant.Location.LengthBytes));
        Assert.Equal("{{😀}}", Encoding.UTF8.GetString(variants.Source.GetUtf8Bytes(), variant.PatternLocation.StartByte, variant.PatternLocation.LengthBytes));
        foreach (string value in new[] { ".local $x = {$later} .local $later = {1} {{x}}", ".input {$x} .input {$x} {{x}}", ".match $x\na {{A}}", ".match $x\na {{A}} a {{Again}} * {{Other}}" })
        {
            var syntax = Read(value); Assert.True(syntax.Success, "Data-model constraint rejected by syntax parser.");
            Assert.True(Mf2SyntaxReader.ValidateDataModel(syntax).Any(d => d.Id == "RTR0067"), "Missing data-model diagnostic.");
        }
    }
    private static void DeclarationBindings()
    {
        foreach (string source in new[] {
            ".local $a = {$n} .input {$n} {{x}}",
            ".local $a = {$n} .input {$n :number style=percent} {{x}}",
            ".local $a = {$n} .input {$n :integer select=ordinal} .match $a one {{one}} * {{other}}",
            ".input {$n :number maximumFractionDigits=$digits} .input {$digits :integer} {{x}}",
            ".input {$n :number style=$style} .local $style = {|percent|} {{x}}",
            ".local $a = {1 :number select=$selection} .input {$selection :string} {{x}}",
            ".input {$n} .local $n = {1} {{x}}",
            ".local $n = {1} .input {$n} {{x}}",
            ".input {$n :number maximumFractionDigits=$n} {{x}}",
            ".input {$s :string select=$s} {{x}}",
            ".input {$n :number minimumFractionDigits=$n maximumFractionDigits=$n} {{x}}",
            ".input {$é :number maximumFractionDigits=$e\u0301} {{x}}",
            ".input {$n} .input {$n :number maximumFractionDigits=$n} {{x}}",
            ".local $a = {$e\u0301 @note=|雪|}\r\n.input {$é :number}\r\n{{x}}" })
        {
            var syntax = Read(source);
            Assert.True(syntax.Success, "Duplicate declaration must remain a data-model error: " + source);
            var binding = syntax.Declarations[^1];
            var diagnostic = Assert.Single(Mf2SyntaxReader.ValidateDataModel(syntax).Where(d => d.Message == "Duplicate declaration '" + binding.Name + "'.").ToArray());
            Assert.Equal("RTR0067", diagnostic.Id);
            Assert.Equal(TranslationDiagnosticSeverity.Error, diagnostic.Severity);
            Assert.True(ReferenceEquals(binding.NameLocation, diagnostic.Location), "Duplicate declaration lost the invalid binding's exact source location.");
            Assert.Equal("$" + binding.Name, Encoding.UTF8.GetString(syntax.Source.GetUtf8Bytes(), diagnostic.Location.StartByte, diagnostic.Location.LengthBytes));
        }
        foreach (string source in new[] {
            ".input {$n :number} .local $a = {$n} {{x}}",
            ".input {$n} {{x}}",
            ".input {$n :number maximumFractionDigits=2 @note=|$n|} {{x}}",
            ".input {$s :string select=exact} {{x}}",
            ".input {$n :n n=|$n| @n=|$n|} {{x}}",
            ".input {$digits :integer} .input {$n :number maximumFractionDigits=$digits} {{x}}",
            ".input {$n :integer select=ordinal} .local $a = {$n} .match $a one {{one}} * {{other}}",
            ".local $a = {|$n| :string @note=|$n|} .input {$n} {{x}}",
            ".local $a = {1 :n n=n @n=|$n|} .input {$n} {{x}}",
            ".local $a = {$n} .local $b = {$n} {{x}}" })
        {
            var syntax = Read(source);
            Assert.True(syntax.Success, "Ordered control failed syntax: " + source);
            Assert.Equal(0, Mf2SyntaxReader.ValidateDataModel(syntax).Count);
        }
        foreach (string source in new[] {
            ".local $a = {$b} .local $b = {1} {{x}}",
            ".local $a = {$b} .local $b = {$a} {{x}}",
            ".local $a = {$a} {{x}}" })
            Assert.True(Mf2SyntaxReader.ValidateDataModel(Read(source)).Any(d => d.Id == "RTR0067" && d.Message.Contains("referenced before its declaration", StringComparison.Ordinal)), "Local ordering/cycle check was lost.");
    }
    private static void Lowering()
    {
        const string text = "message =\n  .input {\n    $name :string\n  }\n  .match $name\n  |first last| {{Found {bare} {|a\\|b|}}}\n  * {{Other}}\n";
        var result = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes(text))]);
        Assert.True(result.Success, string.Join(";", result.Diagnostics.Select(d => d.Message)));
        var variant = result.Catalogs[0].CanonicalResources[0].Message.Variants[0];
        Assert.Equal("first last", variant.Matches["name"]);
        Assert.Equal("Found bare a|b", string.Concat(variant.Pattern.Nodes.OfType<CompiledMessageText>().Select(n => n.Value)));
    }
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
        Assert.True(service.Complete(syntax, position + 5).Any(item => item.Label == "good"), "Missing allowed option values.");
        var link = Read("{#link ref=terms}Terms{/link}");
        Assert.True(service.Complete(link, 15, link).Any(item => item.Label == "terms"), "Missing base-message slot completion.");
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
        var formattedAlias = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x =\n  .input {$name :string}\n  .local $alias = {$name}\n  {{{$alias :string}}}"))]);
        Assert.True(formattedAlias.Success, string.Join(";", formattedAlias.Diagnostics.Select(d => d.Message)));
        Assert.Equal(1, formattedAlias.Catalogs[0].CanonicalResources[0].Placeholders.Count);
        Assert.Equal("name", formattedAlias.Catalogs[0].CanonicalResources[0].Placeholders[0].Name);
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
