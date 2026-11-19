## Smaller generated .NET catalogs that load faster (RMF2 runtime ABI 5)

Generated C# embeds each locale in a compact binary encoding instead of JSON, and the message table as DEFLATE data
(RMF2 runtime ABI 5, D077.16). For the Translations Editor's 1,000 messages in two locales the embedded catalog data
shrinks from 79.7 to 46.1 bytes per message; a NativeAOT executable no longer links the JSON reader for generated data
(about 34 KB less) and grows by about 130 bytes per message instead of 143. A locale is ready about 5 times sooner on
first use (the editor's English: 1.6 ms → 0.31 ms), and each message is built only when it is first formatted, so a
message that is never shown keeps about 40 bytes instead of about 490 (the editor: 330 KB less memory after first use,
at about 0.15 µs on each message's first format).

Rebuild to get it: `Runic.Translations` 0.15 runs catalogs generated for RMF2 ABI 3, 4 and 5, so libraries built with
0.13 or 0.14 keep working, and `Runic.Translations.Build` 0.15 still generates ABI 4 for a project that references a 0.14
runtime. The binary encoding is specified byte for byte in
[RMF2 execution profile v2](https://github.com/Runic-Artifex/runic-translations-sdk/blob/v0.15.0/specs/translations/rmf2-execution-v2.md#generated-locale-data-binary-encoding);
packs and ESM output stay JSON.
