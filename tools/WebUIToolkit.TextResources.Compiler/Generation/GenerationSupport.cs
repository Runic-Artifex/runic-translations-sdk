using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WebUIToolkit.TextResources.Compiler.Generation;

internal sealed class ResourceTreeNode
{
    internal ResourceTreeNode(string name) => Name = name;

    internal string Name { get; }
    internal SortedDictionary<string, ResourceTreeNode> Children { get; } = new SortedDictionary<string, ResourceTreeNode>(StringComparer.Ordinal);
    internal CompiledTextResource? Resource { get; set; }
}

internal sealed class GenerationWriter
{
    private readonly StringBuilder _builder = new StringBuilder();
    private int _indent;

    internal void Indent() => _indent++;
    internal void Unindent() => _indent--;
    internal void Blank() => _builder.Append('\n');

    internal void Line(string value = "")
    {
        for (int i = 0; i < _indent; i++) _builder.Append("    ");
        _builder.Append(value).Append('\n');
    }

    public override string ToString() => _builder.ToString();
}

internal sealed class GeneratedCatalogDefinition
{
    internal GeneratedCatalogDefinition(int id, CompiledTextResource resource, bool isCanonical)
    {
        Id = id;
        Resource = resource;
        IsCanonical = isCanonical;
    }

    internal int Id { get; }
    internal CompiledTextResource Resource { get; }
    internal bool IsCanonical { get; }
}

internal sealed class GeneratedCatalogTable
{
    private readonly Dictionary<string, int> _idByName;

    private GeneratedCatalogTable(IReadOnlyList<GeneratedCatalogDefinition> definitions, Dictionary<string, int> idByName)
    {
        Definitions = definitions;
        _idByName = idByName;
    }

    internal IReadOnlyList<GeneratedCatalogDefinition> Definitions { get; }

    internal int GetId(string name) => _idByName[name];

    internal static GeneratedCatalogTable Create(CompiledTextCatalog catalog)
    {
        IReadOnlyList<CompiledTextResource> canonical = GenerationSupport.OrderedResources(catalog.CanonicalResources);
        var definitions = new List<GeneratedCatalogDefinition>(canonical.Count);
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < canonical.Count; i++)
        {
            CompiledTextResource resource = canonical[i];
            if (resource.Id != i)
                throw new InvalidOperationException("Canonical resource IDs are not contiguous ordinal key IDs.");
            definitions.Add(new GeneratedCatalogDefinition(i, resource, true));
            ids.Add(resource.Key, i);
        }

