using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Threading;
using Runic.Translations;
using Runic.Translations.Wpf;

/// <summary>XAML binding helper checks: plain and rich messages, inputs, locale changes, threads and lifetime.</summary>
internal static class BindingTests
{
    private const string Namespaces =
        "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:rt='clr-namespace:Runic.Translations.Wpf;assembly=Runic.Translations.Wpf'";

    internal static void Run()
    {
        var manager = new FakeManager();
        var messages = new FakeMessages(manager);
        var renderer = new WpfInlineRenderer(PaymentFixture.MarkupContract, _ => { });
        using var source = new TranslationSource(manager, messages, renderer);
        TranslationSource.Default = source;
        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;

        // Plain message without inputs, from real XAML; follows the locale.
        var title = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message application_title}}\"/>");
        Flush(dispatcher);
        Require(title.Text == "Application", "Plain binding did not show the message: " + title.Text);
        manager.Switch("de");
        Flush(dispatcher);
        Require(title.Text == "Anwendung", "Plain binding did not follow LocaleChanged: " + title.Text);

        // Inputs from the DataContext, converted to the parameter type, and constants.
        var person = new Person { UserName = "Ada", Count = 3 };
        var greeting = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message greeting, Arg0={{Binding UserName}}}}\"/>");
        var items = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message items, Arg0={{Binding Count}}}}\"/>");
        var constant = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message greeting, Arg0={{Binding Source=Grace}}}}\"/>");
        greeting.DataContext = items.DataContext = person;
        Flush(dispatcher);
        Require(greeting.Text == "Hallo Ada" && items.Text == "3 Artikel" && constant.Text == "Hallo Grace", $"Inputs: {greeting.Text} | {items.Text} | {constant.Text}");
        manager.Switch("en");
        Flush(dispatcher);
        Require(greeting.Text == "Hello Ada" && items.Text == "3 items", $"Inputs after switch: {greeting.Text} | {items.Text}");
        greeting.DataContext = new Person { UserName = "Linus" };
        Flush(dispatcher);
        Require(greeting.Text == "Hello Linus", "A new DataContext did not update the message: " + greeting.Text);

        // LocaleChanged raised off the UI thread is marshalled to the dispatcher.
        Task.Run(() => manager.Switch("de")).Wait();
        Flush(dispatcher);
        Require(title.Text == "Anwendung" && greeting.Text == "Hallo Linus", "A background locale change did not reach the UI.");
        Require(source.Locale == "de", "Source lost the locale.");

        // Refresh republishes without LocaleChanged; the app invalidates.
        messages.Suffix = "!";
        Flush(dispatcher);
        Require(title.Text == "Anwendung", "A refresh without Invalidate changed the UI.");
        source.Invalidate();
        Flush(dispatcher);
        Require(title.Text == "Anwendung!", "Invalidate did not refresh: " + title.Text);
        messages.Suffix = "";
        source.RefreshAsync().AsTask().GetAwaiter().GetResult();
        Flush(dispatcher);
        Require(title.Text == "Anwendung" && manager.Refreshes == 1, "RefreshAsync did not refresh the manager and the UI.");

        // Eager validation names the problem.
        Throws<ArgumentException>(() => source.Validate("missing", 0, false), "Unknown key was accepted.");
        Throws<ArgumentException>(() => source.Validate("greeting", 0, false), "Wrong input count was accepted.");
        Throws<ArgumentException>(() => source.Validate("payment", 1, false), "A rich message was accepted as plain.");
        Throws<ArgumentException>(() => source.Validate("application_title", 0, true), "A plain message was accepted as rich.");
        Throws<XamlParseException>(() => XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message nope}}\"/>"), "XAML accepted an unknown message.");

        // Rich message: renders the inline tree with slots and inputs, and re-renders on locale change and new inputs.
        var rich = new TextBlock();
        Translations.SetSlots(rich, Slots(out int[] actions));
        Translations.SetArguments(rich, [1L]);
        Translations.SetRichMessage(rich, "payment");
        Require(rich.Inlines.Count == 0, "Rich content rendered before the dispatcher turn (updates should coalesce).");
        Flush(dispatcher);
        int renders = messages.PaymentCalls;
        Require(rich.Inlines.OfType<InlineUIContainer>().Any(item => item.Child is Button) && renders == 1, $"Rich content missing or rendered {renders} times.");
        manager.Switch("en");
        Flush(dispatcher);
        Require(messages.PaymentCalls == renders + 1, "Rich content did not refresh on LocaleChanged.");
        Translations.SetArguments(rich, [0L]);
        Flush(dispatcher);
        Require(!rich.Inlines.OfType<InlineUIContainer>().Any(item => item.Child is Button), "New arguments did not re-render (count 0 has no retry action).");
        var richFromXaml = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} rt:Translations.RichMessage=\"payment\"/>");
        Translations.SetSlots(richFromXaml, Slots(out _));
        Translations.SetArguments(richFromXaml, [1L]);
        Flush(dispatcher);
        Require(richFromXaml.Inlines.Count > 0, "Rich content from XAML did not render.");
        // A slot problem keeps the previous content instead of crashing the dispatcher.
        int before = rich.Inlines.Count;
        Translations.SetSlots(rich, new Dictionary<string, InlineMarkupBinding>());
        Flush(dispatcher);
        Require(rich.Inlines.Count == before, "A rejected render replaced the content.");
        Translations.SetRichMessage(rich, null);
        Flush(dispatcher);
        Require(rich.Inlines.Count == 0, "Clearing the key did not clear the content.");

        // Lifetime: neither the source nor the manager keeps bound elements alive.
        (WeakReference plain, WeakReference weakRich) = CreateAbandoned(dispatcher, source);
        for (int attempt = 0; attempt < 4; attempt++) { GC.Collect(); GC.WaitForPendingFinalizers(); Flush(dispatcher); }
        Require(!plain.IsAlive, "A plain binding kept its element alive.");
        Require(!weakRich.IsAlive, "A rich binding kept its element alive.");

        // After Dispose the source stops following the manager.
        source.Dispose();
        manager.Switch("en");
        Flush(dispatcher);
        Require(title.Text == "Anwendung" && manager.Subscribers == 0, "A disposed source still followed the manager.");
        TranslationSource.Default = null;
        GC.KeepAlive(messages);
        Console.WriteLine("PASS WPF XAML binding helper: plain, inputs, rich, locale changes, threads and lifetime.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference, WeakReference) CreateAbandoned(Dispatcher dispatcher, TranslationSource source)
    {
        var plain = (TextBlock)XamlReader.Parse($"<TextBlock {Namespaces} Text=\"{{rt:Message application_title}}\"/>");
        var rich = new TextBlock();
        Translations.SetSlots(rich, Slots(out _));
        Translations.SetArguments(rich, [1L]);
        Translations.SetRichMessage(rich, "payment");
        Flush(dispatcher);
        return (new WeakReference(plain), new WeakReference(rich));
    }

    private static Dictionary<string, InlineMarkupBinding> Slots(out int[] actions)
    {
        int[] counter = actions = [0];
        return new()
        {
            ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
            ["retry"] = new InlineActionBinding(() => counter[0]++),
            ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "*" }), false, _ => "Star"),
        };
    }

    private static void Flush(Dispatcher dispatcher) => dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    public sealed class Person
    {
        public string UserName { get; set; } = "";
        public int Count { get; set; }
    }

    /// <summary>A manager whose locale and snapshot are driven by the test.</summary>
    private sealed class FakeManager : ITranslationManager
    {
        private readonly ITranslationSnapshot _snapshot = PaymentFixture.CreateSnapshot();
        public string CurrentLocale { get; private set; } = "en";
        public ITranslationSnapshot Current => _snapshot;
        public int Refreshes { get; private set; }
        public int Subscribers => LocaleChanged?.GetInvocationList().Length ?? 0;
        public event EventHandler<TranslationLocaleChangedEventArgs>? LocaleChanged;
        public void Switch(string locale)
        {
            CurrentLocale = locale;
            LocaleChanged?.Invoke(this, new TranslationLocaleChangedEventArgs(_snapshot, _snapshot));
        }
        public ValueTask SetLocaleAsync(string locale, CancellationToken cancellationToken = default) { Switch(locale); return ValueTask.CompletedTask; }
        public ValueTask RefreshAsync(CancellationToken cancellationToken = default) { Refreshes++; return ValueTask.CompletedTask; }
    }

    /// <summary>Mirrors the shape of a generated readable surface: properties, methods and typed rich content.</summary>
    private sealed class FakeMessages(FakeManager manager)
    {
        private bool German => manager.CurrentLocale == "de";
        public string Suffix { get; set; } = "";
        public int PaymentCalls { get; private set; }
        public string application_title => (German ? "Anwendung" : "Application") + Suffix;
        public string greeting(string name) => (German ? "Hallo " : "Hello ") + name;
        public string items(long count) => German ? count + " Artikel" : count + " items";
        public LocalizedTextContent<PaymentSlots> payment(long count)
        {
            PaymentCalls++;
            return new(manager.Current.FormatContent(PaymentFixture.Key, [new TextArgument("count", count), new TextArgument("tone", "positive")]));
        }
    }
}
