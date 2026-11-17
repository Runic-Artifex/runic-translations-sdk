using System;
using System.Collections.Generic;

namespace Runic.Translations;

/// <summary>
/// Composes a generated catalog's provider with its overlays for <see cref="TranslationContextBuilder"/>. The factory is
/// installed by the <see cref="TranslationOverlay"/> constructor, the only way to create an overlay, so the trimmer and
/// NativeAOT keep <see cref="OverlayTranslationProvider"/>, its pack loading and the markup contract linking only in an
/// application that creates an overlay. A context without overlays never calls it, as before.
/// </summary>
internal static class OverlayComposition
{
    private static Func<CompiledTranslationProvider, TranslationCatalogRegistration, IReadOnlyList<TranslationOverlay>, (ITranslationProvider Provider, string[] SupportedLocales)>? s_compose;

    internal static void Enable() => s_compose ??= static (generated, registration, overlays) =>
    {
        var overlaid = new OverlayTranslationProvider(generated.Catalog, registration.LinkMarkupContract(), generated.ValueFormatter, overlays);
        return (overlaid, overlaid.SupportedLocales);
    };

    // Called only with at least one overlay, whose constructor installed the factory.
    internal static (ITranslationProvider Provider, string[] SupportedLocales) Compose(CompiledTranslationProvider generated, TranslationCatalogRegistration registration,
        IReadOnlyList<TranslationOverlay> overlays) =>
        (s_compose ?? throw new InvalidOperationException("Overlays were attached without an overlay."))(generated, registration, overlays);
}
