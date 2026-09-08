using System.Diagnostics;

namespace Runic.Translations.Editor;

internal static class EditorWorkspacePicker
{
    public static async Task<EditorWorkspacePickerResult> PickAsync(CancellationToken cancellationToken)
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

        string? linuxPicker = File.Exists("/usr/bin/zenity") ? "/usr/bin/zenity" : null;
        if (linuxPicker is null) return null;
        var linux = StartInfo(linuxPicker);
        linux.ArgumentList.Add("--file-selection");
        linux.ArgumentList.Add("--directory");
        return linux;
    }

    private static ProcessStartInfo StartInfo(string fileName) => new(fileName)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    };
}