        var extras = new SortedDictionary<string, ExtraContract>(StringComparer.Ordinal);
        IReadOnlyList<CompiledTextLocale> locales = GenerationSupport.OrderedLocales(catalog.Locales);
        for (int localeIndex = 0; localeIndex < locales.Count; localeIndex++)
        {
            CompiledTextLocale locale = locales[localeIndex];
            IReadOnlyList<CompiledTextResource> resources = GenerationSupport.OrderedResources(locale.DirectResources);
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                CompiledTextResource resource = resources[resourceIndex];
                if (ids.ContainsKey(resource.Key)) continue;
                ExtraContract existing;
                if (!extras.TryGetValue(resource.Key, out existing!))
                {
                    extras.Add(resource.Key, new ExtraContract(locale.Tag, resource));
                    continue;
                }
                if (!SamePlaceholderContract(existing.Resource.Placeholders, resource.Placeholders))
                {
                    throw new InvalidOperationException(
                        "Non-canonical resource '" + resource.Key + "' has inconsistent placeholder contracts in locales '" +
                        existing.Locale + "' and '" + locale.Tag + "'.");
                }
            }
        }

        foreach (KeyValuePair<string, ExtraContract> extra in extras)
        {
            int id = definitions.Count;
            ids.Add(extra.Key, id);
            definitions.Add(new GeneratedCatalogDefinition(id, extra.Value.Resource, false));
        }
        return new GeneratedCatalogTable(definitions.ToArray(), ids);
    }

    private static bool SamePlaceholderContract(
        IReadOnlyList<CompiledTextPlaceholder> left,
        IReadOnlyList<CompiledTextPlaceholder> right)
    {
        IReadOnlyList<CompiledTextPlaceholder> orderedLeft = GenerationSupport.OrderedPlaceholders(left);
        IReadOnlyList<CompiledTextPlaceholder> orderedRight = GenerationSupport.OrderedPlaceholders(right);
        if (orderedLeft.Count != orderedRight.Count) return false;
        for (int i = 0; i < orderedLeft.Count; i++)
        {
            if (!string.Equals(orderedLeft[i].Name, orderedRight[i].Name, StringComparison.Ordinal) ||
                orderedLeft[i].Type != orderedRight[i].Type ||
                !string.Equals(orderedLeft[i].Format, orderedRight[i].Format, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private sealed class ExtraContract
    {
        internal ExtraContract(string locale, CompiledTextResource resource)
        {
            Locale = locale;
            Resource = resource;
        }

        internal string Locale { get; }
        internal CompiledTextResource Resource { get; }
    }
}

internal static class GenerationSupport
{
    private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event",
        "explicit", "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
        "in", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object",
        "operator", "out", "override", "params", "private", "protected", "public", "readonly", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "add", "alias", "and", "ascending", "async", "await", "by", "descending",
        "dynamic", "equals", "file", "from", "get", "global", "group", "init", "into", "join", "let", "managed",
        "nameof", "not", "notnull", "on", "or", "orderby", "partial", "record", "remove", "required", "scoped",
        "select", "set", "unmanaged", "value", "var", "when", "where", "with", "yield",
    };

    internal static ResourceTreeNode BuildTree(CompiledTextCatalog catalog)
    {
        var root = new ResourceTreeNode(string.Empty);
        IReadOnlyList<CompiledTextResource> resources = OrderedResources(catalog.CanonicalResources);
        for (int i = 0; i < resources.Count; i++)
        {
            CompiledTextResource resource = resources[i];
            string[] segments = resource.Key.Split('.');
            ResourceTreeNode current = root;
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                ResourceTreeNode child;
                if (!current.Children.TryGetValue(segments[segmentIndex], out child!))
                {
                    child = new ResourceTreeNode(segments[segmentIndex]);
                    current.Children.Add(child.Name, child);
                }
                current = child;
            }
            current.Resource = resource;
        }
        return root;
    }

    internal static IReadOnlyList<CompiledTextResource> OrderedResources(IReadOnlyList<CompiledTextResource> resources)
    {
        var result = new List<CompiledTextResource>(resources.Count);
        for (int i = 0; i < resources.Count; i++) result.Add(resources[i]);
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        return result;
    }

    internal static IReadOnlyList<CompiledTextPlaceholder> OrderedPlaceholders(IReadOnlyList<CompiledTextPlaceholder> placeholders)
    {
        var result = new List<CompiledTextPlaceholder>(placeholders.Count);
        for (int i = 0; i < placeholders.Count; i++) result.Add(placeholders[i]);
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return result;
    }

    internal static IReadOnlyList<CompiledTextLocale> OrderedLocales(IReadOnlyList<CompiledTextLocale> locales)
    {
        var result = new List<CompiledTextLocale>(locales.Count);
        for (int i = 0; i < locales.Count; i++) result.Add(locales[i]);
        result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Tag, right.Tag));
        return result;
    }

    internal static string CSharpIdentifier(string value) => CSharpKeywords.Contains(value) ? "@" + value : value;

    internal static string CSharpNamespace(string value)
    {
        string[] segments = value.Split('.');
        for (int i = 0; i < segments.Length; i++) segments[i] = CSharpIdentifier(segments[i]);
        return string.Join(".", segments);
    }

    internal static string CSharpString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\0': result.Append("\\0"); break;
                case '\a': result.Append("\\a"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                case '\v': result.Append("\\v"); break;
                default:
                    if (character < ' ' || character == '\u2028' || character == '\u2029' || IsUnpairedSurrogate(value, i))
                        result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    internal static string JsonString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < ' ' || IsUnpairedSurrogate(value, i))
                        result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        result.Append(character);
                    break;
            }
        }
        return result.Append('"').ToString();
    }

    internal static string XmlDocumentation(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            switch (character)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '"': result.Append("&quot;"); break;
                case '\'': result.Append("&apos;"); break;
                case '\r': break;
                case '\n': result.Append(' '); break;
                default:
                    if ((character < ' ' && character != '\t') || character == '\uFFFE' || character == '\uFFFF' || IsUnpairedSurrogate(value, i)) result.Append('\uFFFD');
                    else result.Append(character);
                    break;
            }
        }
        return result.ToString();
    }

    internal static string TypeScriptType(TextResourceArgumentType type)
    {
        switch (type)
        {
            case TextResourceArgumentType.Int:
            case TextResourceArgumentType.Number:
                return "number";
            case TextResourceArgumentType.Boolean:
                return "boolean";
            default:
                return "string";
        }
    }

    internal static string ArgumentTypeName(TextResourceArgumentType type)
    {
        switch (type)
        {
            case TextResourceArgumentType.Boolean: return "Bool";
            default: return type.ToString();
        }
    }

    internal static string ArgumentFormatName(string format)
    {
        if (format.Length == 0) return "None";
        return char.ToUpperInvariant(format[0]) + format.Substring(1);
    }

    internal static string JsonArgumentType(TextResourceArgumentType type)
    {
        switch (type)
        {
            case TextResourceArgumentType.Boolean: return "bool";
            case TextResourceArgumentType.DateTime: return "datetime";
            default: return type.ToString().ToLowerInvariant();
        }
    }

    private static bool IsUnpairedSurrogate(string value, int index)
    {
        char character = value[index];
        if (char.IsHighSurrogate(character)) return index + 1 >= value.Length || !char.IsLowSurrogate(value[index + 1]);
        return char.IsLowSurrogate(character) && (index == 0 || !char.IsHighSurrogate(value[index - 1]));
    }
}
