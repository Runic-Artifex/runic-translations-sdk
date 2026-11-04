using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using StreamJsonRpc;

namespace Runic.Translations.VisualStudio;

/// <summary>
/// Opens every <c>runic.json</c> editor buffer on the Runic language server, as VS Code does, so
/// configuration-changing refactors include unsaved manifest edits instead of being refused.
/// Visual Studio's JSON language service keeps the file: the client sends the standard
/// <c>textDocument/didOpen</c>, <c>didChange</c> (full text) and <c>didClose</c> notifications
/// over the client's JSON-RPC connection, and declares <c>runicConfigurationSync</c>.
/// </summary>
[Export(typeof(IWpfTextViewCreationListener)), ContentType("text"), TextViewRole(PredefinedTextViewRoles.Document)]
internal sealed class ConfigurationViewListener : IWpfTextViewCreationListener
{
    [Import] internal ITextDocumentFactoryService Documents = null!;
    [Import] internal RunicLanguageClient Client = null!;

    public void TextViewCreated(IWpfTextView textView)
    {
        if (!Documents.TryGetTextDocument(textView.TextDataModel.DocumentBuffer, out ITextDocument document)) return;
        if (!string.Equals(Path.GetFileName(document.FilePath), "runic.json", StringComparison.OrdinalIgnoreCase)) return;
        Client.Configuration.Track(document, textView);
    }
}

internal sealed class ConfigurationBuffers
{
    private readonly object gate = new();
    private readonly Dictionary<ITextBuffer, Entry> entries = new();
    private JsonRpc? rpc;
    // StreamJsonRpc does not promise FIFO order for concurrent notifications; one chain keeps
    // didOpen, every didChange and didClose of a buffer in order.
    private Task sending = Task.CompletedTask;

    private sealed class Entry
    {
        internal Entry(string uri) { Uri = uri; }
        internal string Uri { get; }
        internal int Views { get; set; }
    }

    internal void Track(ITextDocument document, ITextView view)
    {
        ITextBuffer buffer = document.TextBuffer;
        lock (gate)
        {
            if (!entries.TryGetValue(buffer, out Entry? entry))
            {
                entry = new Entry(new Uri(document.FilePath).AbsoluteUri);
                entries.Add(buffer, entry);
                buffer.Changed += OnChanged;
                Open(entry, buffer.CurrentSnapshot);
            }
            entry.Views++;
        }
        view.Closed += (_, _) => Release(buffer);
    }

    /// <summary>Attaches a newly initialized server (or detaches with null) and opens the tracked buffers on it.</summary>
    internal void Attach(JsonRpc? connection)
    {
        lock (gate)
        {
            rpc = connection;
            sending = Task.CompletedTask;
            foreach (var pair in entries) Open(pair.Value, pair.Key.CurrentSnapshot);
        }
    }

    private void Release(ITextBuffer buffer)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(buffer, out Entry? entry) || --entry.Views > 0) return;
            entries.Remove(buffer);
            buffer.Changed -= OnChanged;
            Send("textDocument/didClose", new { textDocument = new { uri = entry.Uri } });
        }
    }

    private void OnChanged(object sender, TextContentChangedEventArgs args)
    {
        lock (gate)
        {
            if (!entries.TryGetValue((ITextBuffer)sender, out Entry? entry)) return;
            // Snapshot version numbers grow with every edit, as the server requires.
            Send("textDocument/didChange", new {
                textDocument = new { uri = entry.Uri, version = args.After.Version.VersionNumber },
                contentChanges = new[] { new { text = args.After.GetText() } },
            });
        }
    }

    private void Open(Entry entry, ITextSnapshot snapshot) =>
        Send("textDocument/didOpen", new { textDocument = new { uri = entry.Uri, languageId = "json", version = snapshot.Version.VersionNumber, text = snapshot.GetText() } });

    private void Send(string method, object parameters)
    {
        JsonRpc? connection = rpc;
        if (connection == null) return;
        sending = sending.ContinueWith(_ => connection.NotifyWithParameterObjectAsync(method, parameters), TaskScheduler.Default).Unwrap()
            // A stopped server drops notifications; the next Attach reopens every buffer.
            .ContinueWith(task => { _ = task.Exception; }, TaskScheduler.Default);
    }
}
