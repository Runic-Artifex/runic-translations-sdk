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
/// (<c>text.Messages</c>) and tells every binding to refresh when the translation runtime publishes a snapshot,
/// always on the dispatcher it was created for.
/// </summary>
/// <remarks>
/// Create one per catalog on the UI thread, usually in <c>App.OnStartup</c>, and keep it for the life of the
/// application or call <see cref="Dispose"/>. The manager holds only a weak reference to the source, and bound
/// rich elements are held weakly, so abandoned windows are collected without unsubscribing.
/// A manager that implements <see cref="ITranslationSnapshotNotifier"/> (the built-in
/// <see cref="TranslationManager"/> does) refreshes bindings after a locale switch and after
/// <see cref="ITranslationManager.RefreshAsync"/>. For any other manager only
/// <see cref="ITranslationManager.LocaleChanged"/> is observed; call <see cref="Invalidate"/> after refreshing it.
/// </remarks>
public sealed class TranslationSource : INotifyPropertyChanged, IDisposable
{
    private static readonly object DefaultGate = new();
    private static TranslationSource? s_default;
    private static bool s_defaultUsed;

    private readonly object _gate = new();
    private readonly List<WeakReference<RichMessageBinding>> _listeners = [];
    private readonly Dictionary<string, MemberInfo> _members = new(StringComparer.Ordinal);
    private readonly ManagerHook _hook;
    private readonly Dispatcher _dispatcher;
    private static readonly object RegistryGate = new();
    private static readonly List<WeakReference<TranslationSource>> Registry = [];
    private int _version;
    private int _disposed;

    /// <summary>
    /// The source used when none is named. Set it once at startup: replacing it after a binding has used it throws,
    /// because plain bindings capture their source when the XAML loads. Disposing the default source clears it.
    /// </summary>
    public static TranslationSource? Default
    {
        get { lock (DefaultGate) return s_default; }
        set
        {
            lock (DefaultGate)
            {
                if (s_defaultUsed && value is not null && !ReferenceEquals(value, s_default))
                    throw new InvalidOperationException("TranslationSource.Default was already used by a binding; set it once at startup, or name a Source explicitly.");
                s_default = value;
                if (value is null) s_defaultUsed = false;
            }
        }
    }

    /// <summary>Creates a source on the current thread's dispatcher (else the application's), or on <paramref name="dispatcher"/> when given.</summary>
    /// <param name="manager">The manager whose snapshot publications refresh bindings.</param>
    /// <param name="messages">The generated readable surface, <c>new AppText(manager).Messages</c>, not <c>AppText</c> itself.</param>
    /// <param name="renderer">Required only for rich messages bound with <c>TranslationProperties.RichMessage</c>.</param>
    /// <param name="dispatcher">
    /// The UI dispatcher. Defaults to the current thread's dispatcher, else <see cref="Application.Current"/>'s; there is no
    /// implicit dispatcher, so a call from a thread without one must pass it.
    /// </param>
    public TranslationSource(ITranslationManager manager, object messages, WpfInlineRenderer? renderer = null, Dispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(messages);
        FieldInfo? version = messages.GetType().GetField("ReadableNameVersion", BindingFlags.Public | BindingFlags.Static);
        if (version is not { IsLiteral: true })
            throw new ArgumentException($"{messages.GetType().Name} is not a generated readable surface. Pass 'text.Messages', not 'text'.", nameof(messages));
        _dispatcher = dispatcher ?? Dispatcher.FromThread(Thread.CurrentThread) ?? Application.Current?.Dispatcher
            ?? throw new InvalidOperationException("No dispatcher: create the TranslationSource on the UI thread or pass the UI Dispatcher.");
        Manager = manager;
        Messages = messages;
        Renderer = renderer;
        foreach (MemberInfo member in messages.GetType().GetMembers(BindingFlags.Public | BindingFlags.Instance))
        {
            if (member is PropertyInfo { GetMethod: not null } property && property.GetIndexParameters().Length == 0) _members[member.Name] = property;
            else if (member is MethodInfo { IsSpecialName: false, IsGenericMethodDefinition: false } method && method.DeclaringType != typeof(object)) _members[member.Name] = method;
        }
        _hook = new ManagerHook(this, manager);
        lock (RegistryGate)
        {
            Registry.RemoveAll(item => !item.TryGetTarget(out _));
            Registry.Add(new(this));
        }
    }

    /// <summary>The manager this source follows.</summary>
    public ITranslationManager Manager { get; }

    /// <summary>The generated readable surface this source resolves message names against.</summary>
    public object Messages { get; }

    /// <summary>The inline renderer used for rich messages, or <see langword="null"/>.</summary>
    public WpfInlineRenderer? Renderer { get; }

    /// <summary>Increases on every refresh; bindings with inputs use it to re-read their message.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public int Version => Volatile.Read(ref _version);

