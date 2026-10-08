#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace Runic.Translations.Wpf;

/// <summary>A named input of a message, for the long form of <see cref="MessageExtension"/>.</summary>
public sealed class MessageInput
{
    /// <summary>The input (parameter) name on the generated method, such as <c>name</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The value, usually a binding such as <c>{Binding UserName}</c>.</summary>
    public BindingBase? Value { get; set; }
}

/// <summary>
/// Binds a dependency property to a plain generated message: <c>{rt:Message application_title}</c>.
/// Inputs are bindings. The short form <see cref="Arg0"/> to <see cref="Arg3"/> follows the generated method's
/// parameter order: <c>{rt:Message greeting, Arg0={Binding UserName}}</c>. The long form names the parameters and has
/// no limit: <c>&lt;rt:Message Key="greeting"&gt;&lt;rt:MessageInput Name="name" Value="{Binding UserName}"/&gt;&lt;/rt:Message&gt;</c>.
/// Names, input counts and the message kind are checked when the XAML loads. The property refreshes when a snapshot is published.
/// In a designer without a source it shows <c>[key]</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
[ContentProperty(nameof(Inputs))]
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

    /// <summary>Named inputs (long form). Do not combine with <see cref="Arg0"/> to <see cref="Arg3"/>.</summary>
    public Collection<MessageInput> Inputs { get; } = [];

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
        TranslationSource? source = Source ?? TranslationSource.UseDefault();
        if (source is null)
        {
            if (TranslationDesign.IsInDesignMode) return "[" + key + "]";
            throw new InvalidOperationException("No TranslationSource: set TranslationSource.Default at startup or pass Source=.");
        }
        (List<BindingBase> bindings, List<string>? names) = CollectInputs();
        source.Validate(key, rich: false, bindings.Count, names);
        if (bindings.Count == 0)
        {
            return new Binding { Source = source, Path = new PropertyPath("[" + key + "]"), Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
        }
        var multi = new MultiBinding { Converter = new MessageConverter(source, key, names), Mode = BindingMode.OneWay };
        multi.Bindings.Add(new Binding(nameof(TranslationSource.Version)) { Source = source, Mode = BindingMode.OneWay });
        foreach (BindingBase binding in bindings) multi.Bindings.Add(binding);
        return multi.ProvideValue(serviceProvider);
    }

    private (List<BindingBase>, List<string>?) CollectInputs()
    {
        BindingBase?[] shorthand = [Arg0, Arg1, Arg2, Arg3];
        int last = Array.FindLastIndex(shorthand, arg => arg is not null);
        if (Inputs.Count > 0)
        {
            if (last >= 0) throw new InvalidOperationException($"rt:Message '{Key}' mixes Arg0..Arg3 with named inputs; use one form.");
            foreach (MessageInput input in Inputs)
            {
                if (string.IsNullOrEmpty(input.Name) || input.Value is null) throw new InvalidOperationException($"Every input of rt:Message '{Key}' needs a Name and a Value.");
            }
            return ([.. Inputs.Select(input => input.Value!)], [.. Inputs.Select(input => input.Name!)]);
        }
        var bindings = new List<BindingBase>();
        for (int index = 0; index <= last; index++)
        {
            bindings.Add(shorthand[index] ?? throw new InvalidOperationException($"rt:Message '{Key}' sets Arg{last} but not Arg{index}; inputs must be contiguous from Arg0."));
        }
        return (bindings, null);
    }

    private sealed class MessageConverter(TranslationSource source, string key, IReadOnlyList<string>? names) : IMultiValueConverter
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
            return source.Format(key, inputs, names);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

internal static class TranslationDesign
{
    /// <summary>True in Visual Studio and Blend designers; evaluated on demand so tests can override the metadata.</summary>
    internal static bool IsInDesignMode => DesignerProperties.GetIsInDesignMode(new DependencyObject());
}
