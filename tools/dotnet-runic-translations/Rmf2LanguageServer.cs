using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using JsonValue = System.Text.Json.Nodes.JsonValue;
using System.Text.RegularExpressions;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Compiler.Generation;
using Runic.Translations.Internal;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

/// <summary>Bounded stdio LSP transport over the shared recoverable resource model.</summary>
internal sealed partial class Rmf2LanguageServer
{
    private readonly Rmf2WorkspaceCache _syntaxCache = new();
    // The saved sources of the projects, reused while a file's length and write time are unchanged (#271).
    private readonly InputFiles.SourceFileCache _sourceFiles = new();
    private readonly Dictionary<string, Buffer> _buffers = new(StringComparer.Ordinal);
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly Action<string>? _beforeRequest;
    private readonly Action<string>? _requestCancelled;
    private readonly object _revisionGate = new();
    private string _encoding = "utf-16";
    private bool _shutdown;
    private bool _configurationSync;
    private bool _codeDescriptionSupport;
    // The Runic protocol revision every session speaks (experimental.runic.protocol). Revision 1, the behaviour of
    // Translations 0.13 for a client without initializationOptions.runicProtocol, was removed in 0.15 after its
    // one-minor transition (D069.3, D070.1, #263). runicProtocol is no longer read: any value, or none, gets revision 2.
    internal const int LatestProtocol = 2;
    // Client capabilities that the protocol uses when the client advertises them, and degrades without.
    private bool _applyEditSupport;
    private bool _watchedFilesRegistration;
    // Resource operations of the client's workspace edits, each on its own: Visual Studio 2026 applies `create` and
    // `rename` but not `delete` (SPIKE-VS-0).
    private bool _createFiles;
    private bool _deleteFiles;
    // Whether the client resolves code actions (textDocument.codeAction.resolveSupport). Visual Studio lists no `edit`
    // among the properties yet resolves it, so any resolveSupport counts; without it refactor actions carry their edit.
    private bool _codeActionResolve;
    // Server-to-client requests awaiting their response, by the JSON text of their ID. The reader thread completes
    // them, so the worker can wait for a response while the reader keeps reading.
    private readonly Dictionary<string, TaskCompletionSource<JsonObject>> _clientRequests = new(StringComparer.Ordinal);
    private long _nextClientRequest;
    private bool _readerStopped;
    // Whether the current request had the client apply an edit (workspace/applyEdit) or recorded a review.
    private bool _editApplied;
    // Review data per project and buffer group, for code actions and hover: the compilation with the open buffers, its
    // review compilation (TranslationReviewFile.ForReview), the fingerprints and the review file. Dropped when a buffer,
    // a watched file or the configuration changes, and when the review file's size or write time differs.
    private readonly Dictionary<string, ReviewView> _reviewViews = new(StringComparer.Ordinal);
    // Open buffers by file (FileKey). Clients spell one file's URI differently from .NET
    // (`file:///c%3A/...` and percent-encoded `+ @ ( ) !` in VS Code), so whether a file is
    // open, and which URI its publications use, is decided by its path, never by URI text.
    private readonly Dictionary<string, string> _bufferPaths = new(PathComparer);
    // Catalog diagnostics of project files that are not open buffers, per file. Several projects
    // can compile one file (shared source roots); each contributes its own diagnostics, and the
    // client receives their union, so one project's update or clear never erases another's.
    private readonly Dictionary<string, ProjectFile> _projectFiles = new(PathComparer);
    // The files each project contributes diagnostics to.
    private readonly Dictionary<string, HashSet<string>> _projectFileDiagnostics = new(PathComparer);
    // Diagnostics publications not yet sent, the latest per URI in the order they were made.
    // A publication computed while a newer state revision is queued is held back, not dropped:
    // the newest revision sends whatever it did not supersede, so no clear is ever lost.
    private readonly Dictionary<string, (long Sequence, JsonObject Params)> _outbox = new(StringComparer.Ordinal);
    private long _outboxSequence;
    // Whether each project uses the direct MF2 layout, which decides a file's locale. Recorded
    // whenever the project is loaded and dropped on project, configuration and watched-file
    // changes, so runic/documentInfo does not read the catalog from disk on every editor switch.
    private readonly Dictionary<string, bool> _projectLayouts = new(PathComparer);
    private const int MaximumPublishedProjectFiles = 2_000;
    private static readonly Regex DocumentedDiagnosticId = new("^RTR[0-9]{4}$", RegexOptions.CultureInvariant);
    private readonly List<string> _workspaceRoots = new();
    // Windows file systems are case-insensitive and clients disagree on drive
    // letter case (`file:///c%3A/...` versus `C:\...`). Project identity follows
    // the platform so one directory is never treated as two projects.
    internal static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly SortedSet<string> _diskProjectDirectories = new(PathComparer);
    private readonly SortedSet<string> _projectDirectories = new(PathComparer);
    // Disk source keys come from Path.GetFullPath (native separators) while
    // buffer keys come from URIs (portable separators). On Windows both the
    // separator and the case must be ignored or one file appears twice.
    internal static readonly IEqualityComparer<string> SourcePathComparer = new PlatformSourcePathComparer();
    // Failure categories already reported per open buffer (bounded by the
    // number of exception types, not by changing positions in messages).
    private readonly Dictionary<string, HashSet<string>> _reportedBufferFailures = new(StringComparer.Ordinal);
    // Project ownership per open buffer. Resolution depends on the buffer path,
    // open runic.json overlays, and the project index, so it is invalidated by
    // the buffer's own revision, by any configuration buffer change, and by
    // every project index refresh. Unrelated keystrokes reuse it.
    private readonly Dictionary<string, (string? Project, Exception? Error)> _bufferProjects = new(StringComparer.Ordinal);
    private const int MaximumProjectIndexEntries = 100_000;
    private bool _globalDiagnosticsRefreshPending;
    // Projects whose open buffers changed and that FlushDiagnostics has not validated yet: a burst of keystrokes is
    // validated once, at the state of its last change (#271).
    private readonly SortedSet<string> _pendingValidations = new(PathComparer);
    // Project validations run, for tests that check that queued changes are validated once.
    private int _validations;
    internal int Validations => Volatile.Read(ref _validations);
    private bool _projectIndexRescanPending;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    // settleDiagnostics: answer each request only after the diagnostics of every change before it (SettleValidations).
    internal Rmf2LanguageServer(Stream input, Stream output, Action<string>? beforeRequest = null,
        Action<string>? requestCancelled = null, bool settleDiagnostics = false)
    {
        _settleDiagnostics = settleDiagnostics;
        _input = input;
        _output = output;
        _beforeRequest = beforeRequest;
        _requestCancelled = requestCancelled;
    }
    private CancellationToken _requestCancellation;
    private readonly bool _settleDiagnostics;
    // The test-only environment variable that makes the stdio server settle diagnostics before each response.
    internal const string SettleDiagnosticsVariable = "RUNIC_TRANSLATIONS_LSP_SETTLE_DIAGNOSTICS";
    // Every state notification (didOpen, didChange, didClose, didChangeWatchedFiles, didChangeConfiguration) gets the
    // next state revision when it is queued; a request gets the revision of the state it is computed from.
    private long _latestRevision;
    private long _processedRevision;
    // What each queued state notification changes, by the revision that changed it last (under _revisionGate): the
    // buffer of a file (FileKey), the disk content of a resource file (a watched-file event), and the project as a
    // whole (runic.json, review files, restore and install state, directories, a rescan or the configuration). A
    // request whose result depends on none of these changes is answered, not refused with ContentModified (IsStale).
    // Entries the worker has passed are dropped before each message, so these hold only queued changes.
    private readonly Dictionary<string, long> _bufferChanges = new(PathComparer);
    private readonly Dictionary<string, long> _diskChanges = new(PathComparer);
    private long _projectChange;
    // The current request (worker only): its method and parameters, the roots of the workspaces it compiled, and
    // whether it built a WorkspaceEdit, which makes it depend on every file below those roots.
    private string _requestMethod = "";
    private JsonObject _requestParams = new();
    private readonly HashSet<string> _requestRoots = new(PathComparer);
    private bool _requestEdits;
    private sealed class ContentModifiedException : Exception { }
    private sealed class MethodNotFoundException(string method) : Exception("Unknown method '" + method + "'.") { }
    // The client answered a server-to-client request with an error.
    private sealed class ClientRequestFailedException(string message) : InvalidOperationException(message) { }
    // The requests this server answers. Any other request is answered with MethodNotFound.
    private static readonly HashSet<string> RequestMethods = new(StringComparer.Ordinal) {
        "initialize", "shutdown", "codeAction/resolve", "workspace/executeCommand",
        "runic/documentInfo", "runic/message", "runic/libraryCatalogs", "runic/libraryDocument",
        "textDocument/codeAction", "textDocument/semanticTokens/full", "textDocument/documentSymbol", "textDocument/foldingRange",
        "textDocument/formatting", "textDocument/hover", "textDocument/completion", "textDocument/definition",
        "textDocument/references", "textDocument/rename", "textDocument/prepareRename",
    };
    // InvalidParams for malformed parameters; RequestFailed (-32803) for a request that could not be carried out.
    private static int ErrorCode(Exception error) => error switch {
        ContentModifiedException => -32801,
        OperationCanceledException => -32800,
        MethodNotFoundException => -32601,
        ArgumentException or FormatException or OverflowException or KeyNotFoundException or System.Text.Json.JsonException => -32602,
        _ => -32803,
    };
    internal int Run()
    {
        // The reader remains responsive to cancellation while one worker owns document state.
        using var queue = new BlockingCollection<(JsonObject Request, CancellationTokenSource Cancellation, long Revision)>(256);
        var pending = new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        Task<int> worker = Task.Run(() => {
            foreach (var item in queue.GetConsumingEnumerable())
            {
                var request = item.Request; string method = request["method"]?.GetValue<string>() ?? "";
                JsonNode? id = request["id"]?.DeepClone();
                JsonObject parameters = request["params"] as JsonObject ?? new JsonObject();
                _requestCancellation = item.Cancellation.Token; _processedRevision = item.Revision; _editApplied = false;
                _requestMethod = method; _requestParams = parameters; _requestRoots.Clear(); _requestEdits = false;
                lock (_revisionGate) ForgetProcessedChanges(item.Revision);
                try
                {
                    _requestCancellation.ThrowIfCancellationRequested();
                    // Tests can coordinate the in-process worker through the
                    // internal constructor. The production stdio entry point
                    // never supplies a hook or exposes a barrier method.
                    _beforeRequest?.Invoke(method);
                    if (method == "exit") return _shutdown ? 0 : 1;
                    if (_settleDiagnostics && id is not null && method is not ("initialize" or "shutdown"))
                    {
                        SettleValidations();
                        // Their publications go out before the response, as when validation ran on the worker.
                        lock (_revisionGate) if (item.Revision == _latestRevision) SendPublications();
                    }
                    if (id is not null && !RequestMethods.Contains(method)) throw new MethodNotFoundException(method);
                    JsonNode? result = Handle(method, parameters);
                    lock (_revisionGate)
                    {
                        // Project discovery commits its replacement index under
                        // this same gate. Cancellation after that commit point
                        // is completion, not a cancelled partial transaction.
                        if (method != "initialize") _requestCancellation.ThrowIfCancellationRequested();
                        // An edit the client has applied (workspace/applyEdit), or a review the server has recorded,
                        // changes files; the revisions that follow are its own result, not a reason to discard it.
                        if (id is not null && !_editApplied && (parameters["textDocument"] is not null || method is "workspace/executeCommand" or "codeAction/resolve") && IsStale(item.Revision)) throw new ContentModifiedException();
                        if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
                    }
                }
                catch (Exception error) when (error is ContentModifiedException or OperationCanceledException or MethodNotFoundException or ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TranslationAuthoringException or KeyNotFoundException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or TranslationContractException or System.Text.Json.JsonException)
                {
                    if (id is not null) Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = ErrorCode(error), ["message"] = error is ContentModifiedException ? "Document content changed while the request was pending." : error is OperationCanceledException ? "Request cancelled." : error.Message } });
                }
                finally
                {
                    lock (_revisionGate)
                    {
                        if (id is not null) pending.Remove(id.ToJsonString());
                        item.Cancellation.Dispose();
                        // A newer state revision is queued: it sends these publications, or
                        // supersedes them, once it has been processed.
                        if (item.Revision == _latestRevision) SendPublications();
                    }
                }
            }
            return 0;
        });
        _enqueueValidated = () => Enqueue(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = ValidatedMethod, ["params"] = new JsonObject() }, ValidatedMethod);
        _enqueueWatchedFiles = changes => Enqueue(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "workspace/didChangeWatchedFiles", ["params"] = new JsonObject { ["changes"] = changes } }, "workspace/didChangeWatchedFiles");
        try
        {
            while (Read() is { } request)
            {
                // A message without a method is the client's response to a server-to-client request. It completes
                // the waiting request here and never reaches the worker, which may be the one waiting for it.
                if (request["method"] is null)
                {
                    CompleteClientRequest(request);
                    continue;
                }
                string method = request["method"]?.GetValue<string>() ?? "";
                if (method == "$/cancelRequest")
                {
                    string? target = request["params"]?["id"]?.ToJsonString();
                    bool observed = false;
                    lock (_revisionGate)
                    {
                        if (target is not null && pending.TryGetValue(target, out var cancellation))
                        {
                            cancellation.Cancel();
                            observed = true;
                        }
                    }
                    if (observed) _requestCancelled?.Invoke(target!);
                    continue;
                }
                Enqueue(request, method);
                if (method == "exit") break;
            }
        }
        finally
        {
            // No response can arrive any more: a request the worker is waiting on fails instead of hanging.
            FailClientRequests();
            StopFileWatching();
            StopValidator();
            lock (_enqueueGate) queue.CompleteAdding();
            worker.GetAwaiter().GetResult();
        }
        return worker.GetAwaiter().GetResult();

        // Queues a message for the worker with its state revision. The reader and the server's own file watcher
        // (StartFileWatching) both queue messages, so a revision is assigned and queued as one step: the queue stays
        // in revision order.
        void Enqueue(JsonObject request, string method)
        {
            var source = new CancellationTokenSource();
            lock (_enqueueGate)
            {
                if (queue.IsAddingCompleted) { source.Dispose(); throw new IOException("Language server stopped."); }
                long revision;
                lock (_revisionGate) {
                    if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose" or "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration")
                        RecordChange(method, request, ++_latestRevision);
                    revision = _latestRevision;
                    if (request["id"] is { } id) pending[id.ToJsonString()] = source;
                }
                while (!queue.TryAdd((request, source, revision), 100))
                    if (worker.IsCompleted) { source.Dispose(); worker.GetAwaiter().GetResult(); throw new IOException("Language server worker stopped."); }
            }
        }
    }

    // Sends a server-to-client request. The returned task completes with the client's response message.
    private Task<JsonObject> StartClientRequest(string method, JsonObject parameters, out long id)
    {
        id = Interlocked.Increment(ref _nextClientRequest);
        var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_clientRequests)
        {
            if (_readerStopped) completion.SetException(new IOException("The client closed the connection."));
            else _clientRequests[JsonValue.Create(id).ToJsonString()] = completion;
        }
        if (!completion.Task.IsCompleted)
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters });
        return completion.Task;
    }

    // Sends a server-to-client request and waits for its result while the current request runs. Cancelling the
    // current request cancels the wait and tells the client; a response that arrives later is ignored.
    private JsonNode? ClientRequest(string method, JsonObject parameters)
    {
        Task<JsonObject> response = StartClientRequest(method, parameters, out long id);
        try { response.Wait(_requestCancellation); }
        catch (OperationCanceledException)
        {
            bool pending;
            lock (_clientRequests) pending = _clientRequests.Remove(JsonValue.Create(id).ToJsonString());
            if (pending) Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "$/cancelRequest", ["params"] = new JsonObject { ["id"] = id } });
            throw;
        }
        catch (AggregateException failure) when (failure.InnerException is IOException closed) { throw closed; }
        JsonObject message = response.Result;
        if (message["error"] is JsonObject error)
            throw new ClientRequestFailedException($"The client failed {method}: {error["message"]?.GetValue<string>() ?? "no message"}");
        return message["result"];
    }

    private void CompleteClientRequest(JsonObject response)
    {
        // A response to a request this server no longer waits for (cancelled), or never sent, is dropped.
        string? key = response["id"]?.ToJsonString();
        TaskCompletionSource<JsonObject>? completion = null;
        if (key is not null) lock (_clientRequests) _clientRequests.Remove(key, out completion);
        completion?.TrySetResult(response);
    }

    private void FailClientRequests()
    {
        TaskCompletionSource<JsonObject>[] pending;
        lock (_clientRequests)
        {
            _readerStopped = true;
            pending = _clientRequests.Values.ToArray();
            _clientRequests.Clear();
        }
        foreach (var completion in pending) completion.TrySetException(new IOException("The client closed the connection."));
    }

    // A client that registers watchers dynamically gets them from the server, so it needs no watcher code of its own:
    // the manifests, the resources, the review files and the restore and install state that decides which library
    // catalogs resolve. A client without dynamic registration gets the server's own watcher (FileWatching.cs); events
    // it forwards itself are handled as well.
    internal static readonly string[] WatchedFilePatterns = [
        "**/runic.json", "**/*.{rmf2,mf2}", "**/*.review.json",
        "**/{obj/project.assets.json,node_modules/.package-lock.json,node_modules/.modules.yaml,node_modules/.yarn-state.yml,package-lock.json,pnpm-lock.yaml,yarn.lock,bun.lock,bun.lockb}",
    ];
    private void RegisterWatchedFiles()
    {
        var watchers = new JsonArray(WatchedFilePatterns.Select(pattern => (JsonNode)new JsonObject { ["globPattern"] = pattern }).ToArray());
        Task<JsonObject> response = StartClientRequest("client/registerCapability", new JsonObject { ["registrations"] = new JsonArray(new JsonObject {
            ["id"] = "runic.watchedFiles", ["method"] = "workspace/didChangeWatchedFiles", ["registerOptions"] = new JsonObject { ["watchers"] = watchers },
        }) }, out _);
        // Nothing waits for the answer. After a refusal the server watches the workspace itself.
        response.ContinueWith(task => {
            if (task.IsCompletedSuccessfully && task.Result["error"] is JsonObject error)
            {
                Console.Error.WriteLine("runic-rmf2: the client refused the watched-file registration (" + error["message"]?.GetValue<string>() + "); the server watches the workspace itself.");
                StartFileWatching();
                // Changes made before the watchers started would be missed: rescan once now.
                try { _enqueueWatchedFiles?.Invoke(new JsonArray()); }
                catch (Exception failure) when (failure is IOException or InvalidOperationException or ObjectDisposedException) { }
            }
        }, TaskScheduler.Default);
    }
    private JsonNode? Handle(string method, JsonObject args)
    {
        if (method is "textDocument/didOpen" or "textDocument/didChange" or "textDocument/didClose" or "textDocument/didSave" or
            "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration") _reviewViews.Clear();
        if (method is "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration") { _dependencies.Clear(); _sourceFiles.Clear(); _readWorkspaces.Clear(); }
        if (method == ValidatedMethod) { ApplyValidations(); return null; }
        if (method == "initialize")
        {
            _configurationSync = args["initializationOptions"]?["runicConfigurationSync"]?.GetValue<bool>() == true;
            _codeDescriptionSupport = args["capabilities"]?["textDocument"]?["publishDiagnostics"]?["codeDescriptionSupport"]?.GetValue<bool>() == true;
            if (args["workspaceFolders"] is JsonArray folders)
                foreach (var folder in folders) _workspaceRoots.Add(LocalPath(folder!["uri"]!.GetValue<string>()));
            else if (args["rootUri"] is JsonValue rootUri) _workspaceRoots.Add(LocalPath(rootUri.GetValue<string>()));
            RefreshProjectIndex(rescanWorkspace: true);
            var resourceOperations = args["capabilities"]?["workspace"]?["workspaceEdit"]?["resourceOperations"] as JsonArray;
            _createFiles = resourceOperations?.Any(v => v is JsonValue value && value.TryGetValue(out string? text) && text == "create") == true;
            _deleteFiles = resourceOperations?.Any(v => v is JsonValue value && value.TryGetValue(out string? text) && text == "delete") == true;
            var encodings = args["capabilities"]?["general"]?["positionEncodings"] as JsonArray;
            _encoding = encodings?.Select(v => v!.GetValue<string>()).FirstOrDefault(v => v is "utf-8" or "utf-16" or "utf-32") ?? "utf-16";
            JsonNode? clientCapabilities = args["capabilities"];
            _applyEditSupport = clientCapabilities?["workspace"]?["applyEdit"] is JsonValue applyEdit && applyEdit.TryGetValue(out bool canApply) && canApply;
            _codeActionResolve = clientCapabilities?["textDocument"]?["codeAction"]?["resolveSupport"] is JsonObject;
            _watchedFilesRegistration = clientCapabilities?["workspace"]?["didChangeWatchedFiles"]?["dynamicRegistration"] is JsonValue dynamic && dynamic.TryGetValue(out bool canRegister) && canRegister;
            return Capabilities();
        }
        if (method == "shutdown") { _shutdown = true; return null; }
        if (method == "initialized")
        {
            // A client that cannot register watchers gets the server's own (Visual Studio 2026 has no dynamic
            // didChangeWatchedFiles registration, SPIKE-VS-0).
            if (_watchedFilesRegistration) RegisterWatchedFiles(); else StartFileWatching();
            return null;
        }
        if (method == "$/cancelRequest") return null;
        if (method == "codeAction/resolve")
        {
            // A "mark reviewed" action is a command, complete as offered.
            if (args["data"] is null && args["command"] is JsonObject) return args.DeepClone();
            JsonNode data = args["data"] ?? throw new ArgumentException("Missing quick-fix revision evidence.");
            string actionUri = data["uri"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix document URI.");
            int actionVersion = data["version"]?.GetValue<int>() ?? throw new ArgumentException("Missing quick-fix document version.");
            string actionRevision = data["revision"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix source revision.");
            if (!_buffers.TryGetValue(actionUri, out Buffer? actionBuffer)) throw new ContentModifiedException();
            var source = new TranslationSource(LocalPath(actionUri), Utf8.GetBytes(actionBuffer.Text));
            if (actionBuffer.Version != actionVersion || Rmf2DiagnosticActions.Revision(source) != actionRevision)
                throw new ContentModifiedException();
            // Refactorings (Extract Group, Inline Resource) carry the same revision evidence as quick fixes.
            if (data["refactor"] is not null) return ResolveRefactor(args, actionUri, data);
            string fixId = data["fixId"]?.GetValue<string>() ?? throw new ArgumentException("Missing quick-fix ID.");
            Rmf2DiagnosticQuickFix? fix = Rmf2DiagnosticActions.GetQuickFixes(actionBuffer.Syntax, _requestCancellation)
                .Concat(ProjectQuickFixes(actionUri, actionBuffer, compile: true).Fixes).FirstOrDefault(candidate => candidate.Id == fixId);
            if (fix is null) throw new ContentModifiedException();
            return QuickFixAction(actionUri, actionBuffer, fix, args["isPreferred"]?.GetValue<bool>() == true);
        }
        if (method is "workspace/didChangeWatchedFiles" or "workspace/didChangeConfiguration")
        {
            // File watchers and configuration synchronization are notifications,
            // but they still invalidate diagnostics for every open RMF2 buffer.
            // Do this before looking for textDocument: workspace notifications
            // intentionally have no document field.
            QueueGlobalDiagnosticsRefresh(rescanWorkspace: method == "workspace/didChangeWatchedFiles");
            FlushDiagnostics();
            return null;
        }
        if (method == "runic/libraryCatalogs") return LibraryCatalogs(args);
        if (method == "runic/libraryDocument") return LibraryDocument(args);
        if (method == "workspace/executeCommand")
        {
            string command = args["command"]!.GetValue<string>();
            JsonArray arguments = args["arguments"]!.AsArray();
            string sourceUri = arguments[0]!.GetValue<string>(); string sourcePath = LocalPath(sourceUri);
            var workspace = Workspace(sourcePath);
            if (command == "runic.markReviewed")
                return MarkReviewed(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>());
            if (command is "runic.preview" or "runic.renderPreview")
            {
                string key = arguments[1]!.GetValue<string>(), locale = arguments[2]!.GetValue<string>();
                // A configured pseudo-locale (en-XA, ar-XB) is generated from the base locale by the same code as a
                // --pseudo-locales build, so the preview shows exactly what that build produces.
                Rmf2ProjectCompilation compilation = ConfiguredPseudoLocales(workspace).Contains(locale, StringComparer.Ordinal)
                    ? TranslationCompiler.CompileRmf2ProjectV5(workspace.ProjectSource, workspace.Sources, null, pseudoLocales: true, _requestCancellation)
                    : workspace.Validate();
                if (!compilation.Success) throw new TranslationAuthoringException(string.Join("; ", compilation.Diagnostics.Select(d => d.Message)));
                Rmf2ProjectV5 project = compilation.Project!;
                JsonNode message;
                JsonNode markup;
                JsonArray inputContract;
                // Optional: the requested locale (de-CH for de content) and the zone timeZone=local formats in.
                if (command == "runic.renderPreview")
                    return Rmf2Preview.Render(project, key, locale, arguments[3]!.AsObject(), OptionalArgument(arguments, 4), OptionalArgument(arguments, 5));
                JsonNode artifact = JsonNode.Parse(Rmf2LocaleArtifactV5.Render(project, locale).Text)!;
                message = artifact["messages"]?[key]?["ast"] ?? throw new TranslationAuthoringException("Unknown preview message.");
                markup = JsonNode.Parse(project.MarkupContract)!;
                Rmf2MessageContractV5 contract = project.CanonicalMessages.Concat(project.ExtraMessages).Single(item => item.Key == key);
                inputContract = new JsonArray(contract.Inputs.Select(input => (JsonNode)new JsonObject {
                    ["name"] = input.Name,
                    ["type"] = input.Type,
                }).ToArray());
                var examples = workspace.Documents.SelectMany(doc => doc.Nodes.Where(node => !node.IsGroup && string.Join('_', workspace.LogicalPath(doc.Source.Path, node)) == key))
                    .SelectMany(node => node.Properties.Where(value => value.StartsWith("example ", StringComparison.Ordinal)).Select(value => JsonNode.Parse(value.Substring(8))));
                return new JsonObject { ["key"] = key, ["locale"] = locale, ["ast"] = message.DeepClone(), ["inputs"] = inputContract, ["markup"] = markup, ["examples"] = new JsonArray(examples.ToArray()) };
            }
            // A command applies its edit through workspace/applyEdit and answers null; a client without applyEdit
            // receives the WorkspaceEdit.
            if (command == "runic.renameResource")
                return ApplyCommandEdit("Rename resource", WorkspaceEdit(workspace.Rename(arguments[1]!.AsArray().Select(value => value!.GetValue<string>()).ToArray(), arguments[2]!.GetValue<string>())));
            if (command == "runic.renameSlot")
                return ApplyCommandEdit("Rename slot", WorkspaceEdit(workspace.RenameSlot(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>())));
            if (command == "runic.renameInput")
                return ApplyCommandEdit("Rename input", WorkspaceEdit(workspace.RenameInput(sourcePath, arguments[1]!.GetValue<string>(), arguments[2]!.GetValue<string>(), arguments[3]!.GetValue<string>())));
            // A command needs only the operations it uses, and empties an inlined file for a client without `delete`
            // (Visual Studio 2026), as the Inline Resource code action does.
            if (!_createFiles) throw new InvalidOperationException("The client does not support the required file operations.");
            var plan = command switch {
                "runic.extractGroup" => workspace.Extract(sourcePath, arguments[1]!.AsArray().Select(v => v!.GetValue<string>()).ToArray()),
                "runic.inlineResource" => workspace.Inline(sourcePath, LocalPath(arguments[1]!.GetValue<string>())),
                _ => throw new ArgumentException("Unknown RMF2 workspace command."),
            };
            return ApplyCommandEdit(command == "runic.extractGroup" ? "Extract group" : "Inline resource", WorkspaceEdit(plan, emptyInsteadOfDelete: !_deleteFiles));
        }
        var document = args["textDocument"] as JsonObject;
        if (document is null) return null;
        string uri = document["uri"]!.GetValue<string>();
        if (method == "textDocument/didClose")
        {
            bool configuration = Path.GetFileName(LocalPath(uri)).Equals("runic.json", StringComparison.OrdinalIgnoreCase);
            try
            {
                string? closedProject = !configuration && _buffers.ContainsKey(uri) ? BufferProject(uri, out _) : null;
                _buffers.Remove(uri); _reportedBufferFailures.Remove(uri);
                string key = FileKey(LocalPath(uri));
                if (_bufferPaths.TryGetValue(key, out string? mapped) && mapped == uri)
                {
                    _bufferPaths.Remove(key);
                    // The client may still have the file open under another spelling of its URI.
                    string? other = _buffers.Keys.Order(StringComparer.Ordinal).FirstOrDefault(candidate => PathComparer.Equals(FileKey(LocalPath(candidate)), key));
                    if (other is not null) _bufferPaths[key] = other;
                }
                if (configuration) _bufferProjects.Clear(); else _bufferProjects.Remove(uri);
                if (configuration) _projectLayouts.Clear(); else if (closedProject is not null) _projectLayouts.Remove(closedProject);
                Publish(uri, new JsonArray());
                // Diagnostics other projects report for the saved file return to the client.
                PublishProjectFile(key);
                if (configuration) QueueGlobalDiagnosticsRefresh();
                else if (closedProject is not null)
                {
                    // The closed file is still a project file: while its project has open buffers,
                    // its saved content is validated again and its diagnostics stay in the Problems view.
                    _pendingValidations.Add(closedProject);
                    CancelValidation(closedProject);
                }
            }
            finally { FlushDiagnostics(); }
            return null;
        }
        if (method == "textDocument/didOpen")
        {
            try { Update(uri, document["text"]!.GetValue<string>(), document["version"]!.GetValue<int>()); }
            finally { FlushDiagnostics(); }
            return null;
        }
        if (!_buffers.TryGetValue(uri, out Buffer? buffer)) throw new InvalidOperationException("Document is not open.");
        if (method == "textDocument/didChange")
        {
            try
            {
                int version = document["version"]!.GetValue<int>();
                if (version <= buffer.Version) throw new InvalidOperationException("Stale document revision.");
                string text = buffer.Text;
                foreach (JsonNode? change in (JsonArray)args["contentChanges"]!)
                {
                    string replacement = change!["text"]!.GetValue<string>();
                    if (change["range"] is JsonObject range)
                    { int from = Offset(text, range["start"]!), to = Offset(text, range["end"]!); if (to < from) throw new ArgumentException("Invalid edit range."); text = text[..from] + replacement + text[to..]; }
                    else text = replacement;
                }
                Update(uri, text, version);
            }
            finally { FlushDiagnostics(); }
            return null;
        }
        if (method == "runic/documentInfo") return DocumentInfo(uri);
        if (Path.GetFileName(LocalPath(uri)) == "runic.json") return null;
        if (method == "textDocument/codeAction")
        {
            JsonArray? only = args["context"]?["only"] as JsonArray;
            JsonObject requestedRange = args["range"]?.AsObject() ?? throw new ArgumentException("Missing code-action range.");
            int from = Utf8.GetByteCount(buffer.Text.AsSpan(0, Offset(buffer.Text, requestedRange["start"]!)));
            int to = Utf8.GetByteCount(buffer.Text.AsSpan(0, Offset(buffer.Text, requestedRange["end"]!)));
            if (to < from) throw new ArgumentException("Invalid code-action range.");
            // Refactorings too; `only` selects kinds hierarchically (refactor covers refactor.extract).
            if (!RequestsKind(only, "quickfix")) return new JsonArray(RefactorActions(uri, buffer, from, to, only).ToArray());
            // A fix applies at its diagnostic and at the source it edits (RTR0078 is reported at the
            // message name, but the line break is further down).
            bool AtEdit(Rmf2DiagnosticQuickFix fix) => fix.StartByte <= to && from <= fix.StartByte + fix.LengthBytes;
            // Document repairs, then project repairs (RTR0095: add the missing plural variants) and, for a stale
            // review entry (RTR0090), "mark reviewed", which records the current source in the review file.
            (IReadOnlyList<Rmf2DiagnosticQuickFix> projectFixes, IReadOnlyList<TranslationReviewStaleEntry> stale, Rmf2ProjectV5? reviewed) = ProjectQuickFixes(uri, buffer, compile: false);
            Rmf2DiagnosticQuickFix[] fixes = Rmf2DiagnosticActions.GetQuickFixes(buffer.Syntax, _requestCancellation).Concat(projectFixes).Where(fix => fix.Location.StartByte <= to &&
                from <= fix.Location.StartByte + fix.Location.LengthBytes || AtEdit(fix)).ToArray();
            // Several fixes can share one diagnostic (one RTR0078 join per line break, all reported at
            // the message name). Only a fix at the requested source, or the only fix for its
            // diagnostic, is preferred, so a client never auto-applies an arbitrary one of them.
            var actions = new JsonArray(fixes.Select(fix => (JsonNode)QuickFixAction(uri, buffer, fix, AtEdit(fix) ||
                fixes.Count(other => other.DiagnosticId == fix.DiagnosticId && other.Location.StartByte == fix.Location.StartByte && other.Location.LengthBytes == fix.Location.LengthBytes) == 1)).ToArray());
            // The title names the message by its dotted path (notes.pinned), as RTR0090 and the CLI do; the command keeps the canonical key.
            IReadOnlyDictionary<string, IReadOnlyList<string>>? paths = null;
            foreach (TranslationReviewStaleEntry item in stale.Where(item => item.Location.StartByte <= to && from <= item.Location.StartByte + item.Location.LengthBytes))
            {
                paths ??= reviewed is not null ? Rmf2MessageKeys.Paths(reviewed) : new Dictionary<string, IReadOnlyList<string>>();
                string title = $"Mark the '{item.Entry.Locale}' translation of '{Rmf2MessageKeys.Display(paths, item.Entry.Key)}' reviewed ({TranslationReviewFile.StateText(item.Entry.State)})";
                actions.Add((JsonNode)new JsonObject { ["title"] = title, ["kind"] = "quickfix",
                    ["command"] = new JsonObject { ["title"] = title, ["command"] = "runic.markReviewed",
                        ["arguments"] = new JsonArray((JsonNode)JsonValue.Create(uri), JsonValue.Create(item.Entry.Key), JsonValue.Create(item.Entry.Locale)) } });
            }
            foreach (JsonNode refactor in RefactorActions(uri, buffer, from, to, only)) actions.Add(refactor);
            return actions;
        }
        if (method == "textDocument/semanticTokens/full") return SemanticTokens(buffer);
        if (method == "textDocument/documentSymbol")
            return new JsonArray(buffer.Syntax.Nodes.Select(node => {
                var symbol = new JsonObject {
                    ["name"] = node.Path[^1], ["detail"] = string.Join('.', node.Path), ["kind"] = node.IsGroup ? 3 : 13,
                    ["range"] = Range(buffer, node.Location), ["selectionRange"] = Range(buffer, node.NameLocation),
                };
                // Document messages list their headings as an outline (kind String, as in Markdown).
                if (node.MessageSyntax is { } messageSyntax)
                {
                    var headings = Rmf2DocumentSyntax.Blocks(messageSyntax).Where(block => block.Name == "runic:h").Select(block => {
                        string text = Rmf2DocumentSyntax.HeadingText(messageSyntax, block);
                        return (JsonNode)new JsonObject {
                            ["name"] = text.Length == 0 ? Rmf2DocumentSyntax.Describe(block) : text, ["detail"] = Rmf2DocumentSyntax.Describe(block), ["kind"] = 15,
                            ["range"] = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.End)),
                            ["selectionRange"] = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.ContentStart)),
                        };
                    }).ToArray();
                    if (headings.Length > 0) symbol["children"] = new JsonArray(headings);
                }
                return (JsonNode)symbol;
            }).ToArray());
        if (method == "textDocument/foldingRange")
        {
            var ranges = buffer.Syntax.Nodes.Where(n => n.Location.EndLine > n.NameLocation.Line).Select(node => (JsonNode)new JsonObject {
                ["startLine"] = node.NameLocation.Line - 1, ["endLine"] = node.Location.EndLine - (node.Location.EndColumn == 1 ? 2 : 1), ["kind"] = "region",
            }).ToList();
            // Blocks of document messages that span lines fold from their open tag to their close tag.
            foreach (Rmf2ResourceNode node in buffer.Syntax.Nodes.Where(n => n.MessageSyntax is not null))
                foreach (var block in Rmf2DocumentSyntax.Blocks(node.MessageSyntax!))
                {
                    int startLine = Range(buffer, MessageLocation(buffer, node, block.Open.Location.StartByte, block.ContentStart))["start"]!["line"]!.GetValue<int>();
                    int endLine = Range(buffer, MessageLocation(buffer, node, block.ContentEnd, block.End))["start"]!["line"]!.GetValue<int>();
                    if (endLine > startLine) ranges.Add(new JsonObject { ["startLine"] = startLine, ["endLine"] = endLine, ["kind"] = "region" });
                }
            return new JsonArray(ranges.ToArray());
        }
        if (method == "textDocument/formatting")
        {
            string formatted = Utf8.GetString(Rmf2ResourceWriter.Format(buffer.Syntax.Source));
            if (formatted == buffer.Text) return new JsonArray();
            return new JsonArray(new JsonObject { ["range"] = new JsonObject { ["start"] = Position(buffer.Text, 0), ["end"] = Position(buffer.Text, buffer.Text.Length) }, ["newText"] = formatted });
        }
        int offset = args["position"] is JsonNode position ? Offset(buffer.Text, position) : 0;
        int atByte = Utf8.GetByteCount(buffer.Text.AsSpan(0, offset));
        // Read requests find the message in the read workspace's document of the buffer, which Rmf2Workspace.Update has
        // just read: its nodes are the buffer's (Rmf2ResourceReader.Analyze is Read plus a second parse of every message
        // for diagnostics), so a request after typing parses the document once instead of twice (#271).
        Rmf2ResourceDocument syntax = method is "textDocument/hover" or "textDocument/completion" or "textDocument/definition" or
            "textDocument/references" or "runic/message" ? ReadWorkspaceDocument(LocalPath(uri), uri) ?? buffer.Syntax : buffer.Syntax;
        Rmf2ResourceNode? entry = syntax.Nodes.LastOrDefault(n => n.Location.StartByte <= atByte && atByte <= n.Location.StartByte + n.Location.LengthBytes);
        // An override source of a library catalog: completion, hover and definition come from the packed library.
        if (method is "textDocument/hover" or "textDocument/completion" or "textDocument/definition" && OverrideContextOf(uri) is { } library)
            return method switch
            {
                "textDocument/hover" => LibraryHover(library, entry),
                "textDocument/completion" => LibraryCompletion(library, entry, atByte),
                _ => LibraryDefinition(library, entry),
            };
        if (method == "runic/message")
        {
            if (entry is null) throw new TranslationAuthoringException("Place the cursor inside a resource or group.");
            string sourcePath = LocalPath(uri); var workspace = ReadWorkspace(sourcePath, uri);
            var locals = entry.MessageSyntax?.Declarations.Where(declaration => declaration.Kind == "local").Select(declaration => declaration.Name).ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>();
            var inputs = entry.MessageSyntax?.Tokens.Where(token => token.Kind == Mf2SyntaxTokenKind.Variable && !locals.Contains(token.Value)).Select(token => token.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            var slots = entry.MessageSyntax?.Expressions.SelectMany(expression => expression.Options.Where(option => option.Name == "ref" && option.Value is { Kind: not Mf2OperandKind.Variable })).Select(option => option.Value!.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal) ?? Enumerable.Empty<string>();
            return new JsonObject {
                ["key"] = string.Join('_', workspace.LogicalPath(sourcePath, entry)), ["localKey"] = entry.Key, ["isGroup"] = entry.IsGroup,
                ["logicalPath"] = new JsonArray(workspace.LogicalPath(sourcePath, entry).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["path"] = new JsonArray(entry.Path.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["locale"] = workspace.Locale(sourcePath),
                ["locales"] = new JsonArray(workspace.Documents.Select(document => workspace.Locale(document.Source.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                // runic.json pseudoLocales: generated, read-only preview locales.
                ["pseudoLocales"] = new JsonArray(ConfiguredPseudoLocales(workspace).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["inputs"] = new JsonArray(inputs.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["slots"] = new JsonArray(slots.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            };
        }
        // F2 on resources, MF2 locals and inputs, and slot references (Rmf2LanguageServer.Refactoring.cs).
        if (method == "textDocument/prepareRename") return PrepareRename(buffer, entry, atByte);
        if (method == "textDocument/rename") return StandardRename(uri, entry, atByte, args["newName"]!.GetValue<string>());
        if (method == "textDocument/hover")
        {
            if (entry is null) return null;
            string path = LocalPath(uri); var workspace = ReadWorkspace(path, uri);
            string key = string.Join('_', workspace.LogicalPath(path, entry));
            string summary = entry.MessageSyntax is { } messageSyntax ? workspace.LanguageService.Hover(messageSyntax, MessageOffset(entry, atByte)) ?? key : key;
            var contractLines = new List<string>();
            Rmf2ResourceNode? baseEntry = null;
            string? reviewState = null;
            string locale = workspace.Locale(path);
            if (!entry.IsGroup)
            {
                // Translator context: the base-locale source of the same logical message, read from
                // the workspace (open buffers included), so it is shown even when the catalog has errors.
                IReadOnlyList<string> logical = workspace.LogicalPath(path, entry);
                if (!string.Equals(locale, workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
                    baseEntry = workspace.Documents.Where(document => string.Equals(workspace.Locale(document.Source.Path), workspace.BaseLocale, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(document => document.Nodes.Where(node => !node.IsGroup && workspace.LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal)))
                        .FirstOrDefault();
            }
            string hoverProject = ProjectDirectory(path, allowExternalAncestor: true);
            // Hover never compiles: it reads the catalog's current review data, or while a newer validation runs in the
            // background the last one applied (#271). Before the first validation it shows the source alone.
            HoverFacts? facts = CurrentReviewView(hoverProject, BufferUris(hoverProject).ToArray())?.Hover ?? _lastHoverFacts.GetValueOrDefault(hoverProject);
            if (facts?.Review is { } review && !entry.IsGroup)
                reviewState = Rmf2ReviewFileState.Describe(review.BaseLocale, review.Fingerprints, review.File, review.Error, key, locale);
            if (facts is { Compiled: true } && !entry.IsGroup)
            {
                bool localeCompiled = facts.Locales.TryGetValue(locale, out var localeFacts);
                Rmf2MessageContractV5? contract = facts.Contracts.GetValueOrDefault(key);
                if (localeCompiled && localeFacts.ContentLocales.TryGetValue(key, out string? contentLocale)) contractLines.Add("Content locale: " + contentLocale);
                if (contract is { Inputs.Count: > 0 }) contractLines.Add("Inputs: " + string.Join(", ", contract.Inputs.Select(input => "$" + input.Name + ": " + input.Type)));
                if (contract is not null) contractLines.Add("Content: " + contract.Content);
                // The locked block structure translations must keep, one skeleton per distinct source variant.
                if (contract is { Content: Rmf2DocumentProfileV5.Document })
                    contractLines.Add("Structure: " + string.Join(" | ", contract.Skeletons.Select(skeleton => skeleton.Length == 0 ? "(empty)" : skeleton)));
                if (localeCompiled && localeFacts.Fallback is { } fallback) contractLines.Add("Fallback: " + fallback);
            }
            string content = new Rmf2HoverContent {
                Summary = summary, BaseLocale = workspace.BaseLocale, BaseText = baseEntry?.Message,
                BaseComments = baseEntry?.Comments ?? [],
                BaseParameters = baseEntry?.Properties.Where(property => property.StartsWith("param ", StringComparison.Ordinal)).ToArray() ?? [],
                ReviewState = reviewState, Contract = contractLines, Comments = entry.Comments, Properties = entry.Properties,
            }.Render();
            return new JsonObject { ["contents"] = new JsonObject { ["kind"] = "plaintext", ["value"] = content } };
        }
        if (method is "textDocument/definition" or "textDocument/references" && entry?.MessageSyntax is { } symbolSyntax)
        {
            int messageByte = MessageOffset(entry, atByte);
            string? symbol = symbolSyntax.Declarations.Select(d => d.Name)
                .Concat(symbolSyntax.Tokens.Where(t => t.Kind == Mf2SyntaxTokenKind.Variable).Select(t => t.Value))
                .Distinct(StringComparer.Ordinal).FirstOrDefault(name => symbolSyntax.VariableReferences(name).Any(location => location.StartByte <= messageByte && messageByte < location.StartByte + location.LengthBytes));
            if (symbol is not null)
            {
                bool includeDeclaration = args["context"]?["includeDeclaration"]?.GetValue<bool>() ?? true;
                var references = ReadWorkspace(LocalPath(uri), uri).VariableReferences(LocalPath(uri), entry.Key, symbol);
                var selected = references.Where(reference => method == "textDocument/definition" ? reference.IsDeclaration : includeDeclaration || !reference.IsDeclaration);
                return new JsonArray(selected.Select(reference => {
                    var bytes = reference.Document.Source.GetUtf8Bytes();
                    string text = Utf8.GetString(bytes);
                    return (JsonNode)new JsonObject { ["uri"] = ClientUri(reference.Document.Source.Path),
                        ["range"] = new JsonObject {
                            ["start"] = Position(text, Utf8.GetCharCount(bytes.AsSpan(0, reference.Resource.MessageByteMap[reference.Location.StartByte]))),
                            ["end"] = Position(text, Utf8.GetCharCount(bytes.AsSpan(0, reference.Resource.MessageByteMap[reference.Location.StartByte + reference.Location.LengthBytes]))),
                        } };
                }).ToArray());
            }
            if (method == "textDocument/references") return new JsonArray();
        }
        if (method == "textDocument/definition")
        {
            if (entry is null) return new JsonArray();
            string path = LocalPath(uri); var workspace = ReadWorkspace(path, uri); var logical = workspace.LogicalPath(path, entry);
            return new JsonArray(workspace.Documents.SelectMany(document => document.Nodes.Where(node => workspace.LogicalPath(document.Source.Path, node).SequenceEqual(logical, StringComparer.Ordinal))
                .Select(node => (JsonNode)new JsonObject { ["uri"] = ClientUri(document.Source.Path), ["range"] = Range(new Buffer(Utf8.GetString(document.Source.GetUtf8Bytes()), 0, document.Source.Path, () => document), node.NameLocation) })).ToArray());
        }
        if (method == "textDocument/completion")
        {
            var labels = new HashSet<string>(StringComparer.Ordinal) { "one", "two", "few", "many", "zero", "*" };
            string sourcePath = LocalPath(uri); var workspace = ReadWorkspace(sourcePath, uri);
            var completions = entry is { IsGroup: false } ? workspace.Complete(sourcePath, entry.Key, MessageOffset(entry, atByte)) : workspace.LanguageService.Complete(null, 0);
            var items = completions.ToDictionary(item => item.Label, item => item.Detail, StringComparer.Ordinal);
            foreach (string label in labels) items.TryAdd(label, "Runic semantic translation contract");
            return new JsonArray(items.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => (JsonNode)new JsonObject { ["label"] = item.Key, ["detail"] = item.Value, ["kind"] = 14 }).ToArray());
        }
        return null;
    }
    internal static readonly string[] WorkspaceCommands = ["runic.extractGroup", "runic.inlineResource", "runic.markReviewed", "runic.preview", "runic.renameInput", "runic.renameSlot", "runic.renameResource", "runic.renderPreview"];
    internal static readonly string[] RunicRequests = ["runic/documentInfo", "runic/libraryCatalogs", "runic/libraryDocument", "runic/message"];

    // experimental.runic: the protocol revision the server speaks and the Runic commands and requests it answers.
    private static JsonObject RunicCapability() => new() {
        ["protocol"] = LatestProtocol,
        ["commands"] = new JsonArray(WorkspaceCommands.Select(command => (JsonNode)JsonValue.Create(command)).ToArray()),
        ["requests"] = new JsonArray(RunicRequests.Select(request => (JsonNode)JsonValue.Create(request)).ToArray()),
    };

    // The server's capabilities. serverInfo.version is the tool's version (Translations release).
    private JsonObject Capabilities() => new() {
        ["capabilities"] = new JsonObject {
            ["positionEncoding"] = _encoding, ["textDocumentSync"] = 2, ["documentSymbolProvider"] = true,
            ["semanticTokensProvider"] = new JsonObject { ["legend"] = new JsonObject { ["tokenTypes"] = new JsonArray("namespace", "property", "variable", "function", "keyword", "string", "comment", "type"), ["tokenModifiers"] = new JsonArray() }, ["full"] = true },
            ["referencesProvider"] = true, ["foldingRangeProvider"] = true, ["hoverProvider"] = true, ["definitionProvider"] = true,
            ["documentFormattingProvider"] = true, ["renameProvider"] = new JsonObject { ["prepareProvider"] = true },
            ["codeActionProvider"] = new JsonObject { ["codeActionKinds"] = new JsonArray("quickfix", "refactor.extract", "refactor.inline"), ["resolveProvider"] = true },
            ["executeCommandProvider"] = new JsonObject { ["commands"] = new JsonArray(WorkspaceCommands.Select(command => (JsonNode)JsonValue.Create(command)).ToArray()) },
            ["completionProvider"] = new JsonObject { ["triggerCharacters"] = new JsonArray("$", ":", "#", "/", "=") },
            ["experimental"] = new JsonObject { ["runic"] = RunicCapability() },
        },
        ["serverInfo"] = new JsonObject { ["name"] = "Runic RMF2", ["version"] = Program.Version },
    };

    // The owning catalog and the file's locale, for a client's status display. A file that no
    // project owns, or a project whose manifest cannot be read yet, answers with null members
    // rather than an error: the client shows the state, not a failure.
    private JsonObject DocumentInfo(string uri)
    {
        string path = LocalPath(uri);
        bool configuration = Path.GetFileName(path).Equals("runic.json", StringComparison.OrdinalIgnoreCase);
        string? project = configuration ? Path.GetDirectoryName(Path.GetFullPath(path)) : BufferProject(uri, out _);
        var result = new JsonObject { ["project"] = null, ["catalog"] = null, ["baseLocale"] = null, ["locale"] = null };
        if (project is null) return result;
        string manifestPath = Path.Combine(project, "runic.json");
        result["project"] = ClientUri(manifestPath);
        try
        {
            byte[] bytes = TryGetBuffer(manifestPath, out _, out Buffer? manifestBuffer) ? Utf8.GetBytes(manifestBuffer.Text) : File.ReadAllBytes(manifestPath);
            // As the compiler reads runic.json: a BOM is skipped, and a duplicate member is a diagnostic, not an exception.
            if (TranslationCompiler.ReadProjectManifest(new TranslationSource(manifestPath, bytes), out _) is { } manifest)
            {
                if (manifest.Id.Length != 0) result["catalog"] = manifest.Id;
                if (manifest.DefaultLocale.Length != 0) result["baseLocale"] = manifest.DefaultLocale;
            }
            if (!configuration && Dependencies(project) is { } dependencies && dependencies.OverrideOf(path) is { } target)
            {
                // An override source belongs to the library catalog it overrides; its file name is its locale.
                result["catalog"] = target.Catalog;
                result["library"] = target.Dependency?.Display;
                result["baseLocale"] = null;
                string file = Path.GetFileName(path);
                result["locale"] = file.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) ? file[..^5] : null;
                return result;
            }
            if (!configuration)
            {
                // The locale depends only on the path and the project's layout. The layout is
                // recorded whenever the project is validated, which every edit already does.
                if (!_projectLayouts.TryGetValue(project, out bool direct)) direct = Workspace(project, BufferUris(project).ToArray()).IsDirect;
                result["locale"] = Rmf2Workspace.Locale(path, direct);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException or TranslationAuthoringException or ToolUsageException or ToolDiagnosticException) { }
        return result;
    }

    // Physical source location of a message byte range.
    private static TextSourceLocation MessageLocation(Buffer buffer, Rmf2ResourceNode node, int from, int to)
    {
        int last = node.MessageByteMap.Count - 1;
        int start = node.MessageByteMap[Math.Clamp(from, 0, last)], end = node.MessageByteMap[Math.Clamp(to, 0, last)];
        return new TextSourceLocation(buffer.Syntax.Source.Path, start, Math.Max(0, end - start), 0, 0, 0, 0);
    }
    private static int MessageOffset(Rmf2ResourceNode entry, int physicalByte)
    {
        if (entry.MessageByteMap.Count == 0 || physicalByte < entry.MessageByteMap[0]) return -1;
        int index = 0;
        while (index + 1 < entry.MessageByteMap.Count && entry.MessageByteMap[index + 1] <= physicalByte) index++;
        return index;
    }
    // Native application-language services own call sites. Never return a partial F2 edit unasked. With allowMixed, a
    // workspace whose only problem is that it contains application sources returns true instead of refusing, so the
    // caller can ask the user; every other refusal still throws.
    private bool RequireResourceOnlyWorkspace(string path, bool allowMixed = false)
    {
        string root = _workspaceRoots.Where(value => IsWithin(value, path)).OrderByDescending(value => value.Length).FirstOrDefault()
            ?? throw new TranslationAuthoringException("Rename requires an explicit workspace root. Use Rename Resource in Resource Sources for an intentionally bounded edit.");
        var directories = new Stack<string>(); directories.Push(root);
        int visited = 0;
        while (directories.Count > 0)
        {
            _requestCancellation.ThrowIfCancellationRequested();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                if (++visited > 100_000) throw new TranslationAuthoringException("Workspace is too large to establish safe rename scope. Use Rename Resource in Resource Sources explicitly.");
                if (Path.GetFileName(entry) is ".git" or "node_modules" or "bin" or "obj" or "dist" or "artifacts" or ".cache" or ".direnv" or ".vs") continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new TranslationAuthoringException("Rename refused: linked workspace sources cannot be safely indexed. Use Rename Resource in Resource Sources explicitly.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (Path.GetFileName(entry) is not (".git" or "node_modules" or "bin" or "obj" or "dist" or "artifacts" or ".cache" or ".direnv" or ".vs")) directories.Push(entry);
                }
                else if (Path.GetExtension(entry).ToLowerInvariant() is ".cs" or ".ts" or ".tsx" or ".js" or ".jsx" or ".svelte" or ".resx" or ".razor" or ".xaml" or ".cpp" or ".h" or ".mf2")
                {
                    if (allowMixed) return true;
                    throw new TranslationAuthoringException(MixedWorkspaceRefusal);
                }
            }
        }
        return false;
    }
    private const string MixedWorkspaceRefusal = "Rename refused: this workspace contains application or legacy sources whose references Runic cannot safely rewrite. Use the native language refactor, or explicitly choose Rename Resource in Resource Sources and update call sites separately.";
    private static bool IsWithin(string root, string path) => PathContainment.IsWithin(root, path);
    // TranslationSource uses portable separators, including on Windows. Buffer overlays
    // must use the same key as disk sources or the workspace receives duplicates.
    private static string LocalPath(string uri) => new Uri(uri).LocalPath.Replace('\\', '/');
    // One key per file, whatever URI spelling or relative form names it; compared with PathComparer.
    internal static string FileKey(string path) => Path.GetFullPath(path).Replace('\\', '/');

    // The open buffer of a file, found by path rather than by the URI text the client chose.
    private bool TryGetBuffer(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? uri,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Buffer? buffer)
    {
        buffer = null;
        return _bufferPaths.TryGetValue(FileKey(path), out uri) && _buffers.TryGetValue(uri, out buffer);
    }

    // The URI the client uses for an open file; .NET's form for a closed one.
    private string ClientUri(string path) => TryGetBuffer(path, out string? uri, out _) ? uri : new Uri(path).AbsoluteUri;

    private void RefreshProjectIndex(bool rescanWorkspace)
    {
        _bufferProjects.Clear();
        _projectLayouts.Clear();
        if (rescanWorkspace)
            ReplaceProjectIndex(_workspaceRoots, _diskProjectDirectories, MaximumProjectIndexEntries, _revisionGate, null, _requestCancellation);
        _projectDirectories.Clear();
        _projectDirectories.UnionWith(_diskProjectDirectories);
        // Unsaved new configurations participate only when they are contained
        // by an opened workspace and do not cross a linked directory boundary.
        foreach (string path in _buffers.Keys.Select(LocalPath)
            .Where(path => Path.GetFileName(path).Equals("runic.json", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal))
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            if (IsSafeWorkspacePath(path) && (!PathEntryExists(path) || IsRegularManifestFile(path))) _projectDirectories.Add(directory);
        }
    }

    internal static void ReplaceProjectIndex(
        IEnumerable<string> workspaceRoots,
        SortedSet<string> destination,
        int entryLimit,
        object commitGate,
        Action<string>? entryObserved,
        CancellationToken cancellationToken)
    {
        var discovered = new SortedSet<string>(PathComparer);
        int visited = 0;
        foreach (string root in workspaceRoots.Select(Path.GetFullPath).Distinct(PathComparer).Order(PathComparer))
        {
            if (!Directory.Exists(root)) continue;
            var pending = new SortedSet<string>(PathComparer) { root };
            if (++visited > entryLimit)
                throw new InvalidOperationException($"Workspace project discovery exceeds the bounded entry limit of {entryLimit}.");
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pending.Min!;
                pending.Remove(directory);
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > entryLimit)
                        throw new InvalidOperationException($"Workspace project discovery exceeds the bounded entry limit of {entryLimit}.");
                    FileAttributes attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        if ((attributes & FileAttributes.Directory) == 0)
                        {
                            if (Path.GetFileName(entry).Equals("runic.json", StringComparison.Ordinal))
                                discovered.Add(directory);
                        }
                        else
                        {
                            string name = Path.GetFileName(entry);
                            // The same skipped directories as the Translations Editor's catalog discovery.
                            if (!TranslationProjectDiscovery.IsSkipped(name))
                                pending.Add(entry);
                        }
                    }
                    entryObserved?.Invoke(entry);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }
        // Publish a new index only after a complete scan; cancellation or a
        // bounded-scan failure must not leave a partially replaced mapping.
        lock (commitGate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            destination.Clear();
            destination.UnionWith(discovered);
        }
    }

    private static bool PathEntryExists(string path) => TryGetPathAttributes(path, out _);

    private static bool TryGetPathAttributes(string path, out FileAttributes attributes)
    {
        try
        {
            attributes = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { attributes = default; return false; }
        catch (DirectoryNotFoundException) { attributes = default; return false; }
    }

    private static bool IsRegularManifestFile(string path)
    {
        if (!TryGetPathAttributes(path, out FileAttributes attributes)) return false;
        return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
    }

    private bool HasUsableProjectManifest(string directory)
    {
        string path = Path.Combine(directory, "runic.json");
        if (IsRegularManifestFile(path)) return true;
        return !PathEntryExists(path) && TryGetBuffer(path, out _, out _);
    }

    private bool IsSafeWorkspacePath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        foreach (string root in _workspaceRoots.Select(Path.GetFullPath).OrderByDescending(value => value.Length))
        {
            if (!IsWithin(root, fullPath)) continue;
            string? current = fullPath;
            while (current is not null && IsWithin(root, current))
            {
                if (TryGetPathAttributes(current, out FileAttributes attributes) &&
                    (attributes & FileAttributes.ReparsePoint) != 0) return false;
                if (string.Equals(current, root, StringComparison.Ordinal)) break;
                current = Path.GetDirectoryName(current);
            }
            return true;
        }
        return false;
    }

    private string ProjectDirectory(string path, bool allowExternalAncestor = false)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null)
        {
            if (_projectDirectories.Contains(directory) && HasUsableProjectManifest(directory)) return directory;
            if (allowExternalAncestor && IsRegularManifestFile(Path.Combine(directory, "runic.json"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }

        // A mounted resource need not have its project in its ancestor chain.
        // Resolve it against the bounded set of conventional workspace projects
        // and open configuration buffers. Prefer the most-specific matching
        // source root, then the canonical project path for deterministic ties.
        string[] candidates = _projectDirectories.Where(HasUsableProjectManifest).ToArray();
        var matches = new List<(string Project, int Specificity)>();
        foreach (string candidate in candidates)
            foreach (string sourceRoot in ProjectSourceRoots(candidate).Where(IsSafeWorkspacePath))
                if (IsWithin(sourceRoot, fullPath)) matches.Add((candidate, Path.GetFullPath(sourceRoot).Length));
        if (matches.Count != 0)
            return matches.OrderByDescending(match => match.Specificity).ThenBy(match => match.Project, StringComparer.Ordinal).First().Project;
        throw new InvalidOperationException("No runic.json project contains this resource in its project directory or configured source roots.");
    }

    private string BufferProjectDirectory(string path)
    {
        if (!IsSafeWorkspacePath(path))
            throw new InvalidOperationException("Open translation buffers must remain inside an explicit workspace boundary.");
        // Direct ancestors are authoritative for nested projects. The index is
        // still required to associate a sibling mounted resource with a closed
        // manifest elsewhere in the workspace.
        string fullPath = Path.GetFullPath(path);
        string root = _workspaceRoots.Select(Path.GetFullPath).Where(candidate => IsWithin(candidate, fullPath))
            .OrderByDescending(candidate => candidate.Length).First();
        string? directory = Path.GetDirectoryName(fullPath);
        while (directory is not null && IsWithin(root, directory))
        {
            string config = Path.Combine(directory, "runic.json");
            bool buffered = TryGetBuffer(config, out _, out _) && (!PathEntryExists(config) || IsRegularManifestFile(config));
            if (IsRegularManifestFile(config) || buffered) return directory;
            if (string.Equals(directory, root, StringComparison.Ordinal)) break;
            directory = Path.GetDirectoryName(directory);
        }
        return ProjectDirectory(path);
    }

    private bool IsInsideWorkspaceRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return _workspaceRoots.Any(root => IsWithin(Path.GetFullPath(root), fullPath));
    }

    // Resolves the project that owns an open buffer for overlays and diagnostics.
    // Buffers inside an explicit workspace root use the strict boundary rules
    // first, including the refusal of linked paths. When no project owns a safe
    // in-root buffer (a root opened on a feature folder below its runic.json),
    // or the buffer lies outside every root (Visual Studio loose-file mode, or
    // a root that does not contain the file), the buffer uses the same nearest
    // regular runic.json ancestor rule as explicit commands, so it joins exactly
    // the project a preview loads from disk. The workspace overlays buffers of
    // such an external project only onto sources it already reads from disk.
    private string ResolveBufferProjectDirectory(string path)
    {
        if (!IsInsideWorkspaceRoot(path)) return ProjectDirectory(path, allowExternalAncestor: true);
        try { return BufferProjectDirectory(path); }
        catch (InvalidOperationException) when (IsSafeWorkspacePath(path))
        {
            return ProjectDirectory(path, allowExternalAncestor: true);
        }
    }

    private string? BufferProject(string uri, out Exception? error)
    {
        if (!_bufferProjects.TryGetValue(uri, out var entry))
        {
            try { entry = (ResolveBufferProjectDirectory(LocalPath(uri)), null); }
            catch (Exception failure) when (failure is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                entry = (null, failure);
            }
            _bufferProjects[uri] = entry;
        }
        error = entry.Error;
        return entry.Project;
    }

    private void ReportBufferFailure(string uri, Exception error)
    {
        // stdout is the LSP channel. Report each failure category once per open
        // buffer on stderr instead of once per keystroke; messages can carry
        // changing source positions, so they are not part of the key.
        if (!_reportedBufferFailures.TryGetValue(uri, out HashSet<string>? reported))
            _reportedBufferFailures[uri] = reported = new HashSet<string>(StringComparer.Ordinal);
        if (reported.Add(error.GetType().FullName ?? error.GetType().Name))
            Console.Error.WriteLine($"runic-rmf2: open buffer {uri} has no project overlay or catalog diagnostics: {error.Message}");
    }

    // Mounted-resource ownership reads the manifest through the same reader as
    // the command-line tool and the Translations Editor, so a buffer belongs to
    // exactly the project whose compilation discovers it. A manifest whose
    // source layout is unusable (including one bad entry while a mount is being
    // typed) contributes no mounts: its mounted buffers publish only their own
    // syntax diagnostics until it is fixed, while the open runic.json, which
    // still resolves through its directory, carries the located configuration
    // diagnostic. Buffers beneath the project directory resolve through the
    // ancestor rule, so no implicit-directory fallback is needed here.
    private string[] ProjectSourceRoots(string directory)
    {
        string projectPath = Path.Combine(directory, "runic.json");
        if (PathEntryExists(projectPath) && !IsRegularManifestFile(projectPath)) return [];
        byte[] bytes;
        if (TryGetBuffer(projectPath, out _, out Buffer? projectBuffer)) bytes = Utf8.GetBytes(projectBuffer.Text);
        else
        {
            try { bytes = File.ReadAllBytes(projectPath); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
        }
        TranslationManifestLayout layout = TranslationManifestReader.Read(bytes, directory);
        return layout.IsValid ? layout.SourceRoots.ToArray() : [];
    }

    private Rmf2Workspace Workspace(string path)
    {
        // Explicit language operations may target a complete project outside
        // the opened workspace (for example an inert preview fixture). Such a
        // project is loaded directly but never added to the watched index.
        string directory = ProjectDirectory(path, allowExternalAncestor: true);
        // The command resolved its project from disk just now. A runic.json may
        // have appeared without a watched-file notification (common in loose-file
        // mode), so cached buffer ownership is re-resolved against the same disk
        // state. Commands are rare; keystrokes keep using the cache.
        _bufferProjects.Clear();
        string[] group = BufferUris(directory).ToArray();
        return Workspace(directory, group);
    }

    // An unsaved buffer under translations/overrides belongs to a library catalog's overrides, not to the project.
    private static bool OverridesDeclared(CompilerInputs inputs) =>
        TranslationManifestReader.Read(inputs.Project.GetUtf8Bytes(), Path.GetDirectoryName(Path.GetFullPath(inputs.Project.Path))!).OverridesRoot is not null;

    private Rmf2Workspace Workspace(string directory, IReadOnlyCollection<string> group)
    {
        _requestCancellation.ThrowIfCancellationRequested();
        (Rmf2Workspace workspace, string root) = BuildWorkspace(SnapshotOf(directory, group), _requestCancellation);
        // A request that edits or locates across files depends on every file below this root (IsStale).
        _requestRoots.Add(root);
        _projectLayouts[directory] = workspace.IsDirect;
        return workspace;
    }

    // The workspace of read requests (hover, completion, definition, references, runic/message) per project, kept while the
    // project's saved files and set of open buffers stay as they were, with a changed buffer updated in place
    // (Rmf2Workspace.Update re-reads that file only). Building one walks the project and compares every source with the
    // syntax cache: 6-28 ms per request at 20,000 messages x 10 locales, most of a hover or a completion (#271). It is
    // built without a request's cancellation, since it outlives the request; requests that edit build their own.
    // Watched-file events, configuration changes and runic.json buffers drop it.
    private sealed class ReadWorkspaceEntry(Rmf2Workspace workspace, string root, string group, Dictionary<string, int> versions)
    {
        internal Rmf2Workspace Workspace { get; } = workspace;
        internal string Root { get; } = root;
        internal string Group { get; } = group;
        internal Dictionary<string, int> Versions { get; } = versions;
    }
    private readonly Dictionary<string, ReadWorkspaceEntry> _readWorkspaces = new(PathComparer);

    private Rmf2Workspace ReadWorkspace(string path, string uri)
    {
        if (BufferProject(uri, out _) is not { } project) return Workspace(path);
        string[] group = BufferUris(project).ToArray();
        string groupKey = string.Join('\n', group);
        if (_readWorkspaces.TryGetValue(project, out ReadWorkspaceEntry? entry) && entry.Group == groupKey && UpdateReadWorkspace(entry, group))
        {
            _requestRoots.Add(entry.Root);
            return entry.Workspace;
        }
        _requestCancellation.ThrowIfCancellationRequested();
        (Rmf2Workspace workspace, string root) = BuildWorkspace(SnapshotOf(project, group), CancellationToken.None);
        _projectLayouts[project] = workspace.IsDirect;
        _readWorkspaces[project] = new ReadWorkspaceEntry(workspace, root, groupKey, group.ToDictionary(item => item, item => _buffers[item].Version, StringComparer.Ordinal));
        _requestRoots.Add(root);
        return workspace;
    }

    // The read workspace's document of an open buffer in a project, at the buffer's text; null when the workspace does not
    // compile the buffer (a loose buffer, a library override source), whose own syntax the caller reads instead.
    private Rmf2ResourceDocument? ReadWorkspaceDocument(string path, string uri)
    {
        if (BufferProject(uri, out _) is null) return null;
        // BuildWorkspace keys a buffer's source by its LocalPath, so the document's path is the buffer's.
        return ReadWorkspace(path, uri).Documents.FirstOrDefault(document => string.Equals(document.Source.Path, path, StringComparison.Ordinal));
    }

    // Brings a kept workspace to the buffers' current text; false when one cannot be updated in place (it is rebuilt).
    private bool UpdateReadWorkspace(ReadWorkspaceEntry entry, string[] group)
    {
        foreach (string uri in group)
        {
            Buffer buffer = _buffers[uri];
            if (entry.Versions.GetValueOrDefault(uri, -1) == buffer.Version) continue;
            string file = LocalPath(uri);
            try { entry.Workspace.Update(file, Utf8.GetBytes(buffer.Text), entry.Workspace.Revision(file)); }
            // A buffer the workspace does not compile (outside the project's sources) has no source to update.
            catch (KeyNotFoundException) { return false; }
            entry.Versions[uri] = buffer.Version;
        }
        return true;
    }

    // A project's open buffers as one immutable state: what a workspace is built from, on the worker or off it.
    private sealed record WorkspaceSnapshot(string Directory, Buffer? ProjectBuffer, (string Path, Buffer Buffer)[] Buffers);

    private WorkspaceSnapshot SnapshotOf(string directory, IReadOnlyCollection<string> group)
    {
        TryGetBuffer(Path.Combine(directory, "runic.json"), out _, out Buffer? projectBuffer);
        return new WorkspaceSnapshot(directory, projectBuffer, group.Select(uri => (LocalPath(uri), _buffers[uri])).ToArray());
    }

    // Builds a project's workspace from a snapshot and the files on disk, and returns the root that contains its sources.
    // It reads only the snapshot, the disk, _workspaceRoots (set once, in initialize) and the locked syntax cache, so the
    // background validator runs it too (Rmf2LanguageServer.Validation.cs).
    private (Rmf2Workspace Workspace, string Root) BuildWorkspace(WorkspaceSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = snapshot.Directory;
        string projectPath = Path.Combine(directory, "runic.json");
        if (!IsRegularManifestFile(projectPath))
            throw new InvalidOperationException("Translation project manifests must be regular non-linked runic.json files.");
        Buffer? projectBuffer = snapshot.ProjectBuffer;
        CompilerInputs inputs = InputFiles.ReadProject(directory, projectBuffer is null ? null : new TranslationSource(projectPath, Utf8.GetBytes(projectBuffer.Text)), _sourceFiles);
        var sources = inputs.Messages.Select(s => new TranslationSource(Path.GetFullPath(s.Path), s.GetUtf8Bytes())).ToDictionary(s => s.Path, SourcePathComparer);
        // A project outside every workspace root was reached through the
        // explicit-command ancestor rule, not through the bounded index.
        bool externalProject = !IsInsideWorkspaceRoot(directory);
        foreach ((string file, Buffer buffer) in snapshot.Buffers)
        {
            if (externalProject || !IsInsideWorkspaceRoot(file))
            {
                // Buffers of an external project, and loose buffers outside
                // every root, may only replace a source that this project
                // already reads from disk. They never add a file that the
                // preview would not otherwise compile.
                if (sources.ContainsKey(file)) sources[file] = new TranslationSource(file, Utf8.GetBytes(buffer.Text));
                continue;
            }
            // A new unsaved resource joins the project only where discovery
            // would find it on disk: beneath a configured source root, or the
            // project directory when no roots are configured. ReadProject
            // reports the project directory as the implicit root and returns
            // no roots only for a malformed configuration.
            if (sources.ContainsKey(file) || ((Path.GetExtension(file).Equals(".rmf2", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(file).Equals(".mf2", StringComparison.OrdinalIgnoreCase)) &&
                (inputs.SourceRoots is { } sourceRoots ? sourceRoots.Any(sourceRoot => IsWithin(sourceRoot, file)) : IsWithin(directory, file)) &&
                !(inputs.OverrideSources is not null && IsWithin(Path.Combine(directory, "overrides"), file) && OverridesDeclared(inputs))))
                sources[file] = new TranslationSource(file, Utf8.GetBytes(buffer.Text));
        }
        string root = directory;
        foreach (string file in sources.Keys)
            while (!IsWithin(root, file)) root = Path.GetDirectoryName(root)!;
        return (_syntaxCache.Create(root, new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes()), sources.Values, cancellationToken), root);
    }

    private IEnumerable<string> BufferUris(string projectDirectory)
    {
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            string? candidate = BufferProject(uri, out Exception? error);
            if (error is not null) ReportBufferFailure(uri, error);
            if (candidate is not null && PathComparer.Equals(candidate, projectDirectory)) yield return uri;
        }
    }
    // emptyInsteadOfDelete: a deleted file is emptied by a text edit instead, for a client without the `delete`
    // resource operation (Inline Resource in Visual Studio 2026). Nothing is lost if the client applies only
    // part of the edit, and the empty resource contributes nothing to the catalog until the user deletes it.
    private JsonObject WorkspaceEdit(TranslationWorkspaceTransactionPlan plan, bool emptyInsteadOfDelete = false)
    {
        if (!_configurationSync && plan.Edits.Any(edit => Path.GetFileName(edit.RelativePath) == "runic.json"))
            throw new TranslationAuthoringException("Refactor refused: this client does not synchronize unsaved runic.json buffers. Use the Translations Editor or VS Code for configuration-changing transactions.");
        _requestRoots.Add(plan.Root); _requestEdits = true;
        var edits = new JsonArray();
        foreach (var edit in plan.Edits)
        {
            string path = Path.GetFullPath(edit.RelativePath, plan.Root);
            // An open document is edited under the URI the client opened it with.
            TryGetBuffer(path, out string? openUri, out Buffer? buffer);
            string uri = openUri ?? new Uri(path).AbsoluteUri;
            if (edit.Kind == TranslationWorkspaceEditKind.Create) edits.Add((JsonNode)new JsonObject { ["kind"] = "create", ["uri"] = uri });
            if (edit.Kind == TranslationWorkspaceEditKind.Delete && !emptyInsteadOfDelete) { edits.Add((JsonNode)new JsonObject { ["kind"] = "delete", ["uri"] = uri }); continue; }
            string before = edit.Kind == TranslationWorkspaceEditKind.Create ? "" : buffer?.Text ?? File.ReadAllText(path, Utf8);
            string after = edit.Kind == TranslationWorkspaceEditKind.Delete ? "" : Utf8.GetString(edit.GetUtf8Bytes()!);
            edits.Add((JsonNode)new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = buffer is null ? null : JsonValue.Create(buffer.Version) },
                ["edits"] = new JsonArray(new JsonObject { ["range"] = new JsonObject { ["start"] = Position(before, 0), ["end"] = Position(before, before.Length) }, ["newText"] = after }),
            });
        }
        return new JsonObject { ["documentChanges"] = edits };
    }

    private void Update(string uri, string text, int version)
    {
        if (Utf8.GetByteCount(text) > 8 * 1024 * 1024) throw new ArgumentException("Document exceeds RMF2 byte limit.");
        if (!_buffers.ContainsKey(uri) && _buffers.Count >= 256) throw new ArgumentException("Too many open resource buffers.");
        bool configuration = Path.GetFileName(LocalPath(uri)) == "runic.json";
        string extension = Path.GetExtension(LocalPath(uri));
        if (!configuration && !extension.Equals(".rmf2", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".mf2", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The Runic language server supports .mf2, .rmf2, and runic.json synchronization; use the native service for application sources.");
        string path = LocalPath(uri);
        bool direct = extension.Equals(".mf2", StringComparison.OrdinalIgnoreCase);
        // Parsed when a request or a publication first needs it: a burst of keystrokes parses the document once (#271).
        var buffer = new Buffer(text, version, path, () => {
            var source = new TranslationSource(path, configuration ? Array.Empty<byte>() : Utf8.GetBytes(text));
            return direct ? Rmf2ResourceReader.AnalyzeDirect(source) : Rmf2ResourceReader.Analyze(source);
        });
        _buffers[uri] = buffer;
        _bufferPaths[FileKey(LocalPath(uri))] = uri;
        if (configuration)
        {
            // A configuration overlay can add, remove, or remount source roots.
            // Rebuild ownership before validating every resulting project group;
            // otherwise buffers from the old mapping retain stale diagnostics.
            _bufferProjects.Clear();
            _projectLayouts.Clear();
            _readWorkspaces.Clear();
            QueueGlobalDiagnosticsRefresh();
            return;
        }
        _bufferProjects.Remove(uri);
        string? project = BufferProject(uri, out Exception? unresolved);
        if (project is null)
        {
            // Without a resolvable project only the buffer's own syntax
            // diagnostics are published; the reason is logged, not swallowed.
            ReportBufferFailure(uri, unresolved!);
            PublishDiagnostics(Array.Empty<TranslationDiagnostic>(), [uri], null);
            return;
        }
        _pendingValidations.Add(project);
        CancelValidation(project);
    }

    private void QueueGlobalDiagnosticsRefresh(bool rescanWorkspace = false)
    {
        // Every project is validated again once the refresh runs.
        CancelValidation(null);
        _globalDiagnosticsRefreshPending = true;
        _projectIndexRescanPending |= rescanWorkspace;
    }

    // Validates what the processed state changes left pending, once no newer state change is queued: a burst of
    // keystrokes, watcher events or configuration changes is validated once, at the state of its last change, instead
    // of once per change (#271). Waiting loses nothing: a validation made while a newer revision is queued would only
    // have its publications held in the outbox until that revision is processed (Run). Every state notification ends
    // here, so the newest one always validates. A validation that fails stays pending for the next change.
    private void FlushDiagnostics()
    {
        if (_processedRevision != Interlocked.Read(ref _latestRevision)) return;
        if (_globalDiagnosticsRefreshPending)
        {
            bool rescanWorkspace = _projectIndexRescanPending;
            // The refresh validates every project with open buffers and clears those without.
            RefreshDiagnostics(rescanWorkspace);
            if (rescanWorkspace) _projectIndexRescanPending = false;
            _globalDiagnosticsRefreshPending = false;
            _pendingValidations.Clear();
            return;
        }
        foreach (string project in _pendingValidations.ToArray())
        {
            string[] group = BufferUris(project).ToArray();
            // A project whose last buffer closed meanwhile no longer owns diagnostics in the client.
            if (group.Length > 0) ScheduleValidation(project, group);
            else ClearProjectFileDiagnostics(project);
            _pendingValidations.Remove(project);
        }
    }

    private void RefreshDiagnostics(bool rescanWorkspace)
    {
        RefreshProjectIndex(rescanWorkspace);
        var groups = new SortedDictionary<string, List<string>>(PathComparer);
        var unresolved = new List<string>();
        foreach (string uri in _buffers.Keys.Order(StringComparer.Ordinal))
        {
            string? project = BufferProject(uri, out Exception? error);
            if (project is null) { ReportBufferFailure(uri, error!); unresolved.Add(uri); continue; }
            if (!groups.TryGetValue(project, out List<string>? group)) groups.Add(project, group = []);
            group.Add(uri);
        }
        // A project without open buffers no longer owns diagnostics in the client.
        foreach (string project in _projectFileDiagnostics.Keys.Where(project => !groups.ContainsKey(project)).ToArray())
            ClearProjectFileDiagnostics(project);
        foreach (var group in groups) ScheduleValidation(group.Key, group.Value.ToArray());
        // An unresolved buffer has no project whose catalog diagnostics can be
        // attributed safely. Publish only its own syntax diagnostics.
        foreach (string uri in unresolved) PublishDiagnostics(Array.Empty<TranslationDiagnostic>(), [uri], null);
    }

    private void PublishDiagnostics(IReadOnlyList<TranslationDiagnostic> catalogDiagnostics, IEnumerable<string> uris, string? project)
    {
        // Closed files first: a file that has just been opened withdraws its closed-file
        // publication here, before its buffer publication below, so a client that maps both
        // URI spellings to one resource never applies the withdrawal after the buffer's diagnostics.
        if (project is not null) PublishProjectFileDiagnostics(project, catalogDiagnostics);
        foreach (string uri in uris)
        {
            if (!_buffers.TryGetValue(uri, out Buffer? current)) continue;
            string path = FileKey(LocalPath(uri));
            PublishProjectFile(path);
            var diagnostics = current.Syntax.Diagnostics.Concat(catalogDiagnostics.Where(d => PathComparer.Equals(DiagnosticPath(d), path)))
                .DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message));
            Publish(uri, new JsonArray(diagnostics.Select(d => (JsonNode)Diagnostic(d, Range(current, d.Location))).ToArray()));
        }
    }

    // Catalog diagnostics located in project files that are not open (a translation that lacks a
    // message, a closed runic.json) are published from the saved file, so the Problems view shows
    // the whole catalog rather than only the open buffers.
    private void PublishProjectFileDiagnostics(string project, IReadOnlyList<TranslationDiagnostic> catalogDiagnostics)
    {
        var files = catalogDiagnostics.Where(d => !string.IsNullOrEmpty(d.Location.Path))
            .GroupBy(DiagnosticPath, PathComparer)
            .Where(file => !_bufferPaths.ContainsKey(file.Key))
            .OrderBy(file => file.Key, StringComparer.Ordinal)
            .Take(MaximumPublishedProjectFiles);
        if (!_projectFileDiagnostics.TryGetValue(project, out HashSet<string>? contributed))
            _projectFileDiagnostics[project] = contributed = new HashSet<string>(PathComparer);
        var current = new HashSet<string>(PathComparer);
        foreach (var file in files)
        {
            _requestCancellation.ThrowIfCancellationRequested();
            var located = file.DistinctBy(d => (d.Id, d.Location.StartByte, d.Location.LengthBytes, d.Message)).ToArray();
            // Keystrokes in another file revalidate the project; a closed file is read again only
            // when its diagnostics or its saved content changed since the last publication.
            string? stamp = FileStamp(file.Key);
            if (stamp is null) continue;
            string fingerprint = stamp + "\n" + string.Join("\n", located.Select(d => string.Join('\u001f', d.Id, d.Severity, d.Location.StartByte, d.Location.LengthBytes, d.Message)));
            if (_projectFiles.TryGetValue(file.Key, out ProjectFile? entry) && entry.Contributions.TryGetValue(project, out var previous) && previous.Fingerprint == fingerprint)
            {
                current.Add(file.Key);
                continue;
            }
            if (!TryReadProjectFile(file.Key, out string text, out TranslationSource? encoded)) continue;
            var payload = new JsonArray(located.Select(d => (JsonNode)Diagnostic(d, encoded is null ? EmptyRange() : Range(text, encoded, d.Location))).ToArray());
            if (entry is null) _projectFiles[file.Key] = entry = new ProjectFile();
            entry.Contributions[project] = (fingerprint, payload);
            current.Add(file.Key); contributed.Add(file.Key);
            PublishProjectFile(file.Key);
        }
        foreach (string file in contributed.Where(file => !current.Contains(file)).ToArray())
            RemoveProjectFileContribution(project, contributed, file);
        if (contributed.Count == 0) _projectFileDiagnostics.Remove(project);
    }

    private void ClearProjectFileDiagnostics(string project)
    {
        if (!_projectFileDiagnostics.Remove(project, out HashSet<string>? contributed)) return;
        foreach (string file in contributed.ToArray()) RemoveProjectFileContribution(project, contributed, file);
    }

    private void RemoveProjectFileContribution(string project, HashSet<string> contributed, string file)
    {
        contributed.Remove(file);
        if (_projectFiles.TryGetValue(file, out ProjectFile? entry) && entry.Contributions.Remove(project)) PublishProjectFile(file);
    }

    // Publishes the union of every project's diagnostics for a closed file, or withdraws the
    // closed-file publication when the file is open or no project reports it any more.
    private void PublishProjectFile(string file)
    {
        if (!_projectFiles.TryGetValue(file, out ProjectFile? entry)) return;
        bool open = _bufferPaths.TryGetValue(file, out string? bufferUri);
        if (open || entry.Contributions.Count == 0)
        {
            // An open file's diagnostics come from its buffer under the client's URI. Clearing
            // the same URI would erase them; another spelling of it is cleared before the
            // buffer is published (PublishDiagnostics publishes closed files first).
            if (entry.SentUri is { } sent && sent != bufferUri) Publish(sent, new JsonArray());
            entry.SentUri = null;
            if (entry.Contributions.Count == 0) _projectFiles.Remove(file);
            return;
        }
        entry.SentUri ??= new Uri(file).AbsoluteUri;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var union = new JsonArray();
        foreach (var contribution in entry.Contributions.OrderBy(item => item.Key, StringComparer.Ordinal))
            foreach (JsonNode? diagnostic in contribution.Value.Payload)
                if (seen.Add(diagnostic!.ToJsonString())) union.Add(diagnostic.DeepClone());
        Publish(entry.SentUri, union);
    }

    private static string? FileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || (info.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return null;
            return info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    // Reads a closed project file for diagnostic ranges. A linked or unreadable file is skipped;
    // a file that is not valid UTF-8 (RTR0001) is published at its start.
    private static bool TryReadProjectFile(string path, out string text, out TranslationSource? encoded)
    {
        text = ""; encoded = null;
        if (!TryGetPathAttributes(path, out FileAttributes attributes) || (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0) return false;
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
        try { text = Utf8.GetString(bytes); encoded = new TranslationSource(path, bytes); }
        catch (DecoderFallbackException) { text = ""; encoded = null; }
        return true;
    }

    private static string DiagnosticPath(TranslationDiagnostic diagnostic) => FileKey(diagnostic.Location.Path);

    private JsonObject Diagnostic(TranslationDiagnostic diagnostic, JsonObject range)
    {
        var result = new JsonObject {
            ["range"] = range, ["severity"] = diagnostic.Severity switch { TranslationDiagnosticSeverity.Error => 1, TranslationDiagnosticSeverity.Warning => 2, _ => 3 },
            ["code"] = diagnostic.Id, ["source"] = "runic-rmf2", ["message"] = diagnostic.Message,
        };
        // Every RTR ID has an entry in docs/guides/translations/diagnostics.md; the link names
        // the release tag of this tool, as the C# generator's help links do.
        if (_codeDescriptionSupport && DocumentedDiagnosticId.IsMatch(diagnostic.Id))
            result["codeDescription"] = new JsonObject { ["href"] = RunicReleaseLinks.Diagnostic(diagnostic.Id) };
        return result;
    }

    private IReadOnlyList<TranslationDiagnostic> ConfigDiagnostics(TranslationSource project)
        => TranslationCompiler.CompileRmf2ProjectV5(project, Array.Empty<TranslationSource>(), null, _requestCancellation).Diagnostics;
    // Queues a publication; the latest per URI replaces an earlier unsent one and moves to the end.
    private void Publish(string uri, JsonArray diagnostics) =>
        _outbox[uri] = (++_outboxSequence, new JsonObject { ["uri"] = uri, ["version"] = _buffers.TryGetValue(uri, out var buffer) ? JsonValue.Create(buffer.Version) : null, ["diagnostics"] = diagnostics });

    // Caller holds _revisionGate and has processed the latest state revision.
    private void SendPublications()
    {
        foreach (var publication in _outbox.Values.OrderBy(item => item.Sequence))
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "textDocument/publishDiagnostics", ["params"] = publication.Params });
        _outbox.Clear();
    }
    private JsonObject SemanticTokens(Buffer buffer)
    {
        var spans = new List<(int Start, int End, int Kind)>();
        foreach (var node in buffer.Syntax.Nodes)
        {
            spans.Add((node.NameLocation.StartByte, node.NameLocation.StartByte + node.NameLocation.LengthBytes, node.IsGroup ? 0 : 1));
            if (node.MessageSyntax is not { } syntax) continue;
            foreach (var token in syntax.Tokens)
            {
                int kind = token.Kind switch { Mf2SyntaxTokenKind.Variable => 2, Mf2SyntaxTokenKind.Function => 3, Mf2SyntaxTokenKind.Literal => 5, Mf2SyntaxTokenKind.Name => 1, Mf2SyntaxTokenKind.Attribute => 1, Mf2SyntaxTokenKind.MarkupOpen or Mf2SyntaxTokenKind.MarkupClose => 7, _ => -1 };
                if (kind >= 0 && token.Location.LengthBytes > 0)
                    spans.Add((node.MessageByteMap[token.Location.StartByte], node.MessageByteMap[token.Location.StartByte + token.Location.LengthBytes], kind));
            }
        }
        byte[] bytes = buffer.Syntax.Source.GetUtf8Bytes(); var data = new JsonArray(); int previousLine = 0, previousCharacter = 0;
        foreach (var span in spans.OrderBy(span => span.Start))
        {
            _requestCancellation.ThrowIfCancellationRequested();
            int start = Utf8.GetCharCount(bytes.AsSpan(0, span.Start)), end = Utf8.GetCharCount(bytes.AsSpan(0, span.End));
            while (start < end)
            {
                int newline = buffer.Text.IndexOf('\n', start); int finish = newline < 0 ? end : Math.Min(end, newline);
                if (finish > start && buffer.Text[finish - 1] == '\r') finish--;
                if (finish > start)
                {
                    var from = Position(buffer.Text, start); var to = Position(buffer.Text, finish);
                    int line = from["line"]!.GetValue<int>(), character = from["character"]!.GetValue<int>();
                    data.Add((JsonNode)JsonValue.Create(line - previousLine)); data.Add((JsonNode)JsonValue.Create(line == previousLine ? character - previousCharacter : character));
                    data.Add((JsonNode)JsonValue.Create(to["character"]!.GetValue<int>() - character)); data.Add((JsonNode)JsonValue.Create(span.Kind)); data.Add((JsonNode)JsonValue.Create(0));
                    previousLine = line; previousCharacter = character;
                }
                if (newline < 0 || newline >= end) break;
                start = newline + 1;
            }
        }
        return new JsonObject { ["data"] = data, ["resultId"] = buffer.Version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
    }
    private JsonObject Range(Buffer buffer, TextSourceLocation location) => Range(buffer.Text, buffer.Encoded, location);
    private JsonObject Range(string text, TranslationSource encoded, TextSourceLocation location)
    {
        Rmf2DiagnosticSpan span = Rmf2DiagnosticActions.GetSpan(encoded, location);
        return new JsonObject { ["start"] = Position(text, span.StartUtf16), ["end"] = Position(text, span.StartUtf16 + span.LengthUtf16) };
    }
    private static JsonObject EmptyRange() => new() {
        ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 }, ["end"] = new JsonObject { ["line"] = 0, ["character"] = 0 } };
    // The review compilation and its source fingerprints are computed on first use (#271): a catalog that does not
    // compile costs a second compilation for them (TranslationReviewFile.ForReview), which only a review file, hover's
    // review state, "Mark reviewed" and the review quick fixes need.
    private sealed class ReviewView(string stamp, Rmf2ProjectCompilation compilation, Func<Rmf2ProjectCompilation> review, string? reviewPath,
        TranslationInterchangeReview? file, string? error, TranslationInterchangeException? readFailure)
    {
        private readonly Lazy<(Rmf2ProjectCompilation Review, IReadOnlyDictionary<string, string>? Fingerprints)> _review = new(() => {
            Rmf2ProjectCompilation compiled = review();
            return (compiled, compiled is { Success: true, Project: not null } ? TranslationInterchange.ComputeSourceFingerprints(compiled) : null);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
        internal string Stamp { get; } = stamp;
        internal Rmf2ProjectCompilation Compilation { get; } = compilation;
        internal Rmf2ProjectCompilation Review => _review.Value.Review;
        internal IReadOnlyDictionary<string, string>? Fingerprints => _review.Value.Fingerprints;
        internal string? ReviewPath { get; } = reviewPath;
        private HoverFacts? _hover;
        // What hover shows from this validation (HoverFacts); the validator computes it, a request on first use.
        internal HoverFacts Hover => _hover ??= HoverFacts.Of(this);
        internal TranslationInterchangeReview? File { get; } = file;
        internal string? Error { get; } = error;
        internal TranslationInterchangeException? ReadFailure { get; } = readFailure;
        // Whether a review file exists, readable or not: only then does the review compilation matter for diagnostics.
        internal bool HasReviewFile => File is not null || Error is not null;
    }

    // The review data of a project compiled with the buffers of group, cached (see _reviewViews). The review compilation
    // is the compilation itself when it succeeded, else the same sources without validation policies, else the base
    // locale alone, so RTR0090, hover's review state and "Mark reviewed" work while translations are incomplete.
    private ReviewView ReviewViewOf(string project, IReadOnlyCollection<string> group, Rmf2Workspace? workspace = null)
    {
        if (CurrentReviewView(project, group) is { } cached) return cached;
        workspace ??= Workspace(project, group);
        ReviewView view = BuildReviewView(project, workspace, _requestCancellation);
        _reviewViews[ReviewCacheKey(project, group)] = view;
        return view;
    }

    // What hover shows from a validation: the compiled contracts, the content locale of each message per locale, the
    // fallback locales and the review state. Kept per project while a newer validation runs, instead of the whole
    // compilation, so the server holds one compiled catalog at a time (#271).
    private sealed class HoverFacts
    {
        private HoverFacts() { }
        internal bool Compiled { get; private init; }
        internal Dictionary<string, Rmf2MessageContractV5> Contracts { get; private init; } = new();
        internal Dictionary<string, (string? Fallback, Dictionary<string, string> ContentLocales)> Locales { get; private init; } = new();
        internal (string? BaseLocale, IReadOnlyDictionary<string, string>? Fingerprints, TranslationInterchangeReview? File, string? Error)? Review { get; private init; }

        internal static HoverFacts Of(ReviewView view)
        {
            Rmf2ProjectV5? project = view.Compilation.Success ? view.Compilation.Project : null;
            var contracts = new Dictionary<string, Rmf2MessageContractV5>(StringComparer.Ordinal);
            var locales = new Dictionary<string, (string? Fallback, Dictionary<string, string> ContentLocales)>(StringComparer.Ordinal);
            if (project is not null)
            {
                foreach (Rmf2MessageContractV5 contract in project.CanonicalMessages.Concat(project.ExtraMessages)) contracts.TryAdd(contract.Key, contract);
                foreach (Rmf2LocaleV5 locale in project.Locales)
                {
                    var contentLocales = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (Rmf2TranslationV5 resource in locale.ResolvedResources) contentLocales.TryAdd(resource.Key, resource.ContentLocale);
                    locales.TryAdd(locale.Tag, (locale.FallbackTag, contentLocales));
                }
            }
            return new HoverFacts {
                Compiled = project is not null, Contracts = contracts, Locales = locales,
                Review = view.HasReviewFile ? (view.Review.Project?.DefaultLocale, view.Fingerprints, view.File, view.Error) : null,
            };
        }
    }

    private static string ReviewCacheKey(string project, IReadOnlyCollection<string> group) =>
        project + "\n" + string.Join("\n", group.Order(StringComparer.Ordinal));

    // The review data of the project with exactly these buffers in their current state, when a validation or a request
    // has computed it since the last change, else null.
    private ReviewView? CurrentReviewView(string project, IReadOnlyCollection<string> group) =>
        _reviewViews.TryGetValue(ReviewCacheKey(project, group), out ReviewView? cached) && cached.Stamp == ReviewStamp(cached.ReviewPath) ? cached : null;

    // Compiles a workspace and reads its review file. It touches no server state, so the background validator runs it.
    private static ReviewView BuildReviewView(string project, Rmf2Workspace workspace, CancellationToken cancellationToken)
    {
        Rmf2ProjectCompilation compilation = workspace.Validate();
        TranslationSource projectSource = workspace.ProjectSource;
        IReadOnlyList<TranslationSource> sources = workspace.Sources.ToArray();
        // The review compiles of a catalog that does not compile (TranslationReviewFile.ForReview) reuse the session's file
        // units and memos (#276), instead of two full compiles.
        TranslationValidationSession? session = workspace.Session;
        // The catalog names the review file. runic.json has it even when the catalog does not compile.
        string? catalog = compilation.Project?.Id ?? (TranslationCompiler.ReadProjectManifest(projectSource, out _)?.Id is { Length: > 0 } id ? id : null);
        string? path = catalog is not null && TranslationReviewFile.IsFileName(catalog) ? TranslationReviewFile.PathFor(Path.Combine(project, "runic.json"), catalog) : null;
        string stamp = ReviewStamp(path);
        TranslationInterchangeReview? file = null;
        TranslationInterchangeException? failure = null;
        string? error = null;
        if (path is not null)
        {
            try { file = TranslationReviewFile.Read(path, catalog!); }
            catch (TranslationInterchangeException exception) { failure = exception; error = exception.Message; }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { error = exception.Message; }
        }
        Func<Rmf2ProjectCompilation> review;
        if (file is not null || error is not null)
        {
            // A review file's stale entries are diagnostics (RTR0090): compile for review now, cancellable, off the worker.
            Rmf2ProjectCompilation computed = TranslationReviewFile.ForReview(compilation, projectSource, sources, session, null, cancellationToken);
            review = () => computed;
        }
        // Without a review file only a request asks for it, later, on the worker; the validation's cancellation source is
        // disposed by then.
        else review = () => TranslationReviewFile.ForReview(compilation, projectSource, sources, session, null, CancellationToken.None);
        var view = new ReviewView(stamp, compilation, review, path, file, error, failure);
        if (view.HasReviewFile) _ = view.Fingerprints;
        return view;
    }

    private static string ReviewStamp(string? path)
    {
        if (path is null) return "-";
        var info = new FileInfo(path);
        return info.Exists ? info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
    }

    // The review file's warnings beside the compiler's: RTR0090 for each stale entry, RTR0091 when the file cannot be read
    // (the same rules and file as runic-translations validate, through Tooling).
    private static IReadOnlyList<TranslationDiagnostic> WithReviewDiagnostics(ReviewView view)
    {
        IReadOnlyList<TranslationDiagnostic> diagnostics = view.Compilation.Diagnostics;
        if (view.ReadFailure is not null && view.ReviewPath is not null) return [.. diagnostics, TranslationReviewFile.InvalidDiagnostic(view.ReviewPath, view.ReadFailure)];
        if (view.File is null || view.Fingerprints is null) return diagnostics;
        return [.. diagnostics, .. TranslationReviewFile.StaleDiagnostics(view.Review, view.File)];
    }

    // The project repairs for one open buffer and its stale review entries whose "Mark reviewed" can run now (the files
    // with the key's base message and translation are saved), from its project compiled with the open buffers.
    // The listing (compile: false) offers them only from the current validation, so it never waits for a compilation;
    // the client asks again when the validation publishes its diagnostics. Resolving an action (compile: true) needs the
    // exact edit, so it compiles when the validation of the current state has not finished.
    private (IReadOnlyList<Rmf2DiagnosticQuickFix> Fixes, IReadOnlyList<TranslationReviewStaleEntry> Stale, Rmf2ProjectV5? Reviewed) ProjectQuickFixes(string uri, Buffer buffer, bool compile)
    {
        string? project = BufferProject(uri, out _);
        if (project is null) return ([], [], null);
        try
        {
            string[] group = BufferUris(project).ToArray();
            if ((compile ? ReviewViewOf(project, group) : CurrentReviewView(project, group)) is not { } view) return ([], [], null);
            string path = LocalPath(uri);
            var source = new TranslationSource(path, Utf8.GetBytes(buffer.Text));
            IReadOnlyList<Rmf2DiagnosticQuickFix> fixes = Rmf2DiagnosticActions.GetProjectQuickFixes(view.Compilation, source, _requestCancellation);
            TranslationReviewStaleEntry[] stale = [];
            if (view.File is { } review && view.Fingerprints is not null)
                stale = TranslationReviewFile.Stale(view.Review, review).Where(entry => PathComparer.Equals(FileKey(entry.Location.Path), FileKey(path)) &&
                    UnsavedReviewFiles(project, view.Review.Project, null, entry.Entry.Key, entry.Entry.Locale).Count == 0).ToArray();
            return (fixes, stale, stale.Length > 0 ? view.Review.Project : null);
        }
        catch (Exception error) when (error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or InvalidOperationException or IOException or FormatException or TranslationInterchangeException or TranslationAuthoringException or System.Text.Json.JsonException)
        {
            return ([], [], null);
        }
    }

    // The open buffers, among the files with the key's base message and its translation, whose text differs from the
    // file on disk. Where no compilation locates the translation (it is missing, or only the base locale compiled), every
    // buffer of the project in that locale counts.
    private List<string> UnsavedReviewFiles(string project, Rmf2ProjectV5? buffers, Rmf2ProjectV5? disk, string key, string locale)
    {
        var files = new HashSet<string>(PathComparer);
        bool translationFound = false;
        foreach (Rmf2ProjectV5 compiled in new[] { buffers, disk }.OfType<Rmf2ProjectV5>())
            foreach (Rmf2LocaleV5 item in compiled.Locales.Where(item => item.Tag == compiled.DefaultLocale || item.Tag == locale))
                foreach (Rmf2TranslationV5 resource in item.DirectResources.Where(resource => resource.Key == key))
                {
                    files.Add(Path.GetFullPath(resource.SourceLocation.Path));
                    translationFound |= item.Tag == locale;
                }
        string[] group = BufferUris(project).ToArray();
        if (!translationFound && group.Length != 0)
        {
            Rmf2Workspace workspace = Workspace(project, group);
            foreach (string uri in group)
                if (string.Equals(workspace.Locale(LocalPath(uri)), locale, StringComparison.OrdinalIgnoreCase)) files.Add(LocalPath(uri));
        }
        var unsaved = new List<string>();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            if (!TryGetBuffer(file, out _, out Buffer? buffer)) continue;
            byte[]? saved = File.Exists(file) ? File.ReadAllBytes(file) : null;
            if (saved is null || !saved.AsSpan().SequenceEqual(Utf8.GetBytes(buffer.Text))) unsaved.Add(file);
        }
        return unsaved;
    }

    private static string? OptionalArgument(JsonArray arguments, int index) =>
        arguments.Count > index && arguments[index] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    // The pseudo-locales runic.json configures, in their canonical spelling, read as the compiler reads runic.json (a
    // UTF-8 BOM is skipped; a duplicate member is the compiler's diagnostic, which validation publishes); none when
    // runic.json is not an object.
    internal static IReadOnlyList<string> ConfiguredPseudoLocales(Rmf2Workspace workspace) =>
        TranslationCompiler.ReadProjectManifest(workspace.ProjectSource, out _)?.PseudoLocales.Select(static pseudo => pseudo.Tag).ToArray() ?? [];

    // runic.markReviewed: records the entry's state again for the source on disk, as runic-translations review mark
    // does, and republishes the diagnostics. It refuses while the base message's or the translation's file has unsaved
    // changes: the review file is on disk, so it must describe the files on disk. Works while translations are
    // incomplete (the review compilation); the review file is written with compare-and-swap.
    private JsonNode? MarkReviewed(string sourcePath, string key, string locale)
    {
        string projectDirectory = ProjectDirectory(sourcePath, allowExternalAncestor: true);
        CompilerInputs inputs = InputFiles.ReadProject(projectDirectory, null);
        var projectSource = new TranslationSource(Path.GetFullPath(inputs.Project.Path), inputs.Project.GetUtf8Bytes());
        TranslationSource[] sources = inputs.Messages.Select(source => new TranslationSource(Path.GetFullPath(source.Path), source.GetUtf8Bytes())).ToArray();
        // Through the project's validation session (#276), which reuses the units of the files the buffers did not change.
        TranslationValidationSession session = _syntaxCache.Session(projectSource.Path);
        Rmf2ProjectCompilation review = TranslationReviewFile.ForReview(session.Validate(projectSource, sources, _requestCancellation).Compilation,
            projectSource, sources, session, null, _requestCancellation);
        if (!review.Success || review.Project is not { } project)
            throw new TranslationAuthoringException("Fix the errors in runic.json or the base locale before recording a review.");
        if (locale == project.DefaultLocale || !project.Locales.Any(item => item.Tag == locale))
            throw new ArgumentException($"'{locale}' is not a translated locale of catalog '{project.Id}'.");
        IReadOnlyDictionary<string, string> fingerprints = TranslationInterchange.ComputeSourceFingerprints(review);
        if (!fingerprints.ContainsKey(key)) throw new ArgumentException($"'{key}' is not a message of catalog '{project.Id}'.");
        Rmf2ProjectV5? buffers = BufferUris(projectDirectory).Any() ? ReviewViewOf(projectDirectory, BufferUris(projectDirectory).ToArray()).Review.Project : null;
        List<string> unsaved = UnsavedReviewFiles(projectDirectory, buffers, project, key, locale);
        if (unsaved.Count != 0)
            throw new TranslationAuthoringException("Save " + string.Join(", ", unsaved.Select(Path.GetFileName)) +
                " first to record a review: the review file describes the files on disk.");
        string path = TranslationReviewFile.PathFor(Path.Combine(projectDirectory, "runic.json"), project.Id);
        try
        {
            TranslationReviewFile.Update(path, project.Id, existing => TranslationReviewFile.Mark(existing, project.Id, [key], locale,
                existing?.Entries.FirstOrDefault(entry => entry.Key == key && entry.Locale == locale)?.State ?? TranslationReviewState.Translated, null, fingerprints),
                TranslationReviewFile.SourcesOf(TranslationReviewFile.CurrentSources(review), [key]));
        }
        catch (TranslationInterchangeException exception) { throw new TranslationAuthoringException("The review was not recorded: " + exception.Message); }
        // The review is recorded: the watched-file event of the review file that may follow is its result, and the
        // request is answered, not refused with ContentModified.
        _editApplied = true;
        _reviewViews.Clear();
        QueueGlobalDiagnosticsRefresh();
        FlushDiagnostics();
        return null;
    }

    private JsonObject QuickFixAction(string uri, Buffer buffer, Rmf2DiagnosticQuickFix fix, bool preferred)
    {
        // Repairs were authorized by the shared compiler against this immutable buffer syntax.
        var editLocation = new TextSourceLocation(buffer.Syntax.Source.Path, fix.StartByte, fix.LengthBytes, 0, 0, 0, 0);
        return new JsonObject {
            ["title"] = fix.Title, ["kind"] = "quickfix", ["isPreferred"] = preferred,
            ["data"] = new JsonObject { ["uri"] = uri, ["version"] = buffer.Version, ["revision"] = fix.ExpectedRevision, ["fixId"] = fix.Id },
            ["edit"] = new JsonObject { ["documentChanges"] = new JsonArray(new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = buffer.Version },
                ["edits"] = new JsonArray(new JsonObject { ["range"] = Range(buffer, editLocation), ["newText"] = fix.NewText }),
            }) },
        };
    }
    private JsonObject Position(string text, int offset)
    {
        int line = 0, start = 0;
        for (int i = 0; i < offset; i++) if (text[i] == '\n') { line++; start = i + 1; }
        ReadOnlySpan<char> value = text.AsSpan(start, offset - start);
        int character = _encoding == "utf-8" ? Utf8.GetByteCount(value) : _encoding == "utf-32" ? ScalarCount(value) : value.Length;
        return new JsonObject { ["line"] = line, ["character"] = character };
    }
    private static int ScalarCount(ReadOnlySpan<char> value) { int count = 0; foreach (Rune rune in value.EnumerateRunes()) { _ = rune; count++; } return count; }
    private int Offset(string text, JsonNode position)
    {
        int line = position["line"]!.GetValue<int>(), character = position["character"]!.GetValue<int>(), at = 0;
        if (line < 0 || character < 0) throw new ArgumentException("Negative LSP position.");
        for (int i = 0; i < line; i++) { int next = text.IndexOf('\n', at); if (next < 0) throw new ArgumentException("Position is outside the document."); at = next + 1; }
        int used = 0;
        while (used < character && at < text.Length && text[at] is not ('\n' or '\r'))
        {
            Rune rune = Rune.GetRuneAt(text, at);
            used += _encoding == "utf-8" ? rune.Utf8SequenceLength : _encoding == "utf-32" ? 1 : rune.Utf16SequenceLength;
            at += rune.Utf16SequenceLength;
        }
        if (used != character) throw new ArgumentException("Position splits a Unicode scalar or exceeds the line.");
        return at;
    }
    private JsonObject? Read()
    {
        var header = new List<byte>();
        while (header.Count < 8192)
        {
            int value = _input.ReadByte(); if (value < 0) return null; header.Add((byte)value);
            if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
        }
        string headers = Encoding.ASCII.GetString(header.ToArray());
        Match length = Regex.Match(headers, "(?im)^Content-Length: *([0-9]+)\\r$");
        if (!length.Success || !int.TryParse(length.Groups[1].Value, out int count) || count > 16 * 1024 * 1024) throw new IOException("Invalid LSP frame length.");
        byte[] payload = new byte[count]; _input.ReadExactly(payload);
        return JsonNode.Parse(payload)?.AsObject();
    }
    private void Send(JsonObject message)
    {
        byte[] bytes = Utf8.GetBytes(message.ToJsonString());
        _output.Write(Encoding.ASCII.GetBytes("Content-Length: " + bytes.Length + "\r\n\r\n")); _output.Write(bytes); _output.Flush();
    }
    // One immutable state of an open document. Its syntax is parsed on first use, once: requests and publications need it,
    // the background validator reads only Text (#271).
    private sealed class Buffer(string text, int version, string path, Func<Rmf2ResourceDocument> parse)
    {
        private readonly Lazy<Rmf2ResourceDocument> _syntax = new(parse, LazyThreadSafetyMode.ExecutionAndPublication);
        private TranslationSource? _encoded;
        internal string Text { get; } = text;
        internal int Version { get; } = version;
        internal Rmf2ResourceDocument Syntax => _syntax.Value;
        // The UTF-8 source of Text, encoded once per buffer: folding ranges and the outline map
        // many block locations of one immutable buffer.
        internal TranslationSource Encoded => _encoded ??= new TranslationSource(path, Utf8.GetBytes(Text));
    }
    private sealed class ProjectFile
    {
        // Per project directory: a fingerprint of the diagnostics and the saved file, and their payload.
        internal Dictionary<string, (string Fingerprint, JsonArray Payload)> Contributions { get; } = new(PathComparer);
        // The URI of the closed-file publication the client currently shows, if any.
        internal string? SentUri { get; set; }
    }
    private sealed class PlatformSourcePathComparer : IEqualityComparer<string>
    {
        // Backslash is a legal file-name character on Unix, so only Windows
        // folds separators; case follows PathComparer.
        private static string Normalize(string path) => OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
        public bool Equals(string? x, string? y) => x is null || y is null ? x == y : PathComparer.Equals(Normalize(x), Normalize(y));
        public int GetHashCode(string path) => PathComparer.GetHashCode(Normalize(path));
    }
}
