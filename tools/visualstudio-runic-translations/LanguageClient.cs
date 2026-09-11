using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json.Linq;
using StreamJsonRpc;

namespace Runic.Translations.VisualStudio;

internal static class ContentTypes
{
    [Export, Name("rmf2"), BaseDefinition(CodeRemoteContentDefinition.CodeRemoteContentTypeName)]
    internal static ContentTypeDefinition Definition = null!;
    [Export, FileExtension(".rmf2"), ContentType("rmf2")]
    internal static FileExtensionToContentTypeDefinition Extension = null!;
}

[Export(typeof(ILanguageClient)), Export(typeof(RunicLanguageClient)), ContentType("rmf2")]
public sealed class RunicLanguageClient : ILanguageClient, ILanguageClientCustomMessage2, IDisposable
{
    private Process? process;
    private JsonRpc? rpc;
    public string Name => "Runic RMF2";
    public IEnumerable<string> ConfigurationSections => Array.Empty<string>();
    public object? InitializationOptions => null;
    public IEnumerable<string> FilesToWatch => new[] { "**/*.rmf2", "**/runic.json" };
    public bool ShowNotificationOnInitializeFailed => true;
    public object? MiddleLayer => null;
    public object? CustomMessageTarget => null;
    public event Microsoft.VisualStudio.Threading.AsyncEventHandler<EventArgs>? StartAsync;
    public event Microsoft.VisualStudio.Threading.AsyncEventHandler<EventArgs>? StopAsync;
    public async Task<Connection?> ActivateAsync(CancellationToken token)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
        string? file = dte?.ActiveDocument?.FullName;
        if (string.IsNullOrEmpty(file)) file = dte?.Solution?.FullName;
        if (string.IsNullOrEmpty(file)) throw new InvalidOperationException("Open an RMF2 document in the solution or folder containing the local Runic tool manifest.");
        var launch = ServerLaunch.Find(Path.GetDirectoryName(file)!, Environment.GetEnvironmentVariable("RUNIC_TRANSLATIONS_SERVER"));
        DisposeProcess();
        process = new Process { StartInfo = new ProcessStartInfo {
            FileName = Environment.GetEnvironmentVariable("RUNIC_DOTNET") ?? "dotnet", Arguments = launch.Arguments,
            WorkingDirectory = launch.Directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        } };
        process.ErrorDataReceived += (_, args) => { if (args.Data != null) Trace.WriteLine("Runic: " + args.Data); };
        if (!process.Start()) throw new InvalidOperationException("Could not start the project-local Runic language server.");
        process.BeginErrorReadLine();
        return new Connection(process.StandardOutput.BaseStream, process.StandardInput.BaseStream);
    }
    public async Task OnLoadedAsync() { if (StartAsync != null) await StartAsync.InvokeAsync(this, EventArgs.Empty); }
    public Task OnServerInitializedAsync() => Task.CompletedTask;
    public Task<InitializationFailureContext?> OnServerInitializeFailedAsync(ILanguageClientInitializationInfo initializationState)
        => Task.FromResult<InitializationFailureContext?>(new InitializationFailureContext { FailureMessage = "Runic could not initialize. Restore the project's local runic-translations tool, or set RUNIC_TRANSLATIONS_SERVER to the built tool DLL before starting Visual Studio. See ActivityLog and Runic server output for details." });
    public Task AttachForCustomMessageAsync(JsonRpc jsonRpc) { rpc = jsonRpc; return Task.CompletedTask; }
    internal Task<JObject> RequestAsync(string method, object parameters, CancellationToken cancellationToken)
        => (rpc ?? throw new InvalidOperationException("The Runic language server is not ready.")).InvokeWithParameterObjectAsync<JObject>(method, parameters, cancellationToken);
    internal async Task RestartAsync()
    {
        if (StopAsync != null) await StopAsync.InvokeAsync(this, EventArgs.Empty);
        DisposeProcess();
        if (StartAsync != null) await StartAsync.InvokeAsync(this, EventArgs.Empty);
    }
    public void Dispose() => DisposeProcess();
    private void DisposeProcess()
    {
        rpc = null;
        if (process == null) return;
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        process.Dispose(); process = null;
    }
}

internal sealed class ServerLaunch
{
    internal string Directory { get; }
    internal string Arguments { get; }
    private ServerLaunch(string directory, string arguments) { Directory = directory; Arguments = arguments; }
    internal static ServerLaunch Find(string directory, string? assembly)
    {
        if (!string.IsNullOrEmpty(assembly))
        {
            if (!Path.IsPathRooted(assembly) || !File.Exists(assembly)) throw new InvalidOperationException("RUNIC_TRANSLATIONS_SERVER must identify an existing absolute tool DLL.");
            return new ServerLaunch(directory, Quote(assembly!) + " lsp");
        }
        for (string? current = directory; current != null; current = Path.GetDirectoryName(current))
        {
            string manifest = Path.Combine(current, ".config", "dotnet-tools.json");
            if (!File.Exists(manifest)) continue;
            var json = JObject.Parse(File.ReadAllText(manifest));
            foreach (var tool in (json["tools"] as JObject ?? new JObject()).Properties())
                if (tool.Value["commands"] is JArray commands && commands.ToObject<string[]>() is string[] names && Array.IndexOf(names, "runic-translations") >= 0)
                    return new ServerLaunch(current, "tool run runic-translations -- lsp");
            if (json.Value<bool?>("isRoot") == true) break;
        }
        throw new InvalidOperationException("No project-local runic-translations tool manifest found. Restore the local tool before opening RMF2 resources.");
    }
    // Windows CommandLineToArgvW escaping; ProcessStartInfo on .NET Framework has no ArgumentList.
    internal static string Quote(string value)
    {
        var result = new System.Text.StringBuilder("\""); int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes); result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
