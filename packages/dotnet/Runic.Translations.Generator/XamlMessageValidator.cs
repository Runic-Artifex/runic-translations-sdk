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
    private bool _defaultCatalogKnown;

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
        // A source in a resource/style/template may flow to another element. Without
        // a per-file catalog assertion, stay conservative for the entire file.
        _defaultCatalogKnown = !elements.Any(HasSourceOverride);
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

    private static bool HasSourceOverride(XElement element) =>
        (IsRunic(element.Name.NamespaceName) && element.Name.LocalName is "TranslationProperties.Source" or "Message.Source" or "MessageExtension.Source") ||
        CompiledAttributes(element).Any(attribute =>
            (IsRunic(attribute.Name.NamespaceName) && attribute.Name.LocalName == "TranslationProperties.Source") ||
            (IsRunic(element.Name.NamespaceName) && IsMessage(element.Name.LocalName) && attribute.Name.LocalName == "Source") ||
            (attribute.Name.LocalName == "Property" && IsSourceProperty(element, attribute.Value)) ||
            HasMessageSourceMarkup(element, attribute.Value)) ||
        (element.Name.LocalName == "Setter.Property" && element.Elements().Any(child =>
            child.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml" && (child.Name.LocalName is "Static" or "StaticExtension") &&
            Resolves(child, StaticValue(child.Attribute("Member")?.Value) ?? string.Empty, "TranslationProperties.SourceProperty")));

    private static bool HasMessageSourceMarkup(XElement element, string value)
    {
        if (!TryReadMarkup(value, out string type, out List<string> parts, out _)) return false;
        foreach (string part in parts)
        {
            if (!TrySplit(part, '=', out List<string> assignment)) continue;
            if (IsMessageMarkup(element, type) && assignment.Count > 1 && assignment[0] == "Source") return true;
            string nested = assignment.Count == 1 ? part : string.Join("=", assignment.Skip(1));
            if (HasMessageSourceMarkup(element, nested)) return true;
        }
        return false;
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
        if ((hasInputs && args.Count > 0) || (args.Count > 0 && args.Max() + 1 != args.Count) || names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            Report(TranslationsDiagnostics.XamlInputs, location, "Message inputs must use contiguous Arg0..Arg3 or unique named MessageInput entries; do not mix the forms.");
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
        string? catalog = !string.IsNullOrWhiteSpace(_input.Catalog) ? _input.Catalog :
            _defaultCatalogKnown && !explicitSource ? _input.DefaultCatalog : null;
        if (string.IsNullOrWhiteSpace(catalog)) return;
        if (catalog != _catalog.Id)
        {
            Report(TranslationsDiagnostics.XamlDeclaration, location, "XAML catalog '" + catalog + "' is not the local TranslationProject catalog '" + _catalog.Id +
                "'. External catalogs are not checked; set TranslationsValidateXaml=\"false\" on the file's Page or TranslationXaml item to skip it.");
            return;
        }
        if (!_catalog.Messages.TryGetValue(key, out Rmf2ReadableMessageV1? message))
        {
            Report(TranslationsDiagnostics.XamlKey, location, "'" + key + "' is not a readable message of catalog '" + catalog + "'. Use its flattened readable name." +
                (string.IsNullOrWhiteSpace(_input.Catalog)
                    ? " The file was checked against TranslationsXamlCatalog; if its source comes from another file or code, set TranslationsValidateXaml=\"false\" on its Page item."
                    : string.Empty));
            return;
        }
        // RichMessage's WPF inline adapter does not accept document content.
        bool inlineRich = message.Contract.Structured && message.Contract.Content != Rmf2DocumentProfileV5.Document;
        if (rich ? !inlineRich : message.Contract.Structured)
        {
            Report(TranslationsDiagnostics.XamlKind, location, "Message '" + key + "' cannot be used with " + (rich ? "TranslationProperties.RichMessage" : "Message") + "; choose the adapter for its plain, inline or document content.");
            return;
        }
        if (count is { } actual && (actual != message.Inputs.Count ||
            names is not null && names.Any(name => !message.Inputs.Any(input => input.Identifier == name))))
            Report(TranslationsDiagnostics.XamlInputs, location, "Message '" + key + "' requires " + message.Inputs.Count + " input(s): " +
                string.Join(", ", message.Inputs.Select(static input => input.Identifier)) + ".");
    }

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
