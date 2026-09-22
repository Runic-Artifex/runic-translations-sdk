using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2ProjectV5Tests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 selects the semantic contract when profile is omitted", Dispatch);
        runner.Add("RMF2 v5 canonical multilingual fixture links and executes typed locals", Fixture);
        runner.Add("RMF2 v5 target callers inherit canonical carriers and may omit inputs", Callers);
        runner.Add("RMF2 v5 project composition matches flat split and external mounts", Composition);
        runner.Add("RMF2 v5 project discovery rejects collisions undeclared locales and invalid fallback", InvalidProjects);
        runner.Add("RMF2 v5 markup canonicalizes aliases typed defaults locals and annotations", Markup);
        runner.Add("RMF2 v5 markup validates every variant and functional slot obligation", InvalidMarkup);
        runner.Add("RMF2 v5 caller fingerprint excludes content but source hash detects it", Fingerprints);
        runner.Add("RMF2 v5 caller fingerprint captures inputs slots and renderer contracts", FingerprintChanges);
        runner.Add("RMF2 v5 fallback changes freshness while preserving caller compatibility", Fallback);
        runner.Add("RMF2 v5 allowed extra keys retain separate dynamic contracts", ExtraKeys);
        runner.Add("RMF2 v5 generated names are NFC injective portable and order independent", Names);
        runner.Add("RMF2 v5 metadata spans limits and cancellation retain project validation", Validation);
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    internal static TranslationSource Project(string extra = "") => Source("translations/runic.json",
        "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"" + extra + "}");
    private static Rmf2ProjectCompilationV5 Compile(string english, string? german = null, string config = "")
        => TranslationCompiler.CompileRmf2ProjectV5(Project(config), german is null
            ? [Source("translations/en.rmf2", english)] : [Source("translations/en.rmf2", english), Source("translations/de.rmf2", german)]);
    private static Rmf2ProjectV5 Good(string english, string? german = null, string config = "")
    {
        var result = Compile(english, german, config);
        Assert.True(result.Success, Errors(result)); return result.Project!;
    }
    private static string Errors(Rmf2ProjectCompilationV5 result) => string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message));
    private static void Bad(string english, string? german = null, string config = "", string? diagnostic = null)
    {
        var result = Compile(english, german, config);
        Assert.True(!result.Success && (diagnostic is null || result.Diagnostics.Any(d => d.Id == diagnostic)), "Unexpected acceptance/diagnostic: " + english + "\n" + german + "\n" + Errors(result));
        Assert.True(result.Project is null, "An invalid v5 project exposed a usable carrier.");
    }
    private static void Dispatch()
    {
        var project = Project();
        TranslationSource[] sources = [Source("translations/en.rmf2", "hello = Hello {$name}")];
        var selected = TranslationCompiler.CompileRmf2ProjectV5(project, sources);
        Assert.True(selected.Success && selected.Project is not null,
            "Omitted configuration did not select the semantic carrier.");
        TranslationSource[] direct = [Source("translations/en/hello.mf2", "Hello {$name}")];
        var directResult = TranslationCompiler.CompileRmf2ProjectV5(project, direct);
        Assert.True(directResult.Success && directResult.Project!.CanonicalMessages.Single().Key == "hello",
            "Direct MF2 did not normalize into the selected semantic model.");
        var mixed = TranslationCompiler.CompileRmf2ProjectV5(project,
            [Source("translations/en.rmf2", "hello = Hello"), Source("translations/en/other.mf2", "Other")]);
        Assert.True(!mixed.Success && mixed.Diagnostics.Any(diagnostic => diagnostic.Id == "RTR0052"),
            "Mixed direct and grouped source representations were accepted.");
        var retiredSelector = TranslationCompiler.CompileRmf2ProjectV5(
            Source("translations/runic.json", "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\",\"executionProfile\":\"rmf2-execution-v1\"}"), sources);
        Assert.True(!retiredSelector.Success,
            "A retired selector was allowed to revive an older contract.");
        Assert.True(selected.Project!.CanonicalMessages.Count == 1, "Selected semantic project did not link.");
    }
    private static void Fixture()
    {
        string root = RepositoryPaths.Resolve("specs", "translations", "corpus", "v5-project");
        var project = new TranslationSource("translations/runic.json", File.ReadAllBytes(Path.Combine(root, "runic.json")));
        TranslationSource[] sources = [new("translations/en.rmf2", File.ReadAllBytes(Path.Combine(root, "en.rmf2"))), new("translations/de.rmf2", File.ReadAllBytes(Path.Combine(root, "de.rmf2")))];
        var result = TranslationCompiler.CompileRmf2ProjectV5(project, sources);
        Assert.True(result.Success, Errors(result));
        var linked = result.Project!;
        using var expected = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "expected-contract.json")));
        Assert.Equal(expected.RootElement.GetProperty("keys").GetRawText(), JsonSerializer.Serialize(linked.CanonicalMessages.Select(message => message.Key)));
        var bill = linked.CanonicalMessages.Single(message => message.Key == "account_bill");
        Assert.Equal("count:int64,rate:decimal,用户:string", string.Join(',', bill.Inputs.Select(input => input.Name + ":" + input.Type)));
        Assert.Equal("runic:link", bill.Slots["invoice"].Kind);
        Assert.True(bill.Structured, "Structured output obligation missing.");
        var german = linked.Locales.Single(locale => locale.Tag == "de").ResolvedResources.Single(resource => resource.Key == "account_total");
        var runtime = Rmf2RuntimeV5Tests.Lower(german.Message);
        Assert.Equal("12,50%", runtime.Format([new("rate", .125m)], "de"));
        var billRuntime = Rmf2RuntimeV5Tests.Lower(linked.Locales.Single(locale => locale.Tag == "de").ResolvedResources.Single(resource => resource.Key == "account_bill").Message);
        Assert.True(billRuntime.FormatContent([new("count", 2L), new("rate", .125m)], "de").Nodes.Length > 0, "Linked rich message could not execute.");
        var reversed = TranslationCompiler.CompileRmf2ProjectV5(project, sources.Reverse()).Project!;
        Assert.Equal(linked.CallerFingerprint, reversed.CallerFingerprint);
        Assert.Equal(linked.SourceHash, reversed.SourceHash);
        Assert.Equal(linked.MarkupContract, reversed.MarkupContract);
    }
    private static void Callers()
    {
        const string english = "x =\n  .input {$count :integer}\n  .input {$unused :string}\n  {{Count {$count} {$unused}}}";
        foreach (string translated in new[] { "x = Anzahl {$count}", "x =\n  .local $n = {$count}\n  {{{$n}}}", "x =\n  .local $n = {$count :number minimumFractionDigits=2}\n  {{{$n}}}", "x =\n  .input {$count}\n  .match $count\n  one {{Ein}}\n  * {{{$count}}}", "x = Fertig" })
        {
            var project = Good(english, translated);
            var message = project.Locales.Single(locale => locale.Tag == "de").DirectResources[0].Message;
            if (message.Inputs.Count != 0) Assert.Equal("int64", message.Inputs[0].Type);
            Rmf2RuntimeV5Tests.Lower(message);
            Assert.Equal(2, project.CanonicalMessages[0].Inputs.Count);
        }
        Bad(english, "x = {$extra}", diagnostic: "RTR0016");
        Bad(english, "x = {$count :string}", diagnostic: "RTR0065");
        Bad(english, "x =\n  .input {$count :number}\n  {{{$count}}}", diagnostic: "RTR0065");
        var normalized = Good("x = {$cafe\u0301}", "x = {$café}");
        Assert.Equal("café", normalized.CanonicalMessages[0].Inputs[0].Name);
    }
    private static void Composition()
    {
        var flat = Good("checkout {\n  cart {\n    title = {$name}\n  }\n}");
        var split = TranslationCompiler.CompileRmf2ProjectV5(Project(), [Source("translations/checkout/cart/en.rmf2", "title = {$name}")]);
        var mounted = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"sourceRoots\":[{\"path\":\"../src/shop/i18n\",\"namespace\":[\"checkout\"]}]"), [Source("src/shop/i18n/cart/en.rmf2", "title = {$name}")]);
        var directMounted = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"sourceRoots\":[{\"path\":\"../src/shop/i18n\",\"namespace\":[\"checkout\",\"cart\"]}]"), [Source("src/shop/i18n/en/title.mf2", "{$name}")]);
        Assert.True(split.Success, Errors(split)); Assert.True(mounted.Success, Errors(mounted)); Assert.True(directMounted.Success, Errors(directMounted));
        Assert.Equal(flat.CallerFingerprint, split.Project!.CallerFingerprint);
        Assert.Equal(flat.CallerFingerprint, mounted.Project!.CallerFingerprint);
        Assert.Equal(flat.CallerFingerprint, directMounted.Project!.CallerFingerprint);
        Assert.Equal("checkout.cart.title", string.Join('.', mounted.Project.CanonicalMessages[0].Path));
        Assert.Equal("checkout.cart.title", string.Join('.', directMounted.Project.CanonicalMessages[0].Path));

        var flatKey = Good("a_b = {$name}");
        var nestedKey = Good("a {\n  b = {$name}\n}");
        Assert.Equal(flatKey.CanonicalMessages[0].Key, nestedKey.CanonicalMessages[0].Key);
        Assert.True(Rmf2GeneratedNamesV1.Path(flatKey.CanonicalMessages[0].Path) != Rmf2GeneratedNamesV1.Path(nestedKey.CanonicalMessages[0].Path),
            "Distinct logical paths unexpectedly shared a generated API name.");
        Assert.True(flatKey.CallerFingerprint != nestedKey.CallerFingerprint,
            "The caller fingerprint omitted the logical path used by generated APIs.");
    }
    private static void InvalidProjects()
    {
        foreach (string text in new[] { "a = A\na = B", "a = A\na {\n  b = B\n}", "a_b {\n  c = C\n}\na {\n  b_c = C\n}" }) Bad(text);
        Bad("a {\n  b = A\n}", "a = A", diagnostic: "RTR0054");
        Bad("x = X", "x = X", ",\"locales\":[\"en\"]", "RTR0004");
        Bad("x = X", "x = X", ",\"locales\":[\"en\",{\"tag\":\"de\",\"fallback\":\"de\"}]");
        Good("a {\n  b = B\n}\na {\n  c = C\n}");
        var duplicate = TranslationCompiler.CompileRmf2ProjectV5(Project(), [Source("translations/en.rmf2", "x = X"), Source("translations/en.rmf2", "y = Y")]);
        Assert.True(!duplicate.Success, "Duplicate paths accepted.");
        var caseAlias = TranslationCompiler.CompileRmf2ProjectV5(Project(), [Source("translations/one/en.rmf2", "x = X"), Source("translations/One/de.rmf2", "x = X")]);
        Assert.True(!caseAlias.Success, "Case-only alias accepted.");
    }
    private const string Custom = ",\"markup\":{\"contracts\":[{\"name\":\"app:badge\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{\"amount\":{\"type\":\"number\",\"default\":\"1e2\"},\"enabled\":{\"type\":\"boolean\",\"default\":\"true\"},\"tone\":{\"type\":\"enum\",\"values\":[\"positive\",\"neutral\"],\"default\":\"neutral\"}}}],\"aliases\":{\"badge\":\"app:badge\"}}";
    private static void Markup()
    {
        var project = Good("x = {#badge @open=||}Yes{/badge @close}", config: Custom);
        var tags = project.Locales[0].DirectResources[0].Message.Variants[0].Nodes.OfType<Rmf2MarkupV5>().ToArray();
        Assert.Equal("app:badge", tags[0].Name); Assert.Equal("app:badge", tags[1].Name);
        Assert.Equal("number-literal", tags[0].Options[0].Value.Kind); Assert.Equal("100", tags[0].Options[0].Value.Canonical);
        Assert.Equal("close", tags[1].Annotations[0].Name);
        Assert.Equal("100", project.MarkupContracts["app:badge"].Options["amount"].Default);
        using var exported = JsonDocument.Parse(project.MarkupContract);
        Assert.Equal(1, exported.RootElement.GetProperty("version").GetInt32());
        var locals = Good("x =\n  .input {$n :integer}\n  .local $amount = {$n :number}\n  {{{#badge amount=$amount}Yes{/badge}}}", config: Custom);
        Assert.Equal("local", locals.Locales[0].DirectResources[0].Message.Variants[0].Nodes.OfType<Rmf2MarkupV5>().First().Options[0].Value.Kind);
        Rmf2RuntimeV5Tests.Lower(locals.Locales[0].DirectResources[0].Message).FormatContent([new("n", 42L)], "en");
    }
    private static void InvalidMarkup()
    {
        foreach (string text in new[] { "x = {#badge amount=|100|}X{/badge}", "x = {#badge tone=1}X{/badge}", "x = {#badge enabled=maybe}X{/badge}", "x = {#badge amount=$unknown}X{/badge}", "x = {#badge extra=1}X{/badge}", "x = {#badge/}", "x = {#unknown/}", "x = {#link}{#action}Go{/action}{/link}", "x = {#icon/}", "x = {#link ref=$slot}Go{/link}", "x = {#link}Go{/link}{#link}Go{/link}" }) Bad(text, config: Custom);
        Bad("x = {#link ref=terms}Terms{/link}", "x = Text", diagnostic: "RTR0062");
        Bad("x = {#link ref=terms}Terms{/link}", "x = {#action ref=terms}Go{/action}", diagnostic: "RTR0062");
        Bad("x = {#link ref=terms}Terms{/link}", "x = {#link ref=other}Go{/link}", diagnostic: "RTR0062");
        const string conditional = "x =\n  .input {$n :integer}\n  .match $n\n  one {{{#link}Go{/link}}}\n  * {{Nothing}}";
        Bad(conditional, diagnostic: "RTR0062");
        const string slots = ",\"markup\":{\"slots\":{\"x\":{\"link\":{\"min\":0,\"max\":1}}}}";
        Good(conditional, "x = Fertig", slots);
        Bad(conditional, "x = {#link}Go{/link}{#link}Again{/link}", slots, "RTR0062");
        Bad("x =\n  .input {$n :integer}\n  .match $n\n  one {{{#link}Go{/link}}}\n  * {{{#link}{#action}Go{/action}{/link}}}");
        Bad("x = Plain", config: ",\"markup\":{\"slots\":{\"x\":{\"unknown\":{\"min\":0,\"max\":1}}}}", diagnostic: "RTR0062");
        Bad("x = {#badge}X{/badge}", config: Custom.Replace("\"default\":\"1e2\"", "\"default\":\"NaN\"", StringComparison.Ordinal), diagnostic: "RTR0060");
    }
    private static void Fingerprints()
    {
        string[] changes =
        [
            "x =\n  .input {$n :integer}\n  {{First {$n}}}",
            "x =\n  .input {$n :integer}\n  {{Other {$n @note=|changed|}}}",
            "x =\n  .input {$n :integer}\n  .local $alias = {$n :number minimumFractionDigits=2}\n  {{{$alias}}}",
            "x =\n  .input {$n :integer select=ordinal}\n  .match $n\n  one {{One}}\n  * {{{$n}}}",
        ];
        var projects = changes.Select(text => Good(text)).ToArray();
        Assert.Equal(1, projects.Select(project => project.CallerFingerprint).Distinct().Count());
        Assert.Equal(changes.Length, projects.Select(project => project.SourceHash).Distinct().Count());
        var renamed = Good("x = {$café}"); var decomposed = Good("x = {$cafe\u0301}");
        Assert.Equal(renamed.CallerFingerprint, decomposed.CallerFingerprint);
        Assert.True(renamed.SourceHash != decomposed.SourceHash, "Source spelling changes did not invalidate freshness.");
        var alias = Good("x = {#badge}X{/badge}", config: Custom);
        var canonical = Good("x = {#app:badge}X{/app:badge}", config: Custom.Replace("1e2", "100", StringComparison.Ordinal));
        Assert.Equal(alias.CallerFingerprint, canonical.CallerFingerprint);
    }
    private static void FingerprintChanges()
    {
        string[] messages = ["x = {$name}", "x = {$other}", "x = {$name :integer}", "y = {$name}", "x = {#strong}{$name}{/strong}", "x = {#link ref=one}{$name}{/link}", "x = {#link ref=two}{$name}{/link}"];
        Assert.Equal(messages.Length, messages.Select(message => Good(message).CallerFingerprint).Distinct().Count());
        var one = Good("x = {#link}Go{/link}");
        var two = Good("x = {#link}Go{/link}", config: ",\"markup\":{\"slots\":{\"x\":{\"link\":{\"min\":0,\"max\":2}}}}");
        Assert.True(one.CallerFingerprint != two.CallerFingerprint, "Slot cardinality missing from fingerprint.");
        Assert.True(Good("x = {#badge}X{/badge}", config: Custom).CallerFingerprint != Good("x = {#badge}X{/badge}", config: Custom.Replace("\"plainText\":\"children\"", "\"plainText\":\"explicit\"", StringComparison.Ordinal)).CallerFingerprint, "Renderer obligation missing from fingerprint.");
    }
    private static void Fallback()
    {
        const string settings = ",\"validation\":{\"translationCompleteness\":\"allow\"},\"locales\":[\"en\",\"de\",{\"tag\":\"fr\",\"fallback\":\"de\"}]";
        var german = Good("x = English {$name}", "x = Deutsch {$name}", settings);
        var english = Good("x = English {$name}", "x = Deutsch {$name}", settings.Replace("\"fallback\":\"de\"", "\"fallback\":\"en\"", StringComparison.Ordinal));
        Assert.Equal(german.CallerFingerprint, english.CallerFingerprint);
        Assert.True(german.SourceHash != english.SourceHash, "Fallback settings did not change freshness.");
        Assert.Equal("de", german.Locales.Single(locale => locale.Tag == "fr").ResolvedResources[0].ContentLocale);
        Assert.Equal("en", english.Locales.Single(locale => locale.Tag == "fr").ResolvedResources[0].ContentLocale);
        Assert.True(german.MarkupContract != english.MarkupContract, "Export erased content locale resolution.");
        var noInputs = Good("x = {$name}", "x = Hallo");
        Assert.Equal(0, noInputs.Locales.Single(locale => locale.Tag == "de").DirectResources[0].Message.Inputs.Count);
        Assert.Equal(1, noInputs.CanonicalMessages[0].Inputs.Count);
    }
    private static void Names()
    {
        string[] names = ["class", "default", "await", "arguments", "a-b", "a+b", "a.b", "a_b", "1lead", "A", "a", "r_61", "用户", "café", "🌍", "namespace"];
        string[] generated = names.Select(Rmf2GeneratedNamesV1.Identifier).ToArray();
        Assert.Equal(names.Length, generated.Distinct(StringComparer.Ordinal).Count());
        Assert.True(generated.All(name => name.StartsWith("r_", StringComparison.Ordinal) && name.Skip(2).All(c => char.IsAsciiHexDigit(c))), "Generated names are not portable ASCII identifiers.");
        Assert.Equal(Rmf2GeneratedNamesV1.Identifier("café"), Rmf2GeneratedNamesV1.Identifier("cafe\u0301"));
        Assert.True(Rmf2GeneratedNamesV1.Path(["a_b", "c"]) != Rmf2GeneratedNamesV1.Path(["a", "b_c"]), "Segment flattening collided.");
        Assert.True(Rmf2GeneratedNamesV1.Path(["a", "b"]) != Rmf2GeneratedNamesV1.Identifier("a_b"), "Path and leaf collided.");
        Assert.Equal(string.Join(',', generated.Reverse()), string.Join(',', names.Reverse().Select(Rmf2GeneratedNamesV1.Identifier)));
        var broad = Good("x = {$用户} {$café} {$a-b} {$class}");
        Assert.Equal(4, broad.CanonicalMessages[0].Inputs.Count);
    }
    private static void ExtraKeys()
    {
        const string settings = ",\"validation\":{\"extraLocaleKeys\":\"allow\"}";
        var plain = Good("x = X", "x = X", settings);
        var extra = Good("x = X", "x = X\nextra = {#link}{$n :integer}{/link}", settings);
        Assert.Equal(plain.CallerFingerprint, extra.CallerFingerprint);
        Assert.True(plain.SourceHash != extra.SourceHash, "Extra content was omitted from freshness.");
        Assert.Equal(-1, extra.ExtraMessages[0].Id);
        Assert.Equal("int64", extra.ExtraMessages[0].Inputs[0].Type);
        using var markup = JsonDocument.Parse(extra.MarkupContract);
        Assert.True(markup.RootElement.GetProperty("messages").TryGetProperty("extra", out _), "Extra renderer contract was erased.");
        var compiled = TranslationCompiler.CompileRmf2ProjectV5(Project(settings),
            [Source("translations/en.rmf2", "x = X"), Source("translations/de.rmf2", "x = X\nextra = {$n :integer}"), Source("translations/fr.rmf2", "x = X\nextra = {$n}")]);
        Assert.True(compiled.Success, Errors(compiled));
        Assert.Equal("int64", compiled.Project!.Locales.Single(locale => locale.Tag == "fr").DirectResources.Single(resource => resource.Key == "extra").Message.Inputs[0].Type);
        var invalid = TranslationCompiler.CompileRmf2ProjectV5(Project(settings),
            [Source("translations/en.rmf2", "x = X"), Source("translations/de.rmf2", "x = X\nextra = {$n :integer}"), Source("translations/fr.rmf2", "x = X\nextra = {$other}")]);
        Assert.True(!invalid.Success && invalid.Diagnostics.Any(d => d.Id == "RTR0016"), "Allowed extra keys lost caller validation.");
    }
    private static void Validation()
    {
        Good("@param $cafe\u0301 - User\n@example {\"café\":\"Ada\"}\nx = {$café}");
        Good("@example {\"n\":9223372036854775807}\nx = {$n :integer}");
        Bad("@example {\"n\":9223372036854775808}\nx = {$n :integer}", diagnostic: "RTR0051");
        Bad("@example {\"café\":\"Ada\",\"cafe\u0301\":\"A\"}\nx = {$café}", diagnostic: "RTR0051");
        var invalid = Compile("# heading\nx = Good\ny =\n  .local $a = {$n}\n  .input {$n :number}\n  {{{$a}}}");
        Assert.True(invalid.Diagnostics.Any(d => d.Id == "RTR0067" && d.Location.Path == "translations/en.rmf2" && d.Location.Line == 5), "Semantic diagnostics lost physical source mapping.");
        var limit = TranslationCompiler.CompileRmf2ProjectV5(Project(), [Source("translations/en.rmf2", "a = A\nb = B")], new(maximumKeysPerCatalog: 1));
        Assert.True(!limit.Success && limit.Diagnostics.Any(d => d.Id == "RTR0022" || d.Id == "RTR0050"), "Project bypassed resource limits.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { TranslationCompiler.CompileRmf2ProjectV5(Project(), [], cancellationToken: canceled.Token); throw new InvalidOperationException("Canceled compilation proceeded."); }
        catch (OperationCanceledException) { }
    }
}
