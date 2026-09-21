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
            TranslationPackContract contract = new("app", "de", Fingerprint,
                [new TranslationPackMessageContract(key)], messageGrammarVersion: 4, rmf2MarkupContract: MarkupContract);
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
            Console.WriteLine("PASS Native-AOT RMF2 artifact-v4 structured renderer/annotation/plain-text smoke");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static string CreateArtifact() =>
        "{\"artifactVersion\":4,\"messageGrammarVersion\":4,\"catalog\":\"app\",\"locale\":\"de\",\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{\"astVersion\":4,\"contentLocale\":\"de\",\"inputs\":{},\"selectors\":[],\"variants\":[{\"matches\":{},\"nodes\":[" +
        "{\"kind\":\"markup\",\"name\":\"shop:children\",\"attributes\":{},\"standalone\":false,\"annotations\":{},\"variableOptions\":[],\"children\":[{\"kind\":\"text\",\"value\":\"Keep\"}]}," +
        "{\"kind\":\"markup\",\"name\":\"shop:omit\",\"attributes\":{},\"standalone\":false,\"annotations\":{},\"variableOptions\":[],\"children\":[{\"kind\":\"text\",\"value\":\"Drop\"}]}," +
        "{\"kind\":\"markup\",\"name\":\"shop:break\",\"attributes\":{},\"standalone\":true,\"annotations\":{},\"variableOptions\":[],\"children\":[]}," +
        "{\"kind\":\"markup\",\"name\":\"runic:strong\",\"attributes\":{},\"standalone\":false,\"annotations\":{\"note\":\"internal\"},\"variableOptions\":[],\"children\":[{\"kind\":\"text\",\"value\":\"Extern\"}]}] }]}},\"markupContract\":" + MarkupContract + "}";

    private static void Require(bool condition, string operation)
    {
        if (!condition) throw new InvalidOperationException("Native-AOT RMF2 smoke failed at " + operation + ".");
    }
}
