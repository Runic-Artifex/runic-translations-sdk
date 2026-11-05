using System;

namespace Runic.Translations.Authoring;

/// <summary>
/// A UTF-8 byte order mark before runic.json, as Visual Studio saves it. The compiler skips it; System.Text.Json does
/// not, so every authoring read of runic.json skips it too, and edits keep it (their offsets are past it).
/// </summary>
internal static class Utf8Bom
{
    internal static int Length(ReadOnlySpan<byte> bytes) => bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;

    internal static ReadOnlyMemory<byte> Skip(byte[] bytes) => bytes.AsMemory(Length(bytes));
}
