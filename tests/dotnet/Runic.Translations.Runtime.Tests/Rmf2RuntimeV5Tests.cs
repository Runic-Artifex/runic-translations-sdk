using System;
using System.Collections.Generic;
using System.Globalization;
using Runic.Translations;

namespace Runic.Translations.Runtime.Tests;

internal static class Rmf2RuntimeV5Tests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("v5 formatted locals inherit then replace metadata without changing carriers", LocalInheritance);
        runner.Add("v5 literals cover every typed carrier without caller inputs", Literals);
        runner.Add("v5 finite dynamic options validate enums ranges and coupled defaults", DynamicOptions);
        runner.Add("v5 dynamic options follow typed local dependencies", OptionLocals);
        runner.Add("v5 exact numeric keys outrank categories independent of source order", NumericRanking);
        runner.Add("v5 selector ranks compare lexicographically and preserve literal stars", LexicographicRanking);
        runner.Add("v5 forward input selection metadata reaches earlier locals and direct selectors", ForwardInputSelection);
        runner.Add("v5 canonical decimals preserve exact precision and CLDR operands", DecimalSelection);
        runner.Add("v5 annotations remain inert ordered and separate on closing markup", Annotations);
        runner.Add("v5 caller contracts preserve NFC identities and ignore presentation hints", CallerContracts);
        runner.Add("v5 hostile model construction rejects malformed reference and option graphs", InvalidModels);
        runner.Add("v5 hostile decimal spellings reject overflow underflow and rounding", DecimalDomain);
        runner.Add("v5 output bounds and locale capability errors use public format errors", RuntimeErrors);
        runner.Add("v5 snapshot constant evaluation content locale and contract checking are additive", SnapshotDispatch);
        runner.Add("v5 immutable arrays and compatibility checks preserve v4 ABI", Compatibility);
    }
    private static CompiledRmf2Value Input(string name) => new("input", name);
    private static CompiledRmf2Value Local(string name) => new("local", name);
    private static CompiledRmf2Value Text(string value) => new("string-literal", value);
    private static CompiledRmf2Value Number(string value, string? canonical = null) => new("number-literal", value, canonical ?? value);
    private static CompiledRmf2Expression Expr(CompiledRmf2Value value, TextArgumentType type, string? function = null, params CompiledRmf2Option[] options) => new(value, type, function, options);
    private static CompiledRmf2Declaration Declare(string name, TextArgumentType type, string? function = null) => new("input", name, Expr(Input(name), type, function));
    private static CompiledRmf2Message Simple(CompiledRmf2Input[] inputs, CompiledRmf2Declaration[] declarations, params CompiledRmf2Node[] nodes) => new(inputs, declarations, [], [new([], nodes)]);
    private static CompiledRmf2Node Output(CompiledRmf2Value value, TextArgumentType type, string? function = null, params CompiledRmf2Option[] options) => new(Expr(value, type, function, options));
    private static void LocalInheritance()
    {
        var message = Simple([new("amount", TextArgumentType.Number)], [Declare("amount", TextArgumentType.Number, "number"),
            new("local", "percent", Expr(Input("amount"), TextArgumentType.Number, "number", new CompiledRmf2Option("style", Text("percent")))),
            new("local", "alias", Expr(Local("percent"), TextArgumentType.Number)),
            new("local", "decimal", Expr(Local("alias"), TextArgumentType.Number, "number", new CompiledRmf2Option("style", Text("decimal"))))],
            Output(Local("alias"), TextArgumentType.Number), new(" / "), Output(Local("decimal"), TextArgumentType.Number));
        Assert.Equal("50% / 0.5", message.Format([new("amount", .5m)], "en"));
        var integer = Simple([new("n", TextArgumentType.Int)], [Declare("n", TextArgumentType.Int, "integer"),
            new("local", "a", Expr(Input("n"), TextArgumentType.Int, "number", new CompiledRmf2Option("style", Text("percent"))))], Output(Local("a"), TextArgumentType.Int, "integer"));
        Assert.Equal("2", integer.Format([new("n", 2L)], "en"));
        Assert.Throws<ArgumentException>(() => Expr(Input("n"), TextArgumentType.Number, "integer"));
    }
    private static void Literals()
    {
        (CompiledRmf2Value Value, TextArgumentType Type, string? Function, string Expected)[] cases =
        [
            (Text("hi"), TextArgumentType.String, null, "hi"), (Number("1e2", "100"), TextArgumentType.Number, null, "100"),
            (Number("-9223372036854775808"), TextArgumentType.Int, "integer", "-9223372036854775808"),
            (Text("true"), TextArgumentType.Bool, "runic:boolean", "true"),
            (Text("2024-02-29"), TextArgumentType.Date, "date", "2024-02-29"),
            (Text("23:59:58"), TextArgumentType.Time, "time", "23:59:58"),
            (Text("2024-02-29T23:59:58Z"), TextArgumentType.DateTime, "datetime", "2024-02-29T23:59:58Z"),
            (Text("00112233-4455-6677-8899-aabbccddeeff"), TextArgumentType.Guid, "runic:uuid", "00112233-4455-6677-8899-aabbccddeeff"),
        ];
        foreach (var item in cases) Assert.Equal(item.Expected, Simple([], [], Output(item.Value, item.Type, item.Function)).Format([], "en"));
        Assert.Equal("{00112233-4455-6677-8899-aabbccddeeff}", Simple([], [], Output(cases[7].Value, TextArgumentType.Guid, "runic:uuid", new CompiledRmf2Option("style", Text("b")))).Format([], "en"));
        Assert.Equal("yesterday", Simple([], [], Output(Number("-1"), TextArgumentType.Number, "runic:relative-time", new CompiledRmf2Option("numeric", Text("auto")))).Format([], "en"));
        Assert.Equal("1.3", Simple([], [], Output(Number("1.25"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Number("1")))).Format([], "en"));
        Assert.Equal("-1.3", Simple([], [], Output(Number("-1.25"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Number("1")))).Format([], "en"));
        Assert.Equal("-1", Simple([], [], Output(Number("-1"), TextArgumentType.Int, "integer")).Format([], "sv"));
        Assert.Equal("7922816251426433759354395033500%", Simple([], [], Output(Number("79228162514264337593543950335"), TextArgumentType.Number, "number", new CompiledRmf2Option("style", Text("percent")))).Format([], "en"));
    }
    private static void DynamicOptions()
    {
        var message = Simple([new("digits", TextArgumentType.Int), new("style", TextArgumentType.String)], [Declare("digits", TextArgumentType.Int), Declare("style", TextArgumentType.String)],
            Output(Number("1.234567"), TextArgumentType.Number, "number", new CompiledRmf2Option("style", Input("style")), new CompiledRmf2Option("minimumFractionDigits", Input("digits"))));
        Assert.Equal("1.234567", message.Format([new("digits", 0L), new("style", "decimal")], "en"));
        Assert.Equal("123.4567%", message.Format([new("digits", 4L), new("style", "percent")], "en"));
        foreach (long digits in new long[] { -1, 5, 6, 7, long.MaxValue })
            Assert.Throws<TranslationFormatException>(() => message.Format([new("digits", digits), new("style", "percent")], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([new("digits", 0L), new("style", "PERCENT")], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([new("digits", "2"), new("style", "percent")], "en"));
        var maximum = Simple([new("digits", TextArgumentType.Int)], [Declare("digits", TextArgumentType.Int)], Output(Number("1.2"), TextArgumentType.Number, "number", new CompiledRmf2Option("minimumFractionDigits", Number("3")), new CompiledRmf2Option("maximumFractionDigits", Input("digits"))));
        Assert.Throws<TranslationFormatException>(() => maximum.Format([new("digits", 2L)], "en"));
        (TextArgumentType Type, string Function, string Option, string Valid, string Invalid)[] enums =
        [ (TextArgumentType.Int,"integer","useGrouping","always","auto"), (TextArgumentType.Date,"date","style","long","medium"),
          (TextArgumentType.Time,"time","style","long","medium"), (TextArgumentType.DateTime,"datetime","style","short","full"),
          (TextArgumentType.Guid,"runic:uuid","style","p","P"), (TextArgumentType.Number,"runic:relative-time","unit","week","decade"),
          (TextArgumentType.Number,"runic:relative-time","numeric","auto","never") ];
        foreach (var item in enums)
        {
            var dynamic = Simple([new("option", TextArgumentType.String), new("value", item.Type)], [Declare("option", TextArgumentType.String)], Output(Input("value"), item.Type, item.Function, new CompiledRmf2Option(item.Option, Input("option"))));
            TextArgument value = item.Type switch { TextArgumentType.Int => new("value", 1234L), TextArgumentType.Number => new("value", 2m), TextArgumentType.Date => new("value", new DateOnly(2024, 1, 2)), TextArgumentType.Time => new("value", new TimeOnly(12, 34, 56)), TextArgumentType.DateTime => new("value", DateTimeOffset.UnixEpoch), _ => new("value", Guid.Empty) };
            Assert.True(dynamic.Format([new("option", item.Valid), value], "en").Length > 0, item.Function);
            Assert.Throws<TranslationFormatException>(() => dynamic.Format([new("option", item.Invalid), value], "en"));
        }
    }
    private static void OptionLocals()
    {
        var message = Simple([new("digits", TextArgumentType.Int)], [Declare("digits", TextArgumentType.Int),
            new("local", "alias", Expr(Input("digits"), TextArgumentType.Int, "number", new CompiledRmf2Option("style", Text("percent"))))], Output(Number("1.234"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Local("alias"))));
        Assert.Equal("1.23", message.Format([new("digits", 2L)], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([new("digits", 100L)], "en"));
        var constant = Simple([], [new("local", "digits", Expr(Number("2"), TextArgumentType.Int, "integer"))], Output(Number("1.234"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Local("digits"))));
        Assert.Equal("1.23", constant.Format([], "en"));
    }
    private static CompiledRmf2Message NumericMessage(params CompiledRmf2Variant[] variants) => new([new("n", TextArgumentType.Number)], [], [new(Input("n"), TextArgumentType.Number, "plural")], variants);
    private static void NumericRanking()
    {
        var message = NumericMessage(new CompiledRmf2Variant([new("one")], [new("category")]), new([new("1e0", "1")], [new("exact")]), new([new()], [new("fallback")]));
        Assert.Equal("exact", message.Format([new("n", 1.000m)], "en"));
        Assert.Equal("fallback", message.Format([new("n", 2m)], "en"));
        var percent = new CompiledRmf2Message([new("n", TextArgumentType.Number)], [new("input", "n", Expr(Input("n"), TextArgumentType.Number, "number", new CompiledRmf2Option("style", Text("percent"))))], [new(Input("n"), TextArgumentType.Number, "plural")],
            [new([new("one")], [Output(Input("n"), TextArgumentType.Number)]), new([new()], [new("other")])]);
        Assert.Equal("100%", percent.Format([new("n", 1m)], "en"));
    }
    private static void LexicographicRanking()
    {
        var message = new CompiledRmf2Message([new("a", TextArgumentType.Number), new("b", TextArgumentType.String)], [],
            [new(Input("a"), TextArgumentType.Number, "plural"), new(Input("b"), TextArgumentType.String, "exact")],
            [new([new("one"), new("*")], [new("category + exact")]), new([new("1", "1"), new()], [new("numeric first")]), new([new(), new()], [new("fallback")])]);
        Assert.Equal("numeric first", message.Format([new("a", 1m), new("b", "*")], "en"));
        var stars = new CompiledRmf2Message([new("s", TextArgumentType.String)], [], [new(Input("s"), TextArgumentType.String, "exact")], [new([new()], [new("wildcard")]), new([new("*")], [new("star")]), new([new("é")], [new("NFC")])]);
        Assert.Equal("star", stars.Format([new("s", "*")], "en"));
        Assert.Equal("NFC", stars.Format([new("s", "e\u0301")], "en"));
    }
    private static void DecimalSelection()
    {
        var message = NumericMessage(new CompiledRmf2Variant([new("9007199254740993", "9007199254740993")], [new("exact")]), new([new("one")], [new("one")]), new([new()], [new("other")]));
        Assert.Equal("exact", message.Format([new("n", 9007199254740993m)], "en"));
        Assert.Equal("other", message.Format([new("n", 9007199254740992m)], "en"));
        Assert.Equal("one", message.Format([new("n", 1.000m)], "en"));
        Assert.Equal("other", message.Format([new("n", 1.0000000000000000000000000001m)], "en"));
        var ordinal = new CompiledRmf2Message([new("n", TextArgumentType.Int)], [new("input", "n", Expr(Input("n"), TextArgumentType.Int, "integer", new CompiledRmf2Option("select", Text("ordinal"))))], [new(Input("n"), TextArgumentType.Int, "ordinal")], [new([new("few")], [new("rd")]), new([new()], [new("th")])]);
        Assert.Equal("rd", ordinal.Format([new("n", 23L)], "en"));
        Assert.Equal("th", ordinal.Format([new("n", 13L)], "en"));
    }
    private static void ForwardInputSelection()
    {
        foreach (string selection in new[] { "exact", "ordinal" })
        {
            CompiledRmf2Declaration[] declarations = [
                new("local", "alias", Expr(Input("n"), TextArgumentType.Number)),
                new("input", "n", Expr(Input("n"), TextArgumentType.Number, "number", new CompiledRmf2Option("select", Text(selection))))];
            foreach (var operand in new[] { Input("n"), Local("alias") })
            {
                var message = new CompiledRmf2Message([new("n", TextArgumentType.Number)], declarations,
                    [new(operand, TextArgumentType.Number, selection)],
                    [new([selection == "exact" ? new("23.0", "23") : new("few")], [new("selected")]), new([new()], [new("fallback")])]);
                Assert.Equal("selected", message.Format([new("n", 23m)], "en"));
                Assert.Equal("fallback", message.Format([new("n", 13m)], "en"));
            }
        }
        Assert.Throws<ArgumentException>(() => Simple([], [
            new("local", "alias", Expr(Local("later"), TextArgumentType.Number)),
            new("local", "later", Expr(Number("1"), TextArgumentType.Number))], new CompiledRmf2Node("x")));
    }
    private static void Annotations()
    {
        CompiledRmf2Annotation[] annotations = [new("flag"), new("empty", Text("")), new("number", Number("1e2", "100"))];
        var expression = new CompiledRmf2Expression(Text("ok"), TextArgumentType.String, annotations: annotations);
        var message = Simple([], [], new CompiledRmf2Node("strong", "open", annotations: annotations), new(expression), new("strong", "close", annotations: annotations));
        var content = message.FormatContent([], "en");
        Assert.Equal(0, content.Nodes.Span[0].Attributes.Length);
        foreach (var node in content.Nodes.Span)
        {
            Assert.Equal(3, node.Annotations.Length); Assert.Equal<CompiledRmf2Value?>(null, node.Annotations.Span[0].Value);
            Assert.Equal("", node.Annotations.Span[1].Value!.Value); Assert.Equal("100", node.Annotations.Span[2].Value!.Canonical);
        }
        Assert.Equal("ok", content.Nodes.Span[1].Value);
        Assert.Throws<TranslationFormatException>(() => message.Format([], "en"));
    }
    private static void CallerContracts()
    {
        var message = Simple([new("é", TextArgumentType.Number)], [], Output(Input("é"), TextArgumentType.Number));
        var value = TextArgument.CreateRmf2("é", new("_", 1.25m, TextArgumentFormat.Percent4));
        Assert.Equal("1.25", message.Format([value], "en"));
        Assert.Throws<ArgumentException>(() => TextArgument.CreateRmf2("e\u0301", new("_", 1m)));
        Assert.Throws<TranslationFormatException>(() => message.Format([], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([new("unknown", 1m)], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([default], "en"));
        Assert.Throws<TranslationFormatException>(() => message.Format([value, value], "en"));
        var snapshot = Snapshot(message, [new("é", TextArgumentType.Number)]);
        Assert.Equal("1.25", snapshot.Format(new("test", 0, "Value"), [value]));
    }
    private static void InvalidModels()
    {
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Value("bogus", "x"));
        foreach (string name in new[] { "*", "0start", "input:namespace", "a/b", "\ud800" })
            Assert.Throws<ArgumentException>(() => Input(name));
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Annotation("a", Input("n")));
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Expression(Text("x"), TextArgumentType.String, annotations: [new("same"), new("same")]));
        Assert.Throws<ArgumentException>(() => Expr(Number("1.2"), TextArgumentType.Int, "integer"));
        Assert.Throws<ArgumentException>(() => Expr(Text("2023-02-29"), TextArgumentType.Date, "date"));
        Assert.Throws<ArgumentException>(() => Expr(Text(" 00112233-4455-6677-8899-aabbccddeeff "), TextArgumentType.Guid, "runic:uuid"));
        Assert.Throws<ArgumentException>(() => Expr(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Text("2"))));
        Assert.Throws<ArgumentException>(() => Expr(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("minimumFractionDigits", Number("5")), new CompiledRmf2Option("style", Text("percent"))));
        Assert.Throws<ArgumentException>(() => Expr(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("select", Input("s"))));
        Assert.Throws<ArgumentException>(() => Expr(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("unknown", Text("x"))));
        Assert.Throws<ArgumentException>(() => Simple([], [], Output(Local("later"), TextArgumentType.String)));
        Assert.Throws<ArgumentException>(() => Simple([], [new("local", "first", Expr(Local("later"), TextArgumentType.String)), new("local", "later", Expr(Text("x"), TextArgumentType.String))], new CompiledRmf2Node("x")));
        Assert.Throws<ArgumentException>(() => Simple([new("n", TextArgumentType.Int)], [], Output(Input("n"), TextArgumentType.Number)));
        Assert.Throws<ArgumentException>(() => Simple([new("digits", TextArgumentType.Int)], [], Output(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Input("digits")))));
        Assert.Throws<ArgumentException>(() => Simple([new("digits", TextArgumentType.Int)], [new("local", "alias", Expr(Input("digits"), TextArgumentType.Int))], Output(Number("1"), TextArgumentType.Number, "number", new CompiledRmf2Option("maximumFractionDigits", Local("alias")))));
        Assert.Throws<ArgumentException>(() => NumericMessage(new CompiledRmf2Variant([new("1", "1")], [new("a")]), new([new("1.0", "1")], [new("b")]), new([new()], [])));
        Assert.Throws<ArgumentException>(() => NumericMessage(new CompiledRmf2Variant([new("1", "1")], [])));
        Assert.Throws<ArgumentException>(() => NumericMessage(new CompiledRmf2Variant([new("1")], []), new([new()], [])));
        Assert.Throws<ArgumentException>(() => Simple([], [], new CompiledRmf2Node("strong", "close")));
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Variant([], new CompiledRmf2Node[4097]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledRmf2Message([new("s", TextArgumentType.String)], [], [new(Input("s"), TextArgumentType.String, "exact")],
            [new([new("é")], []), new([new("e\u0301")], []), new([new()], [])]));
    }
    private static void DecimalDomain()
    {
        string[] invalid = ["1e29", "1e-29", "79228162514264337593543950336", "0.12345678901234567890123456789", "01", "+1", "NaN", "1e2147483648", "0e2147483648", new string('1', 4097)];
        foreach (string value in invalid) Assert.Throws<ArgumentException>(() => Number(value, "0"));
        Assert.Equal("0", Number("-0.000", "0").Canonical);
        Assert.Equal("0", Number("0e-2147483648", "0").Canonical);
        Assert.Equal("0.0123", Number("1.2300e-2", "0.0123").Canonical);
        Assert.Throws<ArgumentException>(() => Number("1.2300", "1.2300"));
    }
    private static void RuntimeErrors()
    {
        Assert.Throws<TranslationFormatException>(() => Simple([], [], new CompiledRmf2Node("12345")).Format([], "en", 4));
        Assert.Throws<TranslationFormatException>(() => NumericMessage(new CompiledRmf2Variant([new()], [])).Format([new("n", 2m)], "zz"));
        Assert.Throws<TranslationFormatException>(() => Simple([], [], Output(Number("1"), TextArgumentType.Number, "runic:relative-time")).Format([], "da"));
    }
    private static CompiledTranslationSnapshot Snapshot(CompiledRmf2Message message, CompiledRmf2Input[] inputs) => new(new("test", "en",
        [CompiledTranslationDefinition.FromRmf2Inputs("Value", inputs)], [new("en", null, [new(0, "compatibility", CompiledTextMessage.FromRmf2(message))])]), "en");
    private static void SnapshotDispatch()
    {
        Assert.Equal("1.25", Snapshot(Simple([], [], Output(Number("1.25"), TextArgumentType.Number)), []).Format(new("test", 0, "Value"), []));
        var french = new CompiledRmf2Message([], [], [], [new([], [Output(Number("1.25"), TextArgumentType.Number)])], "fr");
        Assert.Equal("1,25", Snapshot(french, []).Format(new("test", 0, "Value"), []));
        Assert.Throws<ArgumentException>(() => Snapshot(Simple([new("n", TextArgumentType.Int)], [], Output(Input("n"), TextArgumentType.Int)), [new("n", TextArgumentType.Number)]));
    }
    private static void Compatibility()
    {
        Assert.Equal(2, TranslationsCompatibility.Rmf2RuntimeAbiVersion); Assert.Equal(1, TranslationsCompatibility.RuntimeAbiVersion); Assert.Equal(2, TranslationsCompatibility.MessageGrammarVersion);
        Assert.True(TranslationsCompatibility.SupportsRmf2RuntimeAbi(1), "ABI 1 remains supported"); Assert.True(TranslationsCompatibility.SupportsRmf2RuntimeAbi(2), "ABI 2 is supported");
        foreach (int version in new[] { -1, 0, 3, int.MaxValue }) Assert.Throws<NotSupportedException>(() => TranslationsCompatibility.EnsureRmf2RuntimeAbi(version));
        CompiledRmf2Node[] nodes = [new("original")]; var message = Simple([], [], nodes); nodes[0] = new("changed");
        message.Variants.ToArray()[0] = new([], [new("changed")]);
        Assert.Equal("original", message.Format([], "en"));
        Assert.Equal<CompiledRmf2Message?>(null, new CompiledTextMessage([new(CompiledTextMessageNodeKind.Text, "old")], true).Rmf2V5);
        Assert.Throws<ArgumentNullException>(() => _ = new CompiledTextMessage(null!));
    }
}
