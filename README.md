# Hybrasyl.Protocol 

A DOOMVAS extension protocol using TLS 1.3, support for 16-bit opcodes, and more.

This protocol is intended to be an extension to the native 0xAA framing (starting at 0xB0). Clients can declare what version they support, and interoperability between versions is a goal. By default, this version ships a simple 0x100 Ping / 0x101 Pong 0xB0 opcode.

These protocol extensions overload the initial 0x7E "hello" packet (via 0xAA framing) to contain a capability marker: [0x00][0x48][0x59][0x42] [u8 version] [u8 flags] (eg \0HYB ...). Modern clients can use this as an indication that an upgrade to TLS is supported, then responding to a DialectOffer from the server (including minimum and maximum versions) with a DialectChoice indicating the greatest dialect version the client supports.

The 16-bit opcode space is divided into 64-opcode categories, allowing 1024 categories. The first 4 categories (0x00 - 0xFF) are used to encapsulate variants of existing 0xAA packets (eg, with longer lengths, different fields, etc). Net new opcodes are expected to reasonably mirror opcodes on client / server side (send 0x1FF, get 0x1FF as a response) or at least within the same category as 0xAA variant types (dialogs, etc) are deconstructed into individual packets.

Long term, the intent is for most if not all 0xAA opcodes to be extended and mirrored in the 0xB0 dialect, and ultimately replaced with 0xB1 and higher.
