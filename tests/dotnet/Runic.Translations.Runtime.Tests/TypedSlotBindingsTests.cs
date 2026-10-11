using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Runic.Translations;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

/// <summary>Typed slot bindings (W220-002). Also linked into the NativeAOT runtime probe fixture.</summary>
internal sealed class TypedSlotBindingsTests
{
    internal const string MarkupContract = """
        {
          "version": 2,
          "contracts": {
            "runic:action": { "kind": "paired", "placement": "inline", "children": "inline", "interactive": true, "plainText": "explicit", "options": {} },
            "runic:icon": { "kind": "standalone", "placement": "inline", "children": "none", "interactive": false, "plainText": "alternateText", "options": {} },
            "runic:link": { "kind": "paired", "placement": "inline", "children": "inline", "interactive": true, "plainText": "children", "options": {} }
          },
          "messages": {
            "help": {
              "slots": {
                "guide": { "kind": "runic:link", "min": 1, "max": 1 },
                "retry": { "kind": "runic:action", "min": 0, "max": 1 },
                "star": { "kind": "runic:icon", "min": 1, "max": 1 }
              },
              "structured": true,
              "content": "inline",
              "skeletons": [],
              "contentLocales": { "en": "en" }
            }
          }
        }
        """;

    internal static LocalizedTextContent Content(bool includeRetry = true, bool includeStar = true, string guide = "guide") =>
        Message(includeRetry, includeStar, guide).FormatContent([], "en");

    private static CompiledRmf2Message Message(bool includeRetry, bool includeStar, string guide)
    {
        var nodes = new List<CompiledRmf2Node> { new("See "), Functional("runic:link", guide, "open"), new("the guide"), Functional("runic:link", guide, "close") };
        if (includeRetry) { nodes.Add(new(" or ")); nodes.Add(Functional("runic:action", "retry", "open")); nodes.Add(new("retry")); nodes.Add(Functional("runic:action", "retry", "close")); }
        if (includeStar) { nodes.Add(new(" ")); nodes.Add(Functional("runic:icon", "star", "standalone")); }
        return new CompiledRmf2Message([], [], [], [new([], nodes.ToArray())], "en");
    }

    private static CompiledRmf2Node Functional(string name, string slot, string kind) =>
        kind == "close" ? new CompiledRmf2Node(name, kind) : new CompiledRmf2Node(name, kind, [new("ref", new("string-literal", slot))]);

    internal static HelpSlots Slots(Action? retry = null) => new(
        guide: new InlineLinkBinding(new Uri("https://example.test/guide")),
        retry: new InlineActionBinding(retry ?? (() => { })),
        star: new InlineIconBinding(new object(), false, locale => "Star (" + locale + ")"));

    [Test, DisplayName("typed slots render and project like the string-key overloads")]
    public void BoundMatchesDictionary()
    {
        var renderer = new Rmf2InlineRenderer(MarkupContract);
        HelpSlots slots = Slots();
        var dictionary = new Dictionary<string, InlineMarkupBinding> { ["guide"] = slots.guide, ["retry"] = slots.retry, ["star"] = slots.star };
        LocalizedTextContent content = Content();
        BoundLocalizedTextContent bound = new LocalizedTextContent<HelpSlots>(content).Bind(slots);
        Assert.Equal(Describe(renderer.Render("help", content, dictionary)), Describe(renderer.Render(bound)));
        foreach ((bool actions, bool annotate) in new[] { (true, false), (true, true) })
            Assert.Equal(renderer.ToPlainText("help", content, dictionary, actions, annotate), renderer.ToPlainText(bound, actions, annotate));
        Assert.Equal("See the guide or retry Star (en)", renderer.ToPlainText(bound, allowActionLabels: true));
        Assert.Throws<TranslationFormatException>(() => renderer.ToPlainText(bound), "explicit label-only");
        Assert.Throws<ArgumentNullException>(() => renderer.Render(null!));
        Assert.Throws<ArgumentNullException>(() => renderer.ToPlainText((BoundLocalizedTextContent)null!));
    }

