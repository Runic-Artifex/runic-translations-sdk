using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runic.Translations.Compiler.Generation;
using TUnit.Core;

namespace Runic.Translations.Compiler.Tests;

// Per-source units let hosts (the source generator, `runic-translations serve`) recompile only edited
// files. Linking from units must be indistinguishable from compiling the sources directly.
[Category("rmf2-semantic-v5")]
internal sealed class Rmf2SourceUnitTests
{
    private const string English = """
        title = Shop
        greeting =
          .input {$name :string}
          .input {$count :integer}
          .match $count
          one {{Hello {$name}, one item}}
          * {{Hello {$name}, {$count} items}}
        """;
    private const string German = """
        title = Laden
        greeting =
          .input {$name :string}
          .input {$count :integer}
          .match $count
          one {{Hallo {$name}, ein Artikel}}
          * {{Hallo {$name}, {$count} Artikel}}
        """;

    [Test, DisplayName("RMF2 v5 source units link exactly like direct compilation")]
    public void LinkMatchesDirectCompilation()
    {
        var options = new TranslationCompilerOptions();
        foreach (TranslationSource[] sources in new[]
                 {
                     new[] { Source("translations/en.rmf2", English), Source("translations/de.rmf2", German) },
                     new[] { Source("translations/en/title.mf2", "Shop"), Source("translations/de/title.mf2", "Laden") },
                     // A translation that changes the caller contract fails identically.
                     new[] { Source("translations/en.rmf2", English), Source("translations/de.rmf2", German.Replace(":integer", ":number", StringComparison.Ordinal)) },
                     // Diagnostics are mapped back into each source file.
                     new[] { Source("translations/en.rmf2", "title = {$x :nope}\n"), Source("translations/de.rmf2", "title = Laden\n") },
                 })
        {
            Rmf2ProjectCompilationV5 direct = TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(), sources.AsEnumerable().Reverse(), options);
            Rmf2ProjectCompilationV5 linked = TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(),
                sources.Select(source => Rmf2SourceUnitV5.Create(source, options)).ToArray(), options);
            Assert.Equal(Describe(direct), Describe(linked), sources[0].Path);
        }
    }

    [Test, DisplayName("RMF2 v5 source units relower only edited sources")]
    public void UnitsReuseUnchangedLowering()
    {
        var options = new TranslationCompilerOptions();
        Rmf2SourceUnitV5 english = Rmf2SourceUnitV5.Create(Source("translations/en.rmf2", English), options);
        Rmf2SourceUnitV5 german = Rmf2SourceUnitV5.Create(Source("translations/de.rmf2", German), options);
        Rmf2ProjectV5 first = Link(options, english, german);
        Assert.Equal(2, english.LoweringCount, "English lowering");
        Assert.Equal(2, german.LoweringCount, "German lowering");
        Rmf2ProjectV5 again = Link(options, english, german);
        Assert.Equal(2, english.LoweringCount + german.LoweringCount - 2, "Relinking unchanged units must not lower again");
        Assert.Equal(first.SourceHash, again.SourceHash);

        Rmf2SourceUnitV5 edited = Rmf2SourceUnitV5.Create(Source("translations/de.rmf2", German.Replace("Laden", "Geschäft", StringComparison.Ordinal)), options);
        Assert.True(!edited.Matches(german.Source, options) && english.Matches(Source("translations/en.rmf2", English), options), "Unit identity does not follow the source bytes.");
        Rmf2ProjectV5 afterEdit = Link(options, english, edited);
        Assert.Equal(2, english.LoweringCount, "Editing the German source relowered the English source");
        Assert.Equal(2, edited.LoweringCount, "edited German lowering");
        Assert.True(afterEdit.SourceHash != first.SourceHash, "The edit did not reach the linked project.");

        // A base-locale input change gives translations a new caller contract, so they lower again.
        Rmf2SourceUnitV5 retyped = Rmf2SourceUnitV5.Create(Source("translations/en.rmf2", English.Replace(":integer", ":number", StringComparison.Ordinal)), options);
        Rmf2ProjectCompilationV5 contractChange = TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(), [retyped, edited], options);
        Assert.Equal(3, edited.LoweringCount, "translated message lowered against the new caller contract");
        Assert.Equal(Describe(TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(), [retyped.Source, edited.Source], options)),
            Describe(contractChange), "caller contract change");

        // Units built with other options are recompiled rather than trusted.
        Rmf2ProjectV5 otherOptions = Link(new TranslationCompilerOptions(), english, german);
        Assert.Equal(2, english.LoweringCount, "Units built with different options were reused");
        Assert.Equal(first.SourceHash, otherOptions.SourceHash);
    }

    private static Rmf2ProjectV5 Link(TranslationCompilerOptions options, params Rmf2SourceUnitV5[] units)
    {
        Rmf2ProjectCompilationV5 result = TranslationCompiler.CompileRmf2ProjectV5(Rmf2ProjectV5Tests.Project(), units, options);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => d.Id + ": " + d.Message)));
        return result.Project!;
    }

    private static string Describe(Rmf2ProjectCompilationV5 result)
    {
        var builder = new StringBuilder();
        foreach (TranslationDiagnostic diagnostic in result.Diagnostics)
            builder.Append(diagnostic.Id).Append(' ').Append(diagnostic.Severity).Append(' ').Append(diagnostic.Location).Append(' ').AppendLine(diagnostic.Message);
        if (result.Project is { } project)
        {
            builder.AppendLine(project.CallerFingerprint).AppendLine(project.SourceHash);
            foreach (TranslationGeneratedOutput output in new[]
                     {
                         TranslationOutputRenderer.RenderRmf2V5CSharpKeys(project),
                         TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(project),
                         TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(project),
                     }.Concat(TranslationOutputRenderer.RenderRmf2V5EsmModules(project)))
                builder.AppendLine(output.RelativePath).AppendLine(output.Text);
        }
        return builder.ToString();
    }

    private static TranslationSource Source(string path, string text) => new(path, Encoding.UTF8.GetBytes(text));
}
