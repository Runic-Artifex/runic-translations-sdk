using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Runic.Translations.Editor;

/// <summary>
/// The Linux desktop entry and icon of the editor for the current user, which <c>desktop-entry install</c> writes and
/// <c>desktop-entry uninstall</c> removes. On Wayland the compositor finds a window's icon through the desktop entry
/// named after its app ID (the GTK application ID, <see cref="EditorCommandLineOperations.ApplicationId"/>); on X11,
/// <c>StartupWMClass</c> names the same ID. A normal launch writes neither.
/// </summary>
/// <remarks>
/// The files go below <c>$XDG_DATA_HOME</c> (by default <c>~/.local/share</c>): <c>applications/&lt;id&gt;.desktop</c>
/// and <c>icons/hicolor/&lt;size&gt;x&lt;size&gt;/apps/&lt;id&gt;.png</c>, the size read from the packaged icon. The
/// entry carries <see cref="MarkerKey"/>, so install replaces and uninstall removes only an entry the editor wrote; a
/// desktop entry of that name from somewhere else, such as a package or the user, is left alone.
/// </remarks>
internal sealed class EditorDesktopEntry
{
    internal const string MarkerKey = "X-Runic-Translations-Editor-Installed";

    private readonly string _applicationId;
    private readonly string _dataHome;

    internal EditorDesktopEntry(string applicationId, string dataHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        if (!Path.IsPathFullyQualified(dataHome)) throw new ArgumentException("The data directory must be a fully qualified path.", nameof(dataHome));
        _applicationId = applicationId;
        _dataHome = dataHome;
    }

