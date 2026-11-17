using System;
using System.Text;
using Runic.Translations.Generated;

namespace Runic.Translations;

/// <summary>
/// The message table of a generated catalog for RMF2 runtime ABI 4 (D072.2): one UTF-8 line per message in ID order,
/// <c>key</c> followed by its caller inputs as <c> name:type</c> in ordinal order, each line ending in <c>\n</c>. The
/// first <c>canonicalCount</c> lines are the canonical messages, the rest the permitted locale extras. The catalog's
/// definitions, keys and external-pack contracts are read from it with one loop instead of code per message.
/// </summary>
/// <remarks>Keys are dot-separated ASCII identifiers and input names are RMF2 names, so neither holds a space, a colon or a line break.</remarks>
internal static class GeneratedMessageTable
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static CompiledTranslationDefinition[] ReadDefinitions(ReadOnlySpan<byte> table, int canonicalCount)
    {
        int count = Count(table, canonicalCount);
        var definitions = new CompiledTranslationDefinition[count];
        int position = 0;
        for (int id = 0; id < count; id++)
        {
            ReadOnlySpan<byte> line = NextLine(table, ref position);
            string name = Key(line, out ReadOnlySpan<byte> inputs);
            definitions[id] = CompiledTranslationDefinition.FromRmf2Inputs(name, Inputs(inputs), id < canonicalCount);
        }
        return definitions;
    }

    internal static TranslationKey[] CreateKeys(string catalog, ReadOnlySpan<byte> table, int canonicalCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        Count(table, canonicalCount);
        var keys = new TranslationKey[canonicalCount];
        int position = 0;
        for (int id = 0; id < keys.Length; id++)
            keys[id] = new TranslationKey(catalog, id, Key(NextLine(table, ref position), out _));
        return keys;
    }

    // The message contracts of one locale's external pack: every canonical message and the extras the locale defines
    // (extraIds, ascending), in ordinal key order as the pack contract requires.
    internal static TranslationPackMessageContract[] CreatePackMessages(string catalog, ReadOnlySpan<byte> table, int canonicalCount, ReadOnlySpan<int> extraIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalog);
        int count = Count(table, canonicalCount);
        for (int index = 0; index < extraIds.Length; index++)
        {
            if (extraIds[index] < canonicalCount || extraIds[index] >= count || (index > 0 && extraIds[index] <= extraIds[index - 1]))
                throw Malformed("The external pack's extra message IDs must be ascending IDs of permitted locale extras.");
        }
        var messages = new TranslationPackMessageContract[canonicalCount + extraIds.Length];
        int position = 0, written = 0, extra = 0;
        for (int id = 0; id < count && written < messages.Length; id++)
        {
            ReadOnlySpan<byte> line = NextLine(table, ref position);
            if (id >= canonicalCount)
            {
                if (extraIds[extra] != id) continue;
                extra++;
            }
            string name = Key(line, out ReadOnlySpan<byte> inputs);
            messages[written++] = TranslationPackMessageContract.FromRmf2Inputs(new TranslationKey(catalog, id, name), Inputs(inputs));
        }
        Array.Sort(messages, static (left, right) => string.CompareOrdinal(left.Key.Name, right.Key.Name));
        return messages;
    }

    // The number of messages; the table must end with a line break and hold at least canonicalCount lines. A catalog
    // without messages has an empty table, which the catalog itself refuses, as it refused its empty definitions before.
    private static int Count(ReadOnlySpan<byte> table, int canonicalCount)
    {
        if (table.IsEmpty && canonicalCount == 0) return 0;
        if (table.IsEmpty || table[^1] != (byte)'\n') throw Malformed("The generated message table must end with a line break.");
        int count = table.Count((byte)'\n');
        if (canonicalCount < 1 || canonicalCount > count)
            throw Malformed("The generated message table does not hold its canonical messages.");
        return count;
    }

    private static ReadOnlySpan<byte> NextLine(ReadOnlySpan<byte> table, ref int position)
    {
        ReadOnlySpan<byte> rest = table[position..];
        int end = rest.IndexOf((byte)'\n');
        position += end + 1;
        return rest[..end];
    }

    private static string Key(ReadOnlySpan<byte> line, out ReadOnlySpan<byte> inputs)
    {
        int space = line.IndexOf((byte)' ');
        ReadOnlySpan<byte> key = space < 0 ? line : line[..space];
        inputs = space < 0 ? default : line[(space + 1)..];
        if (key.IsEmpty || (space >= 0 && inputs.IsEmpty)) throw Malformed("A message of the generated message table has no key or an empty input list.");
        return Decode(key);
    }

    private static CompiledRmf2Input[] Inputs(ReadOnlySpan<byte> inputs)
    {
        if (inputs.IsEmpty) return Array.Empty<CompiledRmf2Input>();
        var result = new CompiledRmf2Input[inputs.Count((byte)' ') + 1];
        for (int index = 0; index < result.Length; index++)
        {
            int space = inputs.IndexOf((byte)' ');
            ReadOnlySpan<byte> input = space < 0 ? inputs : inputs[..space];
            inputs = space < 0 ? default : inputs[(space + 1)..];
            int colon = input.LastIndexOf((byte)':');
            if (colon <= 0) throw Malformed("An input of the generated message table has no name or type.");
            result[index] = new CompiledRmf2Input(Decode(input[..colon]), Type(input[(colon + 1)..]));
        }
        return result;
    }

    // The RMF2 carrier names, as the compiler writes them.
    private static TextArgumentType Type(ReadOnlySpan<byte> type) =>
        type.SequenceEqual("string"u8) ? TextArgumentType.String
        : type.SequenceEqual("int64"u8) ? TextArgumentType.Int
        : type.SequenceEqual("decimal"u8) ? TextArgumentType.Number
        : type.SequenceEqual("boolean"u8) ? TextArgumentType.Bool
        : type.SequenceEqual("date"u8) ? TextArgumentType.Date
        : type.SequenceEqual("time"u8) ? TextArgumentType.Time
        : type.SequenceEqual("datetime"u8) ? TextArgumentType.DateTime
        : type.SequenceEqual("guid"u8) ? TextArgumentType.Guid
        : type.SequenceEqual("list"u8) ? TextArgumentType.List
        : throw Malformed("An input of the generated message table has an unknown type.");

    private static string Decode(ReadOnlySpan<byte> utf8)
    {
        try { return StrictUtf8.GetString(utf8); }
        catch (DecoderFallbackException exception)
        {
            throw new TranslationPackException("The generated message table is not valid UTF-8.", TranslationPackFailureReason.Malformed, exception);
        }
    }

    private static TranslationPackException Malformed(string message) => new(message, TranslationPackFailureReason.Malformed);
}
