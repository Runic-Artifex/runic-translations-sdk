using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>Semantic output node kinds. Hosts decide how named elements render.</summary>
public enum LocalizedTextContentNodeKind
{
    /// <summary>Plain localized text.</summary>
    Text,
    /// <summary>Start of a named semantic element.</summary>
    ElementStart,
    /// <summary>End of a named semantic element.</summary>
    ElementEnd,
    /// <summary>A standalone RMF2 element, distinct from an empty pair.</summary>
    ElementStandalone,
}

/// <summary>One immutable node in safe structured localized output.</summary>
public sealed class LocalizedTextContentNode
{
    private readonly CompiledTextMarkupProperty[] _attributes;
    private readonly CompiledRmf2Annotation[] _annotations;

    internal LocalizedTextContentNode(LocalizedTextContentNodeKind kind, string value, CompiledTextMarkupProperty[]? attributes = null)
        : this(kind, value, attributes, null) { }

    internal LocalizedTextContentNode(LocalizedTextContentNodeKind kind, string value, CompiledTextMarkupProperty[]? attributes,
        CompiledRmf2Annotation[]? annotations)
    {
        Kind = kind;
        Value = value;
        _attributes = attributes is null ? Array.Empty<CompiledTextMarkupProperty>() : (CompiledTextMarkupProperty[])attributes.Clone();
        _annotations = annotations is null ? [] : (CompiledRmf2Annotation[])annotations.Clone();
    }

    /// <summary>The semantic node kind.</summary>
    public LocalizedTextContentNodeKind Kind { get; }
    /// <summary>Plain text or semantic element name.</summary>
    public string Value { get; }
    /// <summary>Semantic attributes; never HTML attributes without host validation.</summary>
    public ReadOnlyMemory<CompiledTextMarkupProperty> Attributes => (CompiledTextMarkupProperty[])_attributes.Clone();
    /// <summary>Ordered inert v5 annotations; separate from active renderer options.</summary>
    public ReadOnlyMemory<CompiledRmf2Annotation> Annotations => (CompiledRmf2Annotation[])_annotations.Clone();
}

/// <summary>Safe structured localized output that has no implicit HTML conversion.</summary>
public sealed class LocalizedTextContent
{
    private readonly LocalizedTextContentNode[] _nodes;

    internal LocalizedTextContent(IReadOnlyList<LocalizedTextContentNode> nodes, string locale = "en")
    {
        Locale = locale;
        _nodes = new LocalizedTextContentNode[nodes.Count];
        for (int index = 0; index < nodes.Count; index++) _nodes[index] = nodes[index];
    }

    /// <summary>The effective content locale, including whole-message fallback.</summary>
    public string Locale { get; }

    /// <summary>The balanced semantic node stream.</summary>
    public ReadOnlyMemory<LocalizedTextContentNode> Nodes => (LocalizedTextContentNode[])_nodes.Clone();
}
