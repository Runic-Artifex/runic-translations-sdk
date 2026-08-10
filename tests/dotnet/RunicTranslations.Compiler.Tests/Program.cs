using System;

namespace RunicTranslations.Compiler.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        RuntimeContractTests.Register(runner);
        CompilerTests.Register(runner);
        EsmGenerationTests.Register(runner);
        CppGenerationTests.Register(runner);
        SchemaV2Tests.Register(runner);
        CapabilityMatrixTests.Register(runner);
        AnalysisTests.Register(runner);
        CorpusTests.Register(runner);
        SchemaTests.Register(runner);
        return runner.Run();
    }
}
