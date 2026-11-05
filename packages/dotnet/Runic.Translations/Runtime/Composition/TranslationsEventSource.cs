using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;

namespace Runic.Translations;

/// <summary>
/// Development signals of the runtime, under the event source name <c>Runic-Translations</c>.
/// </summary>
/// <remarks>
/// A catalog that resolves a requested locale to another locale (<see cref="LocaleResolution.Parent"/>,
/// <see cref="LocaleResolution.Related"/> or <see cref="LocaleResolution.Default"/>) when a context publishes is reported at the <see cref="EventLevel.Verbose"/>
/// level, for <c>dotnet-trace</c>, <c>dotnet-counters</c> or an <see cref="EventListener"/>, and to an attached debugger's
/// output. Nothing is computed when neither listens.
/// </remarks>
[EventSource(Name = "Runic-Translations")]
internal sealed class TranslationsEventSource : EventSource
{
    internal static readonly TranslationsEventSource Log = new();

    private TranslationsEventSource()
    {
    }

    internal static void ReportResolutions(IReadOnlyList<TranslationCatalogStatus> statuses)
    {
        bool traced = Log.IsEnabled(EventLevel.Verbose, EventKeywords.All), debugged = Debugger.IsLogging();
        if (!traced && !debugged) return;
        foreach (TranslationCatalogStatus status in statuses)
        {
            if (status.LocaleResolution == LocaleResolution.Exact) continue;
            if (traced)
            {
                if (status.LocaleResolution == LocaleResolution.Parent) Log.LocaleParentFallback(status.CatalogId, status.RequestedLocale, status.Locale);
                else if (status.LocaleResolution == LocaleResolution.Related) Log.LocaleRelatedMatch(status.CatalogId, status.RequestedLocale, status.Locale);
                else Log.LocaleDefaultFallback(status.CatalogId, status.RequestedLocale, status.Locale);
            }

            if (debugged)
            {
                Debugger.Log(0, "Runic.Translations", FormattableString.Invariant(
                    $"Catalog '{status.CatalogId}' shows '{status.Locale}' for requested locale '{status.RequestedLocale}' ({status.LocaleResolution}).{Environment.NewLine}"));
            }
        }
    }

    /// <summary>A catalog lacks the requested locale and shows a parent locale.</summary>
    [Event(1, Level = EventLevel.Verbose, Message = "Catalog '{0}' lacks requested locale '{1}' and shows its parent '{2}'.")]
    public void LocaleParentFallback(string catalog, string requestedLocale, string locale) => WriteEvent(1, catalog, requestedLocale, locale);

    /// <summary>A catalog lacks the requested locale and its parents and shows its default locale.</summary>
    [Event(2, Level = EventLevel.Verbose, Message = "Catalog '{0}' lacks requested locale '{1}' and shows its default locale '{2}'.")]
    public void LocaleDefaultFallback(string catalog, string requestedLocale, string locale) => WriteEvent(2, catalog, requestedLocale, locale);

    /// <summary>A catalog lacks the requested locale and its parents and shows a locale CLDR likely subtags match.</summary>
    [Event(3, Level = EventLevel.Verbose, Message = "Catalog '{0}' lacks requested locale '{1}' and shows the related locale '{2}'.")]
    public void LocaleRelatedMatch(string catalog, string requestedLocale, string locale) => WriteEvent(3, catalog, requestedLocale, locale);
}
