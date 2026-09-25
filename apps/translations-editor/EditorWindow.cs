using Runic.Application.Views;
using Runic.Application.Views.CsWebUi;
using Runic.Application.Views.ReactiveUI;

namespace Runic.Translations.Editor;

public sealed partial class EditorWindow : ReactiveRunicWindow<EditorViewModel>, IDisposable, IAsyncDisposable
{
    private readonly CsWebUiBridgeWindow<EditorViewModel> _host;

    public EditorWindow(CsWebUiBridgeWindow<EditorViewModel> host) : base(host.ViewModel) => _host = host;

    public void SetRootFolder(string path) => _host.SetRootFolder(path);
    public void SetSize(uint width, uint height) => _host.SetSize(width, height);
    public void Show(string entry) => _host.Show(entry);
    public void ShowWebView(string entry) => _host.NativeWindow.ShowWebView(entry);
    public string StartServer(string entry) => _host.StartServer(entry);
    public ValueTask<CsWebUiBridgeCloseResult> CloseAsync(TimeSpan timeout) => _host.CloseAsync(timeout);
    public void Dispose() => _host.Dispose();
    public ValueTask DisposeAsync() => _host.DisposeAsync();
}
