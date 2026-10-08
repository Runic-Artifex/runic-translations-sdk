namespace Runic.Translations.Build.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--rmf2-lsp-benchmark") return Rmf2LspBenchmark.Run();
        TestRunner runner = new();
        if (args.Length == 1 && args[0] == "--xaml-wpf") { XamlBuildTests.Register(runner, wpf: true); return runner.Run(); }
        if (args.Length == 1 && args[0] == "--xaml") { XamlBuildTests.Register(runner); return runner.Run(); }
        if (args.Length == 3 && args[0] == "--xaml-packages") { XamlBuildTests.Register(runner, args[1], args[2]); return runner.Run(); }
        XamlBuildTests.Register(runner);
        Rmf2DiagnosticLspTests.Register(runner);
        Rmf2DocumentLspTests.Register(runner);
        Rmf2IntegrationTests.Register(runner);
        if (args.Length == 0) { CliIntegrationTests.Register(runner); ServeModeTests.Register(runner); BuildIntegrationTests.Register(runner); }
        return runner.Run();
    }
}
