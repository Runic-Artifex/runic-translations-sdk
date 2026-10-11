using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using Runic.Translations.Tool;
using TUnit.Core;

namespace Runic.Translations.Build.Tests;

// `runic-translations serve` is the persistent compiler behind the Vite plugin's dev loop.
internal sealed class ServeModeTests
{
    private static readonly string[] EsmOnly = ["esm"];

    [Test, DisplayName("Serve mode generates the same bytes as one-shot generation across edits")]
    public void ServeMatchesOneShotAcrossEdits()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string served = temporary.Resolve("served"), oneShot = temporary.Resolve("one-shot");
        using var server = new CompileServerHarness();
        for (int edit = 0; edit < 3; edit++)
        {
            File.WriteAllText(Path.Combine(project, "en.rmf2"), $"title = Shop {edit}\ngreeting =\n  .input {{$name :string}}\n  {{{{Hello {{$name}}!}}}}\n", new UTF8Encoding(false));
            JsonElement response = server.Send(Request(edit, "generate", project, served));
            Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());
            Assert.Equal(edit, response.GetProperty("id").GetInt32());
            Assert.Contains("generated", response.GetProperty("output").GetString() ?? string.Empty);

            ProcessResult generate = TestFixture.RunTool(temporary, "generate", "--project", project, "--output", oneShot, "--emit-esm");
            Assert.Equal(0, generate.ExitCode, generate.Combined);
            string[] files = TestFixture.RelativeFiles(oneShot);
            Assert.Equal(string.Join('\n', files), string.Join('\n', TestFixture.RelativeFiles(served)));
            foreach (string file in files)
                Assert.FileBytesEqual(Path.Combine(oneShot, file), Path.Combine(served, file));
            Assert.True(Directory.EnumerateFiles(served, "*.js", SearchOption.AllDirectories).Any(file => File.ReadAllText(file).Contains($"Shop {edit}", StringComparison.Ordinal)), $"Edit {edit} is missing from the served output.");
        }
    }

    [Test, DisplayName("Serve mode reports diagnostics and malformed requests without stopping")]
    public void ServeReportsFailures()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        using var server = new CompileServerHarness();

        File.WriteAllText(Path.Combine(project, "en.rmf2"), "title = {$name :unknown-function}\n", new UTF8Encoding(false));
        JsonElement failed = server.Send(Request(1, "generate", project, temporary.Resolve("out")));
        Assert.False(failed.GetProperty("ok").GetBoolean(), failed.ToString());
        Assert.Equal(1, failed.GetProperty("exitCode").GetInt32());
        Assert.Contains("error RTR", failed.GetProperty("message").GetString() ?? string.Empty);
        JsonElement translation = failed.GetProperty("diagnostics").EnumerateArray().First(item => item.GetProperty("code").GetString()!.StartsWith("RTR", StringComparison.Ordinal) && (item.GetProperty("path").GetString() ?? string.Empty).EndsWith("en.rmf2", StringComparison.Ordinal));
        Assert.Equal("error", translation.GetProperty("severity").GetString());
        Assert.True((translation.GetProperty("path").GetString() ?? string.Empty).EndsWith("en.rmf2", StringComparison.Ordinal), translation.ToString());
        Assert.Equal(1, translation.GetProperty("line").GetInt32());
        Assert.Contains("docs/guides/translations/diagnostics.md#rtr", translation.GetProperty("helpUri").GetString() ?? string.Empty);
        Assert.False(failed.GetProperty("diagnostics").EnumerateArray().Any(item => item.GetProperty("code").GetString() == "RCLI9012"), failed.ToString());
        Assert.False(Directory.Exists(temporary.Resolve("out")), "A failed compilation wrote output.");

        JsonElement malformed = server.Send("not json");
        Assert.Equal(JsonValueKind.Null, malformed.GetProperty("id").ValueKind);
        Assert.Equal(2, malformed.GetProperty("exitCode").GetInt32());

        JsonElement oversized = server.Send("{\"id\":7,\"method\":\"validate\",\"project\":\"" + new string('a', 1024 * 1024) + "\"}");
        Assert.Equal(JsonValueKind.Null, oversized.GetProperty("id").ValueKind);
        Assert.Contains("exceeds the supported size", oversized.GetProperty("message").GetString() ?? string.Empty);

        JsonElement unknown = server.Send("""{"id":"x","method":"compile"}""");
        Assert.Equal("x", unknown.GetProperty("id").GetString());
        Assert.Contains("unknown method 'compile'", unknown.GetProperty("message").GetString() ?? string.Empty);
        Assert.Contains("requires project and output", server.Send("""{"id":2,"method":"generate","project":"translations"}""").GetProperty("message").GetString() ?? string.Empty);
        Assert.Contains("unknown output group", server.Send("""{"id":3,"method":"generate","project":"p","output":"o","emit":["wasm"]}""").GetProperty("message").GetString() ?? string.Empty);

        File.WriteAllText(Path.Combine(project, "en.rmf2"), "title = Fixed\ngreeting =\n  .input {$name :string}\n  {{Hello {$name}!}}\n", new UTF8Encoding(false));
        JsonElement validated = server.Send(JsonSerializer.Serialize(new { id = 4, method = "validate", project }));
        Assert.True(validated.GetProperty("ok").GetBoolean(), validated.ToString());
    }

    [Test, DisplayName("Generation leaves byte-identical artifacts untouched")]
    public void GenerationKeepsUnchangedArtifacts()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        string output = temporary.Resolve("generated");
        ProcessResult first = TestFixture.RunTool(temporary, "generate", "--project", project, "--output", output, "--emit-esm");
        Assert.Equal(0, first.ExitCode, first.Combined);
        var past = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (string file in Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, past);

        File.WriteAllText(Path.Combine(project, "de.rmf2"), "title = Geschäft\ngreeting =\n  .input {$name :string}\n  {{Hallo {$name}!}}\n", new UTF8Encoding(false));
        ProcessResult second = TestFixture.RunTool(temporary, "generate", "--project", project, "--output", output, "--emit-esm");
        Assert.Equal(0, second.ExitCode, second.Combined);
        Dictionary<string, (long Length, DateTime LastWriteUtc)> snapshot = TestFixture.SnapshotFiles(output);
        string[] rewritten = snapshot.Where(pair => pair.Value.LastWriteUtc != past).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        Assert.True(rewritten.Length > 0, "The edited locale was not regenerated.");
        Assert.True(rewritten.Length < snapshot.Count, "Unchanged artifacts were rewritten: " + string.Join(", ", rewritten));
        ProcessResult verify = TestFixture.RunTool(temporary, "verify", "--project", project, "--output", output, "--emit-esm");
        Assert.Equal(0, verify.ExitCode, verify.Combined);
    }

    [Test, DisplayName("Serve mode runs as a process and exits on shutdown or closed input")]
    public void ServeProcessLifecycle()
    {
        using TemporaryDirectory temporary = new();
        string project = WriteProject(temporary);
        foreach (bool shutdown in new[] { true, false })
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = temporary.Path,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = new UTF8Encoding(false),
            };
            start.ArgumentList.Add(RepositoryPaths.ToolAssembly);
            start.ArgumentList.Add("serve");
            using Process process = Process.Start(start)!;
            try
            {
                using (JsonDocument ready = JsonDocument.Parse(process.StandardOutput.ReadLine() ?? "null"))
                {
                    Assert.Equal("ready", ready.RootElement.GetProperty("event").GetString());
                    Assert.Equal(CompileServer.Protocol, ready.RootElement.GetProperty("protocol").GetString());
                }
                process.StandardInput.WriteLine(JsonSerializer.Serialize(new { id = 1, method = "generate", project = "translations", output = "generated", emit = EsmOnly }));
                process.StandardInput.Flush();
                using (JsonDocument response = JsonDocument.Parse(process.StandardOutput.ReadLine() ?? "null"))
                    Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
                Assert.True(File.Exists(temporary.Resolve("generated", "app.esm-v5", "web-module-manifest-v3.json")), "Relative paths did not resolve against the server's working directory.");
                if (shutdown)
                {
                    process.StandardInput.WriteLine("""{"id":2,"method":"shutdown"}""");
                    process.StandardInput.Flush();
                    using JsonDocument response = JsonDocument.Parse(process.StandardOutput.ReadLine() ?? "null");
                    Assert.Equal(2, response.RootElement.GetProperty("id").GetInt32());
                }
                else process.StandardInput.Close();
                Assert.True(process.WaitForExit(30_000), "serve did not exit.");
                Assert.Equal(0, process.ExitCode);
                Assert.Equal(string.Empty, process.StandardOutput.ReadToEnd(), "serve wrote non-protocol output.");
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
        }
    }

    private static string WriteProject(TemporaryDirectory temporary)
    {
        string project = temporary.Resolve("translations");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "runic.json"), """
            { "schemaVersion": 1, "catalog": "app", "code": { "namespace": "Example", "className": "AppText" }, "baseLocale": "en", "locales": ["en", "de"] }
            """, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(project, "en.rmf2"), "title = Shop\ngreeting =\n  .input {$name :string}\n  {{Hello {$name}!}}\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(project, "de.rmf2"), "title = Laden\ngreeting =\n  .input {$name :string}\n  {{Hallo {$name}!}}\n", new UTF8Encoding(false));
        return project;
    }

    private static string Request(int id, string method, string project, string output) =>
        JsonSerializer.Serialize(new { id, method, project, output, emit = EsmOnly });

    // Drives one in-process server: each Send writes a request line and reads its response line.
    private sealed class CompileServerHarness : IDisposable
    {
        private readonly BlockingLineReader _input = new();
        private readonly ResponseStream _output = new();
        private readonly Thread _thread;

        internal CompileServerHarness()
        {
            _thread = new Thread(() => new CompileServer(_input, _output).Run("test")) { IsBackground = true };
            _thread.Start();
            using JsonDocument ready = JsonDocument.Parse(_output.ReadLine());
            Assert.Equal("ready", ready.RootElement.GetProperty("event").GetString());
        }

        internal JsonElement Send(string request)
        {
            _input.Add(request);
            using JsonDocument response = JsonDocument.Parse(_output.ReadLine());
            return response.RootElement.Clone();
        }

        public void Dispose()
        {
            Send("""{"id":0,"method":"shutdown"}""");
            Assert.True(_thread.Join(TimeSpan.FromSeconds(30)), "The server did not stop after shutdown.");
            _input.Dispose();
            _output.Dispose();
        }
    }

    private sealed class BlockingLineReader : TextReader
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _lines = new();
        private string _current = string.Empty;
        private int _position;
        internal void Add(string line) => _lines.Add(line + "\n");
        public override int Read()
        {
            if (_position == _current.Length) { _current = _lines.Take(); _position = 0; }
            return _current[_position++];
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _lines.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class ResponseStream : Stream
    {
        private readonly System.Collections.Concurrent.BlockingCollection<string> _lines = new();
        private readonly MemoryStream _pending = new();

        internal string ReadLine() => _lines.TryTake(out string? line, TimeSpan.FromSeconds(60))
            ? line
            : throw new TimeoutException("The server did not respond.");

        public override void Write(byte[] buffer, int offset, int count)
        {
            for (int index = offset; index < offset + count; index++)
            {
                if (buffer[index] != (byte)'\n') { _pending.WriteByte(buffer[index]); continue; }
                _lines.Add(Encoding.UTF8.GetString(_pending.ToArray()));
                _pending.SetLength(0);
            }
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _lines.Dispose(); _pending.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
