using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runic.CommandLine;
using Runic.Translations;
using Runic.Translations.CommandLine;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers Runic.CommandLine's English and German framework text with <c>Runic.Translations.Hosting</c>.</summary>
public static class RunicCommandLineTranslationsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the <c>runic-command-line</c> catalog (<see cref="TranslationCommandTextResolver.CatalogRegistration"/>) to the
    /// application's translation context and registers an <see cref="ICommandTextResolver"/> over that context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The resolver follows the context's locale, so one <c>SetLocaleAsync</c> switches the application's text and the
    /// command line's together, and the context's overlays apply to the framework text: a build-time override
    /// (<c>translations/overrides/runic-command-line/&lt;locale&gt;.rmf2</c>, added with <c>AddOverlays(AppTextCatalog.Overlays)</c>)
    /// or a run-time overlay. Messages of <paramref name="applicationCatalogId"/> named after a framework key override that
    /// key too, as with <see cref="TranslationCommandTextResolver(ITranslationContext, string?)"/>.
    /// </para>
    /// <para>
    /// The resolver is transient: it resolves the context of the scope it is created in, so it works with a singleton
    /// context and with <c>options.Lifetime = ServiceLifetime.Scoped</c>. Calling this more than once registers once; a
    /// resolver the application registers itself wins.
    /// </para>
    /// </remarks>
    /// <param name="services">The application's services.</param>
    /// <param name="applicationCatalogId">The catalog of the application's own text, or <see langword="null"/> for none. It must be registered with the context.</param>
    /// <returns><paramref name="services"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddRunicCommandLineTranslations(this IServiceCollection services, string? applicationCatalogId = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(RunicCommandLineTranslationsRegistration))) return services;
        services.AddSingleton(new RunicCommandLineTranslationsRegistration());
        services.AddRunicTranslations().AddCatalog(TranslationCommandTextResolver.CatalogRegistration);
        services.TryAddTransient<ICommandTextResolver>(provider =>
            new TranslationCommandTextResolver(provider.GetRequiredService<ITranslationContext>(), applicationCatalogId));
        return services;
    }
}

// Marks that AddRunicCommandLineTranslations ran, so a second call registers nothing.
internal sealed class RunicCommandLineTranslationsRegistration;
