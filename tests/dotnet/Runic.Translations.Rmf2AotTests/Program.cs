using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Runic.Translations;

namespace Runic.Translations.Rmf2AotTests;

internal static class Program
{
    private const string Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000";
    private const string MarkupContract = "{\"version\":2,\"contracts\":{\"runic:li\":{\"kind\":\"paired\",\"placement\":\"list-item\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"runic:p\":{\"kind\":\"paired\",\"placement\":\"block\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"runic:strong\":{\"kind\":\"paired\",\"placement\":\"inline\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"shop:break\":{\"kind\":\"standalone\",\"placement\":\"inline\",\"children\":\"none\",\"interactive\":false,\"plainText\":\"lineBreak\",\"options\":{}},\"shop:children\":{\"kind\":\"paired\",\"placement\":\"inline\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"shop:omit\":{\"kind\":\"paired\",\"placement\":\"inline\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"omit\",\"options\":{}},\"runic:ul\":{\"kind\":\"paired\",\"placement\":\"block\",\"children\":\"list-items\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}}},\"messages\":{\"greeting\":{\"slots\":{},\"structured\":true,\"contentLocales\":{\"de\":\"de\"},\"content\":\"inline\",\"skeletons\":[]},\"notice\":{\"slots\":{},\"structured\":true,\"contentLocales\":{\"de\":\"de\"},\"content\":\"document\",\"skeletons\":[\"p,ul(li)\"]}}}";

    public static async Task<int> Main()
    {
        try
        {
            var key = new TranslationKey("app", 0, "greeting");
            var noticeKey = new TranslationKey("app", 1, "notice");
            TranslationPackContract contract = TranslationPackContract.CreateRmf2V5("app", "de", Fingerprint,
                [new TranslationPackMessageContract(key), new TranslationPackMessageContract(noticeKey)], MarkupContract);
            VerifiedExternalTranslationPack verified = await TranslationPackLoader.VerifyAsync(
                new ExternalTranslationPack(Encoding.UTF8.GetBytes(CreateArtifact())), contract).ConfigureAwait(false);
            CompiledTextMessage message = verified.Messages.Single(item => item.Key.Name == "greeting").Message!;
            CompiledTextMessage notice = verified.Messages.Single(item => item.Key.Name == "notice").Message!;
            var catalog = new CompiledTranslationCatalog("app", "de",
                [new CompiledTranslationDefinition("greeting", Array.Empty<TranslationPlaceholderDescriptor>()), new CompiledTranslationDefinition("notice", Array.Empty<TranslationPlaceholderDescriptor>())],
                [new CompiledTranslationLocale("de", null, [new CompiledTranslationValue(0, "", message), new CompiledTranslationValue(1, "", notice)])]);
            LocalizedTextContent content = new CompiledTranslationSnapshot(catalog, "de").FormatContent(key, Array.Empty<TextArgument>());
            Rmf2InlineRenderer renderer = new(MarkupContract);

            IReadOnlyList<InlineMarkupRun> runs = renderer.Render("greeting", content);
            InlineMarkupRun strong = runs.Single(run => run.Name == "runic:strong");
            Require(!strong.Options.ContainsKey("@note"), "annotation visibility");
            Require(renderer.ToPlainText("greeting", content) == "Keep\nExtern", "policy projection");
            BoundLocalizedTextContent bound = new LocalizedTextContent<GreetingSlots>(content).Bind(new GreetingSlots());
            Require(bound.Key == "greeting" && bound.Slots.Count == 0, "typed slot static key dispatch");
            Require(renderer.Render(bound).Count == runs.Count, "typed bound render");
            Require(renderer.ToPlainText(bound) == "Keep\nExtern", "typed bound projection");
            var documents = new Rmf2DocumentRenderer(Rmf2MarkupContract.Link(MarkupContract));
            var document = new LocalizedDocumentContent(new CompiledTranslationSnapshot(catalog, "de").FormatContent(noticeKey, Array.Empty<TextArgument>()));
            IReadOnlyList<DocumentBlock> blocks = documents.Render(new LocalizedDocumentContent<NoticeSlots>(document).Bind(new NoticeSlots()));
            Require(blocks.Count == 2 && blocks[1].Blocks.Single().Occurrence == "ul[1]/li[1]", "document blocks");
            Require(documents.ToPlainText("notice", document, options: new Rmf2PlainTextOptions { ListMarker = "* " }) == "Hallo\n\n* Eins", "document projection");
            Console.WriteLine("PASS Native-AOT RMF2 artifact-v5 structured renderer/annotation/plain-text/typed-slot/document smoke");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string CreateArtifact() =>
        "{\"artifactVersion\":5,\"messageGrammarVersion\":5,\"profile\":\"rmf2-execution-v2\",\"catalog\":\"app\",\"locale\":\"de\",\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{\"contentLocale\":\"de\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[" +
        "{\"kind\":\"markup\",\"name\":\"shop:children\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[]},{\"kind\":\"text\",\"value\":\"Keep\"},{\"kind\":\"markup\",\"name\":\"shop:children\",\"markupKind\":\"close\",\"options\":[],\"annotations\":[]}," +
        "{\"kind\":\"markup\",\"name\":\"shop:omit\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[]},{\"kind\":\"text\",\"value\":\"Drop\"},{\"kind\":\"markup\",\"name\":\"shop:omit\",\"markupKind\":\"close\",\"options\":[],\"annotations\":[]}," +
        "{\"kind\":\"markup\",\"name\":\"shop:break\",\"markupKind\":\"standalone\",\"options\":[],\"annotations\":[]}," +
        "{\"kind\":\"markup\",\"name\":\"runic:strong\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[{\"name\":\"note\",\"value\":{\"kind\":\"string-literal\",\"value\":\"internal\"}}]},{\"kind\":\"text\",\"value\":\"Extern\"},{\"kind\":\"markup\",\"name\":\"runic:strong\",\"markupKind\":\"close\",\"options\":[],\"annotations\":[]}] }]}}," +
        "\"notice\":{\"contentLocale\":\"de\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[" +
        Tag("runic:p", "open") + ",{\"kind\":\"text\",\"value\":\"Hallo\"}," + Tag("runic:p", "close") + "," + Tag("runic:ul", "open") + "," + Tag("runic:li", "open") +
        ",{\"kind\":\"text\",\"value\":\"Eins\"}," + Tag("runic:li", "close") + "," + Tag("runic:ul", "close") + "]}]}}},\"markupContract\":" + MarkupContract + "}";

    private static string Tag(string name, string kind) =>
        "{\"kind\":\"markup\",\"name\":\"" + name + "\",\"markupKind\":\"" + kind + "\",\"options\":[],\"annotations\":[]}";

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException("Native-AOT RMF2 smoke failed at " + operation + ".");
    }
}

/// <summary>Mirrors the generated slot type shape for a document message without slots.</summary>
internal sealed class NoticeSlots : IRmf2SlotBindings<NoticeSlots>
{
    static string IRmf2SlotBindings<NoticeSlots>.MessageKey => "notice";
    void IRmf2SlotBindings<NoticeSlots>.CopyTo(IDictionary<string, MarkupBinding> destination) { }
}

/// <summary>Mirrors the generated slot type shape for a structured message without slots.</summary>
internal sealed class GreetingSlots : IRmf2SlotBindings<GreetingSlots>
{
    static string IRmf2SlotBindings<GreetingSlots>.MessageKey => "greeting";
    void IRmf2SlotBindings<GreetingSlots>.CopyTo(IDictionary<string, MarkupBinding> destination) { }
}
