#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Runic.Translations.Wpf;

/// <summary>
/// Connects a generated catalog to XAML. It resolves message names on the generated readable surface
/// (<c>text.Messages</c>) and tells every binding to refresh when the manager raises
/// <see cref="ITranslationManager.LocaleChanged"/>, always on the dispatcher it was created for.
/// </summary>
/// <remarks>
/// Create one per catalog on the UI thread, usually in <c>App.OnStartup</c>, and keep it for the life of the
/// application or call <see cref="Dispose"/>. The manager holds only a weak reference to the source, and bound
/// elements are held weakly, so abandoned windows are collected without unsubscribing.
/// <see cref="ITranslationManager.RefreshAsync"/> never raises <c>LocaleChanged</c>; call
/// <see cref="RefreshAsync"/> (or <see cref="Invalidate"/> after refreshing the manager yourself) to show a
/// republished snapshot.
/// </remarks>
public sealed class TranslationSource : INotifyPropertyChanged, IDisposable
{
    private readonly object _gate = new();
    private readonly List<WeakReference<RichMessageBinding>> _listeners = [];
    private readonly Dictionary<string, MemberInfo> _members = new(StringComparer.Ordinal);
    private readonly ManagerHook _hook;
    private readonly Dispatcher _dispatcher;
    private int _version;
    private int _disposed;

    /// <summary>Source used by <c>{rt:Message}</c> and <c>Translations.RichMessage</c> when none is named.</summary>
    public static TranslationSource? Default { get; set; }

    /// <summary>Creates a source on the current dispatcher, or on <paramref name="dispatcher"/> when given.</summary>
    /// <param name="manager">The manager whose locale changes refresh bindings.</param>
    /// <param name="messages">The generated readable surface, such as <c>new AppText(manager).Messages</c>.</param>
    /// <param name="renderer">Required only for rich messages bound with <c>Translations.RichMessage</c>.</param>
    /// <param name="dispatcher">The UI dispatcher; defaults to <see cref="Dispatcher.CurrentDispatcher"/>.</param>
    public TranslationSource(ITranslationManager manager, object messages, WpfInlineRenderer? renderer = null, Dispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(messages);
        Manager = manager;
        Messages = messages;
        Renderer = renderer;
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        foreach (MemberInfo member in messages.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            if (member is PropertyInfo { GetMethod: not null } property && property.GetIndexParameters().Length == 0) _members[member.Name] = property;
            else if (member is MethodInfo { IsSpecialName: false, IsGenericMethodDefinition: false } method && method.DeclaringType != typeof(object)) _members[member.Name] = method;
        }
        _hook = new ManagerHook(this, manager);
    }

    /// <summary>The manager whose locale this source follows.</summary>
    public ITranslationManager Manager { get; }

    /// <summary>The generated readable surface this source resolves message names against.</summary>
    public object Messages { get; }

    /// <summary>The inline renderer used for rich messages, or <see langword="null"/>.</summary>
    public WpfInlineRenderer? Renderer { get; }

    /// <summary>The active canonical locale.</summary>
    public string Locale => Manager.CurrentLocale;