    [Test, DisplayName("typed slots bound values are a snapshot of CopyTo")]
    public void BoundIsSnapshot()
    {
        var slots = new RetainingSlots();
        BoundLocalizedTextContent bound = new LocalizedTextContent<RetainingSlots>(Content()).Bind(slots);
        slots.Retained!.Add("guide", new InlineLinkBinding(new Uri("https://example.test/later")));
        Assert.Equal(0, bound.Slots.Count);
    }

    [Test, DisplayName("typed slots bind once into a reusable read-only ordinal value")]
    public void BindShape()
    {
        var renderer = new Rmf2InlineRenderer(MarkupContract);
        int calls = 0;
        var typed = new LocalizedTextContent<HelpSlots>(Content());
        BoundLocalizedTextContent bound = typed.Bind(Slots(() => calls++));
        Assert.Equal("help", bound.Key);
        Assert.Same(typed.Content, bound.Content);
        Assert.Equal("guide,retry,star", string.Join(",", bound.Slots.Keys.Order(StringComparer.Ordinal)));
        Assert.False(bound.Slots.ContainsKey("GUIDE"), "Slot lookup must be ordinal.");
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, MarkupBinding>)bound.Slots).Add("extra", new InlineLinkBinding(new Uri("https://example.test"))));
        Assert.Equal(renderer.ToPlainText(bound, true), renderer.ToPlainText(bound, true));
        Assert.Equal(0, calls);
        Assert.Throws<ArgumentNullException>(() => typed.Bind(null!));
    }

    [Test, DisplayName("typed slots wrapper forwards content and converts implicitly")]
    public void WrapperShape()
    {
        LocalizedTextContent content = Content();
        var typed = new LocalizedTextContent<HelpSlots>(content);
        LocalizedTextContent untyped = typed;
        Assert.Same(content, untyped);
        Assert.Equal(content.Locale, typed.Locale);
        Assert.Equal(content.Nodes.Length, typed.Nodes.Length);
        Assert.Throws<ArgumentNullException>(() => _ = new LocalizedTextContent<HelpSlots>(null!));
        Assert.Throws<ArgumentNullException>(() => { LocalizedTextContent ignored = (LocalizedTextContent<HelpSlots>)null!; });
    }

    [Test, DisplayName("typed slots hand-wrapped content is validated against the slot contract")]
    public void HandWrapped()
    {
        var renderer = new Rmf2InlineRenderer(MarkupContract);
        // Another message's content that satisfies the help contract (optional retry absent) renders.
        Assert.Equal("See the guide Star (en)",
            renderer.ToPlainText(new LocalizedTextContent<HelpSlots>(Content(includeRetry: false)).Bind(Slots())));
        // Missing required occurrences fail the minimum count.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<HelpSlots>(Content(includeStar: false)).Bind(Slots())), "multiplicity");
        // A slot reference outside the contract is rejected.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<HelpSlots>(Content(guide: "elsewhere")).Bind(Slots())), "Invalid functional slot");
        // A slot type for a key that this renderer's catalog does not contain is rejected.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<UnknownSlots>(Content()).Bind(new UnknownSlots())), "Unknown RMF2 message contract");
    }

    [Test, DisplayName("typed slots keep every runtime binding rule")]
    public void RuntimeRules()
    {
        var renderer = new Rmf2InlineRenderer(MarkupContract);
        var typed = new LocalizedTextContent<HelpSlots>(Content());
        HelpSlots valid = Slots();
        // A future non-inline binding in an inline slot is an incompatible binding.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<EmbedLikeSlots>(Content()).Bind(new EmbedLikeSlots(valid))), "incompatible binding for slot 'guide'");
        // Link scheme.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(typed.Bind(new HelpSlots(new InlineLinkBinding(new Uri("javascript:alert(1)")), valid.retry, valid.star))), "slot 'guide'");
        // Icon alternate text.
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(typed.Bind(new HelpSlots(valid.guide, valid.retry, new InlineIconBinding(new object(), false)))), "alternate text");
        // Multiplicity: a second occurrence of a max:1 slot.
        var twice = new CompiledRmf2Message([], [], [], [new([], [
            Functional("runic:link", "guide", "open"), new("a"), Functional("runic:link", "guide", "close"),
            Functional("runic:link", "guide", "open"), new("b"), Functional("runic:link", "guide", "close"),
            Functional("runic:icon", "star", "standalone")])], "en");
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<HelpSlots>(twice.FormatContent([], "en")).Bind(valid)), "multiplicity");
        // Nesting: an interactive element inside another.
        var nested = new CompiledRmf2Message([], [], [], [new([], [
            Functional("runic:link", "guide", "open"), Functional("runic:action", "retry", "open"), new("x"),
            Functional("runic:action", "retry", "close"), Functional("runic:link", "guide", "close"),
            Functional("runic:icon", "star", "standalone")])], "en");
        Assert.Throws<TranslationFormatException>(() =>
            renderer.Render(new LocalizedTextContent<HelpSlots>(nested.FormatContent([], "en")).Bind(valid)));
    }

    private static string Describe(IReadOnlyList<InlineMarkupRun> runs)
    {
        var text = new StringBuilder();
        foreach (InlineMarkupRun run in runs)
        {
            text.Append('[').Append(run.Name).Append('|').Append(run.Text).Append('|').Append(run.Standalone).Append('|')
                .Append(run.Binding is null ? "-" : run.Binding.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
                .Append(string.Join(",", run.Options.Select(option => option.Key + "=" + option.Value))).Append(Describe(run.Children)).Append(']');
        }
        return text.ToString();
    }
}

