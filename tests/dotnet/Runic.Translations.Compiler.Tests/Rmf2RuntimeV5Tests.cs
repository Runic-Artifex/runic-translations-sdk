using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2RuntimeV5Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 normalized compiler values execute without a v4 adapter", CompilerValues);
        runner.Add("RMF2 v5 locale grammars execute against the same typed caller contract", CallerContracts);
        runner.Add("RMF2 v5 normalized annotation and markup ordering survives runtime lowering", Annotations);
        runner.Add("RMF2 v5 declaration rebinding cannot reach runtime execution", DeclarationRebinding);
    }
    private static void CompilerValues()
    {
        var message = Compile(".input {$n :number}\n.input {$digits :integer}\n.local $percent = {$n :number style=percent maximumFractionDigits=$digits}\n.local $alias = {$percent}\n.local $decimal = {$alias :number style=decimal}\n{{{$alias} / {$decimal} / {1e+2 :number}}}");
        Assert.Equal("12.35% / 0.123456 / 100", message.Format([new("n", .123456m), new("digits", 2L)], "en"));
        var integer = Compile(".input {$n :integer}\n.local $a = {$n :number style=percent}\n.local $b = {$a}\n{{{$b :integer}}}");
        Assert.Equal("42", integer.Format([new("n", 42L)], "en"));
        var selection = Compile(".local $n = {42 :number style=percent}\n.local $a = {$n}\n.match $a\nother {{category}}\n42 {{exact}}\n* {{fallback}}");
        Assert.Equal("exact", selection.Format([], "en"));
    }
    private static void CallerContracts()
    {
        var english = Compile(".input {$n :integer}\n.match $n\none {{one}}\n* {{{$n} files}}");
        var french = Compile(".input {$n :integer}\n.local $display = {$n :number minimumFractionDigits=2}\n{{{$display} fichiers}}");
        Assert.Equal(english.Inputs.Span[0].Name, french.Inputs.Span[0].Name);
        Assert.Equal(english.Inputs.Span[0].Type, french.Inputs.Span[0].Type);
        Assert.Equal("one", english.Format([new("n", 1L)], "en"));
        Assert.Equal("1,00 fichiers", french.Format([new("n", 1L)], "fr"));
        var exact = Compile(".input {$n :number select=exact}\n.match $n\n|9007199254740993| {{exact}}\n* {{other}}");
        Assert.Equal("exact", exact.Format([new("n", 9007199254740993m)], "en"));
    }
    private static void Annotations()
    {
        var message = Compile("{{{#strong @open=||}{|ok| @flag @n=1e2}{/strong @close=||}}}");
        var content = message.FormatContent([], "en");
        Assert.Equal("open", content.Nodes.Span[0].Annotations.Span[0].Name);
        Assert.Equal("flag", content.Nodes.Span[1].Annotations.Span[0].Name);
        Assert.Equal("100", content.Nodes.Span[1].Annotations.Span[1].Value!.Canonical);
        Assert.Equal("close", content.Nodes.Span[2].Annotations.Span[0].Name);
    }
    private static void DeclarationRebinding()
    {
        foreach (string selector in new[] { "n", "a" })
        {
            RejectBeforeExecution(".local $a = {$n}\n.input {$n :number select=ordinal}\n.match $" + selector + "\nfew {{ordinal}}\n* {{fallback}}");
            RejectBeforeExecution(".local $a = {$n}\n.input {$n :number select=exact}\n.match $" + selector + "\n23 {{exact}}\n* {{fallback}}");
            var ordinal = Compile(".input {$n :number select=ordinal}\n.local $a = {$n}\n.match $" + selector + "\nfew {{ordinal}}\n* {{fallback}}");
            Assert.Equal("ordinal", ordinal.Format([new("n", 23m)], "en"));
            Assert.Equal("fallback", ordinal.Format([new("n", 13m)], "en"));
            var exact = Compile(".input {$n :number select=exact}\n.local $a = {$n}\n.match $" + selector + "\n23 {{exact}}\n* {{fallback}}");
            Assert.Equal("exact", exact.Format([new("n", 23m)], "en"));
        }
        RejectBeforeExecution(".local $a = {$n :number}\n.input {$n :number style=percent}\n{{{$a}}}");
        RejectBeforeExecution(".local $a = {1.234 :number maximumFractionDigits=$digits}\n.input {$digits :integer}\n{{{$a}}}");
        RejectBeforeExecution(".input {$n :number style=$style}\n.input {$style :string}\n{{{$n}}}");
    }
    private static void RejectBeforeExecution(string text)
    {
        var result = Rmf2SemanticCompilerV5.Compile(new TranslationSource("test.mf2", Encoding.UTF8.GetBytes(text)));
        if (!result.Success) return;
        // Even an older semantic producer cannot activate this invalid model.
        try { _ = Lower(result.Message!); }
        catch (ArgumentException exception) when (exception.Message.Contains("Duplicate v5 declaration", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Declaration rebinding reached runtime execution.");
    }
    // Test-only direct lowering. Project linking, generated emission and pack
    // dispatch deliberately remain outside this runtime implementation slice.
    private static CompiledRmf2Message Compile(string text)
    {
        var result = Rmf2SemanticCompilerV5.Compile(new TranslationSource("test.mf2", Encoding.UTF8.GetBytes(text)));
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(item => item.Message)));
        return Lower(result.Message!);
    }
    internal static CompiledRmf2Message Lower(Rmf2MessageV5 message) => new(message.Inputs.Select(item => new CompiledRmf2Input(item.Name, Type(item.Type))).ToArray(),
            message.Declarations.Select(item => new CompiledRmf2Declaration(item.Kind, item.Name, Expression(item.Expression))).ToArray(),
            message.Selectors.Select(item => new CompiledRmf2Selector(Value(item.Value), Type(item.Type), item.Function)).ToArray(),
            message.Variants.Select(item => new CompiledRmf2Variant(item.Keys.Select(key => new CompiledRmf2Key(key.Value, key.Canonical)).ToArray(), item.Nodes.Select(Node).ToArray())).ToArray());
    private static CompiledRmf2Value Value(Rmf2ValueV5 value) => new(value.Kind, value.Value, value.Canonical);
    private static CompiledRmf2Option Option(Rmf2OptionV5 option) => new(option.Name, Value(option.Value));
    private static CompiledRmf2Annotation Annotation(Rmf2AnnotationV5 annotation) => new(annotation.Name, annotation.Value is null ? null : Value(annotation.Value));
    private static CompiledRmf2Expression Expression(Rmf2ExpressionV5 expression) => new(Value(expression.Operand), Type(expression.ValueType), expression.Function,
        expression.Options.Select(Option).ToArray(), expression.Annotations.Select(Annotation).ToArray());
    private static CompiledRmf2Node Node(Rmf2NodeV5 node) => node switch
    {
        Rmf2TextV5 text => new(text.Value), Rmf2ExpressionNodeV5 expression => new(Expression(expression.Expression)),
        Rmf2MarkupV5 markup => new(markup.Name, markup.MarkupKind, markup.Options.Select(Option).ToArray(), markup.Annotations.Select(Annotation).ToArray()),
        _ => throw new InvalidOperationException("Unexpected normalized node."),
    };
    private static TextArgumentType Type(string type) => type switch
    {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number, "boolean" => TextArgumentType.Bool,
        "date" => TextArgumentType.Date, "time" => TextArgumentType.Time, "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid,
        _ => throw new InvalidOperationException("Unexpected normalized carrier."),
    };
}
