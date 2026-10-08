#pragma warning disable CA1822, CA1806 // FakeMessages mirrors generated instance members; constructor-failure checks discard the result.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Runic.Translations;
using Runic.Translations.Wpf;

/// <summary>XAML binding helper checks: plain and rich messages, inputs, publications, threads, templates and lifetime.</summary>
internal static class BindingTests
{
    private const string Namespaces =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' " +
        "xmlns:rt='clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf'";

    internal static void Run()
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
        var manager = new NotifyingManager();
        var messages = new FakeMessages(manager);
        var renderer = new WpfInlineRenderer(PaymentFixture.MarkupContract, _ => { }, new Dictionary<string, WpfMarkupFactory> {
            ["shop:badge"] = (_, children) => { var span = new Span(); span.Inlines.AddRange(children); return span; } });
        var source = new TranslationSource(manager, messages, renderer);
        TranslationSource.Default = source;
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

        Construction(manager, messages);
        Plain(manager, dispatcher, out TextBlock title);
        Inputs(manager, dispatcher);
        Publications(manager, messages, source, dispatcher, title);
        Templates(dispatcher);
        Rich(manager, messages, source, dispatcher);
        Lifetime(manager, source, dispatcher);
        Defaults(manager, source, dispatcher, title);
        Console.WriteLine("PASS WPF XAML binding helper: plain, inputs, rich, publications, templates, defaults and lifetime.");
    }

    private static void Construction(NotifyingManager manager, FakeMessages messages)
    {
        // The generated readable surface carries ReadableNameVersion; the facade itself does not.
        Throws<ArgumentException>(() => new TranslationSource(manager, new object()), "A non-generated messages object was accepted.");
        Throws<ArgumentException>(() => new TranslationSource(manager, new WrongFacade()), "The catalog class instead of its Messages was accepted.");
        // No ambient dispatcher: a thread without one must say which to use.
        Throws<AggregateException>(() => Task.Run(() => new TranslationSource(manager, messages)).Wait(), "A source was created without a dispatcher.");
    }

    private static readonly NotifyingManager Peer = new();

    private static void Plain(NotifyingManager manager, Dispatcher dispatcher, out TextBlock title)
    {
        title = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message application_title}\"/>");
        Flush(dispatcher);
        Require(title.Text == "Application", "Plain binding did not show the message: " + title.Text);
        manager.Switch("de");
        Flush(dispatcher);
        Require(title.Text == "Anwendung", "Plain binding did not follow the locale: " + title.Text);
        Task.Run(() => manager.Switch("en")).Wait();
        Flush(dispatcher);
        Require(title.Text == "Application", "A background locale change did not reach the UI.");
        manager.Switch("de");
        Flush(dispatcher);

        // Any failure of the message itself, not only argument errors, becomes [key] and a trace.
        var broken = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message broken}\"/>");
        Flush(dispatcher);
        Require(broken.Text == "[broken]", "A throwing message was not shown as [key]: " + broken.Text);
    }

    private static void Inputs(NotifyingManager manager, Dispatcher dispatcher)
    {
        var person = new Person { UserName = "Ada", Count = 3 };
        var greeting = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message greeting, Arg0={Binding UserName}}\"/>");
        var items = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message items, Arg0={Binding Count}}\"/>");
        var constant = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message greeting, Arg0={Binding Source=Grace}}\"/>");
        var named = Parse<TextBlock>("<TextBlock {NS}><TextBlock.Text><rt:Message Key='pair'><rt:MessageInput Name='second' Value='{Binding Source=B}'/>" +
            "<rt:MessageInput Name='first' Value='{Binding UserName}'/></rt:Message></TextBlock.Text></TextBlock>");
        var many = Parse<TextBlock>("<TextBlock {NS}><TextBlock.Text><rt:Message Key='many'>" +
            string.Concat("abcde".Select(name => $"<rt:MessageInput Name='{name}' Value='{{Binding Source={name}}}'/>")) + "</rt:Message></TextBlock.Text></TextBlock>");
        greeting.DataContext = items.DataContext = named.DataContext = person;
        Flush(dispatcher);
        Require(greeting.Text == "Hallo Ada" && items.Text == "3 Artikel" && constant.Text == "Hallo Grace", $"Inputs: {greeting.Text} | {items.Text} | {constant.Text}");
        Require(named.Text == "Ada+B", "Named inputs are matched by parameter name: " + named.Text);
        Require(many.Text == "a.b.c.d.e", "More than four named inputs: " + many.Text);
        manager.Switch("en");
        Flush(dispatcher);
        Require(greeting.Text == "Hello Ada" && items.Text == "3 items", $"Inputs after switch: {greeting.Text} | {items.Text}");
        greeting.DataContext = new Person { UserName = "Linus" };
        Flush(dispatcher);
        Require(greeting.Text == "Hello Linus", "A new DataContext did not update the message: " + greeting.Text);
        manager.Switch("de");
        Flush(dispatcher);

        // Load-time checks name the problem with parameter names.
        XamlParseException gap = Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message pair, Arg1={Binding Source=x}}\"/>"), "A gap in Arg0..Arg3 was accepted.");
        Require(gap.ToString().Contains("Arg0", StringComparison.Ordinal), "The gap error does not name Arg0: " + gap.Message);
        XamlParseException arity = Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message pair, Arg0={Binding Source=x}}\"/>"), "Too few inputs were accepted.");
        Require(arity.ToString().Contains("first", StringComparison.Ordinal) && arity.ToString().Contains("second", StringComparison.Ordinal), "The arity error omits parameter names.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS}><TextBlock.Text><rt:Message Key='pair'><rt:MessageInput Name='first' Value='{Binding Source=x}'/>" +
            "<rt:MessageInput Name='third' Value='{Binding Source=x}'/></rt:Message></TextBlock.Text></TextBlock>"), "An unknown input name was accepted.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS}><TextBlock.Text><rt:Message Key='pair'><rt:MessageInput Name='first' Value='{Binding Source=x}'/>" +
            "<rt:MessageInput Name='first' Value='{Binding Source=x}'/></rt:Message></TextBlock.Text></TextBlock>"), "A duplicate input name was accepted.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS}><TextBlock.Text><rt:Message Key='pair' Arg0='{Binding Source=x}'><rt:MessageInput Name='first' Value='{Binding Source=x}'/>" +
            "</rt:Message></TextBlock.Text></TextBlock>"), "Mixed input forms were accepted.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message nope}\"/>"), "An unknown message was accepted.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message payment, Arg0={Binding Source=1}}\"/>"), "A rich message was accepted as plain.");
    }

    private static void Publications(NotifyingManager manager, FakeMessages messages, TranslationSource source, Dispatcher dispatcher, TextBlock title)
    {
        // A refresh keeps the locale; the built-in notifier still publishes it, so no Invalidate is needed.
        messages.Suffix = "!";
        manager.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Flush(dispatcher);
        Require(title.Text == "Anwendung!", "A refresh publication did not update the UI: " + title.Text);
        messages.Suffix = "";
        source.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Flush(dispatcher);
        Require(title.Text == "Anwendung" && manager.Refreshes == 2, "TranslationSource.RefreshAsync did not refresh.");

        // A custom manager without the notifier follows LocaleChanged only; Invalidate covers refreshes.
        var plainManager = new FakeManager();
        var plainMessages = new FakeMessages(plainManager);
        using var legacy = new TranslationSource(plainManager, plainMessages);
        var text = new TextBlock();
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[application_title]") { Source = legacy });
        Flush(dispatcher);
        plainMessages.Suffix = "?";
        plainManager.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Flush(dispatcher);
        Require(text.Text == "Application", "A custom manager refresh changed the UI without Invalidate.");
        legacy.Invalidate();
        Flush(dispatcher);
        Require(text.Text == "Application?", "Invalidate did not refresh.");
        plainManager.Switch("de");
        Flush(dispatcher);
        Require(text.Text == "Anwendung?", "A custom manager LocaleChanged did not refresh.");
    }

    private static void Templates(Dispatcher dispatcher)
    {
        var person = new Person { UserName = "Ada", Count = 1 };
        var template = Parse<DataTemplate>("<DataTemplate {NS}><TextBlock Text=\"{rt:Message greeting, Arg0={Binding UserName}}\"/></DataTemplate>");
        var fromTemplate = (TextBlock)template.LoadContent();
        fromTemplate.DataContext = person;
        var styled = Parse<TextBlock>("<TextBlock {NS}><TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Text' Value=\"{rt:Message application_title}\"/>" +
            "</Style></TextBlock.Style></TextBlock>");
        var styledWithInput = Parse<TextBlock>("<TextBlock {NS}><TextBlock.Style><Style TargetType='TextBlock'><Setter Property='Text' Value=\"{rt:Message greeting, Arg0={Binding UserName}}\"/>" +
            "</Style></TextBlock.Style></TextBlock>");
        styledWithInput.DataContext = person;
        var button = Parse<Button>("<Button {NS}><Button.Template><ControlTemplate TargetType='Button'><TextBlock Text=\"{rt:Message application_title}\"/></ControlTemplate></Button.Template></Button>");
        button.ApplyTemplate();
        var fromControlTemplate = (TextBlock)VisualTreeHelper.GetChild(button, 0);
        Flush(dispatcher);
        Require(fromTemplate.Text == "Hallo Ada", "DataTemplate: " + fromTemplate.Text);
        Require(styled.Text == "Anwendung" && styledWithInput.Text == "Hallo Ada", $"Style setter: {styled.Text} | {styledWithInput.Text}");
        Require(fromControlTemplate.Text == "Anwendung", "ControlTemplate: " + fromControlTemplate.Text);
    }

    private static void Rich(NotifyingManager manager, FakeMessages messages, TranslationSource source, Dispatcher dispatcher)
    {
        manager.Switch("en");
        Flush(dispatcher);

        // Typed slots: the generated slots object is bound through LocalizedTextContent<T>.Bind.
        var rich = new TextBlock();
        TranslationProperties.SetSlots(rich, TypedSlots());
        TranslationProperties.SetArguments(rich, new object?[] { 1L });
        TranslationProperties.SetRichMessage(rich, "payment");
        Require(rich.Inlines.Count == 0, "Rich content rendered before the dispatcher turn (updates should coalesce).");
        Flush(dispatcher);
        int renders = messages.PaymentCalls;
        Require(HasButton(rich) && renders == 1, $"Rich content missing or rendered {renders} times.");
        manager.Switch("de");
        Flush(dispatcher);
        Require(messages.PaymentCalls == renders + 1, "Rich content did not refresh on a publication.");
        manager.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Flush(dispatcher);
        Require(messages.PaymentCalls == renders + 2, "Rich content did not refresh on a refresh.");

        // Inputs by position and by name; slots as a dictionary.
        TranslationProperties.SetArguments(rich, new Dictionary<string, object?> { ["count"] = 0L });
        Flush(dispatcher);
        Require(!HasButton(rich), "Named arguments did not re-render (count 0 has no retry action).");
        TranslationProperties.SetSlots(rich, DictionarySlots());
        TranslationProperties.SetArguments(rich, new object?[] { 1L });
        Flush(dispatcher);
        Require(HasButton(rich), "Dictionary slots did not render.");

        // Failures keep the previous content and never reach the dispatcher.
        TranslationProperties.SetArguments(rich, new object?[] { "abc" });
        Flush(dispatcher);
        Require(HasButton(rich), "A wrong argument type replaced the content.");
        TranslationProperties.SetArguments(rich, new object?[] { 1L, 2L });
        Flush(dispatcher);
        Require(HasButton(rich), "A wrong argument count replaced the content.");
        TranslationProperties.SetArguments(rich, new object?[] { 1L });
        TranslationProperties.SetSlots(rich, new object());
        Flush(dispatcher);
        Require(HasButton(rich), "A foreign slots object replaced the content.");
        TranslationProperties.SetSlots(rich, new Dictionary<string, InlineMarkupBinding>());
        Flush(dispatcher);
        Require(HasButton(rich), "Missing slots replaced the content.");
        TranslationProperties.SetRichMessage(rich, null);
        Flush(dispatcher);
        Require(rich.Inlines.Count == 0, "Clearing the key did not clear the content.");

        // Arguments that do not exist yet mean "not ready", not an error.
        var late = new TextBlock();
        TranslationProperties.SetSlots(late, TypedSlots());
        TranslationProperties.SetRichMessage(late, "payment");
        int before = messages.PaymentCalls;
        Flush(dispatcher);
        Require(late.Inlines.Count == 0 && messages.PaymentCalls == before, "A message with inputs rendered before its Arguments existed.");
        TranslationProperties.SetArguments(late, new object?[] { 1L });
        Flush(dispatcher);
        Require(HasButton(late), "Late Arguments did not render.");

        // Load-time checks: bad key, plain key as rich, missing renderer.
        Throws<ArgumentException>(() => TranslationProperties.SetRichMessage(new TextBlock(), "missing"), "A bad rich key was accepted.");
        Throws<ArgumentException>(() => TranslationProperties.SetRichMessage(new TextBlock(), "application_title"), "A plain message was accepted as rich.");
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} rt:TranslationProperties.RichMessage='missing'/>"), "XAML accepted a bad rich key.");
        using (var bare = new TranslationSource(Peer, new FakeMessages(Peer)))
        {
            var unrendered = new TextBlock();
            TranslationProperties.SetSource(unrendered, bare);
            Throws<InvalidOperationException>(() => TranslationProperties.SetRichMessage(unrendered, "payment"), "A missing renderer was accepted.");
        }

        // From XAML, with the source inherited from a parent.
        var panel = Parse<StackPanel>("<StackPanel {NS}><TextBlock rt:TranslationProperties.RichMessage='payment'/></StackPanel>");
        var child = (TextBlock)panel.Children[0];
        TranslationProperties.SetSlots(child, TypedSlots());
        TranslationProperties.SetArguments(child, new object?[] { 1L });
        Flush(dispatcher);
        Require(HasButton(child), "Rich content from XAML did not render.");
        TranslationProperties.SetSource(panel, source);
        Flush(dispatcher);
        Require(HasButton(child), "Setting an inherited source broke the element.");
    }


    private static void Lifetime(NotifyingManager manager, TranslationSource source, Dispatcher dispatcher)
    {
        (WeakReference a, WeakReference b, WeakReference c, WeakReference d) = CreateAbandoned(dispatcher);
        WeakReference window = ShowAndClose(dispatcher);
        WeakReference abandonedSource = CreateAbandonedSource(Peer);
        for (int attempt = 0; attempt < 5; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); Flush(dispatcher); }
        Require(!a.IsAlive, "A plain binding kept its element alive.");
        Require(!b.IsAlive, "A rich binding kept its element alive.");
        Require(!c.IsAlive, "A binding with inputs kept its element alive.");
        Require(!d.IsAlive, "A rich element with a typed slots object stayed alive.");
        Require(!window.IsAlive, "A closed window stayed alive.");
        Require(!abandonedSource.IsAlive, "The manager kept an abandoned source alive.");
        Peer.Switch("de");
        Require(Peer.Subscribers == 0, "An abandoned source did not detach from the manager.");
        Require(source.ListenerCount < 8, "Dead rich listeners accumulate: " + source.ListenerCount);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference, WeakReference, WeakReference) CreateAbandoned(Dispatcher dispatcher)
    {
        var plain = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message application_title}\"/>");
        var withInput = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message greeting, Arg0={Binding UserName}}\"/>");
        withInput.DataContext = new Person { UserName = "x" };
        var rich = new TextBlock();
        TranslationProperties.SetSlots(rich, DictionarySlots());
        TranslationProperties.SetArguments(rich, new object?[] { 1L });
        TranslationProperties.SetRichMessage(rich, "payment");
        var typed = new TextBlock();
        TranslationProperties.SetSlots(typed, TypedSlots());
        TranslationProperties.SetArguments(typed, new object?[] { 1L });
        TranslationProperties.SetRichMessage(typed, "payment");
        Flush(dispatcher);
        return (new WeakReference(plain), new WeakReference(rich), new WeakReference(withInput), new WeakReference(typed));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndClose(Dispatcher dispatcher)
    {
        var panel = Parse<StackPanel>("<StackPanel {NS}><TextBlock Text=\"{rt:Message application_title}\"/><TextBlock rt:TranslationProperties.RichMessage='payment'/></StackPanel>");
        var rich = (TextBlock)panel.Children[1];
        TranslationProperties.SetSlots(rich, DictionarySlots());
        TranslationProperties.SetArguments(rich, new object?[] { 1L });
        var window = new Window { Content = panel, ShowActivated = false, Width = 200, Height = 100, WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0 };
        window.Show();
        Flush(dispatcher);
        Require(HasButton(rich), "The rich element in a shown window did not render.");
        window.Close();
        Flush(dispatcher);
        return new WeakReference(window);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedSource(NotifyingManager peer) => new(new TranslationSource(peer, new FakeMessages(peer)));

    private static void Defaults(NotifyingManager manager, TranslationSource source, Dispatcher dispatcher, TextBlock title)
    {
        // The default was used by bindings, so replacing it silently would split plain and rich bindings.
        using var other = new TranslationSource(Peer, new FakeMessages(Peer));
        Throws<InvalidOperationException>(() => TranslationSource.Default = other, "Replacing a used default was accepted.");
        TranslationSource.Default = source;

        source.Dispose();
        Require(TranslationSource.Default is null, "Dispose did not clear the default.");
        manager.Switch("en");
        Flush(dispatcher);
        Require(title.Text == "Anwendung" && manager.Subscribers == 0, $"A disposed source still followed the manager: {title.Text}, {manager.Subscribers} subscribers.");

        // No source: a load fails with the reason.
        Throws<XamlParseException>(() => Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message application_title}\"/>"), "XAML loaded without a source.");
        Throws<InvalidOperationException>(() => TranslationProperties.SetRichMessage(new TextBlock(), "payment"), "A rich message was accepted without a source.");

        // In a designer there is no startup code: show the key and stay quiet. Permanent for the process, so it runs last.
        DesignerProperties.IsInDesignModeProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(true));
        var designed = Parse<TextBlock>("<TextBlock {NS} Text=\"{rt:Message application_title}\"/>");
        var designedRich = new TextBlock();
        TranslationProperties.SetRichMessage(designedRich, "payment");
        Flush(dispatcher);
        Require(designed.Text == "[application_title]" && designedRich.Inlines.Count == 0, "Design mode did not show the key quietly: " + designed.Text);
    }

    private static T Parse<T>(string xaml) => (T)XamlReader.Parse(xaml.Replace("{NS}", Namespaces, StringComparison.Ordinal));

    private static PaymentSlots TypedSlots() => new(
        privacy: new InlineLinkBinding(new Uri("https://example.test/privacy")), retry: new InlineActionBinding(() => { }),
        star: new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "*" }), false, _ => "Star"),
        terms: new InlineLinkBinding(new Uri("https://example.test/terms")));

    private static Dictionary<string, InlineMarkupBinding> DictionarySlots() => new()
    {
        ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
        ["retry"] = new InlineActionBinding(() => { }),
        ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "*" }), false, _ => "Star"),
    };

    private static bool HasButton(TextBlock block) => block.Inlines.OfType<InlineUIContainer>().Any(item => item.Child is Button);

    private static void Flush(Dispatcher dispatcher) => dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static T Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T exception) { return exception; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public sealed class Person
    {
        public string UserName { get; set; } = "";
        public int Count { get; set; }
    }

    /// <summary>A manager that only raises LocaleChanged, like a custom implementation.</summary>
    private class FakeManager : ITranslationManager
    {
        private readonly ITranslationSnapshot _snapshot = PaymentFixture.CreateSnapshot();
        public string CurrentLocale { get; private set; } = "en";
        public ITranslationSnapshot Current => _snapshot;
        public int Refreshes { get; protected set; }
        public virtual int Subscribers => LocaleChanged?.GetInvocationList().Length ?? 0;
        public event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;
        public virtual void Switch(string locale)
        {
            CurrentLocale = locale;
            LocaleChanged?.Invoke(this, new TranslationLocaleChangedEventArgs(_snapshot, _snapshot));
        }
        public ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default) { Switch(locale); return ValueTask.CompletedTask; }
        public virtual ValueTask RefreshAsync(CancellationToken cancellationToken = default) { Refreshes++; return ValueTask.CompletedTask; }
    }

    /// <summary>A manager that also publishes every snapshot, like the built-in one.</summary>
    private sealed class NotifyingManager : FakeManager, ITranslationSnapshotNotifier
    {
        public event EventHandler<TranslationSnapshotPublishedEventArgs>? SnapshotPublished;
        public override int Subscribers => base.Subscribers + (SnapshotPublished?.GetInvocationList().Length ?? 0);
        public override void Switch(string locale)
        {
            base.Switch(locale);
            SnapshotPublished?.Invoke(this, new TranslationSnapshotPublishedEventArgs(Current, isRefresh: false));
        }
        public override ValueTask RefreshAsync(CancellationToken cancellationToken = default)
        {
            Refreshes++;
            SnapshotPublished?.Invoke(this, new TranslationSnapshotPublishedEventArgs(Current, isRefresh: true));
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WrongFacade { public string application_title => ""; }

    /// <summary>Mirrors the shape of a generated readable surface: properties, methods and typed rich content.</summary>
    private sealed class FakeMessages(FakeManager manager)
    {
        public const int ReadableNameVersion = 1;
        private bool German => manager.CurrentLocale == "de";
        public string Suffix { get; set; } = "";
        public int PaymentCalls { get; private set; }
        public string application_title => (German ? "Anwendung" : "Application") + Suffix;
        public string broken => throw new KeyNotFoundException("missing resource");
        public string greeting(string name) => (German ? "Hallo " : "Hello ") + name;
        public string items(long count) => German ? count + " Artikel" : count + " items";
        public string pair(string first, string second) => first + "+" + second;
        public string many(string a, string b, string c, string d, string e) => string.Join(".", a, b, c, d, e);
        public LocalizedTextContent<PaymentSlots> payment(long count)
        {
            PaymentCalls++;
            return new(manager.Current.FormatContent(PaymentFixture.Key, [new TextArgument("count", count), new TextArgument("tone", "positive")]));
        }
    }
}
