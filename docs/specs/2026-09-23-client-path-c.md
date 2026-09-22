# Client path C: an in-process client outside ScreenManager's init stages

Date: 2026-09-23
Status: measured, not viable as designed. Addendum to `2026-07-17-client-side-testing.md`.
Tracks: issue #100.

## The question

The July spike measured two paths and parked both: path A (an in-process stub platform, weeks
of work and then a treadmill against every release) and path B (a real client subprocess,
blocked in practice by `ScreenManager.DoGameInitStage2`'s session-key gate, passable only with
a Harmony patch or an Anego-supported switch, neither in hand). A third shape, borrowed from
Zaldaryon.Pharos 0.4.0 (`github.com/Zaldaryon/Pharos`, MIT, commit `d6bd1ca`, a third-party
client-boot harness for xUnit tests), builds a real `ClientMain` directly by constructor, wired
to a hidden GLFW window and FBO, never routed through `ScreenManager`'s stage sequence at all,
so the session-key gate is never reached because the code that checks it is never called.
Pharos's own boot does not call `ClientMain.Start()` either, so `SystemNetworkProcess` (the
engine's packet-pump thread) never exists and its own join does not complete: confirmed directly
by this spike's baseline run (Result, below), not taken on faith from Pharos's documentation.

This spike asks the next question directly: build a real client in-process the same way, join
it to an embedded server through the engine's singleplayer loopback (the same auth-skip
mechanism Atlas's own `DummyClientConnector` already uses server-side), and this time call
`ClientMain.Start()` for real. Does the player reach `Playing` with a spawned entity, and what
does that cost.

## Method

A throwaway prototype, `spike/ClientPathC/` on branch `spike/client-path-c`, not part of
`Atlas.slnx` and not referenced by anything in `src/`. It references the engine DLLs from a
local 1.22.3 install (`VINTAGE_STORY`) directly, and, for the server and client boot recipes it
starts from, a Zaldaryon.Pharos 0.4.0 build (`Zaldaryon.Pharos.dll`, MIT, Copyright (c) 2026
Zaldaryon; the notice is kept in `spike/ClientPathC/LICENSE-PHAROS.md`, called out here per that
license). The project takes the build's location from `PharosLibPath` or the `PHAROS_LIB`
environment variable and refuses to build without one set. No Pharos source is copied into the
spike; it is referenced as a compiled library and called through its public API
(`EmbeddedServerHost.Boot`, `HeadlessClientBootstrap.Boot`, `HeadlessClient.ConnectLoopback`).
The one addition on top of that recipe is a single call, `client.Client.Start()`, placed after
boot and before the loopback join.

Everything graphical runs inside a nested, isolated KWin virtual session, via
`spike/ClientPathC/run-nested.sh`: `dbus-run-session -- kwin_wayland --virtual --xwayland
--no-lockscreen --no-global-shortcuts` with `DISPLAY`/`WAYLAND_DISPLAY` unset and the default
`XDG_RUNTIME_DIR` (an earlier attempt pointed `XDG_RUNTIME_DIR` at a private directory for the
compositor itself and could not get Xwayland to start; that override is gone from the script).
The script waits for a new entry to appear under `/tmp/.X11-unix`, checks it with `glxinfo
-display :N -B` under `LIBGL_ALWAYS_SOFTWARE=1` and aborts unless the renderer is `llvmpipe`,
then runs the spike with that display, its own private `XDG_RUNTIME_DIR`,
`OPENTK_4_USE_WAYLAND=0` and `LIBGL_ALWAYS_SOFTWARE=1`. The client process itself also refuses
to start unless it sees a `DISPLAY`, no `WAYLAND_DISPLAY`, and `CLIENT_PATHC_NESTED=1`, all set
only by that script, so it cannot open a window on a real desktop session by accident.
Decompilation of `ClientMain.Start`, `ClientPlatformWindows`, `ScreenManager`,
`SystemRenderOITLayers` and related classes used `ilspycmd` against the local install's DLLs,
for interoperability diagnosis only; nothing decompiled is redistributed.

## What the decompiled code shows

