# Hybrasyl Extended Framing

_The key words "MUST", "MUST NOT", "REQUIRED", "SHALL", "SHALL NOT", "SHOULD",
"SHOULD NOT", "RECOMMENDED", "MAY", and "OPTIONAL" in this document are to be
interpreted as described in RFC 2119._

**Scope**: Hybrasyl Server, Brigid, and any other tool, client, or server using
`Hybrasyl.Protocol`.

**Status**: everything here is normative. What this library implements is a narrower
claim:

| Area | In `Hybrasyl.Protocol` v0.0.2 |
|---|---|
| Frame format, routing, dispatch (§4–§6.1) | Implemented |
| Dialect negotiation and the `0xFF` namespace (§3.4) | Implemented |
| Capability marker read/write (§3.1) | Implemented |
| TLS configuration and probe helpers (§8.5) | Implemented |
| Outbound shape selection (§6.2) | **Not implemented** |
| Transport selection, upgrade boundary, certificate trust, downgrade memory (§3.3, §8.3, §8.4) | The consumer's — this library holds no connection or cross-session state |
| Server and client duties (§9) | The consumer's |

A requirement being unimplemented here does not weaken it; several of them are
addressed to consumers by design.

> This file is the authoritative wire contract. It lives in the repository that
> implements it, so a consumer of the package can read the contract it is coding
> against. Changes to the wire format land here in the same commit as the code.
>
> Opcode assignments are in [ALLOCATIONS.md](ALLOCATIONS.md).

---

## 1. Overview

This document describes a negotiated protocol dialect layered *beside* the retail
Dark Ages protocol rather than replacing it. Retail framing (`0xAA`) remains the
baseline and is modeled by DALib.

An extension dialect is identified by a single byte starting at `0xB0`, carried in
every extension frame. A server advertises support by appending a marker to its
existing `0x7E` lobby greeting. A capable client accepts by upgrading the connection
to TLS 1.3, STARTTLS-style, and the two ends then negotiate a dialect *inside* the
encrypted channel. Where no negotiation occurs, both ends speak retail `0xAA`.

Extension frames carry u16 opcodes and travel inside TLS. Implementations MUST NOT
introduce cryptography of their own at any layer of this protocol.

### 1.1 Design goals

In priority order:

1. Never disturb retail clients. A retail client MUST be able to connect to a
   capable server, and a capable client to a retail server, with no observable
   change in behaviour.
2. Replace host and environment sniffing with protocol-level negotiation.
3. Allow wholesale format upgrades — widening a field, replacing a packet, adding
   new ones — without a capability matrix.
4. Keep old dialect versions expirable, so version-specific code has a bounded
   lifetime.
5. Keep DALib a pure retail model.
6. Obtain real transport confidentiality and integrity, which retail has none of,
   without writing any cryptography.

### 1.2 Rejected approaches

**Capability and feature-flag negotiation.** Capability sets accumulate permanently:
once a flag ships, some peer somewhere depends on it, and every construction site
grows a branch per flag. The result is combinatorial version branching that cannot
be retired. A single ordered dialect version replaces the set with one comparison,
and the floor mechanism (§7) makes retirement possible.

**Modifying `0xAA` framing.** Extension traffic MUST NOT alter retail framing in any
way; byte-compatibility with retail clients and third-party tooling is a hard
requirement.

---

## 2. Terminology

| Term | Meaning |
|---|---|
| **retail** | The stock Dark Ages / USDA / DOOMVAS protocol, framed with a leading `0xAA`. |
| **dialect** | A numbered version of the extension protocol, identified by one byte, `0xB0` and up. |
| **extension frame** | A frame in the layout of §4, carrying a dialect byte and a u16 opcode. |
| **capability marker** | The token a capable server appends to its `0x7E` lobby greeting (§3.1). |
| **shape** | The concrete field layout of one packet in one dialect. |
| **floor / ceiling** | The lowest and highest dialects a server supports (§7). |

---

## 3. Dialect identification and negotiation

Extension dialects are numbered from `0xB0` upward, one number per version:
`0xB0` is v1, `0xB1` is v2, and so on.

- Implementations MUST treat `0xB0`–`0xFE` as the valid dialect range.
- `0xFF` MUST NOT be allocated as a dialect. It is the **system namespace**, carrying
  connection-level messages that are not packets (§3.4).
