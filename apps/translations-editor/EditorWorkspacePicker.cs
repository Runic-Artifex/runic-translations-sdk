using System.Diagnostics;
using Runic.Platform;
#if RUNIC_PLATFORM_Linux
using Runic.Platform.Linux.Portal;
using Runic.Platform.Runtime;
#endif

namespace Runic.Translations.Editor;

/// <summary>
/// Chooses a workspace directory. On Linux it uses the Runic SDK's XDG desktop portal FileChooser, which needs no
/// toolkit and no parent window. CS-WebUI windows have no native owner, so the window's <see cref="IFileDialogs"/>
/// always reports <c>OwnerUnavailable</c>, and Runic SDK 0.16 makes unowned dialogs public only for the portal. Windows
/// and macOS therefore keep their system folder dialog through PowerShell and AppleScript until the SDK offers theirs.
/// </summary>
/// <remarks>The latest selection's directory lease is held until the next pick or disposal, as the SDK asks for
/// filesystem work through <see cref="IDirectoryLease.LocalPath"/>.</remarks>
internal sealed class EditorWorkspacePicker : IDisposable
{
    private readonly object _gate = new();
    private IAsyncDisposable? _selection;
    private bool _disposed;

    public async Task<EditorWorkspacePickerResult> PickAsync(CancellationToken cancellationToken)
    {
#if RUNIC_PLATFORM_Linux
        if (OperatingSystem.IsLinux()) return await PickWithPortalAsync(cancellationToken).ConfigureAwait(false);
#endif
        return await PickWithSystemDialogAsync(cancellationToken).ConfigureAwait(false);
    }

#if RUNIC_PLATFORM_Linux
    // One portal application identity, inferred from the desktop, for the process.
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static class PortalDialogs
    {
        internal static readonly IPickerBackend Value = new PortalApplication().CreateUnparentedFileDialogs();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<EditorWorkspacePickerResult> PickWithPortalAsync(CancellationToken cancellationToken)
    {
        PickerResult<IDirectoryLease> result;
        try
        {
            result = await PortalDialogs.Value.OpenDirectoryAsync(new OpenDirectoryOptions(OwnerPolicy.PreferOwner), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new EditorWorkspacePickerResult(false, false, null, EditorNotice.FromException(exception));
        }
        switch (result)
        {
            case PickerResult<IDirectoryLease>.Selected(var lease):
                string path;
                try { path = Path.GetFullPath(lease.LocalPath); }
                catch
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
                Keep(lease);
                return new EditorWorkspacePickerResult(true, false, path, null);
            case PickerResult<IDirectoryLease>.Dismissed:
                return new EditorWorkspacePickerResult(false, true, null, null);
            case PickerResult<IDirectoryLease>.Unavailable:
                return new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_unavailable"));
            default:
                return new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_start"));
        }
    }

    private void Keep(IAsyncDisposable selection)
    {
        IAsyncDisposable? previous;
        lock (_gate)
        {
            if (_disposed) previous = selection;
            else
            {
                previous = _selection;
                _selection = selection;
            }
        }
        Release(previous);
    }
#endif

    public void Dispose()
    {
        IAsyncDisposable? selection;
        lock (_gate)
        {
            _disposed = true;
            selection = _selection;
            _selection = null;
        }
        Release(selection);
    }

    private static void Release(IAsyncDisposable? selection)
    {
        if (selection is null) return;
        // A lease releases its native grant; nothing waits for it, and a failure only loses that release.
        _ = Task.Run(async () =>
        {
            try { await selection.DisposeAsync().ConfigureAwait(false); }
            catch (Exception exception) { Trace.TraceWarning($"The workspace directory lease was not released: {exception.Message}"); }
        });
    }

    private static async Task<EditorWorkspacePickerResult> PickWithSystemDialogAsync(CancellationToken cancellationToken)
    {
        ProcessStartInfo? startInfo = CreateStartInfo();
        if (startInfo is null)
        {
            return new EditorWorkspacePickerResult(
                false,
                false,
                null,
                EditorNotice.Create("ui_backend_picker_unavailable"));
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                return new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_start"));
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string output = (await outputTask.ConfigureAwait(false)).Trim();
            string error = (await errorTask.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0 || output.Length == 0)
                return new EditorWorkspacePickerResult(false, true, null, error.Length == 0 ? null : EditorNotice.External(error));
            return new EditorWorkspacePickerResult(true, false, Path.GetFullPath(output), null);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new EditorWorkspacePickerResult(false, false, null, EditorNotice.FromException(exception));
        }
    }

    // TODO(runic-sdk 0.17): replace both with the SDK's unowned Windows and macOS file dialogs once they are public
    // (today WindowsPlatformProvider.CreateUnownedFileDialogs and MacOSPlatformProvider.CreateUnownedFileDialogs are
    // internal to Runic Desktop), then remove this process code.
    private static ProcessStartInfo? CreateStartInfo()
    {
        if (OperatingSystem.IsWindows())
        {
            var result = StartInfo("powershell.exe");
            result.ArgumentList.Add("-NoProfile");
            result.ArgumentList.Add("-STA");
            result.ArgumentList.Add("-Command");
            result.ArgumentList.Add(
                "Add-Type -AssemblyName System.Windows.Forms; " +
                "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dialog.SelectedPath) }");
            return result;
        }

        if (OperatingSystem.IsMacOS())
        {
            var result = StartInfo("/usr/bin/osascript");
            result.ArgumentList.Add("-e");
            result.ArgumentList.Add("POSIX path of (choose folder)");
            return result;
        }

        return null;
    }

    private static ProcessStartInfo StartInfo(string fileName) => new(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
}
