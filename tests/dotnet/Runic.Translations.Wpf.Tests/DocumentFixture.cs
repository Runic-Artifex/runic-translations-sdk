using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using Runic.Translations;

/// <summary>
/// Document messages built like compiler output: the bounded #15 example ("backup") and a heading with lists ("lists").
/// </summary>
internal static class DocumentFixture
{
    internal static TranslationKey BackupKey { get; } = new("guide", 0, "backup");
    internal static TranslationKey ListsKey { get; } = new("guide", 1, "lists");

    internal static string MarkupContract { get; } = CreateContract();

    private static string CreateContract()
    {
        JsonObject Tag(string kind, string placement, string children, bool interactive, string plainText, JsonObject? options = null) => new()
        {
            ["kind"] = kind, ["placement"] = placement, ["children"] = children, ["interactive"] = interactive,
            ["plainText"] = plainText, ["options"] = options ?? new JsonObject(),
        };
        JsonObject Integer(int minimum, int maximum, string? fallback) => new()
        {
            ["type"] = "integer", ["values"] = new JsonArray(), ["default"] = fallback, ["literalOnly"] = true, ["minimum"] = minimum, ["maximum"] = maximum,
        };
        var contract = new JsonObject
        {
            ["version"] = 2,
            ["contracts"] = new JsonObject
            {
                ["runic:action"] = Tag("paired", "inline", "inline", true, "explicit"),
                ["runic:br"] = Tag("standalone", "inline", "none", false, "lineBreak"),
                ["runic:em"] = Tag("paired", "inline", "inline", false, "children"),
                ["runic:h"] = Tag("paired", "block", "inline", false, "children", new JsonObject { ["level"] = Integer(1, 6, null) }),
                ["runic:icon"] = Tag("standalone", "inline", "none", false, "alternateText"),
                ["runic:li"] = Tag("paired", "list-item", "inline", false, "children"),
                ["runic:link"] = Tag("paired", "inline", "inline", true, "children"),
                ["runic:ol"] = Tag("paired", "block", "list-items", false, "children", new JsonObject
                {
                    ["marker"] = new JsonObject
                    {
                        ["type"] = "enum", ["values"] = new JsonArray("decimal", "lower-alpha", "upper-alpha", "lower-roman", "upper-roman"),
                        ["default"] = "decimal", ["literalOnly"] = true,
                    },
                    ["start"] = Integer(1, int.MaxValue, "1"),
                }),
                ["runic:p"] = Tag("paired", "block", "inline", false, "children"),
                ["runic:strong"] = Tag("paired", "inline", "inline", false, "children"),
                ["runic:ul"] = Tag("paired", "block", "list-items", false, "children"),
            },
            ["messages"] = new JsonObject
            {
                ["backup"] = new JsonObject
                {
                    ["slots"] = new JsonObject
                    {
                        ["check"] = new JsonObject { ["kind"] = "runic:action", ["min"] = 1, ["max"] = 1 },
                        ["guide"] = new JsonObject { ["kind"] = "runic:link", ["min"] = 1, ["max"] = 1 },
                    },
                    ["structured"] = true, ["content"] = "document", ["skeletons"] = new JsonArray("p,ul(li,li),p"),
                    ["contentLocales"] = new JsonObject { ["en"] = "en" },
                },
                ["lists"] = new JsonObject
                {
                    ["slots"] = new JsonObject { ["star"] = new JsonObject { ["kind"] = "runic:icon", ["min"] = 1, ["max"] = 1 } },
                    ["structured"] = true, ["content"] = "document",
                    ["skeletons"] = new JsonArray("h[level=1],ol[marker=lower-alpha;start=25](li,li,li),ul(li,li)"),
                    ["contentLocales"] = new JsonObject { ["en"] = "en" },
                },
            },
        };
        return contract.ToJsonString();
    }

    // The interactive sample also builds the same English messages under a right-to-left locale ("he") to check mirroring.
    internal static CompiledTranslationSnapshot CreateSnapshot(string locale = "en")
    {
        CompiledRmf2Input[] backupInputs = [new("fileName", TextArgumentType.String)];
        var backup = new CompiledRmf2Message(backupInputs,
            [new("input", "fileName", new(new("input", "fileName"), TextArgumentType.String, "string"))], [],
            [new([], Backup())], locale);
        var lists = new CompiledRmf2Message([], [], [], [new([], Lists())], locale);
        var catalog = new CompiledTranslationCatalog("guide", locale,
            [CompiledTranslationDefinition.FromRmf2Inputs("backup", backupInputs), CompiledTranslationDefinition.FromRmf2Inputs("lists", [])],
            [new CompiledTranslationLocale(locale, null,
                [new CompiledTranslationValue(0, "", CompiledTextMessage.FromRmf2(backup)), new CompiledTranslationValue(1, "", CompiledTextMessage.FromRmf2(lists))])]);
        return new CompiledTranslationSnapshot(catalog, locale);
    }

