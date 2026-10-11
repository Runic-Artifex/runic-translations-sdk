using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

[Category("rmf2-semantic-v5")]
internal sealed class Rmf2AuthoringTests
{
    private static TranslationSource Source(string text) => new("sample.mf2", Encoding.UTF8.GetBytes(text));
    private static TranslationSource Apply(TranslationSource source, Rmf2AuthoringOperation operation) => Rmf2AuthoringService.Apply(source, Rmf2AuthoringService.Revision(source), operation);
    private static string Text(TranslationSource source) => Encoding.UTF8.GetString(source.Bytes);
    [Test, DisplayName("RMF2 authoring projects quoted branches and pinned CLDR categories")]
    public void Projection()
    {
        var source = Source(".input {$count :number minimumFractionDigits=0}\n.match $count\none {{One 🦊}}\n* {{Many {$count}}}\n");
        var projection = Rmf2AuthoringService.Project(source, "en-US");
        Assert.True(projection.Supported, projection.Reason ?? "unsupported");
        Assert.Equal("One 🦊", projection.Variants[0].Pattern);
        Assert.Equal("Many {$count}", projection.Variants[1].Pattern);
        Assert.Equal("decimal", projection.Inputs[0].Type);
        Assert.Equal("one,other", string.Join(',', projection.PluralCategories));
        Assert.Equal("one,few,many,other", string.Join(',', Rmf2AuthoringService.Project(source, "cs").PluralCategories));
        Assert.Equal("48.2", projection.CldrVersion);
        var mixed = Rmf2AuthoringService.Project(Source(".input {$cardinal :integer}\n.input {$ordinal :integer select=ordinal}\n.match $cardinal $ordinal\n* * {{Items}}"), "en");
        Assert.Equal("one,other", string.Join(',', mixed.PluralCategories));
        Assert.Equal("one,other", string.Join(',', mixed.SelectorPluralCategories[0]));
        Assert.Equal("one,two,few,other", string.Join(',', mixed.SelectorPluralCategories[1]));
        Assert.True(!Rmf2AuthoringService.Project(Source("{:future}"), "en").Supported, "Unknown functions must have an explicit source-only fallback.");
    }
    [Test, DisplayName("RMF2 authoring patches patterns and references with revision guards")]
    public void Patches()
    {
        var source = Source(".input {$count :number}\n.local $value = {$count :number}\n.match $count\none {{One 🦊}}\n* {{|count| {$value} {$count @note=|unchanged|}}}\n");
        var changed = Apply(source, new("set-pattern", "0", "Exactly one 🦊"));
        Assert.Equal(Text(source).Replace("One 🦊", "Exactly one 🦊", StringComparison.Ordinal), Text(changed));
        var renamed = Apply(changed, new("rename-input", Name: "count", NewName: "amount"));
        Assert.True(Text(renamed).Contains("|count|", StringComparison.Ordinal), "Literal spelling must stay untouched.");
        Assert.True(Text(renamed).Contains("@note=|unchanged|", StringComparison.Ordinal), "Annotations must stay untouched.");
        Assert.True(Text(renamed).Contains(".match $amount", StringComparison.Ordinal), "Matcher references must rename.");
        try { Rmf2AuthoringService.Apply(changed, Rmf2AuthoringService.Revision(source), new("set-pattern", "0", "Stale")); throw new InvalidOperationException("Stale edit accepted."); }
        catch (ArgumentException) { }
    }
    [Test, DisplayName("RMF2 authoring persists declarations selectors and branches without legacy JSON")]
    public void Structure()
    {
        var source = Source("Hello 🦊\n");
        source = Apply(source, new("add-input", Name: "count", Function: "integer"));
        Assert.Equal(".input {$count :integer}\n{{Hello 🦊}}\n", Text(source));
        source = Apply(source, new("set-selectors", Selectors: ["count"]));
        source = Apply(source, new("add-variant", Pattern: "One {$count}", Keys: ["one"]));
        var projection = Rmf2AuthoringService.Project(source, "en");
        Assert.Equal(2, projection.Variants.Count);
        source = Apply(source, new("add-input", Name: "gender", Function: "string"));
        source = Apply(source, new("set-selectors", Selectors: ["count", "gender"]));
        Assert.Equal("*,*|one,*", string.Join('|', Rmf2AuthoringService.Project(source, "en").Variants.Select(v => string.Join(',', v.Keys))));
        source = Apply(source, new("set-selectors", Selectors: ["count"]));
        Assert.Equal("*|one", string.Join('|', Rmf2AuthoringService.Project(source, "en").Variants.Select(v => string.Join(',', v.Keys))));
        try { Apply(source, new("set-selectors", Selectors: ["gender", "unused"])); throw new InvalidOperationException("Unknown selector accepted."); } catch (ArgumentException) { }
        source = Apply(source, new("set-pattern", "0", "Several {$count}"));
        Assert.True(Rmf2SemanticCompilerV5.Compile(source).Success, "Structured edit should be executable MF2.");
        source = Apply(source, new("remove-variant", "1"));
        Assert.Equal(1, Rmf2AuthoringService.Project(source, "en").Variants.Count);
        try { Apply(source, new("remove-variant", "0")); throw new InvalidOperationException("Fallback removed."); } catch (ArgumentException) { }
        try { Apply(source, new("set-pattern", "0", "Broken }} pattern")); throw new InvalidOperationException("Broken pattern accepted."); } catch (ArgumentException) { }
    }
    [Test, DisplayName("RMF2 semantic review excludes syntax and storage framing but preserves boundaries")]
    public void Semantic()
    {
        var plain = Rmf2AuthoringService.Semantic(Source("Hello {$name}\n"));
        Assert.Equal("Hello ", plain.Text.Single());
        Assert.Equal("name:string", plain.Placeholders.Single());
        Assert.True(!plain.HasBoundaryWhitespace, "An expression at the boundary isn't literal whitespace.");
        Assert.True(!Rmf2AuthoringService.Semantic(Source("Hello\r\n")).HasBoundaryWhitespace, "Storage newline isn't translator whitespace.");
        Assert.True(Rmf2AuthoringService.Semantic(Source("{{Hello\n}}\n")).HasBoundaryWhitespace, "Quoted newline is intentional.");
        Assert.True(Rmf2AuthoringService.Semantic(Source("Hello \n")).HasBoundaryWhitespace, "Plain trailing spaces are intentional.");
        var plural = Rmf2AuthoringService.Semantic(Source(".input {$count :number}\n.match $count\none {{One item}}\n* {{Many items}}\n"));
        Assert.Equal("One item|Many items", string.Join('|', plural.Text));
        Assert.Equal("count:decimal", plural.Placeholders.Single());
        Assert.True(!plural.Text.Any(t => t.Contains(".input", StringComparison.Ordinal)), "Declarations cannot enter glossary text.");
        var link = Rmf2AuthoringService.Semantic(Source("{#link ref=help}Help{/link}"));
        Assert.True(link.Slots.Contains("open:link:help", StringComparer.Ordinal), "Slot identity is part of suggestion compatibility.");
    }
}
