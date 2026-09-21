# RMF2 v5 project fixture

`runic.json` keeps project schema v1 and resource syntax `rmf2-v1`. Tests opt in
through the typed `Rmf2ExecutionV2` compiler entry point.

The English and German messages share caller and functional-slot contracts but
use different selector trees and may omit source inputs. Both preserve formatted
local aliases and exact typed values. `expected-contract.json` records sorted
canonical keys. Compiler tests also assert normalized caller types, slot kinds,
content locales, deterministic hashes and execution through the typed .NET
runtime without a v4 conversion.

This fixture complements the message-level semantic-v5 corpus; generated modules
and external locale packs remain separate integration work.
