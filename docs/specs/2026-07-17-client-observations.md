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

Update 2026-09: `ITestPlayer.Say(message)` added, closing the gap the Caminus 0.12.0-rc.1
feedback named directly: a scenario running a command through `IWorldSession.ExecuteCommand`
(`IChatCommandApi.ExecuteUnparsed` with a synthetic console caller) gets the command's
*return value*, but any reply the handler routes through the calling player specifically
(`args.Caller.Player.SendMessage`, or the engine's own status-message echo, which targets
`Caller.Player` too) has nowhere to go - the console caller carries no player, so nothing
reaches `Client.ChatLines()`. `Say` sends the same `Packet_Client` a real client's chat box
sends, over the player's own dummy connection, so the server's real chat/command dispatch
runs with `client.Player` set to the real, joined `IServerPlayer` and routes replies back
through that player's own connection - the same path every other `Client` capture taps.
Verified by decompile against 1.21.7, 1.22.3 (the reference install for this pass) and
1.22.7; run live against 1.22.3 and rebuilt-and-run against 1.21.7.

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

## Say: the inbound path

The tap above is one-directional: the server-to-client stream. `ITestPlayer.Say(message)`
is the client-to-server counterpart, sent over the same dummy connection
`DummyClientConnector.Connect`/`RequestJoin`/`SendClientLoadedAndReady` already use for the
join sequence - `DummyClientConnector.Say` builds the exact `Packet_Client` a real client's
chat box builds (`Vintagestory.Client.ClientPackets.Chat(groupid, message)`, decompiled and
byte-identical on all three versions) and sends it with the same `Serialize`/
`connection.TcpClient.Send` pair `RequestJoin` uses:

```
Packet_Client { Id = 4, Chatline = Packet_ChatLine { Message, Groupid = GlobalConstants.GeneralChatGroup } }
```

`GlobalConstants.GeneralChatGroup` (0) is what a real client's chat box sends on, unless the
player switched chat tabs (`HudDialogChat`, client-side UI state a headless test player has
no equivalent of, so there is nothing to switch). `4` is `PacketHandlers[4] =
HandleChatLine` on every supported version, and `HandleChatLine`/`HandleChatMessage`
(`ServerMain`) are byte-identical decompiles on 1.21.7, 1.22.3 and 1.22.7: `message.Trim()`,
clamp `Groupid` to at least `-1`, then either dispatch as a command
(`message.StartsWith('/')` -> `api.commandapi.Execute(cmd, client.Player, groupid, args)`,
the *real* client.Player, not a synthetic caller) or, for a plain line, rate-limit, broadcast
to the group and echo the line back to the sender (`player.SendMessage(groupid, message,
EnumChatType.OwnMessage, data)`) - both landing back in `Client.ChatLines()` through the
same `Packet_Server.Chatline` tap the rest of this doc describes. The command path's own
reply mechanism (`Vintagestory.Common.ChatCommandApi.Execute(string, IServerPlayer, ...)`)
calls `player.SendMessage` directly on the real calling player for both the status message
and any "no such command"/error text, so a handler needs no special test-awareness to be
Say-testable - the same code path a real player's command hits.

