using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
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
    private const string PseudoLocalesProperty = "build_property.RunicTranslationsPseudoLocales";
    private const string RuntimeAssemblyName = "Runic.Translations";
    private const int RequiredRmf2Version = Rmf2ProjectV5.RuntimeAbiVersion;
    private const string CompatibilityNamespace = "Runic.Translations.Generated";
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

        // The link and its diagnostics are computed once per change to the translation sources,
        // project file or runtime ABI. Source rendering and XAML validation are separate outputs,
        // so a XAML edit revalidates only that file and never relinks or re-renders sources.
        // runic.json pseudoLocales are generated in development builds only: Runic.Translations.Build sets the
        // property for the Debug configuration.
        IncrementalValueProvider<bool> pseudoLocales = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => provider.GlobalOptions.TryGetValue(PseudoLocalesProperty, out string? value) &&
                string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            .WithTrackingName("TranslationPseudoLocales");
        IncrementalValueProvider<LinkResult> link = units.Collect().Combine(projects).WithTrackingName("TranslationCompilation").Combine(runtimeAbi).Combine(pseudoLocales)
            .Select(static (pair, cancellationToken) => Link(pair.Left.Left.Left, pair.Left.Left.Right, pair.Left.Right, pair.Right, cancellationToken))
            .WithTrackingName("TranslationLink");
        // runic.json "overrides": each library catalog its package packs (runic/<catalog>/) is compiled with the project's
        // override sources in a step of its own, so editing the application's messages never recompiles library catalogs.
        IncrementalValueProvider<OverlayResult> overlays = projects
            .Combine(inputs.Where(static input => input.Kind is InputKind.PackedProject or InputKind.PackedMf2 or InputKind.OverrideMf2).Collect())
            .Combine(runtimeAbi)
            .Select(static (pair, cancellationToken) => LinkOverlays(pair.Left.Left, pair.Left.Right, pair.Right, cancellationToken))
            .WithTrackingName("TranslationOverlays");
        context.RegisterSourceOutput(link.Combine(overlays).WithTrackingName("TranslationEmit"),
            static (productionContext, pair) => Emit(productionContext, pair.Left, pair.Right));

        // Catalogs of referenced projects, linked only to check XAML against: their own build emits and reports them.
        IncrementalValuesProvider<SourceUnit> referencedUnits = inputs
            .Where(static input => input.Kind == InputKind.ReferencedMf2)
            .Select(static (input, cancellationToken) => SourceUnit.Create(input, cancellationToken))
            .WithTrackingName("TranslationReferencedSourceUnits");
        IncrementalValueProvider<ImmutableArray<XamlCatalog>> referencedCatalogs = referencedUnits.Collect()
            .Combine(inputs.Where(static input => input.Kind == InputKind.ReferencedProject).Collect())
            .Combine(runtimeAbi)
            .Select(static (pair, cancellationToken) => LinkReferenced(pair.Left.Left, pair.Left.Right, pair.Right, cancellationToken))
            .WithTrackingName("TranslationReferencedCatalogs");

        IncrementalValueProvider<XamlCatalogs?> xamlCatalog = link
            .Select(static (result, _) => result.XamlCatalog)
            .Combine(referencedCatalogs)
            .Select(static (pair, _) => XamlCatalogs.Create(pair.Left, pair.Right))
            .WithTrackingName("TranslationXamlCatalog");
        context.RegisterSourceOutput(
            inputs.Where(static input => input.Kind == InputKind.Xaml).Combine(xamlCatalog).WithTrackingName("TranslationXamlValidation"),
            static (productionContext, pair) =>
            {
                if (pair.Right is { } catalog) XamlMessageValidator.Validate(productionContext, pair.Left, catalog);
            });
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
            int version = -1, rmf2Version = -1, typedSlotsVersion = -1, compositionVersion = -1, minimumVersion = -1, minimumRmf2Version = -1;
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
                else if (reader.StringComparer.Equals(field.Name, "CompositionVersion")) compositionVersion = value;
                else if (reader.StringComparer.Equals(field.Name, "MinimumRuntimeAbiVersion")) minimumVersion = value;
                else if (reader.StringComparer.Equals(field.Name, "MinimumRmf2RuntimeAbiVersion")) minimumRmf2Version = value;
            }
            return new RuntimeReference(true, version < 0 ? RuntimeAbiState.Missing
                : new RuntimeAbiState(version, rmf2Version, typedSlotsVersion, compositionVersion, minimumVersion, minimumRmf2Version));
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
            typedSlotsVersion = Constant(compatibility, "TypedSlotBindingsVersion"), compositionVersion = Constant(compatibility, "CompositionVersion"),
            minimumVersion = Constant(compatibility, "MinimumRuntimeAbiVersion"), minimumRmf2Version = Constant(compatibility, "MinimumRmf2RuntimeAbiVersion");
        return new RuntimeReference(true, version < 0 ? RuntimeAbiState.Missing
            : new RuntimeAbiState(version, rmf2Version, typedSlotsVersion, compositionVersion, minimumVersion, minimumRmf2Version));

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
            ? "Referenced Runic.Translations runtime ABI is missing; generated RMF2 code requires runtime ABI version 1 and RMF2 ABI version " + RequiredRmf2Version + "."
            : !state.SupportsBase
                ? "Referenced Runic.Translations runtime ABI version " + state.Version + " is incompatible with generated ABI version 1."
                : state.Rmf2Version < 0
                    ? "Referenced Runic.Translations RMF2 runtime ABI is missing; generated RMF2 code requires ABI version " + RequiredRmf2Version + "."
                    : "Referenced Runic.Translations RMF2 runtime ABI version " + state.Rmf2Version + " is incompatible with generated RMF2 ABI version " + RequiredRmf2Version + ".";
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
        else if (string.Equals(kindValue, "Xaml", StringComparison.Ordinal)) kind = InputKind.Xaml;
        else if (string.Equals(kindValue, "XamlCatalogProject", StringComparison.Ordinal)) kind = InputKind.ReferencedProject;
        else if (string.Equals(kindValue, "XamlCatalogSource", StringComparison.Ordinal)) kind = InputKind.ReferencedMf2;
        else if (string.Equals(kindValue, "PackedCatalogProject", StringComparison.Ordinal)) kind = InputKind.PackedProject;
        else if (string.Equals(kindValue, "PackedCatalogSource", StringComparison.Ordinal)) kind = InputKind.PackedMf2;
        else if (string.Equals(kindValue, "OverrideSource", StringComparison.Ordinal)) kind = InputKind.OverrideMf2;
        else return default;

        string? catalog = null;
        // The catalog an override source or a packed project belongs to; other inputs name a XAML catalog.
        if (kind == InputKind.OverrideMf2) options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationOverrideCatalog", out catalog);
        else if (kind == InputKind.PackedProject) options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationPackedCatalog", out catalog);
        else options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationCatalog", out catalog);
        options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationDefaultCatalog", out string? defaultCatalog);
        // A referenced catalog source names its project declaration, so several referenced catalogs link separately.
        string? owner = kind is InputKind.ReferencedMf2 or InputKind.PackedMf2 && options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationProject", out string? ownerPath) &&
            !string.IsNullOrWhiteSpace(ownerPath) ? NormalizePath(ownerPath, optionsProvider.GlobalOptions) : null;
        // A packed catalog names the package that ships it (runic.json "overrides" from).
        if (kind == InputKind.PackedProject && options.TryGetValue("build_metadata.AdditionalFiles.RunicTranslationPackage", out string? package))
            defaultCatalog = package;
        SourceText? sourceText = additionalText.GetText(cancellationToken);
        string path = NormalizePath(additionalText.Path, optionsProvider.GlobalOptions);
        return sourceText is null
            ? new GeneratorInput(kind, path, null, catalog, defaultCatalog, owner)
            : new GeneratorInput(kind, path, sourceText.ToString(), catalog, defaultCatalog, owner);
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

    // Links the catalog and collects that step's diagnostics. It runs in a cached Select step;
    // the outputs only report, render or validate.
    private static LinkResult Link(ImmutableArray<SourceUnit> units, ImmutableArray<GeneratorInput> projectInputs,
        RuntimeAbiState runtimeAbi, bool pseudoLocales, CancellationToken cancellationToken)
    {
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
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
            diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.UnreadableSource, Location.Create(input.Path, default, default), "Source text could not be read."));

        if (projects.Count == 0 && compiled.Count == 0) return new LinkResult(diagnostics.ToImmutable(), null, false);
        if (projects.Count != 1)
        {
            diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.DuplicateInputs, Location.None,
                "Exactly one Runic translation project must be supplied."));
            return new LinkResult(diagnostics.ToImmutable(), null, false);
        }
        if (!runtimeAbi.IsCompatible)
        {
            diagnostics.Add(CreateAbiDiagnostic(runtimeAbi));
            return new LinkResult(diagnostics.ToImmutable(), null, false);
        }

        GeneratorInput selected = projects[0];
        sourceTexts[selected.Path] = SourceText.From(selected.Text!, StrictUtf8);
        var project = new TranslationSource(selected.Path, StrictUtf8.GetBytes(selected.Text!));
        Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(project, compiled, CompilerOptions, pseudoLocales, cancellationToken);
        bool hasErrors = false;
        for (int index = 0; index < compilation.Diagnostics.Count; index++)
        {
            TranslationDiagnostic diagnostic = compilation.Diagnostics[index];
            diagnostics.Add(CreateDiagnostic(diagnostic, sourceTexts));
            if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) hasErrors = true;
        }
        if (hasErrors || compilation.Project is null) return new LinkResult(diagnostics.ToImmutable(), null, false);

        Rmf2ProjectV5 linked = compilation.Project;
        if (!Rmf2ProjectV5EmissionEligibility.CanEmit(linked))
        {
            diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.Get(Rmf2ProjectV5EmissionEligibility.DiagnosticId), Location.None,
                Rmf2ProjectV5EmissionEligibility.Message));
            return new LinkResult(diagnostics.ToImmutable(), null, false);
        }
        return new LinkResult(diagnostics.ToImmutable(), linked, runtimeAbi.SupportsReadableSurface, runtimeAbi.SupportsComposition,
            runtimeAbi.SupportsRuntimeAbiCall);
    }

    // Links each referenced project's catalog for XAML checks only. The referenced project's own
    // build reports its diagnostics and emits its code, so a catalog that does not link is left out.
    private static ImmutableArray<XamlCatalog> LinkReferenced(ImmutableArray<SourceUnit> units, ImmutableArray<GeneratorInput> projects,
        RuntimeAbiState runtimeAbi, CancellationToken cancellationToken)
    {
        if (projects.IsEmpty || !runtimeAbi.IsCompatible || !runtimeAbi.SupportsReadableSurface) return ImmutableArray<XamlCatalog>.Empty;
        var catalogs = ImmutableArray.CreateBuilder<XamlCatalog>();
        foreach (GeneratorInput project in projects.OrderBy(static project => project.Path, StringComparer.Ordinal))
        {
            if (project.Text is null) continue;
            var compiled = new List<Rmf2SourceUnitV5>();
            foreach (SourceUnit unit in units.OrderBy(static unit => unit.Input.Path, StringComparer.Ordinal))
                if (unit.Compiled is not null && string.Equals(unit.Input.Owner, project.Path, StringComparison.Ordinal)) compiled.Add(unit.Compiled);
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2ProjectV5(
                new TranslationSource(project.Path, StrictUtf8.GetBytes(project.Text)), compiled, CompilerOptions, cancellationToken);
            if (compilation.Project is not { } linked ||
                compilation.Diagnostics.Any(static diagnostic => diagnostic.Severity == TranslationDiagnosticSeverity.Error) ||
                !Rmf2ProjectV5EmissionEligibility.CanEmit(linked) || !Rmf2ReadableNamesV1.SupportsClassName(linked.ClassName))
                continue;
            catalogs.Add(new XamlCatalog(linked));
        }
        return catalogs.ToImmutable();
    }

    // Compiles each library catalog of runic.json "overrides" with the project's override sources. Diagnostics in the
    // override sources are reported as they are; a packed catalog that does not compile is one RTR0104 error, since its
    // own build reports its diagnostics.
    private static OverlayResult LinkOverlays(ImmutableArray<GeneratorInput> projectInputs, ImmutableArray<GeneratorInput> inputs,
        RuntimeAbiState runtimeAbi, CancellationToken cancellationToken)
    {
        if (projectInputs.Length != 1 || projectInputs[0].Text is null) return OverlayResult.Empty;
        var project = new TranslationSource(projectInputs[0].Path, StrictUtf8.GetBytes(projectInputs[0].Text!));
        IReadOnlyList<Rmf2OverrideV5> overrides = TranslationCompiler.ReadOverridesV5(project, CompilerOptions);
        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        var overrideSources = inputs.Where(static input => input.Kind == InputKind.OverrideMf2 && input.Text is not null)
            .OrderBy(static input => input.Path, StringComparer.Ordinal).ToArray();
        // Override sources of a catalog runic.json does not override are not compiled.
        foreach (string catalog in overrideSources.Select(OverrideCatalog).Distinct(StringComparer.Ordinal))
            if (!overrides.Any(item => string.Equals(item.Catalog, catalog, StringComparison.Ordinal)))
                diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.Get("RTR0104"), Location.Create(overrideSources.First(source => OverrideCatalog(source) == catalog).Path, default, default),
                    DiagnosticSeverity.Warning, null, null, "translations/overrides/" + catalog + " is not listed in runic.json \"overrides\"; its sources are ignored."));
        if (overrides.Count == 0) return new OverlayResult(diagnostics.ToImmutable(), ImmutableArray<Rmf2ProjectV5>.Empty, false);
        if (runtimeAbi.IsCompatible && !runtimeAbi.SupportsCompiledOverlays)
        {
            diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.Get("RTR0104"), Location.None, DiagnosticSeverity.Error, null, null,
                "runic.json \"overrides\" needs Runic.Translations 0.9.0 or later, whose TranslationOverlay.FromCompiled loads the compiled overrides; update the Runic.Translations reference."));
            return new OverlayResult(diagnostics.ToImmutable(), ImmutableArray<Rmf2ProjectV5>.Empty, false);
        }
        var compiled = ImmutableArray.CreateBuilder<Rmf2ProjectV5>();
        foreach (Rmf2OverrideV5 item in overrides)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GeneratorInput[] packed = inputs.Where(input => input.Kind == InputKind.PackedProject && input.Text is not null &&
                string.Equals(input.Catalog, item.Catalog, StringComparison.Ordinal)).ToArray();
            GeneratorInput[] fromPackage = packed.Where(input => string.Equals(input.DefaultCatalog, item.From, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (fromPackage.Length != 1)
            {
                string message = packed.Length == 0
                    ? "Catalog '" + item.Catalog + "' to override is not packed by any referenced package; reference '" + item.From +
                      "', and pack it with RunicTranslationsPackCatalog=true (Runic.Translations.Build 0.9.0 or later)."
                    : fromPackage.Length == 0
                        ? "Catalog '" + item.Catalog + "' comes from package '" + string.Join("', '", packed.Select(input => input.DefaultCatalog)) + "', not '" + item.From + "'; correct \"from\" in runic.json."
                        : "Catalog '" + item.Catalog + "' is packed more than once by '" + item.From + "'.";
                diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.Get("RTR0104"), Location.None, DiagnosticSeverity.Error, null, null, message));
                continue;
            }
            GeneratorInput library = fromPackage[0];
            TranslationSource[] librarySources = inputs.Where(input => input.Kind == InputKind.PackedMf2 && input.Text is not null &&
                    string.Equals(input.Owner, library.Path, StringComparison.Ordinal))
                .OrderBy(static input => input.Path, StringComparer.Ordinal)
                .Select(static input => new TranslationSource(input.Path, StrictUtf8.GetBytes(input.Text!))).ToArray();
            GeneratorInput[] sources = overrideSources.Where(source => OverrideCatalog(source) == item.Catalog).ToArray();
            var sourceTexts = sources.ToDictionary(static source => source.Path, static source => SourceText.From(source.Text!, StrictUtf8), StringComparer.Ordinal);
            Rmf2ProjectCompilation compilation = TranslationCompiler.CompileRmf2OverlayV5(
                new TranslationSource(library.Path, StrictUtf8.GetBytes(library.Text!)), librarySources,
                sources.Select(static source => new TranslationSource(source.Path, StrictUtf8.GetBytes(source.Text!))), CompilerOptions, cancellationToken);
            bool failed = false;
            TranslationDiagnostic? libraryError = null;
            foreach (TranslationDiagnostic diagnostic in compilation.Diagnostics)
            {
                if (sourceTexts.ContainsKey(diagnostic.Location.Path)) diagnostics.Add(CreateDiagnostic(diagnostic, sourceTexts));
                else if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) libraryError ??= diagnostic;
                if (diagnostic.Severity == TranslationDiagnosticSeverity.Error) failed = true;
            }
            if (libraryError is not null)
                diagnostics.Add(Diagnostic.Create(TranslationsDiagnostics.Get("RTR0104"), Location.None, DiagnosticSeverity.Error, null, null,
                    "The packed catalog '" + item.Catalog + "' of package '" + item.From + "' does not compile with this Runic.Translations.Build: " +
                    libraryError.Id + " " + libraryError.Message + " Use a Runic.Translations.Build at least as new as the library's."));
            if (!failed && compilation.Project is { } overlay && overlay.OverlayKeys.Count != 0) compiled.Add(overlay);
        }
        return new OverlayResult(diagnostics.ToImmutable(), compiled.ToImmutable(), true);

        static string OverrideCatalog(GeneratorInput input)
        {
            if (!string.IsNullOrEmpty(input.Catalog)) return input.Catalog!;
            string[] segments = input.Path.Split('/');
            return segments.Length >= 2 ? segments[segments.Length - 2] : "";
        }
    }

    private static void Emit(SourceProductionContext context, LinkResult result, OverlayResult overlays)
    {
        foreach (Diagnostic diagnostic in result.Diagnostics) context.ReportDiagnostic(diagnostic);
        foreach (Diagnostic diagnostic in overlays.Diagnostics) context.ReportDiagnostic(diagnostic);
        if (result.Project is not { } linked) return;
        bool readableSurface = result.ReadableSurface, composition = result.Composition, runtimeAbiCall = result.RuntimeAbiCall;

        var outputs = new List<TranslationGeneratedOutput>(5)
        {
            TranslationOutputRenderer.RenderRmf2V5CSharpKeys(linked, readableSurface),
            TranslationOutputRenderer.RenderRmf2V5CSharpAccessors(linked, readableSurface, composition),
            TranslationOutputRenderer.RenderRmf2V5CSharpCatalogData(linked),
            TranslationOutputRenderer.RenderRmf2V5CSharpRegistration(linked, composition, runtimeAbiCall, readableSurface,
                composition && overlays.Declared ? overlays.Overlays : null),
        };
        // The readable surface needs the typed slot types of a runtime that declares
        // TypedSlotBindingsVersion. Without it, keep the encoded files and explain the gap.
        // A class named like a facade member gets no readable file; the compiler already reported RTR0069.
        if (Rmf2ReadableNamesV1.SupportsClassName(linked.ClassName))
        {
            if (readableSurface) outputs.Add(TranslationOutputRenderer.RenderRmf2V5CSharpReadable(linked));
            else context.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.ReadableSurfaceUnsupported, Location.None,
                "The referenced Runic.Translations runtime does not support the readable C# surface; update it. The encoded accessors are still generated."));
        }
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

        DiagnosticSeverity severity = diagnostic.Severity switch
        {
            TranslationDiagnosticSeverity.Information => DiagnosticSeverity.Info,
            TranslationDiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
            _ => DiagnosticSeverity.Error,
        };
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
        Xaml,
        ReferencedProject,
        ReferencedMf2,
        // runic.json "overrides": a library catalog's packed runic.json and sources, and the application's override sources.
        PackedProject,
        PackedMf2,
        OverrideMf2,
    }

    // The library catalogs of runic.json "overrides", each compiled with the project's override sources. Reference equality,
    // as for LinkResult.
    internal sealed class OverlayResult
    {
        internal static readonly OverlayResult Empty = new(ImmutableArray<Diagnostic>.Empty, ImmutableArray<Rmf2ProjectV5>.Empty, false);

        internal OverlayResult(ImmutableArray<Diagnostic> diagnostics, ImmutableArray<Rmf2ProjectV5> overlays, bool declared)
        {
            Diagnostics = diagnostics;
            Overlays = overlays;
            Declared = declared;
        }

        internal ImmutableArray<Diagnostic> Diagnostics { get; }
        internal ImmutableArray<Rmf2ProjectV5> Overlays { get; }
        /// <summary>
        /// Whether runic.json declares overrides and the runtime loads compiled overlays: XCatalog.Overlays is generated then,
        /// with the overlays that compiled, so a failed override reports only its own diagnostics.
        /// </summary>
        internal bool Declared { get; }
    }

    internal readonly struct GeneratorInput : IEquatable<GeneratorInput>
    {
        internal GeneratorInput(InputKind kind, string path, string? text, string? catalog = null, string? defaultCatalog = null, string? owner = null)
        {
            Owner = owner;
            Catalog = catalog;
            DefaultCatalog = defaultCatalog;
            Kind = kind;
            Path = path;
            Text = text;
        }

        internal InputKind Kind { get; }
        internal string Path { get; }
        internal string? Text { get; }
        internal string? Catalog { get; }
        internal string? DefaultCatalog { get; }
        internal string? Owner { get; }

        public bool Equals(GeneratorInput other) =>
            Kind == other.Kind &&
            string.Equals(Catalog, other.Catalog, StringComparison.Ordinal) &&
            string.Equals(DefaultCatalog, other.DefaultCatalog, StringComparison.Ordinal) &&
            string.Equals(Owner, other.Owner, StringComparison.Ordinal) &&
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
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Catalog ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(DefaultCatalog ?? string.Empty);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Owner ?? string.Empty);
                return hash;
            }
        }
    }

    // The linked catalog and the diagnostics of linking it. Reference equality: every rerun of the
    // link (a translation source, project or runtime ABI change) reaches both outputs.
    internal sealed class LinkResult
    {
        internal LinkResult(ImmutableArray<Diagnostic> diagnostics, Rmf2ProjectV5? project, bool readableSurface, bool composition = false,
            bool runtimeAbiCall = false)
        {
            Diagnostics = diagnostics;
            Project = project;
            ReadableSurface = readableSurface;
            Composition = composition;
            RuntimeAbiCall = runtimeAbiCall;
            // XAML keys name readable members. Without the readable surface (an old runtime or a
            // reserved class name, both reported once already) there is nothing to check XAML against.
            XamlCatalog = project is not null && readableSurface && Rmf2ReadableNamesV1.SupportsClassName(project.ClassName)
                ? new XamlCatalog(project)
                : null;
        }

        internal ImmutableArray<Diagnostic> Diagnostics { get; }

        /// <summary>The linked, emittable project; null when linking failed or was not possible.</summary>
        internal Rmf2ProjectV5? Project { get; }
        internal bool ReadableSurface { get; }
        /// <summary>Whether the referenced runtime has the catalog composition surface.</summary>
        internal bool Composition { get; }
        /// <summary>Whether the referenced runtime declares the version rule (MinimumRuntimeAbiVersion, EnsureRuntimeAbi).</summary>
        internal bool RuntimeAbiCall { get; }
        internal XamlCatalog? XamlCatalog { get; }
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

        internal RuntimeAbiState(int version, int rmf2Version = -1, int typedSlotBindingsVersion = -1, int compositionVersion = -1,
            int minimumVersion = -1, int minimumRmf2Version = -1)
        {
            Version = version;
            Rmf2Version = rmf2Version;
            TypedSlotBindingsVersion = typedSlotBindingsVersion;
            CompositionVersion = compositionVersion;
            // A runtime that declares no minimum runs exactly its own ABI, as before the version rule.
            DeclaresMinimumVersion = minimumVersion >= 0;
            MinimumVersion = minimumVersion < 0 ? version : minimumVersion;
            MinimumRmf2Version = minimumRmf2Version < 0 ? rmf2Version : minimumRmf2Version;
        }
        internal int Rmf2Version { get; }

        internal int Version { get; }
        // Additive capability, not part of the ABI gate: a runtime without it still gets the encoded surface.
        internal int TypedSlotBindingsVersion { get; }
        // Additive capability: a runtime without it gets no Registration and no context constructor.
        internal int CompositionVersion { get; }
        internal bool IsMissing => Version < 0;
        internal int MinimumVersion { get; }
        internal int MinimumRmf2Version { get; }
        // Code generated by release N runs on runtime N or later within the major: a later runtime that still runs
        // this generator's ABIs is compatible.
        internal bool SupportsBase => MinimumVersion <= 1 && Version >= 1;
        internal bool IsCompatible => SupportsBase && Rmf2Version >= 0 && MinimumRmf2Version <= RequiredRmf2Version && Rmf2Version >= RequiredRmf2Version;
        internal bool SupportsReadableSurface => TypedSlotBindingsVersion >= 1;
        internal bool SupportsComposition => CompositionVersion >= 1;
        // TranslationOverlay.FromCompiled, which XCatalog.Overlays (runic.json "overrides") calls.
        internal bool SupportsCompiledOverlays => CompositionVersion >= 2;
        internal bool DeclaresMinimumVersion { get; }
        // The runtime that declares MinimumRuntimeAbiVersion also declares EnsureRuntimeAbi, which generated code calls.
        internal bool SupportsRuntimeAbiCall => DeclaresMinimumVersion;

        public bool Equals(RuntimeAbiState other) => Version == other.Version && Rmf2Version == other.Rmf2Version &&
            TypedSlotBindingsVersion == other.TypedSlotBindingsVersion && CompositionVersion == other.CompositionVersion &&
            MinimumVersion == other.MinimumVersion && MinimumRmf2Version == other.MinimumRmf2Version && DeclaresMinimumVersion == other.DeclaresMinimumVersion;
        public override bool Equals(object? obj) => obj is RuntimeAbiState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Version, Rmf2Version, TypedSlotBindingsVersion, CompositionVersion, MinimumVersion, MinimumRmf2Version, DeclaresMinimumVersion);
    }
}
