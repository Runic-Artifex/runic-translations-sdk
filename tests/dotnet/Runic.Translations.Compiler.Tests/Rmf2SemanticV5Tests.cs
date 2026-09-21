using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2SemanticV5Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 v5 keeps typed formatted local chains and literal formatting", LocalsAndLiterals);
        runner.Add("RMF2 v5 infers through aliases and preserves underlying types across formatter overrides", LocalValueTypes);
        runner.Add("RMF2 v5 rejects late input bindings before formatter and selector inference", DeclarationOrder);
        runner.Add("RMF2 v5 UUID literals require exactly 36 D-format characters", UuidLiterals);
        runner.Add("RMF2 v5 dynamic options require declared typed caller inputs", DynamicOptions);
        runner.Add("RMF2 v5 finite option table validates enums ranges defaults and runtime errors", Options);
        runner.Add("RMF2 v5 annotations retain order absence empty and numeric values without inputs", Annotations);
        runner.Add("RMF2 v5 distinguishes wildcard and quoted star and preserves NFC source", WildcardAndNfc);
        runner.Add("RMF2 v5 ranks numeric exact above categories and selectors lexicographically", Selection);
        runner.Add("RMF2 v5 rejects malformed and normalized duplicate key vectors", InvalidKeys);
        runner.Add("RMF2 v5 decimal canonicalization is exact bounded and culture independent", Numbers);
        runner.Add("RMF2 v5 keeps the v4 project emission and refusal boundary unchanged", V4Boundary);
        runner.Add("RMF2 v5 normalized AST equals the golden schema instance", Golden);
        runner.Add("RMF2 v5 schemas are versioned closed and mirrored", Schemas);
        runner.Add("RMF2 v2 registry agrees with its frozen execution profile", Registry);
    }

    private static Rmf2SemanticResultV5 Compile(string text) => Rmf2SemanticCompilerV5.Compile(new TranslationSource("message.mf2", Encoding.UTF8.GetBytes(text)));
    private static Rmf2MessageV5 Message(string text)
    {
        var result = Compile(text);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        return result.Message!;
    }
    private static void Reject(string text, string? id = null)
    {
        var result = Compile(text);
        Assert.True(!result.Success && result.Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error && (id is null || id == d.Id)), "Unexpectedly accepted: " + text);
    }
    private static void LocalsAndLiterals()
    {
        var message = Message(".input {$n :number}\n.local $formatted = {$n :number style=percent}\n.local $alias = {$formatted}\n.local $again = {$alias :number style=decimal}\n{{{$again} {1e+2 :number} {|text| :string} {42 :integer}}}");
        Assert.Equal(1, message.Inputs.Count);
        Assert.Equal("decimal", message.Inputs[0].Type);
        Assert.Equal("number", message.Declarations[1].Expression.Function);
        Assert.Equal("local", message.Declarations[2].Expression.Operand.Kind);
        Assert.Equal("formatted", message.Declarations[2].Expression.Operand.Value);
        Assert.Equal("alias", message.Declarations[3].Expression.Operand.Value);
        var expressions = message.Variants[0].Nodes.OfType<Rmf2ExpressionNodeV5>().ToArray();
        Assert.Equal("local", expressions[0].Expression.Operand.Kind);
        Assert.Equal("number-literal", expressions[1].Expression.Operand.Kind);
        Assert.Equal("1e+2", expressions[1].Expression.Operand.Value);
        Assert.Equal("100", expressions[1].Expression.Operand.Canonical);
        Assert.Equal("number", expressions[1].Expression.Function);
        Assert.Equal("string-literal", expressions[2].Expression.Operand.Kind);
        foreach (string source in new[] { "{$n} {$n :number}", "{$n :number} {$n}", "{$n :number} {$n :integer}", "{$n :integer} {$n :number}" })
            Assert.Equal(source.Contains(":integer", StringComparison.Ordinal) ? "int64" : "decimal", Message(source).Inputs[0].Type);
        Reject("{$n :string} {$n :number}", "RTR0065");
        var constant = Message(".local $n = {42 :number style=percent}\n.local $alias = {$n}\n.match $alias\n42 {{exact}}\n* {{other}}");
        Assert.Equal(0, constant.Inputs.Count);
        Assert.Equal("local", constant.Selectors[0].Value.Kind);
        Assert.Equal("decimal", constant.Selectors[0].Type);
        Reject(".local $later = {$next} .local $next = {1} {{x}}", "RTR0067");
        Reject("{42 :string}", "RTR0065");
        Reject("{1.1 :integer}", "RTR0065");
        Reject("{:number}", "RTR0065");
    }
    private static void DeclarationOrder()
    {
        foreach (string source in new[] {
            ".local $a = {$n} .input {$n} {{ {$a :number} }}",
            ".local $a = {$n} .input {$n :number style=percent} {{ {$a} }}",
            ".local $a = {$n} .input {$n :integer select=ordinal} .match $a one {{one}} * {{other}}",
            ".input {$n :number maximumFractionDigits=$digits} .input {$digits :integer} {{ {$n} }}",
            ".local $a = {1 :number select=$selection} .input {$selection :string} {{ {$a} }}" })
        {
            var result = Compile(source);
            Assert.True(!result.Success && result.Message is null, "Late binding reached v5 inference.");
            var diagnostic = Assert.Single(result.Diagnostics);
            Assert.Equal("RTR0067", diagnostic.Id);
            Assert.True(diagnostic.Message.StartsWith("Duplicate declaration '", StringComparison.Ordinal), "Late binding was mistaken for a capability error.");
            Assert.Equal(Encoding.UTF8.GetByteCount(source[..(source.LastIndexOf(".input {", StringComparison.Ordinal) + 8)]), diagnostic.Location.StartByte);
        }
        var ordered = Message(".input {$n :integer select=ordinal} .local $a = {$n} .match $a one {{one}} * {{other}}");
        Assert.Equal("int64", ordered.Selectors[0].Type);
        Assert.Equal("ordinal", ordered.Selectors[0].Function);
        var options = Message(".input {$digits :integer} .input {$n :number maximumFractionDigits=$digits} .local $a = {$n} {{ {$a} }}");
        Assert.Equal("digits:int64,n:decimal", string.Join(',', options.Inputs.Select(input => input.Name + ":" + input.Type)));
        Message(".local $a = {|$n| :string @note=|$n|} .input {$n :number} {{ {$a} {$n} }}");
    }
    private static void DynamicOptions()
    {
        var message = Message(".input {$n :number}\n.input {$digits :integer}\n.input {$style :string}\n.local $d = {$digits}\n{{{$n :number style=$style minimumFractionDigits=$d maximumFractionDigits=$digits}}}");
        Assert.Equal("digits:int64,n:decimal,style:string", string.Join(',', message.Inputs.Select(input => input.Name + ":" + input.Type)));
        var expression = ((Rmf2ExpressionNodeV5)message.Variants[0].Nodes[0]).Expression;
        Assert.Equal("style,minimumFractionDigits,maximumFractionDigits", string.Join(',', expression.Options.Select(option => option.Name)));
        Assert.Equal("local", expression.Options[1].Value.Kind);
        Assert.Equal("input", expression.Options[2].Value.Kind);
        Reject(".input {$n :number} {{{$n :number maximumFractionDigits=$missing}}}", "RTR0065");
        Reject(".input {$n :number} .input {$digits :string} {{{$n :number maximumFractionDigits=$digits}}}", "RTR0065");
        Reject(".input {$n :number} .input {$select :string} {{{$n :number select=$select}}}", "RTR0065");
        Reject(".local $style = {$implicit} {{ {1 :number style=$style} }}", "RTR0065");
        Message(".local $style = {|percent|} {{ {1 :number style=$style} }}");
    }
    private static void LocalValueTypes()
    {
        foreach (string declaration in new[] { "", ".input {$n} ", ".input {$n :number} " })
        {
            var message = Message(declaration + ".local $a = {$n} .local $b = {$a} {{ {$b :number} }}");
            Assert.Equal("decimal", Assert.Single(message.Inputs).Type);
            Assert.True(message.Declarations.All(item => item.Expression.ValueType == "decimal"), "Alias lost its underlying inferred input type.");
            Assert.Equal("decimal", message.Variants[0].Nodes.OfType<Rmf2ExpressionNodeV5>().Single().Expression.ValueType);
        }
        foreach (string declaration in new[] { "", ".input {$n} ", ".input {$n :integer} " })
        {
            var message = Message(declaration + ".local $a = {$n :number style=percent} .local $alias = {$a} {{ {$alias :integer} }}");
            Assert.Equal("int64", Assert.Single(message.Inputs).Type);
            Assert.True(message.Declarations.All(item => item.Expression.ValueType == "int64"), "A number formatter widened the underlying int64 carrier.");
            var formatted = message.Declarations.Single(item => item.Name == "a").Expression;
            Assert.Equal("number", formatted.Function);
            Assert.Equal("percent", Assert.Single(formatted.Options).Value.Value);
            Assert.True(message.Declarations.Single(item => item.Name == "alias").Expression.Function is null, "Alias replaced the inherited formatter.");
            var output = message.Variants[0].Nodes.OfType<Rmf2ExpressionNodeV5>().Single().Expression;
            Assert.Equal("int64", output.ValueType);
            Assert.Equal("integer", output.Function);
        }
        var literal = Message(".local $n = {42 :integer} .local $a = {$n :number} {{ {$a :integer} }}");
        Assert.Equal(0, literal.Inputs.Count);
        Assert.True(literal.Declarations.All(item => item.Expression.ValueType == "int64"), "Formatter retagged a bound literal local.");
        var selection = Message(".input {$n :integer select=ordinal} .local $a = {$n :number select=exact} .local $alias = {$a} .match $alias 1 {{exact}} * {{other}}");
        Assert.Equal("int64", selection.Selectors[0].Type);
        Assert.Equal("exact", selection.Selectors[0].Function);
        var options = Message(".input {$digits :integer} .local $d = {$digits :number} {{ {1 :number maximumFractionDigits=$d} }}");
        Assert.Equal("int64", options.Declarations[1].Expression.ValueType);
        Reject(".input {$n :number} .local $a = {$n} {{ {$a :integer} }}", "RTR0065");
        Reject(".input {$n :string} .local $a = {$n} {{ {$a :number} }}", "RTR0065");
        Reject(".local $a = {$n} {{ {$a :number} {$a :string} }}", "RTR0065");
    }
    private static void UuidLiterals()
    {
        const string uuid = "00112233-4455-6677-8899-aAbBcCdDeEfF";
        var message = Message(".local $id = {|" + uuid + "| :runic:uuid} .local $alias = {$id} {{ {$alias :runic:uuid style=n} }}");
        Assert.Equal("guid", message.Declarations[0].Expression.ValueType);
        Assert.Equal("guid", message.Declarations[1].Expression.ValueType);
        foreach (string invalid in new[] { " " + uuid, uuid + " ", " " + uuid + " ", "\t" + uuid, uuid + "\n", uuid[..^1], "{" + uuid + "}", uuid.Replace("-", "", StringComparison.Ordinal) })
            Reject("{|" + invalid + "| :runic:uuid}", "RTR0065");
    }
    private static void Options()
    {
        foreach (string source in new[] {
            "{1 :number minimumFractionDigits=2 maximumFractionDigits=4}", "{1 :number style=percent maximumFractionDigits=4}",
            "{|2026-09-21| :date style=long}", "{|12:30:45| :time}", "{|2026-09-21T12:30:45Z| :datetime}",
            "{|true| :runic:boolean}", "{42 :runic:relative-time unit=day numeric=auto}", "{1 :integer useGrouping=always}" }) Message(source);
        foreach (string source in new[] {
            "{1 :number bogus=1}", "{1 :number style=currency}", "{1 :integer useGrouping=sometimes}",
            "{1 :number maximumFractionDigits=7}", "{1 :number maximumFractionDigits=-1}", "{1 :number maximumFractionDigits=1.5}",
            "{1 :number maximumFractionDigits=|2|}", "{1 :number minimumFractionDigits=3 maximumFractionDigits=2}",
            "{1 :number style=percent maximumFractionDigits=5}", "{|x| :string select=plural}", "{|nope| :date}",
            ".input {$max :integer} {{ {1 :number style=percent minimumFractionDigits=5 maximumFractionDigits=$max} }}" }) Reject(source, "RTR0065");
        Assert.True(Rmf2FunctionRegistryV2.ValidateResolvedOptions("number", new Dictionary<string, Rmf2ValueV5>()) is null, "Defaults should be valid.");
        var options = new Dictionary<string, Rmf2ValueV5> {
            ["style"] = new("string-literal", "percent"), ["maximumFractionDigits"] = new("number-literal", "5", "5") };
        Assert.True(Rmf2FunctionRegistryV2.ValidateResolvedOptions("number", options) is not null, "Resolved dynamic options must fail instead of clamping.");
    }
    private static void Annotations()
    {
        var message = Message(".local $n = {1 @declaration} {{ {1 :number @flag @empty=|| @numeric=1.0 @looks=|$notInput|} {#strong @open}x{/strong @close=0} }}");
        Assert.Equal(0, message.Inputs.Count);
        var annotations = message.Variants[0].Nodes.OfType<Rmf2ExpressionNodeV5>().Single().Expression.Annotations;
        Assert.Equal("flag,empty,numeric,looks", string.Join(',', annotations.Select(a => a.Name)));
        Assert.True(annotations[0].Value is null, "Valueless annotation changed.");
        Assert.Equal("string-literal", annotations[1].Value!.Kind);
        Assert.Equal("", annotations[1].Value!.Value);
        Assert.Equal("number-literal", annotations[2].Value!.Kind);
        Assert.Equal("1.0", annotations[2].Value!.Value);
        Assert.Equal("1", annotations[2].Value!.Canonical);
        Assert.Equal("$notInput", annotations[3].Value!.Value);
        Assert.Equal("close", message.Variants[0].Nodes.OfType<Rmf2MarkupV5>().Last().Annotations[0].Name);
        Reject("{1 @note=$name}", "RTR0066");
        Reject("{#strong}{#em}x{/strong}{/em}", "RTR0061");
    }
    private static void WildcardAndNfc()
    {
        const string source = ".input {$name :string}\n.match $name\n* {{fallback}}\n|*| {{star}}\n|e\u0301| {{accent}}";
        var message = Message(source);
        Assert.Equal("wildcard", message.Variants[0].Keys[0].Kind);
        Assert.Equal("literal", message.Variants[1].Keys[0].Kind);
        Assert.Equal("e\u0301", message.Variants[2].Keys[0].Value);
        Assert.Equal(source, string.Concat(message.Syntax.Tokens.Select(token => token.Raw)));
        Assert.Equal(1, Choose(message, new("string", "*")));
        Assert.Equal(2, Choose(message, new("string", "é")));
        Assert.Equal(0, Choose(message, new("string", "other")));
        var boolean = Message(".input {$flag :runic:boolean} .match $flag * {{other}} true {{yes}} false {{no}}");
        Assert.Equal(1, Choose(boolean, new("boolean", "true")));
    }
    private static void Selection()
    {
        var numeric = Message(".input {$n :number} .match $n one {{category}} * {{fallback}} 1.0 {{exact}}");
        Assert.Equal(2, Choose(numeric, new("decimal", "1e0", "one")));
        Assert.Equal(0, Choose(numeric, new("decimal", "21", "one")));
        Assert.Equal(1, Choose(numeric, new("decimal", "2", "other")));
        var ordinal = Message(".input {$n :integer select=ordinal} .match $n one {{category}} 21 {{exact}} * {{fallback}}");
        Assert.Equal("ordinal", ordinal.Selectors[0].Function);
        Assert.Equal(1, Choose(ordinal, new("int64", "21", "one")));
        var multi = Message(".input {$n :number} .input {$s :string} .match $n $s * x {{later exact}} one * {{earlier category}} 1 * {{earlier numeric exact}} * * {{fallback}}");
        Assert.Equal(2, Choose(multi, new("decimal", "1", "one"), new("string", "x")));
        Assert.Equal(1, Choose(multi, new("decimal", "21", "one"), new("string", "x")));
        Assert.Equal(0, Choose(multi, new("decimal", "2", "other"), new("string", "x")));
        Assert.Equal(3, Choose(multi, new("decimal", "2", "other"), new("string", "z")));
        static int Unreachable() => throw new InvalidOperationException("Invalid selector number was accepted.");
        try { Choose(numeric, new("decimal", "1e100", "other")); Unreachable(); }
        catch (ArgumentException) { }
    }
    private static int Choose(Rmf2MessageV5 message, Rmf2SelectorValueV5 value) => Rmf2SelectionV5.Select(message.Variants, message.Selectors, new[] { value });
    private static int Choose(Rmf2MessageV5 message, Rmf2SelectorValueV5 first, Rmf2SelectorValueV5 second) => Rmf2SelectionV5.Select(message.Variants, message.Selectors, new[] { first, second });
    private static void InvalidKeys()
    {
        foreach (string source in new[] {
            ".match $x |é| {{a}} |e\u0301| {{b}} * {{other}}", ".match $x |*| {{only literal}}",
            ".input {$n :number} .match $n 1 {{a}} 1.0 {{b}} * {{other}}",
            ".input {$n :number} .match $n |1e0| {{a}} 1 {{b}} * {{other}}",
            ".input {$n :number} .match $n -0 {{a}} 0.0 {{b}} * {{other}}",
            ".input {$n :number} .match $n banana {{a}} * {{other}}",
            ".input {$n :number select=exact} .match $n one {{a}} * {{other}}",
            ".input {$n :integer} .match $n 1.5 {{a}} * {{other}}",
            ".input {$n :runic:boolean} .match $n yes {{a}} * {{other}}",
            ".match $x 1bad {{a}} * {{other}}", ".match $x 01 {{a}} * {{other}}",
            ".match $x $y a {{a}} * * {{other}}", "{01}", "{1bad}" }) Reject(source, "RTR0067");
        foreach (string source in new[] { ".match $x $key {{a}} * {{other}}", ".match $x |bad\\| {{a}}", ".match $x *x {{a}} * {{other}}" }) Reject(source);
        var vector = Message(".match $x $y |a:b| c {{a}} a |b:c| {{b}} * * {{other}}");
        Assert.Equal(3, vector.Variants.Count);
    }
    private static void Numbers()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            foreach (var pair in new[] { ("1e+2", "100"), ("1.2300e-2", "0.0123"), ("-0.000e999", "0"), ("-10e-1", "-1"), ("1e-28", "0.0000000000000000000000000001"), ("79228162514264337593543950335", "79228162514264337593543950335") })
            {
                Assert.True(Rmf2DecimalV5.TryCanonicalize(pair.Item1, out string canonical), "Valid decimal rejected: " + pair.Item1);
                Assert.Equal(pair.Item2, canonical);
            }
            foreach (string invalid in new[] { "1e29", "1e-29", "79228162514264337593543950336", "0.12345678901234567890123456789", "1e999999999999", "NaN", "Infinity", "01", "+1", ".1", "1.", "1,2" })
                Assert.True(!Rmf2DecimalV5.TryCanonicalize(invalid, out _), "Out-of-domain number accepted: " + invalid);
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Reject("{1e100 :number}", "RTR0065");
        Reject("{1 @note=1e100}", "RTR0065");
    }
    private static void V4Boundary()
    {
        var v4 = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x = Hello {$name}"))]);
        Assert.True(v4.Success, "Existing RMF2 compilation failed.");
        using var artifact = JsonDocument.Parse(TranslationOutputRenderer.RenderLocaleJson(v4.Catalogs[0], "en").Text);
        Assert.Equal(4, artifact.RootElement.GetProperty("artifactVersion").GetInt32());
        var unsupported = TranslationCompiler.CompileProject(Rmf2Tests.Project(), [new TranslationSource("translations/en.rmf2", Encoding.UTF8.GetBytes("x = {42 :number}"))]);
        Assert.True(!unsupported.Success && unsupported.Diagnostics.Any(d => d.Id == "RTR0065"), "v4 silently switched to v5 semantics.");
        Message("{42 :number}");
    }
    private static void Golden()
    {
        string source = File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "message.mf2"));
        using var actual = JsonDocument.Parse(Rmf2MessageJsonV5.Serialize(Message(source)));
        using var golden = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "corpus", "semantic-v5", "locale-artifact.json")));
        Assert.True(JsonElement.DeepEquals(golden.RootElement.GetProperty("messages").GetProperty("Example").GetProperty("ast"), actual.RootElement), "Normalized AST changed from the v5 golden instance.");
    }
    private static void Schemas()
    {
        foreach (string file in new[] { "message-ast-v5.schema.json", "locale-artifact-v5.schema.json" })
        {
            string text = File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "schemas", file));
            Assert.Equal(text, File.ReadAllText(RepositoryPaths.Resolve("docs", "public", "schemas", "translations", file)), "Schema mirror drifted.");
            using var schema = JsonDocument.Parse(text);
            Assert.Equal("https://runic-artifex.eu/schemas/translations/" + file, schema.RootElement.GetProperty("$id").GetString());
            Assert.True(!schema.RootElement.GetProperty("additionalProperties").GetBoolean(), "Root is not closed.");
            Assert.Equal(5, schema.RootElement.GetProperty("properties").GetProperty(file.StartsWith("message", StringComparison.Ordinal) ? "astVersion" : "artifactVersion").GetProperty("const").GetInt32());
            References(schema.RootElement);
            void References(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "$ref")
                        {
                            string[] reference = property.Value.GetString()!.Split('#');
                            using var target = JsonDocument.Parse(reference[0].Length == 0 ? text : File.ReadAllText(RepositoryPaths.Resolve("specs", "translations", "schemas", reference[0])));
                            var location = target.RootElement;
                            if (reference.Length > 1) foreach (string part in reference[1].Split('/').Skip(1)) location = location.GetProperty(part);
                            Assert.Equal(JsonValueKind.Object, location.ValueKind);
                        }
                        References(property.Value);
                    }
                else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) References(item);
            }
        }
    }
    private static void Registry()
    {
        using var profile = JsonDocument.Parse(File.ReadAllBytes(RepositoryPaths.Resolve("specs", "translations", "rmf2-execution-v2.json")));
        var functions = profile.RootElement.GetProperty("functions");
        Assert.Equal(Rmf2FunctionRegistryV2.Functions.Count, functions.EnumerateObject().Count());
        foreach (var rule in Rmf2FunctionRegistryV2.Functions)
        {
            var function = functions.GetProperty(rule.Name);
            Assert.Equal(rule.InputType, function.GetProperty("inputType").GetString());
            Assert.Equal(rule.Selection, function.GetProperty("selection").GetString());
            var options = function.GetProperty("options");
            Assert.Equal(rule.Options.Count, options.EnumerateObject().Count());
            foreach (var option in rule.Options)
            {
                var json = options.GetProperty(option.Name);
                Assert.Equal(option.InputType, json.GetProperty("inputType").GetString());
                Assert.Equal(option.Literal, json.GetProperty("literal").GetBoolean());
                Assert.Equal(option.Dynamic, json.GetProperty("dynamic").GetBoolean());
                Assert.Equal(option.Default, json.GetProperty("default").GetString());
                Assert.Equal(string.Join(',', option.Values), string.Join(',', json.GetProperty("values").EnumerateArray().Select(item => item.GetString())));
                Assert.Equal(option.Minimum, json.TryGetProperty("minimum", out var min) ? min.GetInt32() : null);
                Assert.Equal(option.Maximum, json.TryGetProperty("maximum", out var max) ? max.GetInt32() : null);
            }
        }
    }
}
