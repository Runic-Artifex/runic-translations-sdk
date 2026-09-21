using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>A normalized v5 value: input, local, string-literal or number-literal.</summary>
public sealed class CompiledRmf2Value
{
    /// <summary>Creates a value, verifying exact authored/canonical numeric agreement.</summary>
    public CompiledRmf2Value(string kind, string value, string? canonical = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (kind is not ("input" or "local" or "string-literal" or "number-literal")) throw new ArgumentException("Unknown v5 value kind.", nameof(kind));
        if (kind is "input" or "local") Rmf2RuntimeValidation.Name(value);
        if (kind == "number-literal")
        {
            if (!Rmf2RuntimeDecimal.TryCanonicalize(value, out string exact) || canonical != exact)
                throw new ArgumentException("Number literal has an invalid or inconsistent canonical value.", nameof(canonical));
        }
        else if (canonical is not null) throw new ArgumentException("Only numeric literals have a canonical field.", nameof(canonical));
        Kind = kind; Value = value; Canonical = canonical;
    }
    /// <summary>The normalized value tag.</summary>
    public string Kind { get; }
    /// <summary>The NFC reference name or decoded authored literal.</summary>
    public string Value { get; }
    /// <summary>The exact portable number, present only for a numeric literal.</summary>
    public string? Canonical { get; }
}

/// <summary>An ordered typed formatter or markup option.</summary>
public sealed class CompiledRmf2Option
{
    /// <summary>Creates an option.</summary>
    public CompiledRmf2Option(string name, CompiledRmf2Value value)
    { Rmf2RuntimeValidation.Name(name, true); Name = name; Value = value ?? throw new ArgumentNullException(nameof(value)); }
    /// <summary>The option name.</summary>
    public string Name { get; }
    /// <summary>The literal or typed reference.</summary>
    public CompiledRmf2Value Value { get; }
}

/// <summary>An inert v5 annotation; absent and empty literal values remain distinct.</summary>
public sealed class CompiledRmf2Annotation
{
    /// <summary>Creates an inert annotation with an optional literal value.</summary>
    public CompiledRmf2Annotation(string name, CompiledRmf2Value? value = null)
    {
        Rmf2RuntimeValidation.Name(name, true);
        if (value?.Kind is "input" or "local") throw new ArgumentException("Annotations cannot reference values.", nameof(value));
        Name = name; Value = value;
    }
    /// <summary>The annotation name.</summary>
    public string Name { get; }
    /// <summary>The optional literal. Null means valueless.</summary>
    public CompiledRmf2Value? Value { get; }
}

/// <summary>A v5 expression over an underlying typed carrier and independent formatting metadata.</summary>
public sealed class CompiledRmf2Expression
{
    /// <summary>Creates an expression. A missing function inherits operand formatting.</summary>
    public CompiledRmf2Expression(CompiledRmf2Value operand, TextArgumentType valueType, string? function = null,
        IReadOnlyList<CompiledRmf2Option>? options = null, IReadOnlyList<CompiledRmf2Annotation>? annotations = null)
    {
        Operand = operand ?? throw new ArgumentNullException(nameof(operand));
        Rmf2RuntimeValidation.Type(valueType);
        ValueType = valueType; Function = function;
        OptionArray = Rmf2RuntimeValidation.Copy(options, int.MaxValue);
        AnnotationArray = Rmf2RuntimeValidation.Copy(annotations, int.MaxValue);
        Rmf2RuntimeValidation.UniqueOptions(OptionArray);
        Rmf2RuntimeValidation.UniqueAnnotations(AnnotationArray);
        Rmf2RuntimeValidation.Function(this);
    }
    /// <summary>The underlying value reference or literal.</summary>
    public CompiledRmf2Value Operand { get; }
    /// <summary>The underlying carrier, unchanged by formatting.</summary>
    public TextArgumentType ValueType { get; }
    /// <summary>The explicit function, or null for inheritance.</summary>
    public string? Function { get; }
    /// <summary>Ordered typed options.</summary>
    public ReadOnlyMemory<CompiledRmf2Option> Options => (CompiledRmf2Option[])OptionArray.Clone();
    /// <summary>Ordered inert annotations retained at the runtime model boundary.</summary>
    public ReadOnlyMemory<CompiledRmf2Annotation> Annotations => (CompiledRmf2Annotation[])AnnotationArray.Clone();
    internal CompiledRmf2Option[] OptionArray { get; }
    internal CompiledRmf2Annotation[] AnnotationArray { get; }
}

/// <summary>A v5 caller input, independently of locale formatting choices.</summary>
public sealed class CompiledRmf2Input
{
    /// <summary>Creates a caller contract entry.</summary>
    public CompiledRmf2Input(string name, TextArgumentType type)
    { Rmf2RuntimeValidation.Name(name); Rmf2RuntimeValidation.Type(type); Name = name; Type = type; }
    /// <summary>The NFC input identity.</summary>
    public string Name { get; }
    /// <summary>The required underlying carrier.</summary>
    public TextArgumentType Type { get; }
}

