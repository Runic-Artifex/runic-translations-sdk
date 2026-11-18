using System.Text.Json;
using Runic.Desktop;

namespace Runic.Translations.Editor;

/// <summary>
/// Where the editor's window was and how large, so it opens there again (#225 P24). Kept in the native editor state
/// (<see cref="EditorLocalStateStore"/>) under one key per kind of window, not per workspace, so it never competes with
/// the recovery copies for the record's entries.
/// </summary>
/// <remarks>
/// <para>Runic Desktop restores the placement (<c>RunicDesktopHostOptions.Placement</c>) and keeps it on a monitor that
/// exists now: bounds saved on a monitor since disconnected move to the work area they overlap most, or the nearest, and
/// shrink to fit it. A placement holds the size of the window when it is not maximized, whether it is maximized or
/// full-screen, and its position where the platform reports one. GTK 4 reports a position only on X11 and sets none on
/// Wayland, so on a Wayland desktop the window opens at its size and maximized state where the compositor puts it.</para>
/// <para>A placement belongs to this computer and its screens; its units are each platform's.</para>
/// </remarks>
internal static class EditorWindowPlacements
{
    /// <summary>The key of the editor window's placement in the native editor state.</summary>
    internal const string EditorWindowKey = "runic-translations:window-placement:1:editor";

    private const int Version = 1;
    // Larger than any desktop, smaller than anything that overflows the platforms' window arithmetic.
    private const int MaximumExtent = 100_000;

    /// <summary>The placement the editor window last closed with, or <see langword="null"/> for none, or for a stored
    /// value this version does not read (another version, or invalid).</summary>
    internal static DesktopWindowPlacement? Read() =>
        EditorLocalStateStore.Peek(EditorWindowKey) is { } value ? Parse(value) : null;

    /// <summary>Keeps <paramref name="placement"/> for the next start. Returns whether it was written: a window that
    /// never reported a size has nothing to restore, and a record that cannot take the entry is left as it is.</summary>
    internal static bool Remember(DesktopWindowPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (Parse(Serialize(placement)) is null) return false;
        try
        {
            EditorLocalStateStore.Set(EditorWindowKey, Serialize(placement));
            return true;
        }
        // Not remembering where the window was is no reason to fail the editor's exit; the record stays as it was.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static string Serialize(DesktopWindowPlacement placement)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteNumber("width", placement.Width);
            writer.WriteNumber("height", placement.Height);
            if (placement.X is { } x && placement.Y is { } y)
            {
                writer.WriteNumber("x", x);
                writer.WriteNumber("y", y);
            }
            writer.WriteBoolean("maximized", placement.Maximized);
            writer.WriteBoolean("fullScreen", placement.FullScreen == true);
            if (placement.Origin is { } origin) writer.WriteString("origin", origin.ToString());
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a stored placement; <see langword="null"/> for anything but a valid one of this version.</summary>
    internal static DesktopWindowPlacement? Parse(string value)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 4 });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out JsonElement version) || !version.TryGetInt32(out int stored) || stored != Version ||
                !TryExtent(root, "width", out int width) || !TryExtent(root, "height", out int height) ||
                !TryBoolean(root, "maximized", out bool maximized) || !TryBoolean(root, "fullScreen", out bool fullScreen))
                return null;
            bool hasX = root.TryGetProperty("x", out JsonElement xElement);
            bool hasY = root.TryGetProperty("y", out JsonElement yElement);
            int? x = null, y = null;
            if (hasX || hasY)
            {
                if (!hasX || !hasY || !TryCoordinate(xElement, out int left) || !TryCoordinate(yElement, out int top)) return null;
                (x, y) = (left, top);
            }
            DesktopCoordinateOrigin? origin = null;
            if (root.TryGetProperty("origin", out JsonElement originElement))
            {
                if (originElement.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse(originElement.GetString(), ignoreCase: false, out DesktopCoordinateOrigin parsed) ||
                    !Enum.IsDefined(parsed))
                    return null;
                origin = parsed;
            }
            return new DesktopWindowPlacement
            {
                Width = width,
                Height = height,
                X = x,
                Y = y,
                Maximized = maximized,
                FullScreen = fullScreen,
                Origin = origin,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryExtent(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value) && value is > 0 and <= MaximumExtent;
    }

    private static bool TryCoordinate(JsonElement element, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value) &&
            value is >= -MaximumExtent and <= MaximumExtent;
    }

    private static bool TryBoolean(JsonElement root, string name, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;
        value = element.GetBoolean();
        return true;
    }
}
