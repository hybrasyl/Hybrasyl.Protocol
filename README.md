# Hybrasyl.Protocol 

A DOOMVAS extension protocol using TLS 1.3, support for 16-bit opcodes, and more.

**The authoritative wire contract is [`docs/EXTENSIONS.md`](docs/EXTENSIONS.md)** — byte
layouts, the connection sequence, the version policy, and the threat model — with opcode
and namespace assignments in [`docs/ALLOCATIONS.md`](docs/ALLOCATIONS.md). This README is
a summary; those documents are the specification, and both ship in the NuGet package.

This protocol is intended to be an extension to the native 0xAA framing (starting at 0xB0). Clients can declare what version they support, and interoperability between versions is a goal. By default, this version ships a single liveness exchange in the 0xB0 dialect: 0x0100 ClientEcho (probe C→S, reply S→C) and 0x0101 ServerEcho (probe S→C, reply C→S).

These protocol extensions overload the initial 0x7E "hello" packet (via 0xAA framing) to contain a capability marker: [0x00][0x48][0x59][0x42] [u8 version] [u8 flags] (eg \0HYB ...). Modern clients can use this as an indication that an upgrade to TLS is supported, then responding to a DialectOffer from the server (including minimum and maximum versions) with a DialectChoice naming the one dialect version that client speaks. A client implements exactly one; it reports that version whether or not it falls inside the offered range, and both sides derive the resulting connection mode from the (offer, choice) pair rather than signalling it separately.

The 16-bit opcode space is divided into 64-opcode categories, allowing 1024 categories. The first 4 categories (0x0000 - 0x00FF) are the retail-mirror space: a retail packet that keeps its 1:1 identity but gains an upgraded shape (longer lengths, wider fields) is carried at its zero-extended retail number, so retail 0x15 becomes 0x0015. Net new packets are allocated up from 0x0100 in category blocks.

A retail *variant family* — the dialog packets, where a body byte selects between shapes — does **not** get a single mirror opcode. It explodes into its own category block up in native space, one opcode per variant, so each stays individually visible to dispatch and to versioning: a dialect can replace one opcode, it cannot replace one variant. Opcodes are allocated as exchanges, with a request/response pair sharing one number across the two directions (send 0x0140, get 0x0140 back), since registration is split per direction and costs nothing.

Long term, the intent is for most if not all 0xAA opcodes to be extended and mirrored in the 0xB0 dialect, and ultimately replaced with 0xB1 and higher.
