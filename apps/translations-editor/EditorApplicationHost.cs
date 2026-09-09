using System.Text.Json;
using System.Text.Json.Serialization;
using Runic.Platform;
#if RUNIC_PLATFORM_Linux
using SelectedProvider = Runic.Platform.Linux.LinuxPlatformProvider;
#elif RUNIC_PLATFORM_Windows
using SelectedProvider = Runic.Platform.Windows.WindowsPlatformProvider;
#elif RUNIC_PLATFORM_MacOS
using SelectedProvider = Runic.Platform.MacOS.MacOSPlatformProvider;
#endif
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
    private IDesktopSettings? _settings;
    private static readonly Dictionary<string, string> NoCache = new() { ["Cache-Control"] = "no-store" };

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
        _settings = SelectedProvider.CreateSettings();
        var content = assets.ToDesktopContentHandler(new DesktopAssetOptions { EnableSinglePageApplicationFallback = true });
        _host = new DesktopApplicationHost(new DesktopApplicationHostOptions
        {
            Host = new DesktopHostOptions { Linux = new() { EmbeddedBackend = LinuxEmbeddedBackend.Gtk3WebKit41 } },
            Title = "Runic Translations Editor",
            Surface = new DesktopSurfaceOptions
            {
                ContentHandler = async (request, token) =>
                {
                    if (request.Path != "/runic-desktop-appearance.json") return await content(request, token);
                    if (request.Method != "GET") return ContentResponse.Text("Method not allowed", "text/plain", 405);
                    try
                    {
                        var result = await _settings.ReadAsync(token);
                        return result is PlatformResult<DesktopAppearance>.Success appearance
                            ? new ContentResponse(JsonSerializer.SerializeToUtf8Bytes(appearance.Value, EditorAppearanceJson.Default.DesktopAppearance), "application/json", headers: NoCache)
                            : new ContentResponse("null"u8.ToArray(), "application/json", headers: NoCache);
                    }
                    catch (ObjectDisposedException) { return ContentResponse.Text("Stopping", "text/plain", 503); }
                },
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
            await _settings.DisposeAsync().ConfigureAwait(false);
            await _host.DisposeAsync().ConfigureAwait(false);
            _host = null;
            throw;
        }
    }

    public ValueTask WaitForShutdownAsync(CancellationToken cancellationToken) =>
        _host?.WaitForShutdownAsync(cancellationToken) ?? ValueTask.CompletedTask;

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        if (_settings is not null) await _settings.DisposeAsync().ConfigureAwait(false);
        if (_host is not null) await _host.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_settings is not null) await _settings.DisposeAsync().ConfigureAwait(false);
        if (_host is not null) await _host.DisposeAsync().ConfigureAwait(false);
        _host = null;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DesktopAppearance))]
internal sealed partial class EditorAppearanceJson : JsonSerializerContext;
