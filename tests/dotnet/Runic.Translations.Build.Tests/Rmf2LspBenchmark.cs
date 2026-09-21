using System;
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

namespace Runic.Translations.Build.Tests;

internal static class Rmf2LspBenchmark
{
    private const string BaselinePath = "tests/benchmarks/translations/rmf2-lsp/baseline-v1.json";

    internal static int Run()
    {
        JsonObject baseline = JsonNode.Parse(File.ReadAllText(RepositoryPaths.Resolve(BaselinePath)))!.AsObject();
        int messageCount = baseline["catalogMessages"]!.GetValue<int>();
        int samples = baseline["samples"]!.GetValue<int>();
        int cancellationQueueDepth = baseline["cancellationQueueDepth"]!.GetValue<int>();
        string project = "{\"schemaVersion\":1,\"catalog\":\"benchmark\",\"code\":{\"namespace\":\"Example\",\"className\":\"Text\"},\"baseLocale\":\"en\",\"sourceLayout\":\"rmf2-v1\"}";
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
            int busyStart = 20_000 + sample * cancellationQueueDepth;
            int barrierId = 30_000 + sample;
            incrementalSession.Send(barrierId, "runic/testBarrier", new JsonObject());
            for (int index = 0; index < cancellationQueueDepth; index++)
                incrementalSession.Send(busyStart + index, "textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = incrementalUri } });
            int requestId = 10_000 + sample;
            Stopwatch watch = Stopwatch.StartNew();
            incrementalSession.Send(requestId, "textDocument/semanticTokens/full", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = incrementalUri } });
            incrementalSession.Notify("$/cancelRequest", new JsonObject { ["id"] = requestId });
            incrementalSession.Notify("runic/testBarrier/release", new JsonObject());
            JsonNode response = incrementalSession.Wait(requestId);
            cancellation.Add(watch.Elapsed.TotalMilliseconds);
            Assert.Equal(-32800, response["error"]?["code"]?.GetValue<int>() ?? 0, "LSP cancellation did not interrupt the queued request");
            for (int index = 0; index < cancellationQueueDepth; index++) incrementalSession.Wait(busyStart + index);
            incrementalSession.Wait(barrierId);
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
        private readonly Process _process;
        private readonly object _gate = new();
        private readonly List<JsonNode> _frames = new();
        private readonly Task _reader;
        private int _nextId = 1;

        internal LspSession(string workingDirectory)
        {
            ProcessStartInfo start = new("dotnet") { WorkingDirectory = workingDirectory, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(RepositoryPaths.ToolAssembly); start.ArgumentList.Add("lsp");
            start.Environment["RUNIC_LSP_TEST_BARRIER"] = "1";
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the RMF2 language server.");
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

        internal JsonNode Wait(int id)
        {
            Stopwatch timeout = Stopwatch.StartNew();
            lock (_gate)
            {
                while (true)
                {
                    JsonNode? frame = _frames.FirstOrDefault(value => value["id"]?.ToString() == id.ToString(CultureInfo.InvariantCulture));
                    if (frame is not null) return frame;
                    if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException($"LSP response {id} was not received.");
                    Monitor.Wait(_gate, 100);
                }
            }
        }

        private void Write(JsonObject message)
        {
            string json = message.ToJsonString();
            _process.StandardInput.Write("Content-Length: " + Encoding.UTF8.GetByteCount(json) + "\r\n\r\n" + json);
            _process.StandardInput.Flush();
        }

        private void ReadFrames()
        {
            Stream stream = _process.StandardOutput.BaseStream;
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
                if (!_process.HasExited)
                {
                    Request("shutdown", new JsonObject());
                    Notify("exit", new JsonObject());
                    if (!_process.WaitForExit(5_000)) _process.Kill(true);
                }
            }
            finally { _process.Dispose(); _reader.GetAwaiter().GetResult(); }
        }
    }
}
