# Ship translation updates without a release

An application can load translation packs at run time: corrected wording, a
translation that was missing when the release was built, or a locale added
later. A pack is the same locale artifact v5 that
`runic-translations generate --emit-json` writes. The application downloads it,
checks it, and installs it over the text compiled into the release. This guide
covers building packs, hosting them, verifying their integrity, caching them,
and loading them in .NET and on the web.

A pack changes text only. It cannot add a message, change a message's inputs,
or turn plain text into rich content: the release's code calls the messages it
was built with. Text that a pack does not supply keeps the release's own text.

## Two ways to load a pack

| Loader | Accepts | Since |
| --- | --- | --- |
| **Overlay**: .NET `TranslationOverlay.FromPacks`, web `installLocalePack` | A pack of any release of the catalog. Each message is checked by key against the running release; messages it cannot use are skipped and reported. A pack may add a locale. | 0.9 |
| **Exact pack**: .NET `ExternalTranslationSnapshotFactory`, web `decodeLocalePack` | Only a pack whose contract fingerprint and markup contract equal the running release's, for a locale it declares. | 0.8 |

Use overlays in new code. They let one pack serve every release in the field.
Releases built before 0.9, and code that uses the exact-pack loaders, need a
pack built for their contract; see [Packs for a shipped
release](#packs-for-a-shipped-release).

## Build packs

Keep the translations of a shipped catalog in source control as usual, and
build the packs from the current sources:

```bash
dotnet tool run runic-translations -- generate --project translations --output packs --emit-json
```

`packs/` then holds one `<catalog>.<locale>.locale-v5.json` per locale and
`<catalog>.asset-manifest-v1.json`, which lists every file with its byte length
and SHA-256. Translators can work on the sources while the release is in the
field; nothing in a pack depends on the release's code.

### Packs for a shipped release

A release's contract is in any locale artifact built from its sources. Keep
the base-locale artifact of every release you ship, for example
`releases/1.4.0/app.en.locale-v5.json` from the release build's
`generate --emit-json`. Then build packs of the current sources for that
release:

```bash
dotnet tool run runic-translations -- generate --project translations --output packs/1.4.0 \
  --emit-json --contract releases/1.4.0/app.en.locale-v5.json
```

The output has one pack for each locale that the release declares. Each pack
is stamped with the release's contract fingerprint and markup contract and
holds only the messages the release accepts. For every message it leaves out,
the command prints one line with the locale, the key and the reason:

| Reason | Meaning |
| --- | --- |
| `unknown-key` | The release has no such message. |
| `incompatible-inputs` | The message's inputs differ in name or type. |
| `content-kind-changed` | The message is a document in one release and inline text in the other. |
| `incompatible-markup` | The message's markup, slots or document structure differ. |
| `content-locale-changed` | The locale's text for the message comes from another locale than in the release: it is newly translated, or the translation was removed. An exact-pack loader rejects such a message. |

These packs load in the exact-pack loaders of that release and as overlays.
For releases on 0.9 or later, a plain pack installed as an overlay also applies
newly translated messages (`content-locale-changed`), so prefer it there.
`verify` accepts the same `--contract` option to check retained packs in CI.

## Host packs

Packs are static JSON files; serve them from a CDN or any HTTPS server.

- **One URL per pack and version.** Put a version in the path, such as
  `/translations/app/2026-11-05/de.json`, and publish a small index that names
  the current version. A client then caches each pack forever and only fetches
  the index to learn about updates.
- **One set per release for exact packs.** Key the path by the release's
  contract fingerprint, such as `/translations/app/sha256-…/de.json`, so a
  release finds the packs built for it. Overlays can share one set.
- **Headers.** Serve `application/json`. Cross-origin web clients need CORS
  headers. Packs contain only text, never code, but the text is shown to users,
  so publish them with the same access control as a release.

## Verify integrity

A pack is untrusted input. Every loader bounds its size and depth, parses it
strictly (UTF-8, no duplicate members), and checks it against the release's
contract, so a malformed pack cannot crash the application or change its
behaviour beyond its text. Whether the text is yours is a separate question:
pass an integrity callback, which receives the raw bytes before they are
parsed. A callback that returns `false` or throws rejects the pack
(`integrity-rejected`).

A simple policy: ship a public key with the release, sign the asset manifest
when you publish packs, and accept a pack whose SHA-256 the signed manifest
lists. The callback then only compares hashes:

```ts
// The verified asset manifest: { assets: [{ path, sha256 }] }, signature checked at download.
const trusted = new Set(manifest.assets.map(asset => asset.sha256));
async function integrityVerifier(content: Uint8Array): Promise<boolean> {
  const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', content));
  return trusted.has(Array.from(digest, byte => byte.toString(16).padStart(2, '0')).join(''));
}
```

```csharp
TranslationPackIntegrityVerifier verifier = (content, cancellationToken) =>
    ValueTask.FromResult(trusted.Contains(Convert.ToHexStringLower(SHA256.HashData(content.Span))));
```

HTTPS protects the transfer but not a compromised host or CDN; the signature
does.

## Cache packs

Store the bytes of every pack you install, with the version it came from, and
install the cached bytes at start-up before any download. The application then
shows the updated text offline and from the first frame. Run the integrity
callback on cached bytes too, or keep them where only the application can write.
Drop the cache when the application updates, unless your packs are overlays.

## Load packs in .NET

Add an overlay for the catalog to the application's `TranslationContext` (see
[Catalogs from libraries](composition.md#overriding-library-text)). Its source
returns the cached or downloaded bytes of a locale, or `null`:

```csharp
sealed class PackSource(PackCache cache) : IExternalTranslationSource
{
    public async ValueTask<ExternalTranslationPack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken) =>
        await cache.TryGetAsync(catalog, locale, cancellationToken) is { } bytes ? new ExternalTranslationPack(bytes) : null;
}

TranslationContext translations = await TranslationContext.CreateBuilder()
    .Add(AppTextCatalog.Registration)
    .AddOverlay(TranslationOverlay.FromPacks("app", new PackSource(cache), integrityVerifier: verifier))
    .BuildAsync(settings.Language);

// After a download has stored new packs:
await translations.RefreshAsync();
```

The context loads the overlay's pack of each locale in the requested locale's
chain, checks it key by key, and publishes the result with the next switch or
`RefreshAsync`. Skipped messages are in each catalog's
`TranslationCatalogStatus.OverlayDiagnostics`. A pack that is malformed, names
another catalog or locale, or fails the integrity callback fails the switch with
a `TranslationPackException`; keep the previous cache entry until a new pack has
loaded once. To add a locale the release lacks, pass `addedLocales`.

## Load packs on the web

`installLocalePack` from the catalog's `/dynamic` entry point installs pack
bytes as an [overlay](esm.md#overlays). The ordinary `m.*()` calls then use its
text:

```ts
import { installLocalePack, onOverlayChange } from 'virtual:runic-translations/app/dynamic';

async function updateTranslations(locale: string) {
  const response = await fetch(`/translations/app/${version}/${locale}.json`);
  const result = await installLocalePack(new Uint8Array(await response.arrayBuffer()), { locale, integrityVerifier });
  if (!result.ok) return console.warn('translation pack rejected', result.reason);
  for (const skipped of result.value.diagnostics) console.info('not applied', skipped);
  previous.get(locale)?.remove();      // the pack it replaces
  previous.set(locale, result.value);
}
```

A later pack of the same locale wins per key, so install the new pack before
removing the old one. `onOverlayChange` runs after every install and removal;
components that rendered before the update render again when they read
reactive state, so in Svelte keep a counter in `$state`, increment it in the
listener, and read it where the text is rendered, or switch the locale after
installing a pack for it. Install cached packs before the first render.

With server-side rendering, the server installs the same packs as the browser,
before it renders: overlays are state of the catalog's runtime module and apply
to every request. Otherwise the server renders the release's text and hydration
replaces it.

Pages that never import `installLocalePack` or `installOverlay` bundle no
overlay code. `decodeLocalePack` and `formatDynamicMessage` remain for exact
packs.
