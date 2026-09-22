using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Runic.Translations.Generator.Tests;

internal static class GeneratorTests
{
    private const string Project = """
        {
          "schemaVersion": 1,
          "catalog": "app",
          "code": { "namespace": "Example.Localization", "className": "AppText", "visibility": "public" },
          "baseLocale": "en",
          "locales": [ "en" ]
        }
        """;

    private const string Message = """
        filesDeleted =
          .input {$folder :string}
          .input {$count :integer}
          {{Deleted {$count} files from {$folder}.}}
        """;

    internal static void Register(TestRunner runner)
    {
        runner.Add("generated C# always selects the RMF2 v5 carrier", V5GenerationCompiles);
        runner.Add("generated C# requires the exact base and RMF2 runtime ABI", ExactRuntimeAbiRequirement);
        runner.Add("generated C# executes the selected semantic surface", GeneratedExecution);
        runner.Add("generator input enumeration is deterministic", DeterministicInputOrder);
        runner.Add("unchanged generator inputs stay incrementally cached", IncrementalTrackingIsEnabled);
        runner.Add("multiple project declarations are rejected", MultipleProjects);
        runner.Add("unmarked inputs are invisible", UnmarkedInputsAreIgnored);
        runner.Add("Windows device hint stems are rejected before emission", WindowsDeviceHintStem);
    }

    private static void V5GenerationCompiles()
    {
        GeneratorRun run = GeneratorTestHost.Run(ProjectInput(), MessageInput());
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        Assert.Equal(
            "AppText.Accessors.g.cs|AppText.CatalogData.g.cs|AppText.Keys.g.cs|AppText.Registration.g.cs",
            string.Join("|", HintNames(run)),
            "hint files");
        Diagnostic[] errors = run.Compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.Equal(0, errors.Length, string.Join(Environment.NewLine, errors.Select(static error => error.ToString())));
        string generated = Serialize(run);
        Assert.True(generated.Contains("EnsureRmf2RuntimeAbi(2)", StringComparison.Ordinal), "RMF2 ABI 2 guard missing");
        Assert.True(generated.Contains("Rmf2RuntimeAbiVersion = 2", StringComparison.Ordinal), "RMF2 ABI 2 marker missing");
        Assert.True(generated.Contains("RuntimeAbiVersion = 1", StringComparison.Ordinal), "base generated ABI marker missing");
        Assert.True(generated.Contains("CompiledTextMessage.FromRmf2", StringComparison.Ordinal), "typed v5 message construction missing");
        Assert.True(!generated.Contains("C:/repo", StringComparison.OrdinalIgnoreCase), "absolute path leaked into generated source");
    }

    private static void ExactRuntimeAbiRequirement()
    {
        foreach (RuntimeReferenceMode mode in new[]
                 {
                     RuntimeReferenceMode.Missing,
                     RuntimeReferenceMode.Mismatched,
                     RuntimeReferenceMode.Legacy,
                     RuntimeReferenceMode.Rmf2V1,
                     RuntimeReferenceMode.Rmf2Zero,
                     RuntimeReferenceMode.Rmf2Unknown,
                 })
        {
            GeneratorRun run = GeneratorTestHost.Run(mode, ProjectInput(), MessageInput());
            Assert.Equal("RTR0024", run.SingleResult.Diagnostics.Single().Id, mode + ": ABI diagnostic");
            Assert.Equal(0, run.SingleResult.GeneratedSources.Length, mode + ": generated sources");
        }
    }

