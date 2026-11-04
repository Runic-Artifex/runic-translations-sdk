using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Runic.Translations.Hosting;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// Runic.Translations.Hosting: a context from dependency injection, with catalogs added by several callers.
internal sealed class HostingTests
{
    private const string Fingerprint = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string MarkupContract = """{"version":2,"contracts":{},"messages":{"title":{"slots":{},"structured":false,"contentLocales":{"de":"de","en":"en"},"content":"inline","skeletons":[]}}}""";

    [Test, DisplayName("AddRunicTranslations shares one context between an application and its libraries")]
    public async Task SharedContext()
    {
        var services = new ServiceCollection();
        // A library's own registration method runs first and sets no options.
        AddLibrary(services);
        services.AddRunicTranslations(options => options.InitialLocale = "de")
            .AddCatalog(Registration("app", "Hello", "Hallo"), static context => new AppText(context));
        // The library's method called again registers the same catalog and text class factory again.
        AddLibrary(services);

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        ITranslationContext context = provider.GetRequiredService<ITranslationContext>();
        Assert.Same(context, provider.GetRequiredService<TranslationContext>());
        Assert.Equal("de|2", context.RequestedLocale + "|" + context.Catalogs.Count);
        AppText app = provider.GetRequiredService<AppText>();
        Assert.Same(app, provider.GetRequiredService<AppText>());
        Assert.Equal("Hallo", app.Value);
        await context.SetLocaleAsync("en");
        Assert.Equal("Hello|Grid", app.Value + "|" + provider.GetRequiredService<LibText>().Value);
    }

