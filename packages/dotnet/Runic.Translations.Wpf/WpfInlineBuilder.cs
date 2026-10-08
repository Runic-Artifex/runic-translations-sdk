using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Runic.Translations.Wpf;

/// <summary>
/// The callbacks of one render. Replacing or clearing a host retires it: handlers check <see cref="Active"/>,
/// and retained, detached links and actions are disabled so they also look inert.
/// </summary>
internal sealed class RenderLifetime
{
    private readonly List<ContentElement> _links = new();
    private readonly List<UIElement> _actions = new();

    internal bool Active { get; private set; } = true;

    /// <summary>The copy text of embedded controls: action labels and meaningful icon alternate text.</summary>
    internal Dictionary<InlineUIContainer, Func<string>> CopyText { get; } = new();

    internal void Track(Hyperlink link) => _links.Add(link);
    internal void Track(Button action) => _actions.Add(action);

    internal void Retire()
    {
        if (!Active) return;
        Active = false;
        foreach (ContentElement link in _links) link.IsEnabled = false;
        foreach (UIElement action in _actions) action.IsEnabled = false;
    }
}

/// <summary>Maps semantic inline runs to fresh WPF inlines; shared by the TextBlock and FlowDocument adapters.</summary>
internal sealed class WpfInlineBuilder
{
    private readonly ReadOnlyDictionary<string, WpfMarkupFactory> _custom;
    private readonly Action<Uri> _navigate;
    private readonly Action<string, Inline>? _theme;
    private readonly bool _slotAutomationIds;

    internal WpfInlineBuilder(string contractJson, Action<Uri> navigate, IReadOnlyDictionary<string, WpfMarkupFactory>? custom,
        Action<string, Inline>? theme, bool slotAutomationIds)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _navigate = navigate; _theme = theme; _slotAutomationIds = slotAutomationIds;
        var factories = custom is null ? new Dictionary<string, WpfMarkupFactory>(StringComparer.Ordinal) : new Dictionary<string, WpfMarkupFactory>(custom, StringComparer.Ordinal);
        using var contract = JsonDocument.Parse(contractJson);
        foreach (var factory in factories)
            if (factory.Value is null || factory.Key.StartsWith("runic:", StringComparison.Ordinal) || !contract.RootElement.GetProperty("contracts").TryGetProperty(factory.Key, out _))
                throw new ArgumentException("Custom WPF renderers must name a declared custom markup contract.", nameof(custom));
        _custom = new ReadOnlyDictionary<string, WpfMarkupFactory>(factories);
    }

    internal Inline Create(InlineMarkupRun run, string locale, RenderLifetime lifetime)
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
            string label = icon.Decorative ? "" : icon.AccessibleName!(locale);
            if (!icon.Decorative && string.IsNullOrWhiteSpace(label)) throw new TranslationFormatException("Meaningful icon alternate text is empty.");
            var host = new IconHost(element, icon.Decorative, label);
            var container = new InlineUIContainer(host) { BaselineAlignment = BaselineAlignment.Center };
            lifetime.CopyText[container] = () => label;
            inline = container;
        }
        else if (run.Binding is InlineActionBinding action)
        {
            var label = new TextBlock(); label.Inlines.AddRange(children);
            var button = new Button { Content = label }; button.Click += (_, _) => { if (lifetime.Active) action.Activate(); };
            lifetime.Track(button);
            SlotAutomationId(button, run);
            var container = new InlineUIContainer(button) { BaselineAlignment = BaselineAlignment.Center };
            lifetime.CopyText[container] = () => CopyText(children, lifetime);
            inline = container;
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
                lifetime.Track(link);
                SlotAutomationId(link, run);
            }
            inline = span;
        }
        _theme?.Invoke(run.Name, inline);
        return inline;
    }

    // Slot refs are static and translation-independent, unlike inline occurrence keys, which count the text before an element.
    private void SlotAutomationId(DependencyObject element, InlineMarkupRun run)
    {
        if (_slotAutomationIds && run.Options.TryGetValue("ref", out string? slot)) AutomationProperties.SetAutomationId(element, slot);
    }

    /// <summary>
    /// The copy text of rendered inlines, optionally clipped to <paramref name="from"/>..<paramref name="to"/>: text, line breaks,
    /// action labels and meaningful icon alternate text; link destinations and decorative icons are left out.
    /// </summary>
    internal static string CopyText(IEnumerable<Inline> inlines, RenderLifetime lifetime, TextPointer? from = null, TextPointer? to = null)
    {
        var text = new StringBuilder();
        Append(inlines);
        return text.ToString();

        void Append(IEnumerable<Inline> items)
        {
            foreach (Inline inline in items)
            {
                if (from is not null && to is not null && (inline.ElementEnd.CompareTo(from) <= 0 || inline.ElementStart.CompareTo(to) >= 0)) continue;
                switch (inline)
                {
                    case Run run when from is null || to is null:
                        text.Append(run.Text);
                        break;
                    case Run run:
                        TextPointer start = run.ContentStart.CompareTo(from) < 0 ? from : run.ContentStart;
                        TextPointer end = run.ContentEnd.CompareTo(to) > 0 ? to : run.ContentEnd;
                        // Slice the run's own text: TextRange.Text would add the native list marker of an enclosing list item.
                        if (start.CompareTo(end) < 0)
                        {
                            int offset = Math.Clamp(run.ContentStart.GetOffsetToPosition(start), 0, run.Text.Length);
                            text.Append(run.Text, offset, Math.Clamp(start.GetOffsetToPosition(end), 0, run.Text.Length - offset));
                        }
                        break;
                    case LineBreak:
                        text.Append('\n');
                        break;
                    case InlineUIContainer container:
                        if (lifetime.CopyText.TryGetValue(container, out Func<string>? copy)) text.Append(copy());
                        break;
                    case Span span:
                        Append(span.Inlines);
                        break;
                }
            }
        }
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
