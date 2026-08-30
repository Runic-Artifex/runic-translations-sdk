using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Runic.Translations.Generator.Tests;

internal static class GeneratorTests
{
    private const string Catalog = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "Example.Localization", "className": "AppText", "visibility": "public" },
          "defaultLocale": "en",
          "locales": [ { "tag": "en" } ],
          "layers": [ { "name": "base", "priority": 0 } ]
        }
        """;

    private const string Document = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "locale": "en",
          "layer": "base",
          "resources": {
            "Welcome": "Hello",
            "Files": {
              "Deleted": {
                "$value": "Deleted {count} files from {folder}.",
                "$description": "Shown after deletion.",
                "$placeholders": {
                  "folder": { "type": "string" },
                  "count": { "type": "int", "format": "grouped" }
                }
              }
            }
          }
        }
        """;

    internal static void Register(TestRunner runner)
    {
        runner.Add("valid generation emits four compiling concerns", ValidGenerationCompiles);
        runner.Add("compiler diagnostics preserve exact location", ExactDiagnosticLocation);
        runner.Add("multiple catalogs emit independent concerns", MultipleCatalogs);
        runner.Add("input enumeration produces deterministic source bytes", DeterministicInputOrder);
        runner.Add("unmarked inputs are invisible", UnmarkedInputsAreIgnored);
        runner.Add("paths are normalized before compiler diagnostics", PathsAreNormalized);
        runner.Add("unchanged rerun caches tracked incremental inputs", IncrementalTrackingIsEnabled);
        runner.Add("mismatched runtime ABI reports RTR0024", MismatchedRuntimeAbi);
        runner.Add("missing runtime ABI reports RTR0024", MissingRuntimeAbi);
        runner.Add("Windows device hint stems are rejected before emission", WindowsDeviceHintStem);
        runner.Add("case-insensitive hint collisions are rejected before emission", CaseInsensitiveHintCollision);
    }

    private static void ValidGenerationCompiles()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/Resources/en.texts.json", "Document", Document));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, "generator diagnostics");
        string[] names = HintNames(run);
        Assert.Equal("AppText.Accessors.g.cs|AppText.CatalogData.g.cs|AppText.Keys.g.cs|AppText.Registration.g.cs", string.Join("|", names), "hint files");
        Diagnostic[] errors = run.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(0, errors.Length, errors.Length == 0 ? "generated compilation" : string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
        string allText = string.Join("\n", run.SingleResult.GeneratedSources.Select(static source => source.SourceText.ToString()));
        Assert.True(allText.Contains("RuntimeAbiVersion = 1", StringComparison.Ordinal), "generated ABI marker missing");
        Assert.True(allText.Contains("GeneratorVersion = 1", StringComparison.Ordinal), "generator version marker missing");
        Assert.True(!allText.Contains("C:/repo", StringComparison.OrdinalIgnoreCase), "absolute path leaked into generated source");
    }

    private static void ExactDiagnosticLocation()
    {
        const string invalid = "{\n  \"schemaVersion\": 1,\n  \"catalog\": \"app\",\n  \"locale\": \"en\",\n  \"layer\": \"base\",\n  \"resources\": { \"Bad-Key\": \"x\" }\n}";
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/Resources/en.texts.json", "Document", invalid));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0006");
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal("Resources/en.texts.json", span.Path, "diagnostic path");
        Assert.Equal(5, span.StartLinePosition.Line, "diagnostic start line");
        Assert.Equal(17, span.StartLinePosition.Character, "diagnostic start column");
        Assert.Equal(26, span.EndLinePosition.Character, "diagnostic end column");
    }

    private static void MultipleCatalogs()
    {
        string secondCatalog = Catalog.Replace("\"app\"", "\"admin\"").Replace("AppText", "AdminText");
        string secondDocument = Document.Replace("\"app\"", "\"admin\"");
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/a.catalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/a.en.json", "Document", Document),
            new TestInput("C:/repo/b.catalog.json", "Catalog", secondCatalog),
            new TestInput("C:/repo/b.en.json", "Document", secondDocument));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, "multi-catalog diagnostics");
        Assert.Equal(8, run.SingleResult.GeneratedSources.Length, "multi-catalog source count");
        Assert.Equal(
            "AdminText.Accessors.g.cs|AdminText.CatalogData.g.cs|AdminText.Keys.g.cs|AdminText.Registration.g.cs|AppText.Accessors.g.cs|AppText.CatalogData.g.cs|AppText.Keys.g.cs|AppText.Registration.g.cs",
            string.Join("|", HintNames(run)),
            "multi-catalog hints");
    }

    private static void DeterministicInputOrder()
    {
        TestInput catalog = new("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog);
        TestInput document = new("C:/repo/Resources/en.texts.json", "Document", Document);
        GeneratorRun first = GeneratorTestHost.Run(catalog, document);
        GeneratorRun second = GeneratorTestHost.Run(document, catalog);
        Assert.Equal(Serialize(first), Serialize(second), "generated bytes by input order");
    }

    private static void UnmarkedInputsAreIgnored()
    {
        GeneratorRun run = GeneratorTestHost.Run(new TestInput("C:/repo/random.json", "Other", "not json"));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, "ignored diagnostics");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "ignored generated sources");
    }

    private static void PathsAreNormalized()
    {
        GeneratorRun run = GeneratorTestHost.Run(new TestInput("C:/repo/Resources/bad.json", "Catalog", "["));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0001");
        Assert.Equal("Resources/bad.json", diagnostic.Location.GetLineSpan().Path, "normalized diagnostic path");
        Assert.Equal(1, diagnostic.Location.SourceSpan.Start, "diagnostic start offset");
    }

    private static void IncrementalTrackingIsEnabled()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/Resources/en.texts.json", "Document", Document));
        ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> steps = run.SingleResult.TrackedSteps;
        Assert.True(steps.ContainsKey("TranslationInputs"), "input tracking step missing");
        Assert.True(steps.ContainsKey("TranslationCompilation"), "compilation tracking step missing");
        GeneratorDriver rerunDriver = run.Driver.RunGenerators(run.InputCompilation);
        GeneratorRunResult rerun = rerunDriver.GetRunResult().Results.Single();
        ImmutableArray<IncrementalGeneratorRunStep> inputSteps = rerun.TrackedSteps["TranslationInputs"];
        Assert.True(
            inputSteps.SelectMany(static step => step.Outputs).All(static output => output.Reason == IncrementalStepRunReason.Cached),
            "unchanged additional inputs were not cached");
    }

    private static void MismatchedRuntimeAbi()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            RuntimeReferenceMode.Mismatched,
            new TestInput("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/Resources/en.texts.json", "Document", Document));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0024");
        Assert.Equal(
            "Referenced Runic.Translations runtime ABI version 2 is incompatible with generated ABI version 1.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            "mismatched ABI message");
        Assert.Equal(Location.None, diagnostic.Location, "mismatched ABI location");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "mismatched ABI generated sources");
    }

    private static void MissingRuntimeAbi()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            RuntimeReferenceMode.Missing,
            new TestInput("C:/repo/Resources/app.textcatalog.json", "Catalog", Catalog),
            new TestInput("C:/repo/Resources/en.texts.json", "Document", Document));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0024");
        Assert.Equal(
            "Referenced Runic.Translations runtime ABI is missing; generated code requires ABI version 1.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture),
            "missing ABI message");
        Assert.Equal(Location.None, diagnostic.Location, "missing ABI location");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "missing ABI generated sources");
    }

    private static void WindowsDeviceHintStem()
    {
        string manifest = Catalog.Replace("AppText", "CON", StringComparison.Ordinal);
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/Resources/device.catalog.json", "Catalog", manifest),
            new TestInput("C:/repo/Resources/device.en.json", "Document", Document));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0018");
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        Assert.Equal("Resources/device.catalog.json", span.Path, "device diagnostic path");
        Assert.Equal("Generated class name 'CON' produces a Windows-reserved filename stem.", diagnostic.GetMessage(CultureInfo.InvariantCulture), "device diagnostic message");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "device generated sources");
    }

    private static void CaseInsensitiveHintCollision()
    {
        string firstCatalog = Catalog.Replace("\"app\"", "\"alpha\"", StringComparison.Ordinal).Replace("AppText", "Foo", StringComparison.Ordinal);
        string secondCatalog = Catalog.Replace("\"app\"", "\"beta\"", StringComparison.Ordinal).Replace("AppText", "foo", StringComparison.Ordinal);
        string firstDocument = Document.Replace("\"app\"", "\"alpha\"", StringComparison.Ordinal);
        string secondDocument = Document.Replace("\"app\"", "\"beta\"", StringComparison.Ordinal);
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput("C:/repo/Resources/a.catalog.json", "Catalog", firstCatalog),
            new TestInput("C:/repo/Resources/a.en.json", "Document", firstDocument),
            new TestInput("C:/repo/Resources/z.catalog.json", "Catalog", secondCatalog),
            new TestInput("C:/repo/Resources/z.en.json", "Document", secondDocument));
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0018");
        Assert.Equal("Resources/z.catalog.json", diagnostic.Location.GetLineSpan().Path, "case collision path");
        Assert.True(diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("collides case-insensitively", StringComparison.Ordinal), "case collision message");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "case collision generated sources");
    }

    private static string[] HintNames(GeneratorRun run) => run.SingleResult.GeneratedSources
        .Select(static source => source.HintName)
        .OrderBy(static name => name, StringComparer.Ordinal)
        .ToArray();

    private static string Serialize(GeneratorRun run) => string.Join(
        "\u001e",
        run.SingleResult.GeneratedSources
            .OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .Select(static source => source.HintName + "\u001f" + source.SourceText.ToString()));
}
