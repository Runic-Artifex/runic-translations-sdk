using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.Tool;

namespace Runic.Translations.Build.Tests;

internal static class Rmf2LspBenchmark
{
    private const string BaselinePath = "tests/benchmarks/translations/rmf2-lsp/baseline-v2.json";

    internal static int Run()
    {
        JsonObject baseline = JsonNode.Parse(File.ReadAllText(RepositoryPaths.Resolve(BaselinePath)))!.AsObject();
        int messageCount = baseline["catalogMessages"]!.GetValue<int>();
        int samples = baseline["samples"]!.GetValue<int>();
        int cancellationQueueDepth = baseline["cancellationQueueDepth"]!.GetValue<int>();
        string project = "{\"schemaVersion\":1,\"catalog\":\"benchmark\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"sourceLayout\":\"rmf2-v1\",\"executionProfile\":\"rmf2-execution-v2\"}";
        string largeText = string.Join('\n', Enumerable.Range(0, messageCount).Select(index => $"item_{index:D5} = Value{index}")) + "\n";
        List<double> largeCatalog = new();
        for (int sample = 0; sample < samples; sample++)
        {
            using TemporaryDirectory temporary = CreateWorkspace(project, largeText);
            using LspSession session = new(temporary.Path);
            session.Initialize(temporary.Path);
            string uri = new Uri(temporary.Resolve("en.rmf2")).AbsoluteUri;
            Stopwatch watch = Stopwatch.StartNew();
            session.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = 1, ["text"] = largeText } });
            session.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
            largeCatalog.Add(watch.Elapsed.TotalMilliseconds);
            if (sample == 0)
            {
                JsonNode preview = session.Request("workspace/executeCommand", new JsonObject {
                    ["command"] = "runic.preview",
                    ["arguments"] = new JsonArray(uri, "item_00000", "en"),
                });
                Assert.Equal(5, preview["result"]?["ast"]?["astVersion"]?.GetValue<int>() ?? 0, "LSP benchmark did not exercise execution-v2 preview validation");
            }
        }

        using TemporaryDirectory incrementalWorkspace = CreateWorkspace(project, largeText);
        using LspSession incrementalSession = new(incrementalWorkspace.Path);
        incrementalSession.Initialize(incrementalWorkspace.Path);
        string incrementalUri = new Uri(incrementalWorkspace.Resolve("en.rmf2")).AbsoluteUri;
        incrementalSession.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = incrementalUri, ["version"] = 1, ["text"] = largeText } });
        incrementalSession.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = incrementalUri } });
        List<double> incremental = new();
        for (int sample = 0; sample < samples; sample++)
        {
            Stopwatch watch = Stopwatch.StartNew();
            string replacement = sample % 2 == 0 ? "ChangedA" : "ChangedB";
            incrementalSession.Notify("textDocument/didChange", new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = incrementalUri, ["version"] = sample + 2 },
                ["contentChanges"] = new JsonArray(new JsonObject {
                    ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = 0, ["character"] = 12 }, ["end"] = new JsonObject { ["line"] = 0, ["character"] = 18 } },
                    ["text"] = replacement,
                }),
            });
            incrementalSession.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = incrementalUri } });
            incremental.Add(watch.Elapsed.TotalMilliseconds);
        }

        List<double> cancellation = new();
        for (int sample = 0; sample < samples; sample++)
        {
            using TemporaryDirectory cancellationWorkspace = CreateWorkspace(project, largeText);
            using LspSession cancellationSession = new(cancellationWorkspace.Path, enableRequestBarrier: true);
            cancellationSession.Initialize(cancellationWorkspace.Path);
            string cancellationUri = new Uri(cancellationWorkspace.Resolve("en.rmf2")).AbsoluteUri;
            cancellationSession.Notify("textDocument/didOpen", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = cancellationUri, ["version"] = 1, ["text"] = largeText } });
            cancellationSession.Request("textDocument/documentSymbol", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = cancellationUri } });
            int busyStart = 20_000 + sample * cancellationQueueDepth;
            int barrierId = 30_000 + sample;
            cancellationSession.Send(barrierId, "runic/benchmarkBarrier", new JsonObject());
            cancellationSession.WaitForRequestBarrier();
            for (int index = 0; index < cancellationQueueDepth; index++)
                cancellationSession.Send(busyStart + index, "textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = cancellationUri } });
            int requestId = 10_000 + sample;
            cancellationSession.ExpectCancellation(requestId);
            Stopwatch watch = Stopwatch.StartNew();
            cancellationSession.Send(requestId, "textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = cancellationUri } });
            cancellationSession.Notify("$/cancelRequest", new JsonObject { ["id"] = requestId });
            cancellationSession.WaitForCancellation();
            cancellationSession.ReleaseRequestBarrier();
            JsonNode response = cancellationSession.Wait(requestId);
            cancellation.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(-32800, response["error"]?["code"]?.GetValue<int>() ?? 0, "LSP cancellation did not interrupt the queued request");
            for (int index = 0; index < cancellationQueueDepth; index++) cancellationSession.Wait(busyStart + index);
            cancellationSession.Wait(barrierId);
        }

        double largeMedian = Median(largeCatalog);
        double incrementalMedian = Median(incremental);
        double cancellationMedian = Median(cancellation);
        CheckBound("largeCatalogMedianMilliseconds", largeMedian, baseline["maxMilliseconds"]!["largeCatalogMedian"]!.GetValue<double>());
        CheckBound("incrementalMedianMilliseconds", incrementalMedian, baseline["maxMilliseconds"]!["incrementalMedian"]!.GetValue<double>());
        CheckBound("cancellationMedianMilliseconds", cancellationMedian, baseline["maxMilliseconds"]!["cancellationMedian"]!.GetValue<double>());
        JsonObject report = new() {
            ["protocol"] = "runic-rmf2-lsp-benchmark",
            ["baseline"] = BaselinePath,
            ["executionProfile"] = "rmf2-execution-v2",
            ["latencyTransport"] = "child-process-stdio",
            ["cancellationTransport"] = "in-process-framed-streams",
            ["catalogMessages"] = messageCount,
            ["samples"] = samples,
            ["cancellationQueueDepth"] = cancellationQueueDepth,
            ["largeCatalogMedianMilliseconds"] = largeMedian,
            ["incrementalMedianMilliseconds"] = incrementalMedian,
            ["cancellationMedianMilliseconds"] = cancellationMedian,
            ["runtime"] = Environment.Version.ToString(),
        };
        Console.WriteLine(report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static TemporaryDirectory CreateWorkspace(string project, string source)
    {
        TemporaryDirectory temporary = new();
        File.WriteAllText(temporary.Resolve("runic.json"), project);
        File.WriteAllText(temporary.Resolve("en.rmf2"), source);
        return temporary;
    }

    private static double Median(List<double> values) => values.OrderBy(value => value).ElementAt(values.Count / 2);

    private static void CheckBound(string name, double value, double maximum)
    {
        if (value > maximum) throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"{name} exceeded bounded baseline: {value:F2}ms > {maximum:F2}ms."));
    }

    private sealed class LspSession : IDisposable
    {
        private readonly Process? _process;
        private readonly BlockingByteStream? _serverInput;
        private readonly BlockingByteStream? _serverOutput;
        private readonly RequestBarrier? _requestBarrier;
        private readonly Task<int>? _server;
        private readonly object _gate = new();
        private readonly List<JsonNode> _frames = new();
        private readonly Task _reader;
        private int _nextId = 1;

        internal LspSession(string workingDirectory, bool enableRequestBarrier = false)
        {
            if (!enableRequestBarrier)
            {
                var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory, RedirectStandardInput = true, RedirectStandardOutput = true };
                start.ArgumentList.Add(RepositoryPaths.ToolAssembly);
                start.ArgumentList.Add("lsp");
                _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the RMF2 language server.");
            }
            else
            {
                _serverInput = new BlockingByteStream();
                _serverOutput = new BlockingByteStream();
                _requestBarrier = new RequestBarrier("runic/benchmarkBarrier");
                var server = new Rmf2LanguageServer(_serverInput, _serverOutput, _requestBarrier.BeforeRequest, _requestBarrier.CancellationObserved);
                _server = Task.Run(() => {
                    try { return server.Run(); }
                    finally { _serverOutput.CompleteWriting(); }
                });
            }
            _reader = Task.Run(ReadFrames);
        }

        internal void Initialize(string root)
        {
            Request("initialize", new JsonObject {
                ["rootUri"] = new Uri(root + Path.DirectorySeparatorChar).AbsoluteUri,
                ["capabilities"] = new JsonObject { ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray("utf-16") } },
            });
            Notify("initialized", new JsonObject());
        }

        internal JsonNode Request(string method, JsonObject args)
        {
            int id = Interlocked.Increment(ref _nextId);
            Send(id, method, args);
            return Wait(id);
        }

        internal void Send(int id, string method, JsonObject args)
        {
            JsonObject request = new() { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = args };
            Write(request);
        }

        internal void Notify(string method, JsonObject args) => Write(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = args });

        internal void WaitForRequestBarrier() => (_requestBarrier ?? throw new InvalidOperationException("This session has no request barrier.")).WaitUntilEntered();

        internal void ExpectCancellation(int id) => (_requestBarrier ?? throw new InvalidOperationException("This session has no request barrier.")).ExpectCancellation(id);

        internal void WaitForCancellation() => (_requestBarrier ?? throw new InvalidOperationException("This session has no request barrier.")).WaitForCancellation();

        internal void ReleaseRequestBarrier() => (_requestBarrier ?? throw new InvalidOperationException("This session has no request barrier.")).Release();

        internal JsonNode Wait(int id)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            lock (_gate)
            {
                while (true)
                {
                    JsonNode? frame = _frames.FirstOrDefault(value => value["id"]?.ToString() == id.ToString(CultureInfo.InvariantCulture));
                    if (frame is not null) return frame;
                    if (_server?.IsFaulted == true) _server.GetAwaiter().GetResult();
                    if (_process?.HasExited == true) throw new InvalidOperationException($"LSP exited before response {id} with code {_process.ExitCode}.");
                    if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException($"LSP response {id} was not received.");
                    Monitor.Wait(_gate, 100);
                }
            }
        }

        private void Write(JsonObject message)
        {
            string json = message.ToJsonString();
            if (_process is not null)
            {
                _process.StandardInput.Write("Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json);
                _process.StandardInput.Flush();
            }
            else
            {
                byte[] payload = Encoding.UTF8.GetBytes("Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json);
                _serverInput!.Write(payload);
                _serverInput.Flush();
            }
        }

        private void ReadFrames()
        {
            Stream stream = _process?.StandardOutput.BaseStream ?? _serverOutput!;
            try
            {
                while (true)
                {
                    List<byte> header = new(); int value;
                    while ((value = stream.ReadByte()) >= 0)
                    {
                        header.Add((byte)value);
                        if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                    }
                    if (value < 0) return;
                    string text = Encoding.ASCII.GetString(header.ToArray());
                    int start = text.IndexOf(':'); int length = int.Parse(text[(start + 1)..].Trim(), CultureInfo.InvariantCulture);
                    byte[] payload = new byte[length]; stream.ReadExactly(payload);
                    lock (_gate) { _frames.Add(JsonNode.Parse(payload)!); Monitor.PulseAll(_gate); }
                }
            }
            catch (Exception exception)
            {
                lock (_gate) { _frames.Add(new JsonObject { ["error"] = new JsonObject { ["message"] = exception.ToString() } }); Monitor.PulseAll(_gate); }
            }
        }

        public void Dispose()
        {
            try
            {
                // Do not leave the in-process worker blocked when a benchmark
                // assertion fails before the normal release point.
                _requestBarrier?.Release();
                if (_process is not null)
                {
                    if (!_process.HasExited)
                    {
                        Request("shutdown", new JsonObject());
                        Notify("exit", new JsonObject());
                        if (!_process.WaitForExit(5000)) throw new TimeoutException("Child-process LSP did not exit.");
                    }
                }
                else if (!_server!.IsCompleted)
                {
                    Request("shutdown", new JsonObject());
                    Notify("exit", new JsonObject());
                    if (!_server.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("In-process LSP did not exit.");
                }
            }
            finally
            {
                if (_process is not null && !_process.HasExited) _process.Kill(entireProcessTree: true);
                _serverInput?.CompleteWriting();
                _serverOutput?.CompleteWriting();
                _reader.GetAwaiter().GetResult();
                _process?.Dispose();
                _serverInput?.Dispose();
                _serverOutput?.Dispose();
                _requestBarrier?.Dispose();
            }
        }
    }

    private sealed class RequestBarrier(string method) : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new(false);
        private readonly ManualResetEventSlim _cancelled = new(false);
        private readonly ManualResetEventSlim _released = new(false);
        private string? _expectedCancellation;

        internal void BeforeRequest(string requestMethod)
        {
            if (!string.Equals(requestMethod, method, StringComparison.Ordinal)) return;
            _entered.Set();
            if (!_released.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Benchmark request barrier was not released.");
        }

        internal void WaitUntilEntered()
        {
            if (!_entered.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Benchmark request barrier was not reached.");
        }

        internal void ExpectCancellation(int id) => _expectedCancellation = id.ToString(CultureInfo.InvariantCulture);

        internal void CancellationObserved(string id)
        {
            if (string.Equals(id, _expectedCancellation, StringComparison.Ordinal)) _cancelled.Set();
        }

        internal void WaitForCancellation()
        {
            if (!_cancelled.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Benchmark cancellation was not observed by the LSP reader.");
        }

        internal void Release() => _released.Set();

        public void Dispose()
        {
            _entered.Dispose();
            _cancelled.Dispose();
            _released.Dispose();
        }
    }

    private sealed class BlockingByteStream : Stream
    {
        private readonly BlockingCollection<byte[]> _chunks = new();
        private byte[]? _current;
        private int _offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        internal void CompleteWriting()
        {
            if (!_chunks.IsAddingCompleted) _chunks.CompleteAdding();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count), "Offset and count must identify a range within the buffer.");
            while (_current is null || _offset == _current.Length)
            {
                if (!_chunks.TryTake(out _current, Timeout.Infinite)) return 0;
                _offset = 0;
            }
            int copied = Math.Min(count, _current.Length - _offset);
            Array.Copy(_current, _offset, buffer, offset, copied);
            _offset += copied;
            return copied;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset + count > buffer.Length) throw new ArgumentOutOfRangeException(nameof(count), "Offset and count must identify a range within the buffer.");
            byte[] copy = new byte[count];
            Array.Copy(buffer, offset, copy, 0, count);
            _chunks.Add(copy);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                CompleteWriting();
                _chunks.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
