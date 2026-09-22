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
    private const string MarkupContract = "{\"version\":1,\"contracts\":{\"runic:strong\":{\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"shop:break\":{\"kind\":\"standalone\",\"children\":\"none\",\"interactive\":false,\"plainText\":\"lineBreak\",\"options\":{}},\"shop:children\":{\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"children\",\"options\":{}},\"shop:omit\":{\"kind\":\"paired\",\"children\":\"inline\",\"interactive\":false,\"plainText\":\"omit\",\"options\":{}}},\"messages\":{\"greeting\":{\"slots\":{},\"structured\":true,\"contentLocales\":{\"de\":\"de\"}}}}";

    public static async Task<int> Main()
    {
        try
        {
            var key = new TranslationKey("app", 0, "greeting");
            TranslationPackContract contract = TranslationPackContract.CreateRmf2V5("app", "de", Fingerprint,
                [new TranslationPackMessageContract(key)], MarkupContract);
            VerifiedExternalTranslationPack verified = await TranslationPackLoader.VerifyAsync(
                new ExternalTranslationPack(Encoding.UTF8.GetBytes(CreateArtifact())), contract).ConfigureAwait(false);
            CompiledTextMessage message = verified.Messages.Single().Message!;
            var catalog = new CompiledTranslationCatalog("app", "de",
                [new CompiledTranslationDefinition("greeting", Array.Empty<TranslationPlaceholderDescriptor>())],
                [new CompiledTranslationLocale("de", null, [new CompiledTranslationValue(0, "", message)])]);
            LocalizedTextContent content = new CompiledTranslationSnapshot(catalog, "de").FormatContent(key, Array.Empty<TextArgument>());
            Rmf2InlineRenderer renderer = new(MarkupContract);

            IReadOnlyList<InlineMarkupRun> runs = renderer.Render("greeting", content);
            InlineMarkupRun strong = runs.Single(run => run.Name == "runic:strong");
            Require(!strong.Options.ContainsKey("@note"), "annotation visibility");
            Require(renderer.ToPlainText("greeting", content) == "Keep\nExtern", "policy projection");
            Console.WriteLine("PASS Native-AOT RMF2 artifact-v5 structured renderer/annotation/plain-text smoke");
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
        "{\"kind\":\"markup\",\"name\":\"runic:strong\",\"markupKind\":\"open\",\"options\":[],\"annotations\":[{\"name\":\"note\",\"value\":{\"kind\":\"string-literal\",\"value\":\"internal\"}}]},{\"kind\":\"text\",\"value\":\"Extern\"},{\"kind\":\"markup\",\"name\":\"runic:strong\",\"markupKind\":\"close\",\"options\":[],\"annotations\":[]}] }]}}},\"markupContract\":" + MarkupContract + "}";

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException("Native-AOT RMF2 smoke failed at " + operation + ".");
    }
}
