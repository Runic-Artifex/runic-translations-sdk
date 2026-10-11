import { test, expect } from 'bun:test';
import { assertReleaseLinks, releaseLinks } from './readme-links.mjs';

const links = { repository: 'Runic-Artifex/runic-translations-sdk', tag: 'v0.6.0-preview.6', directory: 'packages/dotnet/Runic.Translations.Wpf' };

test('packed README links name the release tag', () => {
  const source = [
    '[guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/packages/dotnet/Runic.Translations/README.md#install)',
    '[examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/main/examples)',
    '<https://github.com/Runic-Artifex/runic-translations-sdk/tree/main>',
    '[Runtime](../Runic.Translations/README.md) and [inventory](docs/inventory.md#windows)',
    '[section](#install), [portal](https://docs.runic-artifex.eu/packages/), [mail](mailto:a@b.c)',
    '[a branch named mainline](https://github.com/Runic-Artifex/runic-translations-sdk/tree/mainline)',
  ].join('\n');
  expect(releaseLinks(source, links)).toBe([
    '[guide](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.6.0-preview.6/packages/dotnet/Runic.Translations/README.md#install)',
    '[examples](https://github.com/Runic-Artifex/runic-translations-sdk/tree/v0.6.0-preview.6/examples)',
    '<https://github.com/Runic-Artifex/runic-translations-sdk/tree/v0.6.0-preview.6>',
    '[Runtime](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.6.0-preview.6/packages/dotnet/Runic.Translations.Wpf/../Runic.Translations/README.md) and [inventory](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.6.0-preview.6/packages/dotnet/Runic.Translations.Wpf/docs/inventory.md#windows)',
    '[section](#install), [portal](https://docs.runic-artifex.eu/packages/), [mail](mailto:a@b.c)',
    '[a branch named mainline](https://github.com/Runic-Artifex/runic-translations-sdk/tree/mainline)',
  ].join('\n'));
});

test('a remaining main-branch link fails the pack, including other repositories', () => {
  const other = '[Views](https://github.com/Runic-Artifex/runic-sdk/tree/main/packages/web/views).';
  expect(releaseLinks(other, links)).toBe(other);
  expect(() => assertReleaseLinks(other, 'views README')).toThrow(
    'views README links to a main branch (github.com/Runic-Artifex/runic-sdk/tree/main/packages/web/views).');
  expect(() => assertReleaseLinks(releaseLinks('[x](https://github.com/Runic-Artifex/runic-translations-sdk/blob/main/LICENSE)', links), 'x')).not.toThrow();
});
