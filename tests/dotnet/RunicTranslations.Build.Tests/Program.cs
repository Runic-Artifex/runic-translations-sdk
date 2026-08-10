namespace RunicTranslations.Build.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        CliIntegrationTests.Register(runner);
        BuildIntegrationTests.Register(runner);
        GeneratedConsumerTests.Register(runner);
        WaveBCorpusTests.Register(runner);
        WaveCAssetManifestTests.Register(runner);
        return runner.Run();
    }
}
