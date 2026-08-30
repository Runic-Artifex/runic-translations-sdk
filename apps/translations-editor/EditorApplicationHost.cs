using Runic.Application;
using Runic.Application.Desktop;
using Runic.Desktop;
using Runic.Assets;
using Runic.Assets.Desktop;
using Runic.Translations.Editor.Contract;
using Runic.Application.Bridge;

namespace Runic.Translations.Editor;

internal sealed class EditorDesktopHost : IApplicationHost
{
    internal const string PackagedUiResourceName = "Runic.Assets.StaticFiles";

    internal static bool PackagedUiEmbedded =>
        typeof(EditorDesktopHost).Assembly.GetManifestResourceInfo(PackagedUiResourceName) is not null;

    private readonly EditorSession _session;
    private readonly string _workspacePath;
    private readonly bool _useWebView;
    private DesktopApplicationHost? _host;

    public EditorDesktopHost(EditorSession session, string workspacePath, bool useWebView)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        _workspacePath = workspacePath;
        _useWebView = useWebView;
    }

    public async ValueTask StartAsync(
        ApplicationCompositionManifest manifest,
        ReadOnlyMemory<string> arguments,
        CancellationToken cancellationToken)
    {
        AssetArchiveSource assets = AssetArchive.ReadEmbedded(
            typeof(EditorDesktopHost).Assembly,
            PackagedUiResourceName);
        _host = new DesktopApplicationHost(new DesktopApplicationHostOptions
        {
            Title = "Runic Translations Editor",
            Surface = new DesktopSurfaceOptions
            {
                ContentHandler = assets.ToDesktopContentHandler(new DesktopAssetOptions
                {
                    EnableSinglePageApplicationFallback = true,
                }),
            },
            Window = new DesktopWindowOptions
            {
                Browser = _useWebView ? BrowserKind.Embedded : BrowserKind.Any,
                Width = 1440,
                Height = 900,
                MinimumWidth = 980,
                MinimumHeight = 680,
                Centered = true,
                Resizable = true,
                HighContrast = DesktopPlatform.IsHighContrast,
            },
            CreateBridgeSession = () => new ApplicationBridgeSession(
                new EditorBridgeDispatcher(new EditorBridgeHandler(_session))),
        });
        try
        {
            await _host.StartAsync(manifest, arguments, cancellationToken).ConfigureAwait(false);
            Console.WriteLine(
                $"Runic Translations Editor is serving '{Path.GetFullPath(_workspacePath)}' at {_host.Surface!.Url}");
        }
        catch
        {
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            throw;
        }
    }

    public ValueTask WaitForShutdownAsync(CancellationToken cancellationToken) =>
        _host?.WaitForShutdownAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        _host?.StopAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync().ConfigureAwait(false);
        _host = null;
    }
}
