using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Runic.Translations;
using Runic.Translations.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers an <see cref="ITranslationContext"/> and its catalogs with dependency injection.</summary>
public static class RunicTranslationsServiceCollectionExtensions
{
    /// <summary>
    /// Registers a <see cref="TranslationContext"/> (also as <see cref="ITranslationContext"/>) for the catalogs that this
    /// and every other call add, and an <see cref="ITranslationContextFactory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call it from the application and from each library's own registration method; the calls share one set of
    /// registrations. Only the application passes <paramref name="configure"/>; a library calls it without options.
    /// </para>
    /// <para>
    /// The context is a singleton by default; set <see cref="RunicTranslationsOptions.Lifetime"/> to
    /// <see cref="ServiceLifetime.Scoped"/> for one context per scope, such as per web request. Generated catalogs compose
    /// without I/O, so the context is built synchronously on first use. With an overlay, whose packs load asynchronously,
    /// call <see cref="RunicTranslationsServiceProviderExtensions.InitializeRunicTranslationsAsync"/> once after building
    /// the service provider; resolving the context before that throws instead of blocking on I/O.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">The application's options; libraries pass none.</param>
    /// <exception cref="InvalidOperationException">Another call set an option to a different value.</exception>
    /// <exception cref="ArgumentException">The lifetime is <see cref="ServiceLifetime.Transient"/>.</exception>
    public static IRunicTranslationsBuilder AddRunicTranslations(this IServiceCollection services, Action<RunicTranslationsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        RunicTranslationsRegistrations registrations = Registrations(services);
        if (configure is not null) registrations.Configure(configure);
        registrations.Register(services);
        return new RunicTranslationsBuilder(services, registrations);
    }

    internal static RunicTranslationsRegistrations Registrations(IServiceCollection services)
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
            registrations.AddCatalog(registration, options);
            return this;
        }

        public IRunicTranslationsBuilder AddOverlay(TranslationOverlay overlay)
        {
            ArgumentNullException.ThrowIfNull(overlay);
            registrations.AddOverlay(overlay);
            return this;
        }
    }
}

/// <summary>Initializes the translation context of a service provider.</summary>
public static class RunicTranslationsServiceProviderExtensions
{
    /// <summary>
    /// Composes the registered catalogs and overlays for the initial locale without blocking, for example after
    /// <c>builder.Build()</c> in an ASP.NET Core application or after <c>BuildServiceProvider()</c> in a desktop application.
    /// </summary>
    /// <remarks>
    /// Required when an overlay is registered: overlay packs load asynchronously, and resolving the context before
    /// initialization throws rather than blocking on I/O. Without overlays it is optional and builds the context early.
    /// In scoped mode it composes the shared snapshots that every scope's context then starts from without I/O. Calling it
    /// again returns the first initialization.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// <c>AddRunicTranslations</c> was not called, or a context registered by the application lacks a catalog that was
    /// added through <c>AddCatalog</c>.
    /// </exception>
    public static async ValueTask InitializeRunicTranslationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        RunicTranslationsRegistrations registrations = services.GetService<RunicTranslationsRegistrations>() ??
            throw new InvalidOperationException("Runic Translations is not registered; call services.AddRunicTranslations() first.");
        await registrations.InitializeAsync(cancellationToken).ConfigureAwait(false);
        // Runs the catalog check against a context the application registered itself.
        if (registrations.Lifetime == ServiceLifetime.Singleton) _ = services.GetRequiredService<ITranslationContext>();
    }
}

/// <summary>The catalogs, overlays, options and text classes collected by every <c>AddRunicTranslations</c> call.</summary>
internal sealed class RunicTranslationsRegistrations : ITranslationContextFactory
{
    private readonly List<(TranslationCatalogRegistration Registration, TranslationOptions? Options)> _catalogs = [];
    private readonly List<TranslationOverlay> _overlays = [];
    private readonly Dictionary<Type, Text> _texts = [];
    private readonly List<ServiceDescriptor> _descriptors = [];
    private readonly Lock _gate = new();
    private ServiceLifetime? _registeredLifetime;
    private TranslationContextBuilder? _builder;
    private Task<TranslationContext>? _initialization;
    private TranslationContext? _singleton;