**Fact.** `ClientMain.Start()` (`Vintagestory.Client.NoObf.ClientMain`, decompiled) never calls
`ScreenManager`'s init stages (`DoGameInitStage2`, where the session-key gate lives) or
`SessionManager`. Its only `ScreenManager` reference is the static `ScreenManager.Platform`,
used for `CheckGlError`, which Pharos's boot assigns before `Start()` runs. It starts the
engine's eight background client threads (`compresschunks`, `blockticking`, `relight`,
`tesselateterrain`, `chunkvis`, `networkproc`, `chunkculling`, `asyncparticles`), constructs
`SystemNetworkProcess` as `networkproc`, and builds the fixed 40-entry `clientSystems` array in
place of Pharos's one-entry stand-in.

**Fact.** Despite never calling into `ScreenManager`'s own code, `Start()` still depends on
several pieces of state that, in the normal boot sequence, only `ScreenManager.Start(
ClientProgramArgs, string[])` or `Vintagestory.Client.ClientProgram.Main` set up as a side
effect, ahead of time. Three such dependencies surfaced directly, in the order `Start()` hits
them (Result, below): `ClientPlatformWindows.crashreporter` (set only in `ClientProgram.Main`),
`RuntimeEnv.MainThreadId` (set only inside `ScreenManager.Start()`, nowhere else in
`VintagestoryLib.dll`), and whatever `ClientPlatformWindows`/`ICoreClientAPI.Render` state
`SystemRenderOITLayers`'s constructor reads from `capi.Render.FrameBuffers`. Pharos's boot,
which exists specifically to avoid running either of those two methods, sets none of them.

## Result

