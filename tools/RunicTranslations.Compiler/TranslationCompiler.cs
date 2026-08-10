using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace RunicTranslations.Compiler;

public static class TranslationCompiler
{
    private static readonly string[] ManifestMembers = { "$schema", "schemaVersion", "catalog", "code", "defaultLocale", "locales", "layers", "validation", "runtime", "outputs" };
    private static readonly string[] DocumentMembers = { "$schema", "schemaVersion", "catalog", "locale", "layer", "resources" };
    private static readonly string[] LeafMembers = { "$value", "$description", "$placeholders", "$since", "$deprecated", "$tags" };
    private static readonly string[] CodeMembers = { "namespace", "className", "visibility" };
    private static readonly string[] LocaleMembers = { "tag", "fallback" };
    private static readonly string[] LayerMembers = { "name", "priority" };
    private static readonly string[] ValidationMembers = { "translationCompleteness", "extraLocaleKeys", "emptyValues" };
    private static readonly string[] RuntimeMembers = { "unsupportedLocale", "missingKey" };
    private static readonly string[] OutputMembers = { "typescript", "templateManifest" };
    private static readonly string[] TypeScriptOutputMembers = { "enabled", "moduleName" };
    private static readonly string[] TemplateOutputMembers = { "enabled" };
    private static readonly string[] PlaceholderMembers = { "type", "format" };
    private static readonly string[] StructuredMessageMembers = { "inputs", "declarations", "selectors", "variants" };
    private static readonly string[] DeclarationMembers = { "name", "input", "function", "format", "unit", "numeric" };
    private static readonly string[] SelectorMembers = { "name", "input", "function" };
    private static readonly string[] VariantMembers = { "match", "value" };
    private static readonly string[] PatternInputMembers = { "input" };
    private static readonly string[] PatternFormatMembers = { "format" };
    private static readonly string[] PatternMarkupMembers = { "markup" };
    private static readonly string[] PatternLocalMembers = { "local" };
    private static readonly string[] FormatExpressionMembers = { "input", "function", "format", "unit", "numeric" };
    private static readonly string[] MarkupExpressionMembers = { "name", "attributes", "children" };

    /// <summary>Compiles manifest and resource document sources using default limits.</summary>
    /// <remarks>Inputs and outputs are deterministic and no environment state is consulted.</remarks>
    public static TranslationCompilation Compile(
        IEnumerable<TranslationSource> manifests,
        IEnumerable<TranslationSource> documents,
        TranslationCompilerOptions? options = null)
        => Compile(manifests, documents, options, CancellationToken.None);

