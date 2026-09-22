using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Generator;

/// <summary>Generates typed C# translation surfaces from explicitly marked additional files.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class TranslationsGenerator : IIncrementalGenerator
{
    private const string KindMetadata = "build_metadata.AdditionalFiles.RunicTranslationKind";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";
    private readonly TranslationProjectProfile? _profileOverride;

    /// <summary>Creates the shipping generator, selecting an execution profile only from the declared project.</summary>
    public TranslationsGenerator() { }

    internal TranslationsGenerator(TranslationProjectProfile profile) => _profileOverride = profile;

    internal static TranslationsGenerator CreateRmf2ExecutionV2() => new(TranslationProjectProfile.Rmf2ExecutionV2);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        TranslationProjectProfile? profileOverride = _profileOverride;
        IncrementalValuesProvider<GeneratorInput> inputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input.Kind != InputKind.None)
            .WithTrackingName("TranslationInputs");

        IncrementalValueProvider<RuntimeAbiState> runtimeAbi = context.CompilationProvider
            .Select(static (compilation, _) => InspectRuntimeAbi(compilation))
            .WithTrackingName("TranslationRuntimeAbi");

        context.RegisterSourceOutput(
            inputs.Collect().WithTrackingName("TranslationCompilation").Combine(runtimeAbi),
            (productionContext, pair) =>
            {
                Generate(productionContext, pair.Left, pair.Right, profileOverride);
            });
    }

    private static RuntimeAbiState InspectRuntimeAbi(Compilation compilation)
    {
        foreach (MetadataReference reference in compilation.References)
        {
            if (!(compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly) ||
                !string.Equals(assembly.Identity.Name, "Runic.Translations", StringComparison.Ordinal))
                continue;

            INamespaceSymbol? runicNamespace = NamespaceMember(assembly.GlobalNamespace, "Runic");
            INamespaceSymbol? currentNamespace = runicNamespace is null
                ? null
                : NamespaceMember(runicNamespace, "Translations");
            if (currentNamespace is null) return RuntimeAbiState.Missing;
            INamedTypeSymbol? compatibility = null;
            foreach (INamedTypeSymbol candidate in currentNamespace.GetTypeMembers("TranslationsCompatibility"))
            {
                compatibility = candidate;
                break;
            }
            if (compatibility is null) return RuntimeAbiState.Missing;
            int rmf2Version = -1;
            foreach (ISymbol member in compatibility.GetMembers("Rmf2RuntimeAbiVersion"))
                if (member is IFieldSymbol marker && marker.HasConstantValue && marker.ConstantValue is int rmf2) rmf2Version = rmf2;
            foreach (ISymbol member in compatibility.GetMembers("RuntimeAbiVersion"))
            {
                if (member is IFieldSymbol field && field.HasConstantValue && field.ConstantValue is int version)
                    return new RuntimeAbiState(version, rmf2Version);
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

    private static Diagnostic CreateAbiDiagnostic(RuntimeAbiState state, TranslationProjectProfile profile)
    {
        string message = state.IsMissing
            ? profile == TranslationProjectProfile.Rmf2ExecutionV2
                ? "Referenced Runic.Translations runtime ABI is missing; generated RMF2 code requires runtime ABI version 1 and RMF2 ABI version 2."
                : "Referenced Runic.Translations runtime ABI is missing; generated code requires ABI version 1."
            : state.Version != 1
                ? "Referenced Runic.Translations runtime ABI version " + state.Version + " is incompatible with generated ABI version 1."
                : profile == TranslationProjectProfile.Rmf2ExecutionV2
                    ? state.Rmf2Version < 0
                        ? "Referenced Runic.Translations RMF2 runtime ABI is missing; generated RMF2 code requires ABI version 2."
                        : "Referenced Runic.Translations RMF2 runtime ABI version " + state.Rmf2Version + " is incompatible with generated RMF2 ABI version 2."
                    : "Referenced Runic.Translations runtime ABI is incompatible with generated ABI version 1.";
        return Diagnostic.Create(Descriptor("RTR0024", DiagnosticSeverity.Error), Location.None, message);
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
        if (string.Equals(kindValue, "Project", StringComparison.Ordinal)) kind = InputKind.Project;
        else if (string.Equals(kindValue, "Rmf2", StringComparison.Ordinal) || string.Equals(kindValue, "Mf2", StringComparison.Ordinal)) kind = InputKind.Mf2;
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

    private static void Generate(SourceProductionContext context, IEnumerable<GeneratorInput> inputs,
        RuntimeAbiState runtimeAbi, TranslationProjectProfile? profileOverride)
    {
        var projects = new List<TranslationSource>();
        var messages = new List<TranslationSource>();
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
            var source = new TranslationSource(input.Path, new UTF8Encoding(false, true).GetBytes(input.Text));
            if (input.Kind == InputKind.Project) projects.Add(source);
            else messages.Add(source);
        }

        if (projects.Count == 0 && messages.Count == 0) return;
        if (projects.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptor("RTR0002", DiagnosticSeverity.Error),
                Location.None,
                "Exactly one Runic translation project must be supplied."));
            return;
        }
        TranslationProjectProfile profile;
        if (profileOverride is { } forcedProfile) profile = forcedProfile;
        else
        {
            TranslationProjectProfileSelection selection = TranslationCompiler.SelectProjectProfile(
                projects[0], null, context.CancellationToken);
            bool profileErrors = false;
            for (int index = 0; index < selection.Diagnostics.Count; index++)
            {
                TranslationDiagnostic diagnostic = selection.Diagnostics[index];
                context.ReportDiagnostic(CreateDiagnostic(diagnostic, sourceTexts));
                if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) profileErrors = true;
            }
            if (profileErrors) return;
            profile = selection.Profile;
        }
        if (!runtimeAbi.IsCompatible || profile == TranslationProjectProfile.Rmf2ExecutionV2 && runtimeAbi.Rmf2Version != 2)
        {
            context.ReportDiagnostic(CreateAbiDiagnostic(runtimeAbi, profile));
            return;
        }
        if (profile == TranslationProjectProfile.Rmf2ExecutionV2)
        {
            GenerateRmf2V5(context, projects[0], messages, sourceTexts);
            return;
        }

        TranslationCompilation compilation = TranslationCompiler.CompileProject(projects[0], messages, null, context.CancellationToken);

        bool hasErrors = false;
        for (int i = 0; i < compilation.Diagnostics.Count; i++)
        {
            TranslationDiagnostic diagnostic = compilation.Diagnostics[i];
            context.ReportDiagnostic(CreateDiagnostic(diagnostic, sourceTexts));
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) hasErrors = true;
        }

        if (hasErrors) return;

        var emittedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int catalogIndex = 0; catalogIndex < compilation.Catalogs.Count; catalogIndex++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            CompiledTextCatalog catalog = compilation.Catalogs[catalogIndex];
            // V4 output requires ABI 1. ABI 2 explicitly retains that contract;
            // do not infer compatibility for missing or unknown future markers.
            if (catalog.MessageGrammarVersion == 4 && runtimeAbi.Rmf2Version is not (1 or 2))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor("RTR0024", DiagnosticSeverity.Error), Location.None,
                    "RMF2 generated code requires the additive RMF2 runtime ABI version 1. Upgrade the runtime and generator together."));
                continue;
            }
            TranslationGeneratedOutput[] outputs =
            {
                TranslationOutputRenderer.RenderCSharpKeys(catalog),
                TranslationOutputRenderer.RenderCSharpAccessors(catalog),
                TranslationOutputRenderer.RenderCSharpCatalogData(catalog),
                TranslationOutputRenderer.RenderCSharpRegistration(catalog),
            };

            for (int outputIndex = 0; outputIndex < outputs.Length; outputIndex++)
            {
                TranslationGeneratedOutput output = outputs[outputIndex];
                if (!emittedHints.Add(output.RelativePath))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Descriptor("RTR0018", DiagnosticSeverity.Error),
                        Location.None,
                        "Generated hint name '" + output.RelativePath + "' collides across catalogs."));
                    continue;
                }

                context.AddSource(output.RelativePath, SourceText.From(output.Text, new UTF8Encoding(false, true)));
            }
        }
    }

    private static void GenerateRmf2V5(SourceProductionContext context, TranslationSource project,
        IReadOnlyList<TranslationSource> messages, Dictionary<string, SourceText> sourceTexts)
    {
        Rmf2ProjectCompilationV5 compilation = TranslationCompiler.CompileRmf2ProjectV5(project, messages, null, context.CancellationToken);
        bool hasErrors = false;
        for (int index = 0; index < compilation.Diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = compilation.Diagnostics[index];
            context.ReportDiagnostic(CreateDiagnostic(diagnostic, sourceTexts));
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) hasErrors = true;
        }
        if (hasErrors || compilation.Project is null) return;

        Rmf2ProjectV5 linked = compilation.Project;
        if (!Rmf2ProjectV5EmissionEligibility.CanEmit(linked))
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptor(Rmf2ProjectV5EmissionEligibility.DiagnosticId, DiagnosticSeverity.Error), Location.None,
                Rmf2ProjectV5EmissionEligibility.Message));
            return;
        }
        TranslationGeneratedOutput[] outputs =
        {
            TranslationOutputRenderer.RenderRmf2V5CSharpKeys(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(linked),
        };
        var emittedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < outputs.Length; index++)
        {
            TranslationGeneratedOutput output = outputs[index];
            if (!emittedHints.Add(output.RelativePath))
            {
                context.ReportDiagnostic(Diagnostic.Create(Descriptor("RTR0018", DiagnosticSeverity.Error), Location.None,
                    "Generated hint name '" + output.RelativePath + "' collides across catalogs."));
                continue;
            }
            context.AddSource(output.RelativePath, SourceText.From(output.Text, new UTF8Encoding(false, true)));
        }
    }

    private static Diagnostic CreateUnreadableDiagnostic(string path)
    {
        return Diagnostic.Create(
            Descriptor("RTR0001", DiagnosticSeverity.Error),
            Location.Create(path, default, default),
            "Source text could not be read.");
    }

    private static Diagnostic CreateDiagnostic(
        TranslationDiagnostic diagnostic,
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

        DiagnosticSeverity severity = diagnostic.Severity == TranslationDiagnosticSeverity.Warning
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
            "Runic.Translations",
            severity,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/Runic-Artifex/runic-translations");
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        return value > maximum ? maximum : value;
    }

    private enum InputKind
    {
        None,
        Project,
        Mf2,
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

        internal RuntimeAbiState(int version, int rmf2Version = -1) { Version = version; Rmf2Version = rmf2Version; }
        internal int Rmf2Version { get; }

        internal int Version { get; }
        internal bool IsMissing => Version < 0;
        internal bool IsCompatible => Version == 1;

        public bool Equals(RuntimeAbiState other) => Version == other.Version && Rmf2Version == other.Rmf2Version;
        public override bool Equals(object? obj) => obj is RuntimeAbiState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Version, Rmf2Version);
    }
}
