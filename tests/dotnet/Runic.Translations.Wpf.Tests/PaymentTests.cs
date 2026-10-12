using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using Runic.Translations;
using Runic.Translations.Wpf;
using TUnit.Core;

namespace Runic.Translations.Wpf.Tests;

/// <summary>The WPF payment consumer: custom badge, icon accessibility and callback lifetime of the inline renderer.</summary>
internal sealed class PaymentTests
{
    [Test, DisplayName("payment: invalid selected content reaches no WPF renderer, asset or theme callback"), NotInParallel(WpfSuite.Key, Order = 101)]
    public void InvalidSelectedContent()
    {
        var payment = new Payment();
        JsonObject strictContract = JsonNode.Parse(PaymentFixture.MarkupContract)!.AsObject();
        strictContract["messages"]!["payment"]!["slots"]!["retry"]!["min"] = 1;
        int prematureFactories = 0;
        var guardedSlots = new Dictionary<string, InlineMarkupBinding>(payment.Slots) {
            ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => { prematureFactories++; return new TextBlock(); }), false,
                _ => { prematureFactories++; return "Star"; }),
        };
        var guardedRenderer = new WpfInlineRenderer(strictContract.ToJsonString(), _ => { },
            new Dictionary<string, WpfMarkupFactory> { ["shop:badge"] = (_, _) => { prematureFactories++; return new Span(); } },
            (_, _) => prematureFactories++);
        bool rejectedSelectedTree = false;
        try { guardedRenderer.SetContent(payment.Target, "payment", payment.Snapshot.FormatContent(PaymentFixture.Key, [new TextArgument("count", 0), new TextArgument("tone", "positive")]), guardedSlots); }
        catch (TranslationFormatException) { rejectedSelectedTree = true; }
        Require(rejectedSelectedTree && prematureFactories == 0 && payment.Target.Inlines.Count == 0,
            "Invalid selected content reached a WPF renderer, asset or theme callback.");
    }

    [Test, DisplayName("payment: rendering keeps the locale and badge options and activates no action"), NotInParallel(WpfSuite.Key, Order = 102)]
    public void Render()
    {
        var payment = new Payment();
        payment.Render();
        Require(payment.Calls == 0 && payment.Target.Language.IetfLanguageTag == "en", "Rendering activated an action or lost locale.");
        Require(payment.BadgeRenders > 0, "The custom badge factory did not run.");
    }

    [Test, DisplayName("payment: a meaningful icon keeps its accessible name"), NotInParallel(WpfSuite.Key, Order = 103)]
    public void IconAccessibility()
    {
        var payment = new Payment();
        payment.Render();
        var icon = payment.Target.Inlines.OfType<InlineUIContainer>().Select(item => item.Child).OfType<Decorator>().Single();
        var peer = UIElementAutomationPeer.CreatePeerForElement(icon)!;
        Require(peer.GetName() == "Star" && peer.IsContentElement(), "Meaningful icon accessibility was lost.");
    }

    [Test, DisplayName("payment: whitespace icon alternate text is rejected and keeps the active action control"), NotInParallel(WpfSuite.Key, Order = 104)]
    public void BlankIconLabel()
    {
        var payment = new Payment();
        payment.Render();
        Button button = payment.Button();
        var blankLabelSlots = new Dictionary<string, InlineMarkupBinding>(payment.Slots) {
            ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock()), false, _ => " \t "),
        };
        bool rejectedBlankLabel = false;
        try { payment.Renderer.SetContent(payment.Target, "payment", payment.Content, blankLabelSlots); }
        catch (TranslationFormatException) { rejectedBlankLabel = true; }
        Require(rejectedBlankLabel, "Whitespace meaningful icon alternate text was accepted.");
        Require(ReferenceEquals(button, payment.Button()), "A rejected render replaced the active action control.");
    }

    [Test, DisplayName("payment: a rerender replaces the action control and retires the previous action"), NotInParallel(WpfSuite.Key, Order = 105)]
    public void RerenderRetiresAction()
    {
        var payment = new Payment();
        payment.Render();
        Button firstButton = payment.Button();
        payment.Render();
        Button button = payment.Button();
        Require(!ReferenceEquals(firstButton, button), "A successful rerender did not replace the action control.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(payment.Calls == 1, "Action did not fire once.");
        payment.Render();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(payment.Calls == 1, "Detached action remained active.");
    }

    [Test, DisplayName("payment: typed bound content renders the same WPF tree as the dictionary overload"), NotInParallel(WpfSuite.Key, Order = 106)]
    public void TypedBoundContent()
    {
        var payment = new Payment();
        payment.Render();
        string dictionaryShape = Shape(payment.Target);
        payment.Renderer.SetContent(payment.Target, payment.Bound());
        Require(Shape(payment.Target) == dictionaryShape && payment.Target.Language.IetfLanguageTag == "en", "Typed bound content rendered a different WPF tree.");
    }

    [Test, DisplayName("payment: a rejected typed render keeps the active content and a rerender retires its action"), NotInParallel(WpfSuite.Key, Order = 107)]
    public void RejectedTypedRender()
    {
        var payment = new Payment();
        BoundLocalizedTextContent bound = payment.Bound();
        payment.Renderer.SetContent(payment.Target, bound);
        Button boundButton = payment.Button();
        BoundLocalizedTextContent rejected = new LocalizedTextContent<PaymentSlots>(payment.Content).Bind(new PaymentSlots(
            (InlineLinkBinding)payment.Slots["privacy"], (InlineActionBinding)payment.Slots["retry"],
            new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock()), false, _ => " "), (InlineLinkBinding)payment.Slots["terms"]));
        bool rejectedBound = false;
        try { payment.Renderer.SetContent(payment.Target, rejected); }
        catch (TranslationFormatException) { rejectedBound = true; }
        Require(rejectedBound && ReferenceEquals(boundButton, payment.Button()), "A rejected typed render replaced the active content.");
        payment.Renderer.SetContent(payment.Target, bound);
        boundButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(payment.Calls == 0, "Detached typed action remained active.");
    }

    [Test, DisplayName("payment: ClearContent removes the rendered content"), NotInParallel(WpfSuite.Key, Order = 108)]
    public void Clear()
    {
        var payment = new Payment();
        payment.Renderer.SetContent(payment.Target, payment.Bound());
        WpfInlineRenderer.ClearContent(payment.Target); Require(payment.Target.Inlines.Count == 0, "Clear did not dispose content.");
    }

    /// <summary>A fresh payment consumer: snapshot, content for one item, slots, a renderer with the custom badge and a target.</summary>
    private sealed class Payment
    {
        internal Payment()
        {
            Content = Snapshot.FormatContent(PaymentFixture.Key, [new TextArgument("count", 1), new TextArgument("tone", "positive")]);
            Slots = new Dictionary<string, InlineMarkupBinding> {
                ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
                ["retry"] = new InlineActionBinding(() => Calls++), ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "★" }), false, _ => "Star"),
            };
            Renderer = new WpfInlineRenderer(PaymentFixture.MarkupContract, _ => { }, new Dictionary<string, WpfMarkupFactory> {
                ["shop:badge"] = (run, children) =>
                {
                    Require(run.Options["tone"] == "positive", "Badge lost its options.");
                    Require(!run.Options.ContainsKey("@note"), "Annotation leaked into WPF markup options.");
                    BadgeRenders++;
                    var span = new Span(); span.Inlines.AddRange(children); return span;
                } });
        }

        internal CompiledTranslationSnapshot Snapshot { get; } = PaymentFixture.CreateSnapshot();
        internal LocalizedTextContent Content { get; }
        internal Dictionary<string, InlineMarkupBinding> Slots { get; }
        internal WpfInlineRenderer Renderer { get; }
        internal TextBlock Target { get; } = new();
        internal int Calls { get; private set; }
        internal int BadgeRenders { get; private set; }

        internal void Render() => Renderer.SetContent(Target, "payment", Content, Slots);

        internal Button Button() => Target.Inlines.OfType<InlineUIContainer>().Select(item => item.Child).OfType<Button>().Single();

        internal BoundLocalizedTextContent Bound() => new LocalizedTextContent<PaymentSlots>(Content).Bind(new PaymentSlots(
            privacy: (InlineLinkBinding)Slots["privacy"], retry: (InlineActionBinding)Slots["retry"],
            star: (InlineIconBinding)Slots["star"], terms: (InlineLinkBinding)Slots["terms"]));
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static string Shape(TextBlock target) => string.Join("|", target.Inlines.Select(Shape));
    private static string Shape(Inline inline) => inline switch
    {
        Run run => "Run:" + run.Text,
        Span span => span.GetType().Name + "(" + string.Join(",", span.Inlines.Select(Shape)) + ")",
        InlineUIContainer container => "UI:" + container.Child?.GetType().Name,
        _ => inline.GetType().Name,
    };
}

