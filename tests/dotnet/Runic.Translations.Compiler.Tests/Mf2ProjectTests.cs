using System.Linq;
using System.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Compiler.Tests;

internal static class Mf2ProjectTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("MF2 projects compile conventional message files", CompilesProject);
        runner.Add("MF2 project diagnostics reject non-identifier message files", RejectsBadMessageId);
    }

    private static void CompilesProject()
    {
        TranslationCompilation compilation = TranslationCompiler.CompileMf2Project(
            Source("translations/runic.json", """
                {
                  "schemaVersion": 1,
                  "catalog": "app",
                  "code": { "namespace": "Example", "className": "AppText" },
                  "baseLocale": "en",
                  "locales": ["en", "de"]
                }
                """),
            [
                Source("translations/en/application_title.mf2", "Runic application"),
                Source("translations/de/application_title.mf2", "Runische Anwendung"),
                Source("translations/en/file_count.mf2", """
                    .input {$count :integer select=plural}
                    .match $count
                    one {{One file}}
                    * {{{$count} files}}
                    """),
                Source("translations/de/file_count.mf2", """
                    .input {$count :integer select=plural}
                    .match $count
                    one {{Eine Datei}}
                    * {{{$count} Dateien}}
                    """),
            ]);

        Assert.True(compilation.Success, string.Join("\n", compilation.Diagnostics.Select(item => item.Id + ": " + item.Message)));
        Assert.Equal(1, compilation.Catalogs.Count);
        Assert.Equal("application_title", compilation.Catalogs[0].CanonicalResources[0].Key);
        TranslationGeneratedOutput messages = TranslationOutputRenderer.RenderEsmModules(compilation.Catalogs[0])
            .Single(output => output.Kind == TranslationGeneratedOutputKind.EsmMessageNamespace);
        Assert.True(messages.Text.Contains("as application_title", System.StringComparison.Ordinal),
            "Identifier-safe message export was not generated.");
    }

    private static void RejectsBadMessageId()
    {
        TranslationCompilation compilation = TranslationCompiler.CompileMf2Project(
            Source("translations/runic.json", """
                { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en" }
                """),
            [Source("translations/en/application-title.mf2", "Title")]);
        Assert.True(!compilation.Success, "Invalid message filename was accepted.");
        Assert.True(compilation.Diagnostics.Any(item => item.Id == "RTR0006"), "Expected identifier diagnostic was not reported.");
    }

    private static TranslationSource Source(string path, string text) =>
        new(path, new UTF8Encoding(false).GetBytes(text));
}