/// <summary>Mirrors the generated slot type shape: sealed class, explicit interface members, null-checked constructor.</summary>
internal sealed class HelpSlots : IRmf2SlotBindings<HelpSlots>
{
    public HelpSlots(InlineLinkBinding guide, InlineActionBinding retry, InlineIconBinding star)
    {
        this.guide = guide ?? throw new ArgumentNullException(nameof(guide));
        this.retry = retry ?? throw new ArgumentNullException(nameof(retry));
        this.star = star ?? throw new ArgumentNullException(nameof(star));
    }
#pragma warning disable IDE1006 // Generated slot properties keep the RMF2 slot ID verbatim.
    public InlineLinkBinding guide { get; }
    public InlineActionBinding retry { get; }
    public InlineIconBinding star { get; }
#pragma warning restore IDE1006
    static string IRmf2SlotBindings<HelpSlots>.MessageKey => "help";
    void IRmf2SlotBindings<HelpSlots>.CopyTo(IDictionary<string, MarkupBinding> destination)
    { destination.Add("guide", this.guide); destination.Add("retry", this.retry); destination.Add("star", this.star); }
}

internal sealed class UnknownSlots : IRmf2SlotBindings<UnknownSlots>
{
    static string IRmf2SlotBindings<UnknownSlots>.MessageKey => "missing";
    void IRmf2SlotBindings<UnknownSlots>.CopyTo(IDictionary<string, MarkupBinding> destination) { }
}

/// <summary>Stands in for a future block-level binding (W220-004's EmbedBinding).</summary>
internal sealed record EmbedLikeBinding(string Source) : MarkupBinding;

internal sealed class EmbedLikeSlots(HelpSlots inner) : IRmf2SlotBindings<EmbedLikeSlots>
{
    static string IRmf2SlotBindings<EmbedLikeSlots>.MessageKey => "help";
    void IRmf2SlotBindings<EmbedLikeSlots>.CopyTo(IDictionary<string, MarkupBinding> destination)
    { destination.Add("guide", new EmbedLikeBinding("figure.png")); destination.Add("retry", inner.retry); destination.Add("star", inner.star); }
}

/// <summary>Hand-written slot type that keeps the CopyTo destination and mutates it later.</summary>
internal sealed class RetainingSlots : IRmf2SlotBindings<RetainingSlots>
{
    public IDictionary<string, MarkupBinding>? Retained { get; private set; }
    public static string MessageKey => "help";
    public void CopyTo(IDictionary<string, MarkupBinding> destination) => Retained = destination;
}
