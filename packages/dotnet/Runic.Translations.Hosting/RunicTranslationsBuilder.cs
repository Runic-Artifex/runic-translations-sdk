using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Runic.Translations.Hosting;

/// <summary>Adds catalogs and overlays to the shared translation context.</summary>
public interface IRunicTranslationsBuilder
{
    /// <summary>The service collection.</summary>
    IServiceCollection Services { get; }

    /// <summary>Registers a catalog, such as <c>GridTextCatalog.Registration</c>.</summary>
    /// <remarks>A catalog registered again with the same contract fingerprint is ignored; see <see cref="TranslationContextBuilder.Add"/>.</remarks>
    IRunicTranslationsBuilder AddCatalog(TranslationCatalogRegistration registration, TranslationOptions? options = null);

    /// <summary>Attaches an overlay to the catalog it names.</summary>
    IRunicTranslationsBuilder AddOverlay(TranslationOverlay overlay);
}

/// <summary>Options of the shared translation context.</summary>
public sealed class RunicTranslationsOptions
{
    /// <summary>The initial requested locale; the default locale of the first registered catalog when null.</summary>
    public string? InitialLocale { get; set; }

    /// <summary>The value formatter of every catalog; the default formatter when null.</summary>
    public ITextValueFormatter? ValueFormatter { get; set; }
}

/// <summary>Registers generated text classes over the shared context.</summary>
public static class RunicTranslationsBuilderExtensions
{
    /// <summary>
    /// Registers a catalog and its generated text class as a singleton, created over the shared context, for example
    /// <c>AddCatalog(GridTextCatalog.Registration, context =&gt; new GridText(context))</c>.
    /// </summary>
    public static IRunicTranslationsBuilder AddCatalog<TText>(this IRunicTranslationsBuilder builder, TranslationCatalogRegistration registration,
        Func<ITranslationContext, TText> textFactory, TranslationOptions? options = null)
        where TText : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(textFactory);
        builder.AddCatalog(registration, options);
        builder.Services.TryAddSingleton(provider => textFactory(provider.GetRequiredService<ITranslationContext>()));
        return builder;
    }
}
