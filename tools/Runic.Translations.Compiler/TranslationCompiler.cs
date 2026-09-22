using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Runic.Translations.Compiler;

public static partial class TranslationCompiler
{
    private static readonly string[] Mf2ProjectMembers = { "$schema", "schemaVersion", "catalog", "code", "baseLocale", "locales", "validation", "runtime", "sourceRoots", "markup" };
    private static readonly string[] CodeMembers = { "namespace", "className", "visibility" };
    private static readonly string[] LocaleMembers = { "tag", "fallback" };
    private static readonly string[] ValidationMembers = { "translationCompleteness", "extraLocaleKeys", "emptyValues" };
    private static readonly string[] RuntimeMembers = { "unsupportedLocale", "missingKey" };

    /// <summary>Compiles a convention-based Runic project whose messages are standard MF2 files.</summary>
    /// <remarks>
    /// The project file is named <c>runic.json</c>. Message paths are relative to its directory and
    /// use the convention <c>{locale}/{message-id}.mf2</c>.
    /// </remarks>
    public static Rmf2ProjectCompilationV5 CompileMf2Project(
        TranslationSource project,
        IEnumerable<TranslationSource> messages,
        TranslationCompilerOptions? options = null)
        => CompileMf2Project(project, messages, options, CancellationToken.None);

