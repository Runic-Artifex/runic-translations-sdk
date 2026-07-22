using System;
using System.Collections.Generic;

namespace WebUIToolkit.TextResources.Compiler.Generation;

internal static class CSharpOutputRenderer
{
    internal static TextResourceGeneratedOutput RenderKeys(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string visibility = Visibility(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName);
        writer.Line("/// <summary>Stable keys for the " + GenerationSupport.XmlDocumentation(catalog.Id) + " text-resource catalog.</summary>");
        writer.Line(visibility + " static partial class " + className + "Keys");
        writer.Line("{");
        writer.Indent();
        WriteKeyMembers(writer, catalog, GenerationSupport.BuildTree(catalog));
        writer.Unindent();
        writer.Line("}");
        return Output(TextResourceGeneratedOutputKind.CSharpKeys, catalog.ClassName + ".Keys.g.cs", writer);
    }

    internal static TextResourceGeneratedOutput RenderAccessors(CompiledTextCatalog catalog)
    {
        var writer = StartFile(catalog);
        string visibility = Visibility(catalog);
        string className = GenerationSupport.CSharpIdentifier(catalog.ClassName);
        ResourceTreeNode root = GenerationSupport.BuildTree(catalog);
        string rootManagerField = ManagerField(root);
        writer.Line("/// <summary>Strongly typed accessors for the " + GenerationSupport.XmlDocumentation(catalog.Id) + " text-resource catalog.</summary>");
        writer.Line(visibility + " sealed partial class " + className);
        writer.Line("{");
        writer.Indent();
        if (HasDirectResource(root))
        {
            writer.Line("private readonly global::WebUIToolkit.TextResources.ITextResourceManager " + rootManagerField + ";");
            writer.Blank();
        }
        writer.Line("/// <summary>Creates accessors over a locale manager.</summary>");
        writer.Line("public " + className + "(global::WebUIToolkit.TextResources.ITextResourceManager manager)");
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
        return Output(TextResourceGeneratedOutputKind.CSharpAccessors, catalog.ClassName + ".Accessors.g.cs", writer);
    }

