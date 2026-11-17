using System;
using System.IO;

// Runic.Translations.Build links this file: its source discovery task runs on every build and must not load the compiler.
#if RUNIC_TRANSLATIONS_BUILD
namespace Runic.Translations.Build;
#else
namespace Runic.Translations.Compiler;
#endif

/// <summary>
/// Whether a path lies within a directory. Every containment check of the Translations tools goes through this type
/// instead of testing a <see cref="Path.GetRelativePath(string, string)"/> result for a <c>"../"</c> prefix, which
/// misses Windows' <c>..\</c> (WIN-04).
/// </summary>
/// <remarks>
/// <see cref="Path.GetRelativePath(string, string)"/> compares the two paths with the platform's rules, so a
/// difference in case alone stays within the directory on Windows and macOS. A path on another drive or share comes
/// back rooted and is outside. The source tree's guard test (<c>PathContainmentGuardTests</c>) keeps raw
/// <c>"../"</c> prefix checks out of the C# sources.
/// </remarks>
internal static class PathContainment
{
    /// <summary>Whether <paramref name="path"/> is <paramref name="root"/> itself or lies below it.</summary>
    internal static bool IsWithin(string root, string path) => IsWithinRelative(Path.GetRelativePath(root, path));

    /// <summary>Whether <paramref name="path"/> lies below <paramref name="root"/>, excluding the directory itself.</summary>
    internal static bool IsBelow(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != "." && IsWithinRelative(relative);
    }

    /// <summary>
    /// <paramref name="path"/> relative to <paramref name="root"/> with <c>/</c> separators when it lies within it, or
    /// <see langword="null"/> when it is outside.
    /// </summary>
    internal static string? RelativeWithin(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return IsWithinRelative(relative) ? relative.Replace('\\', '/') : null;
    }

    /// <summary>Whether a <see cref="Path.GetRelativePath(string, string)"/> result stays within its base directory.</summary>
    internal static bool IsWithinRelative(string relative) => IsWithinRelative(relative, OperatingSystem.IsWindows());

    /// <summary>
    /// Whether a relative path stays within its base directory: it is not <c>..</c>, does not start with <c>..</c> and
    /// either separator, and is not rooted. With <paramref name="windows"/>, a drive (<c>C:</c>) or a leading separator
    /// also counts as rooted, so Windows-shaped results can be checked on any platform.
    /// </summary>
    internal static bool IsWithinRelative(string relative, bool windows)
    {
        ArgumentNullException.ThrowIfNull(relative);
        // The rules come from `windows`, not from the host: Path.IsPathRooted would apply Windows rules on a Windows host
        // even for a Unix-shaped check ("C:translations" is a file name by Unix rules).
        if (relative.Length == 0 || relative[0] == '/') return false;
        if (windows && (relative[0] == '\\' || (relative.Length >= 2 && relative[1] == ':' && char.IsAsciiLetter(relative[0])))) return false;
        // "..", "../x" or "..\x" leave the directory; "..x" is a file name within it.
        return !(relative.Length >= 2 && relative[0] == '.' && relative[1] == '.' && (relative.Length == 2 || relative[2] is '/' or '\\'));
    }
}
