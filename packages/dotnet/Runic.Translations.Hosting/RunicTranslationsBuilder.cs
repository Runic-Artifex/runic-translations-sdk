using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Runic.Translations.Hosting;

/// <summary>Adds catalogs and overlays to the shared translation registrations.</summary>
public interface IRunicTranslationsBuilder
{
    /// <summary>The service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Registers a catalog, such as <c>GridTextCatalog.Registration</c>.</summary>
    /// <remarks>
    /// A catalog registered again with the same contract fingerprint is ignored, and options belong to one registration;
    /// see <see cref="TranslationContextBuilder.Add"/>. Conflicts throw when the context is built.
    /// </remarks>
    IRunicTranslationsBuilder AddCatalog(TranslationCatalogRegistration registration, TranslationOptions? options = null);

    /// <summary>Attaches an overlay to the catalog it names.</summary>
    /// <remarks>
    /// Overlay packs are loaded asynchronously, so with an overlay the application must call
    /// <see cref="RunicTranslationsServiceProviderExtensions.InitializeRunicTranslationsAsync"/> before the context is resolved.
    /// </remarks>
    IRunicTranslationsBuilder AddOverlay(TranslationOverlay overlay);
}

/// <summary>Application-owned options of the translation context.</summary>
/// <remarks>
/// Only the application sets these, in its own <c>AddRunicTranslations(options =&gt; ...)</c> call; a library calls
/// <c>AddRunicTranslations()</c> without options. Two calls that set one option to different values throw
/// <see cref="InvalidOperationException"/>.
/// </remarks>
public sealed class RunicTranslationsOptions
{
    /// <summary>The initial requested locale; the default locale of the first registered catalog when null.</summary>
    public string? InitialLocale { get; set; }

    /// <summary>The value formatter of every catalog; the default formatter when null.</summary>
    public ITextValueFormatter? ValueFormatter { get; set; }

    /// <summary>
    /// The lifetime of <see cref="ITranslationContext"/> and the text classes registered with
    /// <see cref="RunicTranslationsBuilderExtensions.AddCatalog{TText}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="ServiceLifetime.Singleton"/> (the default) gives the process one context, for a desktop application
    /// whose user picks one language. <see cref="ServiceLifetime.Scoped"/> gives every scope its own context, for a web
    /// application in which each request switches to its user's locale without affecting other requests; the contexts
    /// share their catalogs' providers and composed snapshots. <see cref="ServiceLifetime.Transient"/> is not supported.
    /// </remarks>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Singleton;
}

/// <summary>Creates translation contexts from the registered catalogs and overlays, for example one per window.</summary>
/// <remarks>
/// Contexts share their catalogs' providers and composed snapshots with the registered context, so a further context costs
/// a snapshot lookup per catalog. Each owns its requested locale.
/// </remarks>
public interface ITranslationContextFactory
{
    /// <summary>Creates a context for <paramref name="initialLocale"/>, the configured initial locale when null.</summary>
    ValueTask<TranslationContext> CreateAsync(string? initialLocale = null, CancellationToken cancellationToken = default);
}

/// <summary>Registers generated text classes over the translation context.</summary>
public static class RunicTranslationsBuilderExtensions
{
    /// <summary>
    /// Registers a catalog and its generated text class, created over the translation context with the context's lifetime,
    /// for example <c>AddCatalog(GridTextCatalog.Registration, context =&gt; new GridText(context))</c>.
    /// </summary>
    /// <remarks>
    /// Register a text class in one place, normally the library that owns its catalog. Registering it again with the same
    /// factory is ignored; another factory throws <see cref="InvalidOperationException"/>. Use this factory overload rather
    /// than a type registration such as <c>services.AddSingleton&lt;GridText&gt;()</c>: the generated class has a
    /// constructor over an <see cref="ITranslationManager"/> and one over an <see cref="ITranslationContext"/>, between
    /// which the container cannot choose when it can supply both.
    /// </remarks>
    /// <exception cref="InvalidOperationException"><typeparamref name="TText"/> is already registered with another factory.</exception>
    public static IRunicTranslationsBuilder AddCatalog<TText>(this IRunicTranslationsBuilder builder, TranslationCatalogRegistration registration,
        Func<ITranslationContext, TText> textFactory, TranslationOptions? options = null)
        where TText : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(textFactory);
        builder.AddCatalog(registration, options);
        RunicTranslationsServiceCollectionExtensions.Registrations(builder.Services).AddText(builder.Services, typeof(TText), textFactory,
            provider => textFactory(provider.GetRequiredService<ITranslationContext>()));
        return builder;
    }
}
