# Client observations: what the server sends a test player, without a client

Date: 2026-07-17
Status: implemented (this pass): `ITestPlayer.Client` shipped with decoders for block
highlights, particles, mod-channel packets and chat lines
Tracks: issue #100 "Client-side testing" (tier 2 of three), from the Caminus field request
(VS 1.22.7, Atlas 0.11.0) and a same-day request on Discord (Artalus)
Game versions verified: 1.22.0 as the reference (decompiled and run live), 1.22.7 and
1.21.7 (decompiled; 1.21.7 also run live, it is the CI floor lane)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[world snapshot/rollback](2026-07-06-world-snapshot-rollback.md),
[pre-1.22 compatibility](2026-07-12-pre-122-compat.md)
Sibling: [client-side testing](2026-07-17-client-side-testing.md), the headless-client
feasibility spike (tier 1), written separately

## Motivation

Caminus's server side reacts to a player's thermal state by calling
`sapi.World.HighlightBlocks(player, slot 7, positions, colors)`,
`sapi.World.SpawnParticles(...)`, and by sending a protobuf `OverlayPacket` over its mod
network channel `"caminus"`; its client side renders all three. Atlas embeds a server only,
so none of it was assertable: 36 server scenarios green and the overlay untested. The
tiers in issue #100 rank a real headless client first for value, but the test player's
dummy connection already receives every byte a real client would, so tapping and decoding
that stream covers the bulk of the need (highlights, particles, packets, chat) with no
client process, no window, no GPU, and no new dependency. That is this pass.

## Method

- Decompilation (ILSpy) of the send path (`ServerMain.SendPacket` overloads,
  `BroadcastArbitraryPacket`, `SendPacketFast`, `DummyNetConnection`, `DummyNetwork`,
  `DummyTcpNetClient`), the packet builders (`SendHighlightBlocksPacket`,
  `SpawnParticles`, `ServerPackets.ChatLine`, `NetworkChannel.GenPacket`), the client
  handlers they target (`SystemHighlightBlocks.HandlePacket`,
  `GeneralPacketHandler.HandleSpawnParticles`, `NetworkAPI.HandleCustomPacket`,
  `NetworkChannel.SetMessageHandler`) and the two renderers that consume the colors
  (`BlockHighlight` plus `MeshData.AddVertexSkipTex`, `ParticlePoolQuads` plus
  `ParticleGeneric.UpdateBuffers` and the `particlesquad.vsh` shader), on 1.22.0,
  1.22.7 and 1.21.7.
- Live runs of the E2E suite below against 1.22.0 (the default install) and 1.21.7.

## The tap point, as measured

Every server-to-client TCP send ends in one of two `DummyNetConnection` methods for a
test player, and both enqueue the serialized `Packet_Server` bytes into the shared
`DummyNetwork.ClientReceiveBuffer` under `ClientReceiveBufferLock`:

- `SendPacket(int clientId, byte[])` (what `SendPacket(IServerPlayer, Packet_Server)`,
  `SendPacket(int, Packet_Server)` and the particle loop use) compresses only when
  `!IsSinglePlayerClient`, so a dummy connection always receives plain bytes, then calls
  `Socket.Send(bytes, compressed: false)`.
- `BroadcastArbitraryPacket(Packet_Server, ...)` and `SendPacket(int, BoxedPacket)`
  call `PreparePacketForSending` (the dummy override clones the buffer, never compresses)
  then `SendPreparedPacket`/`HiPerformanceSend`.
- `SendPacketFast` first tries `DummyNetConnection.SendServerPacketDirectly`, an
  in-process shortcut into `ClientSystemStartup.instance`; with no client process that
  instance is null, the shortcut returns false and the call falls through to the
  serialized send above.

