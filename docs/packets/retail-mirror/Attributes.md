# Attributes

The player's stats, vitals, experience and combat values. Replaces retail `0x08` for the
duration of a negotiated connection.

| | |
|---|---|
| **Opcode** | `0x0008` |
| **Block** | 0–3 — retail mirror |
| **Dialect** | `0xB0` (V1) |
| **Initiator** | S→C |
| **Reply** | none |
| **Body** | `[u32 blockFlags][blocks…]`, variable |

Retail `0x08` upgraded 1:1, so it keeps its number zero-extended, per
[ALLOCATIONS.md §1](../../ALLOCATIONS.md).

## Wire format

```
[u32 blockFlags]
[ blocks, in ascending bit order ]
```

Big-endian throughout, envelope and body alike.

`blockFlags` selects which blocks follow **and nothing else**. Retail's flag byte also
carried standalone state — an unread-mail indicator and a movement mode — which here live
in the Status block. All eight retail flag bits were allocated, which is why this field is
`u32` rather than `u8`.

| Bit | Block | Size |
|---|---|---|
| `0x00000001` | Primary | 44 |
| `0x00000002` | Vitals | 8 |
| `0x00000004` | Experience | 44 |
| `0x00000008` | Combat | 34 |
| `0x00000010` | Status | 4 |
| `0x00000020` | ExtendedStats | 2 + 10·n |
| `0x00000040`+ | reserved | — |

Blocks appear in ascending bit order. A sender MUST NOT reorder them.

### Primary — `0x01`, 44 bytes

| Type | Field |
|---|---|
| `u16` | level |
| `u16` | ability |
| `u32` | maxHp |
| `u32` | maxMp |
| `u32` | str |
| `u32` | int |
| `u32` | wis |
| `u32` | con |
| `u32` | dex |
| `u32` | levelPoints |
| `u32` | maxWeight |
| `u32` | currentWeight |

### Vitals — `0x02`, 8 bytes

| Type | Field |
|---|---|
| `u32` | hp |
| `u32` | mp |

### Experience — `0x04`, 44 bytes

| Type | Field |
|---|---|
| `u64` | experience |
| `u64` | expToLevel |
| `u64` | abilityExp |
| `u64` | abilityToNext |
| `u64` | gold |
| `u32` | gamePoints |

### Combat — `0x08`, 34 bytes

| Type | Field |
|---|---|
| `i32` | ac |
| `f64` | mr |
| `f64` | dmg |
| `f64` | hit |
| `u8` | offensiveElement |
| `u8` | offensiveElementBase |
| `u8` | offensiveElementOverride |
| `u8` | defensiveElement |
| `u8` | defensiveElementBase |
| `u8` | defensiveElementOverride |

### Status — `0x10`, 4 bytes

| Type | Field |
|---|---|
| `u8` | movementMode |
| `u8` | blinded |
| `u8` | hasUnreadMail |
| `u8` | hasParcel |

### ExtendedStats — `0x20`, 2 + 10·n bytes

```
[u16 count] [ (u16 statId, f64 value) × count ]
```

## Field semantics

**Units live in this document and in the stat enum, never on the wire.** A multiplier, a
probability and a flat rating are all `f64`; what one means is a property of its field or
its `statId`.

`mr`, `dmg`, `hit` are multipliers centred on 1.0 — `1.05` is a 5% bonus. They are sent raw
rather than as retail's byte rating quantized around 128 at ×800, which capped the
expressible range at roughly ±0.16 regardless of what the server computed.

`ac` is a signed rating; lower is better, per retail convention.

**Elements carry three values each.** `offensiveElementBase` is the character's intrinsic
element, `offensiveElementOverride` is a temporary replacement (from a status effect, `0`
when none), and `offensiveElement` is the effective value the receiver should display.
Resolving `override == none ? base : override` is a **game rule, not a formatting
transform**, so the sender resolves it and the receiver MUST NOT re-derive it. Base and
override are carried for breakdown display only.

`movementMode` is a value 0–3, not a bitfield. **Modes 1 and 2 bypass client-side tile and
entity collision validation; modes 0 and 3 apply normal collision.** A swimming animation
is an incidental side effect of a non-zero mode, not its purpose.

`blinded`, `hasUnreadMail`, `hasParcel` are booleans: `0` false, non-zero true. Retail
packed the latter two as two nibbles of a single byte, decoded only when a separate flag
bit was also set; that cross-field dependency does not survive here.

`levelPoints` is the count of unspent points. Retail carried a separate "has level points"
boolean alongside it; `levelPoints > 0` states the same fact, and a second source of truth
for it is a defect waiting to happen.

