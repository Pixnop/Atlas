# Client-side testing: headless-client feasibility spike

Date: 2026-07-17
Status: feasibility measured (this pass); nothing implemented, nothing committed beyond this
document
Status 2026-09-04: tier 2 shipped in 0.12.0 (`ITestPlayer.Client`, `ITestPlayer.Say`). Tier 1
stays parked: the request for a supported offline client path
([anegostudios/VintageStory-Issues#10012](https://github.com/anegostudios/VintageStory-Issues/issues/10012))
was closed as not planned and redirected to the vintagestory.at suggestions forum, so the
cheaper "supported offline switch" branch below is off the table. The gating question the
recommendation actually poses, whether a test launcher bypassing the session-key check is
acceptable, has not been put to anyone yet; until the forum route answers it, tier 1 stays
where the "if the answer is no" branch leaves it.
Tracks: issue #100 "Client-side testing: capture what the server sends to a test player, and a
headless-client feasibility spike" (tier 1 and tier 3; tier 2 is a sibling change on the
existing dummy connection)
Game versions probed: 1.22.3 full client install (decompiled and run live), 1.22.7 server-only
install (file layout compared)
Prerequisites: [Atlas design](2026-07-02-atlas-design.md),
[pre-1.22 compat](2026-07-12-pre-122-compat.md) (the decompile-first method)

## The field need

Caminus (VS 1.22.7, Atlas 0.11.0, 36 server scenarios green) cannot exercise its client side:
a mod-channel packet handler (protobuf `OverlayPacket` on channel `caminus`), a `HudElement`
built with `GuiComposer` + `AddDynamicText` (three lines), a hotkey K registered with
`RegisterHotKey` + `SetHotKeyHandler` that sends `/caminus overlay`, and the client-received
effects of `sapi.World.HighlightBlocks(player, slot 7, ...)` and `sapi.World.SpawnParticles(...)`.
Artalus asked the same question on Discord the same day. Three tiers, by value:

1. A test player with a real client, no window and no GPU if possible, joined to the embedded
   server: trigger a hotkey by code, send chat and commands, read open dialogs and their text,
   read received highlights (positions and colors per slot), particles (position, color,
   velocity) and mod-channel packets by type.
2. Without a client: capture what the server sends to a player on the existing dummy
   connection (`player.Client.Highlights(slot)`, `Particles()`, `Packets<T>("caminus")`).
   Roughly 80 percent of the need, no rendering. Being implemented separately.
3. A PNG screenshot of the headless client for agent-driven visual review.

This document answers tier 1 (and tier 3, which follows from it) with evidence: what the
client engine actually needs to boot, whether a stub platform can carry it in-process, whether
the real client can run offscreen as a subprocess, and what each path costs.

## Method

- Decompilation with ILSpy of the full 1.22.3 client install: `Vintagestory.dll` (the entry,
  four lines: `ClientLinux.Main` -> `ClientProgram.Main`), `VintagestoryLib.dll` (both sides:
  `Vintagestory.Client`, `Vintagestory.Client.NoObf`, `Vintagestory.Client.MaxObf`,
  `Vintagestory.ClientNative`, `Vintagestory.Server`), `Lib/OpenTK.Windowing.Desktop.dll`
  (4.9.4, GLFW initialization), `Lib/cairo-sharp.dll` (native resolver); targeted
  decompilation of `VintagestoryAPI.dll` for the mod-facing GUI, hotkey and chat surfaces.
- A grep census of direct `GL.*` (OpenTK) calls per client file, to measure how much rendering
  bypasses `ClientPlatformAbstract`.
- Two live experiments on this machine (Linux, AMD GPU, no Xvfb installed), both offscreen,
  in a throwaway scratch project that is not part of the repository: the real client launched
  as a subprocess on a headless nested compositor with software GL against a real dedicated
  server (path B), and `ScreenManager` driven with a generated stub platform in-process
  (path A). Each is described with its exact outcome below.
- What was not measured: the client on a GitHub runner (sized from the local numbers and the
  runner image contents), the client on Windows or macOS, and the client-side bridge mod
  itself (designed from the decompile, not built).

## Engine facts, decompile-verified on 1.22.3

### The boot path is hard-wired to one concrete platform and one window

`ClientProgram.Start(ClientProgramArgs, string[])` (public class, `Vintagestory.Client`):

| Step | Symbol | Consequence for a headless client |
|---|---|---|
| Platform | `new ClientPlatformWindows(logger)`, declared as the concrete type; `ScreenManager.Platform` is the abstract `ClientPlatformAbstract` but only `ClientProgram` ever assigns it | a stub platform is only reachable by not calling `ClientProgram.Start` at all (path A) |
| Working directory and install | `Environment.CurrentDirectory = dir(Assembly.GetExecutingAssembly().Location)`; `GamePaths.Binaries => AppDomain.CurrentDomain.BaseDirectory`; `CleanInstallCheck.IsCleanInstall()` and the assets check both read `BaseDirectory` | a launcher that is not itself inside the install must set `APP_CONTEXT_BASE_DIRECTORY` to the install (verified: without it the boot stalls on a modal "critical files are missing" GTK message box) |
| Window | `new GameWindowNative(GameWindowSettings, NativeWindowSettings)` (OpenTK 4.9.4 `GameWindow` over GLFW 3.4.0), context version from `ClientSettings.GlContextVersion` (default "4.3"), retried down to 3.3 | a real GL context is created before anything else; GLFW's Null platform is compiled into the bundled `libglfw.so.3` ("Wayland X11 GLX Null EGL OSMesa") but OpenTK 4.9.4's `GLFWProvider.EnsureInitialized` only sets one init hint (`X11` when `XDG_SESSION_TYPE=wayland` and `OPENTK_4_USE_WAYLAND=0`), so the Null platform is unreachable without patching |
| Audio | `clientPlatformWindows.StartAudio()` -> `new AudioOpenAl(logger)`; `initContext` wraps `ALC.OpenDevice`/`CreateContext` in try/catch and logs "Failed creating audio context" on failure | no flag disables audio; a machine without an audio device does not block the boot (the failure is caught), and the bundled `libopenal.so.1` is OpenAL Soft, so `ALSOFT_DRIVERS=null` should give a silent device (not verified here: this machine has audio) |
| Loop | `screenManager.Start(args, rawArgs)`; `gameWindowNative.Run()` (GLFW event loop, `window_RenderFrame` -> `frameHandler.OnNewFrame(dt)` -> `SwapBuffers`) | frames only exist inside `GameWindow.Run`; a stub platform must pump `ScreenManager.OnNewFrame` itself |
| Single instance | `UriHandler.TryConnectClientPipe()` on the named pipe `SingleInstanceVintageStoryWithUriScheme`: if another client already runs, `--connect` is forwarded to it and the process exits | a test launcher must never run while the developer's own game is open, and two test clients on one machine need `multipleInstances`-style isolation (the pipe is global per user) |

### Connect-on-launch exists and is complete

`ClientProgramArgs` (CommandLine parser): `-c/--connect <host:port>`, `--pw`, `--dataPath`,
`--logPath`, `--addModPath` (appended to the client's mod search paths in
`ScreenManager.loadMods`), `--addOrigin`, `--tracelog`, `-o/--openWorld`, `--rndWorld`.
`ScreenManager.HandleArgs()` calls `ConnectToMultiplayer(host, password)` ->
`ServerConnectData.FromHost` (parses `host:port`, SRV lookup only when no port and not an IP) ->
`StartGame(singleplayer: false, null, connectData)` -> `new GuiScreenRunningGame` ->
`new ClientMain(this, Platform)` -> `ClientMain.Start()` -> `Connect()` over a real
`TcpNetClient` + `UdpNetClient`. The join sequence is the same one the dummy connection
mirrors: identification (packet 1) from `ClientSystemStartup.sendIdentificationPacket`,
then `GeneralPacketHandler.HandlePlayerData` sends 26 (`ClientLoaded`) on own-player data and
29 (`PlayerReady`) once `TriggerIsPlayerReady()` passes.

### The login gate is the one non-technical obstacle

`ScreenManager.DoGameInitStage2()` (after assets, sounds and shaders):

```
if (!sessionManager.IsCachedSessionKeyValid()) { LoadAndCacheScreen(typeof(GuiScreenLogin)); return; }
sessionManager.ValidateSessionKeyWithServer(OnValidationDone);   // https://auth3.vintagestory.at/clientvalidate
```

`SessionManager.IsCachedSessionKeyValid()` (`Vintagestory.Client.MaxObf`) verifies the RSA
signature of `ClientSettings.Sessionkey` against `SessionSignature` with a public key baked
into the assembly, and requires a non-empty `PlayerUID`. Only a valid key reaches Stage 3, and
Stage 3 proceeds even when the auth server is unreachable (`EnumAuthServerResponse.Offline`
-> `ClientIsOffline = true` -> Stage 4 -> Stage 5 -> `HandleArgs`). So `--connect` is only
honored by a client that has logged in at least once on that data path. Offline multiplayer
is otherwise fully supported by the engine: `ClientSystemStartup.HandleLoginTokenAnswer` sends
the identification with `"offline"` in place of an MP token when `ClientIsOffline`, and the
server skips `VerifyPlayerWithAuthServer` entirely when `Config.VerifyPlayerAuth` is false
(`ServerMain.HandlePlayerIdentification`: `if (client.IsSinglePlayerClient ||
!Config.VerifyPlayerAuth) { PreFinalizePlayerIdentification(...); return; }`).

A test data path has no session key. Three ways past the gate, in order of cleanliness:

1. Copy a logged-in `clientsettings.json` (the developer's own) into the test data path.
   Works locally, never in CI, and moves an account credential around: rejected.
2. Ship a tiny launcher that applies two Harmony prefixes before `ClientProgram.Main`
   (`IsCachedSessionKeyValid` -> true, `ValidateSessionKeyWithServer` -> callback `Offline`).
   0Harmony.dll ships in the game's `Lib/`. This is what the experiment used. It does not
   bypass ownership (the full client install is still required) and it only enables the
   engine's own offline mode against a local server with `VerifyPlayerAuth=false`, but it is a
   patch on the login code of a commercial client, so it must be cleared with Anego Studios
   before Atlas ships it. The blocking question for the recommended path is this one, not a
   technical one.
3. Ask Anego Studios for a supported switch (a `--offline` flag, or honoring `--connect` when
   the target is loopback and the server does not verify auth). Same outcome as 2 without the
   patch; depends on upstream.

### How much rendering bypasses the platform abstraction

`ClientPlatformAbstract` (340 lines, ~150 abstract members) wraps the window, input, audio,
bitmaps, textures, meshes, framebuffers, shaders and screenshots. `ClientPlatformWindows` is
its only subclass (3755 lines, 519 direct `GL.*` calls). Outside it, the census of direct
OpenTK `GL.*` calls in the client namespaces:

| File | Direct `GL.*` calls | First call site |
|---|---|---|
| `SystemRenderOITLayers` | 38 | `OnRenderFrame` (render callback) |
| `ShaderProgramBase` | 38 | every `Uniform*` setter (`GL.Uniform1/2/3/4`, `GL.UniformMatrix4`) |
| `VAO` | 11 | `Dispose` and buffer updates |
| `ChunkRenderer` | 9 | `RenderOpaque` |
| `SystemRenderSunMoon` | 8 | **the constructor** |
| `UBO` | 7 | `Bind` |
| `SystemRenderFrameBufferDebug` | 6 | `OnRenderFrame2DOverlay` |
| `SvgLoader` | 5 | `LoadSvg` |
| `ScreenManager.Render` | 2 | `GL.ClearBuffer` + `GL.DepthRange`, **every frame** |
| `GameWindowNative`, `ClientMain` (`GL.DepthRange` x2), `InventoryItemRenderer`, `ClientSystemStartup.HandleLevelFinalize`, `Screenshot`, `ClientPlatformAbstract.DisposeIndexBuffer` | 1 to 2 each | |

Sixteen files, about 130 call sites. Shader compilation and uniform-location lookup go through
the platform (`CompileShader`, `CreateShaderProgram`, `GetUniformLocation`: the stub probe
below counted 42 and 1137 calls respectively during `ShaderRegistry.Load()`), but setting a
uniform does not, and the per-frame `ScreenManager.Render` does not. `ClientMain.Start()`
builds a fixed `clientSystems` array of 40 systems (not pluggable), 15 of them `SystemRender*`,
and constructs `SystemRenderSunMoon` (direct GL in the constructor) on every start, before any
connection. OpenTK 4 GL entry points are function pointers loaded by the window's bindings
context; with no context they are never loaded, so each of those ~130 sites is a hard failure
on a stub, not a no-op.

The abstraction also leaks types: a `ClientPlatformAbstract` subclass must reference
`OpenTK.Windowing.Common` (`WindowState`), `OpenTK.Windowing.GraphicsLibraryFramework`
(`WindowAttribute`), `SkiaSharp` (`SKBitmap`) and `cairo-sharp` (`ImageSurface`) from the
game's `Lib/`, and the `libcairo-2` native resolver is registered by the static constructor of
`Cairo.CairoAPI`, which the stock boot touches through `ClientPlatformWindows` and a stub must
touch explicitly (measured: `DllNotFoundException: libcairo-2` otherwise).

### The mod-facing surfaces a client bridge would use

| Need | Symbol | Notes |
|---|---|---|
| Trigger a hotkey by code | `HotkeyManager.TriggerHotKey(KeyEvent, IWorldAccessor, IPlayer, bool allowCharacterControls, bool keyUp)` (public, `ScreenManager.hotkeyManager`); or `capi.Input.HotKeys[code].Handler(hotKey.CurrentMapping)` (`HotKey.Handler` and `CurrentMapping` are public fields) | the second form calls the mod's handler directly and skips `ShouldTriggerHotkeys`, dialog focus and `KeyboardState`; the first goes through the engine's dispatch with a synthesized `KeyEvent { KeyCode = (int)GlKeys.K }` |
| Send chat and commands | `ICoreClientAPI.SendChatMessage(string, int groupId, string data)`; `TriggerChatMessage(string)` also executes dot-prefixed client commands | what the Caminus hotkey handler itself does |
| Open dialogs and their text | `IGuiAPI.OpenedGuis` and `LoadedGuis` (`List<GuiDialog>`, backed by `ClientMain.OpenedGuis`); `GuiDialog.Composers` (`DlgComposers`, by name); `GuiDialog.DebugName` (virtual, defaults to the type name), `DialogType`, `IsOpened()`; `composer.GetDynamicText(key)` (`GuiElementDynamicTextHelper`), `GetStaticText`, `GetRichtext`, `GetElement(key)`; `GuiElementTextBase.GetText()` (public virtual) | text is a CPU-side string in `GuiElementTextBase.text`; Cairo composition runs without GL, only the final texture upload goes through the platform |
| Received highlights | `ClientMain.PacketHandlers` (public `ServerPacketHandler<Packet_Server>[256]`), slot 52 is `SystemHighlightBlocks.HandlePacket`; `Packet_HighlightBlocks { Slotid, Blocks (packed, `BlockTypeNet.UnpackBlockPositions`), Colors, ColorsCount, Mode, Shape, Scale }` | `SystemHighlightBlocks.highlightsByslotId` is private and `BlockHighlight` only keeps the tesselated `MeshRef`, `mode`, `shape`, `Scale` (positions are consumed by `TesselateModel`); the server-driven path does NOT raise `ClientEventManager.OnHighlightBlocks` (that event only fires for client-side API calls), so a bridge must wrap `PacketHandlers[52]` to see positions and colors |
| Received particles | `PacketHandlers[61]` = `GeneralPacketHandler.HandleSpawnParticles`; `Packet_SpawnParticles { ParticlePropertyProviderClassName, Data }`, decoded with `ClientMain.ClassRegistry.CreateParticlePropertyProvider(name)` + `FromBytes(BinaryReader, world)` | wrapping the handler yields the `IParticlePropertiesProvider` (for `SimpleParticleProperties`: `MinPos`, `AddPos`, `MinVelocity`, `Color`, `MinQuantity`) before `ParticleManager` consumes it |
| Mod-channel packets by type | `PacketHandlers[55]` = `NetworkAPI.HandleCustomPacket` (`Packet_CustomPacket { ChannelId, MessageId, Data }`); channel and message ids come from the `Packet_NetworkChannels` the server sends at join | a bridge can either wrap slot 55 and decode by the mod's registered types, or register its own `SetMessageHandler<T>` on the channel when the mod exposes its message types |
| Screenshot | `ClientPlatformAbstract.SaveScreenshot(path, filename, withAlpha, flip, metaDataStr)` and `GrabScreenshot(...)` -> `Screenshot.GrabScreenshot` -> `GL.ReadPixels` of the current framebuffer, PNG via SkiaSharp; reachable from a mod as `ScreenManager.Platform.SaveScreenshot(...)` | tier 3 mechanism for path B; the client's own `SystemScreenshot` hotkey does the same |

All of these except `HotkeyManager` and `ClientMain` internals are public API. A client bridge
mod can reach `ClientMain` as `(ClientMain)capi.World` and `ScreenManager.Platform` /
`ScreenManager.hotkeyManager` are public statics.

### What the embedded server must do to accept a real client

Atlas boots `ServerMain(..., isDedicatedServer: false)` and never opens a socket. The engine's
own LAN toggle (`CmdToggleAllowLan`) shows the exact recipe for a non-dedicated server:

```
server.MainSockets[1] = new TcpNetServer(); server.MainSockets[1].SetIpAndPort(ip, port); server.MainSockets[1].Start();
server.UdpSockets[1]  = new UdpNetServer(server.Clients); server.UdpSockets[1].SetIpAndPort(ip, port); server.UdpSockets[1].Start();
```

Slot 1 is precisely the `EngineTcpSlot` that `DummyClientConnector` already reserves and never
claims, so real and dummy players coexist by construction. Plus `Config.VerifyPlayerAuth =
false` before launch (a fresh Atlas config defaults it to true), a free loopback port, and
`WhitelistMode` is a non-issue (`Default` only applies to dedicated servers and exempts local
connections anyway). The client-side mod check (`SystemModHandler`) requires every server mod
with `RequiredOnClient` to be present on the client: the mod under test, and any universal
fixture mod, must be passed to the client through `--addModPath`; Atlas's own server-side
bridge (`Side = "Server"`) is not required on the client.

## Path A: in-process stub platform. Verdict: not viable as a headless client

A generated subclass of `ClientPlatformAbstract` (every abstract member a traced no-op, seven
hand-written: logger, asset manager, sizes, clock, GLSL version string) driving
`ScreenManager` directly, without `ClientProgram`, without a window. Measured depth:

| Step | Result with the stub |
|---|---|
| `new ScreenManager(stub)`, `Lang.PreLoad` | pass |
| `ScreenManager.Start` (GUI composition through Cairo, `LoadOrUpdateCairoTexture`, input registration) | pass once `Cairo.CairoAPI` is touched first (otherwise `DllNotFoundException: libcairo-2`) and `GuiStyle` fonts are set as `ClientProgram.Start` does |
| Stage 1 (626 base assets, sounds) | pass; `CreateAudioData` must return a real `AudioMetaData` (a null from the stub NREs inside `AudioMetaData.DoLoad`); audio decoding is CPU-side (csvorbis), only playback touches OpenAL |
| Stage 2, `ShaderRegistry.Load()` | pass: 84 `GetGLShaderVersionString`, 42 `CompileShader`, 1137 `GetUniformLocation`, all through the platform; the version string must look like GL's ("4.60") |
| Stage 2, cursors and the first screen | fails in `GuiElement.getImageSurfaceFromAsset` because `CreateBitmapFromPng` returned null: the stub must carry the real SkiaSharp bitmap code |
| `ClientMain.Start()` (not reached) | would fail in `SystemRenderSunMoon..ctor` (direct GL) before any connection; then `ScreenManager.Render` (`GL.ClearBuffer`, every frame), `ShaderProgramBase.Uniform*`, `ChunkRenderer`, `SystemRenderOITLayers`: about 130 sites in 16 files, none reachable through the abstraction |

So the stub approach gets through asset loading and shader registration but stops exactly
where the engine starts to render, and the rendering code is not replaceable: the 40-system
array in `ClientMain.Start()` is hard-coded, `ShaderProgramBase` sets uniforms with direct GL
on every renderer, and `ScreenManager.Render` clears buffers with direct GL every frame.
Making this run needs either a Harmony patch set over every direct-GL site (a maintenance
treadmill against an obfuscation-shuffled assembly on every game release) or a real GL
context, at which point the stub has no purpose: with a real context you can use
`ClientPlatformWindows` and the real window. A "reduced" path A (stub + a GLFW Null-platform
window + EGL surfaceless context) is theoretically possible with the bundled GLFW but
unreachable through OpenTK 4.9.4 without patching `GLFWProvider`, and it would still run the
full renderer, so it saves nothing over path B except the process boundary. Path A is dead
for a headless client. The stub probe is not wasted, though: it establishes that everything
up to rendering (assets, Cairo GUI composition, dialog text, hotkey registration, mod loading)
is CPU-side and stubbable, which is what makes tier 2 style assertions on GUI text feasible
in a future "client logic without renderer" mode if the vendor ever exposes one.

## Path B: real client subprocess on a virtual display + client bridge mod. Verdict: viable, measured live

The real client (`Vintagestory.dll` entry via a 60-line launcher), `--connect
127.0.0.1:<port> --dataPath <per-run dir> --addModPath <mod under test> <client bridge>`,
running on an offscreen display with Mesa llvmpipe, against a server with
`VerifyPlayerAuth=false`; the client bridge mod (`Side = "Client"`, the mirror of
`src/Atlas.Bridge`) exposes the surfaces in the table above over a loopback socket and pushes
events the way `BridgeRendezvous` does today, but across a process boundary. This is the
experiment that ran.

### The experiment

Setup, all on this machine, nothing visible on the desktop at any point:

- Display: no Xvfb is installed here (`xorg-server-xvfb` is not present and installing it needs
  root), so the offscreen display was a nested KWin with its virtual backend, on an isolated
  bus: `dbus-run-session -- kwin_wayland --virtual --xwayland --no-lockscreen
  --no-global-shortcuts --width 1280 --height 800` (with `WAYLAND_DISPLAY` and `DISPLAY`
  unset). It spawned its own Xwayland on `:2` (the real session stays on `:1`). `glxinfo
  -display :2` with `LIBGL_ALWAYS_SOFTWARE=1` reported `llvmpipe (LLVM 22.1.8, 256 bits)`,
  core profile 4.6, direct rendering: the same renderer a CI runner would use.
- Server: the stock dedicated server from the same install, fresh data path, `--port 42431
  --withconfig='{ VerifyPlayerAuth: false, AdvertiseServer: false, MaxClients: 4 }'`. Fresh
  world, 25 seconds to "Dedicated Server now running".
- Client data path: a fresh directory with a 15-line `clientsettings.json` (`playername`,
  `playeruid`, `masterSoundLevel: 0`, `musicLevel: 0`, `gameWindowMode: 0`, 1024x768,
  `maxFps: 30`, `viewDistance: 64`, `skipNvidiaProfileCheck`, `multipleInstances`). The
  engine fills in the other 140 keys on first save.
- Launcher: a scratch console project referencing the install's `Vintagestory.dll`,
  `VintagestoryLib.dll` and `Lib/0Harmony.dll`, which sets `APP_CONTEXT_BASE_DIRECTORY` to the
  install, resolves assemblies from the install and its `Lib/`, applies the two session-gate
  prefixes (plus one that skips the newest-version web check), and calls
  `ClientProgram.Main(args)`. Environment: `DISPLAY=:2`, `OPENTK_4_USE_WAYLAND=0`,
  `LIBGL_ALWAYS_SOFTWARE=1`, `FONTCONFIG_FILE=<install>/fonts.conf` (what `run.sh` sets).

Result, from the logs (times are wall clock on a 24-core desktop):

| t | Event | Source |
|---|---|---|
| 23:24:28 | client logger started | `client-main.log` |
| 23:24:30 | "Client 1 uid atlas-client-spike attempting identification. Name: atlasclient" | `server-main.log` |
| 23:24:30 | "Okay, received single use mp token from auth server. Sending ident packet" (the offline token) | `client-debug.log` |
| 23:24:30 | "atlasclient joined." | `server-audit.log` (`SendServerReady`) |
| 23:24:32 | `HandleRequestJoin: Begin. Player: atlasclient` | `server-debug.log` |
| 23:24:33 to :40 | server assets received, 14091 block types, three 4096x2048 atlases composed, "Server assets loaded" | `client-main.log` |
| 23:24:41 | "Received level finalize"; "atlasclient [::ffff:127.0.0.1]:39710 joins." (packet 26 handled: `HandleClientLoaded`) | both |
| 23:24:42 | "(^_^) No issues captured during startup" | `client-main.log` |
| 23:24:43 | "Welcome atlasclient, may you survive well and prosper" (the server's own-player chat on `Playing`) | `client-chat.log` |

Fourteen seconds from process start to in-world on software GL. A capture of the client's X
window on `:2` (`xprop -root _NET_CLIENT_LIST`, then ImageMagick `import -window <id>`) shows
the game world rendered by llvmpipe with the first-join "Customize Skin" dialog, the minimap
and the hotbar: a real framebuffer, so tier 3 is free on this path (and the engine's own
`SaveScreenshot` gives the same PNG without any X tooling). Process footprint at steady state:
RSS 3.4 GB, 85 threads, 700 to 1200 percent CPU (llvmpipe rasterizing at the 30 fps cap;
`window_RenderFrame` only sleeps when `MaxFps` is between 10 and 241, so the CI value should
be around 15, never 10 or below). A SIGTERM shuts it down cleanly through `ClientProgram.OnExit`.
The first attempt, without `APP_CONTEXT_BASE_DIRECTORY`, stalled silently on the GTK message
box mentioned above: worth remembering when the launcher runs from Atlas's bin folder.

What the experiment did not do: no client bridge mod was written, so hotkeys, dialog text and
packet capture were not exercised end to end; those surfaces are established from the decompile
only (table above). No run under real Xvfb, no run on a 4-core machine.

### CI implications

- GitHub `ubuntu-latest` images ship `xvfb-run` and Mesa with llvmpipe (`libgl1-mesa-dri`);
  `xvfb-run -a -s "-screen 0 1024x768x24"` replaces the nested KWin used here. To verify in
  the first lane, not assumed: the audio device (expect "Failed creating audio context" to be
  caught; set `ALSOFT_DRIVERS=null` for a silent OpenAL Soft device), and fontconfig with the
  install's `fonts.conf`.
- The lane needs the FULL client install (the server-only archive Atlas downloads today lacks
  `Vintagestory.dll`, the whole `Lib/` folder with OpenTK, GLFW, OpenAL, Skia, Cairo, Harmony,
  and the client assets): a different download, roughly 10 times the size, cached like the
  server archives in `compat.yml`. Its terms of use for CI are the same question as the login
  patch and must be cleared upstream.
- Budget per client scenario: about 3.5 GB RSS (fits the 16 GB standard runner next to the
  embedded server), a boot in the 30 to 60 second range on 4 vCPUs (14 s here on 24 cores),
  CPU-bound rendering for the whole scenario. One client per test class, reused across
  scenarios of that class, is the only affordable shape; one client per scenario is not.
- Determinism: the client is a real-time process (no `Ticks(n)` control); assertions must
  poll the bridge with timeouts, like the existing `WaitForJoin`.

### Local implications

- Leon's machine has no Xvfb; the nested `kwin_wayland --virtual` recipe worked and is
  reproducible, but it is a Plasma-specific trick. The honest local requirement is
  `xorg-server-xvfb` (one package) or an equivalent headless display, configured explicitly.
- The "pas lancer le jeu" rule holds only if Atlas refuses to launch the client on the user's
  real display: the launcher must require an explicit offscreen display (an
  `ATLAS_CLIENT_DISPLAY` setting or an Atlas-spawned Xvfb) and fail closed when it is absent,
  rather than inheriting `DISPLAY`/`WAYLAND_DISPLAY` from the shell. The single-instance
  named pipe adds a second guard: if the developer's own game is open, `--connect` would be
  forwarded to it and the test would silently drive the wrong process, so the launcher must
  check that pipe first and refuse.
- Per-run data paths keep the client's `clientsettings.json` (written every 2 seconds by
  `ScreenManager.OnNewFrame`) out of the developer's real config.

## Effort estimate per path

| Path | What it takes | Estimate | Confidence |
|---|---|---|---|
| A, stub platform | full-fidelity CPU stub (bitmaps, audio decode, textures as ids) plus Harmony patches over ~130 direct-GL sites in 16 files, redone per game release | weeks, then a treadmill | high that it is not worth it |
| B, subprocess + client bridge | launcher (session-gate prefixes, base directory, display guard, single-instance guard); embedded server LAN sockets in slot 1 + `VerifyPlayerAuth=false`; client bridge mod (`Side = "Client"`): loopback socket, hotkey/chat/dialog/packet-capture commands, event push; `ITestClient` API on the xUnit side with polling waits; CI lane with the full install cached and `xvfb-run`; docs | 2 to 3 weeks of focused work for a first usable version (hotkey, chat, dialog text, highlights, particles, custom packets, screenshot), assuming the upstream answer is yes | medium: the engine side is measured, the bridge is designed, CI is sized not run |
| Tier 3 on B | `ScreenManager.Platform.SaveScreenshot(path)` through the bridge, or an X capture of the window; a `client.Screenshot(path)` API | 1 to 2 days once B exists | high |

## Recommendation

Not now for the code; yes for the path. Tier 2 (server-send capture on the dummy connection,
in flight) is the right first delivery: it covers highlights, particles and mod-channel packets
with no new process, no GL, no licensing question. Tier 1 should be built on path B and only
path B, and its start is gated on one non-technical answer: whether Anego Studios accepts a
test launcher that bypasses the cached-session-key check to run their client offline against
a local `VerifyPlayerAuth=false` server, and a full client install in CI. Ask that question
now, with this document as the evidence; if the answer is yes, schedule B as sized above; if
the answer is a supported offline switch instead, B gets cheaper; if the answer is no, tier 1
is closed as "vendor policy", and the client-logic parts of the need (dialog text, hotkey
handlers) are only reachable through unit-level tests inside the mod itself, which is what
the mod-preparation list below already enables.

## What a mod must prepare to be testable

Independent of the path, and useful today for tier 2 and for the mod's own unit tests:

- Stable dialog identifiers: override `GuiDialog.DebugName` (or pick a fixed composer key in
  `Composers`) for every `HudElement`/`GuiDialog`, and give each `AddDynamicText` a fixed
  element key (`"line1"`, `"line2"`, `"line3"`), so a bridge can address them by name rather
  than by type or position. Expose the composed text through a small accessor if the elements
  are private.
- Hotkey codes: register with a stable `hotkeyCode` (`"caminus-overlay"`) and keep the
  handler side-effect free apart from the message it sends, so `HotKeys[code].Handler` can be
  invoked by code with `CurrentMapping`.
- Highlight slot constants: one `public const int` per slot (`OverlaySlot = 7`), never a
  literal at the call site, so both sides of a test refer to the same number.
- Channel and message types: the channel name (`"caminus"`) and every registered message type
  as public constants/types in the mod's API assembly, in the same registration order on both
  sides (the engine maps message ids by registration order), so a capture layer can decode
  `Packet_CustomPacket` by type.
- Client-only logic behind a testable seam: keep the overlay's text formatting in a pure
  function of the received packet, so it is unit-testable without any client at all.
