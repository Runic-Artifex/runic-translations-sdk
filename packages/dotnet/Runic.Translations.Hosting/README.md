# Runic.Translations.Hosting

Register one shared Runic Translations context, its catalogs and overlays with `Microsoft.Extensions.DependencyInjection`.
An application and each library it uses add their catalogs; every catalog switches to one locale together.

## Install

```bash
dotnet add package Runic.Translations.Hosting --version <VERSION>
```

Replace `<VERSION>` with the current version shown on NuGet. The package targets .NET 10 and depends on
`Runic.Translations` and `Microsoft.Extensions.DependencyInjection.Abstractions`.

## Register catalogs

The application passes its options once; each library registers its own catalog in its own registration method,
without options. All calls share one set of registrations, and every catalog switches to one locale together:

```csharp
using Microsoft.Extensions.DependencyInjection;

// The application
services.AddRunicTranslations(options => options.InitialLocale = settings.Language)
    .AddCatalog(AppTextCatalog.Registration, context => new AppText(context));

// A library
public static IServiceCollection AddAcmeGrid(this IServiceCollection services)
{
    services.AddRunicTranslations().AddCatalog(GridTextCatalog.Registration, context => new GridText(context));
    return services;
}
```

The options belong to the application: the application's `InitialLocale` applies whichever call runs first, and two
calls that set an option to different values throw `InvalidOperationException`. A library therefore never passes
options.

`AddCatalog<TText>` registers a generated text class over the context, with the context's lifetime. Register a text class
once, in the library that owns its catalog: registering it again with another factory throws. Always use this factory
overload. Since 0.9 the generated text class has two one-parameter constructors, `XText(ITranslationManager)` and
`XText(ITranslationContext)`, so a type registration such as `services.AddSingleton<GridText>()` fails with
"ambiguous constructors" when the container can supply both. `new GridText(null)` still compiles and picks the manager
constructor.

`AddOverlay(TranslationOverlay)` attaches application overrides to a library catalog.

## Singleton or scoped

| Application | Lifetime | Why |
| --- | --- | --- |
| Desktop (WPF, Avalonia, console) | `Singleton` (default) | The user picks one language for the whole process. |
| Web (ASP.NET Core) | `options.Lifetime = ServiceLifetime.Scoped` | Each request switches to its user's locale; with a singleton, one request's `SetLocaleAsync` would switch every concurrent request. |

```csharp
services.AddRunicTranslations(options => options.Lifetime = ServiceLifetime.Scoped);

app.Use(async (http, next) =>
{
    var translations = http.RequestServices.GetRequiredService<ITranslationContext>();
    await translations.SetLocaleAsync(http.Features.Get<IRequestCultureFeature>()!.RequestCulture.UICulture.Name);
    await next();
});
```

Scoped contexts share their catalogs' providers and composed snapshots, so a context per request costs a snapshot lookup
per catalog, not a recomposition. `ITranslationContextFactory.CreateAsync(locale)` creates further contexts, for example
one per window, in either mode.

## Overlays and asynchronous initialization

Generated catalogs compose without I/O, so the context is built on first use. Overlay packs load asynchronously; with an
overlay, initialize the context once after building the service provider, which keeps every library catalog that was
added through `AddCatalog`:

```csharp
var app = builder.Build();
await app.Services.InitializeRunicTranslationsAsync();   // loads and checks overlay packs without blocking
app.Run();
```

Resolving the context before that throws instead of blocking on I/O, which could deadlock a UI thread. In scoped mode,
initialization composes the shared snapshots that every scope's context starts from. Do not build a
`TranslationContext` yourself and register the instance: it would bypass the catalogs that libraries add, and resolving
`ITranslationContext` throws when a catalog added through `AddCatalog` is missing.

See [Catalogs from libraries](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/composition.md).

## Compatibility and status

This package targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in
the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)).

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE).