**Measured, in the nested session.** `EmbeddedServerHost.Boot()` against `ServerWorldOptions {
WorldName = "ClientPathC", Seed = "4242", PlayStyle = "creativebuilding", WorldType =
"superflat" }` produced a running `ServerMain` (148 mod systems from 3 mods, 4468 items, 14089
blocks, 125 server-side systems started, world generators running) in 7.0-8.3 s across four
runs, at 805-968 MB RSS and 34 OS threads for the whole process at that point. `
HeadlessClientBootstrap.Boot()` (Pharos's own, unmodified code) then produced a running headless
client, GL renderer `llvmpipe (LLVM 22.1.8, 256 bits)`, 300-600 ms later, at 83 threads and
897-1053 MB RSS. Both are the same server and client boot shapes Atlas's own `ServerHost` and
Pharos's `EmbeddedServerHost`/`HeadlessClientBootstrap` already use elsewhere; nothing about
either boot is new.

**Baseline, measured (no `Start()` call, `variant=baseline`).** With Pharos's boot left exactly
as it ships, `ConnectLoopback` wires the singleplayer loopback and returns immediately, but the
join never completes: `WaitForPlayerJoined(45s)` returned `false` after 1316 frames and 1316
server ticks, the client's own `player` field stayed `null`, and the server's own client record
stayed `state=Connecting`. No thread named `networkproc` appears in the client's thread list.
This confirms directly, for the first time, what Pharos not calling `Start()` was expected to
mean: without the packet-pump thread `Start()` creates, nothing ever reads the loopback socket
on the client side.

**Variant 1, measured (`client.Client.Start()` called after boot, before the join).**
`Start()` throws immediately, 3 ms after the client finishes booting:

```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Vintagestory.Client.NoObf.ClientPlatformWindows.AddOnCrash(OnCrashHandler handler)
   at Vintagestory.Client.NoObf.ClientMain.Start()
```

`AddOnCrash` dereferences `crashreporter` (`crashreporter.OnCrash = OnCrash;`), a public field
on `ClientPlatformWindows` that is never assigned anywhere in `VintagestoryLib.dll` except
`ClientProgram.Main` (`clientPlatformWindows.crashreporter = crashreporter;`). Pharos's boot
constructs `ClientPlatformWindows` directly and wires `window`, `XPlatInterface.Window`,
`WindowSize` and `assetManager` by hand; it does not go through `ClientProgram.Main` and does
not set `crashreporter`. The join was not reached: this is the actual, live-measured result the
spike set out to produce, and it is not the join completing.

**Diagnostic follow-up, not part of the committed code.** To see how deep the gap goes, two
one-line workarounds were tried interactively (patched in locally, run, then reverted; neither
is in the commit): setting `crashreporter` to a fresh `CrashReporter` before calling `Start()`
gets past the exception above, and `Start()` immediately throws a second one,
`InvalidOperationException: Cannot call this method outside the main thread`, from
`ClientEventAPI.RegisterGameTickListener` (via `ParticleManager.Init`, via
`SystemRenderParticles`'s constructor), because `RuntimeEnv.MainThreadId` is still its default
value of `0` and never equals `Environment.CurrentManagedThreadId`. Searching the whole
decompiled assembly, that field is assigned in exactly one place: `ScreenManager.Start()`.
Setting it by hand before calling `Start()` gets past that exception too, and a third one
follows: a `NullReferenceException` inside `SystemRenderOITLayers.BeforeOIT`'s constructor,
dereferencing `capi.Render.FrameBuffers[1]`. That one was not chased further.

Three fixes in, each revealing the next, with no sign of the chain ending: constructing
`ClientMain`'s system array by calling `Start()` touches enough render-pipeline state that
`ScreenManager.Start()` normally sets up first that reproducing it by hand converges with
exactly the work path A already priced at weeks. The earlier draft of this document attributed
the missing measurement to a session-local Xwayland startup fault under the nested KWin virtual
session. That was wrong: the actual cause was this spike's own launch script pointing
`XDG_RUNTIME_DIR` at a private directory for the compositor process itself, which the isolation
recipe does not call for (only the client process needs a private one). With the default
runtime directory, the nested Xwayland comes up in about a second on every attempt, `glxinfo`
reports `llvmpipe`, and the run above is what it measured.

## Variants

Only one C# variant is committed (`client.Client.Start()` after Pharos's boot, before the
loopback join); it is the only change this spike makes to Pharos's own recipe, and it ran, to a
real, reproducible result (above). A second variant (loading real assets before `Start()`, in
case the stub asset manager matters once a frame is rendered) and a third (narrowing `Start()`'s
system set to skip the render systems, for a join-only client) were designed but not built:
after the diagnostic chain above, both would only be useful if patching around
`ScreenManager.Start()`'s side effects one at a time still looked like a short list, and by the
third undiagnosed dependency it no longer does.

## Verdict

**Not viable as designed.** Calling `client.Client.Start()` once, directly after Pharos's boot,
does not let the join complete; it throws before doing anything past the start of its own
initialization. The premise this spike tested, that going around `ScreenManager`'s stage
sequence lets a headless client skip the setup work that sequence does, does not hold: `Start()`
still depends on several pieces of state only `ScreenManager.Start()` or `ClientProgram.Main`
set up, and by the third one found, that list still had no visible end. Making this path work
means finding and reproducing that state by hand, one exception at a time, which is the same
kind of work the July spec already priced for path A, not a shortcut around it.

**What is solid.** The server and client boot recipes themselves (`EmbeddedServerHost.Boot`,
`HeadlessClientBootstrap.Boot`) are fast, reproducible and already used elsewhere in Atlas and
Pharos: nothing here argues against using Pharos for what it is built for, in-process testing
against a running client and server that never calls `Start()`. The isolation recipe
(`run-nested.sh`) also now works reliably and is reusable for any future spike that needs a
disposable graphical session.

**Cost, if someone wants to keep pulling this thread.** Not small. The concrete next step is
enumerating `ScreenManager.Start()`'s full side-effect list (its body is under 200 lines; the
three found here came from three exceptions, not a full read of it) and deciding, for each one,
whether to reproduce it locally or ask Pharos's maintainer to add it to
`HeadlessClientBootstrap.Boot()` upstream. Either way, this is now a bounded, well-scoped list to
work through rather than an open question, which is the actual outcome of this spike: not a
working join, but a much narrower one.

## What is committed

Two branches carry this addendum. `spike/client-path-c` (pushed standalone, not part of any
PR; prototype code as of commit `17b889c`) carries the prototype: `ClientPathC.csproj`,
`Program.cs`, `LICENSE-PHAROS.md` and `run-nested.sh` under `spike/ClientPathC/`, plus this
spec. Build it with `dotnet build spike/ClientPathC/ClientPathC.csproj -c Release` after
setting `VINTAGE_STORY` and either `PharosLibPath` or `PHAROS_LIB` to a Zaldaryon.Pharos 0.4.0
build; run it with `spike/ClientPathC/run-nested.sh [1|baseline] [joinTimeoutSeconds]`. Not
part of `Atlas.slnx`; nothing in `src/` changed. `docs/client-path-c` (cut from `origin/main`)
carries only this spec and a pointer added to the July spec: the PR built from that branch is
meant to be read without needing the prototype branch open.

No `CHANGELOG.md` entry: nothing here changes what a NuGet consumer sees. No new ADR: this
spike closes off one path without the project adopting any of the three; an ADR is the right
artifact once a path is actually chosen to build on.
