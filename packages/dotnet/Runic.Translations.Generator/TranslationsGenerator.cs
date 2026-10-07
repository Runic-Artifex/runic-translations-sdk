using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;

namespace Runic.Translations.Generator;

/// <summary>Generates typed C# translation surfaces from explicitly marked additional files.</summary>
/// <remarks>
/// Each translation source is parsed in its own incremental step (<c>TranslationSourceUnits</c>), so an
/// edit reparses only that file; its messages are lowered once and memoized on the cached unit. The
/// catalog is then linked from all units. The runtime ABI is read from metadata once per reference
/// (<c>TranslationRuntimeReferences</c>), not from symbols on every compilation, so C# edits never
/// re-run translation work.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class TranslationsGenerator : IIncrementalGenerator
{
    private const string KindMetadata = "build_metadata.AdditionalFiles.RunicTranslationKind";
    private const string ProjectDirectoryProperty = "build_property.ProjectDir";
    private const string RuntimeAssemblyName = "Runic.Translations";
    private const string CompatibilityNamespace = "Runic.Translations";
    private const string CompatibilityType = "TranslationsCompatibility";
    private static readonly TranslationCompilerOptions CompilerOptions = new();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<GeneratorInput> inputs = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (pair, cancellationToken) => CreateInput(pair.Left, pair.Right, cancellationToken))
            .Where(static input => input.Kind != InputKind.None)
            .WithTrackingName("TranslationInputs");

        IncrementalValuesProvider<SourceUnit> units = inputs
            .Where(static input => input.Kind == InputKind.Mf2)
            .Select(static (input, cancellationToken) => SourceUnit.Create(input, cancellationToken))
            .WithTrackingName("TranslationSourceUnits");

        IncrementalValueProvider<ImmutableArray<GeneratorInput>> projects = inputs
            .Where(static input => input.Kind == InputKind.Project)
            .Collect()
            .WithTrackingName("TranslationProjects");

        IncrementalValueProvider<RuntimeAbiState> runtimeAbi = context.MetadataReferencesProvider
            .Select(static (reference, cancellationToken) => InspectRuntimeReference(reference, cancellationToken))
            .WithTrackingName("TranslationRuntimeReferences")
            .Collect()
            .Select(static (references, _) => SelectRuntimeAbi(references))
            .WithTrackingName("TranslationRuntimeAbi");

        context.RegisterSourceOutput(
            units.Collect().Combine(projects).WithTrackingName("TranslationCompilation").Combine(runtimeAbi),
            static (productionContext, pair) => Generate(productionContext, pair.Left.Left, pair.Left.Right, pair.Right));
    }

    // Reads the ABI markers of a referenced Runic.Translations assembly. Other references are
    // rejected by their assembly name. Roslyn reruns this only for added or replaced references.
    private static RuntimeReference InspectRuntimeReference(MetadataReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return reference switch
        {
            PortableExecutableReference executable => InspectMetadata(executable),
            CompilationReference compilation => InspectCompilation(compilation.Compilation),
            _ => RuntimeReference.None,
        };
    }

    private static RuntimeReference InspectMetadata(PortableExecutableReference reference)
    {
        Metadata metadata;
        try { metadata = reference.GetMetadata(); }
        catch (Exception exception) when (exception is BadImageFormatException or System.IO.IOException) { return RuntimeReference.None; }
        ModuleMetadata? module = metadata switch
        {
            AssemblyMetadata assembly => assembly.GetModules() is { Length: > 0 } modules ? modules[0] : null,
            ModuleMetadata single => single,
            _ => null,
        };
        if (module is null) return RuntimeReference.None;
        MetadataReader reader = module.GetMetadataReader();
        if (!reader.IsAssembly || !reader.StringComparer.Equals(reader.GetAssemblyDefinition().Name, RuntimeAssemblyName))
            return RuntimeReference.None;
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            if (!reader.StringComparer.Equals(type.Name, CompatibilityType) ||
                !reader.StringComparer.Equals(type.Namespace, CompatibilityNamespace) ||
                !type.GetDeclaringType().IsNil)
                continue;
            int version = -1, rmf2Version = -1, typedSlotsVersion = -1;
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                ConstantHandle constantHandle = field.GetDefaultValue();
                if (constantHandle.IsNil) continue;
                Constant constant = reader.GetConstant(constantHandle);
                if (constant.TypeCode != ConstantTypeCode.Int32) continue;
                int value = reader.GetBlobReader(constant.Value).ReadInt32();
                if (reader.StringComparer.Equals(field.Name, "RuntimeAbiVersion")) version = value;
                else if (reader.StringComparer.Equals(field.Name, "Rmf2RuntimeAbiVersion")) rmf2Version = value;
                else if (reader.StringComparer.Equals(field.Name, "TypedSlotBindingsVersion")) typedSlotsVersion = value;
            }
            return new RuntimeReference(true, version < 0 ? RuntimeAbiState.Missing : new RuntimeAbiState(version, rmf2Version, typedSlotsVersion));
        }
        return new RuntimeReference(true, RuntimeAbiState.Missing);
    }

    // A project reference in an IDE workspace: read the constants from its symbols.
    private static RuntimeReference InspectCompilation(Compilation compilation)
    {
        if (!string.Equals(compilation.AssemblyName, RuntimeAssemblyName, StringComparison.Ordinal)) return RuntimeReference.None;
        INamedTypeSymbol? compatibility = compilation.Assembly.GetTypeByMetadataName(CompatibilityNamespace + "." + CompatibilityType);
        if (compatibility is null) return new RuntimeReference(true, RuntimeAbiState.Missing);
        int version = Constant(compatibility, "RuntimeAbiVersion"), rmf2Version = Constant(compatibility, "Rmf2RuntimeAbiVersion"),
            typedSlotsVersion = Constant(compatibility, "TypedSlotBindingsVersion");
        return new RuntimeReference(true, version < 0 ? RuntimeAbiState.Missing : new RuntimeAbiState(version, rmf2Version, typedSlotsVersion));

        static int Constant(INamedTypeSymbol type, string name)
        {
            foreach (ISymbol member in type.GetMembers(name))
                if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: int value }) return value;
            return -1;
        }
    }

    private static RuntimeAbiState SelectRuntimeAbi(ImmutableArray<RuntimeReference> references)
    {
        foreach (RuntimeReference reference in references)
            if (reference.IsRuntime) return reference.State;
        return RuntimeAbiState.Missing;
    }

    private static Diagnostic CreateAbiDiagnostic(RuntimeAbiState state)
    {
        string message = state.IsMissing
            ? "Referenced Runic.Translations runtime ABI is missing; generated RMF2 code requires runtime ABI version 1 and RMF2 ABI version 2."
            : state.Version != 1
                ? "Referenced Runic.Translations runtime ABI version " + state.Version + " is incompatible with generated ABI version 1."
                : state.Rmf2Version < 0
                    ? "Referenced Runic.Translations RMF2 runtime ABI is missing; generated RMF2 code requires ABI version 2."
                    : "Referenced Runic.Translations RMF2 runtime ABI version " + state.Rmf2Version + " is incompatible with generated RMF2 ABI version 2.";
        return Diagnostic.Create(TranslationsDiagnostics.RuntimeAbi, Location.None, message);
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

    private static void Generate(SourceProductionContext context, ImmutableArray<SourceUnit> units,
        ImmutableArray<GeneratorInput> projectInputs, RuntimeAbiState runtimeAbi)
    {
        var unreadable = new List<GeneratorInput>();
        var projects = new List<GeneratorInput>();
        var compiled = new List<Rmf2SourceUnitV5>(units.Length);
        var sourceTexts = new Dictionary<string, SourceText>(StringComparer.Ordinal);
        foreach (GeneratorInput projectInput in projectInputs)
        {
            if (projectInput.Text is null) unreadable.Add(projectInput);
            else projects.Add(projectInput);
        }
        foreach (SourceUnit unit in units)
        {
            if (unit.Compiled is null) { unreadable.Add(unit.Input); continue; }
            compiled.Add(unit.Compiled);
            sourceTexts[unit.Input.Path] = unit.Text!;
        }

        unreadable.Sort(static (left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Path, right.Path);
            return comparison != 0 ? comparison : left.Kind.CompareTo(right.Kind);
        });
        foreach (GeneratorInput input in unreadable)
            context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.UnreadableSource, Location.Create(input.Path, default, default), "Source text could not be read."));

        if (projects.Count == 0 && compiled.Count == 0) return;
        if (projects.Count != 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.DuplicateInputs, Location.None,
                "Exactly one Runic translation project must be supplied."));
            return;
        }
        if (!runtimeAbi.IsCompatible)
        {
            context.ReportDiagnostic(CreateAbiDiagnostic(runtimeAbi));
            return;
        }

        GeneratorInput selected = projects[0];
        sourceTexts[selected.Path] = SourceText.From(selected.Text!, StrictUtf8);
        var project = new TranslationSource(selected.Path, StrictUtf8.GetBytes(selected.Text!));
        GenerateRmf2V5(context, project, compiled, sourceTexts, runtimeAbi.SupportsReadableSurface);
    }

    private static void GenerateRmf2V5(SourceProductionContext context, TranslationSource project,
        IReadOnlyList<Rmf2SourceUnitV5> units, Dictionary<string, SourceText> sourceTexts, bool readableSurface)
    {
        Rmf2ProjectCompilationV5 compilation = TranslationCompiler.CompileRmf2ProjectV5(project, units, CompilerOptions, context.CancellationToken);
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
            context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.Get(Rmf2ProjectV5EmissionEligibility.DiagnosticId), Location.None,
                Rmf2ProjectV5EmissionEligibility.Message));
            return;
        }
        var outputs = new List<TranslationGeneratedOutput>(5)
        {
            TranslationOutputRenderer.RenderRmf2V5CSharpKeys(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(linked),
        };
        // The readable surface needs the typed slot types of a runtime that declares
        // TypedSlotBindingsVersion. Without it, keep the encoded files and explain the gap.
        if (readableSurface) outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpReadable(linked));
        else context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.ReadableSurfaceUnsupported, Location.None,
            "The referenced Runic.Translations runtime does not support the readable C# surface; update it. The encoded accessors are still generated."));
        var emittedHints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < outputs.Count; index++)
        {
            TranslationGeneratedOutput output = outputs[index];
            if (!emittedHints.Add(output.RelativePath))
            {
                context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.NameCollision, Location.None,
                    "Generated hint name '" + output.RelativePath + "' collides across catalogs."));
                continue;
            }
            context.AddSource(output.RelativePath, SourceText.From(output.Text, StrictUtf8));
        }
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
        return Diagnostic.Create(TranslationsDiagnostics.Get(diagnostic.Id), location, severity,
            additionalLocations: null, properties: null, diagnostic.Message);
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        if (value < minimum) return minimum;
        return value > maximum ? maximum : value;
    }

    internal enum InputKind
    {
        None,
        Project,
        Mf2,
    }

    internal readonly struct GeneratorInput : IEquatable<GeneratorInput>
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

    // One parsed translation source. Equality is the input's, so an unchanged file keeps its cached
    // unit, including the memoized lowering of its messages, across generator runs.
    internal sealed class SourceUnit : IEquatable<SourceUnit>
    {
        private SourceUnit(GeneratorInput input, SourceText? text, Rmf2SourceUnitV5? compiled)
        {
            Input = input;
            Text = text;
            Compiled = compiled;
        }

        internal GeneratorInput Input { get; }
        internal SourceText? Text { get; }

        /// <summary>The compiled unit, or null when the source text could not be read.</summary>
        internal Rmf2SourceUnitV5? Compiled { get; }

        internal static SourceUnit Create(GeneratorInput input, CancellationToken cancellationToken)
        {
            if (input.Text is null) return new SourceUnit(input, null, null);
            var source = new TranslationSource(input.Path, StrictUtf8.GetBytes(input.Text));
            return new SourceUnit(input, SourceText.From(input.Text, StrictUtf8),
                Rmf2SourceUnitV5.Create(source, CompilerOptions, cancellationToken));
        }

        public bool Equals(SourceUnit? other) => other is not null && Input.Equals(other.Input);
        public override bool Equals(object? obj) => Equals(obj as SourceUnit);
        public override int GetHashCode() => Input.GetHashCode();
    }

    private readonly struct RuntimeReference : IEquatable<RuntimeReference>
    {
        internal static RuntimeReference None => default;

        internal RuntimeReference(bool isRuntime, RuntimeAbiState state)
        {
            IsRuntime = isRuntime;
            State = state;
        }

        internal bool IsRuntime { get; }
        internal RuntimeAbiState State { get; }

        public bool Equals(RuntimeReference other) => IsRuntime == other.IsRuntime && State.Equals(other.State);
        public override bool Equals(object? obj) => obj is RuntimeReference other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(IsRuntime, State);
    }

    private readonly struct RuntimeAbiState : IEquatable<RuntimeAbiState>
    {
        internal static readonly RuntimeAbiState Missing = new RuntimeAbiState(-1);

        internal RuntimeAbiState(int version, int rmf2Version = -1, int typedSlotBindingsVersion = -1)
        {
            Version = version;
            Rmf2Version = rmf2Version;
            TypedSlotBindingsVersion = typedSlotBindingsVersion;
        }
        internal int Rmf2Version { get; }

        internal int Version { get; }
        // Additive capability, not part of the ABI gate: a runtime without it still gets the encoded surface.
        internal int TypedSlotBindingsVersion { get; }
        internal bool IsMissing => Version < 0;
        internal bool IsCompatible => Version == 1 && Rmf2Version == 2;
        internal bool SupportsReadableSurface => TypedSlotBindingsVersion >= 1;

        public bool Equals(RuntimeAbiState other) => Version == other.Version && Rmf2Version == other.Rmf2Version &&
            TypedSlotBindingsVersion == other.TypedSlotBindingsVersion;
        public override bool Equals(object? obj) => obj is RuntimeAbiState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Version, Rmf2Version, TypedSlotBindingsVersion);
    }
}
