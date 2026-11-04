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

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddRunicTranslations(options => options.InitialLocale = settings.Language)
    .AddCatalog(AppTextCatalog.Registration, context => new AppText(context));
```

A library registers its own catalog the same way, in its own registration method, and the calls share one context:

```csharp
public static IServiceCollection AddAcmeGrid(this IServiceCollection services)
{
    services.AddRunicTranslations().AddCatalog(GridTextCatalog.Registration, context => new GridText(context));
    return services;
}
```

`ITranslationContext` (and `TranslationContext`) is a singleton. Generated text classes registered with
`AddCatalog<TText>` are singletons over it, so `AppText` and `GridText` no longer collide on `ITranslationManager`.
`AddOverlay(TranslationOverlay)` attaches application overrides to a library catalog.

The context is built on first use without awaiting, which suits generated catalogs: they compose without I/O. For
overlays whose sources do I/O, build the context with `TranslationContext.CreateBuilder()...BuildAsync()` at startup and
register the instance instead.

See [Catalogs from libraries](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/docs/guides/translations/composition.md).

## Compatibility and status

This package targets .NET 10. Before 1.0 a minor release may contain breaking changes, each listed with upgrade steps in
the release notes (see [Versions before 1.0](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/eng/release/README.md#versions-before-10)).

Licensed under the [MIT License](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE).
