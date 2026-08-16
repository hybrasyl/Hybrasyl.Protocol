# Hybrasyl Extended Framing — Allocations

_The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD",
"SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be
interpreted as described in RFC 2119._

**Scope**: the opcode space, allocation rules, and the registry of assigned numbers
for the extended framing described in [EXTENSIONS.md](EXTENSIONS.md).

> This document is authoritative for allocations. An allocation MUST land here, and in
> the shared library, before either end ships it.

---

## 1. Opcode space

Extension-frame opcodes are **u16, big-endian**.

| Range | Use |
|---|---|
| `0x0000`–`0x00FF` | Retail-mirrored semantics. A retail packet upgraded 1:1 keeps its number, zero-extended: retail `0x08` is `0x0008`. |
| `0x0100`+ | Packets with no retail ancestor, allocated from category blocks (§2). |

The opcode namespace inside extension frames is wholly ours. No retail-block
discipline applies, because the only retail-framed signal this protocol adds is the
marker on the existing `0x7E` greeting, which needs no allocation.

Unused numbers in retail's 256-opcode range MUST NOT be reused for new packets. New
packets are allocated upward from `0x0100`.

---

## 2. Allocation rules

**Exchanges share a number.** Opcode namespaces are per-direction — registration is
split client-to-server and server-to-client — so a request and its response MUST be
allocated *one* number, used in both directions:

```
0x0150  C -> S   GetProfile      the request
0x0150  S -> C   ProfileData     its response — the same number, other direction
```

A request MUST NOT be answered by a different number. That asymmetry is retail's
`0x45`/`0x75` pattern, and allocating the reply as "the next one up" reproduces it
exactly. A packet with no response simply leaves its number unused in the other
direction.

**"Response" means every frame the responder sends as part of that exchange.** A reply
split across several frames carries the exchange's opcode on each of them, and a reply
that arrives long after its request still carries it. Neither multipart nor delayed
replies are grounds for a second number.

**Where both ends may initiate, that is two exchanges, not one.** Each initiator MUST
be allocated its own number, and each is still answered at the number it was sent on:

```
0x0100  ClientEcho    C -> S  probe      S -> C  reply
0x0101  ServerEcho    S -> C  probe      C -> S  reply
```

Allocate by *exchange*, never by message role. Numbering a probe and its reply
separately — a "request" opcode and a "reply" opcode — reintroduces the asymmetry above
by another route, and is the mistake this rule is easiest to make while appearing to
follow it. There are no exceptions: every response in this protocol carries the opcode
of the request it answers.

**Category blocks, IANA-style.** Native space is allocated from 64-opcode,
`0x40`-aligned category blocks; `opcode >> 6` is the block index. A category that
outgrows its block MUST be granted an additional block and MUST NOT be renumbered.
Blocks 0–3 (`0x0000`–`0x00FF`) are the retail-mirror space.

**Variants become opcodes.** Body-byte type variants — retail's dialog pattern — MUST
NOT be used. Each would-be variant is allocated its own opcode within its category
block, so that it remains individually visible to dispatch and to latest-wins
versioning: a dialect can replace one opcode, but it cannot replace one variant.

The migration path therefore splits:

- A retail packet with 1:1 identity upgrades into the mirror space at its zero-extended
  number.
- A retail *variant family* decomposes into a category block, one opcode per variant,
  and never receives a single `0x00xx` mirror.

An in-body discriminator remains acceptable only for a closed set that is genuinely one
polymorphic message and would only ever be versioned as a unit.

---

## 3. Dialects

| Dialect | Byte | Status |
|---|---|---|
| V1 | `0xB0` | Current |

`0xB0`–`0xFE` is the valid range. `0xAB`–`0xAF` MUST be left unallocated as a buffer
above retail's `0xAA`.

`0xFF` MUST NOT be allocated as a dialect: it is the **system namespace** (§5), and its
permanence is what lets a reader route on byte 0 alone.

---

## 4. Category blocks

| Block | Range | Category |
|---|---|---|
| 0–3 | `0x0000`–`0x00FF` | Retail mirror |
| 4 | `0x0100`–`0x013F` | System / infrastructure |
| 5 | `0x0140`–`0x017F` | Dialogs (reserved) |

**Next free block: 6 (`0x0180`).**

---

## 5. The `0xFF` system namespace

`0xFF` is permanently reserved and is never a dialect. It carries connection-level
messages that are not packets, in their own envelope:

```
[0xFF] [u16 length] [u8 type] [payload]
```

`length` counts the type byte plus the payload — everything after the length field, the
same meaning it has in an extension frame. New connection-level messages are allocated a
**type** here, not an opcode.

