using System;
using System.Collections.Generic;

namespace RunicTextResources;

/// <summary>Semantic output node kinds. Hosts decide how named elements render.</summary>
public enum LocalizedTextContentNodeKind
{
    /// <summary>Plain localized text.</summary>
    Text,
    /// <summary>Start of a named semantic element.</summary>
    ElementStart,
    /// <summary>End of a named semantic element.</summary>
    ElementEnd,
}

/// <summary>One immutable node in safe structured localized output.</summary>
public sealed class LocalizedTextContentNode
{
    private readonly CompiledTextMarkupProperty[] _attributes;

    internal LocalizedTextContentNode(LocalizedTextContentNodeKind kind, string value, CompiledTextMarkupProperty[]? attributes = null)
    {
        Kind = kind;
        Value = value;
        _attributes = attributes is null ? Array.Empty<CompiledTextMarkupProperty>() : (CompiledTextMarkupProperty[])attributes.Clone();
    }

    /// <summary>The semantic node kind.</summary>
    public LocalizedTextContentNodeKind Kind { get; }
    /// <summary>Plain text or semantic element name.</summary>
    public string Value { get; }
    /// <summary>Semantic attributes; never HTML attributes without host validation.</summary>
    public ReadOnlyMemory<CompiledTextMarkupProperty> Attributes => (CompiledTextMarkupProperty[])_attributes.Clone();
}

/// <summary>Safe structured localized output that has no implicit HTML conversion.</summary>
public sealed class LocalizedTextContent
{
    private readonly LocalizedTextContentNode[] _nodes;

    internal LocalizedTextContent(IReadOnlyList<LocalizedTextContentNode> nodes)
    {
        _nodes = new LocalizedTextContentNode[nodes.Count];
        for (int index = 0; index < nodes.Count; index++) _nodes[index] = nodes[index];
    }

    /// <summary>The balanced semantic node stream.</summary>
    public ReadOnlyMemory<LocalizedTextContentNode> Nodes => (LocalizedTextContentNode[])_nodes.Clone();
}
