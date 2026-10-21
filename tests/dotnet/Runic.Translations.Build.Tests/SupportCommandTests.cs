using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Runic.CommandLine;
using Runic.Translations.Tool;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// runic-translations support preview|collect|remove, moved from dotnet runic support --mode <mode>.
internal sealed class SupportCommandTests
{
    // The editor's diagnostics.json, with its groups out of order and a null commit.
    private const string EditorSummary = """
        {"schema":"runic.translations.editor-diagnostics/1","generatedAt":"2026-10-20T12:00:00+00:00","application":{"product":"Runic Translations Editor","version":"0.7.0-preview.5","updateChannel":"preview","commit":null,"runtime":".NET 10.0.0","runtimeIdentifier":"linux-x64","operatingSystem":"Linux","architecture":"X64"},"workspace":{"catalogId":"app","schemaVersion":1,"localeCount":2,"documentCount":3,"messageCount":4,"compilerSuccess":false,"reviewStateAvailable":true,"pendingTransaction":false,"pendingTransactionPathCount":0,"diagnostics":[{"id":"RTR0011","severity":"warning","count":2},{"id":"RTR0010","severity":"error","count":1}]}}
        """;

    // runic.support-envelope/1, byte for byte as dotnet runic support wrote it.
    private const string Envelope =
        """{"Schema":"runic.support-envelope/1","Collectors":[{"Id":"runic.translations.editor-diagnostics","Schema":"runic.translations.editor-diagnostics/1","Application":{"Product":"Runic Translations Editor","Version":"0.7.0-preview.5","UpdateChannel":"preview","Commit":null,"Runtime":".NET 10.0.0","RuntimeIdentifier":"linux-x64","OperatingSystem":"Linux","Architecture":"X64"},"Workspace":{"CatalogId":"app","SchemaVersion":1,"LocaleCount":2,"DocumentCount":3,"MessageCount":4,"CompilerSuccess":false,"ReviewStateAvailable":true,"PendingTransaction":false,"PendingTransactionPathCount":0,"Diagnostics":[{"Id":"RTR0010","Severity":"error","Count":1},{"Id":"RTR0011","Severity":"warning","Count":2}]}}],"Omissions":[{"Source":"automatic-capture","Reason":"not-collected-by-local-support-envelope"},{"Source":"network","Reason":"not-collected-by-local-support-envelope"},{"Source":"workspace-roots","Reason":"not-collected-by-local-support-envelope"},{"Source":"relative-paths","Reason":"not-collected-by-local-support-envelope"},{"Source":"source-text","Reason":"not-collected-by-local-support-envelope"},{"Source":"translation-text","Reason":"not-collected-by-local-support-envelope"},{"Source":"review-text","Reason":"not-collected-by-local-support-envelope"},{"Source":"sessions-cookies-tokens","Reason":"not-collected-by-local-support-envelope"}]}""";

    private const string Omissions = """["automatic-capture","network","workspace-roots","relative-paths","source-text","translation-text","review-text","sessions-cookies-tokens"]""";