    private static void GeneratedExecution()
    {
        const string resource = """
            dynamic =
              .input {$digits :integer}
              .input {$n :number}
              .input {$style :string}
              .local $d = {$digits}
              {{{$n :number style=$style minimumFractionDigits=$d maximumFractionDigits=$digits}}}
            exact =
              .input {$n :number}
              .match $n
              1.0 {{exact}}
              one {{category}}
              * {{other}}
            literal = {1e+2 :number}
            rich = {#strong @open}Hello{/strong @close}
            """;
        string dynamicMember = PathMember("dynamic"), exactMember = PathMember("exact");
        string literalMember = PathMember("literal"), richMember = PathMember("rich");
        string source = $$"""
            namespace Example.Localization;
            public static class ExecutionProbe
            {
                public static string Run()
                {
                    global::Runic.Translations.ITranslationManager manager =
                        AppTextCatalog.CreateManagerAsync().AsTask().GetAwaiter().GetResult();
                    var text = new AppText(manager);
                    string dynamicValue = text.{{dynamicMember}}(2L, 0.125m, "percent");
                    string exact = text.{{exactMember}}(1.00m);
                    string literal = text.{{literalMember}};
                    int richNodes = text.{{richMember}}.Nodes.Length;
                    global::Runic.Translations.TranslationPackContract contract = AppTextCatalog.CreateExternalPackContract("en");
                    return dynamicValue + "|" + exact + "|" + literal + "|" + richNodes.ToString(global::System.Globalization.CultureInfo.InvariantCulture)
                        + "|" + contract.Profile + "|" + contract.MessageGrammarVersion.ToString(global::System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            """;
        GeneratorRun run = GeneratorTestHost.RunWithConsumer(source, ProjectInput(),
            new TestInput("C:/repo/translations/en.rmf2", "Rmf2", resource));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, string.Join("\n", run.SingleResult.Diagnostics));
        Assert.Equal("12.50%|exact|100|3|rmf2-execution-v2|5",
            Execute(run, "Example.Localization.ExecutionProbe", "Run"), "generated execution");
    }

    private static void DeterministicInputOrder()
    {
        TestInput project = ProjectInput();
        TestInput message = MessageInput();
        Assert.Equal(
            Serialize(GeneratorTestHost.Run(project, message)),
            Serialize(GeneratorTestHost.Run(message, project)),
            "generated bytes by input order");
    }

    private static void IncrementalTrackingIsEnabled()
    {
        GeneratorRun run = GeneratorTestHost.Run(ProjectInput(), MessageInput());
        ImmutableDictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> steps = run.SingleResult.TrackedSteps;
        Assert.True(steps.ContainsKey("TranslationInputs"), "input tracking step missing");
        Assert.True(steps.ContainsKey("TranslationCompilation"), "compilation tracking step missing");
        GeneratorRunResult rerun = run.Driver.RunGenerators(run.InputCompilation).GetRunResult().Results.Single();
        Assert.True(
            rerun.TrackedSteps["TranslationInputs"].SelectMany(static step => step.Outputs)
                .All(static output => output.Reason == IncrementalStepRunReason.Cached),
            "unchanged additional inputs were not cached");
    }

    private static void MultipleProjects()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            ProjectInput(),
            ProjectInput(Project.Replace("\"app\"", "\"admin\"", StringComparison.Ordinal), "C:/repo/admin/runic.json"),
            MessageInput());
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0002");
        Assert.True(diagnostic.GetMessage(CultureInfo.InvariantCulture).Contains("Exactly one", StringComparison.Ordinal), "multiple-project message");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "multiple-project generated sources");
    }

    private static void UnmarkedInputsAreIgnored()
    {
        GeneratorRun run = GeneratorTestHost.Run(new TestInput("C:/repo/random.json", "Other", "not json"));
        Assert.Equal(0, run.SingleResult.Diagnostics.Length, "ignored diagnostics");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "ignored generated sources");
    }

    private static void WindowsDeviceHintStem()
    {
        GeneratorRun run = GeneratorTestHost.Run(
            ProjectInput(Project.Replace("AppText", "CON", StringComparison.Ordinal)),
            MessageInput());
        Diagnostic diagnostic = run.SingleResult.Diagnostics.Single(static item => item.Id == "RTR0018");
        Assert.Equal("translations/runic.json", diagnostic.Location.GetLineSpan().Path, "device diagnostic path");
        Assert.Equal(0, run.SingleResult.GeneratedSources.Length, "device generated sources");
    }

    private static string Execute(GeneratorRun run, string typeName, string methodName)
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = run.Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        Assembly assembly = Assembly.Load(stream.ToArray());
        MethodInfo method = assembly.GetType(typeName, throwOnError: true)!.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!;
        return (string)method.Invoke(null, null)!;
    }

    private static string PathMember(params string[] segments) => string.Join("_", segments.Select(Encoded));
    private static string Encoded(string value) => "r_" + Convert.ToHexStringLower(Encoding.UTF8.GetBytes(value.Normalize(NormalizationForm.FormC)));
    private static TestInput ProjectInput(string text = Project, string path = "C:/repo/translations/runic.json") => new(path, "Project", text);
    private static TestInput MessageInput(string text = Message, string path = "C:/repo/translations/en.rmf2") => new(path, "Rmf2", text);
    private static string[] HintNames(GeneratorRun run) => run.SingleResult.GeneratedSources
        .Select(static source => source.HintName).OrderBy(static name => name, StringComparer.Ordinal).ToArray();
    private static string Serialize(GeneratorRun run) => string.Join(
        "\u001e",
        run.SingleResult.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal)
            .Select(static source => source.HintName + "\u001f" + source.SourceText.ToString()));
}