/// <summary>An ordered input or local declaration.</summary>
public sealed class CompiledRmf2Declaration
{
    /// <summary>Creates a declaration with kind input or local.</summary>
    public CompiledRmf2Declaration(string kind, string name, CompiledRmf2Expression expression)
    {
        if (kind is not ("input" or "local")) throw new ArgumentException("Unknown declaration kind.", nameof(kind));
        Rmf2RuntimeValidation.Name(name); Kind = kind; Name = name;
        Expression = expression ?? throw new ArgumentNullException(nameof(expression));
    }
    /// <summary>The declaration kind.</summary>
    public string Kind { get; }
    /// <summary>The declared NFC identity.</summary>
    public string Name { get; }
    /// <summary>The value and its formatting metadata.</summary>
    public CompiledRmf2Expression Expression { get; }
}

/// <summary>A resolved selector identity and selection function.</summary>
public sealed class CompiledRmf2Selector
{
    /// <summary>Creates a selector with exact, plural or ordinal selection.</summary>
    public CompiledRmf2Selector(CompiledRmf2Value value, TextArgumentType type, string function)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Kind is not ("input" or "local") || type is not (TextArgumentType.String or TextArgumentType.Bool or TextArgumentType.Int or TextArgumentType.Number) ||
            function is not ("exact" or "plural" or "ordinal") || (type is TextArgumentType.String or TextArgumentType.Bool && function != "exact"))
            throw new ArgumentException("Invalid v5 selector.", nameof(value));
        Value = value; Type = type; Function = function;
    }
    /// <summary>The input or local reference.</summary>
    public CompiledRmf2Value Value { get; }
    /// <summary>The underlying selector carrier.</summary>
    public TextArgumentType Type { get; }
    /// <summary>The resolved selection function.</summary>
    public string Function { get; }
}

/// <summary>A tagged wildcard or literal key, preserving literal stars.</summary>
public sealed class CompiledRmf2Key
{
    /// <summary>Creates a wildcard when value is null, otherwise an exact or category literal.</summary>
    public CompiledRmf2Key(string? value = null, string? canonical = null)
    {
        if (canonical is not null && (value is null || !Rmf2RuntimeDecimal.TryCanonicalize(value, out string exact) || canonical != exact))
            throw new ArgumentException("Invalid canonical selector key.", nameof(canonical));
        Value = value; Canonical = canonical;
    }
    /// <summary>Null denotes wildcard; all other strings are literal values.</summary>
    public string? Value { get; }
    /// <summary>The canonical numeric exact key, when present.</summary>
    public string? Canonical { get; }
}

/// <summary>A closed v5 pattern node containing text, an expression, or a linked markup event.</summary>
public sealed class CompiledRmf2Node
{
    /// <summary>Creates literal authored text.</summary>
    public CompiledRmf2Node(string text)
    { ArgumentNullException.ThrowIfNull(text); if (text.Length > 65536) throw new ArgumentException("Text node exceeds the v5 limit.", nameof(text)); Kind = "text"; Value = text; OptionArray = []; AnnotationArray = []; }
    /// <summary>Creates an expression node.</summary>
    public CompiledRmf2Node(CompiledRmf2Expression expression)
    { Expression = expression ?? throw new ArgumentNullException(nameof(expression)); Kind = "expression"; Value = string.Empty; OptionArray = []; AnnotationArray = []; }
    /// <summary>Creates an open, close or standalone markup event after project contract linking.</summary>
    public CompiledRmf2Node(string name, string markupKind, IReadOnlyList<CompiledRmf2Option>? options = null,
        IReadOnlyList<CompiledRmf2Annotation>? annotations = null)
    {
        Rmf2RuntimeValidation.Name(name, true);
        if (markupKind is not ("open" or "close" or "standalone")) throw new ArgumentException("Invalid markup event.", nameof(markupKind));
        Kind = "markup"; Value = name; MarkupKind = markupKind;
        OptionArray = Rmf2RuntimeValidation.Copy(options, int.MaxValue); AnnotationArray = Rmf2RuntimeValidation.Copy(annotations, int.MaxValue);
        Rmf2RuntimeValidation.UniqueOptions(OptionArray);
        Rmf2RuntimeValidation.UniqueAnnotations(AnnotationArray);
    }
    /// <summary>The text, expression or markup tag.</summary>
    public string Kind { get; }
    /// <summary>Authored text or linked markup identity.</summary>
    public string Value { get; }
    /// <summary>The expression, only on expression nodes.</summary>
    public CompiledRmf2Expression? Expression { get; }
    /// <summary>The open, close or standalone event tag.</summary>
    public string? MarkupKind { get; }
    /// <summary>Ordered typed markup options.</summary>
    public ReadOnlyMemory<CompiledRmf2Option> Options => (CompiledRmf2Option[])OptionArray.Clone();
    /// <summary>Ordered inert annotations, including those on closing events.</summary>
    public ReadOnlyMemory<CompiledRmf2Annotation> Annotations => (CompiledRmf2Annotation[])AnnotationArray.Clone();
    internal CompiledRmf2Option[] OptionArray { get; }
    internal CompiledRmf2Annotation[] AnnotationArray { get; }
}

