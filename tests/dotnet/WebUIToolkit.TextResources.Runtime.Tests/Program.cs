using System.Threading.Tasks;

namespace WebUIToolkit.TextResources.Runtime.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        TestRunner runner = new();
        FormatterTests.Register(runner);
        RuntimeTests.Register(runner);
        ExternalPackTests.Register(runner);
        WaveBCorpusTests.Register(runner);
        return await runner.RunAsync().ConfigureAwait(false);
    }
}
