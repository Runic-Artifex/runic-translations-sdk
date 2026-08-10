using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RunicTranslations.Runtime.Tests;

internal static class CompiledMessageTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("snapshot executes generated message AST without parsing compatibility patterns", DirectAst);
        runner.Add("compiled AST executes multiple selectors formats relative time and safe markup", StructuredAst);
        runner.Add("plural selector matches the shared v2 cross-runtime corpus", PluralCorpus);
        runner.Add("relative-time formatter matches the shared v2 cross-runtime corpus", RelativeTimeCorpus);
    }

    private static void DirectAst()
    {
        var message = new CompiledTextMessage(
            Array.Empty<CompiledTextMessageNode>(),
            [new CompiledTextMessageSelector("quantity", "count", CompiledTextMessageSelectorKind.CardinalPlural)],
            [
                new CompiledTextMessageVariant(
                    ["one"],
                    [new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, "One file")]),
                new CompiledTextMessageVariant(
                    ["*"],
                    [
                        new CompiledTextMessageNode(CompiledTextMessageNodeKind.Input, "count"),
                        new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, " files"),
                    ]),
            ]);
        var catalog = new CompiledTranslationCatalog(
            "ast",
            "en",
            [new CompiledTranslationDefinition("Files", [new TranslationPlaceholderDescriptor("count", TextArgumentType.Int, TextArgumentFormat.Plain)])],
            [new CompiledTranslationLocale("en", null, [new CompiledTranslationValue(0, "deliberately { malformed", message)])]);
        var snapshot = new CompiledTranslationSnapshot(catalog, "en");
        var key = new TranslationKey("ast", 0, "Files");
        Assert.Equal("One file", snapshot.Format(key, [new TextArgument("count", 1)]));
        Assert.Equal("3 files", snapshot.Format(key, [new TextArgument("count", 3)]));
    }

    private static void StructuredAst()
    {
        var message = new CompiledTextMessage(
            Array.Empty<CompiledTextMessageNode>(),
            [
                new CompiledTextMessageSelector("quantity", "count", CompiledTextMessageSelectorKind.CardinalPlural),
                new CompiledTextMessageSelector("ownerKind", "owner", CompiledTextMessageSelectorKind.Literal),
            ],
            [
                new CompiledTextMessageVariant(["one", "admin"], [
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, "Exactly "),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Format, "count", TextArgumentFormat.Grouped),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Input, "delta"),
                ]),
                new CompiledTextMessageVariant(["*", "*"], [
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.MarkupStart, "strong", attributes:
                        [new CompiledTextMarkupProperty("tone", "critical")]),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Format, "count", TextArgumentFormat.Grouped),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, " items for "),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Input, "owner"),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.MarkupEnd, "strong"),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.Text, ", "),
                    new CompiledTextMessageNode(CompiledTextMessageNodeKind.RelativeTime, "delta", TextArgumentFormat.Plain, "day", "auto"),
                ]),
            ]);
        var catalog = new CompiledTranslationCatalog("ast", "en",
            [new CompiledTranslationDefinition("Summary", [
                new TranslationPlaceholderDescriptor("count", TextArgumentType.Int, TextArgumentFormat.Plain),
                new TranslationPlaceholderDescriptor("delta", TextArgumentType.Number, TextArgumentFormat.Plain),
                new TranslationPlaceholderDescriptor("owner", TextArgumentType.String, TextArgumentFormat.None),
            ])], [new CompiledTranslationLocale("en", null, [new CompiledTranslationValue(0, "compatibility", message)])]);
        var snapshot = new CompiledTranslationSnapshot(catalog, "en");
        LocalizedTextContent content = snapshot.FormatContent(new TranslationKey("ast", 0, "Summary"),
            [new TextArgument("count", 1234), new TextArgument("delta", -1m), new TextArgument("owner", "guest")]);
        LocalizedTextContentNode[] nodes = content.Nodes.ToArray();
        Assert.Equal(LocalizedTextContentNodeKind.ElementStart, nodes[0].Kind);
        Assert.Equal("strong", nodes[0].Value);
        Assert.Equal("tone", nodes[0].Attributes.Span[0].Name);
        Assert.Equal("1,234", nodes[1].Value);
        Assert.Equal("guest", nodes[3].Value);
        Assert.Equal(LocalizedTextContentNodeKind.ElementEnd, nodes[4].Kind);
        Assert.Equal("yesterday", nodes[6].Value);
        Assert.Throws<TranslationFormatException>(() => snapshot.Format(new TranslationKey("ast", 0, "Summary"),
            [new TextArgument("count", 2), new TextArgument("delta", -1m), new TextArgument("owner", "guest")]));
    }

    private static void PluralCorpus()
    {
        string path = Path.Combine(FindRepositoryRoot(), "spec", "corpus", "v2-plural-conformance.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (JsonElement item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string locale = item.GetProperty("locale").GetString()!;
            bool ordinal = item.GetProperty("ordinal").GetBoolean();
            decimal value = decimal.Parse(item.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            string expected = item.GetProperty("expected").GetString()!;
            Assert.Equal(expected, TextMessageSelector.SelectPlural(value, locale, ordinal), locale + " " + value);
        }
    }

    private static void RelativeTimeCorpus()
    {
        string path = Path.Combine(FindRepositoryRoot(), "spec", "corpus", "v2-relative-time-conformance.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(path));
        Assert.Equal("48.2", document.RootElement.GetProperty("cldrVersion").GetString());
        foreach (JsonElement item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            string locale = item.GetProperty("locale").GetString()!;
            decimal value = decimal.Parse(item.GetProperty("value").GetString()!, CultureInfo.InvariantCulture);
            string unit = item.GetProperty("unit").GetString()!;
            string numeric = item.GetProperty("numeric").GetString()!;
            string expected = item.GetProperty("expected").GetString()!;
            Assert.Equal(expected, TextRelativeTimeFormatter.Format(value, unit, numeric, locale), locale + " " + value + " " + unit);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RunicTranslations.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate repository root.");
    }
}
