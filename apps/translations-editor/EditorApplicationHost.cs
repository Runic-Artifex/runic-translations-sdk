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

    private readonly string _workspacePath;
    private readonly bool _useWebView;
    private DesktopApplicationHost? _host;

    public EditorDesktopHost(string workspacePath, bool useWebView)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        _workspacePath = workspacePath;
        _useWebView = useWebView;
    }

    public async ValueTask StartAsync(
        ApplicationCompositionManifest manifest,
        ReadOnlyMemory<string> arguments,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        AssetArchiveSource assets = AssetArchive.ReadEmbedded(
            typeof(EditorDesktopHost).Assembly,
            PackagedUiResourceName);
        _host = new DesktopApplicationHost(new DesktopApplicationHostOptions
        {
            Host = new DesktopHostOptions { Linux = new() { EmbeddedBackend = LinuxEmbeddedBackend.Gtk3WebKit41 } },
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
        });
        try
        {
            await _host.StartAsync(manifest, arguments, services, cancellationToken).ConfigureAwait(false);
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
