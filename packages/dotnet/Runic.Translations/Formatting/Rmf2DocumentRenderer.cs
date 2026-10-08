using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Runic.Translations;

/// <summary>Structured localized output of an RMF2 document message (document profile v1).</summary>
public sealed class LocalizedDocumentContent
{
    /// <summary>
    /// Wraps formatted content of a document message. Generated accessors call this with the result of
    /// <c>FormatContent</c>; the document structure is checked against the message contract at render time.
    /// </summary>
    public LocalizedDocumentContent(LocalizedTextContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    internal LocalizedTextContent Content { get; }

    /// <summary>The effective content locale, including whole-message fallback.</summary>
    public string Locale => Content.Locale;

    /// <summary>The balanced semantic node stream of blocks and inline content.</summary>
    public ReadOnlyMemory<LocalizedTextContentNode> Nodes => Content.Nodes;
}

/// <summary>Structured document output of a message whose slots are bound through <typeparamref name="TSlots"/>.</summary>
/// <typeparam name="TSlots">The message's generated slot type.</typeparam>
public sealed class LocalizedDocumentContent<TSlots> where TSlots : class, IRmf2SlotBindings<TSlots>
{
    /// <summary>
    /// Wraps formatted document content. Generated accessors call this; wrapping another message's content by hand
    /// is an explicit opt-out that is only validated against the contract of <c>TSlots.MessageKey</c> at render time.
    /// </summary>
    public LocalizedDocumentContent(LocalizedDocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    /// <summary>The untyped document content.</summary>
    public LocalizedDocumentContent Content { get; }

    /// <summary>The effective content locale, including whole-message fallback.</summary>
    public string Locale => Content.Locale;

    /// <summary>The balanced semantic node stream of blocks and inline content.</summary>
    public ReadOnlyMemory<LocalizedTextContentNode> Nodes => Content.Nodes;

    /// <summary>Binds application-owned slot values. The result can be cached and rendered more than once.</summary>
    public BoundLocalizedDocumentContent Bind(TSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        string key = TSlots.MessageKey ?? throw new InvalidOperationException("Typed slot bindings must declare a message key.");
        var bindings = new Dictionary<string, MarkupBinding>(StringComparer.Ordinal);
        slots.CopyTo(bindings);
        // Snapshot after CopyTo: a hand-written slot type may keep the destination and must not mutate the bound values.
        var snapshot = new Dictionary<string, MarkupBinding>(bindings, StringComparer.Ordinal);
        return new BoundLocalizedDocumentContent(key, Content, new ReadOnlyDictionary<string, MarkupBinding>(snapshot));
    }

    /// <summary>Returns the untyped content for the dynamic string-key rendering path.</summary>
    public static implicit operator LocalizedDocumentContent(LocalizedDocumentContent<TSlots> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Content;
    }
}

/// <summary>Document output together with its message key and typed slot bindings.</summary>
public sealed class BoundLocalizedDocumentContent
{
    internal BoundLocalizedDocumentContent(string key, LocalizedDocumentContent content, IReadOnlyDictionary<string, MarkupBinding> slots)
    {
        Key = key;
        Content = content;
        Slots = slots;
    }

    /// <summary>The canonical message key used to select the markup contract.</summary>
    public string Key { get; }

    /// <summary>The document content.</summary>
    public LocalizedDocumentContent Content { get; }

    /// <summary>The bound slot values, keyed ordinally by RMF2 slot ID.</summary>
    public IReadOnlyDictionary<string, MarkupBinding> Slots { get; }
}

/// <summary>One semantic block of document content. Hosts map blocks to their own toolkit.</summary>
/// <param name="Name">The canonical contract name, for example <c>runic:p</c>.</param>
/// <param name="Options">The resolved block options, defaults included.</param>
/// <param name="Blocks">Child blocks: the items of a list, otherwise empty.</param>
/// <param name="Inlines">Inline content of a paragraph, heading or list item, otherwise empty.</param>
/// <param name="Binding">The functional binding of the block; always <see langword="null"/> in document profile v1.</param>
/// <param name="Occurrence">The block's structure path, for example <c>ul[1]/li[3]</c>. It is the same in every locale for the same source variant.</param>
/// <param name="Implicit">Whether the block is an implicit paragraph; always <see langword="false"/> in document profile v1.</param>
public sealed record DocumentBlock(string Name, IReadOnlyDictionary<string, string> Options, IReadOnlyList<DocumentBlock> Blocks,
    IReadOnlyList<InlineMarkupRun> Inlines, MarkupBinding? Binding, string Occurrence, bool Implicit);

/// <summary>Renders RMF2 document messages to semantic blocks over a linked markup contract.</summary>
public sealed class Rmf2DocumentRenderer
{
    private static readonly IReadOnlyList<DocumentBlock> NoBlocks = Array.Empty<DocumentBlock>();
    private static readonly IReadOnlyList<InlineMarkupRun> NoRuns = Array.Empty<InlineMarkupRun>();
    private readonly Rmf2MarkupContract _contract;

