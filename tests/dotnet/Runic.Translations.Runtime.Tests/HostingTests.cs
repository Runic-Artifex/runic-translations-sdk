using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Runic.Translations.Hosting;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// Runic.Translations.Hosting: one shared context from dependency injection, with catalogs added by several callers.
internal sealed class HostingTests
{
    private const string Fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Test, DisplayName("AddRunicTranslations shares one context between an application and its libraries")]
    public async Task SharedContext()
    {
        var services = new ServiceCollection();
        services.AddRunicTranslations(options => options.InitialLocale = "en")
            .AddCatalog(Registration("app", "Hello", "Hallo"), static context => new Text(context, "app"));
        // A library's own registration method, and a repeated registration of the same catalog.
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"), static context => new Text(context, "lib"));
        services.AddRunicTranslations(options => options.InitialLocale = "de").AddCatalog(Registration("lib", "Grid", "Raster"));

        using ServiceProvider provider = services.BuildServiceProvider();
        ITranslationContext context = provider.GetRequiredService<ITranslationContext>();
        Assert.Same(context, provider.GetRequiredService<TranslationContext>());
        Assert.Equal("de|2", context.RequestedLocale + "|" + context.Catalogs.Count);
        Text app = provider.GetRequiredService<Text>();
        Assert.Equal("Hallo", app.Value);
        await context.SetLocaleAsync("en");
        Assert.Equal("Hello|Grid", app.Value + "|" + new Text(context, "lib").Value);
    }

    [Test, DisplayName("AddRunicTranslations validates its arguments")]
    public void Arguments()
    {
        IRunicTranslationsBuilder builder = new ServiceCollection().AddRunicTranslations();
        Assert.Throws<ArgumentNullException>(() => builder.AddCatalog(null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddOverlay(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRunicTranslations());
    }

    private static TranslationCatalogRegistration Registration(string id, string english, string german)
    {
        var catalog = new CompiledTranslationCatalog(id, "en", [new CompiledTranslationDefinition("title", [])],
            [new CompiledTranslationLocale("de", "en", [new(0, german)]), new CompiledTranslationLocale("en", null, [new(0, english)])]);
        return new TranslationCatalogRegistration(id, "en", Fingerprint, "{}",
            (formatter, factory, options) => new CompiledTranslationProvider(catalog.WithOptions(options), formatter, factory));
    }

    // Stands in for a generated text class over a context.
    private sealed class Text(ITranslationContext context, string catalog)
    {
        private readonly ITranslationManager _manager = context.GetManager(catalog);

        public string Value => _manager.Current.TryGetKey("title", out TranslationKey key) ? _manager.Current.Get(key) : "?";
    }
}
