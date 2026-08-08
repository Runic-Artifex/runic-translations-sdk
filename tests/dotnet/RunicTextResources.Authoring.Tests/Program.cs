using System;

namespace RunicTextResources.Authoring.Tests;

internal static class Program
{
    public static int Main()
    {
        TestRunner runner = new();
        ProjectCreationTests.Register(runner);
        WorkspaceDiscoveryTests.Register(runner);
        WorkspaceMutationTests.Register(runner);
        return runner.Run();
    }
}