    /// <summary>Creates a renderer over a linked markup contract.</summary>
    public Rmf2DocumentRenderer(Rmf2MarkupContract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        _contract = contract;
    }

    /// <summary>Builds semantic blocks for typed bound document content.</summary>
    public IReadOnlyList<DocumentBlock> Render(BoundLocalizedDocumentContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return RenderCore(content.Key, content.Content, content.Slots);
    }

    /// <summary>Builds semantic blocks for the selected variant, checking all potential slot bindings.</summary>
    public IReadOnlyList<DocumentBlock> Render(string key, LocalizedDocumentContent content, IReadOnlyDictionary<string, MarkupBinding>? slots = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return RenderCore(key, content, slots ?? new Dictionary<string, MarkupBinding>());
    }

    /// <summary>Projects typed bound document content to plain text.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overloads take unrelated first parameters (string key vs. bound content), so calls cannot become ambiguous.")]
    public string ToPlainText(BoundLocalizedDocumentContent content, Rmf2PlainTextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Rmf2PlainText.Document(_contract, Render(content), content.Content.Locale, options ?? Rmf2PlainTextOptions.Default);
    }

    /// <summary>Projects document content to plain text: blocks are separated by a blank line and list items by a line break.</summary>
    [SuppressMessage("ApiDesign", "RS0026:Do not add multiple public overloads with optional parameters", Justification = "The overloads take unrelated first parameters (string key vs. bound content), so calls cannot become ambiguous.")]
    public string ToPlainText(string key, LocalizedDocumentContent content, IReadOnlyDictionary<string, MarkupBinding>? slots = null, Rmf2PlainTextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Rmf2PlainText.Document(_contract, Render(key, content, slots), content.Locale, options ?? Rmf2PlainTextOptions.Default);
    }

    private IReadOnlyList<DocumentBlock> RenderCore(string key, LocalizedDocumentContent content, IReadOnlyDictionary<string, MarkupBinding> slots)
    {
        Rmf2MarkupContract.Message message = _contract.Get(key);
        if (!message.Document) throw new TranslationFormatException("'" + key + "' is an inline message; render it with Rmf2InlineRenderer.");
        var reader = new Rmf2RunReader<MarkupBinding>(_contract, message, content.Content.Nodes.ToArray(), slots);
        IReadOnlyList<DocumentBlock> blocks = Blocks(reader, null, string.Empty, 0);
        reader.Finish();
        return blocks;
    }

    // Reads the blocks of the document root (closing null) or of one list.
    private static ReadOnlyCollection<DocumentBlock> Blocks(Rmf2RunReader<MarkupBinding> reader, string? closing, string path, int depth)
    {
        var result = new List<DocumentBlock>();
        var siblings = new Dictionary<string, int>(StringComparer.Ordinal);
        while (!reader.AtEnd)
        {
            LocalizedTextContentNode node = reader.Next();
            if (node.Kind == LocalizedTextContentNodeKind.ElementEnd)
            {
                if (node.Value != closing) throw new TranslationFormatException("Unbalanced document content.");
                return result.AsReadOnly();
            }
            if (node.Kind != LocalizedTextContentNodeKind.ElementStart) throw new TranslationFormatException("Document content has text or an inline element outside a block.");
            Rmf2MarkupContract.Tag tag = reader.Tag(node);
            if (tag.Placement != (closing is null ? "block" : "list-item")) throw new TranslationFormatException("Element '" + node.Value + "' is not allowed here.");
            if (depth + 1 > Rmf2MarkupContract.MaximumDepth) throw new TranslationFormatException("RMF2 nesting limit exceeded.");
            IReadOnlyDictionary<string, string> options = reader.Options(node, tag, out _);
            string name = node.Value.StartsWith("runic:", StringComparison.Ordinal) ? node.Value.Substring(6) : node.Value;
            int index = siblings[name] = siblings.GetValueOrDefault(name) + 1;
            string occurrence = (path.Length == 0 ? string.Empty : path + "/") + name + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
            result.Add(tag.Children == "list-items"
                ? new DocumentBlock(node.Value, options, Blocks(reader, node.Value, occurrence, depth + 1), NoRuns, null, occurrence, false)
                : new DocumentBlock(node.Value, options, NoBlocks, reader.ReadInline(node.Value, false, depth + 1, occurrence + "/"), null, occurrence, false));
        }
        if (closing is not null) throw new TranslationFormatException("Unclosed document content.");
        return result.AsReadOnly();
    }
}
