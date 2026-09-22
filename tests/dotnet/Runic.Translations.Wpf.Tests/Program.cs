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

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        CompiledTranslationSnapshot snapshot = PaymentFixture.CreateSnapshot();
        LocalizedTextContent content = snapshot.FormatContent(PaymentFixture.Key,
            [new TextArgument("count", 1), new TextArgument("tone", "positive")]);
        int calls = 0;
        var slots = new Dictionary<string, InlineMarkupBinding> {
            ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
            ["retry"] = new InlineActionBinding(() => calls++), ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "★" }), false, _ => "Star"),
        };
        var renderer = new WpfInlineRenderer(PaymentFixture.MarkupContract, _ => { }, new Dictionary<string, WpfMarkupFactory> { ["shop:badge"] = (run, children) => { Require(run.Options["tone"] == "positive", "Badge lost its options."); Require(!run.Options.ContainsKey("@note"), "Annotation leaked into WPF markup options."); var span = new Span(); span.Inlines.AddRange(children); return span; } });
        var target = new TextBlock();
        JsonObject strictContract = JsonNode.Parse(PaymentFixture.MarkupContract)!.AsObject();
        strictContract["messages"]!["payment"]!["slots"]!["retry"]!["min"] = 1;
        int prematureFactories = 0;
        var guardedSlots = new Dictionary<string, InlineMarkupBinding>(slots) {
            ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => { prematureFactories++; return new TextBlock(); }), false,
                _ => { prematureFactories++; return "Star"; }),
        };
        var guardedRenderer = new WpfInlineRenderer(strictContract.ToJsonString(), _ => { },
            new Dictionary<string, WpfMarkupFactory> { ["shop:badge"] = (_, _) => { prematureFactories++; return new Span(); } },
            (_, _) => prematureFactories++);
        bool rejectedSelectedTree = false;
        try { guardedRenderer.SetContent(target, "payment", snapshot.FormatContent(PaymentFixture.Key, [new TextArgument("count", 0), new TextArgument("tone", "positive")]), guardedSlots); }
        catch (TranslationFormatException) { rejectedSelectedTree = true; }
        Require(rejectedSelectedTree && prematureFactories == 0 && target.Inlines.Count == 0,
            "Invalid selected content reached a WPF renderer, asset or theme callback.");
        renderer.SetContent(target, "payment", content, slots);
        Require(calls == 0 && target.Language.IetfLanguageTag == "en", "Rendering activated an action or lost locale.");
        var containers = target.Inlines.OfType<InlineUIContainer>().ToArray();
        var button = containers.Select(item => item.Child).OfType<Button>().Single();
        var icon = containers.Select(item => item.Child).OfType<Decorator>().Single();
        var peer = UIElementAutomationPeer.CreatePeerForElement(icon)!;
        Require(peer.GetName() == "Star" && peer.IsContentElement(), "Meaningful icon accessibility was lost.");
        var blankLabelSlots = new Dictionary<string, InlineMarkupBinding>(slots) {
            ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock()), false, _ => " \t "),
        };
        bool rejectedBlankLabel = false;
        try { renderer.SetContent(target, "payment", content, blankLabelSlots); }
        catch (TranslationFormatException) { rejectedBlankLabel = true; }
        Require(rejectedBlankLabel, "Whitespace meaningful icon alternate text was accepted.");
        Require(ReferenceEquals(button, target.Inlines.OfType<InlineUIContainer>().Select(item => item.Child).OfType<Button>().Single()),
            "A rejected render replaced the active action control.");
        var firstButton = button;
        renderer.SetContent(target, "payment", content, slots);
        button = target.Inlines.OfType<InlineUIContainer>().Select(item => item.Child).OfType<Button>().Single();
        Require(!ReferenceEquals(firstButton, button), "A successful rerender did not replace the action control.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(calls == 1, "Action did not fire once.");
        renderer.SetContent(target, "payment", content, slots);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(calls == 1, "Detached action remained active.");
        WpfInlineRenderer.ClearContent(target); Require(target.Inlines.Count == 0, "Clear did not dispose content.");
        Console.WriteLine("PASS WPF payment consumer, custom badge, icon accessibility and callback lifetime.");
        return 0;
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

internal static class PaymentFixture
{
    internal static TranslationKey Key { get; } = new("checkout", 0, "payment");

    internal const string MarkupContract = """
        {
          "version": 1,
          "contracts": {
            "runic:action": { "kind": "paired", "interactive": true, "plainText": "explicit", "options": {} },
            "runic:icon": { "kind": "standalone", "interactive": false, "plainText": "alternateText", "options": {} },
            "runic:link": { "kind": "paired", "interactive": true, "plainText": "children", "options": {} },
            "shop:badge": {
              "kind": "paired",
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
