#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Runic.Translations.Wpf;

/// <summary>
/// Attached properties that render a rich generated message into a <see cref="TextBlock"/> and keep it current:
/// <c>rt:Translations.RichMessage="checkout_help"</c> with slots from <see cref="SlotsProperty"/> and optional
/// inputs from <see cref="ArgumentsProperty"/>. Plain messages use <see cref="MessageExtension"/> instead.
/// </summary>
public static class Translations
{
    /// <summary>Flattened readable name of a message that has markup.</summary>
    public static readonly DependencyProperty RichMessageProperty = DependencyProperty.RegisterAttached(
        "RichMessage", typeof(string), typeof(Translations), new PropertyMetadata(null, Changed));

    /// <summary>Slot bindings (links, actions, icons) by slot ID, usually bound to a view model property.</summary>
    public static readonly DependencyProperty SlotsProperty = DependencyProperty.RegisterAttached(
        "Slots", typeof(IReadOnlyDictionary<string, InlineMarkupBinding>), typeof(Translations), new PropertyMetadata(null, Changed));

    /// <summary>Message inputs in the order of the generated method's parameters.</summary>
    public static readonly DependencyProperty ArgumentsProperty = DependencyProperty.RegisterAttached(
        "Arguments", typeof(IReadOnlyList<object?>), typeof(Translations), new PropertyMetadata(null, Changed));

    /// <summary>Source for this element; defaults to <see cref="TranslationSource.Default"/>.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(TranslationSource), typeof(Translations), new PropertyMetadata(null, Changed));

    private static readonly DependencyProperty BindingProperty = DependencyProperty.RegisterAttached(
        "Binding", typeof(RichMessageBinding), typeof(Translations));

    /// <summary>Gets <see cref="RichMessageProperty"/>.</summary>
    public static string? GetRichMessage(DependencyObject element) => (string?)element.GetValue(RichMessageProperty);
    /// <summary>Sets <see cref="RichMessageProperty"/>.</summary>
    public static void SetRichMessage(DependencyObject element, string? value) => element.SetValue(RichMessageProperty, value);
    /// <summary>Gets <see cref="SlotsProperty"/>.</summary>
    public static IReadOnlyDictionary<string, InlineMarkupBinding>? GetSlots(DependencyObject element) =>
        (IReadOnlyDictionary<string, InlineMarkupBinding>?)element.GetValue(SlotsProperty);
    /// <summary>Sets <see cref="SlotsProperty"/>.</summary>
    public static void SetSlots(DependencyObject element, IReadOnlyDictionary<string, InlineMarkupBinding>? value) => element.SetValue(SlotsProperty, value);
    /// <summary>Gets <see cref="ArgumentsProperty"/>.</summary>
    public static IReadOnlyList<object?>? GetArguments(DependencyObject element) => (IReadOnlyList<object?>?)element.GetValue(ArgumentsProperty);
    /// <summary>Sets <see cref="ArgumentsProperty"/>.</summary>
    public static void SetArguments(DependencyObject element, IReadOnlyList<object?>? value) => element.SetValue(ArgumentsProperty, value);
    /// <summary>Gets <see cref="SourceProperty"/>.</summary>
    public static TranslationSource? GetSource(DependencyObject element) => (TranslationSource?)element.GetValue(SourceProperty);
    /// <summary>Sets <see cref="SourceProperty"/>.</summary>
    public static void SetSource(DependencyObject element, TranslationSource? value) => element.SetValue(SourceProperty, value);

    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) throw new InvalidOperationException("Translations rich properties apply to a TextBlock.");
        var binding = (RichMessageBinding?)block.GetValue(BindingProperty);
        if (binding is null) block.SetValue(BindingProperty, binding = new RichMessageBinding(block));
        binding.Schedule();
    }
}

/// <summary>
/// Renders one element's rich message. It is owned by the element, reaches it weakly, and is reached by the source
/// weakly, so neither side keeps the other alive. Work is coalesced and runs on the element's dispatcher.
/// </summary>
internal sealed class RichMessageBinding
{
    private readonly WeakReference<TextBlock> _target;
    private TranslationSource? _subscribed;
    private int _pending;

    public RichMessageBinding(TextBlock target) => _target = new(target);

    public void Schedule()
    {
        if (!_target.TryGetTarget(out TextBlock? block) || Interlocked.Exchange(ref _pending, 1) != 0) return;
        block.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, Render);
    }

    private void Render()
    {
        Interlocked.Exchange(ref _pending, 0);
        if (!_target.TryGetTarget(out TextBlock? block)) return;
        TranslationSource? source = Translations.GetSource(block) ?? TranslationSource.Default;
        if (!ReferenceEquals(source, _subscribed))
        {
            _subscribed?.RemoveListener(this);
            source?.AddListener(this);
            _subscribed = source;
        }
        string? key = Translations.GetRichMessage(block);
        if (string.IsNullOrEmpty(key)) { WpfInlineRenderer.ClearContent(block); return; }
        if (source is null) throw new InvalidOperationException("No TranslationSource: set TranslationSource.Default at startup or Translations.Source.");
        WpfInlineRenderer renderer = source.Renderer
            ?? throw new InvalidOperationException("Rich messages need a WpfInlineRenderer: pass one to the TranslationSource.");
        IReadOnlyList<object?> arguments = Translations.GetArguments(block) ?? [];
        source.Validate(key, arguments.Count, rich: true);
        LocalizedTextContent content = TranslationSource.ToContent(source.Resolve(key, arguments));
        try
        {
            renderer.SetContent(block, key, content, Translations.GetSlots(block) ?? new Dictionary<string, InlineMarkupBinding>());
        }
        catch (TranslationFormatException exception)
        {
            // Like a failing binding: keep the previous content and report. Slots from the DataContext may not exist yet.
            Trace.TraceError($"Runic.Translations.Wpf: cannot render rich message '{key}': {exception.Message}");
        }
    }
}