internal static class PaymentFixture
{
    internal static TranslationKey Key { get; } = new("checkout", 0, "payment");

    internal const string MarkupContract = """
        {
          "version": 2,
          "contracts": {
            "runic:action": { "kind": "paired", "placement": "inline", "children": "inline", "interactive": true, "plainText": "explicit", "options": {} },
            "runic:icon": { "kind": "standalone", "placement": "inline", "children": "none", "interactive": false, "plainText": "alternateText", "options": {} },
            "runic:link": { "kind": "paired", "placement": "inline", "children": "inline", "interactive": true, "plainText": "children", "options": {} },
            "shop:badge": {
              "kind": "paired",
              "placement": "inline",
              "children": "inline",
              "interactive": false,
              "plainText": "children",
              "options": {
                "tone": { "type": "enum", "values": ["neutral", "positive"], "default": "neutral", "literalOnly": false }
              }
            }
          },
          "messages": {
            "payment": {
              "slots": {
                "privacy": { "kind": "runic:link", "min": 1, "max": 1 },
                "retry": { "kind": "runic:action", "min": 0, "max": 1 },
                "star": { "kind": "runic:icon", "min": 1, "max": 1 },
                "terms": { "kind": "runic:link", "min": 1, "max": 1 }
              },
              "structured": true,
              "content": "inline",
              "skeletons": [],
              "contentLocales": { "en": "en" }
            }
          }
        }
        """;

