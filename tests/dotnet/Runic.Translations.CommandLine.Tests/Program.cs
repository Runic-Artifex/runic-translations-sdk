using System.Threading.Tasks;

namespace Runic.Translations.CommandLine.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        TestRunner runner = new();
        ResolverTests.Register(runner);
        return await runner.RunAsync().ConfigureAwait(false);
    }
}