- `0xAB`–`0xAF` MUST be left unallocated as a buffer above retail's `0xAA`.

A single ordered byte gives three properties the design depends on:

- **One version axis.** "Which dialect" is one comparison. A dialect determines every
  packet shape on the connection outright, with no set intersection to compute.
- **Expiry.** A server advertises a contiguous supported range and retires old
  dialects permanently by raising its floor. Capabilities, once shipped, cannot be
  retired.
- **Explicit framing.** A frame carries the dialect it was written under, so a
  mismatch between the wire and connection state is detected rather than misparsed
  (§4.2).

### 3.1 Capability marker

A capable server MUST signal capability by appending a marker to its existing `0x7E`
(`AcceptConnection`) lobby greeting. No new opcode is allocated.

`0x7E` is already the first server-to-client packet on the lobby connection, sent
unencrypted on accept, before `0x00`. Its retail body is `[0x1B]"CONNECTED SERVER"`
(16 characters, no terminator). Retail clients have no `0x7E` handler and ignore the
packet regardless of its contents, which is what makes the greeting a safe carrier.

The marker is appended after the greeting body:

```
[0x00] "HYB" [u8 markerVersion] [u8 flags]
```

- Total length 6 bytes. The leading `0x00` delimits the binary marker from the ASCII
  banner, which has no terminator. `"HYB"` is legible in a capture and is not emitted
  by retail or by third-party servers.
- `markerVersion` versions the marker envelope only. It is **not** a dialect number.
  A client that sees a `markerVersion` it does not recognise MUST still treat the
  marker's presence as capability and proceed; dialect details are settled inside TLS.
- `flags` is reserved in v1 and MUST be written as zero.
- Clients MUST detect the marker by scanning the greeting body for the 4-byte magic,
  not by assuming a fixed offset, so that greeting text may vary between server
  versions.

The marker MUST NOT carry the dialect range. The range belongs inside TLS (§3.4);
advertising it in cleartext would reintroduce the downgrade surface TLS removes.

### 3.2 Connection sequence

The lobby is the only server-first hop. Login and world are client-first: the client
opens with `0x10`. This asymmetry is what makes the handshake deadlock-free.

```
LOBBY (2610) — server speaks first

  C -> connect
  S -> 0x7E greeting  [+ capability marker if capable]     (retail framing, plaintext)
                                                           ...then silence
  C -> { 0x00 client-version (retail framing)  |  TLS ClientHello }
        - retail client, or no marker seen -> stays plaintext, normal lobby flow
        - capable client + marker seen     -> sends ClientHello; upgrade to TLS
  S -> 0x00 crypto key + serverTableCrc                    (in reply to the client's 0x00)
  C -> 0x57 server-table                                   (after comparing the CRC)

LOGIN (2611) / WORLD (2612) — client speaks first

  C -> connect
  C -> { 0x10 client-join (retail framing)  |  TLS ClientHello }
        - retail client   -> plaintext 0x10
        - capable client  -> ClientHello, if the lobby marker was seen

--- inside any established TLS channel ---

  S -> DialectOffer   [u8 min][u8 max]
  C -> DialectChoice  [u8 chosen][string8 clientVersion]
```

Two properties follow from this ordering, and implementations depend on both:

- **The server's `0x00` is a reply, not a second greeting.** After the greeting the
  server sends nothing until the client speaks. A client deciding whether to upgrade
  therefore does so against a drained buffer, with no server-originated plaintext in
  flight (§8.3).
- **No key material crosses the upgrade boundary.** Because the server's `0x00` — which
  carries the seed and key — is sent only after the client has spoken, on an upgrading
  connection it is emitted inside TLS. Even where retail crypto parameters are carried
  unchanged, they never exist in plaintext on such a connection.

### 3.3 Transport selection

A server MUST select a connection's transport by peeking the client's first inbound
byte, and MUST NOT revisit that decision for the life of the connection:

| First inbound byte | Transport |
|---|---|
| `0x16` (TLS handshake record) | Upgrade to TLS |
| `0xAA` (retail frame marker) | Plaintext retail |
| anything else | Reject the connection |

Correspondingly:

