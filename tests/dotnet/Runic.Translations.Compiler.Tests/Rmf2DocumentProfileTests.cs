using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Compiler.Tests;

internal static class Rmf2DocumentProfileTests
{
    internal static void Register(TestRunner runner)
    {
        runner.Add("RMF2 document profile infers the content kind and exports kinds and skeletons", Kinds);
        runner.Add("RMF2 document profile encodes skeletons canonically with defaults and escapes", Skeletons);
        runner.Add("RMF2 document profile normalizes leaf whitespace at compile time", Whitespace);
        runner.Add("RMF2 document profile rejects content-kind violations as RTR0070", KindViolations);
        runner.Add("RMF2 document profile rejects invalid children as RTR0072", InvalidChildren);
        runner.Add("RMF2 document profile enforces depth and node limits as RTR0073", Limits);
        runner.Add("RMF2 document profile locks translated structure with RTR0074 and RTR0071", LockedStructure);
        runner.Add("RMF2 document profile warns about empty blocks and skipped headings", Warnings);
        runner.Add("RMF2 document profile validates block options and default alias collisions", Options);
    }

    private const string Example = "backup =\n    {#p}{#strong}Before continuing{/strong}, save a copy of {$fileName}.{/p}\n    {#ul}\n      {#li}Read the {#link ref=guide}guide{/link}.{/li}\n      {#li}{#action ref=check}Check{/action} the result.{/li}\n    {/ul}\n    {#p}You can continue when the check finishes.{/p}\n";

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
    private static Rmf2ProjectCompilationV5 Compile(string english, string? german = null, string config = "") =>
        TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(config), german is null
            ? [Source("translations/en.rmf2", english)] : [Source("translations/en.rmf2", english), Source("translations/de.rmf2", german)]);
    private static Rmf2ProjectV5 Good(string english, string? german = null, string config = "")
    {
        var result = Compile(english, german, config);
        Assert.True(result.Success, Errors(result));
        return result.Project!;
    }
    private static string Errors(Rmf2ProjectCompilationV5 result) => string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message));
    private static Rmf2ProjectCompilationV5 Bad(string english, string diagnostic, string? german = null, string config = "", string? contains = null)
    {
        var result = Compile(english, german, config);
        Assert.True(!result.Success && result.Diagnostics.Any(d => d.Id == diagnostic && d.Severity == TranslationDiagnosticSeverity.Error && (contains is null || d.Message.Contains(contains, StringComparison.Ordinal))),
            "Expected " + diagnostic + (contains is null ? "" : " containing '" + contains + "'") + ":\n" + english + "\n" + german + "\n" + Errors(result));
        return result;
    }
    private static void Warns(string english, string diagnostic, string? german = null, string? contains = null)
    {
        var result = Compile(english, german);
        Assert.True(result.Success, Errors(result));
        Assert.True(result.Diagnostics.Any(d => d.Id == diagnostic && d.Severity == TranslationDiagnosticSeverity.Warning && (contains is null || d.Message.Contains(contains, StringComparison.Ordinal))),
            "Expected warning " + diagnostic + ":\n" + english + "\n" + Errors(result));
    }
    private static void Silent(string english, string diagnostic, string? german = null)
    {
        var result = Compile(english, german);
        Assert.True(result.Success, Errors(result));
        Assert.True(!(result.Diagnostics.Any(d => d.Id == diagnostic)), "Unexpected " + diagnostic + ":\n" + english + "\n" + Errors(result));
    }
    private static Rmf2MessageContractV5 Contract(Rmf2ProjectV5 project, string key = "x") => project.CanonicalMessages.Single(message => message.Key == key);
    private static IReadOnlyList<Rmf2NodeV5> Nodes(Rmf2ProjectV5 project, int variant = 0, int locale = 0) => project.Locales[locale].DirectResources[0].Message.Variants[variant].Nodes;
    private static string Texts(IReadOnlyList<Rmf2NodeV5> nodes) => string.Join("|", nodes.OfType<Rmf2TextV5>().Select(text => text.Value));
    private static string Skeleton(string message) => string.Join(" ; ", Contract(Good("x = " + message)).Skeletons);

    private static void Kinds()
    {
        var project = Good(Example);
        var contract = Contract(project, "backup");
        Assert.Equal("document", contract.Content);
        Assert.Equal("p,ul(li,li),p", string.Join(";", contract.Skeletons));
        Assert.Equal("Before continuing|, save a copy of |.|Read the |guide|.|Check| the result.|You can continue when the check finishes.", Texts(Nodes(project)));
        using (var exported = JsonDocument.Parse(project.MarkupContract))
        {
            JsonElement message = exported.RootElement.GetProperty("messages").GetProperty("backup");
            Assert.Equal("document", message.GetProperty("content").GetString());
            Assert.Equal("p,ul(li,li),p", message.GetProperty("skeletons")[0].GetString());
            JsonElement contracts = exported.RootElement.GetProperty("contracts");
            Assert.Equal("block", contracts.GetProperty("runic:p").GetProperty("placement").GetString());
            Assert.Equal("inline", contracts.GetProperty("runic:p").GetProperty("children").GetString());
            Assert.Equal("list-items", contracts.GetProperty("runic:ul").GetProperty("children").GetString());
            Assert.Equal("list-item", contracts.GetProperty("runic:li").GetProperty("placement").GetString());
            Assert.Equal("inline", contracts.GetProperty("runic:li").GetProperty("children").GetString());
            Assert.True(!(contracts.TryGetProperty("runic:h", out _)), "Unused block contracts must not be exported.");
        }
        var inline = Good("x = Plain {#strong}text{/strong}");
        Assert.Equal("inline", Contract(inline).Content);
        Assert.Equal(0, Contract(inline).Skeletons.Count);
        using (var exported = JsonDocument.Parse(inline.MarkupContract))
        {
            Assert.Equal("inline", exported.RootElement.GetProperty("messages").GetProperty("x").GetProperty("content").GetString());
            Assert.Equal(0, exported.RootElement.GetProperty("messages").GetProperty("x").GetProperty("skeletons").GetArrayLength());
            Assert.True(!(exported.RootElement.GetProperty("contracts").TryGetProperty("runic:p", out _)), "Inline projects must not export block contracts.");
        }
        // Kind is part of the caller fingerprint.
        Assert.True(!(Good("x = {#p}Same{/p}").CallerFingerprint == Good("x = Same").CallerFingerprint), "Content kind must change the caller fingerprint.");
        // Base-locale structure is not part of the caller fingerprint.
        Assert.Equal(Good("x = {#p}One{/p}").CallerFingerprint, Good("x = {#p}One{/p}{#p}Two{/p}").CallerFingerprint);
        // Empty variants are kind-neutral: documents with zero blocks and the empty skeleton.
        var selected = Good("x =\n  .input {$n :integer}\n  .match $n\n  0 {{}}\n  one {{ \u200E }}\n  * {{{#p}{$n} files{/p}}}");
        Assert.Equal("document", Contract(selected).Content);
        Assert.Equal(" ; p", string.Join(" ; ", Contract(selected).Skeletons));
        Assert.Equal(0, Nodes(selected, 1).Count);
        var allEmpty = Good("x =\n  .input {$n :integer}\n  .match $n\n  0 {{}}\n  * {{ }}");
        Assert.Equal("inline", Contract(allEmpty).Content);
        // A translated empty variant in a document message is normalized to zero blocks.
        var translatedEmpty = Good("x =\n  .input {$n :integer}\n  .match $n\n  0 {{  }}\n  * {{{#p}{$n}{/p}}}", "x =\n  .input {$n :integer}\n  .match $n\n  0 {{ }}\n  * {{{#p}{$n}{/p}}}");
        Assert.Equal(0, translatedEmpty.Locales.Single(locale => locale.Tag == "de").DirectResources[0].Message.Variants[0].Nodes.Count);
    }

    private static void Skeletons()
    {
        Assert.Equal("h[level=1],ol[marker=lower-alpha;start=3](li,li)", Skeleton("{#h level=1}T{/h}{#ol start=3 marker=lower-alpha}{#li}a{/li}{#li}b{/li}{/ol}"));
        Assert.Equal("ol[marker=decimal;start=1](li)", Skeleton("{#ol}{#li}a{/li}{/ol}"));
        Assert.Equal("ol[marker=upper-roman;start=4000](li)", Skeleton("{#ol marker=upper-roman start=|4000|}{#li}a{/li}{/ol}"));
        Assert.Equal("p,p", Skeleton("{#p}a{/p} {#p}b{/p}"));
        Assert.Equal("ul()", Skeleton("{#ul}{/ul}"));
        Assert.Equal("k=a\\,b\\=$;l=\\$x\\(\\)\\[\\]\\;\\\\", Rmf2DocumentProfileV5.EncodeOptions([
            new("l", new("string-literal", "$x()[];\\")), new("k", new("string-literal", "a,b=$"))]));
        Assert.Equal("level=$n", Rmf2DocumentProfileV5.EncodeOptions([new("level", new("input", "n"))]));
    }

    private static void Whitespace()
    {
        string P(string body) => Texts(Nodes(Good("x =\n  {#p}" + body.Replace("\n", "\n  ", StringComparison.Ordinal) + "{/p}")));
        // Leading and trailing runs go, even across tag boundaries; inner runs without a line break stay.
        Assert.Equal("a  b", P("  a  b  "));
        Assert.Equal("bold", Texts(Nodes(Good("x =\n  {#p}{#strong}\n    bold{/strong}{/p}"))));
        // A line-break run becomes one space, placed in the first segment of the run.
        var crossing = Nodes(Good("x =\n  {#p}foo {#strong}\n    bar{/strong}{/p}"));
        Assert.Equal("foo |bar", Texts(crossing));
        // Runs next to br are deleted.
        Assert.Equal("a|b", Texts(Nodes(Good("x =\n  {#p}a\n  {#br/}\n  b{/p}"))));
        // CJK segment breaks are deleted; Hangul keeps a space; U+200B deletes.
        Assert.Equal("日本語", P("日本\n語"));
        Assert.Equal("한 국", P("한\n국"));
        Assert.Equal("a\u200Bb", P("a\u200B\nb"));
        Assert.Equal("ａｂ", P("ａ\nｂ"));
        // U+3000 and NBSP are content and are never trimmed.
        Assert.Equal("\u3000x\u00A0", P("\u3000x\u00A0"));
        // Tabs inside a run without a line break are kept verbatim.
        Assert.Equal("a\tb", P("a\tb"));
        // Placeholders are atoms: the break between two placeholders becomes a space.
        var atoms = Nodes(Good("x =\n  .input {$a :string}\n  .input {$b :string}\n  {{{#p}{$a}\n  {$b}{/p}}}"));
        Assert.Equal(" ", Texts(atoms));
        // Block-level whitespace and bidi marks are dropped.
        var root = Nodes(Good("x =\n  \u200E {#p}a{/p}\n  \u2067 {#ul} {#li}b{/li} {/ul}"));
        Assert.True(root.OfType<Rmf2TextV5>().All(text => text.Value is "a" or "b"), "Block-level whitespace survived: " + Texts(root));
        // Inline messages are untouched.
        Assert.Equal("  a\nb  ", Texts(Nodes(Good("x =\n  {{  a\n  b  }}"))));
        // Thai, Lao, Khmer and Myanmar line breaks become spaces with RTR0078.
        Warns("x =\n  {#p}ภาษา\n  ไทย{/p}", "RTR0078");
        Silent("x =\n  {#p}Line\n  break{/p}", "RTR0078");
        Assert.Equal("ภาษา ไทย", P("ภาษา\nไทย"));
    }

    private static void KindViolations()
    {
        Bad("x = Text {#p}a{/p}", "RTR0070", contains: "document root");
        Bad("x =\n  .input {$n :string}\n  {{{$n}{#p}a{/p}}}", "RTR0070", contains: "placeholder");
        Bad("x = {#strong}a{/strong}{#p}b{/p}", "RTR0070", contains: "strong");
        Bad("x = {#p}a{/p}{#br/}", "RTR0070");
        Bad("x =\n  .input {$n :integer}\n  .match $n\n  one {{{#p}One{/p}}}\n  * {{Many}}", "RTR0070", contains: "mixes");
        Bad("x = {#p}Doc{/p}", "RTR0070", german: "x = Inline", contains: "base locale");
        Bad("x = Inline", "RTR0070", german: "x = {#p}Doc{/p}", contains: "base locale");
    }

    private static void InvalidChildren()
    {
        Bad("x = {#li}orphan{/li}", "RTR0072", contains: "directly inside ul or ol");
        Bad("x = {#p}{#li}a{/li}{/p}", "RTR0072");
        Bad("x = {#p}a {#strong}{#p}b{/p}{/strong}{/p}", "RTR0072");
        Bad("x = {#strong}{#p}a{/p}{/strong}", "RTR0072", contains: "inside inline content");
        Bad("x = {#ul}{#li}a{#ul}{#li}b{/li}{/ul}{/li}{/ul}", "RTR0072", contains: "list item");
        Bad("x = {#ul}text{#li}a{/li}{/ul}", "RTR0072");
        Bad("x = {#ul}{#p}a{/p}{/ul}", "RTR0072", contains: "Only li");
        Bad("x = {#ul}{#strong}a{/strong}{/ul}", "RTR0072");
        Bad("x =\n  .input {$n :string}\n  {{{#ul}{$n}{/ul}}}", "RTR0072");
    }

    private static void Limits()
    {
        string nested = string.Concat(Enumerable.Repeat("{#em}", 15)) + "x" + string.Concat(Enumerable.Repeat("{/em}", 15));
        Good("x = {#p}" + nested + "{/p}");
        Bad("x = {#p}{#strong}" + nested + "{/strong}{/p}", "RTR0073", contains: "16");
        Bad("x = {#ul}{#li}" + nested + "{/li}{/ul}", "RTR0073");
        // The semantic pattern limit (4096 events, closing tags included) binds before the
        // document node limit, which counts no closing tags; both runtimes recheck it.
        Bad("x = " + string.Concat(Enumerable.Repeat("{#p}x{/p}", 1366)), "RTR0065");
    }

    private static void LockedStructure()
    {
        Good(Example, Example.Replace("Before continuing", "Bevor Sie fortfahren", StringComparison.Ordinal));
        var result = Bad(Example, "RTR0074", german: Example.Replace("{/ul}", "  {#li}Extra.{/li}\n    {/ul}", StringComparison.Ordinal), contains: "'ul[1]/li[3]'");
        Assert.True(result.Diagnostics.Any(d => d.Id == "RTR0074" && d.Message.Contains("expected nothing, found li", StringComparison.Ordinal)), Errors(result));
        Bad("x = {#p}a{/p}{#p}b{/p}", "RTR0074", german: "x = {#p}a{/p}", contains: "'p[2]'");
        Bad("x = {#h level=1}a{/h}", "RTR0074", german: "x = {#h level=2}a{/h}", contains: "expected h[level=1], found h[level=2]");
        Bad("x = {#ol}{#li}a{/li}{/ol}", "RTR0074", german: "x = {#ul}{#li}a{/li}{/ul}", contains: "'ol[1]'");
        // Inline markup inside leaves stays free.
        Good("x = {#p}a {#strong}b{/strong}{/p}", "x = {#p}{#em}a{/em} b{/p}");
        // Any source skeleton matches, since .match key tuples may differ by locale.
        const string source = "x =\n  .input {$n :integer}\n  .match $n\n  one {{{#p}One{/p}}}\n  * {{{#p}Many{/p}{#p}files{/p}}}";
        Good(source, "x =\n  .input {$n :integer}\n  .match $n\n  * {{{#p}Viele{/p}{#p}Dateien{/p}}}");
        Bad(source, "RTR0074", german: "x =\n  .input {$n :integer}\n  .match $n\n  * {{{#p}a{/p}{#p}b{/p}{#p}c{/p}}}", contains: "'p[3]'");
        Warns(source, "RTR0071", german: "x =\n  .input {$n :integer}\n  .match $n\n  one {{{#p}Eins{/p}{#p}Datei{/p}}}\n  * {{{#p}Viele{/p}}}");
        Silent(source, "RTR0071", german: "x =\n  .input {$n :integer}\n  .match $n\n  one {{{#p}Eins{/p}}}\n  * {{{#p}Viele{/p}{#p}Dateien{/p}}}");
        // An empty translated variant must match an empty source skeleton.
        Bad("x =\n  .input {$n :integer}\n  .match $n\n  one {{{#p}One{/p}}}\n  * {{{#p}Many{/p}}}", "RTR0074",
            german: "x =\n  .input {$n :integer}\n  .match $n\n  one {{}}\n  * {{{#p}Viele{/p}}}");
        Good("x =\n  .input {$n :integer}\n  .match $n\n  0 {{}}\n  * {{{#p}Many{/p}}}", "x =\n  .input {$n :integer}\n  .match $n\n  0 {{}}\n  * {{{#p}Viele{/p}}}");
    }

    private static void Warnings()
    {
        Warns("x = {#p}a{/p}{#p}{/p}", "RTR0076", contains: "p[2]");
        Warns("x =\n  {#p}a{/p}{#p} {#strong}\n  {/strong} {/p}", "RTR0076");
        Warns("x = {#ul}{#li}a{/li}{#li}{/li}{/ul}", "RTR0076", contains: "ul[1]/li[2]");
        Warns("x = {#ul}{/ul}", "RTR0076", contains: "Empty list");
        Silent("x =\n  .input {$n :integer}\n  .match $n\n  0 {{}}\n  * {{{#p}{$n}{/p}}}", "RTR0076");
        Silent("x = {#p}{#br/}{/p}", "RTR0076");
        Warns("x = {#h level=1}a{/h}{#h level=3}b{/h}", "RTR0077", contains: "from 1 to 3");
        Warns("x = {#h level=2}a{/h}", "RTR0077", contains: "first heading");
        Silent("x = {#h level=1}a{/h}{#h level=2}b{/h}{#h level=2}c{/h}{#h level=1}d{/h}{#h level=2}e{/h}", "RTR0077");
    }

    private static void Options()
    {
        // Unquoted 01, +1 and -0 are already malformed MF2 numbers (RTR0067); quoted forms reach the integer rule.
        foreach (string option in new[] { "level=0", "level=7", "level=|01|", "level=1.0", "level=1e1", "level=|+1|", "level=|-0|", "level=|2147483648|", "level=| 1|" })
            Bad("x = {#h " + option + "}a{/h}", "RTR0061");
        foreach (string option in new[] { "level=01", "level=+1", "level=-0" })
            Assert.True(!Compile("x = {#h " + option + "}a{/h}").Success, "Accepted " + option);
        Good("x = {#h level=|2|}a{/h}");
        Bad("x = {#h}a{/h}", "RTR0061", contains: "level");
        Bad("x =\n  .input {$n :integer}\n  {{{#h level=$n}a{/h}}}", "RTR0061");
        Bad("x = {#ol start=0}{#li}a{/li}{/ol}", "RTR0061");
        Bad("x = {#ol marker=disc}{#li}a{/li}{/ol}", "RTR0061");
        Good("x = {#ol start=2147483647}{#li}a{/li}{/ol}");
        foreach (string alias in new[] { "p", "h", "ul", "ol", "li" })
        {
            var result = Compile("x = Text", config: ",\"markup\":{\"contracts\":[{\"name\":\"app:x\",\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\"}],\"aliases\":{\"" + alias + "\":\"app:x\"}}");
            Assert.True(result.Diagnostics.Any(d => d.Id == "RTR0060"), "Alias '" + alias + "' must collide with the default vocabulary.");
        }
    }
}
