using System;

namespace WebUIToolkit.TextResources.Compiler.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        RuntimeContractTests.Register(runner);
        CompilerTests.Register(runner);
        CorpusTests.Register(runner);
        SchemaTests.Register(runner);
        return runner.Run();
    }
}