    /// <summary>Gets a plain message with no inputs by its flattened readable name, such as <c>application_title</c>.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string this[string key] => Format(key, [], null);

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Refreshes every binding. Safe to call from any thread; the update runs on the source's dispatcher.</summary>
    public void Invalidate()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (_dispatcher.CheckAccess()) Raise(); else _dispatcher.BeginInvoke(DispatcherPriority.Normal, Raise);
    }

    /// <summary>
    /// Republishes the active locale from its sources, then refreshes every binding. Do not await it with
    /// <c>ConfigureAwait(false)</c> and then touch the UI; the refresh itself needs no UI thread.
    /// </summary>
    public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
    {
        await Manager.RefreshAsync(cancellationToken).ConfigureAwait(false);
        if (Manager is not ITranslationSnapshotNotifier) Invalidate();
    }

    /// <summary>Stops following the manager and clears <see cref="Default"/> when it is this source. Bound elements keep their last value.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _hook.Detach();
        lock (_gate) _listeners.Clear();
        lock (RegistryGate) Registry.RemoveAll(item => !item.TryGetTarget(out TranslationSource? live) || ReferenceEquals(live, this));
        lock (DefaultGate) { if (ReferenceEquals(s_default, this)) { s_default = null; s_defaultUsed = false; } }
    }

    /// <summary>Marks <see cref="Default"/> as used and returns it.</summary>
    internal static TranslationSource? UseDefault()
    {
        lock (DefaultGate)
        {
            if (s_default is not null) s_defaultUsed = true;
            return s_default;
        }
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
            _listeners.RemoveAll(item => !item.TryGetTarget(out RichMessageBinding? target) || ReferenceEquals(target, listener));
            _listeners.Add(new(listener));
        }
    }

    internal void RemoveListener(RichMessageBinding listener)
    {
        lock (_gate) _listeners.RemoveAll(item => !item.TryGetTarget(out RichMessageBinding? target) || ReferenceEquals(target, listener));
    }

    internal int ListenerCount { get { lock (_gate) return _listeners.Count; } }

    private MemberInfo Find(string key) => _members.TryGetValue(key, out MemberInfo? member)
        ? member
        : throw new ArgumentException($"'{key}' is not a message of {Messages.GetType().Name}. Use the flattened readable name, such as 'checkout_help'.", nameof(key));

    private static ParameterInfo[] Parameters(MemberInfo member) => member is MethodInfo method ? method.GetParameters() : [];

    private static string Signature(string key, ParameterInfo[] parameters) =>
        $"{key}({string.Join(", ", parameters.Select(parameter => parameter.ParameterType.Name + " " + parameter.Name))})";

    /// <summary>
    /// Load-time check for a binding whose catalog is only known later (inherited or default): throws unless some live source
    /// has a matching message. With no live source yet (startup order, designers) nothing can be said and it passes.
    /// </summary>
    internal static void CheckLive(string key, bool rich, int? inputCount, IReadOnlyList<string>? names)
    {
        TranslationSource[] live;
        lock (RegistryGate)
        {
            Registry.RemoveAll(item => !item.TryGetTarget(out _));
            live = [.. Registry.Select(item => item.TryGetTarget(out TranslationSource? source) ? source : null!).Where(source => source is not null && Volatile.Read(ref source._disposed) == 0)];
        }
        if (live.Length == 0) return;
        ArgumentException? first = null;
        bool anyHas = false;
        foreach (TranslationSource source in live)
        {
            if (!source._members.ContainsKey(key)) continue;
            anyHas = true;
            try { source.Validate(key, rich, inputCount, names); return; }
            catch (ArgumentException exception) { first ??= exception; }
        }
        if (!anyHas) throw new ArgumentException($"No translation source has a message '{key}'. Use the flattened readable name, such as 'checkout_help'.", nameof(key));
        throw first!;
    }

    /// <summary>The number of inputs of a message.</summary>
    internal int InputCount(string key) => Parameters(Find(key)).Length;

    /// <summary>
    /// Throws a descriptive exception unless <paramref name="key"/> names a message of the expected kind and, when given,
    /// the supplied inputs (by position, or by <paramref name="names"/>) match its parameters exactly.
    /// </summary>
    internal void Validate(string key, bool? rich, int? inputCount = null, IReadOnlyList<string>? names = null)
    {
        MemberInfo member = Find(key);
        if (rich is { } expected && IsRichType(ResultType(member)) != expected)
            throw new ArgumentException(expected
                ? $"Message '{key}' has no markup; bind it with {{rt:Message {key}}}."
                : $"Message '{key}' has markup; bind it with TranslationProperties.RichMessage on a TextBlock.", nameof(key));
        if (inputCount is { } count) Order(key, Parameters(member), count, names);
    }

    /// <summary>Maps supplied inputs to parameter positions: (index in the supplied list) per parameter.</summary>
    private static int[] Order(string key, ParameterInfo[] parameters, int count, IReadOnlyList<string>? names)
    {
        string signature = Signature(key, parameters);
        if (count != parameters.Length) throw new ArgumentException($"Message {signature} takes {parameters.Length} input(s) but {count} were supplied.", nameof(key));
        var map = new int[parameters.Length];
        if (names is null)
        {
            for (int index = 0; index < map.Length; index++) map[index] = index;
            return map;
        }
        if (names.Count != count) throw new ArgumentException($"Message {signature} got {names.Count} input name(s) for {count} value(s).", nameof(key));
        Array.Fill(map, -1);
        for (int supplied = 0; supplied < names.Count; supplied++)
        {
            int slot = Array.FindIndex(parameters, parameter => string.Equals(parameter.Name, names[supplied], StringComparison.Ordinal));
            if (slot < 0) throw new ArgumentException($"Message {signature} has no input named '{names[supplied]}'.", nameof(key));
            if (map[slot] >= 0) throw new ArgumentException($"Input '{names[supplied]}' of {signature} is given twice.", nameof(key));
            map[slot] = supplied;
        }
        return map;
    }

    /// <summary>Formats a plain message; any non-fatal failure is traced and shown as <c>[key]</c>, as for a WPF binding error.</summary>
    internal string Format(string key, IReadOnlyList<object?> values, IReadOnlyList<string>? names)
    {
        try { return Resolve(key, values, names) as string ?? throw new InvalidOperationException($"Message '{key}' has markup."); }
        catch (Exception exception) when (!IsFatal(exception))
        {
            Trace.TraceWarning($"Runic.Translations.Wpf: cannot resolve message '{key}': {exception.Message}");
            return "[" + key + "]";
        }
    }

    /// <summary>Evaluates a message against the active snapshot: a string, or typed rich content.</summary>
    internal object Resolve(string key, IReadOnlyList<object?> values, IReadOnlyList<string>? names = null)
    {
        MemberInfo member = Find(key);
        if (member is PropertyInfo property)
        {
            Order(key, [], values.Count, names);
            try { return property.GetValue(Messages)!; }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
        var method = (MethodInfo)member;
        ParameterInfo[] parameters = method.GetParameters();
        int[] map = Order(key, parameters, values.Count, names);
        var converted = new object?[parameters.Length];
        for (int index = 0; index < converted.Length; index++) converted[index] = Convert(key, parameters[index], values[map[index]]);
        try { return method.Invoke(Messages, converted)!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static bool IsFatal(Exception exception) =>
        exception is OutOfMemoryException or StackOverflowException or AccessViolationException or ThreadAbortException or AppDomainUnloadedException;

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

    /// <summary>Binds generated slots (an <c>IRmf2SlotBindings&lt;T&gt;</c> object) to typed content, or returns <see langword="null"/> for an untyped message.</summary>
    internal static BoundLocalizedTextContent? Bind(object value, object slots)
    {
        MethodInfo? bind = value.GetType().GetMethod("Bind");
        if (bind is null) return null;
        try { return (BoundLocalizedTextContent)bind.Invoke(value, [slots])!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Converts a supplied input to the parameter type: null becomes "" for text and fails for a number; other values convert with the invariant culture.</summary>
    private static object? Convert(string key, ParameterInfo parameter, object? value)
    {
        Type parameterType = parameter.ParameterType;
        Type target = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (value is null)
        {
            if (parameterType == typeof(string)) return string.Empty;
            if (!parameterType.IsValueType || target != parameterType) return null;
            throw new InvalidOperationException($"Input '{parameter.Name}' of '{key}' is null but needs a {parameterType.Name}.");
        }
        if (target.IsInstanceOfType(value)) return value;
        try
        {
            if (target == typeof(string)) return System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return System.Convert.ChangeType(value, target, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidOperationException($"Input '{parameter.Name}' of '{key}' cannot convert {value.GetType().Name} '{value}' to {parameterType.Name}.", exception);
        }
    }

    private sealed class ManagerHook
    {
        private readonly WeakReference<TranslationSource> _owner;
        private readonly ITranslationManager _manager;

        public ManagerHook(TranslationSource owner, ITranslationManager manager)
        {
            _owner = new WeakReference<TranslationSource>(owner);
            _manager = manager;
            if (manager is ITranslationSnapshotNotifier notifier) notifier.SnapshotPublished += OnPublished;
            else manager.LocaleChanged += OnLocaleChanged;
        }

        public void Detach()
        {
            if (_manager is ITranslationSnapshotNotifier notifier) notifier.SnapshotPublished -= OnPublished;
            else _manager.LocaleChanged -= OnLocaleChanged;
        }

        private void OnPublished(object? sender, TranslationSnapshotPublishedEventArgs e) => Notify();
        private void OnLocaleChanged(object? sender, TranslationLocaleChangedEventArgs e) => Notify();

        private void Notify()
        {
            if (_owner.TryGetTarget(out TranslationSource? owner)) owner.Invalidate(); else Detach();
        }
    }
}
