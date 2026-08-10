using System;
using System.Collections.Generic;

namespace RunicTranslations.Compiler.Generation;

internal static class CSharpOutputRenderer
{
    internal static TranslationGeneratedOutput RenderKeys(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string visibility = Visibility(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName);
        writer.Line("/// <summary>Stable keys for the " + GenerationSupport.XmlDocumentation(catalog.Id) + " translation catalog.</summary>");
        writer.Line(visibility + " static partial class " + className + "Keys");
        writer.Line("{");
        writer.Indent();
        WriteKeyMembers(writer, catalog, GenerationSupport.BuildTree(catalog));
        writer.Unindent();
        writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpKeys, catalog.ClassName + ".Keys.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderAccessors(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string visibility = Visibility(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName);
        ResourceTreeNode root = GenerationSupport.BuildTree(catalog);
        string rootManagerField = ManagerField(root);
        writer.Line("/// <summary>Strongly typed accessors for the " + GenerationSupport.XmlDocumentation(catalog.Id) + " translation catalog.</summary>");
        writer.Line(visibility + " sealed partial class " + className);
        writer.Line("{");
        writer.Indent();
        if (HasDirectResource(root))
        {
            writer.Line("private readonly global::RunicTranslations.ITranslationManager " + rootManagerField + ";");
            writer.Blank();
        }
        writer.Line("/// <summary>Creates accessors over a locale manager.</summary>");
        writer.Line("public " + className + "(global::RunicTranslations.ITranslationManager manager)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (manager is null) throw new global::System.ArgumentNullException(nameof(manager));");
        if (HasDirectResource(root)) writer.Line(rootManagerField + " = manager;");
        foreach (KeyValuePair<string, ResourceTreeNode> child in root.Children)
        {
            if (child.Value.Resource is null)
                writer.Line("this." + GenerationSupport.CSharpIdentifier(child.Key) + " = new " + GenerationSupport.CSharpIdentifier(child.Key) + "Group(manager);");
        }
        writer.Unindent();
        writer.Line("}");
        WriteAccessorMembers(writer, root, catalog.ClassName + "Keys", rootManagerField);
        writer.Unindent();
        writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpAccessors, catalog.ClassName + ".Accessors.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderCatalogData(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName) + "CatalogData";
        GeneratedCatalogTable table = GeneratedCatalogTable.Create(catalog);
        IReadOnlyList<CompiledTextLocale> locales = GenerationSupport.OrderedLocales(catalog.Locales);
        writer.Line("internal static class " + className);
        writer.Line("{");
        writer.Indent();
        writer.Line("internal const int GeneratedRuntimeAbiVersion = 1;");
        writer.Blank();
        writer.Line("internal static global::RunicTranslations.CompiledTranslationCatalog CreateDefinition()");
        writer.Line("{");
        writer.Indent();
        writer.Line("return new global::RunicTranslations.CompiledTranslationCatalog(");
        writer.Indent();
        writer.Line(GenerationSupport.CSharpString(catalog.Id) + ",");
        writer.Line(GenerationSupport.CSharpString(catalog.DefaultLocale) + ",");
        WriteDefinitions(writer, table.Definitions, ",");
        WriteLocales(writer, locales, table, ",");
        writer.Line("global::RunicTranslations.UnsupportedLocalePolicy." + catalog.UnsupportedLocale + ",");
        writer.Line("global::RunicTranslations.MissingTranslationPolicy." + catalog.MissingKey + ");");
        writer.Unindent();
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        WritePackContractFactory(writer, catalog, table, locales);
        writer.Unindent();
        writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpCatalogData, catalog.ClassName + ".CatalogData.g.cs", writer);
    }

    internal static TranslationGeneratedOutput RenderRegistration(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string visibility = Visibility(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName);
        writer.Line("/// <summary>Reflection-free registration for the " + GenerationSupport.XmlDocumentation(catalog.Id) + " catalog.</summary>");
        writer.Line(visibility + " static class " + className + "Catalog");
        writer.Line("{");
        writer.Indent();
        writer.Line("/// <summary>The stable catalog identifier.</summary>");
        writer.Line("public const string CatalogId = " + GenerationSupport.CSharpString(catalog.Id) + ";");
        writer.Line("/// <summary>The canonical default locale.</summary>");
        writer.Line("public const string DefaultLocale = " + GenerationSupport.CSharpString(catalog.DefaultLocale) + ";");
        writer.Line("/// <summary>The locale-independent key and argument fingerprint.</summary>");
        writer.Line("public const string ContractFingerprint = " + GenerationSupport.CSharpString(catalog.Fingerprint) + ";");
        writer.Line("/// <summary>The generated-code/runtime ABI version.</summary>");
        writer.Line("public const int RuntimeAbiVersion = 1;");
        writer.Line("/// <summary>The generator contract version that emitted this source.</summary>");
        writer.Line("public const int GeneratorVersion = 1;");
        writer.Blank();
        writer.Line("/// <summary>Creates a provider over the compiled, immutable catalog definition.</summary>");
        writer.Line("public static global::RunicTranslations.ITranslationProvider CreateProvider(");
        writer.Indent();
        writer.Line("global::RunicTranslations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::RunicTranslations.ITranslationSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::RunicTranslations.TranslationOptions? options = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("if (global::RunicTranslations.TranslationsCompatibility.RuntimeAbiVersion != RuntimeAbiVersion)");
        writer.Indent();
        writer.Line("throw new global::System.InvalidOperationException(\"RTR0024: Generated translation code is incompatible with the referenced runtime ABI.\");");
        writer.Unindent();
        writer.Line("return new global::RunicTranslations.CompiledTranslationProvider(" + className + "CatalogData.CreateDefinition().WithOptions(options), valueFormatter, snapshotFactory);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a manager after asynchronously resolving its initial immutable snapshot.</summary>");
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::RunicTranslations.ITranslationManager> CreateManagerAsync(");
        writer.Indent();
        writer.Line("string? initialLocale = null,");
        writer.Line("global::RunicTranslations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::RunicTranslations.ITranslationSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default,");
        writer.Line("global::RunicTranslations.TranslationOptions? options = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("global::RunicTranslations.ITranslationProvider provider = CreateProvider(valueFormatter, snapshotFactory, options);");
        writer.Line("global::RunicTranslations.ITranslationSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::RunicTranslations.TranslationManager(provider, snapshot);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a provider that composes verified caller-supplied packs over compiled snapshots.</summary>");
        writer.Line("public static global::RunicTranslations.ITranslationProvider CreateExternalProvider(");
        writer.Indent();
        writer.Line("global::RunicTranslations.IExternalTranslationSource externalSource,");
        writer.Line("global::RunicTranslations.TranslationOptions? options = null,");
        writer.Line("global::RunicTranslations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::RunicTranslations.TranslationPackLimits? limits = null,");
        writer.Line("global::RunicTranslations.TranslationPackIntegrityVerifier? integrityVerifier = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("var factory = new global::RunicTranslations.ExternalTranslationSnapshotFactory(externalSource, CatalogId, ContractFingerprint, CreateExternalPackContract, limits, integrityVerifier);");
        writer.Line("return CreateProvider(valueFormatter, factory, options);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a manager whose snapshots compose verified caller-supplied packs.</summary>");
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::RunicTranslations.ITranslationManager> CreateExternalManagerAsync(");
        writer.Indent();
        writer.Line("global::RunicTranslations.IExternalTranslationSource externalSource,");
        writer.Line("string? initialLocale = null,");
        writer.Line("global::RunicTranslations.TranslationOptions? options = null,");
        writer.Line("global::RunicTranslations.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::RunicTranslations.TranslationPackLimits? limits = null,");
        writer.Line("global::RunicTranslations.TranslationPackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("global::RunicTranslations.ITranslationProvider provider = CreateExternalProvider(externalSource, options, valueFormatter, limits, integrityVerifier);");
        writer.Line("global::RunicTranslations.ITranslationSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::RunicTranslations.TranslationManager(provider, snapshot);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates the generated compatibility contract used to verify one declared locale pack.</summary>");
        writer.Line("public static global::RunicTranslations.TranslationPackContract CreateExternalPackContract(string locale)");
        writer.Line("{");
        writer.Indent();
        writer.Line("return " + className + "CatalogData.CreateExternalPackContract(locale);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Requests and fully verifies an optional caller-owned pack for one declared locale.</summary>");
        writer.Line("public static global::System.Threading.Tasks.ValueTask<global::RunicTranslations.VerifiedExternalTranslationPack?> LoadExternalPackAsync(");
        writer.Indent();
        writer.Line("global::RunicTranslations.IExternalTranslationSource source,");
        writer.Line("string locale,");
        writer.Line("global::RunicTranslations.TranslationPackLimits? limits = null,");
        writer.Line("global::RunicTranslations.TranslationPackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("return global::RunicTranslations.TranslationPackLoader.LoadAsync(source, CreateExternalPackContract(locale), limits, integrityVerifier, cancellationToken);");
        writer.Unindent();
        writer.Line("}");
        writer.Unindent();
        writer.Line("}");
        return Output(TranslationGeneratedOutputKind.CSharpRegistration, catalog.ClassName + ".Registration.g.cs", writer);
    }

    private static GenerationWriter StartFile(CompiledTextCatalog catalog)
    {
        var writer = new GenerationWriter();
        writer.Line("// <auto-generated />");
        writer.Line("#nullable enable");
        writer.Blank();
        writer.Line("namespace " + GenerationSupport.CSharpNamespace(catalog.CodeNamespace) + ";");
        writer.Blank();
        return writer;
    }

    private static TranslationGeneratedOutput Output(TranslationGeneratedOutputKind kind, string path, GenerationWriter writer) =>
        new TranslationGeneratedOutput(kind, path, "text/x-csharp", writer.ToString());

    private static string Visibility(CompiledTextCatalog catalog) => catalog.Visibility == TranslationVisibility.Public ? "public" : "internal";

    private static void WriteKeyMembers(GenerationWriter writer, CompiledTextCatalog catalog, ResourceTreeNode node)
    {
        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
        {
            if (child.Value.Resource is not null)
            {
                CompiledTranslation resource = child.Value.Resource;
                WriteResourceDocumentation(writer, resource, false);
                WriteObsolete(writer, resource);
                writer.Line("public static global::RunicTranslations.TranslationKey " + GenerationSupport.CSharpIdentifier(child.Key) + " { get; } = new global::RunicTranslations.TranslationKey(" + GenerationSupport.CSharpString(catalog.Id) + ", " + resource.Id + ", " + GenerationSupport.CSharpString(resource.Key) + ");");
            }
            else
            {
                writer.Line("/// <summary>Keys below <c>" + GenerationSupport.XmlDocumentation(child.Key) + "</c>.</summary>");
                writer.Line("public static class " + GenerationSupport.CSharpIdentifier(child.Key));
                writer.Line("{");
                writer.Indent();
                WriteKeyMembers(writer, catalog, child.Value);
                writer.Unindent();
                writer.Line("}");
            }
        }
    }

    private static void WriteAccessorMembers(GenerationWriter writer, ResourceTreeNode node, string keyPath, string managerField)
    {
        if (node.Children.Count > 0) writer.Blank();
        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
        {
            string identifier = GenerationSupport.CSharpIdentifier(child.Key);
            string childKeyPath = keyPath + "." + identifier;
            if (child.Value.Resource is not null)
                WriteAccessor(writer, child.Value.Resource, identifier, childKeyPath, managerField);
            else
            {
                writer.Line("/// <summary>Accessors below <c>" + GenerationSupport.XmlDocumentation(child.Key) + "</c>.</summary>");
                writer.Line("public " + identifier + "Group " + identifier + " { get; }");
            }
        }

        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
        {
            if (child.Value.Resource is not null) continue;
            string identifier = GenerationSupport.CSharpIdentifier(child.Key);
            string childManagerField = ManagerField(child.Value);
            writer.Blank();
            writer.Line("/// <summary>Accessors below <c>" + GenerationSupport.XmlDocumentation(child.Key) + "</c>.</summary>");
            writer.Line("public sealed class " + identifier + "Group");
            writer.Line("{");
            writer.Indent();
            if (HasDirectResource(child.Value))
            {
                writer.Line("private readonly global::RunicTranslations.ITranslationManager " + childManagerField + ";");
                writer.Blank();
            }
            writer.Line("internal " + identifier + "Group(global::RunicTranslations.ITranslationManager manager)");
            writer.Line("{");
            writer.Indent();
            if (HasDirectResource(child.Value)) writer.Line(childManagerField + " = manager;");
            foreach (KeyValuePair<string, ResourceTreeNode> grandchild in child.Value.Children)
            {
                if (grandchild.Value.Resource is null)
                {
                    string grandchildIdentifier = GenerationSupport.CSharpIdentifier(grandchild.Key);
                    writer.Line("this." + grandchildIdentifier + " = new " + grandchildIdentifier + "Group(manager);");
                }
            }
            writer.Unindent();
            writer.Line("}");
            WriteAccessorMembers(writer, child.Value, keyPath + "." + identifier, childManagerField);
            writer.Unindent();
            writer.Line("}");
        }
    }

    private static void WriteAccessor(GenerationWriter writer, CompiledTranslation resource, string identifier, string keyPath, string managerField)
    {
        WriteResourceDocumentation(writer, resource, true);
        WriteObsolete(writer, resource);
        IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
        if (placeholders.Count == 0)
        {
            writer.Line("public " + (resource.ProducesStructuredContent ? "global::RunicTranslations.LocalizedTextContent" : "string") + " " + identifier + " => this." + managerField + ".Current." +
                (resource.ProducesStructuredContent ? "FormatContent" : "Format") + "(" + keyPath + ", global::System.ReadOnlySpan<global::RunicTranslations.TextArgument>.Empty);");
            return;
        }

        var parameters = new List<string>(placeholders.Count);
        for (int i = 0; i < placeholders.Count; i++)
        {
            CompiledTextPlaceholder placeholder = placeholders[i];
            parameters.Add(CSharpParameterType(placeholder.Type) + " " + GenerationSupport.CSharpIdentifier(placeholder.Name));
        }
        writer.Line("public " + (resource.ProducesStructuredContent ? "global::RunicTranslations.LocalizedTextContent" : "string") + " " + identifier + "(" + string.Join(", ", parameters) + ")");
        writer.Line("{");
        writer.Indent();
        writer.Line("return this." + managerField + ".Current." + (resource.ProducesStructuredContent ? "FormatContent" : "Format") + "(" + keyPath + ", new global::RunicTranslations.TextArgument[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < placeholders.Count; i++)
        {
            CompiledTextPlaceholder placeholder = placeholders[i];
            string argument = "new global::RunicTranslations.TextArgument(" + GenerationSupport.CSharpString(placeholder.Name) + ", " + GenerationSupport.CSharpIdentifier(placeholder.Name);
            if (placeholder.Type != TranslationArgumentType.String)
                argument += ", global::RunicTranslations.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format);
            writer.Line(argument + "),");
        }
        writer.Unindent();
        writer.Line("});");
        writer.Unindent();
        writer.Line("}");
    }

    private static void WriteDefinitions(GenerationWriter writer, IReadOnlyList<GeneratedCatalogDefinition> definitions, string suffix)
    {
        writer.Line("new global::RunicTranslations.CompiledTranslationDefinition[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < definitions.Count; i++)
        {
            GeneratedCatalogDefinition definition = definitions[i];
            CompiledTranslation resource = definition.Resource;
            IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
            writer.Line("new global::RunicTranslations.CompiledTranslationDefinition(" + GenerationSupport.CSharpString(resource.Key) + ", new global::RunicTranslations.TranslationPlaceholderDescriptor[]");
            writer.Line("{");
            writer.Indent();
            for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
            {
                CompiledTextPlaceholder placeholder = placeholders[placeholderIndex];
                writer.Line("new global::RunicTranslations.TranslationPlaceholderDescriptor(" + GenerationSupport.CSharpString(placeholder.Name) + ", global::RunicTranslations.TextArgumentType." + GenerationSupport.ArgumentTypeName(placeholder.Type) + ", global::RunicTranslations.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format) + "),");
            }
            writer.Unindent();
            writer.Line("}, isCanonical: " + (definition.IsCanonical ? "true" : "false") + "),");
        }
        writer.Unindent();
        writer.Line("}" + suffix);
    }

    private static void WriteLocales(GenerationWriter writer, IReadOnlyList<CompiledTextLocale> locales, GeneratedCatalogTable table, string suffix)
    {
        writer.Line("new global::RunicTranslations.CompiledTranslationLocale[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < locales.Count; i++)
        {
            CompiledTextLocale locale = locales[i];
            var resources = new List<CompiledTranslation>(locale.DirectResources.Count);
            for (int resourceIndex = 0; resourceIndex < locale.DirectResources.Count; resourceIndex++) resources.Add(locale.DirectResources[resourceIndex]);
            resources.Sort((left, right) => table.GetId(left.Key).CompareTo(table.GetId(right.Key)));
            writer.Line("new global::RunicTranslations.CompiledTranslationLocale(" + GenerationSupport.CSharpString(locale.Tag) + ", " + (locale.FallbackTag is null ? "null" : GenerationSupport.CSharpString(locale.FallbackTag)) + ", new global::RunicTranslations.CompiledTranslationValue[]");
            writer.Line("{");
            writer.Indent();
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                CompiledTranslation resource = resources[resourceIndex];
                writer.Line("new global::RunicTranslations.CompiledTranslationValue(" + table.GetId(resource.Key) + ", " + GenerationSupport.CSharpString(resource.Pattern) + ",");
                writer.Indent();
                WriteCompiledMessage(writer, resource.Message);
                writer.Unindent();
                writer.Line("),");
            }
            writer.Unindent();
            writer.Line("}),");
        }
        writer.Unindent();
        writer.Line("}" + suffix);
    }

    private static void WriteCompiledMessage(GenerationWriter writer, CompiledMessagePattern message)
    {
        if (!message.IsVariant)
        {
            writer.Line("new global::RunicTranslations.CompiledTextMessage(");
            writer.Indent();
            WriteMessageNodes(writer, message.Nodes, ")");
            writer.Unindent();
            return;
        }

        writer.Line("new global::RunicTranslations.CompiledTextMessage(");
        writer.Indent();
        writer.Line("global::System.Array.Empty<global::RunicTranslations.CompiledTextMessageNode>(),");
        writer.Line("new global::RunicTranslations.CompiledTextMessageSelector[]");
        writer.Line("{");
        writer.Indent();
        for (int index = 0; index < message.Selectors.Count; index++)
        {
            CompiledMessageSelector selector = message.Selectors[index];
            string kind = selector.Function switch
            {
                "plural" => "CardinalPlural",
                "ordinal" => "OrdinalPlural",
                _ => "Literal",
            };
            writer.Line("new global::RunicTranslations.CompiledTextMessageSelector(" +
                GenerationSupport.CSharpString(selector.Name) + ", " + GenerationSupport.CSharpString(selector.Input) +
                ", global::RunicTranslations.CompiledTextMessageSelectorKind." + kind + "),");
        }
        writer.Unindent();
        writer.Line("},");
        writer.Line("new global::RunicTranslations.CompiledTextMessageVariant[]");
        writer.Line("{");
        writer.Indent();
        for (int variantIndex = 0; variantIndex < message.Variants.Count; variantIndex++)
        {
            CompiledMessageVariant variant = message.Variants[variantIndex];
            writer.Line("new global::RunicTranslations.CompiledTextMessageVariant(new string[]");
            writer.Line("{");
            writer.Indent();
            for (int selectorIndex = 0; selectorIndex < message.Selectors.Count; selectorIndex++)
                writer.Line(GenerationSupport.CSharpString(variant.Matches[message.Selectors[selectorIndex].Name]) + ",");
            writer.Unindent();
            writer.Line("},");
            writer.Indent();
            WriteMessageNodes(writer, variant.Pattern.Nodes, "),");
            writer.Unindent();
        }
        writer.Unindent();
        writer.Line("})");
        writer.Unindent();
    }

    private static void WriteMessageNodes(GenerationWriter writer, IReadOnlyList<CompiledMessageNode> nodes, string suffix)
    {
        writer.Line("new global::RunicTranslations.CompiledTextMessageNode[]");
        writer.Line("{");
        writer.Indent();
        for (int index = 0; index < nodes.Count; index++)
            WriteMessageNode(writer, nodes[index]);
        writer.Unindent();
        writer.Line("}" + suffix);
    }

    private static void WriteMessageNode(GenerationWriter writer, CompiledMessageNode node)
    {
        if (node is CompiledMessageText text)
            writer.Line("new global::RunicTranslations.CompiledTextMessageNode(global::RunicTranslations.CompiledTextMessageNodeKind.Text, " + GenerationSupport.CSharpString(text.Value) + "),");
        else if (node is CompiledMessageInput input)
            writer.Line("new global::RunicTranslations.CompiledTextMessageNode(global::RunicTranslations.CompiledTextMessageNodeKind.Input, " + GenerationSupport.CSharpString(input.Name) + "),");
        else if (node is CompiledMessageFormat format)
        {
            string kind = format.Function == "relativeTime" ? "RelativeTime" : "Format";
            string argumentFormat = format.Function == "relativeTime" ? "Plain" : FormatName(format.Format);
            writer.Line("new global::RunicTranslations.CompiledTextMessageNode(global::RunicTranslations.CompiledTextMessageNodeKind." + kind + ", " +
                GenerationSupport.CSharpString(format.Input) + ", global::RunicTranslations.TextArgumentFormat." + argumentFormat + ", " +
                (format.Unit is null ? "null" : GenerationSupport.CSharpString(format.Unit)) + ", " +
                (format.Numeric is null ? "null" : GenerationSupport.CSharpString(format.Numeric)) + "),");
        }
        else if (node is CompiledMessageMarkup markup)
        {
            writer.Line("new global::RunicTranslations.CompiledTextMessageNode(global::RunicTranslations.CompiledTextMessageNodeKind.MarkupStart, " +
                GenerationSupport.CSharpString(markup.Name) + ", attributes: new global::RunicTranslations.CompiledTextMarkupProperty[]");
            writer.Line("{");
            writer.Indent();
            foreach (KeyValuePair<string, string> attribute in markup.Attributes)
            {
                writer.Line("new global::RunicTranslations.CompiledTextMarkupProperty(" + GenerationSupport.CSharpString(attribute.Key) + ", " + GenerationSupport.CSharpString(attribute.Value) + "),");
            }
            writer.Unindent();
            writer.Line("}),");
            for (int index = 0; index < markup.Children.Count; index++) WriteMessageNode(writer, markup.Children[index]);
            writer.Line("new global::RunicTranslations.CompiledTextMessageNode(global::RunicTranslations.CompiledTextMessageNodeKind.MarkupEnd, " + GenerationSupport.CSharpString(markup.Name) + "),");
        }
    }

    private static string FormatName(string format) => format switch
    {
        "none" => "None", "plain" => "Plain", "grouped" => "Grouped",
        "fixed0" => "Fixed0", "fixed1" => "Fixed1", "fixed2" => "Fixed2", "fixed3" => "Fixed3",
        "fixed4" => "Fixed4", "fixed5" => "Fixed5", "fixed6" => "Fixed6",
        "percent0" => "Percent0", "percent1" => "Percent1", "percent2" => "Percent2",
        "percent3" => "Percent3", "percent4" => "Percent4", "lower" => "Lower", "iso" => "Iso",
        "short" => "Short", "medium" => "Medium", "long" => "Long", "d" => "D", "n" => "N",
        _ => throw new InvalidOperationException("Unknown compiled format '" + format + "'."),
    };

    private static void WritePackContractFactory(
        GenerationWriter writer,
        CompiledTextCatalog catalog,
        GeneratedCatalogTable table,
        IReadOnlyList<CompiledTextLocale> locales)
    {
        writer.Line("internal static global::RunicTranslations.TranslationPackContract CreateExternalPackContract(string locale)");
        writer.Line("{");
        writer.Indent();
        writer.Line("if (locale is null) throw new global::System.ArgumentNullException(nameof(locale));");
        for (int i = 0; i < locales.Count; i++)
        {
            CompiledTextLocale locale = locales[i];
            writer.Line("if (global::System.String.Equals(locale, " + GenerationSupport.CSharpString(locale.Tag) + ", global::System.StringComparison.Ordinal))");
            writer.Line("{");
            writer.Indent();
            WritePackContractReturn(writer, catalog, table, locale);
            writer.Unindent();
            writer.Line("}");
        }
        writer.Line("throw new global::System.ArgumentException(\"The locale is not declared by this generated catalog.\", nameof(locale));");
        writer.Unindent();
        writer.Line("}");
    }

    private static void WritePackContractReturn(
        GenerationWriter writer,
        CompiledTextCatalog catalog,
        GeneratedCatalogTable table,
        CompiledTextLocale locale)
    {
        var resolvedNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < locale.ResolvedResources.Count; i++) resolvedNames.Add(locale.ResolvedResources[i].Key);
        var selectedDefinitions = new List<GeneratedCatalogDefinition>();
        for (int i = 0; i < table.Definitions.Count; i++)
        {
            GeneratedCatalogDefinition definition = table.Definitions[i];
            if (definition.IsCanonical || resolvedNames.Contains(definition.Resource.Key)) selectedDefinitions.Add(definition);
        }
        selectedDefinitions.Sort((left, right) => StringComparer.Ordinal.Compare(left.Resource.Key, right.Resource.Key));
        writer.Line("return new global::RunicTranslations.TranslationPackContract(");
        writer.Indent();
        writer.Line(GenerationSupport.CSharpString(catalog.Id) + ",");
        writer.Line(GenerationSupport.CSharpString(locale.Tag) + ",");
        writer.Line(GenerationSupport.CSharpString(catalog.Fingerprint) + ",");
        writer.Line("new global::RunicTranslations.TranslationPackMessageContract[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < selectedDefinitions.Count; i++)
        {
            GeneratedCatalogDefinition definition = selectedDefinitions[i];
            CompiledTranslation resource = definition.Resource;
            IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
            writer.Line("new global::RunicTranslations.TranslationPackMessageContract(");
            writer.Indent();
            writer.Line("new global::RunicTranslations.TranslationKey(" + GenerationSupport.CSharpString(catalog.Id) + ", " + definition.Id + ", " + GenerationSupport.CSharpString(resource.Key) + "),");
            writer.Line("new global::RunicTranslations.TranslationPackArgumentContract[]");
            writer.Line("{");
            writer.Indent();
            for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
            {
                CompiledTextPlaceholder placeholder = placeholders[placeholderIndex];
                writer.Line("new global::RunicTranslations.TranslationPackArgumentContract(" + GenerationSupport.CSharpString(placeholder.Name) + ", global::RunicTranslations.TextArgumentType." + GenerationSupport.ArgumentTypeName(placeholder.Type) + ", global::RunicTranslations.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format) + "),");
            }
            writer.Unindent();
            writer.Line("}),");
            writer.Unindent();
        }
        writer.Unindent();
        writer.Line("},");
        writer.Line(catalog.MessageGrammarVersion + ");");
        writer.Unindent();
    }

    private static string CSharpParameterType(TranslationArgumentType type)
    {
        switch (type)
        {
            case TranslationArgumentType.String: return "string";
            case TranslationArgumentType.Int: return "long";
            case TranslationArgumentType.Number: return "decimal";
            case TranslationArgumentType.Boolean: return "bool";
            case TranslationArgumentType.Date: return "global::System.DateOnly";
            case TranslationArgumentType.Time: return "global::System.TimeOnly";
            case TranslationArgumentType.DateTime: return "global::System.DateTimeOffset";
            case TranslationArgumentType.Guid: return "global::System.Guid";
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown translation argument type.");
        }
    }

    private static bool HasDirectResource(ResourceTreeNode node)
    {
        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
            if (child.Value.Resource is not null) return true;
        return false;
    }

    private static string ManagerField(ResourceTreeNode node)
    {
        string candidate = "__translationManager";
        while (node.Children.ContainsKey(candidate)) candidate += "_";
        return candidate;
    }

    private static void WriteResourceDocumentation(GenerationWriter writer, CompiledTranslation resource, bool includeParameters)
    {
        string summary = resource.Description ?? "Gets text for the stable key " + resource.Key + ".";
        writer.Line("/// <summary>" + GenerationSupport.XmlDocumentation(summary) + "</summary>");
        writer.Line("/// <remarks>Key: <c>" + GenerationSupport.XmlDocumentation(resource.Key) + "</c>.</remarks>");
        if (!includeParameters) return;
        IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
        for (int i = 0; i < placeholders.Count; i++)
        {
            CompiledTextPlaceholder placeholder = placeholders[i];
            writer.Line("/// <param name=\"" + placeholder.Name + "\">A " + GenerationSupport.XmlDocumentation(GenerationSupport.JsonArgumentType(placeholder.Type)) + " value formatted as <c>" + GenerationSupport.XmlDocumentation(placeholder.Format) + "</c>.</param>");
        }
    }

    private static void WriteObsolete(GenerationWriter writer, CompiledTranslation resource)
    {
        if (resource.DeprecatedReason is not null)
            writer.Line("[global::System.ObsoleteAttribute(" + GenerationSupport.CSharpString(resource.DeprecatedReason) + ")]");
    }
}
