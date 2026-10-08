# RMF2 document profile v1

This profile defines document messages: messages whose top level is a sequence
of blocks (paragraphs, headings and flat lists) instead of a single line of
inline content. It builds on the [v5 project contract](rmf2-project-v5.md) and
markup contract v2 (RMF2 ABI 3). The source syntax is unchanged; document
structure is written with the markup syntax MF2 already has.

```rmf2
help =
  {#h level=1}Getting started{/h}
  {#p}Open the {#strong}Settings{/strong} page.{/p}
  {#ol}{#li}Choose a language.{/li}{#li}Restart the app.{/li}{/ol}
```

## 1. Vocabulary

The vocabulary is always available. Each short name is an alias for the
`runic:` contract; `runic.json` cannot redefine these names, and custom
contracts can only have `placement: "inline"`.

| Name | Contract | Placement | Children | Options |
|---|---|---|---|---|
| `p` | `runic:p` | `block` | `inline` | none |
| `h` | `runic:h` | `block` | `inline` | `level`: integer 1..6, required |
| `ul` | `runic:ul` | `block` | `list-items` | none |
| `ol` | `runic:ol` | `block` | `list-items` | `start`: integer 1..2147483647, default `1`; `marker`: `decimal`, `lower-alpha`, `upper-alpha`, `lower-roman` or `upper-roman`, default `decimal` |
| `li` | `runic:li` | `list-item` | `inline` | none |

All five are paired and not interactive; their plain text is their children.
Every option is literal-only. Integer literals follow markup contract v2:
`0|-?[1-9][0-9]*` within the declared bounds, quoted or unquoted.

`h level` is relative to the message: the host maps level 1 to its own
heading depth.

## 2. Content kinds

Every message has a content kind, `inline` or `document`.

- A variant is classified from its top level. If any markup element at the top
  level has `block` or `list-item` placement, the variant is a document.
  Otherwise it is inline if it contains text other than MF2 whitespace and bidi
  marks, a placeholder or markup. A variant with only MF2 whitespace and bidi
  marks (or nothing) is empty and fits either kind.
- The message kind comes from the base locale; for a locale-only key it comes
  from the first locale that defines the key. If every variant is empty, the
  message is inline. Variants of one message with different kinds report
  `RTR0070`.
- A translation must have the base locale's kind (`RTR0070`).
- The kind is part of the caller contract: changing a message between inline and
  document changes `CallerFingerprint`.

## 3. Nesting

| Position | Allowed content |
|---|---|
| Document top level | `p`, `h`, `ul`, `ol`; MF2 whitespace and bidi marks, which are dropped |
| `ul`, `ol` | `li`; MF2 whitespace and bidi marks, which are dropped |
| `p`, `h`, `li` | text, placeholders and inline markup |
| Inline markup in a document | text, placeholders and inline markup |
| Inline message, any depth | text, placeholders and inline markup |

Text, placeholders or inline markup at the document top level report
`RTR0070`. Every other violation reports `RTR0072`: `li` outside a list,
anything but `li` inside a list, a list inside `li`, or a block inside `p`, `h`,
`li` or inline markup. Nested lists, tables, quotes, code blocks and custom
block contracts are not part of v1.

## 4. Limits

A document may nest at most 16 element levels, block and inline elements
together, and contain at most 4096 nodes (elements, text runs and
placeholders). Either violation reports `RTR0073`. The general pattern limit of
the execution profile (4096 pattern parts, closing tags included) is checked
first and reports `RTR0065`, so the compiler reaches the node limit only through
that earlier rule; runtimes check both limits when they load a pack. Inline
markup nesting keeps its own limit of 16 inline levels (`RTR0061`).

## 5. Whitespace

Whitespace in inline messages is unchanged. In document variants the compiler
normalizes whitespace before the artifact is written, so every runtime receives
the same text:

1. MF2 whitespace and bidi marks between blocks, and between list items, are
   dropped.
2. Inside each `p`, `h` and `li`, the content is read as one sequence of
   characters and atoms. Placeholders and standalone markup are atoms; the open
   and close tags of paired inline markup are transparent. A *run* is a maximal
   sequence of space, tab, CR and LF characters.
   - A run at the start or end of the block is deleted.
   - A run without CR or LF is kept verbatim.
   - A run that contains CR or LF is deleted when it is next to `br`, or when
     either neighbouring character is U+200B ZERO WIDTH SPACE, or when both
     neighbouring characters have East Asian Width F, W or H and neither is
     Hangul. Otherwise it becomes one U+0020 SPACE, placed where the run
     starts.
3. Other characters, including U+3000 and U+00A0, are kept.

Deleting a run between two wide characters can place a combining mark directly
after a base character, for example `か`, a line break and U+3099 become
`か` + U+3099. The compiler does not recompose the result, so the text is then
not in NFC. No profile invariant depends on NFC; authors should keep a
combining mark on the line of its base character.

A block that is empty after normalization reports warning `RTR0076`. A line
break between two Thai, Lao, Khmer or Myanmar characters that becomes a space
reports warning `RTR0078`.

