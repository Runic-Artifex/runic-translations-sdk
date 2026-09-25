using System;

namespace Runic.Translations.Compiler.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        TestRunner runner = new();
        Rmf2SemanticV5Tests.Register(runner);
        Rmf2SemanticV5SchemaTests.Register(runner);
        Rmf2RuntimeV5Tests.Register(runner);
        Rmf2ProjectV5Tests.Register(runner);
        Rmf2ArtifactV5Tests.Register(runner);
        Rmf2EsmV5Tests.Register(runner);
        Rmf2V1CorpusTests.Register(runner);
        if (args.Length == 1 && args[0] == "--rmf2-semantic-v5") return runner.Run();
        SchemaTests.Register(runner);
        return runner.Run();
    }
}
