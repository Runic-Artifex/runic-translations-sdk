using System;

namespace RunicTranslations.Authoring.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        ProjectCreationTests.Register(runner);
        WorkspaceDiscoveryTests.Register(runner);
        WorkspaceMutationTests.Register(runner);
        EditorStateTests.Register(runner);
        return runner.Run();
    }
}