`DummyTcpNetClient.ReadMessage()` is the exact call a real client's network loop makes to
dequeue that buffer, under the same lock, and returns a public `NetIncomingMessage`
(`message`, `messageLength`). Nothing else ever reads the buffer (issue #4's spike noted
that it accumulates). Atlas already holds the `DummyTcpNetClient` for each player
(`DummyPlayerConnection.TcpClient`, used to send the join packets), so the tap is: drain
`ReadMessage()` on the game thread when a scenario reads the surface, and decode each
buffer with `Packet_ServerSerializer.DeserializeBuffer`, the engine's own serializer. No
subclassing, no interception on the sending thread, no lock of Atlas's own: the engine's
lock is the only cross-thread handoff, and the drain doubles as the buffer's first
consumer. The UDP side (`DummyUdpNetServer`, entity positions and UDP mod channels) is
not tapped.

Packets are dispatched on which sub-message they carry (`HighlightBlocks`,
`SpawnParticles`, `CustomPacket`, `Chatline`), not on `Packet_Server.Id`: the ids are
literals in the send sites, not reflectable constants, and they do not follow the
protobuf field tags either (`SpawnParticlesFieldID` is 60 while the packet id is 61,
`ChatlineFieldID` is 7 while the id is 8), whereas each client handler reads exactly the
sub-message, so its presence is the authoritative signal.

## Engine symbols, verified on 1.21.7, 1.22.0 and 1.22.7

| Symbol | Role | 1.21.7 | 1.22.0 | 1.22.7 |
|---|---|---|---|---|
| `DummyNetConnection.Send` / `SendPreparedPacket` | enqueue into `ClientReceiveBuffer` | same | same | same |
| `DummyTcpNetClient.ReadMessage()` | the drain, public, engine-locked | same | same | same |
| `ServerMain.SendPacket(int, byte[])` compress guard | `!IsSinglePlayerClient` | same | same | same |
| `Packet_ServerSerializer.DeserializeBuffer(byte[], int, Packet_Server)` | decode | same | same | same |
| `Packet_Server.Id` for `HighlightBlocks` / `SpawnParticles` / `CustomPacket` / `Chatline` | client dispatch | 52 / 61 / 55 / 8 | same | same |
| `Packet_HighlightBlocks` (`Slotid`, `Blocks`, `Colors`, `ColorsCount`, `Mode`, `Shape`, `Scale`) | highlight payload | same | same | same |
| `BlockTypeNet.PackBlocksPositions` / `UnpackBlockPositions` | zstd-packed X/Y/Z with dimension folded into Y | same | same | same |
| `Packet_SpawnParticles` (`ParticlePropertyProviderClassName`, `Data`) | provider name plus `ToBytes` payload | same | same | same |
| `IClassRegistryAPI.CreateParticlePropertyProvider(string)` + `IParticlePropertiesProvider.FromBytes` | provider rebuild, as the client does | same | same | same |
| `Packet_CustomPacket` (`ChannelId`, `MessageId`, `Data`) | mod-channel payload, protobuf-net body | same | same | same |
| `NetworkChannelBase.channelId` (internal int), `.messageTypes` (internal `Dictionary<Type,int>`) | the wire ids, read by reflection | same | same | same |
| `IServerNetworkAPI.GetChannel(string)` | channel lookup by name | same | same | same |
| `Packet_ChatLine.Message` | chat line text | same | same | same |
| `MeshData.AddVertexSkipTex` writes the highlight color int verbatim into the RGBA vertex bytes | red in the lowest byte | same | same | same |
| `ParticlePoolQuads` unpacks `ColorRed = (byte)color`, `ParticleGeneric.UpdateBuffers` uploads (B, G, R, A), shader reads it as `rgbaBlockIn` | red in bits 16 to 23 | same | same | same |

The two internal fields are the only reflective touchpoints; `EngineCompat` resolves them
once per process and `ValidateAtBoot` fails fast with the game version and the missing or
retyped symbol named. Everything else is a compile-time binding to members that exist
unchanged on every supported version (the single-binary source rule of the pre-1.22 spec).

## The surface

`ITestPlayer.Client` is an `IClientObservations`; every member runs on the game thread:

- `IReadOnlyList<HighlightedBlock> Highlights(int slot)`: the positions and colors of the
  last `HighlightBlocks` packet for that slot. The client replaces a slot's highlight on
  every packet and deletes its mesh when the packet carries no positions
  (`BlockHighlight.TesselateModel` returns early on an empty array), so "latest packet
  wins, empty clears" is exactly the client's state. Colors follow
  `BlockHighlight.TesselateArbitraryModel`: one color per position only when at least as
  many colors as positions were sent (and more than one), otherwise the first color for
  every position; 0 when none were sent (a client then draws its own default). Mode,
  shape and scale are not lifted (add when a mod needs them; the packet carries them).
- `IReadOnlyList<SpawnedParticles> Particles()`: every spawn, oldest first. The provider
  is rebuilt with `CreateParticlePropertyProvider(className)` then `FromBytes`, as
  `HandleSpawnParticles` does. For the `simple` provider (what every
  `World.SpawnParticles(quantity, color, minPos, maxPos, ...)` overload builds) the
  record lifts the deterministic anchors: `Position = MinPos`, `Velocity = MinVelocity`,
  `Quantity = MinQuantity`, `Color`; the provider's own `Pos`/`Quantity`/`GetVelocity`
  are randomized within the `Add*` extents on every read, which is why the anchors are
  lifted instead. Other providers get their own `Pos`, `GetVelocity`, `Quantity` read
  once at decode and `Color = 0`, since block, item and advanced providers resolve their
  color from client-side textures. `Provider` is the escape hatch for everything else.
- `IReadOnlyList<T> Packets<T>(string channel)`: custom packets whose channel id and
  message id match, deserialized with `ProtoBuf.Serializer.Deserialize<T>` (the same call
  `NetworkChannel.SetMessageHandler` makes on a client). The ids come from the server's
  own channel registry: both sides register symmetrically and hand out message ids in
  registration order, so the server knows every name and type a client would. `T` is
  matched by full type name, not `Type` identity, because the game's `ModLoader` loads the
  staged dll through `Assembly.UnsafeLoadFrom` and a scenario assembly referencing the
  mod project may hold its own copy of the type. Unknown channel or unregistered type:
  `ArgumentException` naming what the mod's server side must register.
- `IReadOnlyList<string> ChatLines()`: `Packet_ChatLine.Message` of every chat packet
  (`SendMessage`, group broadcasts, join announcements), oldest first.
- `void Clear()`: forgets everything, undecoded packets included.

`HighlightedBlock(BlockPos Pos, int Color)` and `SpawnedParticles(ProviderClassName,
Provider, Position, Velocity, Quantity, Color)` are records; both expose `Rgba`, the
color decoded with the layout their packet kind renders with (next section).

Captures are synchronous with the send: a server call followed by a read on the same
tick observes the packet, as long as the engine sends it at all. Particles are the
notable gate: `ServerMain.SpawnParticles` only sends to playing clients that were already
sent the chunk at the spawn position (`DidSendChunk`), and chunk streaming to a fresh
player settles over the ticks after `JoinPlayer` returns, so a scenario spawns once per
tick inside `World.Until` until one lands (the E2E test shows the pattern).

## Color conventions

The two effect systems do not read the packed int the same way, and the difference is in
the renderers, not the packets:

- Highlights: `BlockHighlight` hands each color int to `ModelCubeUtilExt.AddFaceSkipTex`,
  which calls `MeshData.AddVertexSkipTex(x, y, z, color)`; that writes the int verbatim
  over the four RGBA vertex bytes (`((int*)rgba)[vertex] = color`), and the
  `blockhighlights.vsh` shader uses `vertexColor` as is. Little-endian, so the lowest
  byte is red: the `ColorUtil.ColorFromRgba(r, g, b, a)` layout. The engine's own default
  highlight color confirms it: `ToRgba(96, bg[2], bg[1], bg[0])` deliberately swaps the
  channels of its RGB array to land red in the lowest byte.
