using System;
using System.Collections.Generic;
using System.Linq;

namespace Runic.Translations.Compiler.Generation;

// Typed generated-code backend over the selected RMF2 project carrier.
internal static class Rmf2CSharpOutputRendererV5
{
    internal static TranslationGeneratedOutput RenderKeys(Rmf2ProjectV5 project)
    {
        GenerationWriter writer = StartFile(project);
        string visibility = Visibility(project), className = GenerationSupport.CSharpIdentifier(project.ClassName);
        writer.Line("/// <summary>Stable keys for the " + GenerationSupport.XmlDocumentation(project.Id) + " translation catalog.</summary>");
        writer.Line(visibility + " static partial class " + className + "Keys");
        writer.Line("{"); writer.Indent();
        foreach (Rmf2MessageContractV5 message in Canonical(project))
        {
            writer.Line("/// <summary>Key for <c>" + GenerationSupport.XmlDocumentation(string.Join(".", message.Path)) + "</c>.</summary>");
            writer.Line("public static global::Runic.Translations.TranslationKey " + Member(message) + " { get; } = new global::Runic.Translations.TranslationKey(" +
                GenerationSupport.CSharpString(project.Id) + ", " + message.Id + ", " + GenerationSupport.CSharpString(message.Key) + ");");
        }
        writer.Unindent(); writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpKeys, project.ClassName + ".Keys.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderAccessors(Rmf2ProjectV5 project)
    {
        GenerationWriter writer = StartFile(project);
        string visibility = Visibility(project), className = GenerationSupport.CSharpIdentifier(project.ClassName);
        writer.Line("/// <summary>Strongly typed RMF2 accessors for the " + GenerationSupport.XmlDocumentation(project.Id) + " translation catalog.</summary>");
        writer.Line(visibility + " sealed partial class " + className);
        writer.Line("{"); writer.Indent();
        writer.Line("private readonly global::Runic.Translations.ITranslationManager __translationManager;"); writer.Blank();
        writer.Line("/// <summary>Creates accessors over a locale manager.</summary>");
        writer.Line("public " + className + "(global::Runic.Translations.ITranslationManager manager)");
        writer.Line("{"); writer.Indent();
        writer.Line("__translationManager = manager ?? throw new global::System.ArgumentNullException(nameof(manager));");
        writer.Unindent(); writer.Line("}");
        foreach (Rmf2MessageContractV5 message in Canonical(project)) WriteAccessor(writer, project, message);
        writer.Unindent(); writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpAccessors, project.ClassName + ".Accessors.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderCatalogData(Rmf2ProjectV5 project)
    {
        GenerationWriter writer = StartFile(project);
        string className = GenerationSupport.CSharpIdentifier(project.ClassName) + "CatalogData";
        Rmf2V5DefinitionTable table = Rmf2V5DefinitionTable.Create(project);
        writer.Line("internal static class " + className);
        writer.Line("{"); writer.Indent();
        writer.Line("internal const int GeneratedRuntimeAbiVersion = 1;");
        writer.Line("internal const int RequiredRmf2RuntimeAbiVersion = 2;"); writer.Blank();
        writer.Line("internal static global::Runic.Translations.CompiledTranslationCatalog CreateDefinition()");
        writer.Line("{"); writer.Indent();
        writer.Line("return new global::Runic.Translations.CompiledTranslationCatalog("); writer.Indent();
        writer.Line(GenerationSupport.CSharpString(project.Id) + ",");
        writer.Line(GenerationSupport.CSharpString(project.DefaultLocale) + ",");
        WriteDefinitions(writer, table, ",");
        WriteLocales(writer, project, table, ",");
        writer.Line("global::Runic.Translations.UnsupportedLocalePolicy." + project.UnsupportedLocale + ",");
        writer.Line("global::Runic.Translations.MissingTranslationPolicy." + project.MissingKey + ");");
        writer.Unindent(); writer.Unindent(); writer.Line("}"); writer.Blank();
        WritePackContractFactory(writer, project, table);
        writer.Unindent(); writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpCatalogData, project.ClassName + ".CatalogData.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderRegistration(Rmf2ProjectV5 project)
    {
        GenerationWriter writer = StartFile(project);
        string visibility = Visibility(project), className = GenerationSupport.CSharpIdentifier(project.ClassName);
        writer.Line("/// <summary>Reflection-free registration for the " + GenerationSupport.XmlDocumentation(project.Id) + " catalog.</summary>");
        writer.Line(visibility + " static class " + className + "Catalog");
        writer.Line("{"); writer.Indent();
        writer.Line("public const string CatalogId = " + GenerationSupport.CSharpString(project.Id) + ";");
        writer.Line("public const string DefaultLocale = " + GenerationSupport.CSharpString(project.DefaultLocale) + ";");
        writer.Line("public const string ContractFingerprint = " + GenerationSupport.CSharpString(project.CallerFingerprint) + ";");
        writer.Line("public const string SourceHash = " + GenerationSupport.CSharpString(project.SourceHash) + ";");
        writer.Line("public const string Rmf2MarkupContract = " + GenerationSupport.CSharpString(project.MarkupContract) + ";");
        writer.Line("public const string Rmf2Profile = " + GenerationSupport.CSharpString(Rmf2ProjectV5.Profile) + ";");
        writer.Line("public const int RuntimeAbiVersion = 1;");
        writer.Line("public const int Rmf2RuntimeAbiVersion = 2;");
        writer.Line("public const int MessageGrammarVersion = 5;");
        writer.Line("public const int GeneratedNameVersion = 1;");
        writer.Line("public const int GeneratorVersion = 1;"); writer.Blank();
        writer.Line("public static global::Runic.Translations.ITranslationProvider CreateProvider("); writer.Indent();
        writer.Line("global::Runic.Translations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::Runic.Translations.ITranslationSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::Runic.Translations.TranslationOptions? options = null)"); writer.Unindent();
        writer.Line("{"); writer.Indent();
        writer.Line("if (global::Runic.Translations.TranslationsCompatibility.RuntimeAbiVersion != 1)"); writer.Indent();
        writer.Line("throw new global::System.InvalidOperationException(\"RTR0024: Generated translation code is incompatible with the referenced runtime ABI.\");"); writer.Unindent();
        writer.Line("global::Runic.Translations.TranslationsCompatibility.EnsureRmf2RuntimeAbi(2);");
        writer.Line("return new global::Runic.Translations.CompiledTranslationProvider(" + className + "CatalogData.CreateDefinition().WithOptions(options), valueFormatter, snapshotFactory);");
        writer.Unindent(); writer.Line("}"); writer.Blank();
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::Runic.Translations.ITranslationManager> CreateManagerAsync("); writer.Indent();
        writer.Line("string? initialLocale = null,");
        writer.Line("global::Runic.Translations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::Runic.Translations.ITranslationSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default,");
        writer.Line("global::Runic.Translations.TranslationOptions? options = null)"); writer.Unindent();
        writer.Line("{"); writer.Indent();
        writer.Line("global::Runic.Translations.ITranslationProvider provider = CreateProvider(valueFormatter, snapshotFactory, options);");
        writer.Line("global::Runic.Translations.ITranslationSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::Runic.Translations.TranslationManager(provider, snapshot);");
        writer.Unindent(); writer.Line("}"); writer.Blank();
        writer.Line("public static global::Runic.Translations.TranslationPackContract CreateExternalPackContract(string locale) => " + className + "CatalogData.CreateExternalPackContract(locale);"); writer.Blank();
        writer.Line("public static global::Runic.Translations.ITranslationProvider CreateExternalProvider("); writer.Indent();
        writer.Line("global::Runic.Translations.IExternalTranslationSource externalSource,");
        writer.Line("global::Runic.Translations.TranslationOptions? options = null,");
        writer.Line("global::Runic.Translations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::Runic.Translations.TranslationPackLimits? limits = null,");
        writer.Line("global::Runic.Translations.TranslationPackIntegrityVerifier? integrityVerifier = null)"); writer.Unindent();
        writer.Line("{"); writer.Indent();
        writer.Line("global::Runic.Translations.TranslationsCompatibility.EnsureRmf2RuntimeAbi(2);");
        writer.Line("var factory = new global::Runic.Translations.ExternalTranslationSnapshotFactory(externalSource, CatalogId, ContractFingerprint, CreateExternalPackContract, limits, integrityVerifier);");
        writer.Line("return CreateProvider(valueFormatter, factory, options);");
        writer.Unindent(); writer.Line("}"); writer.Blank();
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::Runic.Translations.ITranslationManager> CreateExternalManagerAsync("); writer.Indent();
        writer.Line("global::Runic.Translations.IExternalTranslationSource externalSource,");
        writer.Line("string? initialLocale = null,");
        writer.Line("global::Runic.Translations.TranslationOptions? options = null,");
        writer.Line("global::Runic.Translations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::Runic.Translations.TranslationPackLimits? limits = null,");
        writer.Line("global::Runic.Translations.TranslationPackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)"); writer.Unindent();
        writer.Line("{"); writer.Indent();
        writer.Line("global::Runic.Translations.ITranslationProvider provider = CreateExternalProvider(externalSource, options, valueFormatter, limits, integrityVerifier);");
        writer.Line("global::Runic.Translations.ITranslationSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::Runic.Translations.TranslationManager(provider, snapshot);");
        writer.Unindent(); writer.Line("}"); writer.Blank();
        writer.Line("public static global::System.Threading.Tasks.ValueTask<global::Runic.Translations.VerifiedExternalTranslationPack?> LoadExternalPackAsync("); writer.Indent();
        writer.Line("global::Runic.Translations.IExternalTranslationSource source, string locale,");
        writer.Line("global::Runic.Translations.TranslationPackLimits? limits = null,");
        writer.Line("global::Runic.Translations.TranslationPackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)"); writer.Unindent();
        writer.Line("{"); writer.Indent();
        writer.Line("global::Runic.Translations.TranslationsCompatibility.EnsureRmf2RuntimeAbi(2);");
        writer.Line("return global::Runic.Translations.TranslationPackLoader.LoadAsync(source, CreateExternalPackContract(locale), limits, integrityVerifier, cancellationToken);");
        writer.Unindent(); writer.Line("}");
        writer.Unindent(); writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpRegistration, project.ClassName + ".Registration.g.cs", writer);
    }

    private static void WriteAccessor(GenerationWriter writer, Rmf2ProjectV5 project, Rmf2MessageContractV5 message)
    {
        string member = Member(message), result = message.Structured ? "global::Runic.Translations.LocalizedTextContent" : "string";
        string format = message.Structured ? "FormatContent" : "Format";
        writer.Blank();
        writer.Line("/// <summary>Formats <c>" + GenerationSupport.XmlDocumentation(string.Join(".", message.Path)) + "</c>.</summary>");
        if (message.Inputs.Count == 0)
            writer.Line("public " + result + " " + member + " => __translationManager.Current." + format + "(" + Key(project, message) + ", global::System.ReadOnlySpan<global::Runic.Translations.TextArgument>.Empty);");
        else
        {
            var parameters = new List<string>(message.Inputs.Count);
            foreach (Rmf2InputV5 input in message.Inputs) parameters.Add(ParameterType(input.Type) + " " + Rmf2GeneratedNamesV1.Identifier(input.Name));
            writer.Line("public " + result + " " + member + "(" + string.Join(", ", parameters) + ")");
            writer.Line("{"); writer.Indent();
            writer.Line("return __translationManager.Current." + format + "(" + Key(project, message) + ", new global::Runic.Translations.TextArgument[]");
            writer.Line("{"); writer.Indent();
            foreach (Rmf2InputV5 input in message.Inputs)
            {
                string parameter = Rmf2GeneratedNamesV1.Identifier(input.Name);
                writer.Line("global::Runic.Translations.TextArgument.CreateRmf2(" + GenerationSupport.CSharpString(input.Name) + ", new global::Runic.Translations.TextArgument(\"_\", " + parameter + ")),");
            }
            writer.Unindent(); writer.Line("});");
            writer.Unindent(); writer.Line("}");
        }
        // Always retain a span-shaped escape hatch. It keeps the generated API
        // usable if a future C# surface cannot faithfully model a caller name.
        writer.Line("public " + result + " r_args_" + member + "(global::System.ReadOnlySpan<global::Runic.Translations.TextArgument> arguments) => __translationManager.Current." + format + "(" + Key(project, message) + ", arguments);");
    }

    private static void WriteDefinitions(GenerationWriter writer, Rmf2V5DefinitionTable table, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledTranslationDefinition[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2V5Definition definition in table.Definitions)
        {
            writer.Line("global::Runic.Translations.CompiledTranslationDefinition.FromRmf2Inputs(" + GenerationSupport.CSharpString(definition.Contract.Key) + ","); writer.Indent();
            WriteInputs(writer, definition.Contract.Inputs, ", isCanonical: " + (definition.Canonical ? "true" : "false") + "),"); writer.Unindent();
        }
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteLocales(GenerationWriter writer, Rmf2ProjectV5 project, Rmf2V5DefinitionTable table, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledTranslationLocale[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2LocaleV5 locale in project.Locales.OrderBy(item => item.Tag, StringComparer.Ordinal))
        {
            writer.Line("new global::Runic.Translations.CompiledTranslationLocale(" + GenerationSupport.CSharpString(locale.Tag) + ", " +
                (locale.FallbackTag is null ? "null" : GenerationSupport.CSharpString(locale.FallbackTag)) + ", new global::Runic.Translations.CompiledTranslationValue[]");
            writer.Line("{"); writer.Indent();
            foreach (Rmf2TranslationV5 resource in locale.DirectResources.OrderBy(item => table.Id(item.Key)))
            {
                Rmf2MessageContractV5 contract = table.Contract(resource.Key);
                writer.Line("new global::Runic.Translations.CompiledTranslationValue(" + table.Id(resource.Key) + ", \"\", global::Runic.Translations.CompiledTextMessage.FromRmf2("); writer.Indent();
                WriteMessage(writer, resource.Message with { Inputs = contract.Inputs }, resource.ContentLocale, "))),"); writer.Unindent();
            }
            writer.Unindent(); writer.Line("}),");
        }
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteMessage(GenerationWriter writer, Rmf2MessageV5 message, string contentLocale, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Message("); writer.Indent();
        WriteInputs(writer, message.Inputs, ",");
        WriteDeclarations(writer, message.Declarations, ",");
        WriteSelectors(writer, message.Selectors, ",");
        WriteVariants(writer, message.Variants, ",");
        writer.Line(GenerationSupport.CSharpString(contentLocale) + suffix); writer.Unindent();
    }

    private static void WriteInputs(GenerationWriter writer, IReadOnlyList<Rmf2InputV5> inputs, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Input[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2InputV5 input in inputs)
            writer.Line("new global::Runic.Translations.CompiledRmf2Input(" + GenerationSupport.CSharpString(input.Name) + ", global::Runic.Translations.TextArgumentType." + Type(input.Type) + "),");
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteDeclarations(GenerationWriter writer, IReadOnlyList<Rmf2DeclarationV5> declarations, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Declaration[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2DeclarationV5 declaration in declarations)
        {
            writer.Line("new global::Runic.Translations.CompiledRmf2Declaration(" + GenerationSupport.CSharpString(declaration.Kind) + ", " + GenerationSupport.CSharpString(declaration.Name) + ","); writer.Indent();
            WriteExpression(writer, declaration.Expression, ")),"); writer.Unindent();
        }
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteSelectors(GenerationWriter writer, IReadOnlyList<Rmf2SelectorV5> selectors, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Selector[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2SelectorV5 selector in selectors)
            writer.Line("new global::Runic.Translations.CompiledRmf2Selector(" + Value(selector.Value) + ", global::Runic.Translations.TextArgumentType." + Type(selector.Type) + ", " + GenerationSupport.CSharpString(selector.Function) + "),");
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteVariants(GenerationWriter writer, IReadOnlyList<Rmf2VariantV5> variants, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Variant[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2VariantV5 variant in variants)
        {
            writer.Line("new global::Runic.Translations.CompiledRmf2Variant("); writer.Indent();
            writer.Line("new global::Runic.Translations.CompiledRmf2Key[]"); writer.Line("{"); writer.Indent();
            foreach (Rmf2KeyV5 key in variant.Keys)
                writer.Line(key.Kind == "wildcard" ? "new global::Runic.Translations.CompiledRmf2Key()," : "new global::Runic.Translations.CompiledRmf2Key(" + GenerationSupport.CSharpString(key.Value!) + ", " + (key.Canonical is null ? "null" : GenerationSupport.CSharpString(key.Canonical)) + "),");
            writer.Unindent(); writer.Line("},");
            writer.Line("new global::Runic.Translations.CompiledRmf2Node[]"); writer.Line("{"); writer.Indent();
            foreach (Rmf2NodeV5 node in variant.Nodes) WriteNode(writer, node);
            writer.Unindent(); writer.Line("}),"); writer.Unindent();
        }
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteNode(GenerationWriter writer, Rmf2NodeV5 node)
    {
        switch (node)
        {
            case Rmf2TextV5 text:
                writer.Line("new global::Runic.Translations.CompiledRmf2Node(" + GenerationSupport.CSharpString(text.Value) + "),");
                break;
            case Rmf2ExpressionNodeV5 expression:
                writer.Line("new global::Runic.Translations.CompiledRmf2Node("); writer.Indent();
                WriteExpression(writer, expression.Expression, ")),"); writer.Unindent();
                break;
            case Rmf2MarkupV5 markup:
                writer.Line("new global::Runic.Translations.CompiledRmf2Node(" + GenerationSupport.CSharpString(markup.Name) + ", " + GenerationSupport.CSharpString(markup.MarkupKind) + ","); writer.Indent();
                WriteOptions(writer, markup.Options, ",");
                WriteAnnotations(writer, markup.Annotations, "),"); writer.Unindent();
                break;
            default: throw new InvalidOperationException("Unknown v5 node.");
        }
    }

    private static void WriteExpression(GenerationWriter writer, Rmf2ExpressionV5 expression, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Expression("); writer.Indent();
        writer.Line(Value(expression.Operand) + ",");
        writer.Line("global::Runic.Translations.TextArgumentType." + Type(expression.ValueType) + ",");
        writer.Line((expression.Function is null ? "null" : GenerationSupport.CSharpString(expression.Function)) + ",");
        WriteOptions(writer, expression.Options, ",");
        WriteAnnotations(writer, expression.Annotations, suffix); writer.Unindent();
    }

    private static void WriteOptions(GenerationWriter writer, IReadOnlyList<Rmf2OptionV5> options, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Option[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2OptionV5 option in options)
            writer.Line("new global::Runic.Translations.CompiledRmf2Option(" + GenerationSupport.CSharpString(option.Name) + ", " + Value(option.Value) + "),");
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WriteAnnotations(GenerationWriter writer, IReadOnlyList<Rmf2AnnotationV5> annotations, string suffix)
    {
        writer.Line("new global::Runic.Translations.CompiledRmf2Annotation[]"); writer.Line("{"); writer.Indent();
        foreach (Rmf2AnnotationV5 annotation in annotations)
            writer.Line("new global::Runic.Translations.CompiledRmf2Annotation(" + GenerationSupport.CSharpString(annotation.Name) + ", " + (annotation.Value is null ? "null" : Value(annotation.Value)) + "),");
        writer.Unindent(); writer.Line("}" + suffix);
    }

    private static void WritePackContractFactory(GenerationWriter writer, Rmf2ProjectV5 project, Rmf2V5DefinitionTable table)
    {
        writer.Line("internal static global::Runic.Translations.TranslationPackContract CreateExternalPackContract(string locale)");
        writer.Line("{"); writer.Indent();
        writer.Line("if (locale is null) throw new global::System.ArgumentNullException(nameof(locale));");
        foreach (Rmf2LocaleV5 locale in project.Locales.OrderBy(item => item.Tag, StringComparer.Ordinal))
        {
            writer.Line("if (global::System.String.Equals(locale, " + GenerationSupport.CSharpString(locale.Tag) + ", global::System.StringComparison.Ordinal))");
            writer.Line("{"); writer.Indent();
            var resolved = locale.ResolvedResources.Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            writer.Line("return global::Runic.Translations.TranslationPackContract.CreateRmf2V5("); writer.Indent();
            writer.Line(GenerationSupport.CSharpString(project.Id) + ", " + GenerationSupport.CSharpString(locale.Tag) + ", " + GenerationSupport.CSharpString(project.CallerFingerprint) + ",");
            writer.Line("new global::Runic.Translations.TranslationPackMessageContract[]"); writer.Line("{"); writer.Indent();
            foreach (Rmf2V5Definition definition in table.Definitions.Where(item => item.Canonical || resolved.Contains(item.Contract.Key)).OrderBy(item => item.Contract.Key, StringComparer.Ordinal))
            {
                writer.Line("global::Runic.Translations.TranslationPackMessageContract.FromRmf2Inputs("); writer.Indent();
                writer.Line("new global::Runic.Translations.TranslationKey(" + GenerationSupport.CSharpString(project.Id) + ", " + definition.Id + ", " + GenerationSupport.CSharpString(definition.Contract.Key) + "),");
                WriteInputs(writer, definition.Contract.Inputs, "),"); writer.Unindent();
            }
            writer.Unindent(); writer.Line("},");
            writer.Line(GenerationSupport.CSharpString(project.MarkupContract) + ");"); writer.Unindent();
            writer.Unindent(); writer.Line("}");
        }
        writer.Line("throw new global::System.ArgumentException(\"The locale is not declared by this generated catalog.\", nameof(locale));");
        writer.Unindent(); writer.Line("}");
    }

    private static string Value(Rmf2ValueV5 value) => "new global::Runic.Translations.CompiledRmf2Value(" + GenerationSupport.CSharpString(value.Kind) + ", " + GenerationSupport.CSharpString(value.Value) + ", " + (value.Canonical is null ? "null" : GenerationSupport.CSharpString(value.Canonical)) + ")";
    private static string Type(string type) => type switch { "string" => "String", "int64" => "Int", "decimal" => "Number", "boolean" => "Bool", "date" => "Date", "time" => "Time", "datetime" => "DateTime", "guid" => "Guid", _ => throw new InvalidOperationException("Unknown RMF2 carrier '" + type + "'.") };
    private static string ParameterType(string type) => type switch { "string" => "string", "int64" => "long", "decimal" => "decimal", "boolean" => "bool", "date" => "global::System.DateOnly", "time" => "global::System.TimeOnly", "datetime" => "global::System.DateTimeOffset", "guid" => "global::System.Guid", _ => throw new InvalidOperationException("Unknown RMF2 carrier '" + type + "'.") };
    private static string Member(Rmf2MessageContractV5 message) => Rmf2GeneratedNamesV1.Path(message.Path);
    private static string Key(Rmf2ProjectV5 project, Rmf2MessageContractV5 message) => GenerationSupport.CSharpIdentifier(project.ClassName) + "Keys." + Member(message);
    private static Rmf2MessageContractV5[] Canonical(Rmf2ProjectV5 project) => project.CanonicalMessages.OrderBy(item => item.Id).ToArray();
    private static string Visibility(Rmf2ProjectV5 project) => project.Visibility == TranslationVisibility.Public ? "public" : "internal";
    private static GenerationWriter StartFile(Rmf2ProjectV5 project) { var writer = new GenerationWriter(); writer.Line("// <auto-generated />"); writer.Line("#nullable enable"); writer.Blank(); writer.Line("namespace " + GenerationSupport.CSharpNamespace(project.CodeNamespace) + ";"); writer.Blank(); return writer; }
    private static TranslationGeneratedOutput Output(TranslationGeneratedOutputKind kind, string path, GenerationWriter writer) => new(kind, path, "text/x-csharp", writer.ToString());

}
