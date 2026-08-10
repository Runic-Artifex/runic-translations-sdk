using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace RunicTranslations.Generator.Tests;

internal static class GeneratorTestHost
{
    internal static GeneratorRun Run(params TestInput[] inputs) => Run(RuntimeReferenceMode.Matching, inputs);

    internal static GeneratorRun Run(RuntimeReferenceMode runtimeReferenceMode, params TestInput[] inputs)
    {
        var additionalTexts = inputs.Select(static input => (AdditionalText)new MemoryAdditionalText(input.Path, input.Text)).ToImmutableArray();
        var optionsProvider = new TestOptionsProvider(inputs);
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorConsumer",
            new[] { CSharpSyntaxTree.ParseText("internal static class EntryPoint { }", parseOptions) },
            References(runtimeReferenceMode),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new[] { new TranslationsGenerator().AsSourceGenerator() },
            additionalTexts: additionalTexts,
            parseOptions: parseOptions,
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation updated, out ImmutableArray<Diagnostic> driverDiagnostics);
        GeneratorDriverRunResult result = driver.GetRunResult();
        return new GeneratorRun(driver, result, compilation, updated, driverDiagnostics);
    }

    private static IEnumerable<MetadataReference> References(RuntimeReferenceMode runtimeReferenceMode)
    {
        string trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
            if (!IsRuntimeAssembly(path)) yield return MetadataReference.CreateFromFile(path);
        if (runtimeReferenceMode == RuntimeReferenceMode.Matching)
            yield return MetadataReference.CreateFromFile(typeof(TextResourceKey).Assembly.Location);
        else if (runtimeReferenceMode == RuntimeReferenceMode.Mismatched)
            yield return MismatchedRuntimeReference(trustedAssemblies);
    }

    private static PortableExecutableReference MismatchedRuntimeReference(string trustedAssemblies)
    {
        SyntaxTree tree = CSharpSyntaxTree.ParseText("""
            namespace RunicTranslations;
            public static class TranslationsCompatibility
            {
                public const int RuntimeAbiVersion = 2;
            }
            """);
        CSharpCompilation compilation = CSharpCompilation.Create(
            "RunicTranslations",
            new[] { tree },
            trustedAssemblies.Split(Path.PathSeparator)
                .Where(static path => !IsRuntimeAssembly(path))
                .Select(static path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics.Select(static diagnostic => diagnostic.ToString())));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static bool IsRuntimeAssembly(string path) => string.Equals(
        Path.GetFileName(path),
        "RunicTranslations.dll",
        StringComparison.OrdinalIgnoreCase);

    private sealed class MemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        internal MemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text, System.Text.Encoding.UTF8);
        }

        public override string Path { get; }
        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
    }

    private sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, AnalyzerConfigOptions> _fileOptions;
        private readonly AnalyzerConfigOptions _global;

        internal TestOptionsProvider(IEnumerable<TestInput> inputs)
        {
            _global = new DictionaryOptions(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["build_property.ProjectDir"] = "C:/repo/",
            });
            _fileOptions = new Dictionary<string, AnalyzerConfigOptions>(StringComparer.Ordinal);
            foreach (TestInput input in inputs)
            {
                _fileOptions[input.Path] = new DictionaryOptions(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["build_metadata.AdditionalFiles.RunicTextResourceKind"] = input.Kind,
                });
            }
        }

        public override AnalyzerConfigOptions GlobalOptions => _global;
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => DictionaryOptions.Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _fileOptions[textFile.Path];
    }

    private sealed class DictionaryOptions : AnalyzerConfigOptions
    {
        internal static readonly DictionaryOptions Empty = new(new Dictionary<string, string>());
        private readonly IReadOnlyDictionary<string, string> _values;

        internal DictionaryOptions(IReadOnlyDictionary<string, string> values) => _values = values;

        public override bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);
    }
}

internal readonly record struct TestInput(string Path, string Kind, string Text);

internal enum RuntimeReferenceMode
{
    Matching,
    Mismatched,
    Missing,
}

internal sealed record GeneratorRun(
    GeneratorDriver Driver,
    GeneratorDriverRunResult Result,
    Compilation InputCompilation,
    Compilation Compilation,
    ImmutableArray<Diagnostic> DriverDiagnostics)
{
    internal GeneratorRunResult SingleResult => Result.Results.Single();
}
