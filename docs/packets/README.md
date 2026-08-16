# Packet reference

_The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD",
"SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this directory are to be
interpreted as described in RFC 2119._

One file per **exchange**, organised by the category block that owns its opcode.

```
packets/system/          block 4  (0x0100-0x013F)  system / infrastructure
packets/retail-mirror/   blocks 0-3 (0x0000-0x00FF)  1:1 replacements of a retail packet
```

Directory names track the block names in [ALLOCATIONS.md](../ALLOCATIONS.md) §4. When a
block is added there, add the matching directory here.

## One file per exchange, not per direction

Opcodes are allocated per **exchange**, not per message: a probe and its reply share one
number, and the direction says which half you are looking at. `ClientEcho` is C→S *and*
S→C at `0x0100`. So a file covers both halves, and there is deliberately no `client/` and
`server/` split — that structure belongs to retail, whose opcodes are direction-scoped and
where S→C `0x08` and C→S `0x08` are unrelated packets.

## These documents are prescriptive

This is the difference that matters, and it is easy to get wrong by copying the retail
reference in Comhaigne.

Those files are **archaeology**. They describe a binary nobody here controls, so their
value is in observations — *this cache has no reader*, *this byte is dead*. Description is
the only thing available.

Extension packets have no third party. We write both ends. A document that describes what
our implementation currently does is unfalsifiable: the implementation cannot disagree with
it, and the file decays into a changelog of itself.

So a packet document states the **contract an implementation must meet** — what a receiver
MUST reject, what it MAY ignore, what happens on malformed or truncated input, what a later
dialect may add without breaking this one. If a statement here could not be violated by a
conforming implementation doing the wrong thing, it is not pulling its weight.

The bytes are the easy half. The failure modes are the half that gets discovered in
production.

## Template

```markdown
# Name

One sentence: what this exchange is for.

| | |
|---|---|
| **Opcode** | `0xNNNN` |
| **Block** | N — category |
| **Dialect** | `0xB0` (V1) |
| **Initiator** | C→S / S→C / either |
| **Reply** | same opcode, opposite direction / none |
| **Body** | `[...]` or "variable, see below" |

## Wire format
Byte layout. Types, order, sizes. Big-endian, as everywhere in this protocol.

## Field semantics
What each field means, its units, and its valid range. Units live here, not on the wire.

## Receiver requirements
MUST / SHOULD / MAY. What to do with each field. What to do when one is out of range.

## Malformed input
Exact conditions that make a body invalid, and what a receiver does — reject the frame,
drop the connection, or ignore the packet. Say which.

## Forward compatibility
What a later dialect may add. What a V1 receiver does when it sees it. Whether a field can
be extended in place or needs a new opcode.

## History
Dialect version each change landed in.
```

## Where things live

| | |
|---|---|
| [EXTENSIONS.md](../EXTENSIONS.md) | Framing, negotiation, TLS, version policy, threat model. No packet bodies. |
| [ALLOCATIONS.md](../ALLOCATIONS.md) | The registry: which number is which packet, and the allocation rules. One line of body shape, as a hint. |
| `packets/` | The full contract for each exchange. |

A body layout is specified in exactly one place — here. The registry's body column is a
lookup aid, not a second definition; when they disagree, this wins and the registry is
wrong.
