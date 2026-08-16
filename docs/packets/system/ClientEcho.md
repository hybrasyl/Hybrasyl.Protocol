# ClientEcho

The client-initiated liveness exchange: the client sends an opaque token, the server sends
it back unchanged.

| | |
|---|---|
| **Opcode** | `0x0100` |
| **Block** | 4 — system / infrastructure |
| **Dialect** | `0xB0` (V1) |
| **Initiator** | C→S |
| **Reply** | `0x0100`, S→C |
| **Body** | `[u64 token]`, exactly 8 bytes, both directions |

See [ServerEcho](ServerEcho.md) for the server-initiated direction. They are two exchanges,
not one exchange with an asymmetric reply.

## Wire format

```
[u64 token]
```

Eight bytes, big-endian. Identical in both directions. There is no other field, and the
body length is fixed — it does not vary by dialect version.

## Field semantics

**`token`** — opaque to the receiver. It has no structure the responder may rely on, and a
responder that inspects it is wrong.

An initiator typically uses its own monotonic clock ticks, which makes round-trip time fall
out of the reply directly: subtract the token from the current tick count. No timestamp
crosses the wire and no clock synchronisation is required or implied. **A token is not a
time and must not be read as one** — an implementation is free to use a counter, a random
value, or anything else it can correlate.

Interval, timeout and outstanding-probe policy belong to the consumer. This exchange
defines no cadence.

## Receiver requirements

A responder MUST:

- Echo `token` **verbatim**, byte for byte.
- Reply at `0x0100`, the same opcode it received. Not `0x0101` — that is the
  server-initiated exchange, and answering there is a protocol error rather than a
  cosmetic difference.
- Reply in the opposite direction to the probe.

A responder MUST NOT:

- Interpret, validate, canonicalise or range-check `token`. Every 64-bit value is legal,
  including zero and `u64::MAX`.
- Reply to a reply. The exchange is one round trip; a received `0x0100` is a probe if you
  did not send one with that token, and a reply if you did. Correlation is the initiator's
  problem — see below.

An initiator SHOULD:

- Correlate replies to outstanding probes by token, and discard a reply whose token it
  never sent. An unmatched token is not necessarily an attack — a stale reply from a
  previous connection, or a duplicate — but it must not complete a different measurement.
- Clear outstanding probe state on disconnect, so a late reply cannot land on a new
  session.

**On the wire the two halves are indistinguishable.** Nothing in the frame marks a message
as probe or reply. A receiver that cannot tell which it holds has not kept the state
required to tell — the protocol deliberately does not carry a flag, because the initiator
already knows what it sent.

## Malformed input

A body whose length is **not exactly 8 bytes** is invalid. Reject the packet; do not
attempt a partial read and do not pad.

The reference implementation throws `InvalidDataException` from `ClientEcho.Parse` in that
case. Frame-level concerns — a truncated frame, or one exceeding the negotiated maximum —
are handled by the framing layer before a body reaches here; see
[EXTENSIONS.md §4.2](../../EXTENSIONS.md).

An `0x0100` arriving on a connection with **no engaged dialect** is a protocol error. It
cannot be answered, because there is no channel to answer on.

## Forward compatibility

The body is fixed at 8 bytes and **cannot be extended in place**. A receiver keyed to
`BodyLength == 8` will reject a longer body, which is the intended behaviour and not a bug
to work around.

A future exchange needing a payload alongside liveness — a server-pushed status, a
version-carrying heartbeat — takes a new opcode in block 4. Do not widen this one.

## History

| Dialect | Change |
|---|---|
| V1 | Introduced. |

Replaces retail's `0x45`/`0x75` heartbeats, whose asymmetry — a probe answered at a
different number — is exactly what the per-exchange allocation rule exists to prevent.
Retail heartbeats are not carried into any dialect.
