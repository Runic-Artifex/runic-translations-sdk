using System;
using System.Collections.Generic;
using System.ComponentModel;

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

    /// <summary>Creates a locale whose values a generated catalog reads on first use.</summary>
    public static CompiledTranslationLocale CreateDeferredLocale(string locale, string? fallbackLocale, Func<IReadOnlyList<CompiledTranslationValue>> valueFactory) =>
        CompiledTranslationLocale.CreateDeferred(locale, fallbackLocale, valueFactory);

    /// <summary>Reads the direct values that a generated catalog embeds for one locale.</summary>
    /// <exception cref="TranslationPackException">The data is not in the compiler's form.</exception>
    public static IReadOnlyList<CompiledTranslationValue> ReadValues(ReadOnlySpan<byte> utf8Json) =>
        CompiledTranslationLocale.ReadRmf2Values(utf8Json);

    /// <summary>Copies a closed argument carrier under an NFC RMF2 input name.</summary>
    public static TextArgument CreateArgument(string name, TextArgument value) => TextArgument.CreateRmf2(name, value);

    /// <summary>Creates a v5 external-pack message contract from NFC RMF2 caller inputs.</summary>
    public static TranslationPackMessageContract CreatePackMessageContract(TranslationKey key, IReadOnlyList<CompiledRmf2Input> inputs) =>
        TranslationPackMessageContract.FromRmf2Inputs(key, inputs);

    /// <summary>Creates an RMF2 execution-v2 contract for one resolved locale artifact v5.</summary>
    public static TranslationPackContract CreatePackContract(string catalog, string locale, string contractFingerprint,
        IReadOnlyList<TranslationPackMessageContract> messages, string rmf2MarkupContract) =>
        TranslationPackContract.CreateRmf2V5(catalog, locale, contractFingerprint, messages, rmf2MarkupContract);
}