1. **Capability is discovered once, at the lobby, and applies to the session's later
   hops.** Login and world are client-first, so a client MUST decide whether to upgrade
   before the server speaks. It can only do so because the lobby marker already told it
   this server family is capable. A client MUST NOT send a `ClientHello` on login or
   world unless it saw the marker at the lobby: a retail server would read it as a
   malformed `0xAA` frame. The TLS handshake itself is per-connection; only the
   capability bit is session-scoped.

2. **Plaintext retail is the fallback, decided by content and never by a clock.** A
   retail or third-party server sends no marker, and the client sees that absence on
   the first packet — no timer or window is required. Symmetrically, a retail client
   never sends a `ClientHello`, so the server stays plaintext toward it. Implementations
   MUST NOT infer capability from elapsed time. A clock MAY declare a connection dead;
   it MUST NOT decide capability.

3. **A client MUST wait for the greeting before speaking at the lobby.** This is
   load-bearing, not stylistic. Because the server latches transport on the first
   inbound byte, a client that speaks unprompted presents `0xAA`, fixes the connection
   to plaintext, and then receives the capability marker *after* the decision the marker
   exists to inform. No `ClientHello` could follow, and the lobby upgrade would be
   unreachable.

4. **A client MUST bound its wait for the greeting, and MUST fail closed on expiry.**
   A server that accepts and then stays silent leaves the marker rule with nothing to
   decide on. On expiry a client MUST fail the connection with a legible error. It MUST
   NOT time out and assume retail: that would be a second stripping vector (§8.2),
   reachable by delaying the greeting rather than removing the marker.

### 3.4 Dialect negotiation

Once TLS is established, the server MUST send a `DialectOffer` as its first message in
the channel, and the client MUST answer with a `DialectChoice`.

Both travel in the **system-namespace envelope**, not as extension frames:

```
offset:  0        1              3         4
        [0xFF]   [u16 length]   [u8 type] [payload...]

  type 0x00  DialectOffer    [u8 minDialect][u8 maxDialect]
  type 0x01  DialectChoice   [u8 chosenDialect][string8 clientVersion]
```

`string8` is `[u8 length][Latin-1 bytes]`, matching the wire's existing string
convention.

- `length` counts everything after the length field — type plus payload — the same
  meaning it has in an extension frame. Readers and writers MUST agree on this.
- `length` is u16. The frame's u32 exists for map blobs and profile images; this
  namespace carries neither, and 64 KiB is a ceiling it will never approach.
- A reader MUST reject a message whose byte 0 is not `0xFF`, **before** interpreting the
  length. Otherwise a peer that is not speaking this protocol has its bytes read as a
  length, and the reader blocks for a body that never arrives.
- A reader MUST read exactly the prefix, then exactly `length` further bytes, and MUST
  NOT read beyond. A peer MAY pipeline frames immediately behind its message.
- A reader MUST reject a message carrying a type it did not expect at that point in the
  exchange, rather than parsing whatever arrived as whatever was due next.
- A reader MUST reject a payload whose length disagrees with the message's own shape.

**Why these are enveloped rather than bare.** Negotiation establishes the dialect, so —
alone among messages — it can never be re-versioned by the dialect mechanism. That is the
same one-time-choice property the frame's u32 length has. The length prefix is what
supplies an extension path anyway: a reader meeting an unrecognised type, or a payload
longer than it expects, can skip exactly `length` bytes and stay in sync. Without it these
messages could never change.

`0xFF` is reserved permanently for this namespace. Further connection-level messages that
are not packets are allocated new type values here rather than new opcodes.

- Both ends MUST reject a dialect byte outside `0xB0`–`0xFE`, and MUST reject an offer
  whose minimum exceeds its maximum.
- A client whose single dialect is within the offered range MUST choose that dialect.
- A client whose dialect falls **outside** the range — below the floor *or* above the
  ceiling — MUST still send a `DialectChoice` carrying its real dialect, unchanged. It
  MUST NOT substitute a dialect it does not implement in order to fit the range.
- Both ends derive the resulting mode from the (offer, choice) pair; it is never
  separately signalled.

Out-of-range in either direction resolves to `0xAA`-over-TLS. The two cases are
genuinely symmetric: a client implements exactly one dialect (§7), so it has nothing to
fall back *to* and nothing to reach *up* to. A client newer than the server has the same
options as one older than it — none — and retail-over-TLS is the shared floor both can
speak.