Character properties use Unicode 16.0.0: East Asian Width from
`EastAsianWidth.txt`, and Hangul and the Southeast Asian scripts as the
characters whose `Scripts.txt` value is `Hangul`, or `Thai`, `Lao`, `Khmer` or
`Myanmar`. The property tables are fixed with the profile version and do not
follow the host's Unicode version.

## 6. Locked structure

The *skeleton* of a document variant is its tree of block and list elements
with their options, without text, placeholders or inline markup. Translators
may change text and inline markup but not the skeleton: every variant of a
translation must have the skeleton of some source variant, otherwise `RTR0074`
reports the first difference against the closest source skeleton (the one that
agrees with it for the most elements in document order). A translated variant
whose skeleton matches a source variant other than the one with the same keys
reports warning `RTR0071`.

Skeletons are written in this form:

```abnf
skeleton  = [ node *("," node) ]       ; empty for a variant with no blocks
node      = name [ "[" options "]" ] [ "(" [ skeleton ] ")" ]
name      = 1*namechar                  ; contract name without "runic:"
options   = option *(";" option)        ; every option, defaults included,
option    = optname "=" value           ; sorted by name in ordinal order
value     = "$" variable / literal
literal   = *(escaped / plain)          ; canonical literal text
escaped   = "\" ( "\" / "[" / "]" / "(" / ")" / "," / ";" / "=" / "$" )
```

A leading `$` in a literal is escaped; `$` elsewhere is not. Lists always have
parentheses, so an empty list is `ul()`. A variant with no blocks (an empty
variant) has the empty skeleton; a translated empty variant therefore needs an
empty source variant. Examples: `p,ul(li,li),p`, `h[level=1]`,
`ol[marker=decimal;start=1](li)`.

Paths name an element by its 1-based position among siblings with the same
name, for example `ul[1]/li[3]`.

Heading levels are checked per base-locale variant: a first heading above
level 1, or a heading more than one level deeper than the one before it,
reports warning `RTR0077`. Translations are not checked, because their heading
levels are locked with the structure.

## 7. Locale artifact members

Every entry of `markupContract.messages` in a
[locale artifact](schemas/locale-artifact-v5.schema.json) has:

- `content`: `"inline"` or `"document"`;
- `skeletons`: the distinct encoded skeletons of the base-locale variants,
  sorted in ordinal order, including the empty string for an empty variant.
  Inline messages have an empty array.

`content` is part of the caller contract and the fingerprint. `skeletons` is
only in the full markup contract; the caller fingerprint does not cover it.
Packs are bound to the skeletons because both pack loaders compare the pack's
`markupContract` with the generated contract byte for byte.

## 8. Diagnostics

| ID | Severity | Meaning |
|---|---|---|
| `RTR0070` | error | Wrong or mixed content kind, or inline content at the document top level |
| `RTR0071` | warning | Variant matches another variant's source skeleton |
| `RTR0072` | error | Element in an invalid position |
| `RTR0073` | error | Depth or node limit exceeded |
| `RTR0074` | error | Translated skeleton matches no source skeleton |
| `RTR0075` | — | Reserved for a later profile; not reported |
| `RTR0076` | warning | Empty block or list |
| `RTR0077` | warning | Heading skips a level |
| `RTR0078` | warning | Southeast Asian line break became a space |

## 9. Runtime

Both runtimes validate document messages when they load a pack or a dynamic
locale artifact, and again when they render:

- A document message whose pattern breaks the rules of sections 3 to 5 (text or
  inline markup at the top level, an element in an invalid position, a line
  break in leaf text, or a leaf that starts or ends with a space or tab) is
  rejected as `RTR0023/malformed-pattern`. For the leaf check paired inline
  tags are transparent and placeholders and standalone elements are atoms.
- A document message variant whose encoded skeleton is not in the contract's
  `skeletons` list is rejected as `RTR0023/document-structure-mismatch`.
- An inline message that contains block markup is rejected as
  `RTR0023/malformed-pattern`.
- A pack whose `markupContract` differs from the generated contract, including
  its `skeletons`, is rejected as `RTR0023/argument-contract-mismatch`.

Rendering produces a tree of blocks. Each block has its canonical name, its
resolved options including defaults, its child blocks (lists) or inline content
(paragraphs, headings and list items), and an occurrence. A block occurrence is
the path of `name[n]` steps from the root, where `name` drops the `runic:`
prefix and `n` counts the siblings with that name, starting at 1: for example
`p[2]` or `ul[1]/li[3]`. An inline element inside a block has the occurrence
`<block path>/<index path>`, where the index path lists the 0-based position
among all siblings, text included, joined by `.` for nesting, for example
`p[1]/0` or `ul[1]/li[2]/1.0`. Text has no occurrence. Inline messages keep their existing
occurrences.

The plain-text projection joins top-level blocks with `\n\n` and list items
with `\n`. A `ul` item starts with the list marker (default `- `). An `ol` item
starts with its number followed by `. `: numbers count from `start`; the
`decimal` marker writes decimal digits, the alpha markers write bijective
base-26 letters (`a`..`z`, `aa`, ...), and the roman markers write roman
numerals for 1 to 3999 and decimal digits otherwise. Every further line of an
item is indented by two spaces; empty lines stay empty. Inline content is
projected as for inline messages.
