#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Runic.Translations.Wpf;

/// <summary>
/// Attached properties that render a rich generated message into a <see cref="TextBlock"/> and keep it current:
/// <c>rt:TranslationProperties.RichMessage="checkout_help"</c> with the generated slots object in <see cref="SlotsProperty"/>
/// and optional inputs in <see cref="ArgumentsProperty"/>. Plain messages use <see cref="MessageExtension"/> instead.
/// </summary>
/// <remarks>
/// Setting <see cref="RichMessageProperty"/> checks synchronously that the message exists, has markup, and that a source
/// and renderer are available (a XAML load fails with the reason). Set <see cref="SourceProperty"/> before it when you
/// name a source. Rendering happens later on the element's dispatcher; a failure there is traced like a binding error and
/// the previous content stays. An element whose message needs inputs renders once <see cref="ArgumentsProperty"/> is set.
/// In a designer without a source the element stays empty.
/// </remarks>
public static class TranslationProperties
{
    /// <summary>Flattened readable name of a message that has markup.</summary>
    public static readonly DependencyProperty RichMessageProperty = DependencyProperty.RegisterAttached(
        "RichMessage", typeof(string), typeof(TranslationProperties), new PropertyMetadata(null, RichMessageChanged));

    /// <summary>
    /// The slots: the generated slots object (<c>new AppTextSlots.checkout_help(guide: …)</c>, checked by the compiler),
    /// or an <c>IReadOnlyDictionary&lt;string, InlineMarkupBinding&gt;</c> by slot ID for dynamic use.
    /// </summary>
    public static readonly DependencyProperty SlotsProperty = DependencyProperty.RegisterAttached(
        "Slots", typeof(object), typeof(TranslationProperties), new PropertyMetadata(null, OtherChanged));

    /// <summary>
    /// Message inputs: a list in the generated method's parameter order, or an
    /// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> by parameter name.
    /// </summary>
    public static readonly DependencyProperty ArgumentsProperty = DependencyProperty.RegisterAttached(
        "Arguments", typeof(object), typeof(TranslationProperties), new PropertyMetadata(null, OtherChanged));