### 3.5 Connection modes

Three modes are coherent. The axes — whether TLS is in use, and which framing rides
inside it — are orthogonal.

| Mode | Reached by |
|---|---|
| **Plaintext `0xAA`** | A retail client, or a capable peer talking to one. |
| **`0xAA`-over-TLS** | A capable client that upgraded but whose dialect is outside the offered range, in either direction: retail semantics on an encrypted transport. |
| **`0xB0+`-over-TLS** | A negotiated extension dialect. |

A connection that has upgraded MUST NOT be downgraded to plaintext. An out-of-range
client simply speaks retail framing inside the TLS stream, which means such clients
still obtain confidentiality and proxies can carry retail traffic unchanged.

In the third mode the dialect is **additive, not a framing switch**: extension frames
(new and replaced packets) and retail `0xAA` frames (packets not yet migrated) coexist
on the same TLS stream and are routed per frame by byte 0 (§5.1).

### 3.6 Client state

A client tracks two independent pieces of state, and MUST NOT conflate them:

1. **Marker seen** — this server family is capable. This is what any retail-quirk
   gating should key on, and it remains true even if TLS or the dialect exchange
   subsequently declines.
2. **Dialect engaged** — a dialect was negotiated. This, and only this, gates the
   sending of extension frames.

A client MUST NOT gate extension sends on the marker alone.

---

## 4. Frame format

Extension frames travel inside the TLS 1.3 stream, so confidentiality, integrity, and
replay protection are TLS's responsibility and the frame carries no crypto of its own.
Because TLS delivers a byte *stream* rather than message-aligned records, the frame
carries its own length delimiter.

```
offset:  0            4            5             7          8
        [u32 length] [u8 dialect] [u16 opcode] [u8 flags] [body...]
```

Retail's per-opcode crypto patchwork (Normal / MD5Key / unencrypted) does not exist
inside extension frames. The TLS channel is the one uniform boundary.

### 4.1 Fields

**`length`** — u32, counting the bytes that follow the length field itself
(dialect + opcode + flags + body). No associated-data or envelope machinery is
required: TLS has already authenticated every byte.

The width is permanent and cannot be revised by the dialect mechanism, because the
length is read *before* the dialect byte that would select a version — a reader needs
the frame boundary before it can read anything else. u16's 64 KiB ceiling is already
marginal and would force every forum post, mail body, profile image, and map blob to
fit inside it. u64 is unusable range and pure denial-of-service surface. u32 clears any
realistic message with headroom.

**`dialect`** — u8, the dialect under which this frame was written.

- A writer MUST stamp the connection's negotiated dialect on every frame.
- A reader MUST reject a frame whose dialect is outside `0xB0`–`0xFE`.
- A reader on a negotiated connection MUST reject a frame whose dialect is not the one
  negotiated.

On a correct connection this byte is redundant, and it is not surfaced above the codec —
consumers receive typed packets and read the negotiated dialect from connection state.
It is retained as a desync guard: if the wire and connection state ever disagree, the
frame is refused on its header rather than parsed under the wrong shape into
structurally valid garbage. It also keeps a frame interpretable in a log or capture on
its own, which matters while the protocol is young.

**`opcode`** — u16, big-endian. See [ALLOCATIONS.md](ALLOCATIONS.md).

**`flags`** — u8. Every bit is reserved in v1.

- A writer MUST write `0x00`.
- A reader MUST reject any frame setting a bit no dialect defines.

Bit 0 is reserved for a future per-frame transformation opt-in (§8.6). Validity is
dialect-dependent once any bit is defined: a frame stamped v1 must still reject a bit
that a later dialect defines.

### 4.2 Frame size limits

Implementations MUST enforce a maximum frame size. A length prefix is an allocation
primitive — read length, allocate, read body — so a peer claiming a large length is a
memory-exhaustion vector.

- The claimed length MUST be validated **before** any buffering or allocation.
- A violation MUST be fatal to the connection.
- The cap MUST be interpreted as the **total wire size, length field included**, and
  MUST have the same meaning on the reader and the writer, so that any frame writable
  at a given cap is readable at that cap.
- A reader MUST reject a length below the minimum a body-less frame requires.

