using System.Threading.Tasks;

namespace RunicTranslations.Runtime.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        TestRunner runner = new();
        FormatterTests.Register(runner);
        RuntimeTests.Register(runner);
        CompiledMessageTests.Register(runner);
        TranslationReferenceTests.Register(runner);
        ExternalPackTests.Register(runner);
        WaveBCorpusTests.Register(runner);
        return await runner.RunAsync().ConfigureAwait(false);
    }
}
