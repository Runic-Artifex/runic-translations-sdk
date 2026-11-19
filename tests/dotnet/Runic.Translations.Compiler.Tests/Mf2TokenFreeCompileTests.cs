using System;
using System.Linq;
using System.Text;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// A compile reads its messages without their token streams (Mf2SyntaxReader.Read with tokens: false): lowering walks the
// text between a pattern's expressions, and the data-model and inline-profile checks read a valid message's expressions.
// These are the results the token-based implementation gave for the same sources (runic-translations 0.15 before #271
// step 8); a syntax error still goes through the tokens.
[Category("rmf2-semantic-v5")]
internal sealed class Mf2TokenFreeCompileTests
{
    private const string Project = """
        { "schemaVersion": 1, "catalog": "edge", "code": { "namespace": "Edge", "className": "EdgeText" }, "baseLocale": "en", "locales": ["en", { "tag": "de", "fallback": "en" }],
          "validation": { "translationCompleteness": "allow" }, "sourceRoots": [{ "path": "t", "namespace": [] }] }
        """;

    private const string InvalidEnglish = """
        forward =
          .local $a = {$b :string}
          .local $b = {|x|}
          {{A {$a} B {$b}}}
        forward_option =
          .local $n = {$count :number minimumFractionDigits=$digits}
          .local $digits = {2 :integer}
          {{{$n}}}
        input_option =
          .input {$count :number minimumFractionDigits=$late}
          .local $late = {1 :integer}
          {{{$count}}}
        unclosed_body =
          .input {$name :string}
          {{Hello {#b}{$name}}}
        close_first = Text {/b} more {#b}x{/b}
        overlap = {#b}{#i}x{/b}{/i}
        across =
          .input {$count :integer}
          .match $count
          one {{{#b}one}}
          * {{other{/b}}}
        balanced_variants =
          .input {$count :integer}
          .match $count
          one {{{#b}one{/b} {$count}}}
          * {{{#i}many{/i} \{x\} \\ {$count}}}
        leading =    spaced {$v} text \{ escaped \} {#b}bold{/b}
        unicode = Grüße {$name} 😀 ありがとう {$n :number} Ende
        standalone = before {#br/} after

        """;

    private const string InvalidGerman = """
        across =
          .input {$count :integer}
          .match $count
          one {{eins{/b}}}
          * {{{#b}andere}}
        leading = Text {$v} {#b}fett{/b}
        unicode = Grüße {$name} {$n :number}

        """;

    private static readonly string[] InvalidDiagnostics =
    [
        "edge/t/de.rmf2(4,13,4,17): error RTR0061: The inline renderer requires balanced, non-overlapping markup.",
        "edge/t/de.rmf2(5,7,5,11): error RTR0061: The inline renderer requires a closing tag for 'b'.",
        "edge/t/de.rmf2(6,1,6,8): error RTR0061: Unknown markup 'b'.",
        "edge/t/de.rmf2(6,1,6,8): error RTR0061: Unknown markup 'b'.",
        "edge/t/en.rmf2(2,16,2,18): error RTR0067: Local 'b' is referenced before its declaration.",
        "edge/t/en.rmf2(3,10,3,12): error RTR0067: Duplicate declaration 'b'.",
        "edge/t/en.rmf2(6,53,6,60): error RTR0067: Local 'digits' is referenced before its declaration.",
        "edge/t/en.rmf2(7,10,7,17): error RTR0067: Duplicate declaration 'digits'.",
        "edge/t/en.rmf2(10,48,10,53): error RTR0067: Local 'late' is referenced before its declaration.",
        "edge/t/en.rmf2(11,10,11,15): error RTR0067: Duplicate declaration 'late'.",
        "edge/t/en.rmf2(15,11,15,15): error RTR0061: The inline renderer requires a closing tag for 'b'.",
        "edge/t/en.rmf2(16,20,16,24): error RTR0061: The inline renderer requires balanced, non-overlapping markup.",
        "edge/t/en.rmf2(17,11,17,15): error RTR0061: The inline renderer requires a closing tag for 'b'.",
        "edge/t/en.rmf2(17,20,17,24): error RTR0061: The inline renderer requires balanced, non-overlapping markup.",
        "edge/t/en.rmf2(21,9,21,13): error RTR0061: The inline renderer requires a closing tag for 'b'.",
        "edge/t/en.rmf2(22,12,22,16): error RTR0061: The inline renderer requires balanced, non-overlapping markup.",
        "edge/t/en.rmf2(23,1,23,18): error RTR0061: Unknown markup 'b'.",
        "edge/t/en.rmf2(23,1,23,18): error RTR0061: Unknown markup 'b'.",
        "edge/t/en.rmf2(23,1,23,18): error RTR0061: Unknown markup 'i'.",
        "edge/t/en.rmf2(23,1,23,18): error RTR0061: Unknown markup 'i'.",
        "edge/t/en.rmf2(28,1,28,8): error RTR0061: Unknown markup 'b'.",
        "edge/t/en.rmf2(28,1,28,8): error RTR0061: Unknown markup 'b'.",
    ];

