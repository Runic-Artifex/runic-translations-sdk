using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// runic.json pseudoLocales: en-XA and ar-XB generated from the base locale when a development build asks for them.
internal sealed class Rmf2PseudoLocalesTests
{
    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));

    private static TranslationSource Project(string extra) => Source("translations/runic.json",
        "{\"schemaVersion\":1,\"catalog\":\"app\",\"code\":{\"namespace\":\"Example\",\"className\":\"AppText\"},\"baseLocale\":\"en\"" + extra + "}");

    private const string English = """
        save = Save {$name}
        cart {
          items =
            .input {$count :integer}
            .match $count
            one {{One item in {#link}your cart{/link}}}
            * {{{$count} items in {#link}your cart{/link}}}
        }
        braces = Use \{ and \}
        """;

    [Test, DisplayName("en-XA accents and expands text, brackets each pattern and keeps placeholders, markup and escapes")]
    public void AccentedTransform()
    {
        Assert.Equal("[Śààṽéé {$name}]", Rmf2PseudoLocalesV5.Transform("Save {$name}", "en-XA"));
        // Doubled vowels and brackets already make "Read the docs" (13 characters) 19 long, at least 1.35 × 13; "Rhythm
        // syncs" (12, no vowels) needs three '~' to reach 17.
        Assert.Equal("[Ŕééààď ťĥéé ďööćś]", Rmf2PseudoLocalesV5.Transform("Read the docs", "en-XA"));
        Assert.Equal("[Ŕĥÿťĥḿ śÿńćś~~~]", Rmf2PseudoLocalesV5.Transform("Rhythm syncs", "en-XA"));
        Assert.Equal("[ÜÜśéé \\{ ààńď \\}]", Rmf2PseudoLocalesV5.Transform("Use \\{ and \\}", "en-XA"));
        Assert.Equal(".input {$count :integer}\n.match $count\none {{[ÖÖńéé ïïťééḿ ïïń {#link}ÿööüüŕ ćààŕť{/link}]}}\n* {{[{$count} ïïťééḿś]}}",
            Rmf2PseudoLocalesV5.Transform(".input {$count :integer}\n.match $count\none {{One item in {#link}your cart{/link}}}\n* {{{$count} items}}", "en-XA"));
        // Only the text: the placeholder alone gets brackets and no padding.
        Assert.Equal("[{$name}]", Rmf2PseudoLocalesV5.Transform("{$name}", "en-XA"));
    }

    [Test, DisplayName("ar-XB reverses each text run with a right-to-left override and brackets each pattern")]
    public void MirroredTransform()
    {
        Assert.Equal("[‮Save ‬{$name}]", Rmf2PseudoLocalesV5.Transform("Save {$name}", "ar-XB"));
        Assert.Equal("[{$a} {$b}]", Rmf2PseudoLocalesV5.Transform("{$a} {$b}", "ar-XB"));
    }

    [Test, DisplayName("pseudoLocales are generated beside the base files, fall back to it and change only the text")]
    public void GeneratedLocales()
    {
        TranslationSource project = Project(",\"locales\":[\"en\",\"de\"],\"pseudoLocales\":[\"en-XA\"],\"validation\":{\"translationCompleteness\":\"allow\"}");
        TranslationSource[] sources = [Source("translations/en.rmf2", English), Source("translations/de.rmf2", "save = Speichern {$name}\n"),
            Source("translations/settings/en.rmf2", "title = Settings\n")];
        Rmf2ProjectCompilation withPseudo = TranslationCompiler.CompileRmf2ProjectV5(project, sources, null, true);
        Assert.True(withPseudo.Success, string.Join("\n", withPseudo.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Rmf2ProjectV5 linked = withPseudo.Project!;
        Assert.Equal("de,en,en-XA", string.Join(",", linked.Locales.Select(locale => locale.Tag)));
        Assert.Equal("en", linked.Locales.Single(locale => locale.Tag == "en-XA").FallbackTag);
        Rmf2LocaleV5 accented = linked.Locales.Single(locale => locale.Tag == "en-XA");
        Assert.Equal("braces,cart_items,save,settings_title", string.Join(",", accented.DirectResources.Select(resource => resource.Key).Order(StringComparer.Ordinal)));
        Assert.Equal("[Śééťťïïńĝś]", Encoding.UTF8.GetString(accented.DirectResources.Single(resource => resource.Key == "settings_title").Message.Syntax.Source.GetUtf8Bytes()));
        Assert.Equal("translations/settings/en-XA.rmf2", accented.DirectResources.Single(resource => resource.Key == "settings_title").SourceLocation.Path);

        Rmf2ProjectCompilation release = TranslationCompiler.CompileRmf2ProjectV5(project, sources, null, false);
        Assert.True(release.Success, string.Join("\n", release.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Assert.Equal("de,en", string.Join(",", release.Project!.Locales.Select(locale => locale.Tag)));
        Assert.Equal(release.Project.CallerFingerprint, linked.CallerFingerprint, "Pseudo-locales changed the caller contract.");
        Assert.True(release.Project.SourceHash != linked.SourceHash, "The pseudo-locales did not change the source hash.");
    }

    [Test, DisplayName("a document message is bracketed inside each leaf block, and empty blocks and variants stay empty")]
    public void DocumentTransform()
    {
        Assert.Equal("{#p}[Ĥééĺĺöö {$name}]{/p}\n{#ul}{#li}[ÖÖńéé]{/li}{#li}{/li}{/ul}",
            Rmf2PseudoLocalesV5.Transform("{#p}Hello {$name}{/p}\n{#ul}{#li}One{/li}{#li}{/li}{/ul}", "en-XA"));
        Assert.Equal("{#h level=1}[‮Appendix‬]{/h}\n{#p}[{#strong}‮Bold‬{/strong}]{/p}",
            Rmf2PseudoLocalesV5.Transform("{#h level=1}Appendix{/h}\n{#p}{#strong}Bold{/strong}{/p}", "ar-XB"));
        Assert.Equal(".input {$count :integer}\n.match $count\n0 {{}}\n* {{{#p}[{$count} ḟïïĺééś]{/p}}}",
            Rmf2PseudoLocalesV5.Transform(".input {$count :integer}\n.match $count\n0 {{}}\n* {{{#p}{$count} files{/p}}}", "en-XA"));
    }

    private static readonly string[] CorpusLocales = ["en", "de", "fr"];

    [Test, DisplayName("the RMF2 document corpus compiles through both pseudo-locales")]
    public void DocumentCorpus()
    {
        string corpus = RepositoryPaths.Resolve("specs", "translations", "corpus", "rmf2-document-v1");
        JsonObject config = JsonNode.Parse(File.ReadAllText(Path.Combine(corpus, "runic.json")))!.AsObject();
        config["pseudoLocales"] = new JsonArray("en-XA", "ar-XB");
        TranslationSource project = Source("translations/runic.json", config.ToJsonString());
        TranslationSource[] sources = CorpusLocales.Select(locale =>
            new TranslationSource("translations/" + locale + ".rmf2", File.ReadAllBytes(Path.Combine(corpus, locale + ".rmf2")))).ToArray();
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(project, sources, null, true);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Assert.True(!result.Diagnostics.Any(d => d.Severity == TranslationDiagnosticSeverity.Error), "The pseudo-locales report errors.");
        int baseCount = result.Project!.Locales.Single(locale => locale.Tag == "en").DirectResources.Count;
        foreach (string tag in new[] { "en-XA", "ar-XB" })
            Assert.Equal(baseCount, result.Project.Locales.Single(locale => locale.Tag == tag).DirectResources.Count, tag);
    }

    [Test, DisplayName("ar-XB compiles plural and ordinal selectors, which select with the Arabic rules")]
    public void MirroredPlurals()
    {
        const string plurals = """
            items =
              .input {$count :integer}
              .match $count
              0 {{No items}}
              one {{One item}}
              * {{{$count} items}}
            place =
              .input {$n :integer select=ordinal}
              .match $n
              one {{{$n}st}}
              two {{{$n}nd}}
              * {{{$n}th}}

            """;
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"locales\":[\"en\"],\"pseudoLocales\":[\"en-XA\",\"ar-XB\"]"),
            [Source("translations/en.rmf2", plurals)], null, true);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Assert.True(!result.Diagnostics.Any(), string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Rmf2LocaleV5 mirrored = result.Project!.Locales.Single(locale => locale.Tag == "ar-XB");
        Assert.Equal("items,place", string.Join(",", mirrored.DirectResources.Select(resource => resource.Key).Order(StringComparer.Ordinal)));
    }

    [Test, DisplayName("ar-XB is generated as a right-to-left locale with mirrored text")]
    public void MirroredLocale()
    {
        // Plural selectors in ar-XB use the Arabic rules, which the built-in registry gains with all CLDR locales.
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"locales\":[\"en\"],\"pseudoLocales\":[\"ar-XB\"]"),
            [Source("translations/en.rmf2", "save = Save {$name}\n")], null, true);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Rmf2LocaleV5 mirrored = result.Project!.Locales.Single(locale => locale.Tag == "ar-XB");
        Assert.Equal("[\u202ESave \u202C{$name}]", Encoding.UTF8.GetString(mirrored.DirectResources.Single().Message.Syntax.Source.GetUtf8Bytes()));
    }

    [Test, DisplayName("pseudoLocales are validated and a generated locale cannot have sources of its own")]
    public void Validation()
    {
        TranslationSource[] english = [Source("translations/en.rmf2", "save = Save\n")];
        Bad(",\"pseudoLocales\":[\"qps-ploc\"]", english, "pseudoLocales supports only 'en-XA' and 'ar-XB'.");
        Bad(",\"pseudoLocales\":[\"en-XA\",\"en-xa\"]", english, "Duplicate pseudo-locale 'en-XA'.");
        Bad(",\"locales\":[\"en\",\"en-XA\"],\"pseudoLocales\":[\"en-XA\"]", english, "Pseudo-locale 'en-XA' is generated; remove it from locales.");
        Bad(",\"pseudoLocales\":\"en-XA\"", english, "pseudoLocales must be an array of pseudo-locale tags.");
        TranslationSource[] own = [Source("translations/en.rmf2", "save = Save\n"), Source("translations/en-XA.rmf2", "save = Mine\n")];
        // In a development build and in a release build, where the file would otherwise be discovered as a real locale.
        foreach (bool pseudo in new[] { true, false })
        {
            Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"pseudoLocales\":[\"en-XA\"]"), own, null, pseudo);
            Assert.True(result.Diagnostics.Any(d => d.Id == "RTR0004" && d.Message.StartsWith("Pseudo-locale 'en-XA' is generated; remove its source", StringComparison.Ordinal)),
                $"pseudo={pseudo}:\n" + string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        }
        Rmf2ProjectCompilation direct = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"pseudoLocales\":[\"ar-XB\"]"),
            [Source("translations/en/save.mf2", "Save\n"), Source("translations/ar-XB/save.mf2", "Mine\n")], null, false);
        Assert.True(direct.Diagnostics.Any(d => d.Id == "RTR0004" && d.Message.StartsWith("Pseudo-locale 'ar-XB' is generated; remove its source", StringComparison.Ordinal)),
            string.Join("\n", direct.Diagnostics.Select(d => d.Id + ": " + d.Message)));

        // Without locales, the generated files are discovered like any locale's.
        Rmf2ProjectCompilation discovered = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"pseudoLocales\":[\"ar-XB\"]"), english, null, true);
        Assert.True(discovered.Success, string.Join("\n", discovered.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Assert.Equal("ar-XB,en", string.Join(",", discovered.Project!.Locales.Select(locale => locale.Tag)));
    }

    [Test, DisplayName("direct .mf2 projects get <root>/<tag>/<id>.mf2 pseudo files")]
    public void DirectSources()
    {
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(Project(",\"pseudoLocales\":[\"en-XA\"]"),
            [Source("translations/en/greeting.mf2", "Hello {$name}\n")], null, true);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        Rmf2LocaleV5 accented = result.Project!.Locales.Single(locale => locale.Tag == "en-XA");
        Assert.Equal("translations/en-XA/greeting.mf2", accented.DirectResources.Single().SourceLocation.Path);
    }

    private static void Bad(string config, TranslationSource[] sources, string message)
    {
        foreach (bool pseudo in new[] { false, true })
        {
            Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(Project(config), sources, null, pseudo);
            Assert.True(result.Diagnostics.Any(d => d.Id == "RTR0004" && d.Message == message),
                $"pseudo={pseudo}: no '{message}':\n" + string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        }
    }
}
