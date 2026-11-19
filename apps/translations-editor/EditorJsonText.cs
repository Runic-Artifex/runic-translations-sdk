using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Runic.Translations.Editor;

/// <summary>Small JSON documents written with <see cref="Utf8JsonWriter"/>: the editor's packages are NativeAOT
/// executables, where reflection-based serialization of anonymous types is disabled.</summary>
internal static class EditorJsonText
{
    public static string Write(Action<Utf8JsonWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer)) write(writer);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
