using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Runic.Translations.Editor.Contract;

namespace Runic.Translations.Editor;

// This source is included only with RunicEditorHostedE2E=true. It provides the
// browser harness's one static page without expanding the shipped hosted-web
// route surface or accepting arbitrary fixture paths.
internal sealed partial class EditorHostedWebServer
{
    static partial void MapTestFixtures(WebApplication application)
    {
        string? fixturesRoot = Environment.GetEnvironmentVariable("RUNIC_EDITOR_HOSTED_E2E_ASSETS");
        if (string.IsNullOrWhiteSpace(fixturesRoot)) return;
        string root = Path.GetFullPath(fixturesRoot);
        application.MapGet("/__hosted-e2e/hosted-web-browser.html", async (HttpContext context) =>
        {
            string fixture = Path.Combine(root, "hosted-web-browser.html");
            if (!File.Exists(fixture))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            string html = (await File.ReadAllTextAsync(fixture, context.RequestAborted).ConfigureAwait(false))
                .Replace("__BRIDGE_PROTOCOL__", EditorBridgeContract.ProtocolIdentity, StringComparison.Ordinal)
                .Replace("__BRIDGE_VERSION__", EditorBridgeContract.ProtocolVersion.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
                .Replace("__BRIDGE_FINGERPRINT__", EditorBridgeContract.Fingerprint, StringComparison.Ordinal);
            await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
        });
    }
}
