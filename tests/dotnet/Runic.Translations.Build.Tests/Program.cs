namespace Runic.Translations.Build.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        CliIntegrationTests.Register(runner);
        BuildIntegrationTests.Register(runner);
        return runner.Run();
    }
}
