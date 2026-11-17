using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Runic.Translations.Generated;

/// <summary>Factories that generated catalogs call to build their runtime data.</summary>
/// <remarks>
/// Generated-code support, like every type in <c>Runic.Translations.Generated</c>: binary compatible for code that an
/// earlier release of the same major generated; not intended for hand-written code, whose source may break in a minor.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GeneratedCatalogSupport
{
    /// <summary>Creates a v5 caller definition with NFC input names and type-only formatting contracts.</summary>
    public static CompiledTranslationDefinition CreateDefinition(string name, IReadOnlyList<CompiledRmf2Input> inputs, bool isCanonical = true) =>
        CompiledTranslationDefinition.FromRmf2Inputs(name, inputs, isCanonical);

    /// <summary>
    /// Creates the definitions of a generated catalog from its message table (RMF2 runtime ABI 4): one UTF-8 line per
    /// message in ID order, the key followed by <c> name:type</c> for each caller input in ordinal order (RMF2 carrier
    /// names such as <c>string</c> and <c>int64</c>), each line ending in <c>\n</c>. The first
    /// <paramref name="canonicalCount"/> lines are the canonical messages, the rest the permitted locale extras.
    /// </summary>
    /// <exception cref="TranslationPackException">The table is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationDefinition> ReadDefinitions(ReadOnlySpan<byte> messageTable, int canonicalCount) =>
        GeneratedMessageTable.ReadDefinitions(messageTable, canonicalCount);

    /// <summary>
    /// Creates the keys of a generated catalog's canonical messages from its message table (see
    /// <see cref="ReadDefinitions"/>), indexed by message ID.
    /// </summary>
    /// <exception cref="TranslationPackException">The table is not in the compiler's form.</exception>
    public static TranslationKey[] CreateKeys(string catalog, ReadOnlySpan<byte> messageTable, int canonicalCount) =>
        GeneratedMessageTable.CreateKeys(catalog, messageTable, canonicalCount);

    /// <summary>
    /// Creates an RMF2 execution-v2 contract for one resolved locale artifact v5 from a generated catalog's message table
    /// (see <see cref="ReadDefinitions"/>): every canonical message and the permitted locale extras of
    /// <paramref name="extraIds"/>, which the locale defines, in ascending ID order.
    /// </summary>
    /// <exception cref="TranslationPackException">The table or the extra IDs are not in the compiler's form.</exception>
    public static TranslationPackContract CreatePackContract(string catalog, string locale, string contractFingerprint,
        ReadOnlySpan<byte> messageTable, int canonicalCount, ReadOnlySpan<int> extraIds, string rmf2MarkupContract) =>
        TranslationPackContract.CreateRmf2V5(catalog, locale, contractFingerprint,
            GeneratedMessageTable.CreatePackMessages(catalog, messageTable, canonicalCount, extraIds), rmf2MarkupContract);

    /// <summary>Creates a locale whose values a generated catalog reads on first use.</summary>
    public static CompiledTranslationLocale CreateDeferredLocale(string locale, string? fallbackLocale, Func<IReadOnlyList<CompiledTranslationValue>> valueFactory) =>
        CompiledTranslationLocale.CreateDeferred(locale, fallbackLocale, valueFactory);

    /// <summary>Reads the direct values that a generated catalog embeds for one locale.</summary>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationValue> ReadValues(ReadOnlySpan<byte> utf8Json) =>
        CompiledTranslationLocale.ReadRmf2Values(utf8Json);

    /// <summary>
    /// Reads the direct values that a generated catalog embeds for one locale, with the pinned CLDR currency, unit and
    /// compact-notation data the compiler selected for them (only what the locale's messages use).
    /// </summary>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationValue> ReadValues(ReadOnlySpan<byte> utf8Json, ReadOnlySpan<byte> formatData) =>
        CompiledTranslationLocale.ReadRmf2Values(utf8Json, Rmf2FormatData.Parse(formatData));

    /// <summary>
    /// Reads the direct values that a generated catalog embeds for one locale as raw DEFLATE data (RFC 1951) of the
    /// compiler's UTF-8 JSON (RMF2 runtime ABI 4). The JSON is inflated into a pooled buffer, read as
    /// <see cref="ReadValues(ReadOnlySpan{byte})"/> reads it, and the buffer returned: the generated catalog calls this once
    /// per locale, on the locale's first use.
    /// </summary>
    /// <param name="deflatedUtf8Json">The DEFLATE data.</param>
    /// <param name="length">The length of the inflated JSON in bytes, which the data must match exactly.</param>
    /// <exception cref="TranslationPackException">The data does not inflate to <paramref name="length"/> bytes, or is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationValue> ReadDeflatedValues(ReadOnlySpan<byte> deflatedUtf8Json, int length) =>
        DeflatedLocaleData.Read(deflatedUtf8Json, length, null);

    /// <summary>
    /// Reads the direct values that a generated catalog embeds for one locale as raw DEFLATE data, with the pinned CLDR
    /// currency, unit and compact-notation data the compiler selected for them; see <see cref="ReadDeflatedValues(ReadOnlySpan{byte}, int)"/>.
    /// </summary>
    /// <exception cref="TranslationPackException">The data does not inflate to <paramref name="length"/> bytes, or is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationValue> ReadDeflatedValues(ReadOnlySpan<byte> deflatedUtf8Json, int length, ReadOnlySpan<byte> formatData) =>
        DeflatedLocaleData.Read(deflatedUtf8Json, length, Rmf2FormatData.Parse(formatData));

    /// <summary>
    /// Inflates UTF-8 text that a generated catalog embeds as raw DEFLATE data (RMF2 runtime ABI 4), such as its
    /// <c>Rmf2MarkupContract</c>: a few kilobytes of data instead of a UTF-16 string literal of its JSON.
    /// </summary>
    /// <param name="deflatedUtf8">The DEFLATE data.</param>
    /// <param name="length">The length of the inflated text in UTF-8 bytes, which the data must match exactly.</param>
    /// <exception cref="TranslationPackException">The data does not inflate to <paramref name="length"/> bytes of UTF-8.</exception>
    public static string ReadDeflatedText(ReadOnlySpan<byte> deflatedUtf8, int length) => DeflatedLocaleData.ReadText(deflatedUtf8, length);

    /// <summary>Copies a closed argument carrier under an NFC RMF2 input name.</summary>
    public static TextArgument CreateArgument(string name, TextArgument value) => TextArgument.CreateRmf2(name, value);

    // The typed overloads below are the argument of a generated accessor: one shared call instead of the carrier's
    // construction and validation inlined into every accessor (D072.2). Each equals
    // CreateArgument(name, new TextArgument("_", value)), with the carrier's default format.

    /// <summary>Creates a string argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, string value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates an integer argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, long value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a number argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, decimal value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a Boolean argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, bool value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a date argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, DateOnly value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a time argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, TimeOnly value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a date-time argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, DateTimeOffset value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a GUID argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, Guid value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a list argument with its default format under an NFC RMF2 input name.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static TextArgument CreateArgument(string name, IReadOnlyList<string> value) => TextArgument.CreateRmf2(name, new TextArgument("_", value));

    /// <summary>Creates a v5 external-pack message contract from NFC RMF2 caller inputs.</summary>
    public static TranslationPackMessageContract CreatePackMessageContract(TranslationKey key, IReadOnlyList<CompiledRmf2Input> inputs) =>
        TranslationPackMessageContract.FromRmf2Inputs(key, inputs);

    /// <summary>Creates an RMF2 execution-v2 contract for one resolved locale artifact v5.</summary>
    public static TranslationPackContract CreatePackContract(string catalog, string locale, string contractFingerprint,
        IReadOnlyList<TranslationPackMessageContract> messages, string rmf2MarkupContract) =>
        TranslationPackContract.CreateRmf2V5(catalog, locale, contractFingerprint, messages, rmf2MarkupContract);
}