    /// <summary>The user's data directory by the XDG Base Directory specification, or <see langword="null"/> without a
    /// usable <c>$XDG_DATA_HOME</c> or home directory.</summary>
    internal static string? UserDataHome()
    {
        // A relative XDG_DATA_HOME is invalid and is ignored, as the specification asks.
        string? configured = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(configured) && Path.IsPathFullyQualified(configured)) return configured;
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.IsPathFullyQualified(home) ? Path.Combine(home, ".local", "share") : null;
    }

    internal string EntryPath => Path.Combine(_dataHome, "applications", _applicationId + ".desktop");

    internal string IconPath(int size) => Path.Combine(_dataHome, "icons", "hicolor",
        string.Create(CultureInfo.InvariantCulture, $"{size}x{size}"), "apps", _applicationId + ".png");

    /// <summary>Writes the entry and icon, replacing those of an earlier install.</summary>
    /// <param name="command">The program and its arguments that start the editor.</param>
    /// <param name="icon">The PNG application icon.</param>
    /// <returns>The files written.</returns>
    /// <exception cref="EditorDesktopEntryException">A desktop entry of that name that the editor did not write exists, or
    /// the icon is not a square PNG.</exception>
    internal IReadOnlyList<string> Install(IReadOnlyList<string> command, string name, string comment, string icon)
    {
        ArgumentOutOfRangeException.ThrowIfZero(command.Count);
        if (File.Exists(EntryPath) && !IsOwn(EntryPath))
            throw new EditorDesktopEntryException($"'{EntryPath}' exists and was not written by the editor; remove it first.");
        byte[] image = File.ReadAllBytes(icon);
        int size = PngSize(image) ?? throw new EditorDesktopEntryException($"The editor icon '{icon}' is not a square PNG.");
        string iconPath = IconPath(size);
        // Every size an earlier install wrote, such as before the icon changed size, goes first.
        foreach (string stale in InstalledIcons().Where(path => path != iconPath)) File.Delete(stale);
        WriteAtomically(iconPath, image);
        WriteAtomically(EntryPath, Encoding.UTF8.GetBytes(Render(command, name, comment)));
        return [EntryPath, iconPath];
    }

    /// <summary>Removes the entry and icons an earlier install wrote.</summary>
    /// <returns>The files removed; none when nothing was installed.</returns>
    /// <exception cref="EditorDesktopEntryException">The desktop entry of that name was not written by the editor.</exception>
    internal IReadOnlyList<string> Uninstall()
    {
        var removed = new List<string>();
        if (File.Exists(EntryPath))
        {
            if (!IsOwn(EntryPath))
                throw new EditorDesktopEntryException($"'{EntryPath}' was not written by the editor, so it is left as it is.");
            File.Delete(EntryPath);
            removed.Add(EntryPath);
        }
        foreach (string icon in InstalledIcons())
        {
            File.Delete(icon);
            removed.Add(icon);
        }
        return removed;
    }

    /// <summary>The desktop entry's text (Desktop Entry Specification 1.5).</summary>
    internal string Render(IReadOnlyList<string> command, string name, string comment) => string.Join("\n",
        "[Desktop Entry]",
        "Type=Application",
        "Version=1.5",
        "Name=" + EscapeString(name),
        "Comment=" + EscapeString(comment),
        // %f: a folder dropped on the launcher opens as the workspace; without one, the packaged example opens.
        "Exec=" + EscapeString(string.Join(' ', command.Select(QuoteArgument)) + " %f"),
        "TryExec=" + EscapeString(command[0]),
        "Icon=" + _applicationId,
        "Terminal=false",
        "Categories=Development;Translation;",
        "StartupNotify=true",
        "StartupWMClass=" + _applicationId,
        MarkerKey + "=true",
        "");

    private static bool IsOwn(string entry) =>
        File.ReadLines(entry).Any(line => line.Trim() == MarkerKey + "=true");

    // The icons that an install wrote: the application ID's PNG in any size directory of the user's hicolor theme.
    private IEnumerable<string> InstalledIcons()
    {
        string hicolor = Path.Combine(_dataHome, "icons", "hicolor");
        if (!Directory.Exists(hicolor)) return [];
        return [.. Directory.EnumerateDirectories(hicolor)
            .Select(directory => Path.Combine(directory, "apps", _applicationId + ".png"))
            .Where(File.Exists)];
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string staged = path + ".tmp-" + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        try
        {
            File.WriteAllBytes(staged, content);
            File.Move(staged, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }

    // The width of a square PNG from its IHDR chunk, or null.
    internal static int? PngSize(ReadOnlySpan<byte> png)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (png.Length < 24 || !png[..8].SequenceEqual(signature) || !png.Slice(12, 4).SequenceEqual("IHDR"u8)) return null;
        int width = BinaryPrimitives.ReadInt32BigEndian(png.Slice(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.Slice(20, 4));
        return width > 0 && width == height ? width : null;
    }

    // An Exec argument: quoted when it holds a reserved character, with ", `, $ and \ escaped inside the quotes.
    internal static string QuoteArgument(string argument)
    {
        if (argument.Length > 0 && !argument.Any(static c => char.IsWhiteSpace(c) || "\"'\\><~|&;$*?#()`=%".Contains(c)))
            return argument;
        var quoted = new StringBuilder("\"");
        foreach (char c in argument)
        {
            if (c is '"' or '`' or '$' or '\\') quoted.Append('\\');
            // A literal percent sign is %% in Exec, also inside quotes.
            quoted.Append(c == '%' ? "%%" : c.ToString());
        }
        return quoted.Append('"').ToString();
    }

    // A value of type string or localestring: backslash, newline, tab and carriage return are escaped.
    internal static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);

    /// <summary>The command that starts this editor: its native launcher next to the assembly, else the running
    /// process with the assembly (<c>dotnet Runic.Translations.Editor.dll</c>).</summary>
    internal static IReadOnlyList<string> CurrentCommand()
    {
        string apphost = Path.Combine(AppContext.BaseDirectory, "Runic.Translations.Editor");
        if (File.Exists(apphost)) return [apphost];
        // AppContext.BaseDirectory rather than Assembly.Location, which is empty in a NativeAOT executable.
        string assembly = Path.Combine(AppContext.BaseDirectory, "Runic.Translations.Editor.dll");
        if (!File.Exists(assembly)) assembly = string.Empty;
        string process = Environment.ProcessPath ?? throw new EditorDesktopEntryException("The editor's executable is unknown.");
        return string.IsNullOrEmpty(assembly) || Path.GetFileNameWithoutExtension(process) != "dotnet" ? [process] : [process, assembly];
    }

    /// <summary>The packaged application icon, the one the editor's page shows.</summary>
    internal static string PackagedIcon() => Path.Combine(AppContext.BaseDirectory, "www", "brand", "icon.png");

    internal static string ProductName() =>
        typeof(EditorDesktopEntry).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Runic Translations Editor";

    internal static string ProductDescription() =>
        typeof(EditorDesktopEntry).Assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description
        ?? "A focused desktop editor for Runic Translations workspaces.";
}

/// <summary>The desktop entry could not be installed or removed for a reason the user can act on.</summary>
internal sealed class EditorDesktopEntryException(string message) : Exception(message);
