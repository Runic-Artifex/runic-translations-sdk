namespace Runic.Translations.Build.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--rmf2-lsp-benchmark") return Rmf2LspBenchmark.Run();
        TestRunner runner = new();
        Rmf2IntegrationTests.Register(runner);
        if (args.Length == 0) { CliIntegrationTests.Register(runner); BuildIntegrationTests.Register(runner); }
        return runner.Run();
    }
}
