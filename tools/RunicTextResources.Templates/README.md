# RunicTextResources.Templates

Install the template package and create either a resource folder or a standalone
class library:

```text
dotnet new install RunicTextResources.Templates
dotnet new runic-textresources --output Resources --catalog product --defaultLocale de --namespace Customer.Product --className ProductText
dotnet new runic-textresources-project --name Customer.Product.Text --catalog product --defaultLocale de --namespace Customer.Product --className ProductText
```

The item template creates a minimal single-locale schema-v2 catalog. Add package
and MSBuild items to the containing project as described by
`RunicTextResources.Build`. The project template includes the runtime, generator,
build integration, a pinned local tool manifest, and ESM generation. Use the CLI
or Translations Editor when creating arbitrary locale and fallback graphs.