    /// <summary>Compiles a convention-based Runic MF2 project with cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static Rmf2ProjectCompilationV5 CompileMf2Project(
        TranslationSource project,
        IEnumerable<TranslationSource> messages,
        TranslationCompilerOptions? options,
        CancellationToken cancellationToken)
        => CompileRmf2ProjectV5(project, messages, options, cancellationToken);

    /// <summary>Compiles direct <c>.mf2</c> or grouped <c>.rmf2</c> sources into the RMF2 execution-v2 contract.</summary>
    public static Rmf2ProjectCompilationV5 CompileProject(TranslationSource project, IEnumerable<TranslationSource> messages,
        TranslationCompilerOptions? options = null)
        => CompileProject(project, messages, options, CancellationToken.None);

    /// <summary>Compiles direct <c>.mf2</c> or grouped <c>.rmf2</c> sources into the RMF2 execution-v2 contract with cancellation.</summary>
    /// <exception cref="OperationCanceledException">The cancellation token was canceled.</exception>
    public static Rmf2ProjectCompilationV5 CompileProject(TranslationSource project, IEnumerable<TranslationSource> messages,
        TranslationCompilerOptions? options, CancellationToken cancellationToken)
        => CompileRmf2ProjectV5(project, messages, options, cancellationToken);

    private static ManifestModel? ReadMf2Project(ParsedJson parsed, DiagnosticBag diagnostics, TranslationCompilerOptions options)
    {
        JsonValue root = parsed.Root!;
        if (root.Kind != JsonKind.Object)
        {
            diagnostics.Add("RTR0019", TranslationDiagnosticSeverity.Error, "Runic project root must be an object.", parsed.Source, root.Span);
            return null;
        }
        ValidateKnownMembers(root, Mf2ProjectMembers, parsed.Source, diagnostics);
        JsonProperty? version = Required(root, "schemaVersion", JsonKind.Number, parsed.Source, diagnostics);
        if (version is not null && !string.Equals(version.Value.Text, "1", StringComparison.Ordinal))
            diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error, "Unsupported Runic project schemaVersion.", parsed.Source, version.Value.Span);
        JsonProperty? schema = root.Property("$schema");
        if (schema is not null && (schema.Value.Kind != JsonKind.String ||
            !string.Equals(schema.Value.Text, "https://runic-artifex.eu/schemas/translations/project-v1.schema.json", StringComparison.Ordinal)))
            diagnostics.Add("RTR0003", TranslationDiagnosticSeverity.Error, "The Runic project uses an unknown $schema URI.", parsed.Source, schema.Value.Span);

        var model = new ManifestModel(parsed.Source);
        JsonProperty? catalog = Required(root, "catalog", JsonKind.String, parsed.Source, diagnostics);
        JsonProperty? code = Required(root, "code", JsonKind.Object, parsed.Source, diagnostics);
        JsonProperty? baseLocale = Required(root, "baseLocale", JsonKind.String, parsed.Source, diagnostics);
        if (catalog is not null)
        {
            model.Id = catalog.Value.Text!;
            if (!IsCatalogId(model.Id) || IsWindowsDeviceStem(model.Id))
                diagnostics.Add("RTR0006", TranslationDiagnosticSeverity.Error, "Invalid catalog ID '" + model.Id + "'.", parsed.Source, catalog.Value.Span);
        }
        if (code is not null) ReadCode(code.Value, model, parsed.Source, diagnostics);
        if (baseLocale is not null)
        {
            model.DefaultLocaleSpan = baseLocale.Value.Span;
            if (!TryCanonicalizeLocale(baseLocale.Value.Text!, out string canonical))
                diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid base locale '" + baseLocale.Value.Text + "'.", parsed.Source, baseLocale.Value.Span);
            else model.DefaultLocale = canonical;
        }

        JsonProperty? locales = root.Property("locales");
        if (locales is not null)
        {
            if (locales.Value.Kind != JsonKind.Array)
                diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "locales must be an array of locale tags.", parsed.Source, locales.Value.Span);
            else
            {
                if (locales.Value.Items.Count > options.MaximumLocalesPerCatalog)
                    diagnostics.Add("RTR0022", TranslationDiagnosticSeverity.Error, "Locale count exceeds the configured limit.", parsed.Source, locales.Value.Span);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 0; index < locales.Value.Items.Count; index++)
                {
                    JsonValue item = locales.Value.Items[index];
                    if (item.Kind == JsonKind.Object)
                    {
                        ReadMf2Locale(item, model, seen, parsed.Source, diagnostics);
                        continue;
                    }
                    if (item.Kind != JsonKind.String || !TryCanonicalizeLocale(item.Text!, out string locale))
                    {
                        diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "locales contains an invalid locale declaration.", parsed.Source, item.Span);
                        continue;
                    }
                    if (!seen.Add(locale))
                        diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Duplicate locale '" + locale + "'.", parsed.Source, item.Span);
                    else model.Locales.Add(new LocaleModel(locale,
                        string.Equals(locale, model.DefaultLocale, StringComparison.OrdinalIgnoreCase) ? null : model.DefaultLocale,
                        item.Span, item.Span));
                }
                if (model.DefaultLocale.Length != 0 && !seen.Contains(model.DefaultLocale))
                    diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "locales must include baseLocale.", parsed.Source, locales.Value.Span);
            }
        }
        JsonProperty? validation = root.Property("validation");
        if (validation is not null) ReadValidation(validation, model, parsed.Source, diagnostics);
        JsonProperty? runtime = root.Property("runtime");
        if (runtime is not null) ReadRuntime(runtime, model, parsed.Source, diagnostics);
        return model;
    }

    private static void ReadMf2Locale(JsonValue item, ManifestModel model, HashSet<string> seen, TranslationSource source, DiagnosticBag diagnostics)
    {
        ValidateKnownMembers(item, LocaleMembers, source, diagnostics);
        JsonProperty? tagProperty = Required(item, "tag", JsonKind.String, source, diagnostics);
        JsonProperty? fallbackProperty = item.Property("fallback");
        if (tagProperty is null) return;
        if (!TryCanonicalizeLocale(tagProperty.Value.Text!, out string tag))
        {
            diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Invalid locale tag '" + tagProperty.Value.Text + "'.", source, tagProperty.Value.Span);
            return;
        }
        if (!seen.Add(tag))
            diagnostics.Add("RTR0004", TranslationDiagnosticSeverity.Error, "Duplicate locale '" + tag + "'.", source, tagProperty.Value.Span);
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

    private static string ProjectDirectory(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path.Substring(0, slash + 1);
    }

    private static bool TryMf2Identity(string projectDirectory, string sourcePath, out string locale, out string messageId)
    {
        locale = string.Empty;
        messageId = string.Empty;
        if (!sourcePath.StartsWith(projectDirectory, StringComparison.Ordinal) ||
            !sourcePath.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase)) return false;
        string relative = sourcePath.Substring(projectDirectory.Length);
        int slash = relative.IndexOf('/');
        if (slash <= 0 || slash != relative.LastIndexOf('/') || slash == relative.Length - 1) return false;
        locale = relative.Substring(0, slash);
        messageId = relative.Substring(slash + 1, relative.Length - slash - 1 - ".mf2".Length);
        return messageId.Length != 0;
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

    private static bool SupportsBuiltInPlural(string locale, bool ordinal)
        => ordinal ? TranslationCapabilityRegistry.SupportsOrdinal(locale) : TranslationCapabilityRegistry.SupportsCardinal(locale);

    private static bool SupportsRelativeTime(string locale) =>
        TranslationCapabilityRegistry.SupportsRelativeTime(locale);

    private static void AddPolicyDiagnostic(string id, TranslationPolicy policy, string message, TranslationSource source, ByteSpan span, DiagnosticBag diagnostics)
    {
        if (policy == TranslationPolicy.Allow) return;
        diagnostics.Add(id, policy == TranslationPolicy.Warning ? TranslationDiagnosticSeverity.Warning : TranslationDiagnosticSeverity.Error, message, source, span);
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

    internal static bool IsIdentifier(string value)
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
        int index = 1;
        if (parts[0].Length <= 3)
        {
            for (int count = 0; count < 3 && index < parts.Length && parts[index].Length == 3 && AllLetters(parts[index]); count++, index++)
                AppendLower(result, parts[index]);
        }
        if (index < parts.Length && parts[index].Length == 4 && AllLetters(parts[index]))
        {
            string script = parts[index++];
            result.Append('-').Append(char.ToUpperInvariant(script[0])).Append(script.Substring(1).ToLowerInvariant());
        }
        if (index < parts.Length && ((parts[index].Length == 2 && AllLetters(parts[index])) || (parts[index].Length == 3 && AllDigits(parts[index]))))
            result.Append('-').Append(parts[index++].ToUpperInvariant());
        var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (index < parts.Length && IsLocaleVariant(parts[index]))
        {
            if (!variants.Add(parts[index])) return false;
            AppendLower(result, parts[index++]);
        }
        var singletons = new HashSet<char>();
        while (index < parts.Length && IsLocaleExtensionSingleton(parts[index]))
        {
            char singleton = char.ToLowerInvariant(parts[index++][0]);
            if (!singletons.Add(singleton)) return false;
            result.Append('-').Append(singleton);
            int firstSubtag = index;
            while (index < parts.Length && parts[index].Length is >= 2 and <= 8 && AllAlphaNumeric(parts[index])) AppendLower(result, parts[index++]);
            if (index == firstSubtag) return false;
        }
        if (index < parts.Length && parts[index].Length == 1 && (parts[index][0] == 'x' || parts[index][0] == 'X'))
        {
            result.Append("-x");
            index++;
            int firstSubtag = index;
            while (index < parts.Length && parts[index].Length is >= 1 and <= 8 && AllAlphaNumeric(parts[index])) AppendLower(result, parts[index++]);
            if (index == firstSubtag) return false;
        }
        if (index != parts.Length) return false;
        canonical = result.ToString(); return true;
    }

    private static void AppendLower(StringBuilder result, string part) => result.Append('-').Append(part.ToLowerInvariant());
    private static bool IsLocaleVariant(string value) =>
        (value.Length is >= 5 and <= 8 && AllAlphaNumeric(value)) ||
        (value.Length == 4 && value[0] >= '0' && value[0] <= '9' && AllAlphaNumeric(value));
    private static bool IsLocaleExtensionSingleton(string value) => value.Length == 1 &&
        ((value[0] >= '0' && value[0] <= '9') ||
         (value[0] >= 'A' && value[0] <= 'W') || (value[0] >= 'Y' && value[0] <= 'Z') ||
         (value[0] >= 'a' && value[0] <= 'w') || (value[0] >= 'y' && value[0] <= 'z'));

    private static bool AllLetters(string value) { for (int i = 0; i < value.Length; i++) if (!((value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z'))) return false; return true; }
    private static bool AllDigits(string value) { for (int i = 0; i < value.Length; i++) if (value[i] < '0' || value[i] > '9') return false; return true; }
    private static bool AllAlphaNumeric(string value) { for (int i = 0; i < value.Length; i++) if (!((value[i] >= 'A' && value[i] <= 'Z') || (value[i] >= 'a' && value[i] <= 'z') || (value[i] >= '0' && value[i] <= '9'))) return false; return true; }
}