/// <summary>A v5 authored variant with a structural key vector.</summary>
public sealed class CompiledRmf2Variant
{
    /// <summary>Creates a variant, or a simple pattern with no keys.</summary>
    public CompiledRmf2Variant(IReadOnlyList<CompiledRmf2Key> keys, IReadOnlyList<CompiledRmf2Node> nodes)
    { ArgumentNullException.ThrowIfNull(keys); ArgumentNullException.ThrowIfNull(nodes); KeyArray = Rmf2RuntimeValidation.Copy(keys, 16); NodeArray = Rmf2RuntimeValidation.Copy(nodes, 4096); }
    /// <summary>Keys in selector order.</summary>
    public ReadOnlyMemory<CompiledRmf2Key> Keys => (CompiledRmf2Key[])KeyArray.Clone();
    /// <summary>Ordered pattern nodes.</summary>
    public ReadOnlyMemory<CompiledRmf2Node> Nodes => (CompiledRmf2Node[])NodeArray.Clone();
    internal CompiledRmf2Key[] KeyArray { get; }
    internal CompiledRmf2Node[] NodeArray { get; }
}

/// <summary>A validated executable v5 message, distinct from the v4 runtime model.</summary>
public sealed class CompiledRmf2Message
{
    /// <summary>Creates a message after normalized v5 lowering and project markup linking.</summary>
    public CompiledRmf2Message(IReadOnlyList<CompiledRmf2Input> inputs, IReadOnlyList<CompiledRmf2Declaration> declarations,
        IReadOnlyList<CompiledRmf2Selector> selectors, IReadOnlyList<CompiledRmf2Variant> variants, string? contentLocale = null)
    {
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(selectors); ArgumentNullException.ThrowIfNull(variants);
        InputArray = Rmf2RuntimeValidation.Copy(inputs, 32); DeclarationArray = Rmf2RuntimeValidation.Copy(declarations, 256);
        SelectorArray = Rmf2RuntimeValidation.Copy(selectors, 16); VariantArray = Rmf2RuntimeValidation.Copy(variants, 256);
        if (contentLocale is not null && !LocaleTag.TryCanonicalize(contentLocale, out _)) throw new ArgumentException("Invalid content locale.", nameof(contentLocale));
        ContentLocale = contentLocale;
        Rmf2RuntimeValidation.Message(this);
    }
    /// <summary>The ordinally sorted caller input contract.</summary>
    public ReadOnlyMemory<CompiledRmf2Input> Inputs => (CompiledRmf2Input[])InputArray.Clone();
    /// <summary>Ordered declarations.</summary>
    public ReadOnlyMemory<CompiledRmf2Declaration> Declarations => (CompiledRmf2Declaration[])DeclarationArray.Clone();
    /// <summary>Selectors in rank comparison order.</summary>
    public ReadOnlyMemory<CompiledRmf2Selector> Selectors => (CompiledRmf2Selector[])SelectorArray.Clone();
    /// <summary>Variants in authored order.</summary>
    public ReadOnlyMemory<CompiledRmf2Variant> Variants => (CompiledRmf2Variant[])VariantArray.Clone();
    /// <summary>The effective content locale when whole-message fallback applies.</summary>
    public string? ContentLocale { get; }
    /// <summary>Formats using typed carriers; caller formatting hints do not replace message functions.</summary>
    public string Format(ReadOnlySpan<TextArgument> arguments, string locale, int maximumOutputLength = TextPatternFormatter.DefaultMaximumOutputLength) =>
        Rmf2RuntimeEvaluator.Format(this, arguments, locale, maximumOutputLength);
    /// <summary>Produces linked semantic content. Annotations remain on this model and the corresponding output nodes.</summary>
    public LocalizedTextContent FormatContent(ReadOnlySpan<TextArgument> arguments, string locale, int maximumOutputLength = TextPatternFormatter.DefaultMaximumOutputLength) =>
        Rmf2RuntimeEvaluator.FormatContent(this, arguments, locale, maximumOutputLength);
    internal CompiledRmf2Input[] InputArray { get; }
    internal CompiledRmf2Declaration[] DeclarationArray { get; }
    internal CompiledRmf2Selector[] SelectorArray { get; }
    internal CompiledRmf2Variant[] VariantArray { get; }
    internal bool HasMarkup { get; set; }
}