- **The cap MUST be less than `0xAA000004` (2,852,126,724).** This is what makes
  first-byte routing (§5.1) an invariant rather than a prediction. Above that bound a
  length field can begin `0xAA` or `0xFF` and an extension frame becomes
  indistinguishable from a retail frame or a negotiation message. Every realistic cap is
  orders of magnitude below it; the bound exists so no implementation can wander past it.

The cap is otherwise a deployment parameter, required at any field width; it is what
keeps u32's theoretical 4 GiB from ever materialising. `Hybrasyl.Protocol` defaults to
8 MiB, and its cap is an `int`, so it cannot approach the routing bound.

### 4.3 Encoding rules

**All fields are big-endian, envelope and body alike, without exception.** Retail is
big-endian throughout, and mixed byte order gains nothing when consumers deal in typed
packets rather than raw bytes. Uniform big-endian keeps a captured frame readable field
by field in a hex dump. A migrated retail packet keeps big-endian even if some other
tool reports the same value the other way around.

Bodies MAY carry widths retail never used. Retail readers and writers stop at 32 bits;
`Hybrasyl.Protocol` supplies wider types in the same byte order — `u64` and `i64` today,
floating-point and others as packets require. Implementations MUST NOT assemble a wide
field from narrower halves.

---

## 5. Coexistence with retail framing

DALib is unchanged and remains the pure `0xAA` codec. The extension frame is not retail
framing, so teaching DALib to parse it would put a second framing implementation inside
DALib. The alternate framing and length parser live entirely in the shared library.

### 5.1 Routing

A consumer's read loop MUST peek byte 0 and route:

| Byte 0 | Destination |
|---|---|
| `0xAA` | DALib's retail codec, called exactly as today |
| `0xFF` | The system-namespace negotiation reader (§3.4) |
| anything else | The extension codec |

All three genuinely share one stream: a below-floor connection carries retail frames
inside TLS, and negotiation precedes everything on any upgraded connection. Routing by
content rather than by connection state is what keeps that correct without the read loop
tracking where it is in a sequence.

Implementations MUST route on `== 0xAA` and `== 0xFF` rather than on `== 0x00`, and MUST
NOT route on the dialect byte, which sits at offset 4.

**No collision is possible, and it does not depend on the configured cap.** Byte 0 of an
extension frame is the high byte of its big-endian length, so it equals `0xAA` only at a
length of `0xAA000000` — 2,852,126,720 bytes, roughly 2.66 GiB — and `0xFF` only at
`0xFF000000`, roughly 3.98 GiB. A u32 can express both, which is why §4.2 makes the
`< 0xAA000004` cap a **MUST** rather than an observation: under it, the length field can
never begin with either marker, and the property holds for any conforming implementation
rather than for any particular deployment's tuning.

Below a 16 MiB cap byte 0 is exactly `0x00`; between there and the bound it is merely
some other value that is neither marker, which the router handles identically. Route on
the markers, never on `== 0x00`.

### 5.2 Additivity

The extension dispatch table holds only explicitly declared extension packets. Nothing
is composed automatically:

- Packets not yet migrated never enter it. They travel as literal `0xAA` frames on
  DALib's codec, where the first-byte router sends them.
- A retail opcode joins the extension space only when something explicitly replaces it
  with an extension type carrying the corresponding u16 opcode — `0x0015` for retail
  `0x15`.
- `0xAA` is not a valid dialect, so a frame stamped `0xAA` is refused by the extension
  reader before dispatch is reached.

This is what "additive" means mechanically.

---

## 6. Packet shapes and resolution

Each packet type declares **the dialect that introduced its shape**. In this library
that is `[ExtensionClientOpcode(opcode, since)]` and
`[ExtensionServerOpcode(opcode, since)]`, where `since` is the dialect whose shape the
type represents — `Dialect.V1` means "the `0xB0`-and-later shape of this opcode".

Only extension types are registered. Retail types are not in the table.

### 6.1 Receiving

An incoming `(dialect, opcode)` MUST resolve to the registered type with the **highest
introduction less than or equal to that dialect** — latest-wins, as with schema
migrations.

A type therefore serves its introducing dialect and every later one until something
supersedes it. A shape unchanged across dialects is declared once. The consequence is
the property the scheme exists for: **a new dialect that changes three packets is
exactly three new types and zero edits to existing ones.**

