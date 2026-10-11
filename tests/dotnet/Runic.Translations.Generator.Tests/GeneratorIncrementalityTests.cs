using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Runic.Translations.Generator.Tests;

// Cached-step tests: each assertion names the incremental step and the IncrementalStepRunReason that
// proves work was or was not repeated.
internal static class GeneratorIncrementalityTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "Example.Localization", "className": "AppText", "visibility": "public" },
          "baseLocale": "en",
          "locales": [ "en", "de" ]
        }
        """;
    private const string ProjectPath = "C:/repo/translations/runic.json";
    private const string EnglishPath = "C:/repo/translations/en.rmf2";
    private const string GermanPath = "C:/repo/translations/de.rmf2";
    private const string English = "title = Shop\ngreeting =\n  .input {$name :string}\n  {{Hello {$name}!}}\n";
    private const string German = "title = Laden\ngreeting =\n  .input {$name :string}\n  {{Hallo {$name}!}}\n";

    internal static void Register(TestRunner runner)
    {
        runner.Add("editing one translation source reparses only that source", EditReparsesOnlyThatSource);
        runner.Add("C# edits do not rerun translation or runtime ABI work", CSharpEditsAreCached);
        runner.Add("reference changes inspect only the changed references", ReferenceChangesInspectOnlyNewReferences);
        runner.Add("a changed runtime ABI reruns only the link", RuntimeAbiChangeRelinks);
        runner.Add("editing one XAML file revalidates only that file without relinking or rendering", XamlEditRevalidatesOnlyThatFile);
    }

    private const string MainXamlPath = "C:/repo/Views/Main.xaml";
    private const string OtherXamlPath = "C:/repo/Views/Other.xaml";

    private static string Xaml(string key) => """
        <Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:rt="clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf">
          <TextBlock Text="{rt:Message
        """ + " " + key + "}\"/>\n</Window>";

    private static void XamlEditRevalidatesOnlyThatFile()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            new TestInput(ProjectPath, "Project", Project),
            new TestInput(EnglishPath, "Rmf2", English),
            new TestInput(GermanPath, "Rmf2", German),
            new TestInput(MainXamlPath, "Xaml", Xaml("title"), DefaultCatalog: "app"),
            new TestInput(OtherXamlPath, "Xaml", Xaml("title"), DefaultCatalog: "app"));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        string before = Generated(run);

        GeneratorRun edited = GeneratorTestHost.Edit(run, MainXamlPath, Xaml("titel"));
        foreach (string step in new[] { "TranslationSourceUnits", "TranslationProjects", "TranslationRuntimeAbi", "TranslationCompilation", "TranslationLink", "TranslationXamlCatalog" })
            AssertAll(edited, step, IncrementalStepRunReason.Cached);
        IncrementalStepRunReason[] emit = OutputReasons(edited, "TranslationLink");
        Assert.True(emit.Length > 0 && emit.All(static reason => reason == IncrementalStepRunReason.Cached), "source rendering reran: " + string.Join(", ", emit));
        IncrementalStepRunReason[] validated = OutputReasons(edited, "TranslationXamlValidation");
        Assert.Equal(1, validated.Count(static reason => reason == IncrementalStepRunReason.Modified), "XAML validation outputs: " + string.Join(", ", validated));
        var validation = new Dictionary<string, IncrementalStepRunReason>(StringComparer.Ordinal);
        foreach ((object value, IncrementalStepRunReason reason) in edited.SingleResult.TrackedSteps["TranslationXamlValidation"].SelectMany(step => step.Outputs))
            validation[(((TranslationsGenerator.GeneratorInput, XamlCatalogs?))value).Item1.Path] = reason;
        Assert.Equal(IncrementalStepRunReason.Modified, validation[Normalize(MainXamlPath)], "edited XAML");
        Assert.Equal(IncrementalStepRunReason.Cached, validation[Normalize(OtherXamlPath)], "unchanged XAML");
        Assert.Equal("RTR0081", edited.SingleResult.Diagnostics.Single().Id, "edited XAML is revalidated");
        Assert.Equal(before, Generated(edited), "generated sources after a XAML edit");

        // A translation edit relinks and revalidates every XAML file against the new catalog.
        GeneratorRun relinked = GeneratorTestHost.Edit(edited, EnglishPath, English.Replace("Shop", "Store", StringComparison.Ordinal));
        AssertAll(relinked, "TranslationLink", IncrementalStepRunReason.Modified);
        AssertAll(relinked, "TranslationXamlValidation", IncrementalStepRunReason.Modified);
        Assert.Equal("RTR0081", relinked.SingleResult.Diagnostics.Single().Id, "relinked diagnostics");
    }

    // Source output steps fed by the named step (the source rendering output reads TranslationLink).
    private static IncrementalStepRunReason[] OutputReasons(GeneratorRun run, string input) =>
        run.SingleResult.TrackedOutputSteps.SelectMany(pair => pair.Value)
            .Where(step => step.Inputs.Any(source => source.Source.Name == input))
            .SelectMany(step => step.Outputs).Select(output => output.Reason).ToArray();

    private static GeneratorRun Initial() => GeneratorTestHost.Run(
        new TestInput(ProjectPath, "Project", Project),
        new TestInput(EnglishPath, "Rmf2", English),
        new TestInput(GermanPath, "Rmf2", German));

    private static void EditReparsesOnlyThatSource()
    {
        GeneratorRun run = Initial();
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        TranslationsGenerator.SourceUnit english = Unit(run, EnglishPath);
        int englishLowering = english.Compiled!.LoweringCount;
        Assert.True(englishLowering > 0, "The first link did not lower the English messages.");

        GeneratorRun edited = GeneratorTestHost.Edit(run, GermanPath, German.Replace("Laden", "Geschäft", StringComparison.Ordinal));
        AssertReasons(edited, "TranslationSourceUnits", (EnglishPath, IncrementalStepRunReason.Cached), (GermanPath, IncrementalStepRunReason.Modified));
        AssertAll(edited, "TranslationProjects", IncrementalStepRunReason.Cached);
        AssertAll(edited, "TranslationRuntimeAbi", IncrementalStepRunReason.Cached);
        Assert.True(ReferenceEquals(english, Unit(edited, EnglishPath)), "The unchanged English unit was not reused.");
        Assert.Equal(englishLowering, english.Compiled.LoweringCount, "Relinking lowered unchanged English messages again");
        Assert.True(Generated(edited).Contains("Geschäft", StringComparison.Ordinal), "The edit did not reach the generated catalog.");
        AssertAll(edited, "TranslationCompilation", IncrementalStepRunReason.Modified);

        // Saving identical bytes reruns the input but changes nothing downstream.
        GeneratorRun same = GeneratorTestHost.Edit(edited, EnglishPath, English);
        Assert.True(same.SingleResult.TrackedSteps["TranslationInputs"].SelectMany(step => step.Outputs).All(output => output.Reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged),
            "Identical bytes produced a modified input.");
        AssertAll(same, "TranslationSourceUnits", IncrementalStepRunReason.Cached);
        AssertOutputs(same, IncrementalStepRunReason.Cached);
    }

    private static void CSharpEditsAreCached()
    {
        GeneratorRun run = Initial();
        string before = Generated(run);
        GeneratorRun edited = GeneratorTestHost.Recompile(run, compilation =>
        {
            SyntaxTree tree = compilation.SyntaxTrees.Single();
            return compilation.ReplaceSyntaxTree(tree, tree.WithChangedText(Microsoft.CodeAnalysis.Text.SourceText.From(
                "internal static class EntryPoint { internal static int Value => 42; }")));
        });
        foreach (string step in new[] { "TranslationInputs", "TranslationSourceUnits", "TranslationProjects", "TranslationRuntimeReferences", "TranslationRuntimeAbi", "TranslationCompilation" })
            AssertAll(edited, step, IncrementalStepRunReason.Cached);
        AssertOutputs(edited, IncrementalStepRunReason.Cached);
        Assert.Equal(before, Generated(edited), "generated sources after a C# edit");
    }

    private static void ReferenceChangesInspectOnlyNewReferences()
    {
        GeneratorRun run = Initial();
        MetadataReference unrelated = UnrelatedReference();
        GeneratorRun edited = GeneratorTestHost.Recompile(run, compilation => compilation.AddReferences(unrelated));
        IncrementalGeneratorRunStep[] inspections = edited.SingleResult.TrackedSteps["TranslationRuntimeReferences"].ToArray();
        Assert.Equal(1, inspections.SelectMany(step => step.Outputs).Count(output => output.Reason == IncrementalStepRunReason.New), "new reference inspections");
        Assert.True(inspections.SelectMany(step => step.Outputs).Count(output => output.Reason == IncrementalStepRunReason.Cached) > 10,
            "Existing references were inspected again.");
        AssertAll(edited, "TranslationRuntimeAbi", IncrementalStepRunReason.Unchanged);
        AssertOutputs(edited, IncrementalStepRunReason.Cached);
    }

    private static void RuntimeAbiChangeRelinks()
    {
        GeneratorRun run = Initial();
        MetadataReference runtime = run.InputCompilation.References.Single(reference =>
            string.Equals(Path.GetFileName((reference as PortableExecutableReference)?.FilePath), "Runic.Translations.dll", StringComparison.OrdinalIgnoreCase));
        GeneratorRun mismatched = GeneratorTestHost.Recompile(run, compilation =>
            compilation.ReplaceReference(runtime, GeneratorTestHost.SyntheticRuntime(RuntimeReferenceMode.Rmf2V1)));
        AssertAll(mismatched, "TranslationRuntimeAbi", IncrementalStepRunReason.Modified);
        AssertAll(mismatched, "TranslationSourceUnits", IncrementalStepRunReason.Cached);
        Assert.Equal("RTR0024", mismatched.SingleResult.Diagnostics.Single().Id, "ABI diagnostic");
        Assert.Equal(0, mismatched.SingleResult.GeneratedSources.Length, "generated sources with an incompatible runtime");
    }

    private static TranslationsGenerator.SourceUnit Unit(GeneratorRun run, string path) => run.SingleResult.TrackedSteps["TranslationSourceUnits"]
        .SelectMany(step => step.Outputs).Select(output => (TranslationsGenerator.SourceUnit)output.Value)
        .Single(unit => unit.Input.Path == Normalize(path));

    private static void AssertReasons(GeneratorRun run, string step, params (string Path, IncrementalStepRunReason Reason)[] expected)
    {
        var actual = new Dictionary<string, IncrementalStepRunReason>(StringComparer.Ordinal);
        foreach ((object value, IncrementalStepRunReason reason) in run.SingleResult.TrackedSteps[step].SelectMany(item => item.Outputs))
            actual[PathOf(value)] = reason;
        foreach ((string path, IncrementalStepRunReason reason) in expected)
            Assert.Equal(reason, actual[Normalize(path)], $"{step} for {path}");
    }

    private static void AssertAll(GeneratorRun run, string step, IncrementalStepRunReason reason)
    {
        IncrementalStepRunReason[] reasons = run.SingleResult.TrackedSteps[step].SelectMany(item => item.Outputs).Select(output => output.Reason).ToArray();
        Assert.True(reasons.Length > 0 && reasons.All(actual => actual == reason), $"{step}: expected every output {reason}; actual {string.Join(", ", reasons)}.");
    }

    private static void AssertOutputs(GeneratorRun run, IncrementalStepRunReason reason)
    {
        IncrementalStepRunReason[] reasons = run.SingleResult.TrackedOutputSteps.SelectMany(pair => pair.Value).SelectMany(step => step.Outputs).Select(output => output.Reason).ToArray();
        Assert.True(reasons.Length > 0 && reasons.All(actual => actual == reason), $"source output: expected {reason}; actual {string.Join(", ", reasons)}.");
    }

    private static string PathOf(object value) => value switch
    {
        TranslationsGenerator.SourceUnit unit => unit.Input.Path,
        TranslationsGenerator.GeneratorInput input => input.Path,
        _ => throw new InvalidOperationException("Unexpected step value " + value.GetType()),
    };

    private static string Normalize(string path) => path.Substring("C:/repo/".Length);

    private static string Generated(GeneratorRun run) => string.Join("\u001e", run.SingleResult.GeneratedSources
        .OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SourceText.ToString()));

    private static PortableExecutableReference UnrelatedReference()
    {
        string trusted = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        CSharpCompilation compilation = CSharpCompilation.Create("Unrelated",
            [CSharpSyntaxTree.ParseText("namespace Unrelated; public static class Marker { public const int Value = 1; }")],
            trusted.Split(Path.PathSeparator).Where(path => !path.EndsWith("Runic.Translations.dll", StringComparison.OrdinalIgnoreCase)).Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Assert.True(compilation.Emit(stream).Success, "Unrelated reference did not compile.");
        return MetadataReference.CreateFromImage(stream.ToArray());
    }
}
