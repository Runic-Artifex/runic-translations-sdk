using System;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Package catalogs in both formats (rename plan §6.4, P6): today's runic.json, *.rmf2 and runic: under runic/<catalog>/,
// and the rename's rax.json, *.ramf2 and rax: under rax/<catalog>/. The compiler reads both; the format is still
// runic in 0.24, so nothing warns and a project's own catalog is unchanged.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2CatalogFormatTests
{
    private const string LibraryProject = """
        { "schemaVersion": 1, "catalog": "acme-grid", "code": { "namespace": "Acme.Grid", "className": "GridText" },
          "baseLocale": "en", "locales": ["en", "de"], "sourceRoots": [{ "path": "text", "namespace": ["grid"] }] }
        """;
    // Runic's own functions and markup in the runic: namespace; the rax spelling below differs only in it.
    private const string LibraryEnglish = "id = Row {$id :runic:uuid style=n}\nsorted = Sorted: {$sorted :runic:boolean}\nhelp = Read {#runic:link}the guide{/runic:link}\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{One row}}\n  * {{{$count} rows}}\n";
    private const string LibraryGerman = "id = Zeile {$id :runic:uuid style=n}\nsorted = Sortiert: {$sorted :runic:boolean}\nhelp = Lies {#runic:link}die Anleitung{/runic:link}\nrows =\n  .input {$count :integer}\n  .match $count\n  one {{Eine Zeile}}\n  * {{{$count} Zeilen}}\n";

    private static string Rax(string source) => source.Replace("runic:", "rax:", StringComparison.Ordinal);

    private static Rmf2ProjectCompilation CompileRunic() => TranslationCompiler.CompileRmf2ProjectV5(
        new TranslationSource("packages/acme.grid/runic/acme-grid/runic.json", Encoding.UTF8.GetBytes(LibraryProject)),
        [new TranslationSource("packages/acme.grid/runic/acme-grid/text/de.rmf2", Encoding.UTF8.GetBytes(LibraryGerman)),
         new TranslationSource("packages/acme.grid/runic/acme-grid/text/en.rmf2", Encoding.UTF8.GetBytes(LibraryEnglish))]);

    private static Rmf2ProjectCompilation CompileRax() => TranslationCompiler.CompileRmf2ProjectV5(
        new TranslationSource("packages/acme.grid/rax/acme-grid/rax.json", Encoding.UTF8.GetBytes(LibraryProject)),
        [new TranslationSource("packages/acme.grid/rax/acme-grid/text/de.ramf2", Encoding.UTF8.GetBytes(Rax(LibraryGerman))),
         new TranslationSource("packages/acme.grid/rax/acme-grid/text/en.ramf2", Encoding.UTF8.GetBytes(Rax(LibraryEnglish)))]);

    [Test, DisplayName("a catalog in the rax format (rax.json, .ramf2, rax:) compiles as the same catalog in the runic format")]
    public void BothFormatsCompileAlike()
    {
        Rmf2ProjectCompilation runic = CompileRunic(), rax = CompileRax();
        Assert.True(runic.Success, Describe(runic));
        Assert.True(rax.Success, Describe(rax));
        Assert.Equal(runic.Project!.CallerFingerprint, rax.Project!.CallerFingerprint, "the contract fingerprint");
        Assert.Equal("de,en", string.Join(",", rax.Project.Locales.Select(locale => locale.Tag).Order(StringComparer.Ordinal)), "locales from .ramf2 file names");
        Assert.Equal(string.Join(",", runic.Project.CanonicalMessages.Select(message => message.Key)),
            string.Join(",", rax.Project.CanonicalMessages.Select(message => message.Key)), "keys");
        foreach (string locale in new[] { "en", "de" })
            Assert.Equal(Rmf2LocaleArtifactV5.Render(runic.Project, locale).Text, Rmf2LocaleArtifactV5.Render(rax.Project, locale).Text, "the " + locale + " artifact");
        // The generated C# differs only in SourceHash, the hash of the source bytes, which spell the namespace differently.
        static string WithoutSourceHash(string code) => string.Join("\n", code.Split('\n').Where(line => !line.Contains("SourceHash =>", StringComparison.Ordinal)));
        Assert.Equal(WithoutSourceHash(Rmf2CSharpOutputRendererV5.RenderRegistration(runic.Project).Text),
            WithoutSourceHash(Rmf2CSharpOutputRendererV5.RenderRegistration(rax.Project).Text), "the generated C#");
    }

    [Test, DisplayName("an application overrides a rax-format library catalog with its own .rmf2 sources")]
    public void OverridesARaxCatalog()
    {
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2OverlayV5(
            new TranslationSource("packages/acme.grid/rax/acme-grid/rax.json", Encoding.UTF8.GetBytes(LibraryProject)),
            [new TranslationSource("packages/acme.grid/rax/acme-grid/text/de.ramf2", Encoding.UTF8.GetBytes(Rax(LibraryGerman))),
             new TranslationSource("packages/acme.grid/rax/acme-grid/text/en.ramf2", Encoding.UTF8.GetBytes(Rax(LibraryEnglish)))],
            [new TranslationSource("translations/overrides/acme-grid/en.rmf2", Encoding.UTF8.GetBytes("grid {\n  help = See {#runic:link}the manual{/runic:link}\n}\n"))]);
        Assert.True(result.Success, Describe(result));
        Assert.Equal("en=grid_help", string.Join("|", result.Project!.OverlayKeys.Select(pair => pair.Key + "=" + string.Join(",", pair.Value))), "overridden keys");
        Assert.Equal(CompileRunic().Project!.CallerFingerprint, result.Project.CallerFingerprint, "the library's contract");
    }

    [Test, DisplayName("rax: is read only in .ramf2 sources: in a .rmf2 source it stays an unknown name, as before")]
    public void RaxNamespaceOnlyInRaxSources()
    {
        Rmf2ProjectCompilation result = TranslationCompiler.CompileRmf2ProjectV5(
            new TranslationSource("translations/runic.json", Encoding.UTF8.GetBytes(LibraryProject)),
            [new TranslationSource("translations/text/en.rmf2", Encoding.UTF8.GetBytes("id = Row {$id :rax:uuid style=n}\n"))]);
        Assert.True(!result.Success, "rax:uuid is not a function of a .rmf2 source");
        Assert.True(result.Diagnostics.Any(diagnostic => diagnostic.Message.Contains("rax:uuid", StringComparison.Ordinal)), Describe(result));
    }

    [Test, DisplayName("the format of a name: rax.json, .ramf2 and rax: read as today's runic names; nothing warns in 0.24")]
    public void FormatNames()
    {
        Assert.Equal(TranslationCatalogFormat.Runic, TranslationCatalogFormats.Current, "0.24 writes the runic format");
        Assert.Equal(TranslationCatalogFormat.Rax, TranslationCatalogFormats.OfProject("packages/x/rax/x/rax.json"));
        Assert.Equal(TranslationCatalogFormat.Runic, TranslationCatalogFormats.OfProject(@"C:\packages\x\runic\x\runic.json"));
        Assert.Equal(TranslationCatalogFormat.Rax, TranslationCatalogFormats.OfSource("text/en.RAMF2"));
        Assert.Equal(TranslationCatalogFormat.Runic, TranslationCatalogFormats.OfSource("text/en.rmf2"));
        Assert.Equal("en-GB", TranslationCatalogFormats.ResourceStem("en-GB.ramf2"));
        Assert.Equal("de", TranslationCatalogFormats.ResourceStem("de.rmf2"));
        Assert.Equal<string?>(null, TranslationCatalogFormats.ResourceStem("de.mf2"));
        Assert.True(TranslationCatalogFormats.IsSource("x/en.ramf2", TranslationCatalogFormat.Rax) && !TranslationCatalogFormats.IsSource("x/en.ramf2", TranslationCatalogFormat.Runic),
            "a runic catalog does not take .ramf2 sources");
        Assert.Equal("runic:uuid", TranslationCatalogFormats.CanonicalName("rax:uuid", TranslationCatalogFormat.Rax));
        Assert.Equal("acme:badge", TranslationCatalogFormats.CanonicalName("acme:badge", TranslationCatalogFormat.Rax));
        Assert.Equal("rax:uuid", TranslationCatalogFormats.CanonicalName("rax:uuid", TranslationCatalogFormat.Runic));
        Assert.Equal("rax,runic", string.Join(",", TranslationCatalogFormats.PackagePreference.Select(TranslationCatalogFormats.PackageFolder)), "the new names first");

        // In 0.24 neither format is the former one. Once the rename makes rax current, a runic package catalog warns.
        Assert.Equal<string?>(null, TranslationCatalogFormats.FormerFormatWarning("acme-grid", "Acme.Grid", TranslationCatalogFormat.Runic));
        Assert.Equal<string?>(null, TranslationCatalogFormats.FormerFormatWarning("acme-grid", "Acme.Grid", TranslationCatalogFormat.Rax));
        string? after = TranslationCatalogFormats.FormerFormatWarning("acme-grid", "Acme.Grid", TranslationCatalogFormat.Runic, TranslationCatalogFormat.Rax);
        Assert.True(after is not null && after.Contains("Package 'Acme.Grid' ships catalog 'acme-grid' in the former format (runic.json, *.rmf2, runic:, runic/)", StringComparison.Ordinal) &&
            after.Contains("ships rax.json, *.ramf2 and rax:", StringComparison.Ordinal), after ?? "no warning");
        Assert.Equal<string?>(null, TranslationCatalogFormats.FormerFormatWarning("acme-grid", "Acme.Grid", TranslationCatalogFormat.Rax, TranslationCatalogFormat.Rax));
    }

    private static string Describe(Rmf2ProjectCompilation result) =>
        string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Id + " " + diagnostic.Location.Path + " " + diagnostic.Message));
}
