using System;
using System.Collections.Generic;
using System.Windows.Documents;

namespace Runic.Translations.Wpf;

/// <summary>Optional custom factories and theme for a <see cref="WpfInlineRenderer"/> built from a linked contract.</summary>
public sealed class WpfInlineRendererOptions
{
    /// <summary>Factories for declared custom inline contracts, by canonical contract name such as <c>shop:badge</c>.</summary>
    public IReadOnlyDictionary<string, WpfMarkupFactory>? Custom { get; init; }

    /// <summary>Called with the canonical contract name for every rendered markup inline. Plain text runs are not passed.</summary>
    public Action<string, Inline>? Theme { get; init; }
}

/// <summary>Optional custom inline factories and theme for a <see cref="WpfDocumentRenderer"/> built from a linked contract.</summary>
public sealed class WpfDocumentRendererOptions
{
    /// <summary>Factories for declared custom inline contracts, by canonical contract name such as <c>shop:badge</c>.</summary>
    public IReadOnlyDictionary<string, WpfMarkupFactory>? Custom { get; init; }

    /// <summary>
    /// Called with the canonical contract name for every rendered block and markup inline, for example to style
    /// <c>runic:h</c>. Plain text runs are not passed.
    /// </summary>
    public Action<string, TextElement>? Theme { get; init; }
}