    internal static CompiledTranslationSnapshot CreateSnapshot()
    {
        CompiledRmf2Input[] inputs =
        [
            new("count", TextArgumentType.Int),
            new("tone", TextArgumentType.String),
        ];
        var message = new CompiledRmf2Message(
            inputs,
            [
                new("input", "count", new(new("input", "count"), TextArgumentType.Int, "integer")),
                new("input", "tone", new(new("input", "tone"), TextArgumentType.String, "string")),
            ],
            [new(new("input", "count"), TextArgumentType.Int, "plural")],
            [
                new([new("0", "0")], Nodes(includeRetry: false)),
                new([new()], Nodes(includeRetry: true)),
            ],
            "en");
        var catalog = new CompiledTranslationCatalog(
            "checkout",
            "en",
            [CompiledTranslationDefinition.FromRmf2Inputs("payment", inputs)],
            [new CompiledTranslationLocale("en", null,
                [new CompiledTranslationValue(0, "", CompiledTextMessage.FromRmf2(message))])]);
        return new CompiledTranslationSnapshot(catalog, "en");
    }

    private static CompiledRmf2Node[] Nodes(bool includeRetry)
    {
        var nodes = new List<CompiledRmf2Node>
        {
            new("Read "), Link("terms", "open"), new("terms"), Link("terms", "close"),
            new(" and "), Link("privacy", "open"), new("privacy"), Link("privacy", "close"), new(". "),
        };
        if (includeRetry)
        {
            nodes.Add(Action("retry", "open"));
            nodes.Add(new("Retry"));
            nodes.Add(Action("retry", "close"));
            nodes.Add(new(" "));
        }
        nodes.Add(Icon("star"));
        nodes.Add(new(" "));
        nodes.Add(new CompiledRmf2Node("shop:badge", "open",
            [new("tone", new("input", "tone"))],
            [new("note", new("string-literal", "internal"))]));
        nodes.Add(new("Ready"));
        nodes.Add(new CompiledRmf2Node("shop:badge", "close"));
        return nodes.ToArray();
    }

    private static CompiledRmf2Node Link(string slot, string kind) => Functional("runic:link", slot, kind);
    private static CompiledRmf2Node Action(string slot, string kind) => Functional("runic:action", slot, kind);
    private static CompiledRmf2Node Icon(string slot) => Functional("runic:icon", slot, "standalone");
    private static CompiledRmf2Node Functional(string name, string slot, string kind) =>
        kind == "close"
            ? new CompiledRmf2Node(name, kind)
            : new CompiledRmf2Node(name, kind, [new("ref", new("string-literal", slot))]);
}

/// <summary>Mirrors the generated slot type shape for the payment message.</summary>
internal sealed class PaymentSlots : IRmf2SlotBindings<PaymentSlots>
{
    public PaymentSlots(InlineLinkBinding privacy, InlineActionBinding retry, InlineIconBinding star, InlineLinkBinding terms)
    {
        Privacy = privacy ?? throw new ArgumentNullException(nameof(privacy));
        Retry = retry ?? throw new ArgumentNullException(nameof(retry));
        Star = star ?? throw new ArgumentNullException(nameof(star));
        Terms = terms ?? throw new ArgumentNullException(nameof(terms));
    }
    public InlineLinkBinding Privacy { get; }
    public InlineActionBinding Retry { get; }
    public InlineIconBinding Star { get; }
    public InlineLinkBinding Terms { get; }
    static string IRmf2SlotBindings<PaymentSlots>.MessageKey => "payment";
    void IRmf2SlotBindings<PaymentSlots>.CopyTo(IDictionary<string, MarkupBinding> destination)
    { destination.Add("privacy", Privacy); destination.Add("retry", Retry); destination.Add("star", Star); destination.Add("terms", Terms); }
}
