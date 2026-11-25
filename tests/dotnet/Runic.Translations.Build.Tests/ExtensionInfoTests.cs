using System.Linq;
using System.Text.Json;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations as an extension of the Runic core tool (rename plan P2): dotnet runic translations … runs it with
// the arguments unchanged, and dotnet runic doctor reads --extension-info. This is the core's contract
// (runic-sdk tools/dotnet-runic/ToolExtensions.cs, ParseInfo and the dispatch).
internal sealed class ExtensionInfoTests
{
    [Test, DisplayName("--extension-info prints name, version and extensionProtocol 1 as one JSON line")]
    public void ExtensionInfoReportsTheExtension()
    {
        using TemporaryDirectory temporary = new();
        ProcessResult info = TestFixture.RunTool(temporary, "--extension-info");
        Assert.Equal(0, info.ExitCode, info.Combined);
        Assert.Equal(string.Empty, info.StandardError);
        string[] lines = info.StandardOutput.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        Assert.Equal(1, lines.Length, info.StandardOutput);

        using JsonDocument document = JsonDocument.Parse(lines[0]);
        JsonElement root = document.RootElement;
        Assert.Equal("translations", root.GetProperty("name").GetString());
        Assert.Equal(1, root.GetProperty("extensionProtocol").GetInt32());
        Assert.Equal("runic-translations", root.GetProperty("command").GetString());
        Assert.Equal("dotnet-runic-translations", root.GetProperty("package").GetString());

        // The version is the release that --version names, without build metadata.
        ProcessResult version = TestFixture.RunTool(temporary, "--version");
        Assert.Equal(0, version.ExitCode, version.Combined);
        string reported = version.StandardOutput.Trim().Split(' ').Last();
        Assert.Equal(reported, root.GetProperty("version").GetString());
        Assert.True(!reported.Contains('+'), $"The version carries build metadata: {reported}");
    }

    [Test, DisplayName("the tool takes the arguments the core passes: a leading --format, then the command")]
    public void ToolAcceptsTheCoreDispatch()
    {
        using TemporaryDirectory temporary = new();
        // dotnet runic --format json translations --help runs runic-translations --format json --help.
        ProcessResult help = TestFixture.RunTool(temporary, "--format", "json", "--help");
        Assert.Equal(0, help.ExitCode, help.Combined);
        // A leading selector reaches the command after it: support preview's usage fault is a JSON envelope.
        ProcessResult usage = TestFixture.RunTool(temporary, "--format", "json", "support", "preview");
        Assert.True(usage.ExitCode != 0, usage.Combined);
        using JsonDocument envelope = JsonDocument.Parse(usage.StandardOutput);
        Assert.Equal(false, envelope.RootElement.GetProperty("success").GetBoolean());
    }
}