Note that a type identifies a *shape*, not a dialect. Consumers needing the negotiated
dialect read it from connection state.

### 6.2 Sending

**Parse and emit are deliberately asymmetric.**

Parsing is driven by what arrived. Byte 0 routes the frame, so a retail frame reaches
DALib and never the extension dispatch; a retail type registered in that table would be
unreachable by construction.

Emitting is driven by choice, and there retail *is* a candidate. For each migrated
packet the sender selects between retail's `0xAA` shape and the newest extension shape
this connection negotiated: latest-wins over `[0xAA, 0xB0, 0xB1, …]`, capped at the
negotiated dialect.

Without this mechanism every send site touching a migrated packet grows an
`if (retail) … else if (v1) … else if (v2) …` ladder — the exact failure mode this
design exists to avoid.

**What selection cannot do.** A replacement shape carries more information than the
shape it replaces; that is the point of widening a field. Rendering a u16 stat into
retail's u8 has no correct answer, only a chosen one — clamp, saturate, or sentinel —
and the same holds for floating-point into fixed-point. Each migrated packet therefore
requires a **hand-written rendering per live dialect, retail included.** Selection
removes the ladder, not the work: the cost falls from *(send sites × packets)* to
*(packets × live dialects)*.

That second number is bounded by the floor, which is what makes an expirable floor
load-bearing rather than decorative. **Raising the floor deletes renderings.** A
protocol without one accumulates them permanently.

Selection is not yet implemented in this library.

### 6.3 Layering

Four layers. The two interior boundaries exist to keep each decision where it can
actually be made.

| Layer | Owner |
|---|---|
| Domain object — a player and its real stats | The server or client; neither library |
| Wire shapes | DALib (`0xAA`) and this library (`0xB0+`) |
| Selection: which shape to use | This library |
| Rendering: domain object into that shape's fields | The sending consumer |

The middle two are the protocol; the outer two are the game. This library has no
business deciding what 65,535 HP looks like in a u8, and a server has no business
knowing which dialect byte to stamp.

Renderings belong to whichever side sends that direction: a replaced server-to-client
packet is rendered by the server, a replaced client-to-server packet by the client.
Both draw on the same selection mechanism.

**A shape both ends speak MUST be declared in this library.** Implementations MAY scan
additional assemblies for genuinely private extensions, but a shared shape declared in
one end's assembly leaves the other end with no type to resolve.

---

## 7. Version support policy

- A client release MUST implement exactly one dialect version: its newest.
- A server MUST support a contiguous range `[floor..ceiling]` and MUST advertise it as
  a `DialectOffer` inside the established TLS channel (§3.4).
- A client within the range negotiates that dialect. A client outside it — below the
  floor or above the ceiling — falls to `0xAA`-over-TLS. A retail client stays on
  plaintext `0xAA`.
- Raising the floor therefore retires old clients to retail-over-TLS, and a client
  released ahead of a server's deployment lands in the same mode until the server's
  ceiling catches up. Neither is an error state.
- A server MUST maintain the retail baseline indefinitely, for interoperability with
  retail clients and third-party tooling.
- Version-specific packet-shape code is bounded by the server's support window and is
  retired by raising the floor. There is no capability matrix at any point.

---

## 8. Transport security

Implementations MUST NOT use hand-rolled cryptography. The extension channel is a
STARTTLS-style opportunistic upgrade of the existing TCP connection to **TLS 1.3**. All
extension frames MUST travel inside that channel. This provides authenticated key
exchange, forward secrecy, AEAD, and downgrade protection from an audited platform
stack.

### 8.1 Why STARTTLS rather than TLS from byte zero

The same TCP port carries retail-framed plaintext — retail clients, and every
connection's pre-negotiation phase. TLS cannot own the socket from byte zero without
breaking retail coexistence, and a server cannot distinguish a retail client from a
capable one until the client reacts. So the server emits a plaintext probe, a capable
client answers by starting a handshake, and a retail client never does. A single port
is preserved.

### 8.2 Threat model

Clients are **endpoint-untrusted**. The player runs the client and can read their own
keys, memory, and traffic. This channel does **not** protect the server from the player
and does **not** prevent botting or cheating by a legitimate user. Endpoint-user attacks
and protection of server secrets are explicitly out of scope.

