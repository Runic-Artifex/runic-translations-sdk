using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WebUIToolkit.TextResources.Compiler;

public static class TextResourceCompiler
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

    /// <summary>Compiles manifest and resource document sources using default limits.</summary>
    /// <remarks>Inputs and outputs are deterministic and no environment state is consulted.</remarks>
    public static TextResourceCompilation Compile(
        IEnumerable<TextResourceSource> manifests,
        IEnumerable<TextResourceSource> documents,
        TextResourceCompilerOptions? options = null)
        => Compile(manifests, documents, options, CancellationToken.None);

    /// <summary>Compiles manifest and resource document sources with cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static TextResourceCompilation Compile(
        IEnumerable<TextResourceSource> manifests,
        IEnumerable<TextResourceSource> documents,
        CancellationToken cancellationToken)
        => Compile(manifests, documents, null, cancellationToken);

    /// <summary>Compiles manifest and resource document sources with explicit limits and cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static TextResourceCompilation Compile(
        IEnumerable<TextResourceSource> manifests,
        IEnumerable<TextResourceSource> documents,
        TextResourceCompilerOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(documents);
        cancellationToken.ThrowIfCancellationRequested();
        options ??= new TextResourceCompilerOptions();
        var diagnostics = new DiagnosticBag();
        TextResourceSource[] manifestSources = Materialize(manifests);
        TextResourceSource[] documentSources = Materialize(documents);
        if (RejectDuplicateSourcePaths(manifestSources, documentSources, diagnostics))
            return new TextResourceCompilation(Array.Empty<CompiledTextCatalog>(), diagnostics.ToSortedArray());
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
                diagnostics.Add("WUTTEXT0002", TextResourceDiagnosticSeverity.Error,
                    "Catalog '" + manifest.Id + "' has more than one manifest.", manifest.Source, manifest.IdSpan);
        }
        ValidateGeneratedRootIdentities(manifestModels, diagnostics);

        var docsByCatalog = new Dictionary<string, List<DocumentModel>>(StringComparer.Ordinal);
        for (int i = 0; i < documentModels.Count; i++)
        {
            DocumentModel document = documentModels[i];
            if (!manifestsById.ContainsKey(document.Catalog))
                diagnostics.Add("WUTTEXT0002", TextResourceDiagnosticSeverity.Error,
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
                diagnostics.Add("WUTTEXT0002", TextResourceDiagnosticSeverity.Error,
                    "Catalog '" + pair.Key + "' has no resource documents.", pair.Value.Source, pair.Value.IdSpan);
                continue;
            }
            CompiledTextCatalog? catalog = CompileCatalog(pair.Value, catalogDocuments, diagnostics, options, cancellationToken);
            if (catalog is not null) catalogs.Add(catalog);
        }

        return new TextResourceCompilation(catalogs.ToArray(), diagnostics.ToSortedArray());
    }

    private static TextResourceSource[] Materialize(IEnumerable<TextResourceSource> sources)
    {
        var result = new List<TextResourceSource>();
        foreach (TextResourceSource source in sources)
        {
            if (source is null) throw new ArgumentException("A source collection contains null.", nameof(sources));
            result.Add(source);
        }
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));
        return result.ToArray();
    }

    private static bool RejectDuplicateSourcePaths(
        TextResourceSource[] manifests,
        TextResourceSource[] documents,
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
            TextResourceSource representative = Find(manifests, duplicates[i]) ?? Find(documents, duplicates[i])!;
            diagnostics.Add(
                "WUTTEXT0002",
                TextResourceDiagnosticSeverity.Error,
                "Normalized source path '" + duplicates[i] + "' is supplied more than once.",
                representative,
                new ByteSpan(0, 0));
        }

        return duplicates.Count != 0;

        void Count(TextResourceSource[] sources)
        {
            for (int i = 0; i < sources.Length; i++)
            {
                if (counts.TryGetValue(sources[i].Path, out int count)) counts[sources[i].Path] = count + 1;
                else counts.Add(sources[i].Path, 1);
            }
        }

        static TextResourceSource? Find(TextResourceSource[] sources, string path)
        {
            for (int i = 0; i < sources.Length; i++)
                if (string.Equals(sources[i].Path, path, StringComparison.Ordinal)) return sources[i];
            return null;
        }
    }

    private static ManifestModel? ReadManifest(ParsedJson parsed, DiagnosticBag diagnostics, TextResourceCompilerOptions options)
    {
        JsonValue root = parsed.Root!;
        if (root.Kind != JsonKind.Object)
        {
            diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Catalog manifest root must be an object.", parsed.Source, root.Span);
            return null;
        }
        ValidateKnownMembers(root, ManifestMembers, parsed.Source, diagnostics);
        ValidateSchema(root, parsed.Source, diagnostics);
        var model = new ManifestModel(parsed.Source);
        JsonProperty? catalog = Required(root, "catalog", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? code = Required(root, "code", JsonKind.Object, parsed.Source, diagnostics);
        JsonProperty? defaultLocale = Required(root, "defaultLocale", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? locales = Required(root, "locales", JsonKind.Array, parsed.Source, diagnostics);
        JsonProperty? layers = Required(root, "layers", JsonKind.Array, parsed.Source, diagnostics);
        if (catalog is not null)
        {
            model.Id = catalog.Value.Text!; model.IdSpan = catalog.Value.Span;
            if (IsWindowsDeviceStem(model.Id))
                diagnostics.Add("WUTTEXT0018", TextResourceDiagnosticSeverity.Error, "Catalog ID '" + model.Id + "' produces a Windows-reserved generated filename stem.", parsed.Source, catalog.Value.Span);
            else if (!IsCatalogId(model.Id))
                diagnostics.Add("WUTTEXT0006", TextResourceDiagnosticSeverity.Error, "Catalog ID must use lowercase ASCII letters, digits, dots, or hyphens.", parsed.Source, catalog.Value.Span);
        }
        if (code is not null) ReadCode(code.Value, model, parsed.Source, diagnostics);
        if (defaultLocale is not null)
        {
            model.DefaultLocaleSpan = defaultLocale.Value.Span;
            if (!TryCanonicalizeLocale(defaultLocale.Value.Text!, out string canonical))
                diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Invalid default locale '" + defaultLocale.Value.Text + "'.", parsed.Source, defaultLocale.Value.Span);
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

    private static bool ValidateSchema(JsonValue root, TextResourceSource source, DiagnosticBag diagnostics)
    {
        bool valid = true;
        JsonProperty? schemaHint = root.Property("$schema");
        if (schemaHint is not null)
        {
            diagnostics.Add("WUTTEXT0003", TextResourceDiagnosticSeverity.Error,
                "No canonical $schema URI is registered; omit $schema for schema version 1.", source, schemaHint.Value.Span);
            valid = false;
        }
        JsonProperty? version = root.Property("schemaVersion");
        if (version is null)
        {
            diagnostics.Add("WUTTEXT0003", TextResourceDiagnosticSeverity.Error, "Missing required schemaVersion 1.", source, root.Span);
            return false;
        }
        if (version.Value.Kind != JsonKind.Number || !string.Equals(version.Value.Text, "1", StringComparison.Ordinal))
        {
            diagnostics.Add("WUTTEXT0003", TextResourceDiagnosticSeverity.Error, "Unsupported schemaVersion; expected integer 1.", source, version.Value.Span);
            valid = false;
        }
        return valid;
    }

    private static void ReadCode(JsonValue value, ManifestModel model, TextResourceSource source, DiagnosticBag diagnostics)
    {
        ValidateKnownMembers(value, CodeMembers, source, diagnostics);
        JsonProperty? ns = Required(value, "namespace", JsonKind.String, source, diagnostics);
        JsonProperty? className = Required(value, "className", JsonKind.String, source, diagnostics);
        JsonProperty? visibility = value.Property("visibility");
        if (ns is not null)
        {
            model.CodeNamespace = ns.Value.Text!;
            if (!IsNamespace(model.CodeNamespace)) diagnostics.Add("WUTTEXT0006", TextResourceDiagnosticSeverity.Error, "Invalid C# namespace '" + model.CodeNamespace + "'.", source, ns.Value.Span);
        }
        if (className is not null)
        {
            model.ClassName = className.Value.Text!;
            model.ClassNameSpan = className.Value.Span;
            if (IsWindowsDeviceStem(model.ClassName))
                diagnostics.Add("WUTTEXT0018", TextResourceDiagnosticSeverity.Error, "Generated class name '" + model.ClassName + "' produces a Windows-reserved filename stem.", source, className.Value.Span);
            else if (!IsIdentifier(model.ClassName))
                diagnostics.Add("WUTTEXT0006", TextResourceDiagnosticSeverity.Error, "Invalid generated class name '" + model.ClassName + "'.", source, className.Value.Span);
        }
        if (visibility is not null)
        {
            if (visibility.Value.Kind != JsonKind.String || (visibility.Value.Text != "public" && visibility.Value.Text != "internal"))
                diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "visibility must be 'public' or 'internal'.", source, visibility.Value.Span);
            else model.Visibility = visibility.Value.Text == "internal" ? TextResourceVisibility.Internal : TextResourceVisibility.Public;
        }
    }

    private static void ReadLocales(JsonValue value, ManifestModel model, TextResourceSource source, DiagnosticBag diagnostics, TextResourceCompilerOptions options)
    {
        if (value.Items.Count == 0) diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "locales must not be empty.", source, value.Span);
        if (value.Items.Count > options.MaximumLocalesPerCatalog)
            diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error, "Locale count exceeds the configured limit.", source, value.Span);
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < value.Items.Count; i++)
        {
            JsonValue item = value.Items[i];
            if (item.Kind != JsonKind.Object)
            {
                diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Each locale declaration must be an object.", source, item.Span); continue;
            }
            ValidateKnownMembers(item, LocaleMembers, source, diagnostics);
            JsonProperty? tagProperty = Required(item, "tag", JsonKind.String, source, diagnostics);
            JsonProperty? fallbackProperty = item.Property("fallback");
            if (tagProperty is null) continue;
            if (!TryCanonicalizeLocale(tagProperty.Value.Text!, out string tag))
            {
                diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Invalid locale tag '" + tagProperty.Value.Text + "'.", source, tagProperty.Value.Span); continue;
            }
            if (!tags.Add(tag)) diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Duplicate locale '" + tag + "'.", source, tagProperty.Value.Span);
            string? fallback = null;
            ByteSpan fallbackSpan = item.Span;
            if (fallbackProperty is not null)
            {
                fallbackSpan = fallbackProperty.Value.Span;
                if (fallbackProperty.Value.Kind != JsonKind.String || !TryCanonicalizeLocale(fallbackProperty.Value.Text!, out fallback))
                    diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Invalid fallback locale.", source, fallbackProperty.Value.Span);
            }
            model.Locales.Add(new LocaleModel(tag, fallback, tagProperty.Value.Span, fallbackSpan));
        }
    }

    private static void ReadLayers(JsonValue value, ManifestModel model, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (value.Items.Count == 0) diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "layers must not be empty.", source, value.Span);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var priorities = new HashSet<int>();
        for (int i = 0; i < value.Items.Count; i++)
        {
            JsonValue item = value.Items[i];
            if (item.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Each layer declaration must be an object.", source, item.Span); continue; }
            ValidateKnownMembers(item, LayerMembers, source, diagnostics);
            JsonProperty? name = Required(item, "name", JsonKind.String, source, diagnostics);
            JsonProperty? priority = Required(item, "priority", JsonKind.Number, source, diagnostics);
            if (name is null || priority is null) continue;
            if (!IsCatalogId(name.Value.Text!)) diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Invalid layer name '" + name.Value.Text + "'.", source, name.Value.Span);
            if (!int.TryParse(priority.Value.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int priorityValue))
            { diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Layer priority must be a signed 32-bit integer.", source, priority.Value.Span); continue; }
            if (!names.Add(name.Value.Text!)) diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Duplicate layer name '" + name.Value.Text + "'.", source, name.Value.Span);
            if (!priorities.Add(priorityValue)) diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Duplicate layer priority " + priorityValue.ToString(CultureInfo.InvariantCulture) + ".", source, priority.Value.Span);
            model.Layers.Add(new LayerModel(name.Value.Text!, priorityValue, name.Value.Span, priority.Value.Span));
        }
        model.Layers.Sort((left, right) => left.Priority != right.Priority ? left.Priority.CompareTo(right.Priority) : StringComparer.Ordinal.Compare(left.Name, right.Name));
    }

    private static void ReadValidation(JsonProperty property, ManifestModel model, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "validation must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, ValidationMembers, source, diagnostics);
        model.Completeness = ReadPolicy(property.Value.Property("translationCompleteness"), model.Completeness, source, diagnostics);
        model.ExtraKeys = ReadPolicy(property.Value.Property("extraLocaleKeys"), model.ExtraKeys, source, diagnostics);
        model.EmptyValues = ReadPolicy(property.Value.Property("emptyValues"), model.EmptyValues, source, diagnostics);
    }

    private static TextResourcePolicy ReadPolicy(JsonProperty? property, TextResourcePolicy defaultValue, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return defaultValue;
        if (property.Value.Kind != JsonKind.String) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Validation policy must be allow, warning, or error.", source, property.Value.Span); return defaultValue; }
        switch (property.Value.Text)
        {
            case "allow": return TextResourcePolicy.Allow;
            case "warning": return TextResourcePolicy.Warning;
            case "error": return TextResourcePolicy.Error;
            default: diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Unknown validation policy '" + property.Value.Text + "'.", source, property.Value.Span); return defaultValue;
        }
    }

    private static void ReadRuntime(JsonProperty property, ManifestModel model, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "runtime must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, RuntimeMembers, source, diagnostics);
        JsonProperty? unsupported = property.Value.Property("unsupportedLocale");
        if (unsupported is not null && unsupported.Value.Kind == JsonKind.String)
        {
            switch (unsupported.Value.Text)
            {
                case "exact": model.UnsupportedLocale = TextResourceUnsupportedLocalePolicy.Exact; break;
                case "parentsThenDefault": model.UnsupportedLocale = TextResourceUnsupportedLocalePolicy.ParentsThenDefault; break;
                case "default": model.UnsupportedLocale = TextResourceUnsupportedLocalePolicy.Default; break;
                default: diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Unknown unsupportedLocale policy.", source, unsupported.Value.Span); break;
            }
        }
        else if (unsupported is not null) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "unsupportedLocale must be a string.", source, unsupported.Value.Span);
        JsonProperty? missing = property.Value.Property("missingKey");
        if (missing is not null && missing.Value.Kind == JsonKind.String)
        {
            switch (missing.Value.Text)
            {
                case "throw": model.MissingKey = TextResourceMissingKeyPolicy.Throw; break;
                case "returnKey": model.MissingKey = TextResourceMissingKeyPolicy.ReturnKey; break;
                case "returnMarker": model.MissingKey = TextResourceMissingKeyPolicy.ReturnMarker; break;
                default: diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Unknown missingKey policy.", source, missing.Value.Span); break;
            }
        }
        else if (missing is not null) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "missingKey must be a string.", source, missing.Value.Span);
    }

    private static void ValidateOutputs(JsonProperty property, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "outputs must be an object.", source, property.Value.Span); return; }
        ValidateKnownMembers(property.Value, OutputMembers, source, diagnostics);
        for (int i = 0; i < property.Value.Properties.Count; i++)
        {
            JsonProperty output = property.Value.Properties[i];
            if (output.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Output configuration must be an object.", source, output.Value.Span); continue; }
            string[] allowed = output.Name == "typescript" ? TypeScriptOutputMembers : TemplateOutputMembers;
            ValidateKnownMembers(output.Value, allowed, source, diagnostics);
            JsonProperty? enabled = output.Value.Property("enabled");
            if (enabled is not null && enabled.Value.Kind != JsonKind.True && enabled.Value.Kind != JsonKind.False)
                diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "enabled must be boolean.", source, enabled.Value.Span);
            JsonProperty? module = output.Value.Property("moduleName");
            if (module is not null && module.Value.Kind != JsonKind.String)
                diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "moduleName must be a string.", source, module.Value.Span);
        }
    }

    private static void ValidateFallbackGraph(ManifestModel model, DiagnosticBag diagnostics)
    {
        var locales = new Dictionary<string, LocaleModel>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < model.Locales.Count; i++) if (!locales.ContainsKey(model.Locales[i].Tag)) locales.Add(model.Locales[i].Tag, model.Locales[i]);
        if (model.DefaultLocale.Length > 0 && !locales.ContainsKey(model.DefaultLocale))
            diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "defaultLocale is not declared in locales.", model.Source, model.DefaultLocaleSpan);
        for (int i = 0; i < model.Locales.Count; i++)
        {
            LocaleModel locale = model.Locales[i];
            if (string.Equals(locale.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) && locale.Fallback is not null)
                diagnostics.Add("WUTTEXT0012", TextResourceDiagnosticSeverity.Error, "The default locale must not declare a fallback.", model.Source, locale.FallbackSpan);
            if (locale.Fallback is not null && !locales.ContainsKey(locale.Fallback))
                diagnostics.Add("WUTTEXT0012", TextResourceDiagnosticSeverity.Error, "Fallback locale '" + locale.Fallback + "' is not declared.", model.Source, locale.FallbackSpan);
            if (!string.Equals(locale.Tag, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) && locale.Fallback is null)
                diagnostics.Add("WUTTEXT0013", TextResourceDiagnosticSeverity.Error, "Locale '" + locale.Tag + "' has no fallback path to the default locale.", model.Source, locale.Span);
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
                    diagnostics.Add("WUTTEXT0013", TextResourceDiagnosticSeverity.Error, "Fallback cycle closes at locale '" + next.Tag + "'.", model.Source, current.FallbackSpan);
                    for (int p = 0; p < pathItems.Count; p++) fullyChecked.Add(pathItems[p]);
                    break;
                }
                current = next;
            }
            for (int p = 0; p < pathItems.Count; p++) fullyChecked.Add(pathItems[p]);
        }
    }

    private static DocumentModel? ReadDocument(ParsedJson parsed, DiagnosticBag diagnostics, TextResourceCompilerOptions options)
    {
        JsonValue root = parsed.Root!;
        if (root.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Resource document root must be an object.", parsed.Source, root.Span); return null; }
        ValidateKnownMembers(root, DocumentMembers, parsed.Source, diagnostics);
        ValidateSchema(root, parsed.Source, diagnostics);
        JsonProperty? catalog = Required(root, "catalog", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? locale = Required(root, "locale", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? layer = Required(root, "layer", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? resources = Required(root, "resources", JsonKind.Object, parsed.Source, diagnostics);
        if (catalog is null || locale is null || layer is null || resources is null) return null;
        var model = new DocumentModel(parsed.Source)
        {
            Catalog = catalog.Value.Text!,
            CatalogSpan = catalog.Value.Span,
            Layer = layer.Value.Text!,
            LayerSpan = layer.Value.Span,
            LocaleSpan = locale.Value.Span,
        };
        // The matching manifest owns generated-name validation. Preserve an
        // uppercase device spelling here so it associates with that manifest
        // and produces one focused WUTTEXT0018 instead of a document cascade.
        if (!IsCatalogId(model.Catalog) && !IsWindowsDeviceStem(model.Catalog))
            diagnostics.Add("WUTTEXT0006", TextResourceDiagnosticSeverity.Error, "Catalog ID must use lowercase ASCII letters, digits, dots, or hyphens.", parsed.Source, catalog.Value.Span);
        if (!TryCanonicalizeLocale(locale.Value.Text!, out string canonicalLocale))
            diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Invalid locale tag '" + locale.Value.Text + "'.", parsed.Source, locale.Value.Span);
        else model.Locale = canonicalLocale;
        if (!IsCatalogId(model.Layer)) diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Invalid layer name '" + model.Layer + "'.", parsed.Source, layer.Value.Span);
        FlattenResources(resources.Value, string.Empty, default, model, diagnostics, options, 0);
        return model;
    }

    private static void FlattenResources(JsonValue group, string prefix, ByteSpan rootSpan, DocumentModel document, DiagnosticBag diagnostics, TextResourceCompilerOptions options, int depth)
    {
        for (int i = 0; i < group.Properties.Count; i++)
        {
            JsonProperty property = group.Properties[i];
            string key = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
            ByteSpan pathSpan = prefix.Length == 0 ? property.NameSpan : rootSpan;
            if (depth + 1 > options.MaximumDepth)
            {
                diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error, "Resource tree exceeds the configured depth limit.", document.Source, property.NameSpan);
                document.HadLimitError = true;
                continue;
            }
            if (!IsIdentifier(property.Name) || property.Name[0] == '$')
            {
                string id = property.Name.Length > 0 && property.Name[0] == '$' ? "WUTTEXT0019" : "WUTTEXT0006";
                diagnostics.Add(id, TextResourceDiagnosticSeverity.Error, "Invalid resource key segment '" + property.Name + "'.", document.Source, property.NameSpan); continue;
            }
            if (property.Value.Kind == JsonKind.String)
            {
                AddLeaf(document, diagnostics, options, key, property.NameSpan, pathSpan, property.Value.Span, property.Value.Text!, null, null, null, Array.Empty<string>(), Array.Empty<PlaceholderModel>());
                continue;
            }
            if (property.Value.Kind != JsonKind.Object)
            {
                diagnostics.Add("WUTTEXT0008", TextResourceDiagnosticSeverity.Error, "Resource '" + key + "' must be a string, group, or metadata leaf.", document.Source, property.Value.Span); continue;
            }
            JsonProperty? value = property.Value.Property("$value");
            bool hasMetadata = HasDollarMember(property.Value);
            if (value is null)
            {
                if (hasMetadata) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Metadata leaf '" + key + "' is missing $value.", document.Source, property.Value.Span);
                else FlattenResources(property.Value, key, pathSpan, document, diagnostics, options, depth + 1);
                continue;
            }
            ReadMetadataLeaf(property, key, pathSpan, document, diagnostics, options);
        }
    }

    private static void ReadMetadataLeaf(JsonProperty property, string key, ByteSpan pathSpan, DocumentModel document, DiagnosticBag diagnostics, TextResourceCompilerOptions options)
    {
        ValidateKnownMembers(property.Value, LeafMembers, document.Source, diagnostics);
        for (int i = 0; i < property.Value.Properties.Count; i++)
            if (property.Value.Properties[i].Name.Length == 0 || property.Value.Properties[i].Name[0] != '$')
                diagnostics.Add("WUTTEXT0008", TextResourceDiagnosticSeverity.Error, "A metadata leaf cannot also contain child resources.", document.Source, property.Value.Properties[i].NameSpan);
        JsonProperty? value = Required(property.Value, "$value", JsonKind.String, document.Source, diagnostics);
        if (value is null) return;
        string? description = ReadOptionalString(property.Value.Property("$description"), document.Source, diagnostics);
        string? since = ReadOptionalString(property.Value.Property("$since"), document.Source, diagnostics);
        string? deprecated = ReadOptionalString(property.Value.Property("$deprecated"), document.Source, diagnostics);
        string[] tags = ReadTags(property.Value.Property("$tags"), document.Source, diagnostics);
        PlaceholderModel[] placeholders = ReadPlaceholders(property.Value.Property("$placeholders"), document.Source, diagnostics, options);
        AddLeaf(document, diagnostics, options, key, property.NameSpan, pathSpan, value.Value.Span, value.Value.Text!, description, since, deprecated, tags, placeholders);
    }

    private static void AddLeaf(DocumentModel document, DiagnosticBag diagnostics, TextResourceCompilerOptions options, string key, ByteSpan keySpan, ByteSpan pathSpan, ByteSpan valueSpan,
        string pattern, string? description, string? since, string? deprecated, string[] tags, PlaceholderModel[] placeholders)
    {
        if (StrictJsonParser.StrictUtf8.GetByteCount(pattern) > options.MaximumValueBytes)
            diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error, "Resource value exceeds the configured byte limit.", document.Source, valueSpan);
        HashSet<string>? patternNames = ParsePattern(pattern, document.Source, valueSpan, diagnostics);
        if (patternNames is not null)
        {
            var declared = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < placeholders.Length; i++) declared.Add(placeholders[i].Name);
            foreach (string name in Sorted(patternNames))
                if (!declared.Contains(name)) diagnostics.Add("WUTTEXT0015", TextResourceDiagnosticSeverity.Error, "Placeholder '" + name + "' is used but not declared.", document.Source, valueSpan);
            for (int i = 0; i < placeholders.Length; i++)
                if (!patternNames.Contains(placeholders[i].Name)) diagnostics.Add("WUTTEXT0015", TextResourceDiagnosticSeverity.Error, "Placeholder '" + placeholders[i].Name + "' is declared but not used.", document.Source, placeholders[i].Span);
        }
        document.Resources.Add(new ResourceModel(key, pattern, description, since, deprecated, tags, placeholders, document.Source, keySpan, pathSpan, valueSpan));
    }

    private static HashSet<string>? ParsePattern(string pattern, TextResourceSource source, ByteSpan span, DiagnosticBag diagnostics)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < pattern.Length; i++)
        {
            char ch = pattern[i];
            if (ch == '{')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '{') { i++; continue; }
                int close = pattern.IndexOf('}', i + 1);
                if (close < 0) { diagnostics.Add("WUTTEXT0014", TextResourceDiagnosticSeverity.Error, "Message pattern contains an unmatched '{'.", source, span); return null; }
                string name = pattern.Substring(i + 1, close - i - 1);
                if (!IsIdentifier(name)) { diagnostics.Add("WUTTEXT0014", TextResourceDiagnosticSeverity.Error, "Message pattern contains an invalid placeholder.", source, span); return null; }
                names.Add(name); i = close;
            }
            else if (ch == '}')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '}') { i++; continue; }
                diagnostics.Add("WUTTEXT0014", TextResourceDiagnosticSeverity.Error, "Message pattern contains an unmatched '}'.", source, span); return null;
            }
        }
        return names;
    }

    private static PlaceholderModel[] ReadPlaceholders(JsonProperty? property, TextResourceSource source, DiagnosticBag diagnostics, TextResourceCompilerOptions options)
    {
        if (property is null) return Array.Empty<PlaceholderModel>();
        if (property.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0015", TextResourceDiagnosticSeverity.Error, "$placeholders must be an object.", source, property.Value.Span); return Array.Empty<PlaceholderModel>(); }
        if (property.Value.Properties.Count > options.MaximumPlaceholdersPerValue)
            diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error, "Placeholder count exceeds the configured limit.", source, property.Value.Span);
        var result = new List<PlaceholderModel>();
        for (int i = 0; i < property.Value.Properties.Count; i++)
        {
            JsonProperty descriptor = property.Value.Properties[i];
            if (!IsIdentifier(descriptor.Name)) { diagnostics.Add("WUTTEXT0015", TextResourceDiagnosticSeverity.Error, "Invalid placeholder name '" + descriptor.Name + "'.", source, descriptor.NameSpan); continue; }
            if (descriptor.Value.Kind != JsonKind.Object) { diagnostics.Add("WUTTEXT0017", TextResourceDiagnosticSeverity.Error, "Placeholder descriptor must be an object.", source, descriptor.Value.Span); continue; }
            ValidateKnownMembers(descriptor.Value, PlaceholderMembers, source, diagnostics);
            JsonProperty? type = Required(descriptor.Value, "type", JsonKind.String, source, diagnostics);
            JsonProperty? format = descriptor.Value.Property("format");
            if (type is null) continue;
            if (!TryArgumentType(type.Value.Text!, out TextResourceArgumentType argumentType, out string defaultFormat))
            {
                diagnostics.Add("WUTTEXT0017", TextResourceDiagnosticSeverity.Error, "Unknown placeholder type '" + type.Value.Text + "'.", source, type.Value.Span);
                result.Add(new PlaceholderModel(descriptor.Name, TextResourceArgumentType.String, "none", descriptor.NameSpan, type.Value.Span, type.Value.Span));
                continue;
            }
            string selectedFormat = defaultFormat;
            if (format is not null)
            {
                if (format.Value.Kind != JsonKind.String || !IsAllowedFormat(argumentType, format.Value.Text!))
                {
                    diagnostics.Add("WUTTEXT0017", TextResourceDiagnosticSeverity.Error, "Invalid format for placeholder type '" + type.Value.Text + "'.", source, format.Value.Span);
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

    private static CompiledTextCatalog? CompileCatalog(ManifestModel manifest, List<DocumentModel> documents, DiagnosticBag diagnostics, TextResourceCompilerOptions options, CancellationToken cancellationToken)
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
            if (!localeMap.ContainsKey(document.Locale)) { diagnostics.Add("WUTTEXT0004", TextResourceDiagnosticSeverity.Error, "Document locale '" + document.Locale + "' is not declared.", document.Source, document.LocaleSpan); continue; }
            if (!layerMap.ContainsKey(document.Layer)) { diagnostics.Add("WUTTEXT0005", TextResourceDiagnosticSeverity.Error, "Document layer '" + document.Layer + "' is not declared.", document.Source, document.LayerSpan); continue; }
            if (!buckets.TryGetValue(document.Locale, out Dictionary<string, Dictionary<string, ResourceModel>>? byLayer)) { byLayer = new Dictionary<string, Dictionary<string, ResourceModel>>(StringComparer.Ordinal); buckets.Add(document.Locale, byLayer); }
            if (!byLayer.TryGetValue(document.Layer, out Dictionary<string, ResourceModel>? resources)) { resources = new Dictionary<string, ResourceModel>(StringComparer.Ordinal); byLayer.Add(document.Layer, resources); }
            for (int r = 0; r < document.Resources.Count; r++)
            {
                ResourceModel resource = document.Resources[r];
                if (!resources.TryAdd(resource.Key, resource)) diagnostics.Add("WUTTEXT0007", TextResourceDiagnosticSeverity.Error, "Duplicate key '" + resource.Key + "' in locale '" + document.Locale + "' and layer '" + document.Layer + "'.", resource.Source, resource.KeySpan);
                foreach (KeyValuePair<string, ResourceModel> existing in allPathKinds)
                    if (IsPathPrefix(existing.Key, resource.Key) || IsPathPrefix(resource.Key, existing.Key))
                    { diagnostics.Add("WUTTEXT0008", TextResourceDiagnosticSeverity.Error, "Resource path '" + resource.Key + "' conflicts with leaf '" + existing.Key + "'.", resource.Source, resource.PathSpan); break; }
                allPathKinds.TryAdd(resource.Key, resource);
                ValidateIdentifierCollision(manifest, resource, diagnostics);
            }
        }
        if (allPathKinds.Count > options.MaximumKeysPerCatalog)
            diagnostics.Add("WUTTEXT0022", TextResourceDiagnosticSeverity.Error, "Catalog key count exceeds the configured limit.", manifest.Source, manifest.IdSpan);
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
        if (!directByLocale.TryGetValue(manifest.DefaultLocale, out Dictionary<string, ResourceModel>? canonical) || canonical.Count == 0)
        {
            bool defaultDocumentHadLimitError = false;
            for (int i = 0; i < documents.Count; i++)
                if (string.Equals(documents[i].Locale, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase) && documents[i].HadLimitError)
                    defaultDocumentHadLimitError = true;
            if (!defaultDocumentHadLimitError)
                diagnostics.Add("WUTTEXT0009", TextResourceDiagnosticSeverity.Error, "The effective default locale defines no canonical keys.", manifest.Source, manifest.DefaultLocaleSpan);
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
            bool hasValidFallback = locale.Fallback is null || localeMap.ContainsKey(locale.Fallback);
            if (!string.Equals(locale.Tag, manifest.DefaultLocale, StringComparison.OrdinalIgnoreCase))
            {
                if (hasValidFallback)
                    for (int key = 0; key < canonicalKeys.Length; key++)
                        if (!direct.ContainsKey(canonicalKeys[key])) AddPolicyDiagnostic("WUTTEXT0010", manifest.Completeness, "Locale '" + locale.Tag + "' lacks direct translation for key '" + canonicalKeys[key] + "'.", manifest.Source, locale.Span, diagnostics);
                foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                    if (!canonical.ContainsKey(pair.Key)) AddPolicyDiagnostic("WUTTEXT0011", manifest.ExtraKeys, "Locale '" + locale.Tag + "' defines non-canonical key '" + pair.Key + "'.", pair.Value.Source, pair.Value.KeySpan, diagnostics);
                foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                    if (canonical.TryGetValue(pair.Key, out ResourceModel? defaultResource) && !SameContract(defaultResource, pair.Value, out ByteSpan mismatchSpan))
                        diagnostics.Add("WUTTEXT0016", TextResourceDiagnosticSeverity.Error, "Translation placeholder contract for key '" + pair.Key + "' differs from the default locale.", pair.Value.Source, mismatchSpan);
            }
            foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(direct))
                if (pair.Value.Pattern.Length == 0) AddPolicyDiagnostic("WUTTEXT0021", manifest.EmptyValues, "Resource '" + pair.Key + "' has an empty value.", pair.Value.Source, pair.Value.ValueSpan, diagnostics);
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
        IReadOnlyList<CompiledTextResource> canonicalResources = CompileResources(canonical, ids);
        string fingerprint = Fingerprint(manifest.Id, canonicalResources);
        var layers = new List<CompiledTextLayer>();
        for (int i = 0; i < manifest.Layers.Count; i++) layers.Add(new CompiledTextLayer(manifest.Layers[i].Name, manifest.Layers[i].Priority));
        return new CompiledTextCatalog(manifest.Id, manifest.CodeNamespace, manifest.ClassName, manifest.Visibility, manifest.DefaultLocale,
            layers.ToArray(), compiledLocales.ToArray(), canonicalResources, manifest.UnsupportedLocale, manifest.MissingKey, fingerprint);
    }

    private static CompiledTextResource[] CompileResources(Dictionary<string, ResourceModel> resources, Dictionary<string, int> ids)
    {
        var result = new List<CompiledTextResource>();
        foreach (KeyValuePair<string, ResourceModel> pair in SortedPairs(resources))
        {
            ResourceModel resource = pair.Value;
            var placeholders = new List<CompiledTextPlaceholder>();
            for (int i = 0; i < resource.Placeholders.Length; i++) placeholders.Add(new CompiledTextPlaceholder(resource.Placeholders[i].Name, resource.Placeholders[i].Type, resource.Placeholders[i].Format));
            result.Add(new CompiledTextResource(ids.TryGetValue(pair.Key, out int id) ? id : -1, pair.Key, resource.Pattern, resource.Description,
                resource.Since, resource.DeprecatedReason, (string[])resource.Tags.Clone(), placeholders.ToArray(), DiagnosticBag.Location(resource.Source, resource.KeySpan)));
        }
        return result.ToArray();
    }

    private static string Fingerprint(string catalog, IReadOnlyList<CompiledTextResource> resources)
    {
        var builder = new StringBuilder();
        builder.Append("{\"catalog\":").Append(JsonQuote(catalog)).Append(",\"messageGrammarVersion\":1,\"resources\":[");
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
            diagnostics.Add("WUTTEXT0018", TextResourceDiagnosticSeverity.Error, "Generated identifier '" + segments[0] + "' collides with enclosing class '" + manifest.ClassName + "'.", resource.Source, resource.KeySpan);
        for (int i = 1; i < segments.Length; i++)
            if (string.Equals(segments[i], segments[i - 1], StringComparison.Ordinal))
            { diagnostics.Add("WUTTEXT0018", TextResourceDiagnosticSeverity.Error, "Generated identifier '" + segments[i] + "' collides with its enclosing type.", resource.Source, resource.KeySpan); break; }
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
                    "WUTTEXT0018",
                    TextResourceDiagnosticSeverity.Error,
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
                    "WUTTEXT0018",
                    TextResourceDiagnosticSeverity.Error,
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
                "WUTTEXT0018",
                TextResourceDiagnosticSeverity.Error,
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
                        "WUTTEXT0016",
                        TextResourceDiagnosticSeverity.Error,
                        "Placeholder contract for allowed extra key '" + pair.Key + "' differs between locales.",
                        pair.Value.Source,
                        mismatchSpan);
                }
            }
        }
    }

    private static void AddPolicyDiagnostic(string id, TextResourcePolicy policy, string message, TextResourceSource source, ByteSpan span, DiagnosticBag diagnostics)
    {
        if (policy == TextResourcePolicy.Allow) return;
        diagnostics.Add(id, policy == TextResourcePolicy.Warning ? TextResourceDiagnosticSeverity.Warning : TextResourceDiagnosticSeverity.Error, message, source, span);
    }

    private static bool SameContract(ResourceModel left, ResourceModel right, out ByteSpan mismatchSpan)
    {
        mismatchSpan = right.KeySpan;
        if (left.Placeholders.Length != right.Placeholders.Length) return false;
        for (int i = 0; i < left.Placeholders.Length; i++)
        {
            if (left.Placeholders[i].Name != right.Placeholders[i].Name) { mismatchSpan = right.Placeholders[i].Span; return false; }
            if (left.Placeholders[i].Type != right.Placeholders[i].Type) { mismatchSpan = right.Placeholders[i].TypeSpan; return false; }
            if (left.Placeholders[i].Format != right.Placeholders[i].Format) { mismatchSpan = right.Placeholders[i].FormatSpan; return false; }
        }
        return true;
    }

    private static string? ReadOptionalString(JsonProperty? property, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return null;
        if (property.Value.Kind != JsonKind.String) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Leaf metadata value must be a string.", source, property.Value.Span); return null; }
        return property.Value.Text;
    }

    private static string[] ReadTags(JsonProperty? property, TextResourceSource source, DiagnosticBag diagnostics)
    {
        if (property is null) return Array.Empty<string>();
        if (property.Value.Kind != JsonKind.Array) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "$tags must be an array.", source, property.Value.Span); return Array.Empty<string>(); }
        var tags = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < property.Value.Items.Count; i++)
        {
            JsonValue item = property.Value.Items[i];
            if (item.Kind != JsonKind.String || item.Text!.Length == 0) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Tags must be non-empty strings.", source, item.Span);
            else if (!seen.Add(item.Text)) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Duplicate tag '" + item.Text + "'.", source, item.Span);
            else tags.Add(item.Text);
        }
        tags.Sort(StringComparer.Ordinal); return tags.ToArray();
    }

    private static bool HasDollarMember(JsonValue value)
    {
        for (int i = 0; i < value.Properties.Count; i++) if (value.Properties[i].Name.Length > 0 && value.Properties[i].Name[0] == '$') return true;
        return false;
    }

    private static JsonProperty? Required(JsonValue parent, string name, JsonKind kind, TextResourceSource source, DiagnosticBag diagnostics)
    {
        JsonProperty? property = parent.Property(name);
        if (property is null) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Missing required member '" + name + "'.", source, parent.Span); return null; }
        if (property.Value.Kind != kind) { diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Member '" + name + "' has an invalid value kind.", source, property.Value.Span); return null; }
        return property;
    }

    private static void ValidateKnownMembers(JsonValue value, string[] allowed, TextResourceSource source, DiagnosticBag diagnostics)
    {
        for (int i = 0; i < value.Properties.Count; i++)
        {
            bool found = false;
            for (int a = 0; a < allowed.Length; a++) if (value.Properties[i].Name == allowed[a]) { found = true; break; }
            if (!found) diagnostics.Add("WUTTEXT0019", TextResourceDiagnosticSeverity.Error, "Unknown or misplaced member '" + value.Properties[i].Name + "'.", source, value.Properties[i].NameSpan);
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

    private static bool TryArgumentType(string value, out TextResourceArgumentType type, out string format)
    {
        switch (value)
        {
            case "string": type = TextResourceArgumentType.String; format = "none"; return true;
            case "int": type = TextResourceArgumentType.Int; format = "plain"; return true;
            case "number": type = TextResourceArgumentType.Number; format = "plain"; return true;
            case "bool": type = TextResourceArgumentType.Boolean; format = "lower"; return true;
            case "date": type = TextResourceArgumentType.Date; format = "medium"; return true;
            case "time": type = TextResourceArgumentType.Time; format = "short"; return true;
            case "datetime": type = TextResourceArgumentType.DateTime; format = "medium"; return true;
            case "guid": type = TextResourceArgumentType.Guid; format = "d"; return true;
            default: type = TextResourceArgumentType.String; format = "none"; return false;
        }
    }

    private static bool IsAllowedFormat(TextResourceArgumentType type, string format)
    {
        switch (type)
        {
            case TextResourceArgumentType.String: return format == "none";
            case TextResourceArgumentType.Int: return format == "plain" || format == "grouped";
            case TextResourceArgumentType.Number:
                if (format == "plain" || format == "grouped") return true;
                if (format.StartsWith("fixed", StringComparison.Ordinal) && format.Length == 6) return format[5] >= '0' && format[5] <= '6';
                return format.StartsWith("percent", StringComparison.Ordinal) && format.Length == 8 && format[7] >= '0' && format[7] <= '4';
            case TextResourceArgumentType.Boolean: return format == "lower";
            case TextResourceArgumentType.Date: return format == "iso" || format == "short" || format == "medium" || format == "long";
            case TextResourceArgumentType.Time: return format == "iso" || format == "short" || format == "medium";
            case TextResourceArgumentType.DateTime: return format == "iso" || format == "short" || format == "medium" || format == "long";
            case TextResourceArgumentType.Guid: return format == "d" || format == "n";
            default: return false;
        }
    }

    private static string ArgumentTypeName(TextResourceArgumentType type)
    {
        switch (type)
        {
            case TextResourceArgumentType.String: return "string";
            case TextResourceArgumentType.Int: return "int";
            case TextResourceArgumentType.Number: return "number";
            case TextResourceArgumentType.Boolean: return "bool";
            case TextResourceArgumentType.Date: return "date";
            case TextResourceArgumentType.Time: return "time";
            case TextResourceArgumentType.DateTime: return "datetime";
            case TextResourceArgumentType.Guid: return "guid";
            default: throw new ArgumentOutOfRangeException(nameof(type));
        }
    }

    private static bool IsPathPrefix(string possiblePrefix, string value) => value.Length > possiblePrefix.Length && value.StartsWith(possiblePrefix, StringComparison.Ordinal) && value[possiblePrefix.Length] == '.';
    private static string[] Keys(Dictionary<string, ResourceModel> dictionary) { var keys = new List<string>(dictionary.Keys); keys.Sort(StringComparer.Ordinal); return keys.ToArray(); }
    private static string[] Sorted(HashSet<string> values) { var result = new List<string>(values); result.Sort(StringComparer.Ordinal); return result.ToArray(); }
    private static List<KeyValuePair<string, TValue>> SortedPairs<TValue>(Dictionary<string, TValue> values)
    { var result = new List<KeyValuePair<string, TValue>>(values); result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key)); return result; }
}