    internal string? InitialLocale { get; private set; }
    internal ITextValueFormatter? ValueFormatter { get; private set; }
    internal ServiceLifetime Lifetime { get; private set; } = ServiceLifetime.Singleton;

    internal void Configure(Action<RunicTranslationsOptions> configure)
    {
        var options = new RunicTranslationsOptions();
        configure(options);
        if (options.Lifetime is not (ServiceLifetime.Singleton or ServiceLifetime.Scoped))
            throw new ArgumentException("The translation context lifetime must be Singleton or Scoped.", nameof(configure));
        if (options.InitialLocale is not null)
        {
            if (InitialLocale is not null && !string.Equals(InitialLocale, options.InitialLocale, StringComparison.OrdinalIgnoreCase))
                throw Conflict(nameof(RunicTranslationsOptions.InitialLocale), InitialLocale, options.InitialLocale);
            InitialLocale = options.InitialLocale;
        }

        if (options.ValueFormatter is not null)
        {
            if (ValueFormatter is not null && !ReferenceEquals(ValueFormatter, options.ValueFormatter))
                throw Conflict(nameof(RunicTranslationsOptions.ValueFormatter), ValueFormatter.GetType().Name, options.ValueFormatter.GetType().Name);
            ValueFormatter = options.ValueFormatter;
        }

        if (options.Lifetime != ServiceLifetime.Singleton)
        {
            if (Lifetime != ServiceLifetime.Singleton && Lifetime != options.Lifetime)
                throw Conflict(nameof(RunicTranslationsOptions.Lifetime), Lifetime.ToString(), options.Lifetime.ToString());
            Lifetime = options.Lifetime;
        }

        static InvalidOperationException Conflict(string option, string existing, string value) => new(
            $"Runic Translations option {option} is set to '{existing}' and '{value}' by two AddRunicTranslations calls. " +
            "Only the application sets options; libraries call AddRunicTranslations() without them.");
    }

    internal void AddCatalog(TranslationCatalogRegistration registration, TranslationOptions? options)
    {
        lock (_gate)
        {
            _catalogs.Add((registration, options));
            _builder = null;
        }
    }

    internal void AddOverlay(TranslationOverlay overlay)
    {
        lock (_gate)
        {
            _overlays.Add(overlay);
            _builder = null;
        }
    }

    internal void AddText(IServiceCollection services, Type type, Delegate factory, Func<IServiceProvider, object> create)
    {
        if (_texts.TryGetValue(type, out Text? existing))
        {
            if (!existing.Factory.Equals(factory))
                throw new InvalidOperationException(
                    $"Text class {type.FullName} is already registered with another factory. Register a text class once, in the library that owns its catalog.");
            return;
        }

        var text = new Text(type, factory, create);
        _texts.Add(type, text);
        if (_registeredLifetime is { } lifetime) Add(services, new ServiceDescriptor(type, text.Create, lifetime));
    }

    // Registers the context and the text classes with the current lifetime, replacing earlier registrations of this
    // package when the application changed the lifetime after a library registered.
    internal void Register(IServiceCollection services)
    {
        services.TryAddSingleton<ITranslationContextFactory>(static provider => provider.GetRequiredService<RunicTranslationsRegistrations>());
        if (_registeredLifetime == Lifetime) return;
        foreach (ServiceDescriptor descriptor in _descriptors) services.Remove(descriptor);
        _descriptors.Clear();
        _registeredLifetime = Lifetime;
        Add(services, new ServiceDescriptor(typeof(TranslationContext), static provider => provider.GetRequiredService<RunicTranslationsRegistrations>().Resolve(), Lifetime));
        Add(services, new ServiceDescriptor(typeof(ITranslationContext), static provider =>
            provider.GetRequiredService<RunicTranslationsRegistrations>().Check(provider.GetRequiredService<TranslationContext>()), Lifetime));
        foreach (Text text in _texts.Values) Add(services, new ServiceDescriptor(text.Type, text.Create, Lifetime));
    }

