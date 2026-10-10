using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Runic.Translations.Compiler;

namespace Runic.Translations.Generator;

// Presentation-specific parsing only. Message names, parameter order and content kind
// come from the same compiler contracts that emit the readable surface; no reflection
// or separately maintained message schema is involved.
// The readable messages of the linked local catalog, built once per link and shared by every XAML file.
internal sealed class XamlCatalog
{
    internal XamlCatalog(Rmf2ProjectV5 project)
    {
        Id = project.Id;
        foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages)
            if (Rmf2ReadableNamesV1.TryCreate(project.ClassName, contract, out Rmf2ReadableMessageV1? names, out _))
                Messages.Add(names!.Member, names);
    }

    internal string Id { get; }
    internal Dictionary<string, Rmf2ReadableMessageV1> Messages { get; } = new(StringComparer.Ordinal);
}

internal sealed class XamlMessageValidator
{
    private const string NullValue = "{__runic:null}";
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string MarkupCompatibilityNamespace = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    private readonly SourceProductionContext _context;
    private readonly TranslationsGenerator.GeneratorInput _input;
    private readonly SourceText _text;
    private readonly XamlCatalog _catalog;
    // Explicit catalog sources that keep the project default from applying: a style setter
    // can apply to any element, an attached source to its element's content.
    private XObject? _fileSource;
    private readonly Dictionary<XElement, XObject> _scopedSources = [];
    private readonly HashSet<XObject> _reportedSources = [];

    private XamlMessageValidator(SourceProductionContext context, TranslationsGenerator.GeneratorInput input, XamlCatalog catalog)
    {
        _context = context;
        _input = input;
        _text = SourceText.From(input.Text ?? string.Empty);
        _catalog = catalog;
    }

    internal static void Validate(SourceProductionContext context, TranslationsGenerator.GeneratorInput input, XamlCatalog catalog) =>
        new XamlMessageValidator(context, input, catalog).Run();

    private void Run()
    {
        if (_input.Text is null)
        {
            Report(TranslationsDiagnostics.XamlDeclaration, null, "Translation XAML source could not be read.");
            return;
        }
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new StringReader(_input.Text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            Report(TranslationsDiagnostics.XamlDeclaration, null, "Translation XAML is not well-formed: " + exception.Message,
                exception.LineNumber, exception.LinePosition);
            return;
        }
        if (document.Root is null) return;
        // Design-time content (mc:Ignorable namespaces such as d:) and mc:AlternateContent are
        // never checked: WPF drops the former, and which AlternateContent branch it compiles
        // depends on the namespaces it understands, so neither branch is known statically.
        var elements = new List<XElement>();
        CollectCompiledElements(document.Root, elements);
        // A Message's own Source only affects that Message (see Check). An attached source
        // affects its element's content, and also templates, styles and resources of this file,
        // which may be instantiated under it. A style setter may apply to any element.
        foreach (XElement element in elements)
        {
            if (SetterSource(element) is { } setter) _fileSource ??= setter;
            else if (AttachedSource(element) is { } attached) _scopedSources.TryAdd(attached.Scope, attached.Site);
        }
        foreach (XElement element in elements)
        {
            _context.CancellationToken.ThrowIfCancellationRequested();
            if (IsRunic(element.Name.NamespaceName) && IsMessage(element.Name.LocalName)) ValidateObject(element);
            foreach (XAttribute attribute in CompiledAttributes(element))
            {
                if (IsRunic(attribute.Name.NamespaceName) && attribute.Name.LocalName == "TranslationProperties.RichMessage")
                    Check(element, attribute, StaticValue(attribute.Value), rich: true, null, null);
                else ScanMarkup(element, attribute, attribute.Value);
            }
            // Long property form for a static rich key; arguments and slots remain runtime/typed-C# concerns.
            if (IsRunic(element.Name.NamespaceName) && element.Name.LocalName == "TranslationProperties.RichMessage")
                Check(element, element, ElementValue(element), rich: true, null, null);
        }
    }

    private static void CollectCompiledElements(XElement element, List<XElement> elements)
    {
        if (IsIgnored(element)) return;
        elements.Add(element);
        foreach (XElement child in element.Elements()) CollectCompiledElements(child, elements);
    }