This is what `IWorldSession.ExecuteCommand` cannot give a scenario: it calls
`IChatCommandApi.ExecuteUnparsed` with a synthetic `Caller` (`Type = Console`,
`CallerPrivileges = ["*"]`, no `Player`), built to capture the command's *return value*
regardless of privilege - exactly right for asserting `CommandResult`, but a dead end for
any reply the handler (or the engine's own status-message echo) routes through
`Caller.Player` specifically: there is no player behind that caller for it to reach.
`Say` runs the command as the joined player actually is, privileges included - a command
gated behind a role the test player's default role (`suplayer`) lacks needs
`player.Player.SetRole(...)` first, the same escape hatch any other engine-level test setup
uses.

### Inbound engine symbols, verified on 1.21.7, 1.22.3 and 1.22.7

| Symbol | Role | 1.21.7 | 1.22.3 | 1.22.7 |
|---|---|---|---|---|
| `Vintagestory.Client.ClientPackets.Chat(int, string, string)` | the real client's own packet builder | same | same | same |
| `Packet_Client.Id` for `Chatline` / `PacketHandlers[4]` | client dispatch id | 4 | same | same |
| `Packet_ChatLine` (`Message`, `Groupid`, `ChatType`, `Data`) | chat line payload | same | same | same |
| `ServerMain.HandleChatLine` / `HandleChatMessage` | dispatch: trim, clamp group, command or broadcast+echo | byte-identical decompile | byte-identical decompile | byte-identical decompile |
| `Vintagestory.Common.ChatCommandApi.Execute(string, IServerPlayer, int, string, ...)` | real command dispatch, replies via `player.SendMessage` | same | same | same |
| `GlobalConstants.GeneralChatGroup` | public mutable field (not `const`), no reflection needed | `0` | same | same |
| `ServerMain.PreLaunch` -> `ClientPacketParserOffthread.Start` | background thread, `Thread.Sleep(10)` then `PacketParsingLoop()` | same (pre-1.22 exit-check shape only) | same | same |
| `ServerMain.ProcessMain` | drains `ClientPackets` -> `HandleClientPacket_mainthread` every pass, after the pass's game-tick listeners fire | same | same | same |
| `DummyTcpNetServer.network` (internal) / `DummyNetwork.ServerReceiveBuffer` (internal `Queue<object>`) | the raw, unparsed inbound queue `Say` polls to zero | same | same | same |

`IServerPlayer.SetRole` (public API, not reflected) is stable across the same three
versions - checked directly on each install's `VintagestoryAPI.dll`, since it is the
escape hatch a Say test reaches for when a command needs a role the default join lacks.

### The post-send timing guarantee

Sending never blocks: `Say` returns as soon as the bytes are queued on the dummy socket,
then waits for two hops the send itself does not cover before its own `Task` completes, so
a caller reading `Client` right after `await player.Say(...)` sees any reply. Both hops are
confirmed by decompile, but only one of them turned out to be tick-bounded:

1. **Parsing is off the game thread, and its latency is wall-clock bounded, not
   tick-bounded.** `ServerMain.PreLaunch` spawns a dedicated `clientPacketsParser`
   background thread (`ClientPacketParserOffthread.Start`) whenever `ReducedServerThreads`
   is false - Atlas never sets it, so this is always the path Atlas boots. That thread loops
   `Thread.Sleep(10); server.PacketParsingLoop();`, reading every `MainSockets` entry (dummy
   sockets included) and parsing whatever arrived into a `ReceivedClientPacket`, queued on
   the concurrent `ClientPackets` collection - moving it out of the raw, unparsed queue
   (`DummyTcpNetServer.network.ServerReceiveBuffer`, both internal) the send itself enqueued
   into. This is a genuine cross-thread race against the game thread, and `Thread.Sleep(10)`
   is only a nominal floor: under OS scheduling pressure (measured directly - see below) the
   real wait can run well past it, with no tick-count relationship at all, since ticks are
   defined by the SEPARATE game thread's own `Process()` cadence. `Say` therefore polls
   `EngineCompat.PendingInboundCount` down to zero via `TickSource.WaitUntilAsync`, bounded
   by a generous 100-tick (about 3.3s at the default pace) timeout matching the rest of
   Atlas's own uncertain-completion waits (`WaitForJoin`, `WaitForPlaying`), rather than
   guessing a fixed tick count for an OS-scheduling-bound wait.
2. **Dispatch happens once per `Process()` pass, after that pass's game-tick listeners, and
   IS tick-bounded.** `ServerMain.ProcessMain` (called from every `Process()` pass) drains
   `ClientPackets` and calls `HandleClientPacket_mainthread` - which is what actually invokes
   `HandleChatLine` and produces the reply - but only after that same pass's
   `EventManager.TriggerGameTick` (the event `TickSource.RaiseTick` rides, per
   docs/specs/2026-07-14-tick-contract.md) has already fired. So the pass whose `RaiseTick`
   completes a "wait 1 tick" is always ONE pass too early to have dispatched a packet whose
   parsing that same wait just confirmed; it is the FOLLOWING pass's `ProcessMain` that
   dispatches it, and that following pass's own `RaiseTick` is what a "wait 2 ticks" (from
   the moment parsing was confirmed) resolves on. Unlike hop 1, this hop runs entirely on the
   game thread with no cross-thread race, so it genuinely is bounded by tick count regardless
   of system load: `Say` waits a fixed 2 ticks for it (1 is chronologically sufficient at the
   engine's default ~33ms pace, 2 is margin for a slow pass).

Measured: an earlier version of `Say` skipped the hop-1 poll and used one blind
`WaitTicksAsync(2)` for both hops, on the reasoning that one `Process()` pass (~33ms)
comfortably exceeds the parser thread's nominal 10ms poll. That held on an idle machine
(three-for-three, both installs) but was measured flaky under a loaded 130-test sequential
run against the 1.21.7 install specifically (both `Say` scenarios failed with only the
join-time welcome line captured - the packet had not been parsed at all within the 2-tick
window), while the identical run against the default 1.22.3 install passed 130/130: the
same background thread, under enough contention, can miss the ~66ms window a fixed 2-tick
wait allows, and no fixed tick count can bound an arbitrarily-delayed OS thread wake-up.
Re-run after switching hop 1 to the poll above: 128/130 on the loaded 1.21.7 run (the
remaining 2 failures are `StageCommandTests`, a pre-existing, unrelated local-environment
artifact - its "different install" fixture is hardcoded to this exact machine's 1.21.7 path,
so it cannot diverge from itself; CI does not hit it), plus 3/3 more in isolation on the
same install. Because the scheduler drain (where a continuation resumes) always runs
immediately after `Process()` within the same pass (pump order: `Process()`, then the
scheduler drain), and `ProcessMain` (inside `Process()`) always runs before that pass's
drain, a reply produced by the pass that satisfies hop 2's 2-tick wait is already sitting in
the connection's receive buffer by the time `Say`'s continuation - and the caller's, right
after it - runs. No `World.Until`-style polling loop is needed on the caller's side the way
`Particles()` needs one for the chunk-streaming gate: both waits are internal to `Say`.

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
- A command tested through `Say` needs a role the test player actually has: the default
  join role (`suplayer`) carries `chat` but no server-admin privileges, so a command gated
  behind one of those (`RequiresPrivilege`) needs `player.Player.SetRole(...)` first, same
  as any other player would need the role granted to run it.

## Validation

- `tests/Atlas.Pure.Tests/Player/ClientObservationsTests.cs`: the decoders over bytes
  built with the engine's own packers and serializer (highlight color pairing rules,
  empty highlight, simple and custom particle providers, channel/type resolution and
  its diagnostics, both color layouts, a full `Packet_Server` round trip), plus the
  `EngineCompat` internal-field resolver over fake shapes.
- `tests/Atlas.Engine.Tests/ClientObservationTests.cs` (8 scenarios, one host each):
  highlights per slot with per-position and single colors and the empty-clears rule;
  particles in a streamed chunk; mod-channel packets sent on join and on command by
  `tests/ClientCaptureFixtureMod` (channel `atlasfixture`, one protobuf message,
  registered exactly like Caminus's), the unknown-channel and unregistered-type
  diagnostics; chat lines and `Clear()`; clearing on a rollback restore; `Say` running
  the fixture's privileged command through the real chat path and observing both its
  reply (`ChatLines()`) and its channel packet (`Packets<T>`); `Say` with a plain line
  and the engine's own echo back to the sender.
- Runs: the new E2E scenarios three times, the full engine suite and the samples on
  1.22.0, and the engine suite rebuilt and run on 1.21.7 (tallies in the PR). The `Say`
  addition repeats that pattern: its two new scenarios three times, the full
  `ClientObservationTests` class, the full engine suite and samples on the default
  install (1.22.3), and the engine suite rebuilt and run on 1.21.7 (tallies in the PR).
