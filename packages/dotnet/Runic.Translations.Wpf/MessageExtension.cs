#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Runic.Translations.Wpf;

/// <summary>
/// Binds a dependency property to a plain generated message: <c>{rt:Message application_title}</c>. Inputs come from
/// <see cref="Arg0"/> to <see cref="Arg3"/>, in the order of the generated method's parameters:
/// <c>{rt:Message greeting, Arg0={Binding UserName}}</c>. The property refreshes when the locale changes.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class MessageExtension : MarkupExtension
{
    /// <summary>Creates the extension; set <see cref="Key"/>.</summary>
    public MessageExtension() { }

    /// <summary>Creates the extension for a flattened readable message name.</summary>
    public MessageExtension(string key) => Key = key;

    /// <summary>The flattened readable name on the generated surface, such as <c>checkout_title</c>.</summary>
    [ConstructorArgument("key")]
    public string? Key { get; set; }

    /// <summary>The source to use; defaults to <see cref="TranslationSource.Default"/>.</summary>
    public TranslationSource? Source { get; set; }

    /// <summary>First message input, a binding.</summary>
    public BindingBase? Arg0 { get; set; }
    /// <summary>Second message input.</summary>
    public BindingBase? Arg1 { get; set; }
    /// <summary>Third message input.</summary>
    public BindingBase? Arg2 { get; set; }
    /// <summary>Fourth message input.</summary>
    public BindingBase? Arg3 { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        string key = Key ?? throw new InvalidOperationException("rt:Message needs a message name.");
        TranslationSource source = Source ?? TranslationSource.Default
            ?? throw new InvalidOperationException("No TranslationSource: set TranslationSource.Default at startup or pass Source=.");
        List<BindingBase> inputs = [];
        foreach (BindingBase? arg in new[] { Arg0, Arg1, Arg2, Arg3 })
        {
            if (arg is null) break;
            inputs.Add(arg);
        }
        source.Validate(key, inputs.Count, rich: false);
        if (inputs.Count == 0)
        {
            return new Binding { Source = source, Path = new PropertyPath("[" + key + "]"), Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
        }
        var multi = new MultiBinding { Converter = new MessageConverter(source, key), Mode = BindingMode.OneWay };
        multi.Bindings.Add(new Binding(nameof(TranslationSource.Version)) { Source = source, Mode = BindingMode.OneWay });
        foreach (BindingBase input in inputs) multi.Bindings.Add(input);
        return multi.ProvideValue(serviceProvider);
    }

    private sealed class MessageConverter(TranslationSource source, string key) : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var inputs = new object?[values.Length - 1];
            for (int index = 0; index < inputs.Length; index++)
            {
                // A DataContext that has not arrived yet leaves the binding unset; it re-evaluates when the value exists.
                if (ReferenceEquals(values[index + 1], DependencyProperty.UnsetValue)) return DependencyProperty.UnsetValue;
                inputs[index] = values[index + 1];
            }
            return source.Format(key, inputs);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