`statId` in ExtendedStats indexes the shared stat enum in this package. **An id is
permanent once shipped**, exactly like an opcode.

## Receiver requirements

A receiver MUST:

- Treat each present block as the **current state of that block**, replacing whatever it
  held. Blocks are not deltas and carry no increments.
- Treat an absent block as **unchanged**, retaining its previous value.
- Treat a present ExtendedStats block as a **complete snapshot** of extended state,
  replacing it wholesale — including dropping ids that were present before and are not
  present now.
- Clear all state derived from this packet on disconnect or session replacement.

A receiver MUST NOT:

- Merge an ExtendedStats block with a previous one. Absence of an id inside a present block
  means the value no longer applies, not that it is unchanged.
- Re-derive effective elements, or any other value the sender resolved.
- Assume a block is present because it was present in an earlier packet.

A receiver MAY ignore any `statId` it does not recognise; records are a fixed 10 bytes
precisely so an unknown id is skippable.

**On coexistence with retail `0x08`:** a server SHOULD keep emitting retail `0x08` on
negotiated connections. Not for compatibility — a dialect client can ignore it — but so
that path stays exercised rather than running only in the situation where the dialect has
already failed. A receiver with an engaged dialect selects `0x0008` as its source for every
field the two share; this is **source selection, not a merge**, and there is exactly one
owner per field.

## Malformed input

The body is invalid, and the packet MUST be rejected, when:

- Its length is not exactly `4 + Σ(sizes of present fixed blocks) + extendedBlockSize`.
- An ExtendedStats block is present and does not consume exactly `2 + 10·count` bytes
  ending on the body boundary.
- **Any reserved `blockFlags` bit is set.**

That last one deserves emphasis, because it is the opposite of the ExtendedStats rule and
the asymmetry is deliberate:

> **An unknown block flag is a protocol error, not a skip.** Fixed-size blocks cannot be
> skipped without knowing their size, so a receiver seeing an unfamiliar bit has no way to
> find the next block and cannot safely continue. An unknown `statId` *can* be skipped,
> because every record is 10 bytes.

A receiver MUST NOT attempt to guess the length of an unrecognised block, and MUST NOT
process the blocks it did recognise before the unknown bit and then stop — a partially
applied update is worse than a rejected one.

`count` is bounded in practice by the frame-size limit in
[EXTENSIONS.md §4.2](../../EXTENSIONS.md); a receiver SHOULD validate the arithmetic
against the actual body length before allocating, rather than trusting `count`.

## Forward compatibility

**Adding a stat needs no protocol change.** Allocate a `statId`, ship it; older receivers
skip it. This is the extensibility valve, and it is why the block set can stay closed.

**Adding a block requires a dialect version bump**, because reserved bits are rejected
rather than skipped. Weigh that before reaching for one: most new values are stats, and a
stat is free.

**Changing an existing block's layout requires a dialect version bump.** Fields cannot be
appended to a block in place — the sizes above are exact and a receiver validates against
them.

The closed-block/open-field split is the design, not an accident of it. Partial sends are
safe for the fixed blocks *only because that set is closed*, so "absent" can mean exactly
one thing. An open field set has no such guarantee: a missing id would be ambiguous between
unchanged, no longer applicable, and unknown to this sender — which is why ExtendedStats is
all-or-nothing.

## History

| Dialect | Change |
|---|---|
| V1 | Introduced, replacing retail `0x08`. |

## Notes on what changed from retail

Retail `0x08` is documented at rung 1 in Comhaigne,
`docs/protocol/server/0x08-attributes.md`, pinned at
`023d886130b547e903b9ee42e977859075f91d70` (verified from the USDA binary 2026-04-21). Two
properties of that packet drove this replacement:

**About 27% of it is dead or zero-filled.** Byte-pattern search of the retail client finds
no reader for Primary's `{1,0,0}` magic, Primary's trailing `u32`, four Secondary bytes, or
flag bit `0x02`; a further nine bytes are fields Hybrasyl fills with zero. None are carried
here.

**Its field widths had become balance ceilings rather than precision limits.** The byte
ratings capped `mr`/`dmg`/`hit` near ±0.16; primary stats were `u8`, so a configured cap
above 255 would silently wrap; `ac` was `sbyte`. Widening these removes design constraints
inherited from 1997 bandwidth, which is the main reason this packet exists at all.

The full analysis, including the rationale for each field and the evidence behind the
dead-byte inventory, is in Comhaigne at
`docs/plans/brigid/BRIG-53-attributes-replacement-contract.md`.
