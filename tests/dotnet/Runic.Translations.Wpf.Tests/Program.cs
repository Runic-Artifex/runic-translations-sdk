using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using Runic.Translations;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Wpf;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "payment");
        var compiled = TranslationCompiler.CompileRmf2ProjectV5(Source("runic.json"), [Source("en.rmf2"), Source("de.rmf2")]);
        Require(compiled.Success, string.Join("; ", compiled.Diagnostics.Select(d => d.Message)));
        Rmf2ProjectV5 project = compiled.Project!;
        Rmf2MessageContractV5 payment = project.CanonicalMessages.Single(message => message.Key == "payment");
        var key = new TranslationKey(project.Id, payment.Id, payment.Key);
        var contract = TranslationPackContract.CreateRmf2V5(project.Id, "en", project.CallerFingerprint,
            project.CanonicalMessages.Select(message => TranslationPackMessageContract.FromRmf2Inputs(
                new TranslationKey(project.Id, message.Id, message.Key), Inputs(message.Inputs))).ToArray(), project.MarkupContract);
        var verified = TranslationPackLoader.VerifyAsync(new ExternalTranslationPack(Rmf2LocaleArtifactV5.Render(project, "en").GetUtf8Bytes()), contract).AsTask().GetAwaiter().GetResult();
        var runtime = new CompiledTranslationCatalog("checkout", "en",
            project.CanonicalMessages.Select(message => CompiledTranslationDefinition.FromRmf2Inputs(message.Key, Inputs(message.Inputs))).ToArray(),
            [new CompiledTranslationLocale("en", null, verified.Messages.Select(message => new CompiledTranslationValue(message.Key.Id, "", message.Message!)).ToArray())]);
        var snapshot = new CompiledTranslationSnapshot(runtime, "en");
        var content = snapshot.FormatContent(key, [new TextArgument("count", 1), new TextArgument("tone", "positive")]);
        int calls = 0;
        var slots = new Dictionary<string, InlineMarkupBinding> {
            ["terms"] = new InlineLinkBinding(new Uri("https://example.test/terms")), ["privacy"] = new InlineLinkBinding(new Uri("https://example.test/privacy")),
            ["retry"] = new InlineActionBinding(() => calls++), ["star"] = new InlineIconBinding((Func<FrameworkElement>)(() => new TextBlock { Text = "★" }), false, _ => "Star"),
        };
        var renderer = new WpfInlineRenderer(project.MarkupContract, _ => { }, new Dictionary<string, WpfMarkupFactory> { ["shop:badge"] = (run, children) => { Require(run.Options["tone"] == "positive", "Badge lost its options."); Require(!run.Options.ContainsKey("@note"), "Annotation leaked into WPF markup options."); var span = new Span(); span.Inlines.AddRange(children); return span; } });
        var target = new TextBlock();
        JsonObject strictContract = JsonNode.Parse(project.MarkupContract)!.AsObject();
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
        try { guardedRenderer.SetContent(target, "payment", snapshot.FormatContent(key, [new TextArgument("count", 0), new TextArgument("tone", "positive")]), guardedSlots); }
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
        TranslationSource Source(string name)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(directory, name));
            if (name == "en.rmf2") bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("{#badge tone=$tone}", "{#badge tone=$tone @note=|internal|}"));
            return new TranslationSource(Path.Combine(directory, name), bytes);
        }
    }
    private static CompiledRmf2Input[] Inputs(IReadOnlyList<Rmf2InputV5> inputs) => inputs.Select(input => new CompiledRmf2Input(input.Name, input.Type switch
    {
        "string" => TextArgumentType.String, "int64" => TextArgumentType.Int, "decimal" => TextArgumentType.Number,
        "boolean" => TextArgumentType.Bool, "date" => TextArgumentType.Date, "time" => TextArgumentType.Time,
        "datetime" => TextArgumentType.DateTime, "guid" => TextArgumentType.Guid, _ => throw new InvalidOperationException("Unknown input type."),
    })).ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
