# RMF2 payment fixture

This maintained fixture combines two links, a conditional action, an accessible
standalone icon, a custom badge with a dynamic enum option, and a feature directory.
`runic.json` is also the language-neutral renderer contract.

From the repository root in its development shell:

```sh
dotnet run --project tools/dotnet-runic-translations -- generate --project specs/translations/examples/rmf2 --output specs/translations/examples/rmf2/generated --emit-csharp --emit-json --emit-esm
```

Serve this directory with a local static HTTP server and open `browser.html`.
Generated files are disposable and ignored. The host controls every route, asset,
and callback; changing locale demonstrates translator-controlled order. Setting
`count` to zero removes the retry action while keeping its caller binding contract.

For native UI, construct `Rmf2InlineRenderer` with the generated
`CheckoutText.Rmf2MarkupContract`, format the payment key with the generated
manager, then map the returned `InlineMarkupRun` values to your toolkit. The
runtime supplies semantics and typed bindings; toolkit focus, styling and event
wiring belong to that adapter. See the [implementation guide](../../../../docs/guides/translations/rmf2.md).