It protects:

- **Confidentiality against network-position attackers** — LAN, wifi, ISP. Passwords,
  chat, and direct messages. Retail provides none: the `0x00` lobby handshake sends seed
  and key in cleartext, the `0x03` redirect re-keys in cleartext, and the MD5 key table
  derives from the non-secret character name. On an upgrading connection those
  parameters never appear in cleartext at all (§3.2).
- **Integrity against tampering in transit.**

**Stripping is the honest limit.** TLS protects its own negotiation once started; it
does not protect the plaintext decision *to* start, and that decision rides on the
`0x7E` marker, in the clear, on first contact. An attacker who strips the marker leaves
a client that never attempts an upgrade, observes nothing unusual, and sends its
password over plaintext retail framing. Neither end can detect this from inside TLS,
because TLS never happened.

There is no in-band fix. The mitigation is memory held by the client:

- A client MUST record that an authenticated TLS session succeeded against a given
  server identity.
- On a later connection to that same identity that does not upgrade, a client SHOULD
  treat the absence as hostile and warn before any credential flow, rather than falling
  back silently.
- This store SHOULD be shared with the certificate pins of §8.4, which are keyed by the
  same endpoint identity.

First contact remains strippable; every connection after it is protected. This belongs
to the client: the library never sees the credential flow and holds no cross-session
state.

### 8.3 Upgrade boundary

**Across the transition, buffered plaintext application data MUST be discarded, and TLS
bytes MUST be replayed intact.** These are two different buffers, and both halves are
required.

- **Nothing the peer sent before the trigger may survive into the TLS session.** The
  entire STARTTLS injection CVE class is this bug: an endpoint buffers plaintext
  received before the handshake and processes it afterward as though it had arrived over
  TLS.
- **Everything from the trigger byte onward belongs to TLS and MUST reach the TLS
  implementation.** The `0x16` and whatever was read alongside it are handshake records,
  not application data; discarding them fails the handshake outright. A detector MUST
  therefore *peek* rather than consume, and every byte from that point MUST be handed on
  intact.

**A dirty buffer MUST NOT fail the connection.** The rule above governs an upgrade that
proceeds; it does not require killing a connection whose buffer is unclean. On finding
bytes after the greeting frame, a client MUST decline the upgrade and remain on retail
framing, and MUST NOT discard those bytes — they are delivered normally.

The security property is unaffected, because it was never "the buffer is always clean"
but "an upgrade proceeds only from a provably empty buffer." Declining is a correct
outcome of that rule. Failing closed here would break third-party servers that do not
go silent after the greeting.

### 8.4 Certificate validation

- **Default: system-root validation.** A server presenting a publicly trusted
  certificate valid for its hostname is accepted with no prompt. This preserves the
  interoperability promise: third-party servers can participate without being pinned to
  any particular operator.
- **Trust-on-first-use fallback.** Where validation fails — self-signed, unknown CA,
  hostname mismatch, and therefore localhost, development, and self-hosted servers — a
  client SHOULD present the certificate's subject, issuer, and SHA-256 fingerprint and
  ask whether to trust it for that `host:port`. On acceptance the client MUST pin the
  fingerprint, keyed by endpoint. A **changed** certificate for a pinned endpoint MUST
  re-prompt. Without this, localhost and any server lacking a public certificate could
  not use TLS at all.
- **Pre-pinned production certificates.** A client distribution SHOULD ship with its
  flagship server's production fingerprint pinned, so the common path never sees a
  trust dialog and is protected even against a mis-issued public certificate for that
  hostname. Trust-on-first-use is then reserved for genuinely self-hosted servers.
- **Limitation.** A trust-on-first-use connection is subject to interception if the user
  accepts a substituted certificate. The operator's mitigation is a published
  fingerprint; pinning protects every connection after the first.

### 8.5 TLS parameters

