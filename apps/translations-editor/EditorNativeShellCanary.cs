using System.Net;
using Runic.Application.Desktop;
using Runic.Desktop;
using Runic.Assets;
using Runic.Assets.Desktop;
using Runic.Translations.Editor.Contract;
using Runic.Application.Bridge;

namespace Runic.Translations.Editor;

/// <summary>Exercises the packaged Runic Desktop shell without opening a presentation.</summary>
internal static class EditorNativeShellCanary
{
    private const string WebViewUnavailable = "webview-prerequisite-missing";

    internal static async Task<EditorNativeShellEvidence> RunAsync(
        string workspacePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            AssetArchiveSource assets = AssetArchive.ReadEmbedded(
                typeof(EditorDesktopHost).Assembly,
                EditorDesktopHost.PackagedUiResourceName);
            bool highContrast = DesktopPlatform.IsHighContrast;
            bool webViewAvailable = DesktopPlatform.IsEmbeddedWindowAvailable;
            using var editorSession = new EditorSession(workspacePath);
            var dispatcher = new EditorBridgeDispatcher(new EditorBridgeHandler(editorSession));
            await using DesktopHost host = await DesktopHost.StartAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            await using DesktopSurface surface = await host.CreateSurfaceAsync(
                new DesktopSurfaceOptions
                {
                    ContentHandler = assets.ToDesktopContentHandler(new DesktopAssetOptions
                    {
                        EnableSinglePageApplicationFallback = true,
                    }),
                },
                cancellationToken).ConfigureAwait(false);
            await using DesktopApplicationBridge bridge = DesktopApplicationBridge.Attach(
                surface,
                new ApplicationBridgeSession(dispatcher));

            Uri server = surface.Url;
            if (!server.IsLoopback)
                throw new EditorNativeShellCapabilityException("private-loopback-required");

            EditorNativeShellEvidence evidence = new(
                "runic.translations.editor-native-shell/2",
                "private-loopback",
                "exact-loopback-origin",
                "runic-desktop-bridge-attached",
                dispatcher.ProtocolIdentity,
                dispatcher.ProtocolVersion,
                dispatcher.ManifestFingerprint,
                highContrast,
                true,
                true,
                webViewAvailable ? "available" : WebViewUnavailable,
                0,
                0,
                "closed-disposed");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using HttpResponseMessage response = await client.GetAsync(server, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK ||
                (await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false)).Length == 0)
            {
                throw new EditorNativeShellCapabilityException("private-assets-unavailable");
            }

            return evidence with { LoopbackAssetRequests = 1 };
        }
        catch (EditorNativeShellCapabilityException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EditorNativeShellCapabilityException("native-runtime-unavailable");
        }
    }

    internal static string WebViewUnavailableDiagnostic => WebViewUnavailable;
}

internal sealed class EditorNativeShellCapabilityException(
    string code,
    EditorNativeShellEvidence? evidence = null) : Exception(code)
{
    internal string Code { get; } = code;
    internal EditorNativeShellEvidence? Evidence { get; } = evidence;
}

internal sealed record EditorNativeShellEvidence(
    string Schema,
    string Listener,
    string AllowedOrigin,
    string Bridge,
    string ProtocolIdentity,
    int ProtocolVersion,
    string ContractFingerprint,
    bool HighContrast,
    bool HighContrastPropagated,
    bool PrivateFileHandlerStreaming,
    string WebViewCapability,
    int LoopbackAssetRequests,
    int OutboundTransportAttempts,
    string Cleanup)
{
    internal IReadOnlyDictionary<string, string> ToFaultDetails() => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["schema"] = Schema,
        ["listener"] = Listener,
        ["allowedOrigin"] = AllowedOrigin,
        ["bridge"] = Bridge,
        ["protocolIdentity"] = ProtocolIdentity,
        ["protocolVersion"] = ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["contractFingerprint"] = ContractFingerprint,
        ["highContrast"] = HighContrast ? "true" : "false",
        ["highContrastPropagated"] = HighContrastPropagated ? "true" : "false",
        ["privateFileHandlerStreaming"] = PrivateFileHandlerStreaming ? "true" : "false",
        ["webViewCapability"] = WebViewCapability,
        ["loopbackAssetRequests"] = LoopbackAssetRequests.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["outboundTransportAttempts"] = OutboundTransportAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["cleanup"] = Cleanup,
    };
}
