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

A reference for a protocol somebody else defined can only be *descriptive* — you record
what it does, because you cannot change it and are not entitled to say what it ought to do.

This protocol is ours, and we write both ends. A document that describes what
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

## What does not belong in this repository

**This repository is public. The analysis of the retail protocol that informs these
documents is not.**

Keep out of every file here:

- Internal source-grading or confidence vocabulary.
- How a third-party binary was examined, what tooling was used, or its filename. Function
  addresses, symbol names and search methods included.
- Paths, commit hashes or links into private repositories — a citation nobody outside can
  follow is worse than none, because it looks checkable.
- Internal tracker identifiers.

Keep in:

- What the wire looks like, and what an implementation must do with it.
- A plain statement that a retail field is unused, or that a width is limiting, **where it
  explains a design decision here**. The conclusion earns its place; the provenance does
  not travel.

The test: *could a reader outside the project act on this sentence?* If it only tells them
how we came to know something, cut it. The detailed reasoning has a home already, and this
is not it.