    internal static LocalizedDocumentContent Backup(CompiledTranslationSnapshot snapshot, string fileName) =>
        new(snapshot.FormatContent(BackupKey, [new TextArgument("fileName", fileName)]));

    internal static LocalizedDocumentContent Lists(CompiledTranslationSnapshot snapshot) =>
        new(snapshot.FormatContent(ListsKey, Array.Empty<TextArgument>()));

    // {#p}{#strong}Before continuing{/strong}, save a copy of {$fileName}.{/p}
    // {#ul}{#li}Read the {#link ref=guide}guide{/link}.{/li}{#li}{#action ref=check}Check{/action} the result.{/li}{/ul}
    // {#p}You can continue when the check finishes.{/p}
    private static CompiledRmf2Node[] Backup() =>
    [
        Open("runic:p"), Open("runic:strong"), new("Before continuing"), Close("runic:strong"), new(", save a copy of "),
        new(new CompiledRmf2Expression(new("input", "fileName"), TextArgumentType.String, "string")), new("."), Close("runic:p"),
        Open("runic:ul"),
        Open("runic:li"), new("Read the "), Slot("runic:link", "guide"), new("guide"), Close("runic:link"), new("."), Close("runic:li"),
        Open("runic:li"), Slot("runic:action", "check"), new("Check"), Close("runic:action"), new(" the result."), Close("runic:li"),
        Close("runic:ul"),
        Open("runic:p"), new("You can continue when the check finishes."), Close("runic:p"),
    ];

    // {#h level=1}Appendix{/h}
    // {#ol start=25 marker=lower-alpha}{#li}Twenty-five{/li}{#li}Twenty-six{/li}{#li}Twenty-seven{/li}{/ol}
    // {#ul}{#li}First line{#br/}second line{/li}{#li}{#em}Only{/em} {#icon ref=star/}{/li}{/ul}
    private static CompiledRmf2Node[] Lists() =>
    [
        Open("runic:h", ("level", "1")), new("Appendix"), Close("runic:h"),
        Open("runic:ol", ("start", "25"), ("marker", "lower-alpha")),
        Open("runic:li"), new("Twenty-five"), Close("runic:li"), Open("runic:li"), new("Twenty-six"), Close("runic:li"),
        Open("runic:li"), new("Twenty-seven"), Close("runic:li"),
        Close("runic:ol"),
        Open("runic:ul"),
        Open("runic:li"), new("First line"), new CompiledRmf2Node("runic:br", "standalone"), new("second line"), Close("runic:li"),
        Open("runic:li"), Open("runic:em"), new("Only"), Close("runic:em"), new(" "),
        new CompiledRmf2Node("runic:icon", "standalone", [new("ref", new("string-literal", "star"))]), Close("runic:li"),
        Close("runic:ul"),
    ];

    private static CompiledRmf2Node Open(string name, params (string Name, string Value)[] options)
    {
        var compiled = new List<CompiledRmf2Option>();
        foreach ((string option, string value) in options) compiled.Add(new(option, new("string-literal", value)));
        return new CompiledRmf2Node(name, "open", compiled);
    }

    private static CompiledRmf2Node Close(string name) => new(name, "close");
    private static CompiledRmf2Node Slot(string name, string slot) => new(name, "open", [new("ref", new("string-literal", slot))]);

    internal static InlineIconBinding Star(string label = "Star") =>
        new((Func<FrameworkElement>)(() => new TextBlock { Text = "★" }), false, _ => label);
}

/// <summary>Mirrors the generated slot type shape for the backup message.</summary>
internal sealed class BackupSlots(InlineActionBinding check, InlineLinkBinding guide) : IRmf2SlotBindings<BackupSlots>
{
    public InlineActionBinding Check { get; } = check ?? throw new ArgumentNullException(nameof(check));
    public InlineLinkBinding Guide { get; } = guide ?? throw new ArgumentNullException(nameof(guide));
    static string IRmf2SlotBindings<BackupSlots>.MessageKey => "backup";
    void IRmf2SlotBindings<BackupSlots>.CopyTo(IDictionary<string, MarkupBinding> destination)
    { destination.Add("check", Check); destination.Add("guide", Guide); }
}