    /// <summary>Increases on every refresh; bindings with inputs use it to re-read their message.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets a plain message with no inputs by its flattened readable name, such as <c>application_title</c>.</summary>
    public string this[string key] => Format(key, []);

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Refreshes every binding. Safe to call from any thread; the update runs on the source's dispatcher.</summary>
    public void Invalidate()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_dispatcher.CheckAccess()) Raise(); else _dispatcher.BeginInvoke(DispatcherPriority.Normal, Raise);
    }

    /// <summary>Republishes the active locale from its sources, then refreshes every binding.</summary>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Manager.RefreshAsync(cancellationToken).ConfigureAwait(false);
        Invalidate();
    }

    /// <summary>Stops following the manager. Bound elements keep their last value.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _hook.Detach();
        lock (_gate) _listeners.Clear();
    }

    private void Raise()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Interlocked.Increment(ref _version);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        List<RichMessageBinding> live = [];
        lock (_gate)
        {
            _listeners.RemoveAll(item => !item.TryGetTarget(out _));
            foreach (WeakReference<RichMessageBinding> item in _listeners) if (item.TryGetTarget(out RichMessageBinding? target)) live.Add(target);
        }
        foreach (RichMessageBinding listener in live) listener.Schedule();
    }

    internal void AddListener(RichMessageBinding listener)
    {
        lock (_gate)
        {
            if (!_listeners.Any(item => item.TryGetTarget(out RichMessageBinding? target) && ReferenceEquals(target, listener))) _listeners.Add(new(listener));
        }
    }

    internal void RemoveListener(RichMessageBinding listener)
    {
        lock (_gate) _listeners.RemoveAll(item => !item.TryGetTarget(out RichMessageBinding? target) || ReferenceEquals(target, listener));
    }

    /// <summary>Throws a descriptive exception unless <paramref name="key"/> names a message with <paramref name="argumentCount"/> inputs of the expected kind.</summary>
    internal void Validate(string key, int argumentCount, bool? rich)
    {
        if (!_members.TryGetValue(key, out MemberInfo? member))
            throw new ArgumentException($"'{key}' is not a message of {Messages.GetType().Name}. Use the flattened readable name, such as 'checkout_help'.", nameof(key));
        int inputs = member is MethodInfo method ? method.GetParameters().Length : 0;
        if (inputs != argumentCount)
            throw new ArgumentException($"Message '{key}' takes {inputs} input(s) but {argumentCount} were supplied.", nameof(key));
        if (rich is { } expected && IsRichType(ResultType(member)) != expected)
            throw new ArgumentException(expected
                ? $"Message '{key}' has no markup; bind it with {{rt:Message {key}}}."
                : $"Message '{key}' has markup; bind it with Translations.RichMessage on a TextBlock.", nameof(key));
    }

    /// <summary>Formats a plain message; a failure is traced and shown as <c>[key]</c>, as for any WPF binding error.</summary>
    internal string Format(string key, IReadOnlyList<object?> args)
    {
        try { return Resolve(key, args) as string ?? throw new InvalidOperationException($"Message '{key}' has markup."); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or InvalidCastException or OverflowException)
        {
            Trace.TraceWarning($"Runic.Translations.Wpf: cannot resolve message '{key}': {exception.Message}");
            return "[" + key + "]";
        }
    }

    /// <summary>Evaluates a message against the active snapshot: a string, or typed rich content.</summary>
    internal object Resolve(string key, IReadOnlyList<object?> args)
    {
        Validate(key, args.Count, rich: null);
        MemberInfo member = _members[key];
        try
        {
            if (member is PropertyInfo property) return property.GetValue(Messages)!;
            var method = (MethodInfo)member;
            ParameterInfo[] parameters = method.GetParameters();
            var converted = new object?[parameters.Length];
            for (int index = 0; index < converted.Length; index++) converted[index] = Convert(args[index], parameters[index].ParameterType);
            return method.Invoke(Messages, converted)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static Type ResultType(MemberInfo member) => member is MethodInfo method ? method.ReturnType : ((PropertyInfo)member).PropertyType;

    private static bool IsRichType(Type type) =>
        type == typeof(LocalizedTextContent) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(LocalizedTextContent<>));

    /// <summary>Unwraps typed rich content to the untyped content the dictionary renderer overload takes.</summary>
    internal static LocalizedTextContent ToContent(object value)
    {
        if (value is LocalizedTextContent content) return content;
        if (IsRichType(value.GetType()) && value.GetType().GetProperty("Content")?.GetValue(value) is LocalizedTextContent inner) return inner;
        throw new InvalidOperationException("The message does not return rich content.");
    }

    private static object? Convert(object? value, Type parameterType)
    {
        Type target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (value is null)
        {
            if (parameterType == typeof(string)) return string.Empty;
            if (!parameterType.IsValueType || target != parameterType) return null;
            throw new InvalidOperationException($"A null value cannot be passed to a {parameterType.Name} input.");
        }
        if (target.IsInstanceOfType(value)) return value;
        if (target == typeof(string)) return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
    }

    private sealed class ManagerHook
    {
        private readonly WeakReference<TranslationSource> _owner;
        private readonly ITranslationManager _manager;

        public ManagerHook(TranslationSource owner, ITranslationManager manager)
        {
            _owner = new WeakReference<TranslationSource>(owner);
            _manager = manager;
            manager.LocaleChanged += OnLocaleChanged;
        }

        public void Detach() => _manager.LocaleChanged -= OnLocaleChanged;

        private void OnLocaleChanged(object? sender, TranslationLocaleChangedEventArgs e)
        {
            if (_owner.TryGetTarget(out TranslationSource? owner)) owner.Invalidate(); else Detach();
        }
    }
}
