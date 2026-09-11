using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;

namespace Runic.Translations.Wpf;

/// <summary>Application-owned rendering for a declared custom inline contract.</summary>
public delegate Inline WpfMarkupFactory(InlineMarkupRun run, IReadOnlyList<Inline> children);

/// <summary>Links a contract once and creates fresh WPF inline trees on the owning UI thread.</summary>
public sealed class WpfInlineRenderer
{
    private static readonly ConditionalWeakTable<TextBlock, RenderLifetime> Lifetimes = new();
    private sealed class RenderLifetime { internal bool Active = true; }
    private readonly Rmf2InlineRenderer _semantic;
    private readonly ReadOnlyDictionary<string, WpfMarkupFactory> _custom;
    private readonly Action<Uri> _navigate;
    private readonly Action<string, Inline>? _theme;

    /// <summary>Links immutable markup contracts and application-owned navigation, factories and themes.</summary>
    public WpfInlineRenderer(string contractJson, Action<Uri> navigate,
        IReadOnlyDictionary<string, WpfMarkupFactory>? custom = null, Action<string, Inline>? theme = null)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _semantic = new Rmf2InlineRenderer(contractJson); _navigate = navigate; _theme = theme;
        var factories = custom is null ? new Dictionary<string, WpfMarkupFactory>(StringComparer.Ordinal) : new Dictionary<string, WpfMarkupFactory>(custom, StringComparer.Ordinal);
        using var contract = JsonDocument.Parse(contractJson);
        foreach (var factory in factories)
            if (factory.Value is null || factory.Key.StartsWith("runic:", StringComparison.Ordinal) || !contract.RootElement.GetProperty("contracts").TryGetProperty(factory.Key, out _))
                throw new ArgumentException("Custom WPF renderers must name a declared custom markup contract.", nameof(custom));
        _custom = new ReadOnlyDictionary<string, WpfMarkupFactory>(factories);
    }

    /// <summary>Validates and replaces content on the target control’s dispatcher thread.</summary>
    public void SetContent(TextBlock target, string key, LocalizedTextContent content, IReadOnlyDictionary<string, InlineMarkupBinding> slots)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        var runs = _semantic.Render(key, content, slots);
        // Build first: a binding failure must not partially replace displayed content.
        var lifetime = new RenderLifetime();
        Inline[] inlines = runs.Select(run => Create(run, content.Locale, lifetime)).ToArray();
        ClearContent(target); Lifetimes.Add(target, lifetime);
        target.Inlines.Clear(); target.Inlines.AddRange(inlines);
        target.Language = XmlLanguage.GetLanguage(content.Locale);
    }

    /// <summary>Clears rendered content and deactivates callbacks, including on retained detached controls.</summary>
    public static void ClearContent(TextBlock target)
    {
        ArgumentNullException.ThrowIfNull(target); target.Dispatcher.VerifyAccess();
        if (Lifetimes.TryGetValue(target, out var previous)) previous.Active = false;
        Lifetimes.Remove(target); target.Inlines.Clear();
    }

    private Inline Create(InlineMarkupRun run, string locale, RenderLifetime lifetime)
    {
        if (run.Text is string text) return new Run(text);
        Inline[] children = run.Children.Select(child => Create(child, locale, lifetime)).ToArray();
        Inline inline;
        if (_custom.TryGetValue(run.Name, out var factory)) inline = factory(run, children);
        else if (run.Name == "runic:br") inline = new LineBreak();
        else if (run.Binding is InlineIconBinding icon)
        {
            if (icon.Asset is not Func<FrameworkElement> create) throw new TranslationFormatException("WPF icon assets must be Func<FrameworkElement> factories returning a fresh element.");
            FrameworkElement element = create();
            element.Focusable = false;
            var host = new IconHost(element, icon.Decorative, icon.Decorative ? "" : icon.AccessibleName!(locale));
            inline = new InlineUIContainer(host) { BaselineAlignment = BaselineAlignment.Center };
        }
        else if (run.Binding is InlineActionBinding action)
        {
            var label = new TextBlock(); label.Inlines.AddRange(children);
            var button = new Button { Content = label }; button.Click += (_, _) => { if (lifetime.Active) action.Activate(); };
            inline = new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center };
        }
        else
        {
            Span span = run.Name switch {
                "runic:strong" or "runic:bold" => new Bold(),
                "runic:em" or "runic:italic" => new Italic(),
                "runic:code" => new Span { FontFamily = new FontFamily("Consolas") },
                "runic:link" => new Hyperlink(),
                _ => throw new TranslationFormatException("No WPF renderer linked for '" + run.Name + "'."),
            };
            span.Inlines.AddRange(children);
            if (span is Hyperlink link && run.Binding is InlineLinkBinding destination)
            {
                link.NavigateUri = destination.Destination;
                link.RequestNavigate += (_, args) => { if (lifetime.Active) _navigate(args.Uri); args.Handled = true; };
            }
            inline = span;
        }
        _theme?.Invoke(run.Name, inline);
        return inline;
    }
    private sealed class IconHost : Decorator
    {
        private readonly bool _decorative;
        private readonly string _label;
        public IconHost(FrameworkElement element, bool decorative, string label) { _decorative = decorative; _label = label; Child = element; Focusable = false; }
        protected override AutomationPeer OnCreateAutomationPeer() => new IconPeer(this);
        private sealed class IconPeer(IconHost owner) : FrameworkElementAutomationPeer(owner)
        {
            protected override bool IsControlElementCore() => !owner._decorative;
            protected override bool IsContentElementCore() => !owner._decorative;
            protected override string GetNameCore() => owner._label;
            protected override string GetClassNameCore() => "Rmf2Icon";
            protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
            protected override List<AutomationPeer>? GetChildrenCore() => null;
        }
    }

}