    /// <summary>Compiles manifest and resource document sources with cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static TranslationCompilation Compile(
        IEnumerable<TranslationSource> manifests,
        IEnumerable<TranslationSource> documents,
        CancellationToken cancellationToken)
        => Compile(manifests, documents, null, cancellationToken);

    /// <summary>Compiles manifest and resource document sources with explicit limits and cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static TranslationCompilation Compile(
        IEnumerable<TranslationSource> manifests,
        IEnumerable<TranslationSource> documents,
        TranslationCompilerOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TranslationCompilerOptions();
        var diagnostics = new DiagnosticBag();
        TranslationSource[] manifestSources = Materialize(manifests);
        TranslationSource[] documentSources = Materialize(documents);
        if (RejectDuplicateSourcePaths(manifestSources, documentSources, diagnostics))
            return new TranslationCompilation(Array.Empty<CompiledTextCatalog>(), diagnostics.ToSortedArray());
        var manifestModels = new List<ManifestModel>();
        var documentModels = new List<DocumentModel>();

        for (int i = 0; i < manifestSources.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedJson parsed = StrictJsonParser.Parse(manifestSources[i], diagnostics, options, cancellationToken);
            if (parsed.Root is not null)
            {
                ManifestModel? model = ReadManifest(parsed, diagnostics, options);
                if (model is not null) manifestModels.Add(model);
            }
        }
        for (int i = 0; i < documentSources.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ParsedJson parsed = StrictJsonParser.Parse(documentSources[i], diagnostics, options, cancellationToken);
            if (parsed.Root is not null)
            {
                DocumentModel? model = ReadDocument(parsed, diagnostics, options);
                if (model is not null) documentModels.Add(model);
            }
        }

        var manifestsById = new Dictionary<string, ManifestModel>(StringComparer.Ordinal);
        for (int i = 0; i < manifestModels.Count; i++)
        {
            ManifestModel manifest = manifestModels[i];
            if (manifest.Id.Length == 0) continue;
            if (!manifestsById.TryAdd(manifest.Id, manifest))
                diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error,
                    "Catalog '" + manifest.Id + "' has more than one manifest.", manifest.Source, manifest.IdSpan);
        }
        ValidateGeneratedRootIdentities(manifestModels, diagnostics);

        var docsByCatalog = new Dictionary<string, List<DocumentModel>>(StringComparer.Ordinal);
        for (int i = 0; i < documentModels.Count; i++)
        {
            DocumentModel document = documentModels[i];
            if (!manifestsById.ContainsKey(document.Catalog))
                diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error,
                    "Resource document has no matching manifest for catalog '" + document.Catalog + "'.", document.Source, document.CatalogSpan);
            if (!docsByCatalog.TryGetValue(document.Catalog, out List<DocumentModel>? list))
            {
                list = new List<DocumentModel>();
                docsByCatalog.Add(document.Catalog, list);
            }
            list.Add(document);
        }

        var catalogs = new List<CompiledTextCatalog>();
        foreach (KeyValuePair<string, ManifestModel> pair in SortedPairs(manifestsById))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!docsByCatalog.TryGetValue(pair.Key, out List<DocumentModel>? catalogDocuments) || catalogDocuments.Count == 0)
            {
                diagnostics.Add("RTR0002", TranslationDiagnosticSeverity.Error,
                    "Catalog '" + pair.Key + "' has no resource documents.", pair.Value.Source, pair.Value.IdSpan);
                continue;
            }
            CompiledTextCatalog? catalog = CompileCatalog(pair.Value, catalogDocuments, diagnostics, options, cancellationToken);
            if (catalog is not null) catalogs.Add(catalog);
        }

        return new TranslationCompilation(catalogs.ToArray(), diagnostics.ToSortedArray());
    }

    private static TranslationSource[] Materialize(IEnumerable<TranslationSource> sources)
    {
        var result = new List<TranslationSource>();
        foreach (TranslationSource source in sources)
        {
            if (source is null) throw new ArgumentException("A source collection contains null.", nameof(sources));
            result.Add(source);
        }
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return result.ToArray();
    }

    private static bool RejectDuplicateSourcePaths(
        TranslationSource[] manifests,
        TranslationSource[] documents,
        DiagnosticBag diagnostics)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Count(manifests);
        Count(documents);

        var duplicates = new List<string>();
        foreach (KeyValuePair<string, int> pair in counts)
            if (pair.Value > 1) duplicates.Add(pair.Key);
        duplicates.Sort(StringComparer.Ordinal);

        for (int i = 0; i < duplicates.Count; i++)
        {
            TranslationSource representative = Find(manifests, duplicates[i]) ?? Find(documents, duplicates[i])!;
            diagnostics.Add(
                "RTR0002",
                TranslationDiagnosticSeverity.Error,
                "Normalized source path '" + duplicates[i] + "' is supplied more than once.",
                representative,
                new ByteSpan(0, 0));
        }

        return duplicates.Count != 0;

        void Count(TranslationSource[] sources)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (counts.TryGetValue(sources[i].Path, out int count)) counts[sources[i].Path] = count + 1;
                else counts.Add(sources[i].Path, 1);
            }
        }

        static TranslationSource? Find(TranslationSource[] sources, string path)
        {
            for (int i = 0; i < sources.Length; i++)
                if (string.Equals(sources[i].Path, path, StringComparison.Ordinal)) return sources[i];
            return null;
        }
    }

    private static ManifestModel? ReadManifest(ParsedJson parsed, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        JsonValue root = parsed.Root!;
        if (root.Kind != JsonKind.Object)
        {
            diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Catalog manifest root must be an object.", parsed.Source, root.Span);
            return null;
        }
        ValidateKnownMembers(root, ManifestMembers, parsed.Source, diagnostics);
        int schemaVersion = ValidateSchema(root, parsed.Source, diagnostics);
        var model = new ManifestModel(parsed.Source);
        model.SchemaVersion = schemaVersion;
        JsonProperty? catalog = Required(root, "catalog", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? code = Required(root, "code", JsonKind.Object, parsed.Source, diagnostics);
        JsonProperty? defaultLocale = Required(root, "defaultLocale", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? locales = Required(root, "locales", JsonKind.Array, parsed.Source, diagnostics);
        JsonProperty? layers = Required(root, "layers", JsonKind.Array, parsed.Source, diagnostics);
        if (catalog is not null)
        {
            model.Id = catalog.Value.Text!; model.IdSpan = catalog.Value.Span;
            if (IsWindowsDeviceStem(model.Id))
                diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Catalog ID '" + model.Id + "' produces a Windows-reserved generated filename stem.", parsed.Source, catalog.Value.Span);
            else if (!IsCatalogId(model.Id))
                diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "Catalog ID must use lowercase ASCII letters, digits, dots, or hyphens.", parsed.Source, catalog.Value.Span);
        }
        if (code is not null) ReadCode(code.Value, model, parsed.Source, diagnostics);
        if (defaultLocale is not null)
        {
            model.DefaultLocaleSpan = defaultLocale.Value.Span;
            if (!TryCanonicalizeLocale(defaultLocale.Value.Text!, out string canonical))
                diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid default locale '" + defaultLocale.Value.Text + "'.", parsed.Source, defaultLocale.Value.Span);
            else model.DefaultLocale = canonical;
        }
        if (locales is not null) ReadLocales(locales.Value, model, parsed.Source, diagnostics, options);
        if (layers is not null) ReadLayers(layers.Value, model, parsed.Source, diagnostics);
        JsonProperty? validation = root.Property("validation");
        if (validation is not null) ReadValidation(validation, model, parsed.Source, diagnostics);
        JsonProperty? runtime = root.Property("runtime");
        if (runtime is not null) ReadRuntime(runtime, model, parsed.Source, diagnostics);
        JsonProperty? outputs = root.Property("outputs");
        if (outputs is not null) ValidateOutputs(outputs, parsed.Source, diagnostics);
        ValidateFallbackGraph(model, diagnostics);
        return model;
    }

    private static int ValidateSchema(JsonValue root, TranslationSource source, DiagnosticBag diagnostics)
    {
        JsonProperty? schemaHint = root.Property("$schema");
        if (schemaHint is not null)
        {
            diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error,
                "No canonical $schema URI is registered; omit $schema for schema version 1.", source, schemaHint.Value.Span);
        }
        JsonProperty? version = root.Property("schemaVersion");
        if (version is null)
        {
            diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error, "Missing required schemaVersion 1.", source, root.Span);
            return 1;
        }
        if (version.Value.Kind != JsonKind.Number ||
            (!string.Equals(version.Value.Text, "1", StringComparison.Ordinal) && !string.Equals(version.Value.Text, "2", StringComparison.Ordinal)))
        {
            diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error, "Unsupported schemaVersion; expected integer 1 or 2.", source, version.Value.Span);
            return 1;
        }
        return string.Equals(version.Value.Text, "2", StringComparison.Ordinal) ? 2 : 1;
    }

    private static void ReadCode(JsonValue value, ManifestModel model, TranslationSource source, DiagnosticBag diagnostics)
    {
        ValidateKnownMembers(value, CodeMembers, source, diagnostics);
        JsonProperty? ns = Required(value, "namespace", JsonKind.String, source, diagnostics);
        JsonProperty? className = Required(value, "className", JsonKind.String, source, diagnostics);
        JsonProperty? visibility = value.Property("visibility");
        if (ns is not null)
        {
            model.CodeNamespace = ns.Value.Text!;
            if (!IsNamespace(model.CodeNamespace)) diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "Invalid C# namespace '" + model.CodeNamespace + "'.", source, ns.Value.Span);
        }
        if (className is not null)
        {
            model.ClassName = className.Value.Text!;
            model.ClassNameSpan = className.Value.Span;
            if (IsWindowsDeviceStem(model.ClassName))
                diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Generated class name '" + model.ClassName + "' produces a Windows-reserved filename stem.", source, className.Value.Span);
            else if (!IsIdentifier(model.ClassName))
                diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "Invalid generated class name '" + model.ClassName + "'.", source, className.Value.Span);
        }
        if (visibility is not null)
        {
            if (visibility.Value.Kind != JsonKind.String || (visibility.Value.Text != "public" && visibility.Value.Text != "internal"))
                diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "visibility must be 'public' or 'internal'.", source, visibility.Value.Span);
            else model.Visibility = visibility.Value.Text == "internal" ? TranslationVisibility.Internal : TranslationVisibility.Public;
        }
    }

    private static void ReadLocales(JsonValue value, ManifestModel model, TranslationSource source, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        if (value.Items.Count == 0) diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "locales must not be empty.", source, value.Span);
        if (value.Items.Count > options.MaximumLocalesPerCatalog)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Locale count exceeds the configured limit.", source, value.Span);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < value.Items.Count; i++)
        {
            JsonValue item = value.Items[i];
            if (item.Kind != JsonKind.Object)
            {
                diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Each locale declaration must be an object.", source, item.Span); continue;
            }
            ValidateKnownMembers(item, LocaleMembers, source, diagnostics);
            JsonProperty? tagProperty = Required(item, "tag", JsonKind.String, source, diagnostics);
            JsonProperty? fallbackProperty = item.Property("fallback");
            if (tagProperty is null) continue;
            if (!TryCanonicalizeLocale(tagProperty.Value.Text!, out string tag))
            {
                diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid locale tag '" + tagProperty.Value.Text + "'.", source, tagProperty.Value.Span); continue;
            }
            if (!tags.Add(tag)) diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Duplicate locale '" + tag + "'.", source, tagProperty.Value.Span);
            string? fallback = null;
            ByteSpan fallbackSpan = item.Span;
            if (fallbackProperty is not null)
            {
                fallbackSpan = fallbackProperty.Value.Span;
                if (fallbackProperty.Value.Kind != JsonKind.String || !TryCanonicalizeLocale(fallbackProperty.Value.Text!, out fallback))
                    diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid fallback locale.", source, fallbackProperty.Value.Span);
            }
            model.Locales.Add(new LocaleModel(tag, fallback, tagProperty.Value.Span, fallbackSpan));
        }
    }

    private static void ReadLayers(JsonValue value, ManifestModel model, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (value.Items.Count == 0) diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "layers must not be empty.", source, value.Span);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var priorities = new HashSet<int>();
        for (int i = 0; i < value.Items.Count; i++)
        {
            JsonValue item = value.Items[i];
            if (item.Kind != JsonKind.Object) { diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Each layer declaration must be an object.", source, item.Span); continue; }
            ValidateKnownMembers(item, LayerMembers, source, diagnostics);
            JsonProperty? name = Required(item, "name", JsonKind.String, source, diagnostics);
            JsonProperty? priority = Required(item, "priority", JsonKind.Number, source, diagnostics);
            if (name is null || priority is null) continue;
            if (!IsCatalogId(name.Value.Text!)) diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Invalid layer name '" + name.Value.Text + "'.", source, name.Value.Span);
            if (!int.TryParse(priority.Value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int priorityValue))
            { diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Layer priority must be a signed 32-bit integer.", source, priority.Value.Span); continue; }
            if (!names.Add(name.Value.Text!)) diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Duplicate layer name '" + name.Value.Text + "'.", source, name.Value.Span);
            if (!priorities.Add(priorityValue)) diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Duplicate layer priority " + priorityValue.ToString(CultureInfo.InvariantCulture) + ".", source, priority.Value.Span);
            model.Layers.Add(new LayerModel(name.Value.Text!, priorityValue, name.Value.Span, priority.Value.Span));
        }
        model.Layers.Sort((left, right) => left.Priority != right.Priority ? left.Priority.CompareTo(right.Priority) : StringComparer.Ordinal.Compare(left.Name, right.Name));
    }

    private static void ReadValidation(JsonProperty property, ManifestModel model, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "validation must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, ValidationMembers, source, diagnostics);
        model.Completeness = ReadPolicy(property.Value.Property("translationCompleteness"), model.Completeness, source, diagnostics);
        model.ExtraKeys = ReadPolicy(property.Value.Property("extraLocaleKeys"), model.ExtraKeys, source, diagnostics);
        model.EmptyValues = ReadPolicy(property.Value.Property("emptyValues"), model.EmptyValues, source, diagnostics);
    }

    private static TranslationPolicy ReadPolicy(JsonProperty? property, TranslationPolicy defaultValue, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return defaultValue;
        if (property.Value.Kind != JsonKind.String) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Validation policy must be allow, warning, or error.", source, property.Value.Span); return defaultValue; }
        switch (property.Value.Text)
        {
            case "allow": return TranslationPolicy.Allow;
            case "warning": return TranslationPolicy.Warning;
            case "error": return TranslationPolicy.Error;
            default: diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Unknown validation policy '" + property.Value.Text + "'.", source, property.Value.Span); return defaultValue;
        }
    }

    private static void ReadRuntime(JsonProperty property, ManifestModel model, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "runtime must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, RuntimeMembers, source, diagnostics);
        JsonProperty? unsupported = property.Value.Property("unsupportedLocale");
        if (unsupported is not null && unsupported.Value.Kind == JsonKind.String)
        {
            switch (unsupported.Value.Text)
            {
                case "exact": model.UnsupportedLocale = TranslationUnsupportedLocalePolicy.Exact; break;
                case "parentsThenDefault": model.UnsupportedLocale = TranslationUnsupportedLocalePolicy.ParentsThenDefault; break;
                case "default": model.UnsupportedLocale = TranslationUnsupportedLocalePolicy.Default; break;
                default: diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Unknown unsupportedLocale policy.", source, unsupported.Value.Span); break;
            }
        }
        else if (unsupported is not null) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "unsupportedLocale must be a string.", source, unsupported.Value.Span);
        JsonProperty? missing = property.Value.Property("missingKey");
        if (missing is not null && missing.Value.Kind == JsonKind.String)
        {
            switch (missing.Value.Text)
            {
                case "throw": model.MissingKey = TranslationMissingKeyPolicy.Throw; break;
                case "returnKey": model.MissingKey = TranslationMissingKeyPolicy.ReturnKey; break;
                case "returnMarker": model.MissingKey = TranslationMissingKeyPolicy.ReturnMarker; break;
                default: diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Unknown missingKey policy.", source, missing.Value.Span); break;
            }
        }
        else if (missing is not null) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "missingKey must be a string.", source, missing.Value.Span);
    }

    private static void ValidateOutputs(JsonProperty property, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "outputs must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, OutputMembers, source, diagnostics);
        for (int i = 0; i < property.Value.Properties.Count; i++)
        {
            JsonProperty output = property.Value.Properties[i];
            if (output.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Output configuration must be an object.", source, output.Value.Span); continue; }
            string[] allowed = output.Name == "typescript" ? TypeScriptOutputMembers : TemplateOutputMembers;
            ValidateKnownMembers(output.Value, allowed, source, diagnostics);
            JsonProperty? enabled = output.Value.Property("enabled");
            if (enabled is not null && enabled.Value.Kind != JsonKind.True && enabled.Value.Kind != JsonKind.False)
                diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "enabled must be boolean.", source, enabled.Value.Span);
            JsonProperty? module = output.Value.Property("moduleName");
            if (module is not null && module.Value.Kind != JsonKind.String)
                diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "moduleName must be a string.", source, module.Value.Span);
        }
    }

    private static void ValidateFallbackGraph(ManifestModel model, DiagnosticBag diagnostics)
    {
        var locales = new Dictionary<string, LocaleModel>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < model.Locales.Count; i++) if (!locales.ContainsKey(model.Locales[i].Tag)) locales.Add(model.Locales[i].Tag, model.Locales[i]);
        if (model.DefaultLocale.Length > 0 && !locales.ContainsKey(model.DefaultLocale))
            diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "defaultLocale is not declared in locales.", model.Source, model.DefaultLocaleSpan);
        for (int i = 0; i < model.Locales.Count; i++)
        {
            LocaleModel locale = model.Locales[i];
            if (string.Equals(locale.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) && locale.Fallback is not null)
                diagnostics.Add("RTR0012", TranslationDiagnosticSeverity.Error, "The default locale must not declare a fallback.", model.Source, locale.FallbackSpan);
            if (locale.Fallback is not null && !locales.ContainsKey(locale.Fallback))
                diagnostics.Add("RTR0012", TranslationDiagnosticSeverity.Error, "Fallback locale '" + locale.Fallback + "' is not declared.", model.Source, locale.FallbackSpan);
            if (!string.Equals(locale.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) && locale.Fallback is null)
                diagnostics.Add("RTR0013", TranslationDiagnosticSeverity.Error, "Locale '" + locale.Tag + "' has no fallback path to the default locale.", model.Source, locale.Span);
        }

        var fullyChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < model.Locales.Count; i++)
        {
            LocaleModel start = model.Locales[i];
            if (fullyChecked.Contains(start.Tag) || string.Equals(start.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase)) continue;
            var path = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pathItems = new List<string>();
            LocaleModel current = start;
            while (!string.Equals(current.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) && current.Fallback is not null && locales.TryGetValue(current.Fallback, out LocaleModel? next))
            {
                path.Add(current.Tag); pathItems.Add(current.Tag);
                if (path.Contains(next.Tag))
                {
                    diagnostics.Add("RTR0013", TranslationDiagnosticSeverity.Error, "Fallback cycle closes at locale '" + next.Tag + "'.", model.Source, current.FallbackSpan);
                    for (int p = 0; p < pathItems.Count; p++) fullyChecked.Add(pathItems[p]);
                    break;
                }
                current = next;
            }
            for (int p = 0; p < pathItems.Count; p++) fullyChecked.Add(pathItems[p]);
        }
    }

    private static DocumentModel? ReadDocument(ParsedJson parsed, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        JsonValue root = parsed.Root!;
        if (root.Kind != JsonKind.Object) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Resource document root must be an object.", parsed.Source, root.Span); return null; }
        ValidateKnownMembers(root, DocumentMembers, parsed.Source, diagnostics);
        int schemaVersion = ValidateSchema(root, parsed.Source, diagnostics);
        JsonProperty? catalog = Required(root, "catalog", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? locale = Required(root, "locale", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? layer = Required(root, "layer", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? resources = Required(root, "resources", JsonKind.Object, parsed.Source, diagnostics);
        if (catalog is null || locale is null || layer is null || resources is null) return null;
        var model = new DocumentModel(parsed.Source)
        {
            SchemaVersion = schemaVersion,
            Catalog = catalog.Value.Text!,
            CatalogSpan = catalog.Value.Span,
            Layer = layer.Value.Text!,
            LayerSpan = layer.Value.Span,
            LocaleSpan = locale.Value.Span,
        };
        // The matching manifest owns generated-name validation. Preserve an
        // uppercase device spelling here so it associates with that manifest
        // and produces one focused RTR0018 instead of a document cascade.
        if (!IsCatalogId(model.Catalog) && !IsWindowsDeviceStem(model.Catalog))
            diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "Catalog ID must use lowercase ASCII letters, digits, dots, or hyphens.", parsed.Source, catalog.Value.Span);
        if (!TryCanonicalizeLocale(locale.Value.Text!, out string canonicalLocale))
            diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid locale tag '" + locale.Value.Text + "'.", parsed.Source, locale.Value.Span);
        else model.Locale = canonicalLocale;
        if (!IsCatalogId(model.Layer)) diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Invalid layer name '" + model.Layer + "'.", parsed.Source, layer.Value.Span);
        FlattenResources(resources.Value, string.Empty, default, model, diagnostics, options, 0);
        return model;
    }

    private static void FlattenResources(JsonValue group, string prefix, ByteSpan rootSpan, DocumentModel document, DiagnosticBag diagnostics, TranslationCompilerOptions options, int depth)
    {
        for (int i = 0; i < group.Properties.Count; i++)
        {
            JsonProperty property = group.Properties[i];
            string key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            ByteSpan pathSpan = prefix.Length == 0 ? property.NameSpan : rootSpan;
            if (depth + 1 > options.MaximumDepth)
            {
                diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Resource tree exceeds the configured depth limit.", document.Source, property.NameSpan);
                document.HadLimitError = true;
                continue;
            }
            if (!IsIdentifier(property.Name) || property.Name[0] == '$')
            {
                string id = property.Name.Length > 0 && property.Name[0] == '$' ? "RTR0019" : "RTR0006";
                diagnostics.Add(id, TranslationDiagnosticSeverity.Error, "Invalid resource key segment '" + property.Name + "'.", document.Source, property.NameSpan); continue;
            }
            if (property.Value.Kind == JsonKind.String)
            {
                AddLeaf(document, diagnostics, options, key, property.NameSpan, pathSpan, property.Value.Span, property.Value.Text!, null, null, null, Array.Empty<string>(), Array.Empty<PlaceholderModel>());
                continue;
            }
            if (property.Value.Kind != JsonKind.Object)
            {
                diagnostics.Add("RTR0008", TranslationDiagnosticSeverity.Error, "Resource '" + key + "' must be a string, group, or metadata leaf.", document.Source, property.Value.Span); continue;
            }
            JsonProperty? value = property.Value.Property("$value");
            bool hasMetadata = HasDollarMember(property.Value);
            if (value is null)
            {
                if (hasMetadata) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Metadata leaf '" + key + "' is missing $value.", document.Source, property.Value.Span);
                else FlattenResources(property.Value, key, pathSpan, document, diagnostics, options, depth + 1);
                continue;
            }
            ReadMetadataLeaf(property, key, pathSpan, document, diagnostics, options);
        }
    }

    private static void ReadMetadataLeaf(JsonProperty property, string key, ByteSpan pathSpan, DocumentModel document, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        ValidateKnownMembers(property.Value, LeafMembers, document.Source, diagnostics);
        for (int i = 0; i < property.Value.Properties.Count; i++)
            if (property.Value.Properties[i].Name.Length == 0 || property.Value.Properties[i].Name[0] != '$')
                diagnostics.Add("RTR0008", TranslationDiagnosticSeverity.Error, "A metadata leaf cannot also contain child resources.", document.Source, property.Value.Properties[i].NameSpan);
        JsonProperty? value = property.Value.Property("$value");
        if (value is null)
        {
            diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Metadata leaf is missing required member '$value'.", document.Source, property.Value.Span);
            return;
        }
        if (value.Value.Kind != JsonKind.String && !(document.SchemaVersion == 2 && value.Value.Kind == JsonKind.Object))
        {
            diagnostics.Add("RTR0008", TranslationDiagnosticSeverity.Error, "$value must be a string or a schema version 2 structured message.", document.Source, value.Value.Span);
            return;
        }
        if (value is null) return;
        string? description = ReadOptionalString(property.Value.Property("$description"), document.Source, diagnostics);
        string? since = ReadOptionalString(property.Value.Property("$since"), document.Source, diagnostics);
        string? deprecated = ReadOptionalString(property.Value.Property("$deprecated"), document.Source, diagnostics);
        string[] tags = ReadTags(property.Value.Property("$tags"), document.Source, diagnostics);
        PlaceholderModel[] placeholders = ReadPlaceholders(property.Value.Property("$placeholders"), document.Source, diagnostics, options);
        if (value.Value.Kind == JsonKind.Object)
            AddStructuredLeaf(document, diagnostics, options, key, property.NameSpan, pathSpan, value.Value, description, since, deprecated, tags);
        else
            AddLeaf(document, diagnostics, options, key, property.NameSpan, pathSpan, value.Value.Span, value.Value.Text!, description, since, deprecated, tags, placeholders);
    }

    private static void AddStructuredLeaf(DocumentModel document, DiagnosticBag diagnostics, TranslationCompilerOptions options,
        string key, ByteSpan keySpan, ByteSpan pathSpan, JsonValue value, string? description, string? since, string? deprecated, string[] tags)
    {
        ValidateKnownMembers(value, StructuredMessageMembers, document.Source, diagnostics);
        JsonProperty? inputsProperty = Required(value, "inputs", JsonKind.Object, document.Source, diagnostics);
        JsonProperty? selectorsProperty = Required(value, "selectors", JsonKind.Array, document.Source, diagnostics);
        JsonProperty? variantsProperty = Required(value, "variants", JsonKind.Array, document.Source, diagnostics);
        if (inputsProperty is null || selectorsProperty is null || variantsProperty is null) return;

        PlaceholderModel[] placeholders = ReadPortableInputs(inputsProperty.Value, document.Source, diagnostics, options);
        var inputTypes = new Dictionary<string, TranslationArgumentType>(StringComparer.Ordinal);
        for (int index = 0; index < placeholders.Length; index++) inputTypes[placeholders[index].Name] = placeholders[index].Type;
        var declarations = ReadDeclarations(value.Property("declarations"), inputTypes, document.Source, diagnostics);
        var selectors = new List<CompiledMessageSelector>();
        var selectorNames = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < selectorsProperty.Value.Items.Count; index++)
        {
            JsonValue selector = selectorsProperty.Value.Items[index];
            if (selector.Kind != JsonKind.Object) { diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A selector must be an object.", document.Source, selector.Span); continue; }
            ValidateKnownMembers(selector, SelectorMembers, document.Source, diagnostics);
            JsonProperty? name = Required(selector, "name", JsonKind.String, document.Source, diagnostics);
            JsonProperty? input = Required(selector, "input", JsonKind.String, document.Source, diagnostics);
            JsonProperty? function = Required(selector, "function", JsonKind.String, document.Source, diagnostics);
            if (name is null || input is null || function is null) continue;
            if (!IsIdentifier(name.Value.Text!) || !selectorNames.Add(name.Value.Text!))
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Selector names must be unique identifiers.", document.Source, name.Value.Span);
            if (!inputTypes.TryGetValue(input.Value.Text!, out TranslationArgumentType inputType))
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Selector input '" + input.Value.Text + "' is not declared.", document.Source, input.Value.Span);
            string functionName = function.Value.Text!;
            if (functionName is not ("plural" or "ordinal" or "literal"))
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Unknown selector function '" + functionName + "'.", document.Source, function.Value.Span);
            if (functionName is "plural" or "ordinal" && inputType is not (TranslationArgumentType.Int or TranslationArgumentType.Number))
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Plural selectors require an int64 or decimal input.", document.Source, input.Value.Span);
            selectors.Add(new CompiledMessageSelector(name.Value.Text!, input.Value.Text!, functionName));
        }

        var variants = new List<CompiledMessageVariant>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        bool catchAll = false;
        for (int index = 0; index < variantsProperty.Value.Items.Count; index++)
        {
            JsonValue variant = variantsProperty.Value.Items[index];
            if (variant.Kind != JsonKind.Object) { diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A variant must be an object.", document.Source, variant.Span); continue; }
            ValidateKnownMembers(variant, VariantMembers, document.Source, diagnostics);
            JsonProperty? match = Required(variant, "match", JsonKind.Object, document.Source, diagnostics);
            JsonProperty? pattern = variant.Property("value");
            if (pattern is null || pattern.Value.Kind is not (JsonKind.String or JsonKind.Array))
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Variant value must be a string or structured pattern array.", document.Source, pattern?.Value.Span ?? variant.Span);
                continue;
            }
            if (match is null || pattern is null) continue;
            var matches = new SortedDictionary<string, string>(StringComparer.Ordinal);
            for (int m = 0; m < match.Value.Properties.Count; m++)
            {
                JsonProperty item = match.Value.Properties[m];
                if (!selectorNames.Contains(item.Name) || item.Value.Kind != JsonKind.String || item.Value.Text!.Length == 0)
                    diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Variant matches must name every declared selector and use a non-empty string.", document.Source, item.Value.Span);
                else matches[item.Name] = item.Value.Text!;
            }
            if (matches.Count != selectors.Count)
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A variant must match every declared selector.", document.Source, match.Value.Span);
            string signature = string.Join("|", matches);
            if (!signatures.Add(signature)) diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Duplicate variant match.", document.Source, match.Value.Span);
            bool all = matches.Count == selectors.Count;
            foreach (KeyValuePair<string, string> item in matches) all &= item.Value == "*";
            catchAll |= all;
            CompiledMessagePattern? compiled = pattern.Value.Kind == JsonKind.String
                ? MessagePatternCompiler.Compile(pattern.Value.Text!, document.Source, pattern.Value.Span, diagnostics, out HashSet<string> used)
                : CompileStructuredPattern(pattern.Value, inputTypes, declarations, document.Source, diagnostics, out used);
            foreach (string usedName in used) if (!inputTypes.ContainsKey(usedName))
                diagnostics.Add("RTR0015", TranslationDiagnosticSeverity.Error, "Input '" + usedName + "' is used but not declared.", document.Source, pattern.Value.Span);
            if (compiled is not null) variants.Add(new CompiledMessageVariant(matches, compiled));
        }
        if (!catchAll) diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A structured message requires an all-'*' catch-all variant.", document.Source, variantsProperty.Value.Span);
        if (variants.Count == 0) return;
        CompiledMessagePattern message = new(Array.Empty<CompiledMessageNode>(), selectors.ToArray(), variants.ToArray());
        string fallbackPattern = variants[^1].Pattern.Nodes.Count == 0 ? string.Empty : PatternText(variants[^1].Pattern);
        document.Resources.Add(new ResourceModel(key, fallbackPattern, message, description, since, deprecated, tags, placeholders,
            document.Source, keySpan, pathSpan, value.Span));
    }

    private static CompiledMessagePattern? CompileStructuredPattern(JsonValue pattern,
        Dictionary<string, TranslationArgumentType> inputTypes, Dictionary<string, CompiledMessageFormat> declarations,
        TranslationSource source, DiagnosticBag diagnostics,
        out HashSet<string> used)
    {
        used = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<CompiledMessageNode>();
        for (int index = 0; index < pattern.Items.Count; index++)
        {
            JsonValue item = pattern.Items[index];
            if (item.Kind == JsonKind.String)
            {
                nodes.Add(new CompiledMessageText(item.Text!));
                continue;
            }
            if (item.Kind != JsonKind.Object)
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Structured pattern nodes must be strings or objects.", source, item.Span);
                continue;
            }
            JsonProperty? input = item.Property("input");
            JsonProperty? format = item.Property("format");
            JsonProperty? markup = item.Property("markup");
            JsonProperty? local = item.Property("local");
            int nodeMemberCount = (input is null ? 0 : 1) + (format is null ? 0 : 1) + (markup is null ? 0 : 1) + (local is null ? 0 : 1);
            if (nodeMemberCount != 1)
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A structured pattern node must contain exactly one of 'input', 'local', 'format', or 'markup'.", source, item.Span);
                continue;
            }
            if (input is not null)
            {
                ValidateKnownMembers(item, PatternInputMembers, source, diagnostics);
                if (input.Value.Kind != JsonKind.String || !inputTypes.ContainsKey(input.Value.Text!))
                    diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Pattern input is not declared.", source, input.Value.Span);
                else { nodes.Add(new CompiledMessageInput(input.Value.Text!)); used.Add(input.Value.Text!); }
                continue;
            }
            if (local is not null)
            {
                ValidateKnownMembers(item, PatternLocalMembers, source, diagnostics);
                if (local.Value.Kind != JsonKind.String || !declarations.TryGetValue(local.Value.Text!, out CompiledMessageFormat? declaration))
                    diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Pattern local is not declared.", source, local.Value.Span);
                else
                {
                    nodes.Add(new CompiledMessageFormat(declaration.Input, declaration.Function, declaration.Format, declaration.Unit, declaration.Numeric));
                    used.Add(declaration.Input);
                }
                continue;
            }
            if (markup is not null)
            {
                ValidateKnownMembers(item, PatternMarkupMembers, source, diagnostics);
                if (markup.Value.Kind != JsonKind.Object)
                {
                    diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A markup expression must be an object.", source, markup.Value.Span);
                    continue;
                }
                JsonValue markupExpression = markup.Value;
                ValidateKnownMembers(markupExpression, MarkupExpressionMembers, source, diagnostics);
                JsonProperty? name = Required(markupExpression, "name", JsonKind.String, source, diagnostics);
                JsonProperty? children = Required(markupExpression, "children", JsonKind.Array, source, diagnostics);
                JsonProperty? attributes = markupExpression.Property("attributes");
                if (name is null || children is null || !IsIdentifier(name.Value.Text!))
                {
                    diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Markup names must be identifiers.", source, name?.Value.Span ?? markupExpression.Span);
                    continue;
                }
                var compiledAttributes = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (attributes is not null)
                {
                    if (attributes.Value.Kind != JsonKind.Object)
                        diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Markup attributes must be an object.", source, attributes.Value.Span);
                    else for (int attributeIndex = 0; attributeIndex < attributes.Value.Properties.Count; attributeIndex++)
                    {
                        JsonProperty attribute = attributes.Value.Properties[attributeIndex];
                        if (!IsIdentifier(attribute.Name) || attribute.Value.Kind != JsonKind.String)
                            diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Markup attributes require identifier names and string values.", source, attribute.Value.Span);
                        else compiledAttributes[attribute.Name] = attribute.Value.Text!;
                    }
                }
                CompiledMessagePattern? childPattern = CompileStructuredPattern(children.Value, inputTypes, declarations, source, diagnostics, out HashSet<string> childUsed);
                foreach (string childInput in childUsed) used.Add(childInput);
                if (childPattern is not null) nodes.Add(new CompiledMessageMarkup(name.Value.Text!, compiledAttributes, childPattern.Nodes));
                continue;
            }
            ValidateKnownMembers(item, PatternFormatMembers, source, diagnostics);
            if (format!.Value.Kind != JsonKind.Object)
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A format expression must be an object.", source, format.Value.Span);
                continue;
            }
            JsonValue expression = format.Value;
            ValidateKnownMembers(expression, FormatExpressionMembers, source, diagnostics);
            JsonProperty? expressionInput = Required(expression, "input", JsonKind.String, source, diagnostics);
            JsonProperty? function = Required(expression, "function", JsonKind.String, source, diagnostics);
            if (expressionInput is null || function is null || !inputTypes.TryGetValue(expressionInput.Value.Text!, out TranslationArgumentType inputType))
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Format input is not declared.", source, expression.Span);
                continue;
            }
            CompiledMessageFormat? compiledFormat = CompileFormatExpression(
                expression, expressionInput.Value.Text!, function.Value.Text!, inputType, source, diagnostics);
            if (compiledFormat is not null) nodes.Add(compiledFormat);
            used.Add(expressionInput.Value.Text!);
        }
        return new CompiledMessagePattern(nodes.ToArray());
    }

    private static Dictionary<string, CompiledMessageFormat> ReadDeclarations(JsonProperty? property,
        Dictionary<string, TranslationArgumentType> inputTypes, TranslationSource source, DiagnosticBag diagnostics)
    {
        var result = new Dictionary<string, CompiledMessageFormat>(StringComparer.Ordinal);
        if (property is null) return result;
        if (property.Value.Kind != JsonKind.Array)
        {
            diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Declarations must be an array.", source, property.Value.Span);
            return result;
        }
        for (int index = 0; index < property.Value.Items.Count; index++)
        {
            JsonValue declaration = property.Value.Items[index];
            if (declaration.Kind != JsonKind.Object)
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "A declaration must be an object.", source, declaration.Span);
                continue;
            }
            ValidateKnownMembers(declaration, DeclarationMembers, source, diagnostics);
            JsonProperty? name = Required(declaration, "name", JsonKind.String, source, diagnostics);
            JsonProperty? input = Required(declaration, "input", JsonKind.String, source, diagnostics);
            JsonProperty? function = Required(declaration, "function", JsonKind.String, source, diagnostics);
            if (name is null || input is null || function is null) continue;
            if (!IsIdentifier(name.Value.Text!) || result.ContainsKey(name.Value.Text!))
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Declaration names must be unique identifiers.", source, name.Value.Span);
                continue;
            }
            if (!inputTypes.TryGetValue(input.Value.Text!, out TranslationArgumentType inputType))
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Declaration input is not declared.", source, input.Value.Span);
                continue;
            }
            CompiledMessageFormat? compiled = CompileFormatExpression(declaration, input.Value.Text!, function.Value.Text!, inputType, source, diagnostics);
            if (compiled is not null) result.Add(name.Value.Text!, compiled);
        }
        return result;
    }

    private static CompiledMessageFormat? CompileFormatExpression(JsonValue expression, string input, string functionName,
        TranslationArgumentType inputType, TranslationSource source, DiagnosticBag diagnostics)
    {
        foreach (string optionName in new[] { "format", "unit", "numeric" })
        {
            JsonProperty? option = expression.Property(optionName);
            if (option is not null && option.Value.Kind != JsonKind.String)
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Format option '" + optionName + "' must be a string.", source, option.Value.Span);
                return null;
            }
        }
        string formatName = expression.Property("format")?.Value.Text ?? DefaultFormat(inputType);
        if (functionName == "relativeTime")
        {
            string? unit = expression.Property("unit")?.Value.Text;
            string numeric = expression.Property("numeric")?.Value.Text ?? "always";
            if (inputType is not (TranslationArgumentType.Int or TranslationArgumentType.Number) ||
                unit is not ("second" or "minute" or "hour" or "day" or "week" or "month" or "year") ||
                numeric is not ("always" or "auto"))
            {
                diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Relative-time format requires a numeric input, valid unit, and numeric mode.", source, expression.Span);
                return null;
            }
            return new CompiledMessageFormat(input, functionName, "plain", unit, numeric);
        }
        if (!FunctionMatches(functionName, inputType) || !IsAllowedFormat(inputType, formatName))
        {
            diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Format function or format is incompatible with its input.", source, expression.Span);
            return null;
        }
        return new CompiledMessageFormat(input, functionName, formatName, null, null);
    }

    private static bool FunctionMatches(string function, TranslationArgumentType type) => function switch
    {
        "string" => type == TranslationArgumentType.String,
        "integer" => type == TranslationArgumentType.Int,
        "number" => type == TranslationArgumentType.Number,
        "date" => type == TranslationArgumentType.Date,
        "time" => type == TranslationArgumentType.Time,
        "datetime" => type == TranslationArgumentType.DateTime,
        "uuid" => type == TranslationArgumentType.Guid,
        _ => false,
    };

    private static string DefaultFormat(TranslationArgumentType type) => type switch
    {
        TranslationArgumentType.String => "none",
        TranslationArgumentType.Int or TranslationArgumentType.Number => "plain",
        TranslationArgumentType.Boolean => "lower",
        TranslationArgumentType.Date or TranslationArgumentType.Time or TranslationArgumentType.DateTime => "iso",
        TranslationArgumentType.Guid => "d",
        _ => "none",
    };

    private static PlaceholderModel[] ReadPortableInputs(JsonValue value, TranslationSource source, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        if (value.Properties.Count > options.MaximumPlaceholdersPerValue)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Input count exceeds the configured limit.", source, value.Span);
        var result = new List<PlaceholderModel>();
        for (int index = 0; index < value.Properties.Count; index++)
        {
            JsonProperty input = value.Properties[index];
            if (!IsIdentifier(input.Name) || input.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Invalid input declaration.", source, input.Value.Span); continue; }
            ValidateKnownMembers(input.Value, PlaceholderMembers, source, diagnostics);
            JsonProperty? type = Required(input.Value, "type", JsonKind.String, source, diagnostics);
            JsonProperty? format = input.Value.Property("format");
            if (type is null || !TryPortableArgumentType(type.Value.Text!, out TranslationArgumentType argumentType, out string defaultFormat))
            { diagnostics.Add("RTR0030", TranslationDiagnosticSeverity.Error, "Unknown portable input type.", source, type?.Value.Span ?? input.Value.Span); continue; }
            string selected = format?.Value.Text ?? defaultFormat;
            if (format is not null && (format.Value.Kind != JsonKind.String || !IsAllowedFormat(argumentType, selected)))
            { diagnostics.Add("RTR0017", TranslationDiagnosticSeverity.Error, "Invalid portable input format.", source, format.Value.Span); selected = defaultFormat; }
            result.Add(new PlaceholderModel(input.Name, argumentType, selected, input.NameSpan, type.Value.Span, format?.Value.Span ?? type.Value.Span));
        }
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return result.ToArray();
    }

    private static bool TryPortableArgumentType(string value, out TranslationArgumentType type, out string format)
    {
        string mapped = value switch { "int64" => "int", "decimal" => "number", "instant" => "datetime", "uuid" => "guid", _ => value };
        return TryArgumentType(mapped, out type, out format);
    }

    private static string PatternText(CompiledMessagePattern pattern)
    {
        var builder = new StringBuilder();
        Append(pattern.Nodes, builder);
        return builder.ToString();

        static void Append(IReadOnlyList<CompiledMessageNode> nodes, StringBuilder target)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                if (nodes[index] is CompiledMessageText text) target.Append(text.Value.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal));
                else if (nodes[index] is CompiledMessageInput input) target.Append('{').Append(input.Name).Append('}');
                else if (nodes[index] is CompiledMessageFormat format) target.Append('{').Append(format.Input).Append('}');
                else if (nodes[index] is CompiledMessageMarkup markup) Append(markup.Children, target);
            }
        }
    }

    private static void AddLeaf(DocumentModel document, DiagnosticBag diagnostics, TranslationCompilerOptions options, string key, ByteSpan keySpan, ByteSpan pathSpan, ByteSpan valueSpan,
        string pattern, string? description, string? since, string? deprecated, string[] tags, PlaceholderModel[] placeholders)
    {
        if (StrictJsonParser.StrictUtf8.GetByteCount(pattern) > options.MaximumValueBytes)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Resource value exceeds the configured byte limit.", document.Source, valueSpan);
        CompiledMessagePattern? message = MessagePatternCompiler.Compile(
            pattern,
            document.Source,
            valueSpan,
            diagnostics,
            out HashSet<string> patternNames);
        if (message is not null)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < placeholders.Length; i++) declared.Add(placeholders[i].Name);
            foreach (string name in Sorted(patternNames))
                if (!declared.Contains(name)) diagnostics.Add("RTR0015", TranslationDiagnosticSeverity.Error, "Placeholder '" + name + "' is used but not declared.", document.Source, valueSpan);
            for (int i = 0; i < placeholders.Length; i++)
                if (!patternNames.Contains(placeholders[i].Name)) diagnostics.Add("RTR0015", TranslationDiagnosticSeverity.Error, "Placeholder '" + placeholders[i].Name + "' is declared but not used.", document.Source, placeholders[i].Span);
        }
        // Keep the invalid leaf in the semantic model so the existing diagnostic
        // pipeline does not manufacture secondary "missing canonical key" errors.
        // Generation is never reached for a failed compilation.
        message ??= new CompiledMessagePattern(Array.Empty<CompiledMessageNode>());
        document.Resources.Add(new ResourceModel(key, pattern, message, description, since, deprecated, tags, placeholders, document.Source, keySpan, pathSpan, valueSpan));
    }

    private static PlaceholderModel[] ReadPlaceholders(JsonProperty? property, TranslationSource source, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        if (property is null) return Array.Empty<PlaceholderModel>();
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0015", TranslationDiagnosticSeverity.Error, "$placeholders must be an object.", source, property.Value.Span); return Array.Empty<PlaceholderModel>(); }
        if (property.Value.Properties.Count > options.MaximumPlaceholdersPerValue)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Placeholder count exceeds the configured limit.", source, property.Value.Span);
        var result = new List<PlaceholderModel>();
        for (int i = 0; i < property.Value.Properties.Count; i++)
        {
            JsonProperty descriptor = property.Value.Properties[i];
            if (!IsIdentifier(descriptor.Name)) { diagnostics.Add("RTR0015", TranslationDiagnosticSeverity.Error, "Invalid placeholder name '" + descriptor.Name + "'.", source, descriptor.NameSpan); continue; }
            if (descriptor.Value.Kind != JsonKind.Object) { diagnostics.Add("RTR0017", TranslationDiagnosticSeverity.Error, "Placeholder descriptor must be an object.", source, descriptor.Value.Span); continue; }
            ValidateKnownMembers(descriptor.Value, PlaceholderMembers, source, diagnostics);
            JsonProperty? type = Required(descriptor.Value, "type", JsonKind.String, source, diagnostics);
            JsonProperty? format = descriptor.Value.Property("format");
            if (type is null) continue;
            if (!TryArgumentType(type.Value.Text!, out TranslationArgumentType argumentType, out string defaultFormat))
            {
                diagnostics.Add("RTR0017", TranslationDiagnosticSeverity.Error, "Unknown placeholder type '" + type.Value.Text + "'.", source, type.Value.Span);
                result.Add(new PlaceholderModel(descriptor.Name, TranslationArgumentType.String, "none", descriptor.NameSpan, type.Value.Span, type.Value.Span));
                continue;
            }
            string selectedFormat = defaultFormat;
            if (format is not null)
            {
                if (format.Value.Kind != JsonKind.String || !IsAllowedFormat(argumentType, format.Value.Text!))
                {
                    diagnostics.Add("RTR0017", TranslationDiagnosticSeverity.Error, "Invalid format for placeholder type '" + type.Value.Text + "'.", source, format.Value.Span);
                    result.Add(new PlaceholderModel(descriptor.Name, argumentType, defaultFormat, descriptor.NameSpan, type.Value.Span, format.Value.Span));
                    continue;
                }
                selectedFormat = format.Value.Text!;
            }
            result.Add(new PlaceholderModel(descriptor.Name, argumentType, selectedFormat, descriptor.NameSpan,
                type.Value.Span, format is null ? type.Value.Span : format.Value.Span));
        }
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return result.ToArray();
    }

    private static CompiledTextCatalog? CompileCatalog(ManifestModel manifest, List<DocumentModel> documents, DiagnosticBag diagnostics, TranslationCompilerOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var localeMap = new Dictionary<string, LocaleModel>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.Locales.Count; i++) localeMap.TryAdd(manifest.Locales[i].Tag, manifest.Locales[i]);
        var layerMap = new Dictionary<string, LayerModel>(StringComparer.Ordinal);
        for (int i = 0; i < manifest.Layers.Count; i++) layerMap.TryAdd(manifest.Layers[i].Name, manifest.Layers[i]);
        var buckets = new Dictionary<string, Dictionary<string, Dictionary<string, ResourceModel>>>(StringComparer.OrdinalIgnoreCase);
        var allPathKinds = new Dictionary<string, ResourceModel>(StringComparer.Ordinal);
        documents.Sort((left, right) => StringComparer.Ordinal.Compare(left.Source.Path, right.Source.Path));
        for (int i = 0; i < documents.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DocumentModel document = documents[i];
            if (document.SchemaVersion != manifest.SchemaVersion)
            {
                diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error,
                    "Resource document schemaVersion must match its catalog manifest.", document.Source, document.CatalogSpan);
                continue;
            }
            if (!localeMap.ContainsKey(document.Locale)) { diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Document locale '" + document.Locale + "' is not declared.", document.Source, document.LocaleSpan); continue; }
            if (!layerMap.ContainsKey(document.Layer)) { diagnostics.Add("RTR0005", TranslationDiagnosticSeverity.Error, "Document layer '" + document.Layer + "' is not declared.", document.Source, document.LayerSpan); continue; }
            if (!buckets.TryGetValue(document.Locale, out Dictionary<string, Dictionary<string, ResourceModel>>? byLayer)) { byLayer = new Dictionary<string, Dictionary<string, ResourceModel>>(StringComparer.Ordinal); buckets.Add(document.Locale, byLayer); }
            if (!byLayer.TryGetValue(document.Layer, out Dictionary<string, ResourceModel>? resources)) { resources = new Dictionary<string, ResourceModel>(StringComparer.Ordinal); byLayer.Add(document.Layer, resources); }
            for (int r = 0; r < document.Resources.Count; r++)
            {
                ResourceModel resource = document.Resources[r];
                if (!resources.TryAdd(resource.Key, resource)) diagnostics.Add("RTR0007", TranslationDiagnosticSeverity.Error, "Duplicate key '" + resource.Key + "' in locale '" + document.Locale + "' and layer '" + document.Layer + "'.", resource.Source, resource.KeySpan);
                foreach (KeyValuePair<string, ResourceModel> existing in allPathKinds)
                    if (IsPathPrefix(existing.Key, resource.Key) || IsPathPrefix(resource.Key, existing.Key))
                    { diagnostics.Add("RTR0008", TranslationDiagnosticSeverity.Error, "Resource path '" + resource.Key + "' conflicts with leaf '" + existing.Key + "'.", resource.Source, resource.PathSpan); break; }
                allPathKinds.TryAdd(resource.Key, resource);
                ValidateIdentifierCollision(manifest, resource, diagnostics);
            }
        }
        if (allPathKinds.Count > options.MaximumKeysPerCatalog)
            diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Catalog key count exceeds the configured limit.", manifest.Source, manifest.IdSpan);
        ValidateGeneratedTreeCollisions(manifest, allPathKinds, diagnostics);

        var directByLocale = new Dictionary<string, Dictionary<string, ResourceModel>>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.Locales.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string tag = manifest.Locales[i].Tag;
            var effective = new Dictionary<string, ResourceModel>(StringComparer.Ordinal);
            if (buckets.TryGetValue(tag, out Dictionary<string, Dictionary<string, ResourceModel>>? byLayer))
                for (int layer = 0; layer < manifest.Layers.Count; layer++)
                    if (byLayer.TryGetValue(manifest.Layers[layer].Name, out Dictionary<string, ResourceModel>? layerResources))
                        foreach (KeyValuePair<string, ResourceModel> pair in layerResources) effective[pair.Key] = pair.Value;
            directByLocale[tag] = effective;
        }
        directByLocale.TryGetValue(manifest.DefaultLocale, out Dictionary<string, ResourceModel>? canonical);
        bool hasDefaultDocument = false;
        for (int i = 0; i < documents.Count; i++)
            if (string.Equals(documents[i].Locale, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase))
                hasDefaultDocument = true;
        if (canonical is null || (canonical.Count == 0 && (manifest.SchemaVersion < 2 || !hasDefaultDocument)))
        {
            bool defaultDocumentHadLimitError = false;
            for (int i = 0; i < documents.Count; i++)
                if (string.Equals(documents[i].Locale, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase) && documents[i].HadLimitError)
                    defaultDocumentHadLimitError = true;
            if (!defaultDocumentHadLimitError)
                diagnostics.Add("RTR0009", TranslationDiagnosticSeverity.Error, "The effective default locale defines no canonical keys.", manifest.Source, manifest.DefaultLocaleSpan);
            return null;
        }
        string[] canonicalKeys = Keys(canonical);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < canonicalKeys.Length; i++) ids.Add(canonicalKeys[i], i);
        for (int i = 0; i < manifest.Locales.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocaleModel locale = manifest.Locales[i];
            Dictionary<string, ResourceModel> direct = directByLocale[locale.Tag];
            foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(canonical))
            {
                if (ContainsRelativeTime(pair.Value.Message) && !SupportsRelativeTime(locale.Tag))
                    diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error,
                        "The built-in relative-time registry does not support locale '" + locale.Tag + "'.",
                        pair.Value.Source, pair.Value.ValueSpan);
                for (int selectorIndex = 0; selectorIndex < pair.Value.Message.Selectors.Count; selectorIndex++)
                {
                    CompiledMessageSelector selector = pair.Value.Message.Selectors[selectorIndex];
                    if (selector.Function is "plural" or "ordinal" && !SupportsBuiltInPlural(locale.Tag, selector.Function == "ordinal"))
                        diagnostics.Add("RTR0031", TranslationDiagnosticSeverity.Error,
                            "The built-in plural registry does not support locale '" + locale.Tag + "' for selector '" + selector.Name + "'.",
                            pair.Value.Source, pair.Value.ValueSpan);
                }
            }
            bool hasValidFallback = locale.Fallback is null || localeMap.ContainsKey(locale.Fallback);
            if (!string.Equals(locale.Tag, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase))
            {
                if (hasValidFallback)
                    for (int key = 0; key < canonicalKeys.Length; key++)
                        if (!direct.ContainsKey(canonicalKeys[key])) AddPolicyDiagnostic("RTR0010", manifest.Completeness, "Locale '" + locale.Tag + "' lacks direct translation for key '" + canonicalKeys[key] + "'.", manifest.Source, locale.Span, diagnostics);
                foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                    if (!canonical.ContainsKey(pair.Key)) AddPolicyDiagnostic("RTR0011", manifest.ExtraKeys, "Locale '" + locale.Tag + "' defines non-canonical key '" + pair.Key + "'.", pair.Value.Source, pair.Value.KeySpan, diagnostics);
                foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                    if (canonical.TryGetValue(pair.Key, out ResourceModel? defaultResource) && !SameContract(defaultResource, pair.Value, out ByteSpan mismatchSpan))
                        diagnostics.Add("RTR0016", TranslationDiagnosticSeverity.Error, "Translation placeholder contract for key '" + pair.Key + "' differs from the default locale.", pair.Value.Source, mismatchSpan);
            }
            foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                if (pair.Value.Pattern.Length == 0) AddPolicyDiagnostic("RTR0021", manifest.EmptyValues, "Resource '" + pair.Key + "' has an empty value.", pair.Value.Source, pair.Value.ValueSpan, diagnostics);
        }

        ValidateExtraKeyContracts(manifest, canonical, directByLocale, diagnostics);

        var compiledLocales = new List<CompiledTextLocale>();
        var localeByTag = new Dictionary<string, LocaleModel>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < manifest.Locales.Count; i++) if (!localeByTag.ContainsKey(manifest.Locales[i].Tag)) localeByTag.Add(manifest.Locales[i].Tag, manifest.Locales[i]);
        var orderedLocales = new List<LocaleModel>(manifest.Locales); orderedLocales.Sort((left, right) => StringComparer.Ordinal.Compare(left.Tag, right.Tag));
        for (int i = 0; i < orderedLocales.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LocaleModel locale = orderedLocales[i];
            Dictionary<string, ResourceModel> direct = directByLocale[locale.Tag];
            var resolved = new Dictionary<string, ResourceModel>(StringComparer.Ordinal);
            LocaleModel? current = locale;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (current is not null && visited.Add(current.Tag))
            {
                foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(directByLocale[current.Tag])) if (!resolved.ContainsKey(pair.Key)) resolved.Add(pair.Key, pair.Value);
                current = current.Fallback is not null && localeByTag.TryGetValue(current.Fallback, out LocaleModel? next) ? next : null;
            }
            compiledLocales.Add(new CompiledTextLocale(locale.Tag, locale.Fallback,
                CompileResources(direct, ids), CompileResources(resolved, ids)));
        }
        IReadOnlyList<CompiledTranslation> canonicalResources = CompileResources(canonical, ids);
        string fingerprint = Fingerprint(manifest.Id, manifest.SchemaVersion, canonicalResources);
        var layers = new List<CompiledTextLayer>();
        for (int i = 0; i < manifest.Layers.Count; i++) layers.Add(new CompiledTextLayer(manifest.Layers[i].Name, manifest.Layers[i].Priority));
        return new CompiledTextCatalog(manifest.Id, manifest.CodeNamespace, manifest.ClassName, manifest.Visibility, manifest.DefaultLocale,
            layers.ToArray(), compiledLocales.ToArray(), canonicalResources, manifest.UnsupportedLocale, manifest.MissingKey, fingerprint,
            manifest.SchemaVersion, manifest.SchemaVersion);
    }

    private static CompiledTranslation[] CompileResources(Dictionary<string, ResourceModel> resources, Dictionary<string, int> ids)
    {
        var result = new List<CompiledTranslation>();
        foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(resources))
        {
            ResourceModel resource = pair.Value;
            var placeholders = new List<CompiledTextPlaceholder>();
            for (int i = 0; i < resource.Placeholders.Length; i++) placeholders.Add(new CompiledTextPlaceholder(resource.Placeholders[i].Name, resource.Placeholders[i].Type, resource.Placeholders[i].Format));
            result.Add(new CompiledTranslation(ids.TryGetValue(pair.Key, out int id) ? id : -1, pair.Key, resource.Pattern, resource.Description,
                resource.Since, resource.DeprecatedReason, (string[])resource.Tags.Clone(), placeholders.ToArray(), DiagnosticBag.Location(resource.Source, resource.KeySpan), resource.Message));
        }
        return result.ToArray();
    }

    private static bool SupportsBuiltInPlural(string locale, bool ordinal)
        => ordinal ? TranslationCapabilityRegistry.SupportsOrdinal(locale) : TranslationCapabilityRegistry.SupportsCardinal(locale);

    private static bool SupportsRelativeTime(string locale) =>
        TranslationCapabilityRegistry.SupportsRelativeTime(locale);

    private static bool ContainsRelativeTime(CompiledMessagePattern message)
    {
        if (Contains(message.Nodes)) return true;
        for (int index = 0; index < message.Variants.Count; index++) if (Contains(message.Variants[index].Pattern.Nodes)) return true;
        return false;

        static bool Contains(IReadOnlyList<CompiledMessageNode> nodes)
        {
            for (int index = 0; index < nodes.Count; index++)
            {
                if (nodes[index] is CompiledMessageFormat { Function: "relativeTime" }) return true;
                if (nodes[index] is CompiledMessageMarkup markup && Contains(markup.Children)) return true;
            }
            return false;
        }
    }

    private static string Fingerprint(string catalog, int messageGrammarVersion, IReadOnlyList<CompiledTranslation> resources)
    {
        var builder = new StringBuilder();
        builder.Append("{\"catalog\":").Append(JsonQuote(catalog)).Append(",\"messageGrammarVersion\":")
            .Append(messageGrammarVersion).Append(",\"resources\":[");
        for (int i = 0; i < resources.Count; i++)
        {
            if (i != 0) builder.Append(',');
            builder.Append("{\"key\":").Append(JsonQuote(resources[i].Key)).Append(",\"arguments\":[");
            for (int p = 0; p < resources[i].Placeholders.Count; p++)
            {
                if (p != 0) builder.Append(',');
                CompiledTextPlaceholder placeholder = resources[i].Placeholders[p];
                builder.Append("{\"name\":").Append(JsonQuote(placeholder.Name)).Append(",\"type\":")
                    .Append(JsonQuote(ArgumentTypeName(placeholder.Type))).Append(",\"format\":").Append(JsonQuote(placeholder.Format)).Append('}');
            }
            builder.Append("],\"selectors\":[");
            for (int selectorIndex = 0; selectorIndex < resources[i].Message.Selectors.Count; selectorIndex++)
            {
                if (selectorIndex != 0) builder.Append(',');
                CompiledMessageSelector selector = resources[i].Message.Selectors[selectorIndex];
                builder.Append("{\"name\":").Append(JsonQuote(selector.Name)).Append(",\"input\":")
                    .Append(JsonQuote(selector.Input)).Append(",\"function\":").Append(JsonQuote(selector.Function)).Append('}');
            }
            builder.Append("]}");
        }
        builder.Append("]}");
        byte[] bytes = StrictJsonParser.StrictUtf8.GetBytes(builder.ToString());
        byte[] hash = SHA256.HashData(bytes);
        var hex = new StringBuilder(hash.Length * 2 + 7).Append("sha256:");
        for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
        return hex.ToString();
    }

    private static string JsonQuote(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            switch (ch)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (ch < 0x20) result.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else result.Append(ch);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    private static void ValidateIdentifierCollision(ManifestModel manifest, ResourceModel resource, DiagnosticBag diagnostics)
    {
        string[] segments = resource.Key.Split('.');
        if (segments.Length > 0 && string.Equals(segments[0], manifest.ClassName, StringComparison.Ordinal))
            diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Generated identifier '" + segments[0] + "' collides with enclosing class '" + manifest.ClassName + "'.", resource.Source, resource.KeySpan);
        for (int i = 1; i < segments.Length; i++)
            if (string.Equals(segments[i], segments[i - 1], StringComparison.Ordinal))
            { diagnostics.Add("RTR0018", TranslationDiagnosticSeverity.Error, "Generated identifier '" + segments[i] + "' collides with its enclosing type.", resource.Source, resource.KeySpan); break; }
    }

    private static void ValidateGeneratedRootIdentities(
        List<ManifestModel> manifests,
        DiagnosticBag diagnostics)
    {
        var ordered = new List<ManifestModel>(manifests);
        ordered.Sort((left, right) =>
        {
            int comparison = StringComparer.Ordinal.Compare(left.Source.Path, right.Source.Path);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.Id, right.Id);
        });
        var identities = new Dictionary<string, ManifestModel>(StringComparer.Ordinal);
        var hintStems = new Dictionary<string, ManifestModel>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < ordered.Count; i++)
        {
            ManifestModel manifest = ordered[i];
            if (manifest.Id.Length == 0 || manifest.CodeNamespace.Length == 0 || manifest.ClassName.Length == 0 ||
                !IsNamespace(manifest.CodeNamespace) || !IsIdentifier(manifest.ClassName))
                continue;
            string identity = manifest.CodeNamespace + "\0" + manifest.ClassName;
            bool exactTypeCollision = false;
            if (!identities.TryGetValue(identity, out ManifestModel? first))
            {
                identities.Add(identity, manifest);
            }
            else if (!string.Equals(first.Id, manifest.Id, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    "RTR0018",
                    TranslationDiagnosticSeverity.Error,
                    "Generated type '" + manifest.CodeNamespace + "." + manifest.ClassName +
                    "' for catalog '" + manifest.Id + "' collides with catalog '" + first.Id +
                    "' declared in '" + first.Source.Path + "'.",
                    manifest.Source,
                    manifest.ClassNameSpan);
                exactTypeCollision = true;
            }

            if (!hintStems.TryGetValue(manifest.ClassName, out ManifestModel? firstHint))
            {
                hintStems.Add(manifest.ClassName, manifest);
            }
            else if (!exactTypeCollision && !string.Equals(firstHint.Id, manifest.Id, StringComparison.Ordinal))
            {
                diagnostics.Add(
                    "RTR0018",
                    TranslationDiagnosticSeverity.Error,
                    "Generated hint stem '" + manifest.ClassName + "' for catalog '" + manifest.Id +
                    "' collides case-insensitively with stem '" + firstHint.ClassName + "' for catalog '" +
                    firstHint.Id + "' declared in '" + firstHint.Source.Path + "'.",
                    manifest.Source,
                    manifest.ClassNameSpan);
            }
        }
    }

    private static void ValidateGeneratedTreeCollisions(
        ManifestModel manifest,
        Dictionary<string, ResourceModel> resources,
        DiagnosticBag diagnostics)
    {
        var groupPaths = new HashSet<string>(StringComparer.Ordinal);
        var representatives = new Dictionary<string, ResourceModel>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(resources))
        {
            string[] segments = pair.Key.Split('.');
            string parent = string.Empty;
            for (int index = 0; index < segments.Length; index++)
            {
                string childPath = parent.Length == 0 ? segments[index] : parent + "." + segments[index];
                if (!representatives.ContainsKey(childPath)) representatives.Add(childPath, pair.Value);
                if (index < segments.Length - 1) groupPaths.Add(childPath);
                parent = childPath;
            }
        }

        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (string groupPath in Sorted(groupPaths))
        {
            int separator = groupPath.LastIndexOf('.');
            string parent = separator < 0 ? string.Empty : groupPath.Substring(0, separator);
            string groupName = separator < 0 ? groupPath : groupPath.Substring(separator + 1);
            string synthesizedName = groupName + "Group";
            string siblingPath = parent.Length == 0 ? synthesizedName : parent + "." + synthesizedName;
            ReportCollision(siblingPath, synthesizedName, "a generated accessor group type");

            string nestedPath = groupPath + "." + synthesizedName;
            ReportCollision(nestedPath, synthesizedName, "its enclosing generated accessor group type");
        }

        string keysTypeName = manifest.ClassName + "Keys";
        ReportCollision(keysTypeName, keysTypeName, "the enclosing generated keys class");

        void ReportCollision(string path, string identifier, string target)
        {
            if (!reported.Add(path) || !representatives.TryGetValue(path, out ResourceModel? offender)) return;
            diagnostics.Add(
                "RTR0018",
                TranslationDiagnosticSeverity.Error,
                "Generated identifier '" + identifier + "' collides with " + target + ".",
                offender.Source,
                offender.KeySpan);
        }
    }

    private static void ValidateExtraKeyContracts(
        ManifestModel manifest,
        Dictionary<string, ResourceModel> canonical,
        Dictionary<string, Dictionary<string, ResourceModel>> directByLocale,
        DiagnosticBag diagnostics)
    {
        var contracts = new Dictionary<string, ResourceModel>(StringComparer.Ordinal);
        var orderedLocales = new List<LocaleModel>(manifest.Locales);
        orderedLocales.Sort((left, right) => StringComparer.Ordinal.Compare(left.Tag, right.Tag));
        for (int localeIndex = 0; localeIndex < orderedLocales.Count; localeIndex++)
        {
            LocaleModel locale = orderedLocales[localeIndex];
            if (string.Equals(locale.Tag, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(directByLocale[locale.Tag]))
            {
                if (canonical.ContainsKey(pair.Key)) continue;
                if (!contracts.TryGetValue(pair.Key, out ResourceModel? existing))
                {
                    contracts.Add(pair.Key, pair.Value);
                }
                else if (!SameContract(existing, pair.Value, out ByteSpan mismatchSpan))
                {
                    diagnostics.Add(
                        "RTR0016",
                        TranslationDiagnosticSeverity.Error,
                        "Placeholder contract for allowed extra key '" + pair.Key + "' differs between locales.",
                        pair.Value.Source,
                        mismatchSpan);
                }
            }
        }
    }

    private static void AddPolicyDiagnostic(string id, TranslationPolicy policy, string message, TranslationSource source, ByteSpan span, DiagnosticBag diagnostics)
    {
        if (policy == TranslationPolicy.Allow) return;
        diagnostics.Add(id, policy == TranslationPolicy.Warning ? TranslationDiagnosticSeverity.Warning : TranslationDiagnosticSeverity.Error, message, source, span);
    }

    private static bool SameContract(ResourceModel left, ResourceModel right, out ByteSpan mismatchSpan)
    {
        mismatchSpan = right.KeySpan;
        if (left.Message.HasMarkup != right.Message.HasMarkup) { mismatchSpan = right.ValueSpan; return false; }
        if (left.Placeholders.Length != right.Placeholders.Length) return false;
        for (int i = 0; i < left.Placeholders.Length; i++)
        {
            if (left.Placeholders[i].Name != right.Placeholders[i].Name) { mismatchSpan = right.Placeholders[i].Span; return false; }
            if (left.Placeholders[i].Type != right.Placeholders[i].Type) { mismatchSpan = right.Placeholders[i].TypeSpan; return false; }
            if (left.Placeholders[i].Format != right.Placeholders[i].Format) { mismatchSpan = right.Placeholders[i].FormatSpan; return false; }
        }
        if (left.Message.Selectors.Count != right.Message.Selectors.Count) { mismatchSpan = right.ValueSpan; return false; }
        for (int i = 0; i < left.Message.Selectors.Count; i++)
        {
            CompiledMessageSelector leftSelector = left.Message.Selectors[i];
            CompiledMessageSelector rightSelector = right.Message.Selectors[i];
            if (leftSelector.Name != rightSelector.Name || leftSelector.Input != rightSelector.Input || leftSelector.Function != rightSelector.Function)
            { mismatchSpan = right.ValueSpan; return false; }
        }
        return true;
    }

    private static string? ReadOptionalString(JsonProperty? property, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return null;
        if (property.Value.Kind != JsonKind.String) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Leaf metadata value must be a string.", source, property.Value.Span); return null; }
        return property.Value.Text;
    }

    private static string[] ReadTags(JsonProperty? property, TranslationSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return Array.Empty<string>();
        if (property.Value.Kind != JsonKind.Array) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "$tags must be an array.", source, property.Value.Span); return Array.Empty<string>(); }
        var tags = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < property.Value.Items.Count; i++)
        {
            JsonValue item = property.Value.Items[i];
            if (item.Kind != JsonKind.String || item.Text!.Length == 0) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Tags must be non-empty strings.", source, item.Span);
            else if (!seen.Add(item.Text)) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Duplicate tag '" + item.Text + "'.", source, item.Span);
            else tags.Add(item.Text);
        }
        tags.Sort(StringComparer.Ordinal); return tags.ToArray();
    }

    private static bool HasDollarMember(JsonValue value)
    {
        for (int i = 0; i < value.Properties.Count; i++) if (value.Properties[i].Name.Length > 0 && value.Properties[i].Name[0] == '$') return true;
        return false;
    }

    private static JsonProperty? Required(JsonValue parent, string name, JsonKind kind, TranslationSource source, DiagnosticBag diagnostics)
    {
        JsonProperty? property = parent.Property(name);
        if (property is null) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Missing required member '" + name + "'.", source, parent.Span); return null; }
        if (property.Value.Kind != kind) { diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Member '" + name + "' has an invalid value kind.", source, property.Value.Span); return null; }
        return property;
    }

    private static void ValidateKnownMembers(JsonValue value, string[] allowed, TranslationSource source, DiagnosticBag diagnostics)
    {
        for (int i = 0; i < value.Properties.Count; i++)
        {
            bool found = false;
            for (int a = 0; a < allowed.Length; a++) if (value.Properties[i].Name == allowed[a]) { found = true; break; }
            if (!found) diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Unknown or misplaced member '" + value.Properties[i].Name + "'.", source, value.Properties[i].NameSpan);
        }
    }

    private static bool IsCatalogId(string value)
    {
        if (value.Length == 0 || value[0] < 'a' || value[0] > 'z') return false;
        for (int i = 1; i < value.Length; i++)
        { char ch = value[i]; if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '.' || ch == '-')) return false; }
        return true;
    }

    private static bool IsWindowsDeviceStem(string value)
    {
        int separator = value.IndexOf('.');
        string stem = (separator < 0 ? value : value.Substring(0, separator)).ToUpperInvariant();
        if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL") return true;
        return stem.Length == 4 && stem[3] >= '1' && stem[3] <= '9' &&
            ((stem[0] == 'C' && stem[1] == 'O' && stem[2] == 'M') ||
             (stem[0] == 'L' && stem[1] == 'P' && stem[2] == 'T'));
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0])) return false;
        for (int i = 1; i < value.Length; i++) if (!IsIdentifierStart(value[i]) && (value[i] < '0' || value[i] > '9')) return false;
        return true;
    }

    private static bool IsIdentifierStart(char value) => (value >= 'A' && value <= 'Z') || (value >= 'a' && value <= 'z') || value == '_';
    private static bool IsNamespace(string value)
    {
        string[] parts = value.Split('.'); if (parts.Length == 0) return false;
        for (int i = 0; i < parts.Length; i++) if (!IsIdentifier(parts[i])) return false;
        return true;
    }

    private static bool TryCanonicalizeLocale(string value, out string canonical)
    {
        canonical = string.Empty;
        if (value.Length == 0 || value[0] == '-' || value[value.Length - 1] == '-') return false;
        string[] parts = value.Split('-');
        if (parts.Length == 0 || parts[0].Length < 2 || parts[0].Length > 8 || !AllLetters(parts[0])) return false;
        var result = new StringBuilder(value.Length).Append(parts[0].ToLowerInvariant());
        bool extension = false;
        for (int i = 1; i < parts.Length; i++)
        {
            string part = parts[i];
            if (part.Length == 0 || part.Length > 8 || !AllAlphaNumeric(part)) return false;
            result.Append('-');
            if (part.Length == 1) { extension = true; result.Append(part.ToLowerInvariant()); continue; }
            if (!extension && part.Length == 4 && AllLetters(part)) result.Append(char.ToUpperInvariant(part[0])).Append(part.Substring(1).ToLowerInvariant());
            else if (!extension && ((part.Length == 2 && AllLetters(part)) || (part.Length == 3 && AllDigits(part)))) result.Append(part.ToUpperInvariant());
            else result.Append(part.ToLowerInvariant());
        }
        canonical = result.ToString(); return true;
    }

    private static bool AllLetters(string value) { for (int i = 0; i < value.Length; i++) if (!((value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z'))) return false; return true; }
    private static bool AllDigits(string value) { for (int i = 0; i < value.Length; i++) if (value[i] < '0' || value[i] > '9') return false; return true; }
    private static bool AllAlphaNumeric(string value) { for (int i = 0; i < value.Length; i++) if (!((value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z') || (value[i] >= '0' && value[i] <= '9'))) return false; return true; }

    private static bool TryArgumentType(string value, out TranslationArgumentType type, out string format)
    {
        switch (value)
        {
            case "string": type = TranslationArgumentType.String; format = "none"; return true;
            case "int": type = TranslationArgumentType.Int; format = "plain"; return true;
            case "number": type = TranslationArgumentType.Number; format = "plain"; return true;
            case "bool": type = TranslationArgumentType.Boolean; format = "lower"; return true;
            case "date": type = TranslationArgumentType.Date; format = "medium"; return true;
            case "time": type = TranslationArgumentType.Time; format = "short"; return true;
            case "datetime": type = TranslationArgumentType.DateTime; format = "medium"; return true;
            case "guid": type = TranslationArgumentType.Guid; format = "d"; return true;
            default: type = TranslationArgumentType.String; format = "none"; return false;
        }
    }

    private static bool IsAllowedFormat(TranslationArgumentType type, string format)
    {
        switch (type)
        {
            case TranslationArgumentType.String: return format == "none";
            case TranslationArgumentType.Int: return format == "plain" || format == "grouped";
            case TranslationArgumentType.Number:
                if (format == "plain" || format == "grouped") return true;
                if (format.StartsWith("fixed", StringComparison.Ordinal) && format.Length == 6) return format[5] >= '0' && format[5] <= '6';
                return format.StartsWith("percent", StringComparison.Ordinal) && format.Length == 8 && format[7] >= '0' && format[7] <= '4';
            case TranslationArgumentType.Boolean: return format == "lower";
            case TranslationArgumentType.Date: return format == "iso" || format == "short" || format == "medium" || format == "long";
            case TranslationArgumentType.Time: return format == "iso" || format == "short" || format == "medium";
            case TranslationArgumentType.DateTime: return format == "iso" || format == "short" || format == "medium" || format == "long";
            case TranslationArgumentType.Guid: return format == "d" || format == "n";
            default: return false;
        }
    }

    private static string ArgumentTypeName(TranslationArgumentType type)
    {
        switch (type)
        {
            case TranslationArgumentType.String: return "string";
            case TranslationArgumentType.Int: return "int";
            case TranslationArgumentType.Number: return "number";
            case TranslationArgumentType.Boolean: return "bool";
            case TranslationArgumentType.Date: return "date";
            case TranslationArgumentType.Time: return "time";
            case TranslationArgumentType.DateTime: return "datetime";
            case TranslationArgumentType.Guid: return "guid";
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    private static bool IsPathPrefix(string possiblePrefix, string value) => value.Length > possiblePrefix.Length && value.StartsWith(possiblePrefix, StringComparison.Ordinal) && value[possiblePrefix.Length] == '.';
    private static string[] Keys(Dictionary<string, ResourceModel> dictionary) { var keys = new List<string>(dictionary.Keys); keys.Sort(StringComparer.Ordinal); return keys.ToArray(); }
    private static string[] Sorted(HashSet<string> values) { var result = new List<string>(values); result.Sort(StringComparer.Ordinal); return result.ToArray(); }
    private static List<KeyValuePair<string, TValue>> SortedPairs<TValue>(Dictionary<string, TValue> values)
    { var result = new List<KeyValuePair<string, TValue>>(values); result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key)); return result; }
}
