using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RunicTextResources;

namespace RunicTextResources.AotTests;

internal static class Program
{
    private const string Fingerprint = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static async Task<int> Main()
    {
        try
        {
            CompiledTextResourceCatalog catalog = CreateCatalog();
            var greeting = new TextResourceKey("app", 0, "greeting");
            var title = new TextResourceKey("app", 1, "title");
            byte[] bytes = CreatePackBytes();
            var externalFactory = new ExternalTextResourceSnapshotFactory(
                new MemoryPackSource(bytes),
                "app",
                Fingerprint,
                CreatePackContract,
                integrityVerifier: VerifyIntegrity);
            var provider = new CompiledTextResourceProvider(
                catalog,
                snapshotFactory: externalFactory);
            ITextResourceSnapshot initial = new CompiledTextResourceSnapshot(catalog, "en");
            var manager = new TextResourceManager(provider, initial);

            Require(manager.Current.Format(greeting, [new TextArgument("name", "Ada")]) == "Hello Ada", "format");

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

            Console.WriteLine("PASS: Native-AOT compiled snapshot/format/fallback/swap/external-pack smoke");
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

    private static TextResourcePackContract CreatePackContract(string locale) => new(
        "app",
        locale,
        Fingerprint,
        [
            new TextResourcePackMessageContract(
                new TextResourceKey("app", 0, "greeting"),
                [new TextResourcePackArgumentContract("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new TextResourcePackMessageContract(
                new TextResourceKey("app", 1, "title")),
        ]);

    private static byte[] CreatePackBytes() => Encoding.UTF8.GetBytes(
        "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"de\"," +
        "\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{\"greeting\":{" +
        "\"pattern\":\"Extern {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}}}");

    private static CompiledTextResourceCatalog CreateCatalog() => new(
        "app",
        "en",
        [
            new CompiledTextResourceDefinition("greeting",
                [new TextResourcePlaceholderDescriptor("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new CompiledTextResourceDefinition("title", Array.Empty<TextResourcePlaceholderDescriptor>()),
        ],
        [
            new CompiledTextResourceLocale("de", "en", [new CompiledTextResourceValue(0, "Hallo {name}")]),
            new CompiledTextResourceLocale("en", null,
                [new CompiledTextResourceValue(0, "Hello {name}"), new CompiledTextResourceValue(1, "Application")]),
        ]);

    private static void Require(bool condition, string operation)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Native-AOT smoke failed at " + operation + ".");
        }
    }

    private sealed class MemoryPackSource(byte[] bytes) : IExternalTextResourceSource
    {
        public ValueTask<ExternalTextResourcePack?> LoadAsync(
            string catalog,
            string locale,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExternalTextResourcePack? pack = catalog == "app" && locale == "de"
                ? new ExternalTextResourcePack(bytes)
                : null;
            return ValueTask.FromResult(pack);
        }
    }

}