    private const string ValidEnglish = """
        backward =
          .local $b = {|x|}
          .local $a = {$b :string}
          {{A {$a} B {$b} \{literal\} \\ end}}
        option_ref =
          .local $digits = {2 :integer}
          .input {$count :number minimumFractionDigits=$digits}
          {{Count: {$count}}}
        balanced =
          .input {$count :integer}
          .match $count
          one {{{#strong}one{/strong} {$count}}}
          * {{{#em}many{/em} \{x\} \\ {$count} tail }}
        leading =    spaced {$v} text \{ escaped \} {#strong}bold{/strong}
        unicode = Grüße {$name} 😀 ありがとう {$n :number} Ende
        body_markup =
          .input {$name :string}
          {{  Hello {#strong}{$name}{/strong}, \}\} and {|lit|} }}
        plain = Just text with \\ backslash
        only_placeholder = {$x}

        """;

    [Test, DisplayName("The data-model and inline-profile checks of a compile report what the token-based checks reported")]
    public void ChecksMatchTheTokenBasedOnes()
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source("edge/runic.json", Project),
            [Source("edge/t/en.rmf2", InvalidEnglish), Source("edge/t/de.rmf2", InvalidGerman)]);
        string[] actual = compilation.Diagnostics.Select(Format).ToArray();
        Assert.Equal(string.Join("\n", InvalidDiagnostics), string.Join("\n", actual));
    }

    [Test, DisplayName("Lowering walks a pattern's text and expressions as the token-based walk did")]
    public void LoweringMatchesTheTokenBasedWalk()
    {
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(Source("edge/runic.json", Project), [Source("edge/t/en.rmf2", ValidEnglish)]);
        Assert.True(compilation.Success, string.Join("; ", compilation.Diagnostics.Select(Format)));
        Rmf2LocaleV5 english = compilation.Project!.Locales.Single(locale => locale.Tag == "en");
        string actual = string.Join("\n", english.DirectResources.Select(resource => resource.Key + ": " + string.Join(" | ",
            resource.Message.Variants.Select(variant => string.Join(" ", variant.Nodes.Select(Describe))))));
        Assert.Equal(string.Join("\n",
            "backward: «A » {$a} « B » {$b} « {literal} \\ end»",
            "balanced: <open runic:strong> «one» <close runic:strong> « » {$count} | <open runic:em> «many» <close runic:em> « {x} \\ » {$count} « tail »",
            "body_markup: «  Hello » <open runic:strong> {$name} <close runic:strong> «, }} and » {lit} « »",
            "leading: «   spaced » {$v} « text { escaped } » <open runic:strong> «bold» <close runic:strong>",
            "only_placeholder: {$x}",
            "option_ref: «Count: » {$count}",
            "plain: «Just text with \\ backslash»",
            "unicode: «Grüße » {$name} « 😀 ありがとう » {$n} « Ende»"), actual);
    }

    private static string Describe(Rmf2NodeV5 node) => node switch
    {
        Rmf2TextV5 text => "«" + text.Value + "»",
        Rmf2ExpressionNodeV5 expression => "{" + (expression.Expression.Operand.Kind is "input" or "local" ? "$" : "") + expression.Expression.Operand.Value + "}",
        Rmf2MarkupV5 markup => "<" + markup.MarkupKind + " " + markup.Name + ">",
        _ => node.GetType().Name,
    };

    private static string Format(TranslationDiagnostic diagnostic) =>
        $"{diagnostic.Location.Path}({diagnostic.Location.Line},{diagnostic.Location.Column},{diagnostic.Location.EndLine},{diagnostic.Location.EndColumn}): " +
        $"{(diagnostic.Severity == TranslationDiagnosticSeverity.Error ? "error" : "warning")} {diagnostic.Id}: {diagnostic.Message}";

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