    [Test, DisplayName("the application's initial locale wins regardless of order, and conflicting options throw")]
    public void ApplicationOwnsOptions()
    {
        var services = new ServiceCollection();
        services.AddRunicTranslations(options => options.InitialLocale = "de").AddCatalog(Registration("app", "Hello", "Hallo"));
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"));
        using (ServiceProvider provider = services.BuildServiceProvider())
            Assert.Equal("de", provider.GetRequiredService<ITranslationContext>().RequestedLocale);

        InvalidOperationException conflict = Assert.Throws<InvalidOperationException>(() =>
            services.AddRunicTranslations(options => options.InitialLocale = "en"));
        Assert.True(conflict.Message.Contains("InitialLocale", StringComparison.Ordinal), conflict.Message);
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddRunicTranslations(options => options.Lifetime = ServiceLifetime.Transient));
    }

    [Test, DisplayName("a text class registered again with another factory throws")]
    public void DuplicateTextFactory()
    {
        var services = new ServiceCollection();
        services.AddRunicTranslations().AddCatalog(Registration("app", "Hello", "Hallo"), static context => new AppText(context));
        InvalidOperationException duplicate = Assert.Throws<InvalidOperationException>(() =>
            services.AddRunicTranslations().AddCatalog(Registration("app", "Hello", "Hallo"), static context => new AppText(context, "other")));
        Assert.True(duplicate.Message.Contains(typeof(AppText).FullName!, StringComparison.Ordinal), duplicate.Message);
    }

    [Test, DisplayName("text classes use the context constructor even when the container also has an ITranslationManager")]
    public void TextClassesBesideManager()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITranslationManager>(new TranslationManager(Registration("app", "Hello", "Hallo").CreateProvider(),
            new CompiledTranslationSnapshot(Catalog("app", "Hello", "Hallo"), "en")));
        services.AddRunicTranslations(options => options.InitialLocale = "de")
            .AddCatalog(Registration("app", "Hello", "Hallo"), static context => new GeneratedLikeText(context));
        using ServiceProvider provider = services.BuildServiceProvider();
        GeneratedLikeText text = provider.GetRequiredService<GeneratedLikeText>();
        Assert.Equal("context|Hallo", text.Constructor + "|" + text.Value);
        // A type registration cannot choose between the two constructors; this is why AddCatalog takes a factory.
        var ambiguous = new ServiceCollection();
        ambiguous.AddSingleton(provider.GetRequiredService<ITranslationManager>());
        ambiguous.AddSingleton(provider.GetRequiredService<ITranslationContext>());
        ambiguous.AddSingleton<GeneratedLikeText>();
        using ServiceProvider typeActivated = ambiguous.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => typeActivated.GetRequiredService<GeneratedLikeText>());
    }

    [Test, DisplayName("scoped mode gives each scope its own context, so one request's switch does not affect another")]
    public async Task ScopedContexts()
    {
        var services = new ServiceCollection();
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"), static context => new LibText(context));
        // The application chooses the scoped lifetime after the library registered.
        services.AddRunicTranslations(options => { options.Lifetime = ServiceLifetime.Scoped; options.InitialLocale = "en"; })
            .AddCatalog(Registration("app", "Hello", "Hallo"));
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using IServiceScope first = provider.CreateScope(), second = provider.CreateScope();
        ITranslationContext firstContext = first.ServiceProvider.GetRequiredService<ITranslationContext>();
        Assert.Same(firstContext, first.ServiceProvider.GetRequiredService<TranslationContext>());
        await firstContext.SetLocaleAsync("de");
        Assert.Equal("Raster|Grid", first.ServiceProvider.GetRequiredService<LibText>().Value + "|" + second.ServiceProvider.GetRequiredService<LibText>().Value);
        Assert.True(!ReferenceEquals(firstContext, second.ServiceProvider.GetRequiredService<ITranslationContext>()), "one context per scope");
        // A scoped context cannot be resolved from the root provider.
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ITranslationContext>());

        // The factory creates further contexts that can be awaited.
        TranslationContext window = await provider.GetRequiredService<ITranslationContextFactory>().CreateAsync("de");
        Assert.Equal("de|2", window.RequestedLocale + "|" + window.Catalogs.Count);
    }

    [Test, DisplayName("with overlays, the context is composed by InitializeRunicTranslationsAsync and keeps every library catalog")]
    public async Task AsyncInitialization()
    {
        var source = new SlowPackSource();
        var services = new ServiceCollection();
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"), static context => new LibText(context));
        services.AddRunicTranslations(options => options.InitialLocale = "de")
            .AddCatalog(Registration("app", "Hello", "Hallo"), static context => new AppText(context))
            .AddOverlay(TranslationOverlay.FromPacks("lib", source));
        await using ServiceProvider provider = services.BuildServiceProvider();

        // Resolving before initialization would block on the overlay source; it throws instead.
        InvalidOperationException early = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<LibText>());
        Assert.True(early.Message.Contains("InitializeRunicTranslationsAsync", StringComparison.Ordinal), early.Message);

        await provider.InitializeRunicTranslationsAsync();
        Assert.True(source.Loads > 0, "the overlay source was asked for packs");
        Assert.Equal("Raster|Hallo", provider.GetRequiredService<LibText>().Value + "|" + provider.GetRequiredService<AppText>().Value);
        await provider.InitializeRunicTranslationsAsync();

        // Scoped mode: every scope starts from the composed snapshots without I/O.
        var scoped = new ServiceCollection();
        scoped.AddRunicTranslations(options => options.Lifetime = ServiceLifetime.Scoped).AddCatalog(Registration("lib", "Grid", "Raster"),
            static context => new LibText(context)).AddOverlay(TranslationOverlay.FromPacks("lib", source));
        await using ServiceProvider scopedProvider = scoped.BuildServiceProvider();
        await scopedProvider.InitializeRunicTranslationsAsync();
        using IServiceScope scope = scopedProvider.CreateScope();
        Assert.Equal("Grid", scope.ServiceProvider.GetRequiredService<LibText>().Value);
    }

    [Test, DisplayName("a context registered by the application must contain the catalogs added through AddCatalog")]
    public async Task PreRegisteredContextIsChecked()
    {
        var services = new ServiceCollection();
        services.AddSingleton(await TranslationContext.CreateBuilder().Add(Registration("app", "Hello", "Hallo")).BuildAsync());
        services.AddRunicTranslations().AddCatalog(Registration("app", "Hello", "Hallo"));
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"), static context => new LibText(context));
        await using ServiceProvider provider = services.BuildServiceProvider();
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<LibText>());
        Assert.True(missing.Message.Contains("lib", StringComparison.Ordinal), missing.Message);
    }

    [Test, DisplayName("AddRunicTranslations validates its arguments")]
    public async Task Arguments()
    {
        IRunicTranslationsBuilder builder = new ServiceCollection().AddRunicTranslations();
        Assert.Throws<ArgumentNullException>(() => builder.AddCatalog(null!));
        Assert.Throws<ArgumentNullException>(() => builder.AddOverlay(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddRunicTranslations());
        await using ServiceProvider empty = new ServiceCollection().BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => empty.InitializeRunicTranslationsAsync().AsTask());
    }

    // A library's registration method, as AddAcmeGrid would be.
    private static void AddLibrary(IServiceCollection services) =>
        services.AddRunicTranslations().AddCatalog(Registration("lib", "Grid", "Raster"), static context => new LibText(context));

    private static CompiledTranslationCatalog Catalog(string id, string english, string german) =>
        new(id, "en", [new CompiledTranslationDefinition("title", [])],
            [new CompiledTranslationLocale("de", "en", [new(0, german)]), new CompiledTranslationLocale("en", null, [new(0, english)])]);

    private static TranslationCatalogRegistration Registration(string id, string english, string german)
    {
        CompiledTranslationCatalog catalog = Catalog(id, english, german);
        return new TranslationCatalogRegistration(id, "en", Fingerprint, MarkupContract,
            (formatter, factory, options) => new CompiledTranslationProvider(catalog.WithOptions(options), formatter, factory));
    }

    private static string Title(ITranslationManager manager) =>
        manager.Current.TryGetKey("title", out TranslationKey key) ? manager.Current.Get(key) : "?";

    // Stand in for generated text classes over a context.
    private sealed class AppText(ITranslationContext context, string tag = "")
    {
        private readonly ITranslationManager _manager = context.GetManager("app");

        public string Tag { get; } = tag;

        public string Value => Title(_manager);
    }

    private sealed class LibText(ITranslationContext context)
    {
        private readonly ITranslationManager _manager = context.GetManager("lib");

        public string Value => Title(_manager);
    }

    // Has both generated constructors.
    private sealed class GeneratedLikeText
    {
        private readonly ITranslationManager _manager;

        public GeneratedLikeText(ITranslationManager manager)
        {
            _manager = manager;
            Constructor = "manager";
        }

        public GeneratedLikeText(ITranslationContext context)
        {
            _manager = context.GetManager("app");
            Constructor = "context";
        }

        public string Constructor { get; }

        public string Value => Title(_manager);
    }

    // An overlay source that loads asynchronously, like a download; it has no packs.
    private sealed class SlowPackSource : IExternalTranslationSource
    {
        private int _loads;

        internal int Loads => Volatile.Read(ref _loads);

        public async ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _loads);
            await Task.Delay(20, cancellationToken);
            return null;
        }
    }
}