    internal static TextResourceGeneratedOutput RenderCatalogData(CompiledTextCatalog catalog)
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
        writer.Line("internal static global::WebUIToolkit.TextResources.CompiledTextResourceCatalog CreateDefinition()");
        writer.Line("{");
        writer.Indent();
        writer.Line("return new global::WebUIToolkit.TextResources.CompiledTextResourceCatalog(");
        writer.Indent();
        writer.Line(GenerationSupport.CSharpString(catalog.Id) + ",");
        writer.Line(GenerationSupport.CSharpString(catalog.DefaultLocale) + ",");
        WriteDefinitions(writer, table.Definitions, ",");
        WriteLocales(writer, locales, table, ",");
        writer.Line("global::WebUIToolkit.TextResources.UnsupportedLocalePolicy." + catalog.UnsupportedLocale + ",");
        writer.Line("global::WebUIToolkit.TextResources.MissingTextResourcePolicy." + catalog.MissingKey + ");");
        writer.Unindent();
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        WritePackContractFactory(writer, catalog, table, locales);
        writer.Unindent();
        writer.Line("}");
        return Output(TextResourceGeneratedOutputKind.CSharpCatalogData, catalog.ClassName + ".CatalogData.g.cs", writer);
    }

    internal static TextResourceGeneratedOutput RenderRegistration(CompiledTextCatalog catalog)
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
        writer.Line("public static global::WebUIToolkit.TextResources.ITextResourceProvider CreateProvider(");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourceOptions? options = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("if (global::WebUIToolkit.TextResources.TextResourcesCompatibility.RuntimeAbiVersion != RuntimeAbiVersion)");
        writer.Indent();
        writer.Line("throw new global::System.InvalidOperationException(\"WUTTEXT0024: Generated text-resource code is incompatible with the referenced runtime ABI.\");");
        writer.Unindent();
        writer.Line("return new global::WebUIToolkit.TextResources.CompiledTextResourceProvider(" + className + "CatalogData.CreateDefinition().WithOptions(options), valueFormatter, snapshotFactory);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a manager after asynchronously resolving its initial immutable snapshot.</summary>");
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::WebUIToolkit.TextResources.ITextResourceManager> CreateManagerAsync(");
        writer.Indent();
        writer.Line("string? initialLocale = null,");
        writer.Line("global::WebUIToolkit.TextResources.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceSnapshotFactory? snapshotFactory = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourceOptions? options = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceProvider provider = CreateProvider(valueFormatter, snapshotFactory, options);");
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::WebUIToolkit.TextResources.TextResourceManager(provider, snapshot);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a provider that composes verified caller-supplied packs over compiled snapshots.</summary>");
        writer.Line("public static global::WebUIToolkit.TextResources.ITextResourceProvider CreateExternalProvider(");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.IExternalTextResourceSource externalSource,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourceOptions? options = null,");
        writer.Line("global::WebUIToolkit.TextResources.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackLimits? limits = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackIntegrityVerifier? integrityVerifier = null)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("var factory = new global::WebUIToolkit.TextResources.ExternalTextResourceSnapshotFactory(externalSource, CatalogId, ContractFingerprint, CreateExternalPackContract, limits, integrityVerifier);");
        writer.Line("return CreateProvider(valueFormatter, factory, options);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates a manager whose snapshots compose verified caller-supplied packs.</summary>");
        writer.Line("public static async global::System.Threading.Tasks.ValueTask<global::WebUIToolkit.TextResources.ITextResourceManager> CreateExternalManagerAsync(");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.IExternalTextResourceSource externalSource,");
        writer.Line("string? initialLocale = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourceOptions? options = null,");
        writer.Line("global::WebUIToolkit.TextResources.ITextValueFormatter? valueFormatter = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackLimits? limits = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceProvider provider = CreateExternalProvider(externalSource, options, valueFormatter, limits, integrityVerifier);");
        writer.Line("global::WebUIToolkit.TextResources.ITextResourceSnapshot snapshot = await provider.GetSnapshotAsync(initialLocale ?? DefaultLocale, cancellationToken).ConfigureAwait(false);");
        writer.Line("return new global::WebUIToolkit.TextResources.TextResourceManager(provider, snapshot);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Creates the generated compatibility contract used to verify one declared locale pack.</summary>");
        writer.Line("public static global::WebUIToolkit.TextResources.TextResourcePackContract CreateExternalPackContract(string locale)");
        writer.Line("{");
        writer.Indent();
        writer.Line("return " + className + "CatalogData.CreateExternalPackContract(locale);");
        writer.Unindent();
        writer.Line("}");
        writer.Blank();
        writer.Line("/// <summary>Requests and fully verifies an optional caller-owned pack for one declared locale.</summary>");
        writer.Line("public static global::System.Threading.Tasks.ValueTask<global::WebUIToolkit.TextResources.VerifiedExternalTextResourcePack?> LoadExternalPackAsync(");
        writer.Indent();
        writer.Line("global::WebUIToolkit.TextResources.IExternalTextResourceSource source,");
        writer.Line("string locale,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackLimits? limits = null,");
        writer.Line("global::WebUIToolkit.TextResources.TextResourcePackIntegrityVerifier? integrityVerifier = null,");
        writer.Line("global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Unindent();
        writer.Line("{");
        writer.Indent();
        writer.Line("return global::WebUIToolkit.TextResources.TextResourcePackLoader.LoadAsync(source, CreateExternalPackContract(locale), limits, integrityVerifier, cancellationToken);");
        writer.Unindent();
        writer.Line("}");
        writer.Unindent();
        writer.Line("}");
        return Output(TextResourceGeneratedOutputKind.CSharpRegistration, catalog.ClassName + ".Registration.g.cs", writer);
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

    private static TextResourceGeneratedOutput Output(TextResourceGeneratedOutputKind kind, string path, GenerationWriter writer) =>
        new TextResourceGeneratedOutput(kind, path, "text/x-csharp", writer.ToString());

    private static string Visibility(CompiledTextCatalog catalog) => catalog.Visibility == TextResourceVisibility.Public ? "public" : "internal";

    private static void WriteKeyMembers(GenerationWriter writer, CompiledTextCatalog catalog, ResourceTreeNode node)
    {
        foreach (KeyValuePair<string, ResourceTreeNode> child in node.Children)
        {
            if (child.Value.Resource is not null)
            {
                CompiledTextResource resource = child.Value.Resource;
                WriteResourceDocumentation(writer, resource, false);
                WriteObsolete(writer, resource);
                writer.Line("public static global::WebUIToolkit.TextResources.TextResourceKey " + GenerationSupport.CSharpIdentifier(child.Key) + " { get; } = new global::WebUIToolkit.TextResources.TextResourceKey(" + GenerationSupport.CSharpString(catalog.Id) + ", " + resource.Id + ", " + GenerationSupport.CSharpString(resource.Key) + ");");
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
                writer.Line("private readonly global::WebUIToolkit.TextResources.ITextResourceManager " + childManagerField + ";");
                writer.Blank();
            }
            writer.Line("internal " + identifier + "Group(global::WebUIToolkit.TextResources.ITextResourceManager manager)");
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

    private static void WriteAccessor(GenerationWriter writer, CompiledTextResource resource, string identifier, string keyPath, string managerField)
    {
        WriteResourceDocumentation(writer, resource, true);
        WriteObsolete(writer, resource);
        IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
        if (placeholders.Count == 0)
        {
            writer.Line("public string " + identifier + " => this." + managerField + ".Current.Format(" + keyPath + ", global::System.ReadOnlySpan<global::WebUIToolkit.TextResources.TextArgument>.Empty);");
            return;
        }

        var parameters = new List<string>(placeholders.Count);
        for (int i = 0; i < placeholders.Count; i++)
        {
            CompiledTextPlaceholder placeholder = placeholders[i];
            parameters.Add(CSharpParameterType(placeholder.Type) + " " + GenerationSupport.CSharpIdentifier(placeholder.Name));
        }
        writer.Line("public string " + identifier + "(" + string.Join(", ", parameters) + ")");
        writer.Line("{");
        writer.Indent();
        writer.Line("return this." + managerField + ".Current.Format(" + keyPath + ", new global::WebUIToolkit.TextResources.TextArgument[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < placeholders.Count; i++)
        {
            CompiledTextPlaceholder placeholder = placeholders[i];
            string argument = "new global::WebUIToolkit.TextResources.TextArgument(" + GenerationSupport.CSharpString(placeholder.Name) + ", " + GenerationSupport.CSharpIdentifier(placeholder.Name);
            if (placeholder.Type != TextResourceArgumentType.String)
                argument += ", global::WebUIToolkit.TextResources.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format);
            writer.Line(argument + "),");
        }
        writer.Unindent();
        writer.Line("});");
        writer.Unindent();
        writer.Line("}");
    }

    private static void WriteDefinitions(GenerationWriter writer, IReadOnlyList<GeneratedCatalogDefinition> definitions, string suffix)
    {
        writer.Line("new global::WebUIToolkit.TextResources.CompiledTextResourceDefinition[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < definitions.Count; i++)
        {
            GeneratedCatalogDefinition definition = definitions[i];
            CompiledTextResource resource = definition.Resource;
            IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
            writer.Line("new global::WebUIToolkit.TextResources.CompiledTextResourceDefinition(" + GenerationSupport.CSharpString(resource.Key) + ", new global::WebUIToolkit.TextResources.TextResourcePlaceholderDescriptor[]");
            writer.Line("{");
            writer.Indent();
            for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
            {
                CompiledTextPlaceholder placeholder = placeholders[placeholderIndex];
                writer.Line("new global::WebUIToolkit.TextResources.TextResourcePlaceholderDescriptor(" + GenerationSupport.CSharpString(placeholder.Name) + ", global::WebUIToolkit.TextResources.TextArgumentType." + GenerationSupport.ArgumentTypeName(placeholder.Type) + ", global::WebUIToolkit.TextResources.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format) + "),");
            }
            writer.Unindent();
            writer.Line("}, isCanonical: " + (definition.IsCanonical ? "true" : "false") + "),");
        }
        writer.Unindent();
        writer.Line("}" + suffix);
    }

    private static void WriteLocales(GenerationWriter writer, IReadOnlyList<CompiledTextLocale> locales, GeneratedCatalogTable table, string suffix)
    {
        writer.Line("new global::WebUIToolkit.TextResources.CompiledTextResourceLocale[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < locales.Count; i++)
        {
            CompiledTextLocale locale = locales[i];
            var resources = new List<CompiledTextResource>(locale.DirectResources.Count);
            for (int resourceIndex = 0; resourceIndex < locale.DirectResources.Count; resourceIndex++) resources.Add(locale.DirectResources[resourceIndex]);
            resources.Sort((left, right) => table.GetId(left.Key).CompareTo(table.GetId(right.Key)));
            writer.Line("new global::WebUIToolkit.TextResources.CompiledTextResourceLocale(" + GenerationSupport.CSharpString(locale.Tag) + ", " + (locale.FallbackTag is null ? "null" : GenerationSupport.CSharpString(locale.FallbackTag)) + ", new global::WebUIToolkit.TextResources.CompiledTextResourceValue[]");
            writer.Line("{");
            writer.Indent();
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                CompiledTextResource resource = resources[resourceIndex];
                writer.Line("new global::WebUIToolkit.TextResources.CompiledTextResourceValue(" + table.GetId(resource.Key) + ", " + GenerationSupport.CSharpString(resource.Pattern) + "),");
            }
            writer.Unindent();
            writer.Line("}),");
        }
        writer.Unindent();
        writer.Line("}" + suffix);
    }

    private static void WritePackContractFactory(
        GenerationWriter writer,
        CompiledTextCatalog catalog,
        GeneratedCatalogTable table,
        IReadOnlyList<CompiledTextLocale> locales)
    {
        writer.Line("internal static global::WebUIToolkit.TextResources.TextResourcePackContract CreateExternalPackContract(string locale)");
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
        writer.Line("return new global::WebUIToolkit.TextResources.TextResourcePackContract(");
        writer.Indent();
        writer.Line(GenerationSupport.CSharpString(catalog.Id) + ",");
        writer.Line(GenerationSupport.CSharpString(locale.Tag) + ",");
        writer.Line(GenerationSupport.CSharpString(catalog.Fingerprint) + ",");
        writer.Line("new global::WebUIToolkit.TextResources.TextResourcePackMessageContract[]");
        writer.Line("{");
        writer.Indent();
        for (int i = 0; i < selectedDefinitions.Count; i++)
        {
            GeneratedCatalogDefinition definition = selectedDefinitions[i];
            CompiledTextResource resource = definition.Resource;
            IReadOnlyList<CompiledTextPlaceholder> placeholders = GenerationSupport.OrderedPlaceholders(resource.Placeholders);
            writer.Line("new global::WebUIToolkit.TextResources.TextResourcePackMessageContract(");
            writer.Indent();
            writer.Line("new global::WebUIToolkit.TextResources.TextResourceKey(" + GenerationSupport.CSharpString(catalog.Id) + ", " + definition.Id + ", " + GenerationSupport.CSharpString(resource.Key) + "),");
            writer.Line("new global::WebUIToolkit.TextResources.TextResourcePackArgumentContract[]");
            writer.Line("{");
            writer.Indent();
            for (int placeholderIndex = 0; placeholderIndex < placeholders.Count; placeholderIndex++)
            {
                CompiledTextPlaceholder placeholder = placeholders[placeholderIndex];
                writer.Line("new global::WebUIToolkit.TextResources.TextResourcePackArgumentContract(" + GenerationSupport.CSharpString(placeholder.Name) + ", global::WebUIToolkit.TextResources.TextArgumentType." + GenerationSupport.ArgumentTypeName(placeholder.Type) + ", global::WebUIToolkit.TextResources.TextArgumentFormat." + GenerationSupport.ArgumentFormatName(placeholder.Format) + "),");
            }
            writer.Unindent();
            writer.Line("}),");
            writer.Unindent();
        }
        writer.Unindent();
        writer.Line("});");
        writer.Unindent();
    }

    private static string CSharpParameterType(TextResourceArgumentType type)
    {
        switch (type)
        {
            case TextResourceArgumentType.String: return "string";
            case TextResourceArgumentType.Int: return "long";
            case TextResourceArgumentType.Number: return "decimal";
            case TextResourceArgumentType.Boolean: return "bool";
            case TextResourceArgumentType.Date: return "global::System.DateOnly";
            case TextResourceArgumentType.Time: return "global::System.TimeOnly";
            case TextResourceArgumentType.DateTime: return "global::System.DateTimeOffset";
            case TextResourceArgumentType.Guid: return "global::System.Guid";
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown text-resource argument type.");
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
        string candidate = "__textResourceManager";
        while (node.Children.ContainsKey(candidate)) candidate += "_";
        return candidate;
    }

    private static void WriteResourceDocumentation(GenerationWriter writer, CompiledTextResource resource, bool includeParameters)
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

    private static void WriteObsolete(GenerationWriter writer, CompiledTextResource resource)
    {
        if (resource.DeprecatedReason is not null)
            writer.Line("[global::System.ObsoleteAttribute(" + GenerationSupport.CSharpString(resource.DeprecatedReason) + ")]");
    }
}
