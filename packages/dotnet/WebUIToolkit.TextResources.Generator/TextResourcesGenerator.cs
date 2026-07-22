using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using WebUIToolkit.TextResources.Compiler;
using WebUIToolkit.TextResources.Compiler.Generation;

namespace WebUIToolkit.TextResources.Generator;

/// <summary>Generates typed C# text-resource surfaces from explicitly marked additional files.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class TextResourcesGenerator : IIncrementalGenerator
{
    private const string KindMetadata = "build_metadata.AdditionalFiles.WebUIToolkitTextResourceKind";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GeneratorInput> inputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input.Kind != InputKind.None)
            .WithTrackingName("TextResourceInputs");

        IncrementalValueProvider<RuntimeAbiState> runtimeAbi = context.CompilationProvider
            .Select(static (compilation, _) => InspectRuntimeAbi(compilation))
            .WithTrackingName("TextResourceRuntimeAbi");

        context.RegisterSourceOutput(
            inputs.Collect().WithTrackingName("TextResourceCompilation").Combine(runtimeAbi),
            static (productionContext, pair) =>
            {
                if (!pair.Right.IsCompatible)
                {
                    productionContext.ReportDiagnostic(CreateAbiDiagnostic(pair.Right));
                    return;
                }

                Generate(productionContext, pair.Left);
            });
    }

    private static RuntimeAbiState InspectRuntimeAbi(Compilation compilation)
    {
        foreach (MetadataReference reference in compilation.References)
        {
            if (!(compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly) ||
                !string.Equals(assembly.Identity.Name, "WebUIToolkit.TextResources", StringComparison.Ordinal))
                continue;

            INamespaceSymbol? currentNamespace = NamespaceMember(assembly.GlobalNamespace, "WebUIToolkit");
            if (currentNamespace is null) return RuntimeAbiState.Missing;
            currentNamespace = NamespaceMember(currentNamespace, "TextResources");
            if (currentNamespace is null) return RuntimeAbiState.Missing;
            INamedTypeSymbol? compatibility = null;
            foreach (INamedTypeSymbol candidate in currentNamespace.GetTypeMembers("TextResourcesCompatibility"))
            {
                compatibility = candidate;
                break;
            }
            if (compatibility is null) return RuntimeAbiState.Missing;
            foreach (ISymbol member in compatibility.GetMembers("RuntimeAbiVersion"))
            {
                if (member is IFieldSymbol field && field.HasConstantValue && field.ConstantValue is int version)
                    return new RuntimeAbiState(version);
            }

            return RuntimeAbiState.Missing;
        }

        return RuntimeAbiState.Missing;
    }

    private static INamespaceSymbol? NamespaceMember(INamespaceSymbol parent, string name)
    {
        foreach (INamespaceSymbol child in parent.GetNamespaceMembers())
            if (string.Equals(child.Name, name, StringComparison.Ordinal)) return child;
        return null;
    }

    private static Diagnostic CreateAbiDiagnostic(RuntimeAbiState state)
    {
        string message = state.IsMissing
            ? "Referenced WebUIToolkit.TextResources runtime ABI is missing; generated code requires ABI version 1."
            : "Referenced WebUIToolkit.TextResources runtime ABI version " + state.Version + " is incompatible with generated ABI version 1.";
        return Diagnostic.Create(Descriptor("WUTTEXT0024", DiagnosticSeverity.Error), Location.None, message);
    }

    private static GeneratorInput CreateInput(
        AdditionalText additionalText,
        AnalyzerConfigOptionsProvider optionsProvider,
        CancellationToken cancellationToken)
    {
        AnalyzerConfigOptions options = optionsProvider.GetOptions(additionalText);
        if (!options.TryGetValue(KindMetadata, out string? kindValue))
            return default;

        InputKind kind;
        if (string.Equals(kindValue, "Catalog", StringComparison.Ordinal)) kind = InputKind.Catalog;
        else if (string.Equals(kindValue, "Document", StringComparison.Ordinal)) kind = InputKind.Document;
        else return default;

        SourceText? sourceText = additionalText.GetText(cancellationToken);
        string path = NormalizePath(additionalText.Path, optionsProvider.GlobalOptions);
        return sourceText is null
            ? new GeneratorInput(kind, path, null)
            : new GeneratorInput(kind, path, sourceText.ToString());
    }

    private static string NormalizePath(string path, AnalyzerConfigOptions globalOptions)
    {
        string normalized = path.Replace('\\', '/');
        if (globalOptions.TryGetValue(ProjectDirectoryProperty, out string? projectDirectory) &&
            !string.IsNullOrWhiteSpace(projectDirectory))
        {
            string root = projectDirectory.Replace('\\', '/').TrimEnd('/') + "/";
            if (normalized.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(root.Length);
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        return normalized.Length == 0 ? "." : normalized;
    }

    private static void Generate(SourceProductionContext context, IEnumerable<GeneratorInput> inputs)
    {
        var manifests = new List<TextResourceSource>();
        var documents = new List<TextResourceSource>();
        var sourceTexts = new Dictionary<string, SourceText>(StringComparer.Ordinal);

        var materializedInputs = new List<GeneratorInput>();
        foreach (GeneratorInput input in inputs) materializedInputs.Add(input);
        GeneratorInput[] orderedInputs = materializedInputs.ToArray();
        Array.Sort(orderedInputs, static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Path, right.Path);
            return comparison != 0 ? comparison : left.Kind.CompareTo(right.Kind);
        });

        for (int i = 0; i < orderedInputs.Length; i++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            GeneratorInput input = orderedInputs[i];
            if (input.Text is null)
            {
                context.ReportDiagnostic(CreateUnreadableDiagnostic(input.Path));
                continue;
            }

            SourceText sourceText = SourceText.From(input.Text, new UTF8Encoding(false, true));
            sourceTexts[input.Path] = sourceText;
            var source = new TextResourceSource(input.Path, new UTF8Encoding(false, true).GetBytes(input.Text));
            if (input.Kind == InputKind.Catalog) manifests.Add(source);
            else documents.Add(source);
        }

        TextResourceCompilation compilation = TextResourceCompiler.Compile(
            manifests,
            documents,
            options: null,
            context.CancellationToken);

        bool hasErrors = false;
        for (int i = 0; i < compilation.Diagnostics.Count; i++)
        {
            TextResourceDiagnostic diagnostic = compilation.Diagnostics[i];
            context.ReportDiagnostic(CreateDiagnostic(diagnostic, sourceTexts));
            if (diagnostic.Severity == TextResourceDiagnosticSeverity.Error) hasErrors = true;
        }

        if (hasErrors) return;

        var emittedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int catalogIndex = 0; catalogIndex < compilation.Catalogs.Count; catalogIndex++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            CompiledTextCatalog catalog = compilation.Catalogs[catalogIndex];
            TextResourceGeneratedOutput[] outputs =
            {
                TextResourceOutputRenderer.RenderCSharpKeys(catalog),
                TextResourceOutputRenderer.RenderCSharpAccessors(catalog),
                TextResourceOutputRenderer.RenderCSharpCatalogData(catalog),
                TextResourceOutputRenderer.RenderCSharpRegistration(catalog),
            };

            for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
            {
                TextResourceGeneratedOutput output = outputs[outputIndex];
                if (!emittedHints.Add(output.RelativePath))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptor("WUTTEXT0018", DiagnosticSeverity.Error),
                        Location.None,
                        "Generated hint name '" + output.RelativePath + "' collides across catalogs."));
                    continue;
                }

                context.AddSource(output.RelativePath, SourceText.From(output.Text, new UTF8Encoding(false, true)));
            }
        }
    }

    private static Diagnostic CreateUnreadableDiagnostic(string path)
    {
        return Diagnostic.Create(
            Descriptor("WUTTEXT0001", DiagnosticSeverity.Error),
            Location.Create(path, default, default),
            "Source text could not be read.");
    }

    private static Diagnostic CreateDiagnostic(
        TextResourceDiagnostic diagnostic,
        Dictionary<string, SourceText> sourceTexts)
    {
        Location location = Location.None;
        if (sourceTexts.TryGetValue(diagnostic.Location.Path, out SourceText? sourceText))
        {
            int startLine = Clamp(diagnostic.Location.Line - 1, 0, sourceText.Lines.Count - 1);
            int endLine = Clamp(diagnostic.Location.EndLine - 1, startLine, sourceText.Lines.Count - 1);
            TextLine startTextLine = sourceText.Lines[startLine];
            TextLine endTextLine = sourceText.Lines[endLine];
            int startColumn = Clamp(diagnostic.Location.Column - 1, 0, startTextLine.Span.Length);
            int endColumn = Clamp(diagnostic.Location.EndColumn - 1, 0, endTextLine.Span.Length);
            int start = startTextLine.Start + startColumn;
            int end = endTextLine.Start + endColumn;
            if (end < start) end = start;
            var lineSpan = new LinePositionSpan(
                new LinePosition(startLine, startColumn),
                new LinePosition(endLine, endColumn));
            location = Location.Create(diagnostic.Location.Path, TextSpan.FromBounds(start, end), lineSpan);
        }

        DiagnosticSeverity severity = diagnostic.Severity == TextResourceDiagnosticSeverity.Warning
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        return Diagnostic.Create(Descriptor(diagnostic.Id, severity), location, diagnostic.Message);
    }

    private static DiagnosticDescriptor Descriptor(string id, DiagnosticSeverity severity)
    {
        return new DiagnosticDescriptor(
            id,
            "Text resource compilation",
            "{0}",
            "WebUIToolkit.TextResources",
            severity,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/ViktorJannicke/WebUIToolkit");
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        return value > maximum ? maximum : value;
    }

    private enum InputKind
    {
        None,
        Catalog,
        Document,
    }

    private readonly struct GeneratorInput : IEquatable<GeneratorInput>
    {
        internal GeneratorInput(InputKind kind, string path, string? text)
        {
            Kind = kind;
            Path = path;
            Text = text;
        }

        internal InputKind Kind { get; }
        internal string Path { get; }
        internal string? Text { get; }

        public bool Equals(GeneratorInput other) =>
            Kind == other.Kind &&
            string.Equals(Path, other.Path, StringComparison.Ordinal) &&
            string.Equals(Text, other.Text, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is GeneratorInput other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Kind;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Path ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Text ?? string.Empty);
                return hash;
            }
        }
    }

    private readonly struct RuntimeAbiState : IEquatable<RuntimeAbiState>
    {
        internal static readonly RuntimeAbiState Missing = new RuntimeAbiState(-1);

        internal RuntimeAbiState(int version) => Version = version;

        internal int Version { get; }
        internal bool IsMissing => Version < 0;
        internal bool IsCompatible => Version == 1;

        public bool Equals(RuntimeAbiState other) => Version == other.Version;
        public override bool Equals(object? obj) => obj is RuntimeAbiState other && Equals(other);
        public override int GetHashCode() => Version;
    }
}
