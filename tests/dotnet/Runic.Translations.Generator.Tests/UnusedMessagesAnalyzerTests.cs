using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using TUnit.Core;

namespace Runic.Translations.Generator.Tests;

// RTR0100: the messages of a generated catalog that no C# code of the project references (opt-in).
internal sealed class UnusedMessagesAnalyzerTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "shop",
          "code": { "namespace": "Shop", "className": "ShopText" },
          "baseLocale": "en",
          "locales": [ "en" ]
        }
        """;
    private const string English = """
        title = Shop
        greeting = Hello {$name}
        items = {$n :integer} items
        unused = Never shown
        """;
    // The readable member, a reference factory and a key property; title through its key only.
    private const string Consumer = """
        namespace Consumer;
        public static class Uses
        {
            public static string Greet(global::Shop.ShopText text) => text.Messages.greeting("Ada");
            public static global::Runic.Translations.TranslationReference Items() => global::Shop.ShopText.References.items(2);
            public static global::Runic.Translations.TranslationKey Title() => global::Shop.ShopTextKeys.r_7469746c65;
        }
        """;

    private static ImmutableArray<Diagnostic> Analyze(bool enabled)
    {
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(Consumer,
            new TestInput("C:/repo/translations/runic.json", "Project", Project), new TestInput("C:/repo/translations/en.rmf2", "Rmf2", English));
        Assert.True(run.Compilation.GetDiagnostics().All(diagnostic => diagnostic.Severity != DiagnosticSeverity.Error),
            "The consumer does not compile: " + string.Join("; ", run.Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)));
        var options = new AnalyzerOptions([], new OptionsProvider(enabled ? new Dictionary<string, string> { [UnusedMessagesAnalyzer.EnableProperty] = "true" } : []));
        return run.Compilation.WithAnalyzers([new UnusedMessagesAnalyzer()], options).GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    [Test, DisplayName("RTR0100 reports only the messages no C# code references, as information, when enabled")]
    public void ReportsUnreferencedMessages()
    {
        ImmutableArray<Diagnostic> diagnostics = Analyze(enabled: true);
        Diagnostic diagnostic = diagnostics.Single();
        Assert.Equal("RTR0100", diagnostic.Id, "id");
        Assert.Equal(DiagnosticSeverity.Info, diagnostic.Severity, "severity");
        Assert.True(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture).StartsWith("Message 'unused' of Shop.ShopText is not referenced from the C# code of this project.", StringComparison.Ordinal), diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(Analyze(enabled: false).IsEmpty, "RTR0100 was reported without RunicTranslationsReportUnusedMessages.");
    }

    private sealed class OptionsProvider(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(values);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Options([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Options([]);
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
    }
}
