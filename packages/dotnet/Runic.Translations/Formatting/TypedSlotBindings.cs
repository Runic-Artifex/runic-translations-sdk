using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Runic.Translations;

/// <summary>
/// Typed slot bindings for one structured RMF2 message. Generated per message as
/// <c>&lt;ClassName&gt;Slots.&lt;key&gt;</c>; hand-written implementations must mirror that shape.
/// </summary>
/// <typeparam name="TSelf">The implementing slot type.</typeparam>
public interface IRmf2SlotBindings<TSelf> where TSelf : class, IRmf2SlotBindings<TSelf>
{
    /// <summary>The canonical message key whose markup contract these slots satisfy.</summary>
    static abstract string MessageKey { get; }

    /// <summary>Adds every slot binding, keyed by its RMF2 slot ID.</summary>
    void CopyTo(IDictionary<string, MarkupBinding> destination);
}

/// <summary>Structured localized output of a message whose slots are bound through <typeparamref name="TSlots"/>.</summary>
/// <typeparam name="TSlots">The message's generated slot type.</typeparam>
public sealed class LocalizedTextContent<TSlots> where TSlots : class, IRmf2SlotBindings<TSlots>
{
    /// <summary>
    /// Wraps formatted content. Generated accessors call this; wrapping another message's content by hand is an
    /// explicit opt-out that is only validated against the contract of <c>TSlots.MessageKey</c> at render time.
    /// </summary>
    public LocalizedTextContent(LocalizedTextContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content;
    }

    /// <summary>The untyped structured content.</summary>
    public LocalizedTextContent Content { get; }

    /// <summary>The effective content locale, including whole-message fallback.</summary>
    public string Locale => Content.Locale;

    /// <summary>The balanced semantic node stream.</summary>
    public ReadOnlyMemory<LocalizedTextContentNode> Nodes => Content.Nodes;

    /// <summary>Binds application-owned slot values. The result can be cached and rendered more than once.</summary>
    public BoundLocalizedTextContent Bind(TSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        string key = TSlots.MessageKey ?? throw new InvalidOperationException("Typed slot bindings must declare a message key.");
        var bindings = new Dictionary<string, MarkupBinding>(StringComparer.Ordinal);
        slots.CopyTo(bindings);
        // Snapshot after CopyTo: a hand-written slot type may keep the destination and must not mutate the bound values.
        var snapshot = new Dictionary<string, MarkupBinding>(bindings, StringComparer.Ordinal);
        return new BoundLocalizedTextContent(key, Content, new ReadOnlyDictionary<string, MarkupBinding>(snapshot));
    }

    /// <summary>Returns the untyped content for the dynamic string-key rendering path.</summary>
    public static implicit operator LocalizedTextContent(LocalizedTextContent<TSlots> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Content;
    }
}

/// <summary>Structured localized output together with its message key and typed slot bindings.</summary>
public sealed class BoundLocalizedTextContent
{
    internal BoundLocalizedTextContent(string key, LocalizedTextContent content, IReadOnlyDictionary<string, MarkupBinding> slots)
    {
        Key = key;
        Content = content;
        Slots = slots;
    }

    /// <summary>The canonical message key used to select the markup contract.</summary>
    public string Key { get; }

    /// <summary>The structured content.</summary>
    public LocalizedTextContent Content { get; }

    /// <summary>The bound slot values, keyed ordinally by RMF2 slot ID.</summary>
    public IReadOnlyDictionary<string, MarkupBinding> Slots { get; }
}
