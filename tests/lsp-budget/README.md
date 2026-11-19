# Language server budgets

What an editor user waits for in a large catalog, measured against the built tool's `lsp` over standard input and output
as an editor runs it, and checked in the `Language server / large-catalog budgets` CI job (#262, D070.2, D077.4). Run it
with:

```sh
bun eng/run.mjs lsp-budget                       # build the tool, measure, check budgets.json, print the report
bun eng/run.mjs lsp-budget --update              # record the measured values and limits in budgets.json
bun tests/lsp-budget/measure.mjs --features 5    # a smaller catalog (5 × 1,000 messages × 10 locales), for a quick look
```

The measurement reads memory through `/proc`, so it runs on Linux. With warm packages it takes a few minutes on four
processors.

- **The catalog** (`fixture.mjs`): `features` feature folders, each a group with one `<locale>.rmf2` per locale of
  `messagesPerFeature` messages, in the first `locales` of en, de, fr, pl, ar, ja, ru, es, it and pt. The messages follow
  the NativeAOT size probe's pattern of 20 (`tests/aot-size/fixture.mjs`): 14 plain texts, 3 with a string input, 2 plurals
  with the locale's integer categories and 1 number. `budgets.json` sets 20 × 1,000 × 10: 20,000 messages in 10 locales,
  200 files. `RUNIC_LSP_BUDGET_REVIEW=1` adds a review file with stale entries, which makes every validation compile for
  review; it is for local comparisons, not budgeted.
- **The client** (`measure.mjs`): a minimal stdio LSP client that opens `feature00/en.rmf2` and measures, per sample:

| Budget | Measures |
| --- | --- |
| `lsp/open-to-diagnostics-ms` | `didOpen` of the document to the first diagnostics for it: the whole catalog validated with the buffer. |
| `lsp/edit-to-diagnostics-ms` | An edit that breaks a message, and the one that repairs it, each to its diagnostics. |
| `lsp/hover-ms`, `lsp/completion-ms` | Hover on a message name and completion inside an input, with nothing else running. |
| `lsp/hover-after-diagnostics-ms` | A hover sent as an edit's diagnostics arrive, when the server finishes a validation. |
| `lsp/hover-while-typing-ms`, `lsp/completion-while-typing-ms` | A hover or a completion right after five keystrokes, while the catalog is validated in the background. |
| `lsp/typing-to-diagnostics-ms` | The last of five keystrokes to the diagnostics of its version. |
| `lsp/rename-ms` | `prepareRename` and `rename` of a resource, an edit of 10 files (one per locale). |
| `lsp/resident-bytes-after-open` | The server's resident memory once the first diagnostics arrived (`VmRSS`). |
| `lsp/peak-resident-bytes` | The server's peak resident memory over the sample (`VmHWM`). |

A latency is the median of every measurement of every sample, memory the largest sample. `results.json` in
`artifacts/lsp-budget/` keeps each sample's measurements and the processor count; CI uploads it as
`lsp-budget-results`.

## Budget entries

```json
"lsp/rename-ms": { "target": 3000, "headroom": 1.75, "measured": 1610, "limit": 2818 }
```

- `measured` and `limit` are written by `--update`: the limit is the measured value times `headroom`.
- `headroom` covers a shared CI runner's variance and is written by hand: wider for latencies of a few milliseconds than
  for the seconds a validation takes, and narrow for memory.
- `target` is #271's goal for the size the budgets measure, shown beside the limit in the report. A budget above its
  target is a known gap, listed in the pull request that set it.
- A value under half its `measured` reports **improved: lower the budget**; it does not fail.

The limits are for CI's runner, ubuntu-24.04 with 4 processors. A run with more processors is faster; for a local
comparison pin it to four of them (`taskset -c 0-3 bun eng/run.mjs lsp-budget`). A pull request that raises a limit says
why under a "Budget changes" heading in its description.