    /// <summary>Source for this element and its descendants (inherited); defaults to <see cref="TranslationSource.Default"/>.</summary>
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(TranslationSource), typeof(TranslationProperties),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, SourceChanged));

    private static readonly DependencyProperty BindingProperty = DependencyProperty.RegisterAttached(
        "Binding", typeof(RichMessageBinding), typeof(TranslationProperties));

    /// <summary>Gets <see cref="RichMessageProperty"/>.</summary>
    public static string? GetRichMessage(DependencyObject element) => (string?)element.GetValue(RichMessageProperty);
    /// <summary>Sets <see cref="RichMessageProperty"/>.</summary>
    public static void SetRichMessage(DependencyObject element, string? value) => element.SetValue(RichMessageProperty, value);
    /// <summary>Gets <see cref="SlotsProperty"/>.</summary>
    public static object? GetSlots(DependencyObject element) => element.GetValue(SlotsProperty);
    /// <summary>Sets <see cref="SlotsProperty"/>.</summary>
    public static void SetSlots(DependencyObject element, object? value) => element.SetValue(SlotsProperty, value);
    /// <summary>Gets <see cref="ArgumentsProperty"/>.</summary>
    public static object? GetArguments(DependencyObject element) => element.GetValue(ArgumentsProperty);
    /// <summary>Sets <see cref="ArgumentsProperty"/>.</summary>
    public static void SetArguments(DependencyObject element, object? value) => element.SetValue(ArgumentsProperty, value);
    /// <summary>Gets <see cref="SourceProperty"/>.</summary>
    public static TranslationSource? GetSource(DependencyObject element) => (TranslationSource?)element.GetValue(SourceProperty);
    /// <summary>Sets <see cref="SourceProperty"/>.</summary>
    public static void SetSource(DependencyObject element, TranslationSource? value) => element.SetValue(SourceProperty, value);

    private static void RichMessageChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        TextBlock block = Target(element);
        if (e.NewValue is string { Length: > 0 } key)
        {
            TranslationSource? source = GetSource(block) ?? TranslationSource.UseDefault();
            if (source is null)
            {
                if (TranslationDesign.IsInDesignMode) return;
                throw new InvalidOperationException("No TranslationSource: set TranslationSource.Default at startup or TranslationProperties.Source before RichMessage.");
            }
            if (source.Renderer is null) throw new InvalidOperationException("Rich messages need a WpfInlineRenderer: pass one to the TranslationSource.");
            source.Validate(key, rich: true);
        }
        Schedule(block, create: e.NewValue is string { Length: > 0 });
    }

    private static void OtherChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) => Schedule(Target(element), create: false);

    private static void SourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        // Inherited: every descendant is notified, most of them are not rich elements.
        if (element is TextBlock block) Schedule(block, create: false);
    }

    private static TextBlock Target(DependencyObject element) =>
        element as TextBlock ?? throw new InvalidOperationException("TranslationProperties rich properties apply to a TextBlock.");

    private static void Schedule(TextBlock block, bool create)
    {
        var binding = (RichMessageBinding?)block.GetValue(BindingProperty);
        if (binding is null)
        {
            // Nothing rendered and nothing asked for: no work.
            if (!create && GetRichMessage(block) is not { Length: > 0 }) return;
            block.SetValue(BindingProperty, binding = new RichMessageBinding(block));
        }
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
        string? key = TranslationProperties.GetRichMessage(block);
        try
        {
            TranslationSource? source = TranslationProperties.GetSource(block) ?? TranslationSource.UseDefault();
            if (!ReferenceEquals(source, _subscribed))
            {
                _subscribed?.RemoveListener(this);
                source?.AddListener(this);
                _subscribed = source;
            }
            if (string.IsNullOrEmpty(key)) { WpfInlineRenderer.ClearContent(block); return; }
            if (source is null)
            {
                if (TranslationDesign.IsInDesignMode) return;
                throw new InvalidOperationException("No TranslationSource: set TranslationSource.Default at startup or TranslationProperties.Source.");
            }
            WpfInlineRenderer renderer = source.Renderer
                ?? throw new InvalidOperationException("Rich messages need a WpfInlineRenderer: pass one to the TranslationSource.");
            if (!TryReadArguments(source, key, TranslationProperties.GetArguments(block), out List<object?> values, out List<string>? names)) return;
            source.Validate(key, rich: true, values.Count, names);
            object content = source.Resolve(key, values, names);
            object? slots = TranslationProperties.GetSlots(block);
            if (slots is IReadOnlyDictionary<string, InlineMarkupBinding> dictionary)
            {
                renderer.SetContent(block, key, TranslationSource.ToContent(content), dictionary);
            }
            else if (slots is not null && TranslationSource.Bind(content, slots) is { } bound)
            {
                renderer.SetContent(block, bound);
            }
            else if (slots is not null)
            {
                throw new InvalidOperationException($"Message '{key}' is untyped; pass its slots as a dictionary.");
            }
            else
            {
                renderer.SetContent(block, key, TranslationSource.ToContent(content), new Dictionary<string, InlineMarkupBinding>());
            }
        }
        catch (Exception exception) when (!TranslationSource.IsFatal(exception))
        {
            // Like a failing binding: keep the previous content and report. Slots or inputs may not exist yet.
            Trace.TraceError($"Runic.Translations.Wpf: cannot render rich message '{key}': {exception.Message}");
        }
    }

    /// <summary>False when the message needs inputs that have not been supplied yet.</summary>
    private static bool TryReadArguments(TranslationSource source, string key, object? arguments, out List<object?> values, out List<string>? names)
    {
        values = [];
        names = null;
        if (arguments is null || ReferenceEquals(arguments, DependencyProperty.UnsetValue)) return source.InputCount(key) == 0;
        if (arguments is IReadOnlyDictionary<string, object?> named)
        {
            names = [.. named.Keys];
            values = [.. named.Values];
        }
        else if (arguments is IEnumerable sequence and not string)
        {
            values = [.. sequence.Cast<object?>()];
        }
        else
        {
            throw new InvalidOperationException("Arguments must be a list in parameter order or a dictionary by parameter name.");
        }
        return true;
    }
}