- **TLS 1.3 only.** Both ends are ours, so there is no 1.2 fallback, no legacy
  cipher-suite negotiation, and no downgrade dance. The server holds the certificate;
  the client validates.

  **This MUST be enforced as a postcondition, not requested as a precondition.**
  Implementations MUST leave the enabled protocol set at the platform default and, once
  the handshake completes, MUST verify the negotiated protocol is TLS 1.3 and drop the
  connection otherwise. They MUST NOT pin an explicit protocol version in the handshake
  options.

  Pinning is not portable: some platform TLS stacks refuse an explicit request above
  TLS 1.2 and fail the handshake before any certificate is examined, which makes the
  extension channel unreachable there for both ends. A postcondition holds everywhere,
  and is strictly stronger — it also catches a stack that silently negotiates something
  older, which a pin cannot. No application data has crossed at the point of the check,
  so a refused connection discloses nothing.

  Where a platform genuinely cannot negotiate TLS 1.3, the extension channel is
  unavailable there and plaintext retail framing remains. That is a legible failure with
  an accurate message, and it is the intended outcome rather than a silent downgrade.
- **A target host name MUST be supplied by the client, and MUST NOT be empty.** The
  target host is what the platform validates the presented certificate against. With it
  empty the handshake still completes, still encrypts, and still reports no error — but
  any chain-valid certificate is accepted, which is precisely the interception this
  channel exists to prevent, wearing a working connection's clothes. Implementations
  MUST refuse an empty target host rather than making an identity check conditional on
  an optional callback.
- **Revocation checking MUST default to on.** Platform defaults commonly disable it, in
  which case a revoked server certificate validates cleanly for the remainder of its
  stated lifetime. Since the default trust path is public-CA validation, revocation is
  the only mechanism that retires a compromised certificate before expiry.

  It is a parameter rather than a constant because two cases legitimately disable it:
  the trust-on-first-use path, where there is no responder to ask and the check buys
  only a doomed round trip; and offline or air-gapped deployments, where availability
  outranks revocation enforcement. Both are explicit deployment choices. The cost is
  real — an unreachable responder yields an unknown status, which fails validation
  unless the trust callback accepts it — so "on" is both the stricter and the more
  brittle setting.
- **Per-connection handshake** on all three hops. TLS 1.3's 1-RTT handshake makes this
  inexpensive.
- **The handshake MUST be bounded.** Neither the TLS handshake nor the dialect exchange
  that follows it bounds itself, so a peer that advertises capability and then stalls
  blocks indefinitely. On expiry the connection MUST fail with a legible error and MUST
  NOT fall back to plaintext — the same rule as §3.3: a clock may declare a connection
  dead, it may never decide capability. How long is a deployment parameter, like the
  frame-size cap.
- **0-RTT early data MUST NOT be used.** Its replay characteristics are not worth the
  saved round trip.
- **Session resumption is not used.** Handshake frequency is trivially low: a session
  pays the lobby, login, and world handshakes once, over hours. Implementations MAY
  revisit this if a future topology makes reconnects both frequent and not already
  hidden behind an existing loading stall.
- An ALPN tag MAY be negotiated. It is cosmetic, since the socket is already ours.

### 8.6 Compression

Compression is **not available in v1**. TLS 1.3 removed record compression because of
the CRIME and BREACH attack class, and this protocol does not reintroduce it.

If compression is later adopted it MUST be a per-frame opt-in signalled by `flags`
bit 0, MUST NOT use a cross-frame dictionary, and MUST NOT be applied to a frame that
mixes a secret — a password or token — with echoed user content.

---

## 9. Implementation requirements by role

**Shared library — `Hybrasyl.Protocol`.** Extension packet types, framing, dialect
negotiation, and the capability marker, consumed by both ends. No cryptography lives
here; TLS is the platform's.

**DALib.** Unchanged. It remains the pure retail `0xAA` codec. The first-byte router
and the extension framing parser live in the shared library. No divergent semantics
enter DALib, which models the retail wire format as ground truth.

**Servers.** A server MUST append the capability marker to its `0x7E` greeting if
capable, peek the first inbound byte to select transport (§3.3), perform the TLS upgrade
at that boundary, send the `DialectOffer` as its first message inside TLS, and route
inbound frames by byte 0.

**Clients.** A client MUST wait for the lobby greeting before speaking, scan it for the
marker, and upgrade only where the marker was seen. It MUST carry the capability bit
across the session's later hops, answer the `DialectOffer` with a `DialectChoice`,
validate certificates per §8.4, and maintain the downgrade memory of §8.2. Extension
state, the TLS upgrade, and the certificate trust store are per-connection and MUST be
reset on disconnect.
