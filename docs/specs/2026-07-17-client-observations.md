# Client observations: what the server sends a test player, without a client

Date: 2026-07-17
Status: implemented: `ITestPlayer.Client` shipped with decoders for block highlights,
particles, mod-channel packets and chat lines; extended in 0.16 with entity arrivals, player
world data and player-group listings, and a per-pass drain that dates every packet
Tracks: issue #100 "Client-side testing" (tier 2 of three), from a consumer mod's field request
(VS 1.22.7, Atlas 0.11.0) and a same-day request on Discord (Artalus)
Game versions verified: 1.22.0 as the reference (decompiled and run live), 1.22.7 and
1.21.7 (decompiled; 1.21.7 also run live, it is the CI floor lane)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[world snapshot/rollback](2026-07-06-world-snapshot-rollback.md),
[pre-1.22 compatibility](2026-07-12-pre-122-compat.md)
Sibling: [client-side testing](2026-07-17-client-side-testing.md), the headless-client
feasibility spike (tier 1), written separately

Update 2026-10 (0.16): the surface grew by three kinds, `EntityArrivals()` and
`HasReceivedEntity(entityId)` (packets 33, 34 and 40), `PlayerData()` and
`HasReceivedPlayerData(uid)` (41), `GroupListings()` (49) and `GroupUpdates()` (50), each record
carrying the `Tick` it arrived at and a `Sequence`; `IWorldSession.CurrentTick` is the unit of
that `Tick`. The drain moved from "on a read" to "on every pass": a tick listener per joined test
player takes the packets out of the receive buffer and stamps them, and the decoding stays on the
read. Two sections below carry this: [The per-pass drain](#the-per-pass-drain-016) and
[Entity paths](#entity-paths-016). Everything else in this document describes the 0.12 design
and still holds, except where those sections say otherwise. The driving request is a consumer
suite (issues #172 and #173) that has to prove an observer never receives a hidden player's
entity, and read the player-groups listing, without reading the socket's buffer by reflection.

Update 2026-10 (0.16.0-rc.2): `EntityDepartures()` (packet 36, one `ReceivedEntityDeparture` per
entity of every despawn packet, with the engine's reason when the packet carries one) and
`KnowsEntity(entityId)` (the entity arrived and has not departed since, in the order the player
received the packets; it belongs to no capture window, so `Clear()` and a rollback restore apply
the arrivals and departures they drop to it and leave it otherwise alone) joined the surface, and
`UnreadPackets` / `UnreadBytes` count what the drain parked and no read has decoded yet. See
[Entity departures](#entity-departures-016).

Update 2026-09: `ITestPlayer.Say(message)` added, closing the gap that consumer mod's 0.12.0-rc.1
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

A thermal-overlay mod's server side reacts to a player's thermal state by calling
`sapi.World.HighlightBlocks(player, slot 7, positions, colors)`,
`sapi.World.SpawnParticles(...)`, and by sending a protobuf `OverlayPacket` over its own
mod network channel; its client side renders all three. Atlas embeds a server only,
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
`ReadMessage()` on the game thread, and decode each buffer with
`Packet_ServerSerializer.DeserializeBuffer`, the engine's own serializer. Since 0.16 the drain
runs on every server pass and the decode on a read (see
[The per-pass drain](#the-per-pass-drain-016)); in 0.12 to 0.15 both ran on a read. No
subclassing, no interception on the sending thread, no lock of Atlas's own: the engine's
lock is the only cross-thread handoff, and the drain doubles as the buffer's first
consumer. The UDP side (`DummyUdpNetServer`, entity positions and UDP mod channels) is
not tapped.

Packets are dispatched on which sub-message they carry (`HighlightBlocks`,
`SpawnParticles`, `CustomPacket`, `Chatline`, and since 0.16 `Entity`, `EntitySpawn`,
`Entities`, `PlayerData`, `PlayerGroups`, `PlayerGroup`), not on `Packet_Server.Id`: the ids are
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
| `Packet_ChatLine` (`Message`, `Groupid`, `ChatType`) | chat line payload, decoded by `IClientObservations.Chat()` | same | same | same |
| `EnumChatType` member order | cast directly from `Packet_ChatLine.ChatType` (`int`), no `EngineCompat` indirection | same | same | same |
| `MeshData.AddVertexSkipTex` writes the highlight color int verbatim into the RGBA vertex bytes | red in the lowest byte | same | same | same |
| `ParticlePoolQuads` unpacks `ColorRed = (byte)color`, `ParticleGeneric.UpdateBuffers` uploads (B, G, R, A), shader reads it as `rgbaBlockIn` | red in bits 16 to 23 | same | same | same |

The two internal fields are the only reflective touchpoints; `EngineCompat` resolves them
once per process and `ValidateAtBoot` fails fast with the game version and the missing or
retyped symbol named. Everything else is a compile-time binding to members that exist
unchanged on every supported version (the single-binary source rule of the pre-1.22 spec).

### Symbols added in 0.16, verified on 1.20.12, 1.21.7, 1.22.3 and 1.22.7

| Symbol | Role | 1.20.12 | 1.21.7 | 1.22.3 | 1.22.7 |
|---|---|---|---|---|---|
| `Packet_Server.Id` for `Entity` / `EntitySpawn` / `Entities` | the three entity paths (`EntityArrivalPath`) | 33 / 34 / 40 | same | same | same |
| `Packet_Entity` (`EntityId`, `EntityType`), `Packet_EntitySpawn` (`Entity`, `EntityCount`), `Packet_Entities` (`Entities`, `EntitiesCount`) | entity headers; the arrays are sized by the engine's growth, so only the first `Count` entries are real and the tail is null | same | same | same | same |
| `Packet_Server.Id` for `EntityDespawn` | an entity the client is told is gone (rc.2) | 36 | same | same | same |
| `Packet_EntityDespawn` (`EntityId`, `EntityIdCount`, `DespawnReason`, `DespawnReasonCount`) and `EnumDespawnReason` member order | parallel arrays, one entry per entity, sized by the engine's growth; the reason is cast from the packet's `int` (rc.2) | same | same | same | same |
| `Packet_Server.Id` for `PlayerData` / `PlayerGroups` / `PlayerGroup` | world data, full groups listing, single group | 41 / 49 / 50 | same | same | same |
| `Packet_PlayerData` (`PlayerUID`, `PlayerName`, `EntityId`, `ClientId`, `GameMode`) | identity block; `ClientId == -99` is the departure broadcast (`ServerMain`, 1.21.7 and 1.22.7) | same | same | same | same |
| `Packet_PlayerGroups` (`Groups`, `GroupsCount`), `Packet_PlayerGroup` (`Uid`, `Name`, `Owneruid`, `Membership`) | group listing and single group | same | same | same | same |
| `EnumGameMode`, `EnumPlayerGroupMemberShip` member order | cast directly from the packet's `int`, like `EnumChatType` | same | same | same | same |
| `IEventAPI.RegisterGameTickListener(Action<float>, Action<Exception>, int, int)` | the per-pass listener, registered with an error handler | same | same | same | same |
| `IEventAPI.UnregisterGameTickListener(long)` | removing it | same | same | same | same |
| `IEventAPI.UnregisterEventBusListener(EventBusListenerDelegate)` | removing the restored-world hook | absent | absent | present | present |

The packet classes are public fields compiled against, identical across the four decompiled
or run installs, so nothing here needs a reflective shape probe; `EngineContractTests` pins each
field, both enums' member order and the tick listener overloads on every install the pure
suite is pointed at. The one member that is not there on every version,
`UnregisterEventBusListener`, is looked up on the loaded engine and skipped when absent (a direct
call would die with a `MissingMethodException` on the 1.21 floor). On those versions the
restored-world hook of a player who left stays registered, inert, until the host is disposed.

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
- `IReadOnlyList<ReceivedEntity> EntityArrivals()` and `bool HasReceivedEntity(long
  entityId)` (0.16): one record per entity entry of every packet 33, 34 and 40, oldest first,
  duplicates kept; `HasReceivedEntity` is the union of the three paths. See
  [Entity paths](#entity-paths-016).
- `IReadOnlyList<ReceivedEntityDeparture> EntityDepartures()` and `bool KnowsEntity(long
  entityId)` (0.16.0-rc.2): one record per entity of every packet 36, and whether the last thing
  the client was told about an entity is an arrival. See [Entity departures](#entity-departures-016).
- `int UnreadPackets` and `long UnreadBytes` (0.16.0-rc.2): the number and the serialized size of
  the packets the drain parked and no read has decoded, zero after a read, `Clear()` or a restore.
  Reading them neither drains nor decodes nor allocates.
- `IReadOnlyList<ReceivedPlayerData> PlayerData()` and `bool HasReceivedPlayerData(string
  playerUid)` (0.16): every packet 41, with `IsDeparture` for the engine's `ClientId == -99`
  "player left" broadcast and `IsSelf` by uid (the engine sends a player ForOtherPlayers-shaped
  data about itself too, so the packet's shape cannot tell). Inventories, privileges and the
  rest of the body are not kept. `HasReceivedPlayerData` excludes departures.
- `IReadOnlyList<ReceivedGroupListing> GroupListings()` and `IReadOnlyList<ReceivedGroupUpdate>
  GroupUpdates()` (0.16): packet 49, the full list a client replaces its groups with (so only a
  listing proves a group was dropped), and packet 50, one group a client adds or replaces. A
  listing is sent on every join, so the first one is a free positive control. Chat history is not
  kept.
- `void Clear()`: forgets everything, undecoded packets included.

`HighlightedBlock(BlockPos Pos, int Color)` and `SpawnedParticles(ProviderClassName,
Provider, Position, Velocity, Quantity, Color)` are records; both expose `Rgba`, the
color decoded with the layout their packet kind renders with (next section).

The 0.16 records carry `Tick` and `Sequence`, defined in the next section. The entity type is
the short form `Entity.Code.ToShortString()` reads (the `game:` domain left out, other domains
kept; a `game:` prefix on the wire is dropped too, so the value does not depend on the form the
sender used). Records decoded from one packet share its `Tick` and `Sequence` and keep the
packet's own order. `ReceivedGroupListing.Groups` is a read-only wrapper, not a writable array.

Captures are synchronous with the send: a server call followed by a read on the same
tick observes the packet, as long as the engine sends it at all. Particles are the
notable gate: `ServerMain.SpawnParticles` only sends to playing clients that were already
sent the chunk at the spawn position (`DidSendChunk`), and chunk streaming to a fresh
player settles over the ticks after `JoinPlayer` returns, so a scenario spawns once per
tick inside `World.Until` until one lands (the E2E test shows the pattern).

## The per-pass drain (0.16)

### Why

Before 0.16 the receive buffer was drained when a scenario read the surface, so any stamp a
record carried would have been the tick of the first read after the send, not of the send: the
measured case is a join, where every packet sent during a second player's join carried the one
tick of the first player's next read. #172 needs a tick that orders arrivals against a positive
control, because an absence assertion ("the observer never receives this entity until the
player unvanishes") is only worth anything next to a control that proves the observer was
listening when the entity would have arrived.

### What

Each joined test player registers one game-tick listener
(`RegisterGameTickListener(OnPass, OnPassError, 1)`, the error-handler overload, which exists on
1.20.12, 1.21.7, 1.22.3 and 1.22.7), at the end of `JoinPlayer`. The harness's own tick
listener (`Atlas.Bridge`, registered at boot) is earlier in the list, so within a pass the
harness count has already advanced when this one stamps.

The listener only dequeues: `while (ReadMessage() != null)`, reading the message's packet id off
its first bytes, dropping the message unless its kind is one the drain decodes (see "What the
listener keeps"), and parking the bytes of the others with the pass's tick and the next value of
a per-player sequence counter. No deserialization, no logging, nothing but the list's own growth
allocated (the engine's `ReadMessage` allocates the message wrapper, as it always did). A read parks what is still in the engine queue with the tick of the
read, then decodes the whole parked list in order. `Clear()` drops the parked bytes without
decoding them. The sequence counter is never reset, so it stays monotonic for the player's
lifetime across `Clear()` and a rollback restore.

### What `Tick` means

`Tick` is the value of `IWorldSession.CurrentTick` during the pass whose drain found the packet
in the receive buffer, or at the read when a read found it first. It is an arrival stamp, exact
to one pass, not a send stamp: a packet sent at tick T carries T or T + 1.

- T + 1: the scenario sent it between two passes (a scenario continuation runs after the pass's
  listeners, so the next pass is the first to look), or a listener registered after Atlas's sent
  it, or the main-thread task queue (`ProcessMain`, which runs after the pass's listeners) sent
  it.
- T: a listener registered before Atlas's sent it (the harness's own listener included, which is
  where `Until` predicates run), or the scenario read before the next pass.

`Tick` never decreases as `Sequence` grows. `Sequence` is the total order across every kind of
packet and is what to order by; `Tick` is for windows. `Tick` restarts at 0 on a new host
(`RestartWorld`, `FreshWorld`, or a rollback that degraded to a full recycle). No arrival tick
reaches the four older kinds (highlights, particles, mod-channel packets, chat), which keep
their shape.

### Why the decode stays on the read

Three variants were measured on 1.21.7 and 1.22.3, for one to three players, on a superflat and
a standard world, in quiet, streaming and join windows: A, today's behavior (no listener); B,
decode on every pass; C, dequeue and stamp on every pass and decode on the read.

- C costs 0.3 to 1.2 microseconds per pass summed over one to three players and allocates 0.01
  to 0.21 KB per pass, against a baseline of 8.6 to 170 KB per pass (under one percent). It had
  no pass at or above 0.5 ms in 79 thousand warm passes, and the median and p95 of `BusyTime`
  are those of A. The paired differences between C and A are noise (mean busy time between -0.04
  and +0.03 ms, allocation between -0.7 and +8.6 KB, mixed signs).
- B moves `AllocatedBytes` by +15 to +40 percent in chunk-heavy windows (+19 and +29 KB per pass
  in a standard streaming window), adds 1 to 11 ms passes to 0.01 to 0.26 percent of passes, and
  spikes 7 to 13 ms on the first join drain. That is a baseline shift for every suite that has a
  joined player.
- A throw inside a tick listener aborts the rest of that engine pass's listeners, and a
  persistent throw repeats on every pass (1,240 to 1,345 passes in 58 ms when simulated). B puts
  every decode bug there, and the `BlockCubeParticles` bug fixed in the same release shows such
  bugs exist. C's listener only dequeues, so a decode error stays on the scenario's read, where
  it surfaced before.
- Draining only on a read would make `Tick` mean "first read after the send", the thing this
  replaces.

The costs of C: the raw bytes of the decoded kinds are retained until a read or `Clear()`, no
cap (C as first shipped retained every kind, as the engine's own queue did before: about 2 MB
for a join, 5 to 6 MB per 300 passes with three players streaming; "What the listener keeps"
below drops what is never decoded), so a long streaming scenario that never reads should call
`Clear()`; the decode lands on the read (0 to 5 ms after a 300-pass window); and a side reader of
the same buffer goes blind.

### What the listener keeps

Issue #185: a joined player that never reads, with moving entities nearby, held the server's whole
outbound stream for the length of the scenario. Measured on 1.22.3 and 1.21.7 with one player and
100 hens around the spawn, 3000 passes (about 100 s) and no read, the retained size taken as the
heap difference of a forced full collection before and after emptying each queue:

| | UDP queue | parked TCP messages | parked bytes | retained |
| --- | --- | --- | --- | --- |
| 1.22.3 before | 2996 packets | 3480 | 3.6 MB | 17.7 MB UDP, 3.7 MB parked |
| 1.22.3 after | 0 | 2795 | 0.5 MB | 0 MB UDP, 0.7 MB parked |
| 1.21.7 before | 2997 packets | 2602 | 3.5 MB | 13.2 MB UDP, 3.5 MB parked |
| 1.21.7 after | 0 | 1913 | 0.5 MB | 0 MB UDP, 0.6 MB parked |

The UDP queue is the shared `DummyUdpNetServer`'s client receive buffer (`UdpSockets[0]`), one
`Packet_UdpPacket` per pass here (entity positions), about 4 to 6 KB retained each. Its only
reader in the engine is `DummyUdpNetClient`, the real client's UDP reader, which a headless host
does not have, and Atlas reads nothing from it (UDP mod channels are not observed). `SharedUdpDrain`
empties it on every pass, under the engine's own `ClientReceiveBufferLock` (the lock
`SendToClient` takes to enqueue). One tick listener per host, registered at boot (not per
player: the UDP server is one instance for every player), registered on the server's own event
manager, so it goes with the server and there is nothing to unregister. The three engine fields
(`DummyUdpNetServer.network`, `DummyNetwork.ClientReceiveBuffer` and `ClientReceiveBufferLock`) are
resolved and validated at boot by `EngineCompat` and pinned on every install by
`EngineContractTests`. It costs about 0.5 microsecond per pass and allocates nothing.

On TCP the listener drops, at dequeue time, every message whose `Packet_Server.Id` is not one of
the eleven the drain decodes: 8 (chat line), 33 (entity), 34 (entity spawn), 36 (entity despawn,
rc.2), 40 (entity list), 41
(player data), 49 (player groups), 50 (player group), 52 (block highlight), 55 (mod-channel
custom packet) and 61 (particles). Decompiling the send sites of 1.21.7, 1.22.3 and 1.22.7 shows
the engine pairs each of those ids with that sub-message and with no other, so keeping by id keeps
exactly what the sub-message dispatch of `Apply` decodes. Reading the id does not need a
deserialization: every `Packet_Server` goes through `Packet_ServerSerializer.Serialize`, which
writes `Id` first, as field 90 with wire type 0 (the key is the varint 720, the bytes `D0 05`)
followed by the id as a varint, and leaves it out only when it is 1, the server identification.
The dummy connection carries those bytes as they are (no length prefix, never compressed for a
singleplayer-type client). `ServerPacketId.TryRead` reads three bytes for every id the engine
sends. A message that does not start with the key, the identification packet once per join, is
parked like a decoded kind, so the read decodes it and a failure is reported as before: the rule
never guesses. A dropped message still takes a sequence number, so `Sequence` is the position in
the total order the player received everything in, as documented. The layout is pinned by pure
tests over the engine's own serializer and, per install, by `EngineContractTests`.

Reading the id adds about 0.05 microsecond per message: the per-pass listener measured 0.17 to
0.21 microsecond per player and pass before (mean of 4500 samples, three players, 100 hens) and
0.23 to 0.25 after, and the `MeasureTicks` window means over three runs of five 300-pass windows
were 0.011 to 0.018 ms before and 0.009 to 0.018 ms after, which is noise at its millisecond
resolution.

What a scenario that never reads still accumulates is the decoded kinds. On vanilla that is mostly
mod-channel packets of the game's own channels (`EntityAnims/BulkAnimationPacket` about 0.6 per
pass and `remoteplayertracker/PacketPlayerPosition` about 0.3 per pass here, about 145 bytes per
message on the wire), about 0.4 MB retained per minute with 100 hens nearby. Atlas keeps every channel because
`Packets<T>(channel)` can ask for any registered one, and a retention rule per channel would be a
guess about which ones a scenario may ask for. Such a scenario calls `Clear()` now and then.

### Decode errors

A packet that fails to decode must not hide the ones behind it. The decode loop catches per
message, keeps the first exception, finishes the loop, then throws one
`InvalidOperationException` that names the packet id, its `Tick` and its `Sequence` and carries
the original as `InnerException`. The failed packet is dropped, so the next read succeeds; what
was decoded before and after it was kept. An exception that escapes the listener itself reaches
its error handler, which stores the first one; the next read throws it the same way and clears it.
`Clear()` never decodes and never throws, and forgets a stored error.

### `MeasureTicks`

The listener runs inside the server pass, so it is inside a `MeasureTicks` window and is not
excluded: a stopwatch around it would cost more than the microsecond it measures. The
`MeasureTicks` remarks and the Measuring Server Cost page say so, and that a `Client` read made
inside the window (in an `Until` predicate, for example) decodes on the game thread and counts in
`AllocatedBytes`, as it already did. The engine test
`Drain_Should_AllocateLittleAndLeaveNoListenerBehind_...` guards the allocation side: on a quiet
one-player superflat world a direct call of the listener allocates under 1 KB per pass over 300
passes (timing is not asserted).

### Lifetime

The listener and the restored-world hook are removed by `ClientObservations.Detach()`, which
parks once more first (so what a kicked player received is still readable) and is idempotent. It
is called from the one point where a player is verifiably gone, the `onRemoved` callback of
`KickedPlayerCleanup` that `JoinPlayer` arms. Every removal path reaches it, because each runs
the engine's own `DisconnectPlayer` on the game thread, which raises `PlayerDisconnect`: a kick
(`IServerPlayer.Disconnect`), a client closing (`Packet_Client` 14, `Leave`, handled by
`ServerMain.HandleLeave`), and a rollback restore removing a player who joined after the
capture (`RemovePostCapturePlayers`). A removal that lands before the `TestPlayer` exists (a mod
kicking from its `PlayerJoin` handler) is remembered and the new object detaches itself.

Measured on a live host: thirty join and kick cycles, a kick, a client leave and a rollback
removal leave the engine's tick-listener count at its baseline; without `Detach` each cycle left
one listener behind, and every dead player's listener kept firing every pass.

`UnregisterEventBusListener` only exists from 1.22 on, so on 1.21.x the restored-world hook
cannot be unregistered: it stays in the engine's list, inert (it checks a flag), until the host
is disposed. A player the restore itself removes is cleared by it first, like every other
player: `RemovePostCapturePlayers` runs before the hook fires, and the player's `Detach` follows
on a later tick.

### What breaks, on purpose

A side reader of the dummy socket's receive buffer, by reflection, now sees nothing: the buffer
is emptied on every pass. Before, such a reader worked only if it read first and `player.Client`
last, and went blind the moment the order slipped; now it is blind every time, so a positive
control on the same reader fails at once, where an absence assertion on it used to pass for the
wrong reason depending on the order of two reads. The 0.15.1 documentation of the ordering
workaround is removed with it. The two new members of `IClientObservations` and the one of
`IWorldSession` are breaking for a consumer that implements either interface, which the package
does not intend anyone to do.

## Entity paths (0.16)

Where an entity reaches a client, measured on 1.20.12, 1.21.7, 1.22.3 and 1.22.7 (decompiled
and run live with up to seven observers). `EntityArrivalPath` carries the packet id.

| Path | Packet | What | Version notes |
|---|---|---|---|
| `TrackedRange` | 33, one entity | An existing entity entering the client's tracked range (`PhysicsManager.SendTrackedEntitiesStateChanges`), the observer's own entity included. For a player entity it is preceded by that player's packet 41. Built every 0.2 s of accumulated time and gated by the client having been sent its chunk | Same on every version. Arrives 1 to 6 passes after a spawn and 7 to 31 passes after a return into range |
| `Spawn` | 34, a batch | Fresh spawns queued per client (`SendEntitySpawns`, on the engine's physics helper thread, so not synchronous with the tick), and a single-entity priority spawn on the game thread. For a player entity it is followed by that player's packet 41 | Reaches only the first `ceil(n / 3)` of `n` clients: a vanilla loop bound in `PrepareEntitySpawns` (`j < array.Length && j < count; j += 3`, 1.20.12, 1.21.7 and 1.22.7). The others get the same entity as 33 |
| `JoinList` | 40, a list | On 1.22.x: the joining player's own entity, sent to him (`SendPlayerEntity`). On 1.21.x and 1.20.x: the entity of every connected player to the joiner (`SendPlayerEntities`), and an existing player's entity re-sent to every other client when someone joins (`SendInitialPlayerDataForOthers`) | Differs by version: on 1.21.x and 1.20.x it is a path by which a third party's entity reaches an observer without any range or spawn event |

No entity travels in a chunk packet on 1.21.x and 1.22.x (`ServerChunk.ToPacket` has a
`withEntities` flag that it never reads). On 1.20.12 the flag is read and a chunk packet can
carry entities, but none was seen live; Atlas does not decode them.

What follows from the map, and is documented where a consumer reads it (the XML docs of
`IClientObservations` and the Client-Side Testing wiki page):

- Assert on the union (`HasReceivedEntity`), never on `Path`: which path serves which client is
  the engine's business, and packet 34 skips two thirds of the clients.
- Duplicates are real: 33 and 34 in the same pass, and the own entity twice (40 at the join, 33
  a few passes later).
- The order between a player's packet 41 and his entity differs by path (before on 33, after on
  34), so `Sequence` between a `ReceivedPlayerData` and a `ReceivedEntity` is not a contract.
- An absence assertion needs a wait at least as long as the slowest measured arrival (31
  passes), a positive control that arrives after the condition lifts, the union of the paths, and
  must not be made right after a rollback: a restore clears the stores, the server sends the
  restored player its own player data and does not send the entities or the group listings again
  (measured on 1.22.3, consistent on 1.21.7), so "never received" holds for the wrong reason.
- Those times are measured at the default pacing (about 33 ms a pass); the 0.2 s cadence of path
  33 is a constant of `PhysicsManager`, the same on the versions checked.

Player groups: packet 49 is sent on a join, a leave, a kick, a disband and a removal; packet 50
on a create, an invitation, an acceptance and a rename. Live, `/group create x` sends one 50 and
no 49, and `/group leave x` sends a 49 without the group. Group ids share the number space of
`ReceivedChatLine.GroupId`.

## Entity departures (0.16)

How a client is told an entity is gone, from the decompiled engine (1.21.7 and 1.22.7, the send
sites are the same) and measured live on 1.21.7 and 1.22.3 by `ClientDepartureObservationTests`.
Packet 36 (`Packet_EntityDespawn`: `EntityId[]`, `DespawnReason[]`, `DeathDamageSource[]`, parallel
arrays) is sent from two places:

| Sender | When | Reason on the wire |
|---|---|---|
| `ServerSystemEntitySimulation.SendEntityDespawns` | An entity despawned (`ServerMain.DespawnEntity`: death, expiry, pick-up, removal, chunk unload, a player disconnecting), once per client that tracks it | The entity's own `DespawnReason`, `Death` when it has none: the reason `DespawnEntity` is called with is not what is sent. The packet goes to every client whenever the queue is not empty, an empty one (no ids, 6 bytes) for a client that tracks none of the entities |
| `PhysicsManager.SendTrackedEntitiesStateChanges` | The tracking pass finds an id the client tracked that is no longer in its range, or no longer loaded | `OutOfRange` |

Measured: a despawn is usually reported twice to a tracking client (the queue's, 1 to 4 passes
after, then the tracking pass's `OutOfRange`, 3 to 7 passes after, because the despawned id is still
in the client's tracked set), sometimes only by the tracking pass; the reason of the first record
reads `Death` for a despawn asked for with another reason. An entity moving out of a client's range
is reported as `OutOfRange` in the same pass. A dimension change sends nothing: the engine tracks
by coordinates. A fork that hides an entity from one client mid-session sends the same packet.

The one other way a real client loses an entity is not a departure: when a client moves far from
entities, the server unloads that client's chunks (`ServerSystemUnloadChunks.SendOutOfRangeChunkUnloads`),
forgets the ids of the entities in them (`client.TrackedEntities.Remove`) and sends packet 11, and the
client drops the entities of those chunks (`SystemUnloadChunks.UnloadChunk`). No packet 36 follows:
measured with the entity 140 blocks from the observer it is reported, at 170 blocks and more it is not
(1.21.7 and 1.22.3). Atlas does not decode chunk packets, so `KnowsEntity` keeps such an entity.

A rollback restore empties the stores (`Clear()`), then the server despawns the entities the restore
removed a few passes later, so departures can have no arrival to pair with. `KnowsEntity` is kept across
`Clear()` and the restore for that reason: it applies the entity packets parked at the clear and
leaves the rest. The player-data packet 41 with `ClientId == -99` removes the player from the client's
player list, not its entity (`GeneralPacketHandler.HandlePlayerData`).

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
  the `ColorUtil.ToRgba(a, r, g, b)` layout. That mod's server code (`ColorFromRgba` for
  highlights, `ToRgba` for particles) is the correct pairing.

Atlas exposes both forms: `Color` is the raw int exactly as the sender passed it (so a
scenario that computed its color with `ColorUtil` can compare ints), and `Rgba` is the
decoded `(R, G, B, A)` computed with the right layout for that packet kind
(`Rgba.FromRgba` for highlights, `Rgba.FromArgb` for particles). A scenario asserts
`Highlights(7)[0].Rgba.R == 255` without knowing the quirk.

## Clearing rules

- `Clear()`: explicit, forgets everything captured so far.
- `RollbackWorld` restore: observations are cleared, every store of the 0.16 kinds included,
  parked undecoded packets and a stored listener error too. The world state rewound, so
  observations from before the rewind would mislead. Implemented with the same
  `atlas:rollback:restored` event-bus hook a cooperating mod uses to resync its own
  in-memory state, at the same moment (after the SaveGame restore, before any chunk
  column reload); what the engine re-sends after the restore is captured normally, and
  measured on 1.22.3 that is the restored player's own packet 41 and nothing else (no entity, no
  groups listing; the 1.21.7 run of the same scenario is consistent). Players joined before the snapshot survive the restore with their `ITestPlayer` and
  its `Client` intact (E2E-verified). `Sequence` keeps counting across it.
- `FreshWorld` recycle: a new host, so `JoinPlayer` returns new players with empty
  observations; nothing carries over by construction.
- A player that left or was kicked keeps what it received, readable and frozen: its listeners
  are detached, and a later restore no longer clears it. A player the restore itself removes is
  cleared by that restore, like the others, and frozen after.

## What a mod must expose to be testable

- Stable highlight slot ids: a `public const int` per slot (that mod's slot 7), so the
  scenario reads `Highlights(ConsumerMod.OverlaySlot)` rather than a magic number.
- Channel and message-type names: the channel name string and the message class as
  public symbols (the channel name string, `OverlayPacket` with `[ProtoContract]`/`[ProtoMember]`),
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
  registered exactly like a shipping mod's), the unknown-channel and unregistered-type
  diagnostics; chat lines and `Clear()`; clearing on a rollback restore; `Say` running
  the fixture's privileged command through the real chat path and observing both its
  reply (`ChatLines()`) and its channel packet (`Packets<T>`); `Say` with a plain line
  and the engine's own echo back to the sender.
- 0.16 additions. `tests/Atlas.Pure.Tests/Player/ClientEntityDecodersTests.cs`: the entity,
  player-data and group decoders over bytes from the engine's own serializer (null tails of the
  engine's growing arrays, the short type form with and without the `game:` domain, the
  departure marker, the own-uid flag, empty strings for missing fields, a listing that is not a
  writable array). `ClientObservationsDrainTests.cs`: the park and decode order against a
  substitute server API and a queue standing in for the dummy connection (stamps and sequence
  across passes and reads, the tick never decreasing as the sequence grows, one sequence across
  every kind, an empty message, a failed decode surfaced once with the rest kept and the count
  of failures named, a stored listener error thrown once, `Clear()` dropping parked bytes
  without decoding them, the restored hook, `Detach` parking once more, unregistering and
  staying idempotent). `EngineContractTests`: every engine field the decoders read, both
  enums' member order and the tick listener overloads, per install.
  `tests/Atlas.Engine.Tests/ClientEntityObservationTests.cs` (10 scenarios, one host each): a
  spawn reaching four observers (union of the paths, stamps within the scenario's ticks); a
  joining player seen by the others and his departure marker; absent while far (with a positive
  control, after 60 passes), present after a teleport into range; `Clear`; a rollback restore;
  `/group create` and `/group leave` for packets 50 and 49; the arrival tick of a scenario send;
  a packet that cannot be decoded; block cube particles; the listener's allocation and lifetime
  across a kick, a client leave, a rollback removal and thirty join and kick cycles.
- Queue growth (#185). `tests/Atlas.Pure.Tests/Player/ServerPacketIdTests.cs`: the id read off
  bytes from the engine's own serializer for every decoded kind and for the others the engine
  sends, multi-byte ids, the identification packet whose id is omitted, truncated and overlong
  bytes, a buffer longer than its message. `SharedUdpDrainTests.cs`: the drain over the engine's
  real dummy UDP server (what it empties, the engine's lock, nothing before the first join, no
  allocation). `ClientObservationsDrainTests.cs`: a dropped kind is never decoded yet still takes
  a sequence number, an unreadable envelope is still reported by the read. `EngineContractTests`:
  the three UDP fields and where `Id` sits on the wire, per install.
  `tests/Atlas.Engine.Tests/ClientQueueGrowthTests.cs` (2 scenarios): 100 hens and 1000 passes
  without a read, then the UDP queue under 50 packets, every parked packet of a decoded kind (by
  the engine's own deserializer), every hen found by the first read with ordered ticks and
  sequences; and one drain listener per host whatever the players do.
- Runs: the new E2E scenarios three times, the full engine suite and the samples on
  1.22.0, and the engine suite rebuilt and run on 1.21.7 (tallies in the PR). The `Say`
  addition repeats that pattern: its two new scenarios three times, the full
  `ClientObservationTests` class, the full engine suite and samples on the default
  install (1.22.3), and the engine suite rebuilt and run on 1.21.7 (tallies in the PR).
