- Loading an external or overlay pack compares its markup contract with the catalog's entry by entry, in .NET and in
  generated ESM, without writing out the version 2 form of a version 3 contract. Its time and memory now grow with the
  size of the pack and the catalog. A version 3 contract must list canonical locales in ordinal order, as many as the
  catalog's version 3 contract does. A pack that does not meet these rules, or that fails to load for any other
  reason, is refused with `TranslationPackException` (`ArgumentContractMismatch` or `Malformed`) in .NET or an
  `RTR0023` result in ESM, so the application's fallback runs. Upgrade if your application loads packs it did not
  build itself.
