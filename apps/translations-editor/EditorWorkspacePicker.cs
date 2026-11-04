using System.Diagnostics;
using Runic.Platform;
#if RUNIC_PLATFORM_Linux
using Runic.Platform.Linux.Portal;
using Runic.Platform.Runtime;
#endif

namespace Runic.Translations.Editor;

/// <summary>
/// Chooses a workspace directory. On Linux it first uses the Runic SDK's XDG desktop portal FileChooser, which needs no
/// toolkit and no parent window. When the session has no FileChooser portal of version 3 or later, or no session bus,
/// it falls back to <c>/usr/bin/zenity</c> when that is installed. CS-WebUI windows have no native owner, so the window's
/// <see cref="IFileDialogs"/> always reports <c>OwnerUnavailable</c>, and Runic SDK 0.16 makes unowned dialogs public
/// only for the portal. Windows and macOS therefore keep their system folder dialog through PowerShell and AppleScript
/// until the SDK offers theirs.
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
        if (OperatingSystem.IsLinux())
        {
            // The portal showed no dialog when it returns null; the system dialog (zenity) is then tried instead.
            EditorWorkspacePickerResult? portal = await PickWithPortalAsync(cancellationToken).ConfigureAwait(false);
            if (portal is not null) return portal;
        }
#endif
        return await PickWithSystemDialogAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The editor's result for a portal outcome other than a selection, or <see langword="null"/> when the portal could
    /// not show a dialog (no FileChooser of version 3 or later, or no session bus) and the system dialog should be tried.
    /// </summary>
    internal static EditorWorkspacePickerResult? PortalOutcome(PickerResult<IDirectoryLease> result) => result switch
    {
        PickerResult<IDirectoryLease>.Dismissed => new EditorWorkspacePickerResult(false, true, null, null),
        PickerResult<IDirectoryLease>.Unavailable => null,
        PickerResult<IDirectoryLease>.Failed(var code, var diagnostic) =>
            new EditorWorkspacePickerResult(false, false, null, PickerFailed(code.ToString(), diagnostic?.ToString())),
        _ => new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_start")),
    };

    // The dialog ran, but its selection could not be used, for example a folder name that is not UTF-8 or a folder the
    // process cannot read.
    private static EditorNotice PickerFailed(string reason, string? detail) =>
        EditorNotice.Create("ui_backend_picker_failed", ("reason", reason)) with { Detail = detail };

#if RUNIC_PLATFORM_Linux
    // One portal application identity, inferred from the desktop, for the process.
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private static class PortalDialogs
    {
        internal static readonly IPickerBackend Value = new PortalApplication().CreateUnparentedFileDialogs();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    private async Task<EditorWorkspacePickerResult?> PickWithPortalAsync(CancellationToken cancellationToken)
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
        if (result is not PickerResult<IDirectoryLease>.Selected(var lease)) return PortalOutcome(result);
        string path;
        try { path = Path.GetFullPath(lease.LocalPath); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or System.Security.SecurityException)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            return new EditorWorkspacePickerResult(false, false, null, PickerFailed(exception.GetType().Name, exception.Message));
        }
        Keep(lease);
        return new EditorWorkspacePickerResult(true, false, path, null);
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
        ProcessStartInfo? startInfo = CreateStartInfo(File.Exists);
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
            string path;
            try { path = Path.GetFullPath(output); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or System.Security.SecurityException)
            {
                return new EditorWorkspacePickerResult(false, false, null, PickerFailed(exception.GetType().Name, exception.Message));
            }
            return new EditorWorkspacePickerResult(true, false, path, null);
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

    // TODO(runic-sdk 0.17): replace Windows and macOS with the SDK's unowned file dialogs once they are public
    // (today WindowsPlatformProvider.CreateUnownedFileDialogs and MacOSPlatformProvider.CreateUnownedFileDialogs are
    // internal to Runic Desktop). Linux reaches this only when the portal showed no dialog.
    internal static ProcessStartInfo? CreateStartInfo(Func<string, bool> fileExists)
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

        return CreateZenityStartInfo(fileExists);
    }

    /// <summary>The Linux fallback, a zenity folder dialog, or <see langword="null"/> when zenity is not installed.</summary>
    internal static ProcessStartInfo? CreateZenityStartInfo(Func<string, bool> fileExists)
    {
        const string zenity = "/usr/bin/zenity";
        if (!fileExists(zenity)) return null;
        var result = StartInfo(zenity);
        result.ArgumentList.Add("--file-selection");
        result.ArgumentList.Add("--directory");
        return result;
    }

    private static ProcessStartInfo StartInfo(string fileName) => new(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
}