    public ValueTask<TranslationContext> CreateAsync(string? initialLocale = null, CancellationToken cancellationToken = default) =>
        Builder().BuildAsync(initialLocale ?? InitialLocale, cancellationToken);

    internal Task<TranslationContext> InitializeAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_singleton is not null) return Task.FromResult(_singleton);
            if (_initialization is { IsFaulted: false, IsCanceled: false } running) return running;
            return _initialization = CreateAsync(null, cancellationToken).AsTask();
        }
    }

    // The context of a singleton or a scope. Never blocks: generated catalogs compose synchronously, and overlay packs
    // are loaded by InitializeRunicTranslationsAsync, whose composed snapshots later contexts reuse.
    private TranslationContext Resolve()
    {
        lock (_gate)
        {
            if (Lifetime == ServiceLifetime.Singleton && _singleton is not null) return _singleton;
            if (_initialization is { IsCompletedSuccessfully: true } initialized && Lifetime == ServiceLifetime.Singleton) return _singleton = initialized.Result;
            if (_overlays.Count > 0 && _initialization is not { IsCompletedSuccessfully: true })
                throw new InvalidOperationException(
                    "Runic Translations has overlays, whose packs load asynchronously: call await services.InitializeRunicTranslationsAsync() " +
                    "after building the service provider and before resolving ITranslationContext.");
        }

        ValueTask<TranslationContext> build = CreateAsync();
        if (!build.IsCompleted)
        {
            // Observe the abandoned composition; its snapshots still fill the shared cache.
            _ = build.AsTask().ContinueWith(static task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw new InvalidOperationException(
                "The translation context cannot be composed without I/O, for example after an overlay refresh cleared the shared snapshot. " +
                "Create the context with ITranslationContextFactory.CreateAsync where it can be awaited.");
        }

        TranslationContext context = build.GetAwaiter().GetResult();
        if (Lifetime == ServiceLifetime.Singleton)
        {
            lock (_gate) return _singleton ??= context;
        }

        return context;
    }

    // A context the application registered itself bypasses the collected registrations; fail when it lacks a catalog.
    private TranslationContext Check(TranslationContext context)
    {
        List<string> missing = [];
        lock (_gate)
        {
            foreach ((TranslationCatalogRegistration registration, _) in _catalogs)
                if (!context.TryGetManager(registration.CatalogId, out _) && !missing.Contains(registration.CatalogId)) missing.Add(registration.CatalogId);
        }

        return missing.Count == 0 ? context : throw new InvalidOperationException(
            $"The registered TranslationContext lacks catalog(s) {string.Join(", ", missing)} added through AddCatalog. Let AddRunicTranslations " +
            "build the context instead of registering an instance, and call InitializeRunicTranslationsAsync for overlays.");
    }

    private TranslationContextBuilder Builder()
    {
        lock (_gate)
        {
            if (_builder is not null) return _builder;
            TranslationContextBuilder builder = TranslationContext.CreateBuilder();
            foreach ((TranslationCatalogRegistration registration, TranslationOptions? options) in _catalogs) builder.Add(registration, options);
            foreach (TranslationOverlay overlay in _overlays) builder.AddOverlay(overlay);
            if (ValueFormatter is not null) builder.UseValueFormatter(ValueFormatter);
            return _builder = builder;
        }
    }

    private void Add(IServiceCollection services, ServiceDescriptor descriptor)
    {
        // An application that registered the service itself keeps its registration.
        foreach (ServiceDescriptor existing in services)
            if (existing.ServiceType == descriptor.ServiceType) return;
        services.Add(descriptor);
        _descriptors.Add(descriptor);
    }

    private sealed record Text(Type Type, Delegate Factory, Func<IServiceProvider, object> Create);
}
