using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runic.Translations;
using Runic.Translations.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers a shared <see cref="ITranslationContext"/> and its catalogs with dependency injection.</summary>
public static class RunicTranslationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers one <see cref="TranslationContext"/> (also as <see cref="ITranslationContext"/>) for the catalogs that
    /// this and every other call add.
    /// </summary>
    /// <remarks>
    /// Call it from the application and from each library's own registration method; the calls share one context. The
    /// context is built on first use with <see cref="TranslationContextBuilder.Build"/>, which composes generated catalogs
    /// without I/O. For overlays whose sources do I/O, build the context with
    /// <see cref="TranslationContextBuilder.BuildAsync"/> at startup and register the instance instead.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Sets the initial locale and value formatter; every call's action runs, in call order.</param>
    public static IRunicTranslationsBuilder AddRunicTranslations(this IServiceCollection services, Action<RunicTranslationsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        RunicTranslationsRegistrations registrations = Registrations(services);
        if (configure is not null) registrations.Configure.Add(configure);
        services.TryAddSingleton(static provider => provider.GetRequiredService<RunicTranslationsRegistrations>().CreateContext());
        services.TryAddSingleton<ITranslationContext>(static provider => provider.GetRequiredService<TranslationContext>());
        return new RunicTranslationsBuilder(services, registrations);
    }

    private static RunicTranslationsRegistrations Registrations(IServiceCollection services)
    {
        foreach (ServiceDescriptor descriptor in services)
            if (descriptor.ServiceType == typeof(RunicTranslationsRegistrations) && descriptor.ImplementationInstance is RunicTranslationsRegistrations existing)
                return existing;
        var created = new RunicTranslationsRegistrations();
        services.AddSingleton(created);
        return created;
    }

    private sealed class RunicTranslationsBuilder(IServiceCollection services, RunicTranslationsRegistrations registrations) : IRunicTranslationsBuilder
    {
        public IServiceCollection Services { get; } = services;

        public IRunicTranslationsBuilder AddCatalog(TranslationCatalogRegistration registration, TranslationOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(registration);
            registrations.Catalogs.Add((registration, options));
            return this;
        }

        public IRunicTranslationsBuilder AddOverlay(TranslationOverlay overlay)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            registrations.Overlays.Add(overlay);
            return this;
        }
    }
}

/// <summary>The catalogs, overlays and options collected by every <c>AddRunicTranslations</c> call.</summary>
internal sealed class RunicTranslationsRegistrations
{
    internal List<(TranslationCatalogRegistration Registration, TranslationOptions? Options)> Catalogs { get; } = [];
    internal List<TranslationOverlay> Overlays { get; } = [];
    internal List<Action<RunicTranslationsOptions>> Configure { get; } = [];

    internal TranslationContext CreateContext()
    {
        var options = new RunicTranslationsOptions();
        foreach (Action<RunicTranslationsOptions> configure in Configure) configure(options);
        TranslationContextBuilder builder = TranslationContext.CreateBuilder();
        foreach ((TranslationCatalogRegistration registration, TranslationOptions? catalogOptions) in Catalogs) builder.Add(registration, catalogOptions);
        foreach (TranslationOverlay overlay in Overlays) builder.AddOverlay(overlay);
        if (options.ValueFormatter is not null) builder.UseValueFormatter(options.ValueFormatter);
        return builder.Build(options.InitialLocale);
    }
}
