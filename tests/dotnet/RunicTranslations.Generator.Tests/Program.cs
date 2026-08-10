using System;

namespace RunicTranslations.Generator.Tests;

internal static class Program
{
    public static int Main()
    {
        var runner = new TestRunner();
        GeneratorTests.Register(runner);
        return runner.Run();
    }
}