- Particles: `ParticlePoolQuads` unpacks `ColorRed = (byte)color`, `ColorGreen = >> 8`,
  `ColorBlue = >> 16`, and `ParticleGeneric.UpdateBuffers` uploads them in the order
  (ColorBlue, ColorGreen, ColorRed, alpha) as the `rgbaBlockIn` attribute the shader
  multiplies the fragment by. So the byte the shader renders as red is bits 16 to 23:
  the `ColorUtil.ToRgba(a, r, g, b)` layout. Caminus's server code (`ColorFromRgba` for
  highlights, `ToRgba` for particles) is the correct pairing.

Atlas exposes both forms: `Color` is the raw int exactly as the sender passed it (so a
scenario that computed its color with `ColorUtil` can compare ints), and `Rgba` is the
decoded `(R, G, B, A)` computed with the right layout for that packet kind
(`Rgba.FromRgba` for highlights, `Rgba.FromArgb` for particles). A scenario asserts
`Highlights(7)[0].Rgba.R == 255` without knowing the quirk.

## Clearing rules

- `Clear()`: explicit, forgets everything captured so far.
- `RollbackWorld` restore: observations are cleared. The world state rewound, so
  observations from before the rewind would mislead. Implemented with the same
  `atlas:rollback:restored` event-bus hook a cooperating mod uses to resync its own
  in-memory state, at the same moment (after the SaveGame restore, before any chunk
  column reload); what the engine re-sends after the restore is captured normally.
  Players joined before the snapshot survive the restore with their `ITestPlayer` and
  its `Client` intact (E2E-verified).
- `FreshWorld` recycle: a new host, so `JoinPlayer` returns new players with empty
  observations; nothing carries over by construction.
- A kicked player's observations stay readable (what it received before the kick).

## What a mod must expose to be testable

- Stable highlight slot ids: a `public const int` per slot (Caminus's slot 7), so the
  scenario reads `Highlights(CaminusMod.OverlaySlot)` rather than a magic number.
- Channel and message-type names: the channel name string and the message class as
  public symbols (`"caminus"`, `OverlayPacket` with `[ProtoContract]`/`[ProtoMember]`),
  registered on the server side in `StartServerSide` with `RegisterChannel` then
  `RegisterMessageType<T>()`. The scenario project references the mod project for the
  type; matching is by full name, so the ModLoader's own copy of the dll is fine.
- Colors computed with the layout the effect renders with (`ColorFromRgba` for
  highlights, `ToRgba(a, r, g, b)` for particles); `Rgba` then reads back the intended
  channels.
- Particles spawned through `SimpleParticleProperties` (the `World.SpawnParticles`
  overloads) expose position, velocity, quantity and color deterministically; a custom
  provider is still captured and rebuilt, with its own values.
- TCP channels only: a UDP channel (`RegisterUdpChannel`) is not captured.

## Validation

- `tests/Atlas.Pure.Tests/Player/ClientObservationsTests.cs`: the decoders over bytes
  built with the engine's own packers and serializer (highlight color pairing rules,
  empty highlight, simple and custom particle providers, channel/type resolution and
  its diagnostics, both color layouts, a full `Packet_Server` round trip), plus the
  `EngineCompat` internal-field resolver over fake shapes.
- `tests/Atlas.Engine.Tests/ClientObservationTests.cs` (6 scenarios, one host each):
  highlights per slot with per-position and single colors and the empty-clears rule;
  particles in a streamed chunk; mod-channel packets sent on join and on command by
  `tests/ClientCaptureFixtureMod` (channel `atlasfixture`, one protobuf message,
  registered exactly like Caminus's), the unknown-channel and unregistered-type
  diagnostics; chat lines and `Clear()`; clearing on a rollback restore.
- Runs: the new E2E scenarios three times, the full engine suite and the samples on
  1.22.0, and the engine suite rebuilt and run on 1.21.7 (tallies in the PR).