| Type | Direction | Name | Payload |
|---|---|---|---|
| `0x00` | S → C | DialectOffer | `[u8 minDialect][u8 maxDialect]` |
| `0x01` | C → S | DialectChoice | `[u8 chosenDialect][string8 clientVersion]` |

**Next free type: `0x02`.**

`string8` is `[u8 length][Latin-1 bytes]`.

### 5.1 Capability marker

The one negotiation signal that is *not* in this namespace, because it predates the TLS
channel and rides inside a retail packet:

| Direction | Carrier | Body |
|---|---|---|
| S → C | Appended to the retail `0x7E` lobby greeting, plaintext | `[0x00]"HYB"[u8 markerVersion][u8 flags]` |

The capability marker is 6 bytes. `markerVersion` is currently `0x01` and versions the
marker envelope only — it is not a dialect number. `flags` is reserved and MUST be
written as zero. Clients MUST locate the marker by scanning the greeting body for the
4-byte magic `[0x00]"HYB"`, not by assuming a fixed offset.

The marker MUST NOT carry the dialect range; the range belongs inside TLS.

---

## 6. Packet registry

### Block 4 — system / infrastructure (`0x0100`–`0x013F`)

| Opcode | Since | Name | Probe | Reply | Body |
|---|---|---|---|---|---|
| `0x0100` | `0xB0` | ClientEcho | C → S | S → C | `[u64 token]` |
| `0x0101` | `0xB0` | ServerEcho | S → C | C → S | `[u64 token]` |

**Next free: `0x0102`.**

**ClientEcho / ServerEcho.** The liveness exchanges, one per initiator. The responder
MUST echo the token verbatim, at the same opcode, in the opposite direction. The token
is opaque to the responder; an initiator typically uses its own monotonic clock ticks,
so round-trip time falls out of the reply with no wire timestamps and no clock
synchronisation. Interval and timeout policy are the consumer's concern.

The two are separate exchanges because either end may initiate, per §2 — not a request
and a differently-numbered reply. `0x0100` is always answered by `0x0100`.

These replace retail's `0x45`/`0x75` heartbeats, whose asymmetry is exactly what the
allocation rule exists to prevent. They are not carried into any dialect.

### Block 0–3 — retail mirror (`0x0000`–`0x00FF`)

A retail opcode enters this space only when an extension type explicitly replaces it.

| Opcode | Since | Name | Direction | Body |
|---|---|---|---|---|
| `0x0008` | `0xB0` | Attributes | S → C | `[u32 blockFlags][blocks…]` |

**Attributes.** Replaces retail `0x08` for the duration of a negotiated connection.
Retail framing is unaffected, and a server SHOULD keep emitting retail `0x08` alongside
it so that path stays exercised rather than running only when the dialect has failed.

`blockFlags` selects blocks and nothing else; blocks follow in ascending bit order.
Retail's flag byte also carried standalone state (`UnreadMail`, movement mode), which
here lives in the Status block. All eight retail flag bits were allocated, which is why
the field is `u32`.

| Bit | Block | Size |
|---|---|---|
| `0x01` | Primary — level, ability, max HP/MP, str/int/wis/con/dex, level points, weight | 44 |
| `0x02` | Vitals — hp, mp (`u32` each) | 8 |
| `0x04` | Experience — experience, expToLevel, abilityExp, abilityToNext, gold (`u64`), gamePoints (`u32`) | 44 |
| `0x08` | Combat — ac (`i32`), mr/dmg/hit (`f64`), offensive and defensive element as effective/base/override (`u8`) | 34 |
| `0x10` | Status — movementMode, blinded, hasUnreadMail, hasParcel (`u8`) | 4 |
| `0x20` | ExtendedStats — `[u16 count][(u16 statId, f64 value) × count]` | 2 + 10·n |

An unknown `blockFlags` bit is a protocol error, not a skip: fixed-size blocks cannot be
skipped without knowing their size. **Adding a block therefore requires a dialect
version bump; adding a stat id does not.** ExtendedStats exists so the dialect rarely has
to move — its records are a fixed 10 bytes precisely so an unknown `statId` *is*
skippable.

ExtendedStats is a full snapshot within the block: absent means unchanged, present means
the receiver's extended state is replaced wholesale. Partial sends are safe for the fixed
blocks only because that set is closed, so "absent" can mean just one thing. An open field
set has no such guarantee — a missing id would be ambiguous between unchanged, no longer
applicable, and unknown to this sender.

`statId` indexes a shared enum in this package. Units are a property of the id, not of the
wire: multipliers, probabilities and flat ratings are all `f64`. Ids are permanent once
shipped, exactly like opcodes.

Retail `0x08` quantized several of these — `mr`/`dmg`/`hit` as a byte centred on 128 at
×800, primary stats as `u8`, `ac` as `sbyte`. Those widths had become caps on what the
game could express rather than limits on precision, which is the main reason this
replacement exists.
