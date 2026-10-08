using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace Runic.Translations.Wpf;

/// <summary>Application-owned rendering for a declared custom inline contract.</summary>
public delegate Inline WpfMarkupFactory(InlineMarkupRun run, IReadOnlyList<Inline> children);

/// <summary>Links a contract once and creates fresh WPF inline trees on the owning UI thread.</summary>
public sealed class WpfInlineRenderer
{
    private static readonly ConditionalWeakTable<TextBlock, RenderLifetime> Lifetimes = new();
    private readonly Rmf2InlineRenderer _semantic;
    private readonly WpfInlineBuilder _inlines;

    /// <summary>Links immutable markup contracts and application-owned navigation, factories and themes.</summary>
    public WpfInlineRenderer(string contractJson, Action<Uri> navigate,
        IReadOnlyDictionary<string, WpfMarkupFactory>? custom = null, Action<string, Inline>? theme = null)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _semantic = new Rmf2InlineRenderer(contractJson);
        _inlines = new WpfInlineBuilder(contractJson, navigate, custom, theme, slotAutomationIds: false);
    }

    /// <summary>Validates and replaces content on the target control’s dispatcher thread.</summary>
    public void SetContent(TextBlock target, string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding> slots)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        Replace(target, _semantic.Render(key, content, slots), content.Locale);
    }

    /// <summary>Validates typed bound content and replaces it on the target control’s dispatcher thread.</summary>
    public void SetContent(TextBlock target, BoundLocalizedTextContent content)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        Replace(target, _semantic.Render(content), content.Content.Locale);
    }

    private void Replace(TextBlock target, IReadOnlyList<InlineMarkupRun> runs, string locale)
    {
        // Build first: a binding failure must not partially replace displayed content.
        var lifetime = new RenderLifetime();
        Inline[] inlines = runs.Select(run => _inlines.Create(run, locale, lifetime)).ToArray();
        ClearContent(target); Lifetimes.Add(target, lifetime);
        target.Inlines.Clear(); target.Inlines.AddRange(inlines);
        target.Language = XmlLanguage.GetLanguage(locale);
    }

    /// <summary>Clears rendered content and deactivates callbacks, including on retained detached controls.</summary>
    public static void ClearContent(TextBlock target)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        if (Lifetimes.TryGetValue(target, out var previous)) previous.Retire();
        Lifetimes.Remove(target); target.Inlines.Clear();
    }
}
