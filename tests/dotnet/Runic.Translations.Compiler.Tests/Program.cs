using System;

namespace Runic.Translations.Compiler.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--rmf2-benchmark") return Rmf2MarkupTests.Benchmark();
        TestRunner runner = new();
        Rmf2SemanticV5Tests.Register(runner);
        Rmf2SemanticV5SchemaTests.Register(runner);
        Rmf2RuntimeV5Tests.Register(runner);
        if (args.Length == 1 && args[0] == "--rmf2-semantic-v5") return runner.Run();
        RuntimeContractTests.Register(runner);
        CompilerTests.Register(runner);
        EsmGenerationTests.Register(runner);
        CppGenerationTests.Register(runner);
        SchemaV2Tests.Register(runner);
        Mf2ProjectTests.Register(runner);
        TomlProjectTests.Register(runner);
        Rmf2Tests.Register(runner);
        Mf2SyntaxTests.Register(runner);
        Rmf2MarkupTests.Register(runner);
        CapabilityMatrixTests.Register(runner);
        AnalysisTests.Register(runner);
        CorpusTests.Register(runner);
        SchemaTests.Register(runner);
        ContractCoherenceTests.Register(runner);
        return runner.Run();
    }
}
