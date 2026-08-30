using System;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.Tooling;
using Runic.Translations.Compiler;
using Runic.Translations;

namespace Runic.Translations.AotTests;

internal static class Program
{
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static async Task<int> Main()
    {
        try
        {
            CompiledTranslationCatalog catalog = CreateCatalog();
            var greeting = new TranslationKey("app", 0, "greeting");
            var title = new TranslationKey("app", 1, "title");
            byte[] bytes = CreatePackBytes();
            var externalFactory = new ExternalTranslationSnapshotFactory(
                new MemoryPackSource(bytes),
                "app",
                Fingerprint,
                CreatePackContract,
                integrityVerifier: VerifyIntegrity);
            var provider = new CompiledTranslationProvider(
                catalog,
                snapshotFactory: externalFactory);
            ITranslationSnapshot initial = new CompiledTranslationSnapshot(catalog, "en");
            var manager = new TranslationManager(provider, initial);

            Require(manager.Current.Format(greeting, [new TextArgument("name", "Ada")]) == "Hello Ada", "format");
            var reference = new TranslationReference("app", Fingerprint, "greeting",
                new Dictionary<string, TranslationReferenceArgument> { ["name"] = new(TextArgumentType.String, "Ada") });
            string referenceJson = JsonSerializer.Serialize(reference, TranslationReferenceJsonContext.Default.TranslationReference);
            Require(referenceJson.Contains("\"arguments\":{\"name\":\"Ada\"}", StringComparison.Ordinal), "text-reference JSON");
            TranslationCompilation interchangeCompilation = TranslationsTooling.Compile(
                [new TranslationSource("app.catalog.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"code\":{\"namespace\":\"App\",\"className\":\"Text\"},\"defaultLocale\":\"en\",\"locales\":[{\"tag\":\"en\"},{\"tag\":\"de\",\"fallback\":\"en\"}],\"layers\":[{\"name\":\"base\",\"priority\":0}]}"))],
                [new TranslationSource("app.en.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"en\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hello\"}}")), new TranslationSource("app.de.json", Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"catalog\":\"app\",\"locale\":\"de\",\"layer\":\"base\",\"resources\":{\"Hello\":\"Hallo\"}}"))]);
            TranslationXliffExportResult interchange = TranslationInterchange.ExportXliff21(interchangeCompilation);
            TranslationXliffImportResult imported = TranslationInterchange.ImportXliff21(interchange.Documents[0].Bytes);
            Require(imported.TargetLocale == "de" && imported.ResourceDocumentBytes.Length > 0, "Native-AOT XLIFF interchange");

            Task[] swaps = new Task[16];
            for (int index = 0; index < swaps.Length; index++)
            {
                swaps[index] = manager.SetLocaleAsync("de").AsTask();
            }
            await Task.WhenAll(swaps).ConfigureAwait(false);
            Require(manager.CurrentLocale == "de", "concurrent swap");
            Require(manager.Current.Get(title) == "Application", "fallback");
            Require(manager.Current.Format(greeting, [new TextArgument("name", "Ada")]) == "Extern Ada",
                "external pack composition");

            Console.WriteLine("PASS: Native-AOT compiled snapshot/format/fallback/swap/external-pack/transport/XLIFF smoke");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static ValueTask<bool> VerifyIntegrity(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(content.Length > 0);
    }

    private static TranslationPackContract CreatePackContract(string locale) => new(
        "app",
        locale,
        Fingerprint,
        [
            new TranslationPackMessageContract(
                new TranslationKey("app", 0, "greeting"),
                [new TranslationPackArgumentContract("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new TranslationPackMessageContract(
                new TranslationKey("app", 1, "title")),
        ]);

    private static byte[] CreatePackBytes() => Encoding.UTF8.GetBytes(
        "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"de\"," +
        "\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{" +
        "\"pattern\":\"Extern {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");

    private static CompiledTranslationCatalog CreateCatalog() => new(
        "app",
        "en",
        [
            new CompiledTranslationDefinition("greeting",
                [new TranslationPlaceholderDescriptor("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTranslationDefinition("title", Array.Empty<TranslationPlaceholderDescriptor>()),
        ],
        [
            new CompiledTranslationLocale("de", "en", [new CompiledTranslationValue(0, "Hallo {name}")]),
            new CompiledTranslationLocale("en", null,
                [new CompiledTranslationValue(0, "Hello {name}"), new CompiledTranslationValue(1, "Application")]),
        ]);

    private static void Require(bool condition, string operation)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Native-AOT smoke failed at " + operation + ".");
        }
    }

    private sealed class MemoryPackSource(byte[] bytes) : IExternalTranslationSource
    {
        public ValueTask<ExternalTranslationPack?> LoadAsync(
            string catalog,
            string locale,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExternalTranslationPack? pack = catalog == "app" && locale == "de"
                ? new ExternalTranslationPack(bytes)
                : null;
            return ValueTask.FromResult(pack);
        }
    }

}