    private static string Digest => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Envelope)));

    [Test, DisplayName("support preview reports the envelope without writing it")]
    public void PreviewReportsTheEnvelope()
    {
        using TemporaryDirectory temporary = new();
        WriteEditorZip(temporary, EditorSummary);

        ProcessResult human = TestFixture.RunTool(temporary, "support", "preview", "--editor-diagnostics", "editor.zip");
        Assert.Equal(0, human.ExitCode, human.Combined);
        Assert.Equal(
            "Support preview: selected runic.translations.editor-diagnostics; omitted automatic-capture, network, workspace-roots, relative-paths, source-text, translation-text, review-text, sessions-cookies-tokens; outbound transport attempts: 0; digest: " + Digest + "\n",
            human.StandardOutput.Replace("\r\n", "\n"));

        ProcessResult json = TestFixture.RunTool(temporary, "support", "preview", "--editor-diagnostics", "editor.zip", "--format", "json");
        Assert.Equal(0, json.ExitCode, json.Combined);
        using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
        Assert.Equal("support preview", envelope.RootElement.GetProperty("command").GetString());
        Assert.Equal("runic.translations.support/1", envelope.RootElement.GetProperty("payloadType").GetString());
        PayloadSchemas.Validate(PayloadSchemas.Support, envelope.RootElement.GetProperty("payload"));
        Assert.Equal(
            "{\"mode\":\"preview\",\"destination\":null,\"digest\":\"" + Digest + "\",\"collectors\":[\"runic.translations.editor-diagnostics\"],\"omissions\":" + Omissions + ",\"outboundTransportAttempts\":0}",
            envelope.RootElement.GetProperty("payload").GetRawText());
        Assert.Equal("editor.zip", string.Join('|', TestFixture.RelativeFiles(temporary.Path)), "preview wrote a file.");
    }

    [Test, DisplayName("support collect writes the envelope byte for byte and never overwrites it")]
    public void CollectWritesTheEnvelope()
    {
        using TemporaryDirectory temporary = new();
        WriteEditorZip(temporary, EditorSummary);
        string destination = temporary.Resolve("support", "envelope.json");

        ProcessResult json = TestFixture.RunTool(temporary, "support", "collect", "--editor-diagnostics", "editor.zip", "--destination", Path.Combine("support", "envelope.json"), "--format", "json");
        Assert.Equal(0, json.ExitCode, json.Combined);
        Assert.Equal(Envelope, File.ReadAllText(destination, Encoding.UTF8));
        using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
        PayloadSchemas.Validate(PayloadSchemas.Support, envelope.RootElement.GetProperty("payload"));
        Assert.Equal(
            "{\"mode\":\"collect\",\"destination\":" + JsonSerializer.Serialize(destination) + ",\"digest\":\"" + Digest + "\",\"collectors\":[\"runic.translations.editor-diagnostics\"],\"omissions\":" + Omissions + ",\"outboundTransportAttempts\":0}",
            envelope.RootElement.GetProperty("payload").GetRawText());

        ProcessResult again = TestFixture.RunTool(temporary, "support", "collect", "--editor-diagnostics", "editor.zip", "--destination", destination, "--format", "json");
        AssertFault(again, 2, "RTRSUP003");
        Assert.Equal(Envelope, File.ReadAllText(destination, Encoding.UTF8));
    }

    [Test, DisplayName("support remove verifies and deletes only a support envelope")]
    public void RemoveDeletesOnlyAnEnvelope()
    {
        using TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("envelope.json"), Envelope, new UTF8Encoding(false));
        File.WriteAllText(temporary.Resolve("other.json"), """{"Schema":"other/1","Collectors":[],"Omissions":[]}""", new UTF8Encoding(false));

        AssertFault(TestFixture.RunTool(temporary, "support", "remove", "--destination", "other.json", "--format", "json"), 2, "RTRSUP017");
        Assert.True(File.Exists(temporary.Resolve("other.json")), "remove deleted a file that is not a support envelope.");
        AssertFault(TestFixture.RunTool(temporary, "support", "remove", "--destination", "envelope.json", "--editor-diagnostics", "editor.zip", "--format", "json"), 2, "RTRSUP005");

        ProcessResult removed = TestFixture.RunTool(temporary, "support", "remove", "--destination", "envelope.json", "--format", "json");
        Assert.Equal(0, removed.ExitCode, removed.Combined);
        Assert.False(File.Exists(temporary.Resolve("envelope.json")), "remove kept the envelope.");
        using JsonDocument envelope = JsonDocument.Parse(removed.StandardOutput);
        Assert.Equal(
            "{\"mode\":\"remove\",\"destination\":" + JsonSerializer.Serialize(temporary.Resolve("envelope.json")) + ",\"digest\":\"" + Digest + "\",\"collectors\":[],\"omissions\":[],\"outboundTransportAttempts\":0}",
            envelope.RootElement.GetProperty("payload").GetRawText());

        AssertFault(TestFixture.RunTool(temporary, "support", "remove", "--destination", "envelope.json", "--format", "json"), 2, "RTRSUP007");
    }

    [Test, DisplayName("support reports usage errors with RTRSUP codes and exit code 2, and I/O failures with exit code 1")]
    public void SupportFailuresUseTheirCodes()
    {
        using TemporaryDirectory temporary = new();
        WriteEditorZip(temporary, EditorSummary);
        WriteEditorZip(temporary, EditorSummary.Replace("\"catalogId\":\"app\"", "\"catalogId\":\"C:/Users/me/app\"", StringComparison.Ordinal), "private.zip");

        ProcessResult mode = TestFixture.RunTool(temporary, "support", "upload", "--format", "json");
        AssertFault(mode, 2, "RTRSUP001");
        ProcessResult human = TestFixture.RunTool(temporary, "support", "upload");
        Assert.Equal(2, human.ExitCode, human.Combined);
        Assert.Contains("RTRSUP001: Use support preview, collect, or remove.", human.StandardError);
        Assert.Contains("docs/guides/translations/diagnostics.md#rtrsup001", human.StandardOutput);

        AssertFault(TestFixture.RunTool(temporary, "support", "collect", "--editor-diagnostics", "editor.zip", "--format", "json"), 2, "RTRSUP002");
        AssertFault(TestFixture.RunTool(temporary, "support", "preview", "--format", "json"), 2, "RTRSUP008");
        AssertFault(TestFixture.RunTool(temporary, "support", "preview", "--editor-diagnostics", "missing.zip", "--format", "json"), 2, "RTRSUP010");
        // A path in the summary is refused, and the fault does not repeat it.
        ProcessResult path = TestFixture.RunTool(temporary, "support", "preview", "--editor-diagnostics", "private.zip", "--format", "json");
        AssertFault(path, 2, "RTRSUP024");
        Assert.False(path.Combined.Contains("Users/me", StringComparison.Ordinal), path.Combined);

        // The destination's folder is a file, so the envelope cannot be written.
        File.WriteAllText(temporary.Resolve("blocked"), string.Empty);
        ProcessResult io = TestFixture.RunTool(temporary, "support", "collect", "--editor-diagnostics", "editor.zip", "--destination", Path.Combine("blocked", "envelope.json"), "--format", "json");
        AssertFault(io, 1, "RTRSUP025");
    }

    [Test, DisplayName("exit codes follow the Runic tool table")]
    public void ExitCodesFollowTheTable()
    {
        (CommandExitCategory Category, int ExitCode)[] table =
        [
            (CommandExitCategory.Success, 0),
            (CommandExitCategory.CommandFailure, 1),
            (CommandExitCategory.Unavailable, 1),
            (CommandExitCategory.Usage, 2),
            (CommandExitCategory.Validation, 2),
            (CommandExitCategory.HostFailure, 3),
            (CommandExitCategory.Cancelled, 130),
        ];
        foreach ((CommandExitCategory category, int exitCode) in table)
            Assert.Equal(exitCode, ToolExitCodePolicy.Instance.GetExitCode(category), category.ToString());
    }

    [Test, DisplayName("--runic-output is a hidden alias of --format before --")]
    public void FormerOutputSelectorIsAnAlias()
    {
        Assert.Equal(
            "support|preview|--format|json|--format=json|--|--runic-output|json",
            string.Join('|', Runic.Tools.Shared.OutputSelectorAlias.Rewrite(
                ["support", "preview", "--runic-output", "json", "--runic-output=json", "--", "--runic-output", "json"], "--runic-output")));

        using TemporaryDirectory temporary = new();
        WriteEditorZip(temporary, EditorSummary);
        foreach (string[] selector in new[] { new[] { "--runic-output", "json" }, new[] { "--runic-output=json" } })
        {
            ProcessResult json = TestFixture.RunTool(temporary, ["support", "preview", "--editor-diagnostics", "editor.zip", .. selector]);
            Assert.Equal(0, json.ExitCode, json.Combined);
            using JsonDocument envelope = JsonDocument.Parse(json.StandardOutput);
            Assert.Equal("runic.translations.support/1", envelope.RootElement.GetProperty("payloadType").GetString());
        }

        foreach (string[] arguments in new[] { new[] { "--help" }, new[] { "help", "support", "preview" } })
        {
            ProcessResult help = TestFixture.RunTool(temporary, arguments);
            Assert.Equal(0, help.ExitCode, help.Combined);
            Assert.Contains("--format <human|json>", help.StandardOutput);
            Assert.False(help.StandardOutput.Contains("--runic-output", StringComparison.Ordinal), help.StandardOutput);
        }

        ProcessResult completion = TestFixture.RunTool(temporary, "completion", "bash");
        Assert.Equal(0, completion.ExitCode, completion.Combined);
        Assert.Contains("--format", completion.StandardOutput);
        Assert.False(completion.StandardOutput.Contains("--runic-output", StringComparison.Ordinal), completion.StandardOutput);
    }

    private static void AssertFault(ProcessResult result, int exitCode, string code)
    {
        Assert.Equal(exitCode, result.ExitCode, result.Combined);
        using JsonDocument envelope = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(exitCode, envelope.RootElement.GetProperty("exitCode").GetInt32(), result.StandardOutput);
        Assert.Equal(code, envelope.RootElement.GetProperty("fault").GetProperty("code").GetString(), result.StandardOutput);
    }

    private static void WriteEditorZip(TemporaryDirectory temporary, string summary, string name = "editor.zip")
    {
        using ZipArchive archive = ZipFile.Open(temporary.Resolve(name), ZipArchiveMode.Create);
        using Stream stream = archive.CreateEntry("diagnostics.json").Open();
        stream.Write(Encoding.UTF8.GetBytes(summary));
    }
}
