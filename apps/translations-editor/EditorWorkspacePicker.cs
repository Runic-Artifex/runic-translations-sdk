using System.Diagnostics;
using Runic.Platform;
using Runic.Platform.Runtime;
#if RUNIC_PLATFORM_Linux
using Runic.Platform.Linux.Portal;
#elif RUNIC_PLATFORM_Windows
using Runic.Platform.Windows;
#elif RUNIC_PLATFORM_MacOS
using Runic.Platform.MacOS;
#endif

namespace Runic.Translations.Editor;

/// <summary>
/// Chooses a workspace directory through the editor window's <see cref="IFileDialogs"/>, so the folder dialog belongs
/// to the window: the XDG desktop portal's FileChooser parented to the GTK 4 window on Linux, the Common Item Dialog
/// owned by the window on Windows, and an <c>NSOpenPanel</c> sheet on the window on macOS. In an installed browser
/// (<c>--browser</c>, or the fallback when the embedded WebView cannot start) Runic Desktop shows the same dialogs
/// without an owner, since the window asks for <see cref="OwnerPolicy.PreferOwner"/>.
/// </summary>
/// <remarks>
/// <para>Only <c>serve</c> has no window: Runic Desktop's serve-only mode binds no native owner, and its window dialogs
/// report <see cref="PlatformUnavailableReason.OwnerUnavailable"/>. For that case alone the picker falls back to the
/// platform's dialogs without an owner (<see cref="CreateUnownedDialogs"/>). The editor serves on loopback only, so the
/// browser that asked runs on this computer, where the dialog appears. Every other unavailability, such as a session
/// without a FileChooser portal, is reported to the page, which then asks for the directory's path.</para>
/// <para>The latest selection's directory lease is held until the next pick or disposal, as the SDK asks for filesystem
/// work through <see cref="IDirectoryLease.LocalPath"/>. A window's lease is also released when the window closes.</para>
/// </remarks>
internal sealed class EditorWorkspacePicker : IDisposable
{
    private static readonly OpenDirectoryOptions Options = new(OwnerPolicy.PreferOwner);

    private readonly IFileDialogs _window;
    private readonly Func<IFileDialogs?> _unowned;
    private readonly object _gate = new();
    private IAsyncDisposable? _selection;
    private bool _disposed;

    /// <param name="window">The window scope's dialogs (<c>AddRunicPlatformServices</c>).</param>
    /// <param name="unowned">The dialogs without an owner for a window that has none; <see cref="CreateUnownedDialogs"/>
    /// by default.</param>
    internal EditorWorkspacePicker(IFileDialogs window, Func<IFileDialogs?>? unowned = null)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _unowned = unowned ?? CreateUnownedDialogs;
    }

    public async Task<EditorWorkspacePickerResult> PickAsync(CancellationToken cancellationToken)
    {
        PickerResult<IDirectoryLease> result;
        try
        {
            result = await _window.OpenDirectoryAsync(Options, cancellationToken).ConfigureAwait(false);
            if (result is PickerResult<IDirectoryLease>.Unavailable(PlatformUnavailableReason.OwnerUnavailable) && _unowned() is { } unowned)
                result = await unowned.OpenDirectoryAsync(Options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new EditorWorkspacePickerResult(false, false, null, EditorNotice.FromException(exception));
        }
        if (result is not PickerResult<IDirectoryLease>.Selected(var lease)) return Outcome(result);
        string path;
        try { path = Path.GetFullPath(lease.LocalPath); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or
            System.Security.SecurityException or ObjectDisposedException or InvalidOperationException)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            return new EditorWorkspacePickerResult(false, false, null, PickerFailed(exception.GetType().Name, exception.Message));
        }
        Keep(lease);
        return new EditorWorkspacePickerResult(true, false, path, null);
    }

    /// <summary>The editor's result for a dialog outcome other than a selection.</summary>
    internal static EditorWorkspacePickerResult Outcome(PickerResult<IDirectoryLease> result) => result switch
    {
        PickerResult<IDirectoryLease>.Dismissed => new EditorWorkspacePickerResult(false, true, null, null),
        // No dialog could be shown, for example without a FileChooser portal or a session bus: the page asks for the path.
        PickerResult<IDirectoryLease>.Unavailable => new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_unavailable")),
        PickerResult<IDirectoryLease>.Failed(var code, var diagnostic) =>
            new EditorWorkspacePickerResult(false, false, null, PickerFailed(code.ToString(), diagnostic?.ToString())),
        _ => new EditorWorkspacePickerResult(false, false, null, EditorNotice.Create("ui_backend_picker_start")),
    };

    // The dialog ran, but its selection could not be used, for example a folder name that is not UTF-8 or a folder the
    // process cannot read, or another dialog of the window is still open (ResourceBusy).
    private static EditorNotice PickerFailed(string reason, string? detail) =>
        EditorNotice.Create("ui_backend_picker_failed", ("reason", reason)) with { Detail = detail };

    /// <summary>
    /// The platform's folder dialogs without an owner window, for <c>serve</c>, whose window has no native owner: the
    /// portal's unparented FileChooser on Linux (which needs no toolkit), the Common Item Dialog on its own thread on
    /// Windows, and an <c>NSOpenPanel</c> of its own on macOS. <see langword="null"/> where the platform has none.
    /// </summary>
    internal static IFileDialogs? CreateUnownedDialogs() => UnownedDialogs.Value;

    private static class UnownedDialogs
    {
        // One backend for the process; none of them needs disposal.
        internal static readonly IPickerBackend? Value = Create();

        private static IPickerBackend? Create()
        {
#if RUNIC_PLATFORM_Linux
            if (OperatingSystem.IsLinux()) return new PortalApplication().CreateUnparentedFileDialogs();
#elif RUNIC_PLATFORM_Windows
            if (OperatingSystem.IsWindowsVersionAtLeast(8)) return WindowsPlatformProvider.CreateUnownedFileDialogs();
#elif RUNIC_PLATFORM_MacOS
            if (OperatingSystem.IsMacOS()) return MacOSPlatformProvider.CreateUnownedFileDialogs();
#endif
            return null;
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
}