    private static IEnumerable<XAttribute> CompiledAttributes(XElement element) =>
        element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && !IsIgnorable(element, attribute.Name.NamespaceName));

    private static bool IsIgnored(XElement element) =>
        element.Name.NamespaceName == MarkupCompatibilityNamespace || IsIgnorable(element, element.Name.NamespaceName);

    // A namespace listed by mc:Ignorable on this element or an ancestor, resolved where it is declared.
    // Namespaces that WPF understands (its own and Runic's) are compiled even when listed.
    private static bool IsIgnorable(XElement scope, string ns)
    {
        if (ns.Length == 0 || ns is XamlNamespace or PresentationNamespace || IsRunic(ns)) return false;
        if (ns == MarkupCompatibilityNamespace) return true;
        for (XElement? element = scope; element is not null; element = element.Parent)
        {
            if (element.Attribute(XName.Get("Ignorable", MarkupCompatibilityNamespace)) is not { } ignorable) continue;
            foreach (string prefix in ignorable.Value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                if (element.GetNamespaceOfPrefix(prefix)?.NamespaceName == ns) return true;
        }
        return false;
    }

    // A Setter (or trigger) on TranslationProperties.Source; returns where it is declared.
    private static XObject? SetterSource(XElement element)
    {
        if (CompiledAttributes(element).FirstOrDefault(attribute => attribute.Name.LocalName == "Property" && IsSourceProperty(element, attribute.Value)) is { } property)
            return property;
        return element.Name.LocalName == "Setter.Property" && element.Elements().Any(child =>
            child.Name.NamespaceName == XamlNamespace && (child.Name.LocalName is "Static" or "StaticExtension") &&
            Resolves(child, StaticValue(child.Attribute("Member")?.Value) ?? string.Empty, "TranslationProperties.SourceProperty")) ? element : null;
    }

    // An attached TranslationProperties.Source: the element it is set on and where it is declared.
    private static (XElement Scope, XObject Site)? AttachedSource(XElement element)
    {
        if (IsRunic(element.Name.NamespaceName) && element.Name.LocalName == "TranslationProperties.Source" && element.Parent is { } parent)
            return (parent, element);
        if (CompiledAttributes(element).FirstOrDefault(static attribute =>
                IsRunic(attribute.Name.NamespaceName) && attribute.Name.LocalName == "TranslationProperties.Source") is { } attribute)
            return (element, attribute);
        return null;
    }

    // The explicit source that may select another catalog for this element, if any.
    private XObject? InheritedSource(XElement element)
    {
        if (_fileSource is not null) return _fileSource;
        if (_scopedSources.Count == 0) return null;
        for (XElement? current = element; current is not null; current = current.Parent)
            if (_scopedSources.TryGetValue(current, out XObject? site)) return site;
        // Reusable content may be instantiated under any attached source of this file.
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            string name = current.Name.LocalName;
            if (name is "ResourceDictionary" or "Style" || name.EndsWith("Template", StringComparison.Ordinal) ||
                name.EndsWith(".Resources", StringComparison.Ordinal))
                return _scopedSources.Values.First();
        }
        return null;
    }

    private static bool IsSourceProperty(XElement element, string value)
    {
        if (Resolves(element, StaticValue(value) ?? string.Empty, "TranslationProperties.Source")) return true;
        if (!TryReadMarkup(value, out string type, out List<string> parts, out _) || !IsXamlType(element, type, "Static")) return false;
        foreach (string part in parts)
        {
            if (!TrySplit(part, '=', out List<string> assignment)) continue;
            if (assignment.Count > 1 && assignment[0] != "Member") continue;
            string member = assignment.Count == 1 ? part : string.Join("=", assignment.Skip(1));
            if (Resolves(element, StaticValue(member) ?? string.Empty, "TranslationProperties.SourceProperty")) return true;
        }
        return false;
    }

    private static bool IsXamlType(XElement element, string type, string localName)
    {
        int colon = type.IndexOf(':');
        string local = colon < 0 ? type : type.Substring(colon + 1);
        string? ns = colon < 0 ? element.GetDefaultNamespace().NamespaceName : element.GetNamespaceOfPrefix(type.Substring(0, colon))?.NamespaceName;
        return (local == localName || local == localName + "Extension") && ns == "http://schemas.microsoft.com/winfx/2006/xaml";
    }

    private static bool TryReadMarkup(string value, out string type, out List<string> parts, out int end)
    {
        value = value.Trim();
        type = string.Empty;
        parts = [];
        end = -1;
        if (!value.StartsWith('{') || value.StartsWith("{}", StringComparison.Ordinal)) return false;
        end = MatchingBrace(value);
        if (end < 0) return false;
        string content = value.Substring(1, end - 1).Trim();
        int separator = content.IndexOfAny([' ', '\t', '\r', '\n', ',']);
        type = separator < 0 ? content : content.Substring(0, separator);
        string arguments = separator < 0 ? string.Empty : content.Substring(separator).Trim().TrimStart(',');
        return TrySplit(arguments, ',', out parts);
    }

    private void ValidateObject(XElement element)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XAttribute attribute in element.Attributes().Where(static attribute => !attribute.IsNamespaceDeclaration && attribute.Name.NamespaceName.Length == 0))
            properties[attribute.Name.LocalName] = attribute.Value;
        var names = new List<string>();
        bool unresolved = false;
        bool invalid = false;
        foreach (XElement child in element.Elements().Where(static child => !IsIgnored(child)))
        {
            int dot = child.Name.LocalName.IndexOf('.');
            if (IsRunic(child.Name.NamespaceName) && dot > 0 && IsMessage(child.Name.LocalName.Substring(0, dot)))
            {
                string property = child.Name.LocalName.Substring(dot + 1);
                if (property == "Inputs") ReadInputs(child.Elements());
                else if (!properties.TryAdd(property, ElementValue(child) ?? "{dynamic}"))
                {
                    Report(TranslationsDiagnostics.XamlDeclaration, child, "Message property '" + property + "' is assigned more than once.");
                    invalid = true;
                }
            }
            else ReadInputs([child]);
        }
        if (!invalid) ValidateDeclaration(element, element, properties, names, unresolved);

        void ReadInputs(IEnumerable<XElement> inputs)
        {
            foreach (XElement input in inputs)
            {
                if (!IsRunic(input.Name.NamespaceName) || input.Name.LocalName != "MessageInput") { unresolved = true; continue; }
                string? name = StaticValue(input.Attribute("Name")?.Value) ??
                    ElementValue(input.Elements().FirstOrDefault(child => IsRunic(child.Name.NamespaceName) && child.Name.LocalName == "MessageInput.Name"));
                bool valuePresent = input.Attribute("Value") is { } value && !IsNullValue(input, value.Value) ||
                    input.Elements().Any(child => IsRunic(child.Name.NamespaceName) && child.Name.LocalName == "MessageInput.Value" && child.HasElements && ElementValue(child) != NullValue);
                if (name is null && (input.Attribute("Name") is not null || input.Elements().Any(child => IsRunic(child.Name.NamespaceName) && child.Name.LocalName == "MessageInput.Name"))) { unresolved = true; continue; }
                if (string.IsNullOrEmpty(name) || !valuePresent)
                {
                    Report(TranslationsDiagnostics.XamlInputs, input, "Every MessageInput needs a Name and a Value binding.");
                    invalid = true;
                }
                else names.Add(name);
            }
        }
    }

    private void ScanMarkup(XElement element, XObject location, string value)
    {
        value = value.Trim();
        if (!value.StartsWith('{') || value.StartsWith("{}", StringComparison.Ordinal)) return;
        if (!TryReadMarkup(value, out string type, out List<string> parts, out int end))
        {
            string candidate = value.Substring(1).TrimStart().Split([' ', '\t', '\r', '\n', ','], 2)[0];
            if (IsMessageMarkup(element, candidate))
                Report(TranslationsDiagnostics.XamlDeclaration, location, "Message markup extension has unbalanced braces or arguments.");
            return;
        }
        if (IsMessageMarkup(element, type))
        {
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            bool invalid = end != value.Length - 1;
            foreach (string part in parts)
            {
                if (part.Length == 0) continue;
                TrySplit(part, '=', out List<string> assignment);
                string property = assignment.Count == 1 ? "Key" : assignment[0];
                string propertyValue = assignment.Count == 1 ? part : string.Join("=", assignment.Skip(1));
                if (!properties.TryAdd(property.Trim(), propertyValue.Trim())) invalid = true;
            }
            if (invalid) Report(TranslationsDiagnostics.XamlDeclaration, location, "Message requires one Key and unique property assignments.");
            else ValidateDeclaration(element, location, properties, [], unresolved: false);
        }
        // Nested extensions such as Binding.ConverterParameter are parsed using the
        // enclosing element's namespace scope, while braces/commas in quoted strings stay literal.
        foreach (string part in parts)
        {
            TrySplit(part, '=', out List<string> assignment);
            ScanMarkup(element, location, assignment.Count == 1 ? part : string.Join("=", assignment.Skip(1)));
        }
    }

    private void ValidateDeclaration(XElement element, XObject location, Dictionary<string, string> properties,
        List<string> names, bool unresolved)
    {
        if (!properties.TryGetValue("Key", out string? keyValue))
        {
            Report(TranslationsDiagnostics.XamlDeclaration, location, "Message needs a Key.");
            return;
        }
        if (IsNullValue(element, keyValue)) { Report(TranslationsDiagnostics.XamlDeclaration, location, "Message needs a nonnull Key."); return; }
        string? key = StaticValue(keyValue);
        if (key is "") { Report(TranslationsDiagnostics.XamlDeclaration, location, "Message needs a nonempty Key."); return; }
        bool hasInputs = names.Count > 0 || unresolved || properties.ContainsKey("Inputs");
        var args = new HashSet<int>();
        foreach (string property in properties.Keys)
            if (property is "Arg0" or "Arg1" or "Arg2" or "Arg3" && !IsNullValue(element, properties[property])) args.Add(property[3] - '0');
        if (hasInputs && args.Count > 0)
        {
            Report(TranslationsDiagnostics.XamlInputs, location, "Message sets both Arg0..Arg3 and MessageInput entries; use one form. Named MessageInput entries are preferred.");
            return;
        }
        if (args.Count > 0 && args.Max() + 1 != args.Count)
        {
            int gap = Enumerable.Range(0, 4).First(index => !args.Contains(index));
            Report(TranslationsDiagnostics.XamlInputs, location, "Positional inputs must start at Arg0 without gaps; Arg" + gap + " is not set.");
            return;
        }
        if (names.GroupBy(static name => name, StringComparer.Ordinal).FirstOrDefault(static group => group.Count() > 1) is { } duplicate)
        {
            Report(TranslationsDiagnostics.XamlInputs, location, "MessageInput '" + duplicate.Key + "' is set more than once.");
            return;
        }
        bool explicitSource = properties.ContainsKey("Source");
        Check(element, location, key, rich: false,
            unresolved || properties.ContainsKey("Inputs") ? null : hasInputs ? names.Count : args.Count,
            hasInputs ? names : null, explicitSource);
    }

    private void Check(XElement element, XObject location, string? key, bool rich, int? count, List<string>? names, bool explicitSource = false)
    {
        if (key is null) return; // Binding, x:Static, resource or other runtime key.
        string? catalog = _input.Catalog;
        if (string.IsNullOrWhiteSpace(catalog))
        {
            catalog = _input.DefaultCatalog;
            if (string.IsNullOrWhiteSpace(catalog)) return;
            XObject? source = explicitSource ? location : InheritedSource(element);
            if (source is not null)
            {
                ReportSkipped(source, key, location, catalog!);
                return;
            }
        }
        if (catalog != _catalog.Id)
        {
            Report(TranslationsDiagnostics.XamlDeclaration, location, "XAML catalog '" + catalog + "' is not the local TranslationProject catalog '" + _catalog.Id +
                "'. External catalogs are not checked; set TranslationsValidateXaml=\"false\" on the file's Page or TranslationXaml item to skip it.");
            return;
        }
        if (!_catalog.Messages.TryGetValue(key, out Rmf2ReadableMessageV1? message))
        {
            string? suggestion = Suggest(key);
            Report(TranslationsDiagnostics.XamlKey, location, "'" + key + "' is not a readable message of catalog '" + catalog + "'." +
                (suggestion is null ? " Use its flattened readable name." : " Did you mean '" + suggestion + "'?") +
                (string.IsNullOrWhiteSpace(_input.Catalog)
                    ? " The file was checked against TranslationsXamlCatalog; if its source comes from another file or code, set TranslationsValidateXaml=\"false\" on its Page item."
                    : string.Empty));
            return;
        }
        // RichMessage's WPF inline adapter does not accept document content.
        bool document = message.Contract.Content == Rmf2DocumentProfileV5.Document;
        bool inlineRich = message.Contract.Structured && !document;
        if (rich ? !inlineRich : message.Contract.Structured)
        {
            Report(TranslationsDiagnostics.XamlKind, location, document
                ? "Message '" + key + "' is a document; neither {rt:Message} nor TranslationProperties.RichMessage renders documents. Render it with WpfDocumentRenderer."
                : rich
                    ? "Message '" + key + "' is plain text, but TranslationProperties.RichMessage needs inline markup. Use {rt:Message " + key + "} instead."
                    : "Message '" + key + "' contains inline markup, which {rt:Message} cannot show as a string. Use rt:TranslationProperties.RichMessage=\"" + key + "\" on a TextBlock.");
            return;
        }
        if (count is not { } actual) return;
        IReadOnlyList<Rmf2ReadableNameV1> inputs = message.Inputs;
        if (names is null)
        {
            if (actual != inputs.Count)
                Report(TranslationsDiagnostics.XamlInputs, location, "Message '" + key + "' " + DescribeInputs(inputs, positional: true) + ", but " +
                    (actual == 0 ? "no Arg is set." : "Arg0" + (actual > 1 ? "..Arg" + (actual - 1) : string.Empty) + " " + (actual == 1 ? "is" : "are") + " set."));
            else if (actual > 1)
                Report(TranslationsDiagnostics.XamlPositionalInputs, location, "Message '" + key + "' binds " + actual + " inputs by position: " + Positional(inputs) +
                    ". Positions follow the generated parameters, sorted by name rather than by their order in the text; use named MessageInput entries so each value binds by name.");
            return;
        }
        string[] unknown = names.Where(name => !inputs.Any(input => input.Identifier == name)).ToArray();
        string[] missing = inputs.Select(static input => input.Identifier).Where(name => !names.Contains(name, StringComparer.Ordinal)).ToArray();
        if (unknown.Length == 0 && missing.Length == 0) return;
        var problems = new List<string>();
        if (unknown.Length > 0) problems.Add("has no input " + string.Join(", ", unknown.Select(name =>
            "'" + name + "'" + (inputs.FirstOrDefault(input => input.Source == name && input.Identifier != name) is { Identifier: { } readable } ? " (use its readable name '" + readable + "')" : string.Empty))));
        if (missing.Length > 0) problems.Add("is missing " + string.Join(", ", missing.Select(static name => "'" + name + "'")));
        Report(TranslationsDiagnostics.XamlInputs, location, "Message '" + key + "' " + string.Join(" and ", problems) + "; it " + DescribeInputs(inputs, positional: false) + ".");
    }

    private static string DescribeInputs(IReadOnlyList<Rmf2ReadableNameV1> inputs, bool positional) => inputs.Count switch
    {
        0 => "takes no inputs",
        _ => "takes " + inputs.Count + (inputs.Count == 1 ? " input: " : " inputs: ") +
            (positional ? Positional(inputs) : string.Join(", ", inputs.Select(static input => input.Identifier))) +
            (positional && inputs.Count > 4 ? " (more than four need named MessageInput entries)" : string.Empty),
    };

    private static string Positional(IReadOnlyList<Rmf2ReadableNameV1> inputs) =>
        string.Join(", ", inputs.Select(static (input, index) => index < 4 ? "Arg" + index + "=" + input.Identifier : input.Identifier));

    // The closest readable key: same name ignoring case or separators, else a small edit distance.
    private string? Suggest(string key)
    {
        string Normalize(string value) => value.Replace('.', '_').Replace('-', '_').ToLowerInvariant();
        string normalized = Normalize(key);
        string? best = null;
        int bestDistance = Math.Max(1, Math.Min(3, key.Length / 3)) + 1;
        foreach (string candidate in _catalog.Messages.Keys.OrderBy(static candidate => candidate, StringComparer.Ordinal))
        {
            int distance = Normalize(candidate) == normalized ? 0 : Distance(normalized, Normalize(candidate), bestDistance);
            if (distance < bestDistance) { best = candidate; bestDistance = distance; }
        }
        return best;
    }

    // Levenshtein distance, or at least limit when it is limit or more.
    private static int Distance(string left, string right, int limit)
    {
        if (Math.Abs(left.Length - right.Length) >= limit) return limit;
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            int rowMinimum = i;
            for (int j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                rowMinimum = Math.Min(rowMinimum, current[j]);
            }
            if (rowMinimum >= limit) return limit;
            (previous, current) = (current, previous);
        }
        return Math.Min(previous[right.Length], limit);
    }

    // One informational report per explicit source, at its declaration, when it hides a static key.
    private void ReportSkipped(XObject source, string key, XObject location, string catalog)
    {
        if (!_reportedSources.Add(source)) return;
        string text = source == location
            ? "Message '" + key + "' sets its own Source, so it is not checked against catalog '" + catalog + "'. If that Source provides catalog '" + catalog +
                "', set Catalog=\"" + catalog + "\" on the file's Page or TranslationXaml item to check it."
            : (source == _fileSource
                ? "TranslationProperties.Source is set by a style Setter here, which can apply to any element, so keys in this whole file are"
                : "TranslationProperties.Source is set here, so keys in this element's content and in this file's templates, styles and resources are") +
              " not checked against catalog '" + catalog + "' (first: '" + key + "' at line " + Line(location) + "). If this source provides catalog '" + catalog +
              "', set Catalog=\"" + catalog + "\" on the file's Page or TranslationXaml item to check them; keys of another catalog cannot be checked.";
        Report(TranslationsDiagnostics.XamlSourceNotChecked, source, text);
    }

    private static int Line(XObject source) => source is IXmlLineInfo { } info && info.HasLineInfo() ? info.LineNumber : 1;

    private static string? ElementValue(XElement? element)
    {
        if (element is null) return null;
        if (!element.HasElements) return StaticValue(element.Value);
        if (element.Elements().Count() != 1) return null;
        XElement child = element.Elements().Single();
        if (child.Name.NamespaceName != "http://schemas.microsoft.com/winfx/2006/xaml") return null;
        return child.Name.LocalName switch { "String" => StaticValue(child.Value), "Null" or "NullExtension" => NullValue, _ => null };
    }

    private static bool IsNullValue(XElement element, string value)
    {
        value = value.Trim();
        if (value == NullValue) return true;
        if (!value.StartsWith('{') || !value.EndsWith('}')) return false;
        string type = value.Substring(1, value.Length - 2).Trim();
        int colon = type.IndexOf(':');
        return colon > 0 && type.Substring(colon + 1) is "Null" or "NullExtension" &&
            element.GetNamespaceOfPrefix(type.Substring(0, colon))?.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml";
    }

    private static string? StaticValue(string? value)
    {
        if (value is null) return null;
        value = value.Trim();
        if (value.StartsWith("{}", StringComparison.Ordinal)) return value.Substring(2);
        if (value.Length > 1 && value[0] is '\'' or '"' && value[^1] == value[0]) return value.Substring(1, value.Length - 2);
        return value.StartsWith('{') ? null : value;
    }

    private static bool IsMessage(string name) => name is "Message" or "MessageExtension";
    private static bool IsMessageMarkup(XElement element, string name) => Resolves(element, name, "Message") || Resolves(element, name, "MessageExtension");
    private static bool Resolves(XElement element, string name, string localName)
    {
        name = name.Trim();
        int colon = name.IndexOf(':');
        return colon >= 0 ? name.Substring(colon + 1) == localName && IsRunic(element.GetNamespaceOfPrefix(name.Substring(0, colon))?.NamespaceName) :
            name == localName && IsRunic(element.GetDefaultNamespace().NamespaceName);
    }
    private static bool IsRunic(string? ns) => ns is "clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf" or "clr-namespace:Runic.Translations.Wpf";

    private static int MatchingBrace(string value)
    {
        int depth = 0;
        char quote = '\0';
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '\\') { index++; continue; }
            if (quote != '\0') { if (current == quote) quote = '\0'; continue; }
            if (current is '\'' or '"') { quote = current; continue; }
            if (current == '{') depth++;
            if (current == '}' && --depth == 0) return index;
        }
        return -1;
    }

    private static bool TrySplit(string value, char separator, out List<string> parts)
    {
        parts = [];
        int depth = 0, start = 0;
        char quote = '\0';
        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];
            if (current == '\\') { index++; continue; }
            if (quote != '\0') { if (current == quote) quote = '\0'; continue; }
            if (current is '\'' or '"') { quote = current; continue; }
            if (current == '{') depth++;
            if (current == '}' && --depth < 0) return false;
            if (depth == 0 && current == separator) { parts.Add(value.Substring(start, index - start).Trim()); start = index + 1; }
        }
        parts.Add(value.Substring(start).Trim());
        return depth == 0 && quote == '\0';
    }

    private void Report(DiagnosticDescriptor descriptor, XObject? source, string message, int line = 1, int column = 1)
    {
        if (source is IXmlLineInfo { } info && info.HasLineInfo()) { line = info.LineNumber; column = info.LinePosition; }
        int lineIndex = Math.Clamp(line - 1, 0, _text.Lines.Count - 1);
        TextLine textLine = _text.Lines[lineIndex];
        int start = textLine.Start + Math.Clamp(column - 1, 0, textLine.Span.Length);
        TextSpan span = new(start, Math.Min(1, _text.Length - start));
        _context.ReportDiagnostic(Diagnostic.Create(descriptor, Location.Create(_input.Path, span, _text.Lines.GetLinePositionSpan(span)), message));
    }
}
