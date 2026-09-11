namespace Runic.Translations.Build.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        TestRunner runner = new();
        Rmf2IntegrationTests.Register(runner);
        if (args.Length == 0) { CliIntegrationTests.Register(runner); BuildIntegrationTests.Register(runner); }
        return runner.Run();
    }
}
