using System;

namespace Runic.Translations.Generator.Tests;

internal static class Program
{
    public static int Main()
    {
        var runner = new TestRunner();
        GeneratorTests.Register(runner);
        GeneratorIncrementalityTests.Register(runner);
        GeneratorDiagnosticsTests.Register(runner);
        return runner.Run();
    }
}
