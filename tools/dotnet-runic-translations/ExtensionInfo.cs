using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Tool;

/// <summary>
/// <c>runic-translations --extension-info</c>: the tool as an extension of the Runic core tool, which runs it as
/// <c>dotnet runic translations …</c> (in 0.25 <c>rax translations …</c>). It prints one JSON object on one line:
/// <c>name</c> (the command under the core), <c>version</c> (the Translations release, as <c>--version</c> reports
/// it) and <c>extensionProtocol</c>, plus <c>command</c> and <c>package</c>. The core reads it in <c>doctor</c> to
/// check the installed release against its compatibility set; other members may be added, so readers ignore them.
/// </summary>
/// <remarks>
/// Protocol 1: the core passes the arguments after the extension's name unchanged, a leading <c>--format</c> first,
/// runs the tool with the console's standard streams and exits with its exit code.
/// </remarks>
internal static class ExtensionInfo
{
    internal const string Option = "--extension-info";
    internal const string Name = "translations";
    internal const int Protocol = 1;
    internal const string Command = "runic-translations";
    internal const string Package = "dotnet-runic-translations";

    internal static string Format(string version)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("name", Name);
            writer.WriteString("version", version);
            writer.WriteNumber("extensionProtocol", Protocol);
            writer.WriteString("command", Command);
            writer.WriteString("package", Package);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    internal static int Write(TextWriter output, string version)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(Format(version) + "\n");
        output.Flush();
        return Program.Success;
    }
}
