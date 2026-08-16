# ServerEcho

The server-initiated liveness exchange: the server sends an opaque token, the client sends
it back unchanged.

| | |
|---|---|
| **Opcode** | `0x0101` |
| **Block** | 4 — system / infrastructure |
| **Dialect** | `0xB0` (V1) |
| **Initiator** | S→C |
| **Reply** | `0x0101`, C→S |
| **Body** | `[u64 token]`, exactly 8 bytes, both directions |

Identical in shape and rules to [ClientEcho](ClientEcho.md), with the directions swapped.

## Why this is a separate opcode

It would be possible to answer a server probe on `0x0100`. That is precisely what the
allocation rule forbids: an opcode identifies **one exchange**, and the direction says
which half. Reusing `0x0100` for a server-initiated probe would make the direction
ambiguous — a C→S `0x0100` could be either a client probe or a reply to a server probe,
and a receiver would need out-of-band state to tell them apart.

Two numbers cost nothing and make each message self-describing given only the direction it
arrived from. See [ALLOCATIONS.md §2](../../ALLOCATIONS.md).

## Wire format

```
[u64 token]
```

Eight bytes, big-endian. Identical in both directions.

## Field semantics

**`token`** — opaque to the receiver, exactly as in `ClientEcho`. The server chooses it,
the client returns it, and only the server attaches meaning to it.

## Receiver requirements

A client receiving `0x0101` MUST:

- Echo `token` verbatim.
- Reply at `0x0101`, C→S.

A client MUST NOT interpret the token, and MUST NOT answer on `0x0100`.

A server SHOULD correlate replies by token and clear outstanding probes on disconnect, per
the initiator guidance in [ClientEcho](ClientEcho.md).

**A client that never initiates still MUST answer.** Responding is not optional and is not
conditional on the client having its own liveness policy — the server may be using this to
decide whether the connection is alive.

## Malformed input

A body whose length is not exactly 8 bytes is invalid; reject the packet.
`ServerEcho.Parse` throws `InvalidDataException`.

An `0x0101` arriving with no engaged dialect is a protocol error.

## Forward compatibility

Fixed at 8 bytes, not extensible in place. A richer server-push exchange takes a new opcode
in block 4.

## History

| Dialect | Change |
|---|---|
| V1 | Introduced. |

## Implementation status

**Both halves of `ClientEcho` are implemented.** For `ServerEcho`, as of 2026-08-16, the
Hybrasyl server registers a handler that logs an inbound reply but **never initiates a
probe** — there is no send site outside tests. A client is still required to answer if one
arrives; the exchange is specified, and only the server's probe side is unbuilt.

The open decision there is whether to wire the server-initiated direction or drop it and
remove the receive-side handler, so the asymmetry is not later mistaken for an oversight.
