# A real client on the developer's own login: spike measurements and design

Date: 2026-10-03
Status: measured on one Linux machine in two parts; nothing implemented in `main`. The decision is
proposed in [ADR 0012](../adr/0012-real-client-own-login.md).
Tracks: issue #164 (tier 1 of #100). Supersedes in part the path B and path C studies below.
Game versions: 1.22.3 full client install (run live), 1.22.7 (code read, not run)
Prerequisites: [client-side testing](2026-07-17-client-side-testing.md) (path B and its login
gate), [client path C](2026-09-23-client-path-c.md)
Throwaway branch: `spike/client-own-login`, three commits, never merged. Nothing from it is
meant to be cherry-picked as it stands: the real code is written again after 0.16.0 ships.

## What changed since the earlier studies

Path B (a real client subprocess) was measured viable in July and parked on one point: the client
only honours `--connect` once a cached session key passes `SessionManager`'s local check, an RSA
signature plus a non-empty uid, with no date. Every way past it needed either the studio's
approval (a Harmony patch) or a switch the studio declined (upstream issue 10012, closed as not
planned). Path C went around `ScreenManager` and stopped inside `ClientMain.Start()`.

The owner's idea turns the obstacle into the design. The developer logs in once, by hand, on a
data path used only for tests, through the game's own login screen. The unmodified client then
passes the check because someone really logged in there. The code reading (1.22.7) found nothing
else in the way: the 21 client screens hold no consent screen, no first-run screen and no forced
update, so after login the start-up reaches `--connect` with no interaction. The spike checked
that on 1.22.3, and checked a good deal more, as the rest of this document records.

Throughout, a fact is marked as measured when a run produced it and as read when it comes from
the decompiled code alone.

## What the spike never touched

The spike ran the developer's own client install and the developer's own account, in a sandbox
(next section) on a dedicated data path. The test harness only ever handed that path to the client
as an argument and never opened anything under it. Test output was piped through a redaction
filter. Before and after each run the desktop's X11 sockets were intact, and no process, port or
Xvfb outlived a run.

The one thing the developer did by hand was the login, in a game window on the desktop, once.

## The design as proposed

### Setting up, once

1. `VINTAGE_STORY` points at a complete client install. The same install serves the embedded
   server and the client, so the network version cannot differ.
2. Xvfb is installed (`xorg-server-xvfb`, `xvfb` or `xorg-x11-server-Xvfb`, by distribution).
3. `atlas client login` warns that a game window will open on the desktop and waits for a yes. It
   creates the dedicated root, by default `~/.local/state/atlas/client/`, mode 0700, with the
   game's data path in `data/` and an Atlas sentinel beside it. Only if
   `data/clientsettings.json` does not exist, it writes a seed with no secret in it: windowed
   mode, window size, `vsyncMode` 0, `maxFps` 15, volumes at 0, hints off. The frame cap only
   applies when `vsyncMode` is not 1. It then starts the stock client with `--dataPath`, a
   `--logPath` outside the data path, a private `TMPDIR`, and no `--connect`.
4. The developer logs in on the game's login screen, second factor included, and leaves through
   the Quit button of the main menu. Quit forces the settings save (read from the code); closing
   the window within about 2 s of the login can lose it.
5. `atlas client check`, chained by the login command, starts the client offscreen with no
   `--connect` and waits for `Server validation response: Good` or `Offline`. `Bad` means not
   ready. On a good answer it writes a `ready` marker owned by Atlas, beside `data/` and never
   inside it. The line `Cached session key is valid` is not a valid signal: it says only that the
   local signature holds, not that the auth service accepts the session.

### What Atlas does and never does with that path

It creates the path, writes the seed once before any login exists, hands the path to the client,
tests that files exist, and takes a lock on it. It never opens, reads, parses, copies, moves,
archives, prints or logs `clientsettings.json`, its `.bkp` or its `.tmp`, and never writes to it
again. It never copies the data path into scratch, fixtures or retained artefacts.

It accepts only a directory it created itself, recognised by the sentinel. Without that rule, an
`ATLAS_CLIENT_DATA` variable pointed at the game's real data folder would have rewritten the
developer's real settings on every run.

To log out, the developer has to use the game's logout link, the only code that posts
`gamelogout` (`SessionManager`, read). Deleting the folder is not enough: the session stays
alive at the studio, with no expiry on the client side. `atlas client logout` therefore opens the
game for the developer to click, and only then deletes.

### Guards

**The session is unreadable by construction, for Atlas's code.** One type owns the data path. It
can return the path, test existence, create the seed with `FileMode.CreateNew`, check the sentinel
and take the lock. A unit test checks that the literal `clientsettings` appears in that one file
of the repository. Login state comes from the client's log alone. The limit must be written in
the open: this covers Atlas's code only. The mod under test and anything staged with
`--addModPath` run in a logged-in process, where the key is a public static field. That is the
exposure of playing with the mod, no more and no less.

**The session key is an account identifier, not a play token.** It also authorises
`gameserverctrl` actions, for example `deleteallmods` on the owner's hosted server (read from
`ServerCtrlBackendInterface`). A second copy under the home directory doubles the exposure to
backups, sync tools and dotfile repositories; mode 0700 changes nothing there. It lives in the
`.json`, in the `.bkp` and briefly in the `.tmp`, and after a logout the old key stays in the
`.bkp` until the next save.

**Never on the real display.** The launcher accepts only a display value that a successful start
of Atlas's own Xvfb can build. The child's environment starts empty and receives an allowlist.
`DISPLAY`, `WAYLAND_DISPLAY`, the D-Bus address and the user's `XAUTHORITY` never pass, and `HOME`
and `XDG_RUNTIME_DIR` are private. Any failure is a skip, with no fallback to an inherited
display. The engine already forces X11 on a Wayland session by itself (read). `atlas client
login` and `logout` are the only code that opens the game on the real session, and they ask first.
A user-supplied display is not accepted, because a user value could be `:0`.

**The developer's running game is never driven.** The single-instance pipe forwards `--connect`
to an already running game and exits silently (read, `ClientProgram`). On Linux it lives under
`TMPDIR`, and the sandbox's private `/tmp` isolates it in both directions by construction. A
positive control stays in the design: after launch the pipe must appear under the private
directory, or Atlas fails with "launch was forwarded to another game instance".

**The client dies with the host.** Xvfb, the client and the crash reporter share the sandbox's
PID namespace, so the kernel kills them when the sandbox's first process ends. A guardian that
holds a pipe from the host covers the host's own death, SIGKILL included: SIGTERM to the group,
SIGKILL after about 10 s. Backstops: `-terminate` on Xvfb, a wall-clock ceiling, a sweep of
leftovers at start.

**One client per data path**, by a machine-wide lock. **Loopback only**: the listener binds
`127.0.0.1` explicitly and takes a random password. When `Config.LoginFloodProtection` is on, two
requests from one IP within 500 ms cut the second, which matters for quick reconnects (read).

### Skip ladder

A skip is decided before any boot. The first case that matches wins, each with its message and
remedy:

1. a CI variable is present (`CI`, `GITHUB_ACTIONS`, `TF_BUILD`, `GITLAB_CI`);
2. `ATLAS_CLIENT=off`;
3. the system is not Linux;
4. the install lacks the client files (`Vintagestory.dll`, the apphost, `Lib/libglfw.so.3`);
5. the .NET runtime the client needs cannot be found for the apphost, which does not search
   `PATH` (a private dotnet without `DOTNET_ROOT` must give a clear skip);
6. no Xvfb binary;
7. no Atlas data path, no sentinel, or no `ready` marker;
8. the path's lock is held by another run.

Typical message: "Atlas client scenarios skipped: no logged-in client data path at `<path>`. Run
`atlas client login` once on this machine (it opens the game on your desktop; log in with your
own account, then quit). Client scenarios run on developer machines only."

A third outcome sits between "ready" and "the test failed": the infrastructure fails. The session
turns invalid (an "invalid" answer clears the key from the path), the validation stays stuck, the
multiplayer token is refused, the network times out after 10 s. These kill the client and give a
skip with a distinct message, and remove the `ready` marker when the session is the cause. Only
`ATLAS_CLIENT=required` makes them red.

### Silent stops the supervisor must catch by its own deadline

Read from the code, about 120 s as a first value: a modal box at start-up (missing assets, a dirty
install), no GL context, the login screen (the marker line can appear more than once), an empty or
non-JSON 2xx answer that leaves "Validating session" up forever, a multiplayer token that fails
after a good validation (one retry, then a block, with only a translated notification line as a
clue), a wrong network version or a missing client mod (a disconnect screen, never an exit), and
the character gate. The character gate is dealt with below. The non-JSON answer was not tested.

## Spike part 1: the client and the login (measured)

The client ran under `sandbox.sh` (next section) on the 1.22.3 install, with a dedicated data path
seeded with 18 non-secret values. The numbered questions come from the code reading's question
list.

| # | Question | Result |
|---|---|---|
| 1 | Does the stock client reach the login screen offscreen? | Yes, on llvmpipe, with no contact with the desktop display. Isolation is by construction: `strace` was not installed, so there is no syscall trace. |
| 2 | Is the single-instance pipe isolated? | Yes by construction: the sandbox's `/tmp` is private. |
| 3 | Is a partial seed without secrets merged with defaults? | Yes. All 18 seeded values were kept, and the window opened in Normal mode. |
| 4 | Can two paths hold a login for one account? | No. Logging in on the dedicated path logged out the developer's usual game: one session per account. |
| 5 | Online validation and multiplayer token | `Server validation response: Good`, token received, in under a second. |
| 6 | Join a local server | Yes, against the stock dedicated server in loopback (`VerifyPlayerAuth` false, listening on `127.0.0.1` only): in game 13 s after the client started, a welcome line in the chat 2 s later. |
| 7 | The character gate | The "Customize Skin" dialog opened after the welcome. Bypassed in part 2. |
| 8 | Session after a brutal stop | Intact: `Good` at the next start. |
| 10 | Resources | 3.2 to 3.4 GB of memory, 350 to 980 percent CPU on llvmpipe in spite of `maxFps` 15. |

Questions 9 (forced crash), 11 (a 200 answer that is not JSON) and 12 (a paused game thread) were
not part of part 1. Part 2 did 9. Questions 11 and 12 are still undone.

With no network, the client on the dedicated path verifies its key locally, reports "Offline Mode"
and reaches the main menu. That is the engine's offline mode, which is what the offline way out
below relies on.

In the July spec, the "14 s to in-world" figure measured `Connected`, not `Playing`: the welcome
line goes out in `HandleClientLoaded`, at packet 26. Time to `Playing` had never been measured
before part 2.

What question 4 changes. A developer who plays and tests on one account has to log in again at
every switch: logging in on the test path logs the usual game out (measured), and logging back
into the usual game is expected to end the test path's session, so that its next online start
returns to the login screen (the same rule, not exercised separately). Three ways out: accept the
switch, which the developer does for the spike; a second account reserved for tests, one more
licence; or a test client with no network, which never speaks to the studio but uses a key that
may have been replaced elsewhere, with its validation deliberately avoided. The last one is not
to be offered publicly without the studio's agreement.

## The sandbox recipe

One script, `sandbox.sh` (kept with the spike notes, not on the branch), runs the client with no
way to the desktop. In outline:

```
unshare --user --map-root-user --mount --pid --fork --mount-proc [--net]
  ulimit -c 0
  mount -t tmpfs tmpfs /tmp ; mkdir -m 1777 /tmp/.X11-unix
  mount -t tmpfs tmpfs /run/user/<uid>
  Xvfb -displayfd 3 -nolisten tcp -screen 0 1280x800x24     # display number read from fd 3
  env -i HOME=<run>/home TMPDIR=<run>/tmp XDG_RUNTIME_DIR=<run>/xdg PATH=/usr/bin:/bin
         DOTNET_ROOT=/usr/share/dotnet DISPLAY=<private display> LIBGL_ALWAYS_SOFTWARE=1
         ALSOFT_DRIVERS=null OPENTK_4_USE_WAYLAND=0 FONTCONFIG_FILE=<install>/fonts.conf
         LANG=C.UTF-8
    nice -n 10 timeout --signal=TERM --kill-after=15 <timeout>
      ./Vintagestory --dataPath <data> --logPath <run>/logs [--connect 127.0.0.1:<port> --pw <pw>]
```

What each piece is for:

- **User, mount and PID namespaces.** `--map-root-user` needs no privilege. The PID namespace
  means that when the sandbox's first process exits, the kernel kills every process left in it:
  the client, Xvfb, the crash reporter, any stray helper. Nothing needs sweeping afterwards.
- **A private tmpfs over `/tmp`.** The desktop's X11 sockets, which live there, become invisible.
  The single-instance pipe lands there too.
- **A private tmpfs over `/run/user/<uid>`.** The session bus, the Wayland socket and the audio
  sockets disappear with it.
- **A private Xvfb** with `-displayfd`, so that the display number is chosen by Xvfb and read
  back, never guessed, and `-nolisten tcp`. Nothing in the child's environment names any other
  display.
- **An empty environment plus an allowlist**, as in the recipe. `ALSOFT_DRIVERS=null` gives a
  silent OpenAL device, and `LIBGL_ALWAYS_SOFTWARE=1` forces llvmpipe.
- **`RLIMIT_CORE` 0**, set before anything starts. A core dump of a logged-in client holds its
  session key.
- **The network namespace is optional.** With `--net` the client has loopback only and cannot
  reach the auth service: it ends in Offline Mode. Part 1 question 5 and all of part 2 ran with
  the host's network (`NET=host`), since validation and the listener of the embedded host need
  it. The listener binds `127.0.0.1` in the host's namespace.
- When `SERVER_PORT` is set, the script also starts the stock dedicated server inside the
  sandbox, with `{ "VerifyPlayerAuth": false, "AdvertiseServer": false, "MaxClients": 4,
  "Ip": "127.0.0.1" }`, and passes `--connect` to the client. That is how part 1 question 6 ran.

The test harness of part 2 starts the script through `bash -c 'ulimit -c 0; exec ...'` as well,
removes the display, bus and runtime variables from the script's own environment, and never
touches the data path.

## Spike part 2: the real client in Atlas's own host

### The listener in the embedded host

The engine opens real listeners itself only for a dedicated server: in `AfterConfigLoaded` it
puts a `TcpNetServer` in `MainSockets[1]` and a `UdpNetServer` in `UdpSockets[1]`, and
`startSockets()` gives both the same address and port and starts them. The game's own `/allowlan`
command runs the same four calls on a single-player server, so that is the engine's own way to
open a non-dedicated one. Atlas's dummy players ride slot 0, so slot 1 is free for a real client
and both coexist.

`ClientListener` (spike branch, `src/Atlas/Internal/Hosting/ClientListener.cs`, plus a small
`ClientEndpoint` record) does exactly that, right after `server.Launch()`, behind an opt-in the
spike kept internal. It sets `Config.VerifyPlayerAuth` to false and `Config.Password` to 32 hex
characters from `RandomNumberGenerator`, probes a port that is free for TCP and UDP at once
(an ephemeral TCP port, then a UDP bind on the same number, retried up to 20 times), starts both
sockets on `127.0.0.1` and installs them in slot 1. `ServerHost` exposes host, port and password.
`ServerMain.Dispose` already disposes every socket slot, so teardown needs nothing.

All the types are public (`TcpNetServer`, `UdpNetServer`, `NetServer`, `ServerMain.MainSockets`,
`UdpSockets`, `Config`). No reflection and no shape probe were needed on 1.22.3. With an address
given, `ServerNetManager.StartServer` binds exactly that address, with no dual mode. Without one,
the engine binds every interface, which is why the explicit loopback address is the first guard.

Measured (`ss -ltnuH` during a run): `udp UNCONN 127.0.0.1:<port>` and `tcp LISTEN
127.0.0.1:<port>`, and no wildcard line for that port. Two dummy players joined and one `Say`
went through with the listener open. The configuration read from the game thread matched. With
the opt-in off there is no endpoint and no listener, and a dummy join behaves as before. After
`DisposeAsync` no socket stayed on the port. The host starts without opening anything by default.

The host's own pump already runs continuously, which is what a real-time client needs.

### The client joins: timeline

Eight runs are counted, and every figure is in seconds from the moment the `Vintagestory` process
appears. Five of the eight are repeats of one detailed run, the character-gate check, which spread
under 0.4 s. The single values below come from that run, and the 30 ms gap between the last two
rows was seen in it. The ranges cover all eight.

| Event | Seen at |
|---|---|
| Identification (the engine's "attempting identification" line) | +2.9 s |
| `PlayerJoin` (join request handled, packet 11) | +4.9 to +5.1 s |
| `PlayerNowPlaying`, level finalize done (packet 26) | +11.5 s |
| `Playing` (`ConnectionState` is `Playing`, packet 29) | +11.5 to +12.0 s, 30 ms after the line above in the detailed run |

The host booted 6.6 to 7.7 s before the client started, and up to about 15 s on one run with the
machine busy. On the client
side, `client-main.log` shows `Server validation response: Good` at start, the UDP connection on
loopback, the server's assets received (14,091 block types) and "Received level finalize" at
about +12 s. The client held 2.9 GB of resident memory at `Playing`. The scenario waited on the
server side only: engine events, the logger entry for identification, and a per-tick poll of
`IServerPlayer.ConnectionState`.

### The engine quirk: `PlayerReady` never fires for mods

On 1.22.3, `api.Event.PlayerReady` never fires for mods. `ServerMain.HandlePlayerReady` calls
`EventManager.TriggerPlayerReady`, but `CoreServerEventManager` overrides `TriggerPlayerJoin`,
`TriggerPlayerNowPlaying` and `TriggerPlayerLeave` to forward to the mod-level manager and does not
override `TriggerPlayerReady`. `ServerEventAPI.PlayerReady` subscribes to
`ModEventManager.OnPlayerReady`, which therefore stays silent. The spike's first run proved it: the
player was in state `Playing` when it disconnected and the event had not fired. `Playing` has to be
detected by polling `ConnectionState`, as Atlas already does for dummy players. Not checked on
1.22.7.

### The character gate

Without a bypass the client reaches `Connected` at +11.6 s and stops there for good. It never
reaches `Playing`: the "Customize Skin" dialog is open and the client withholds packet 29. One run
waited 115 s and saw identification, `PlayerJoin`, `PlayerCreate` and `PlayerNowPlaying`, but no
`Playing`. A screenshot of the dialog over the HUD, the minimap and the hotbar of the Atlas world
was taken.

What the code does (1.22.3 decompile of the survival mod and the engine):

- Server, `CharacterSystem.StartServerSide` registers a handler on `api.Event.PlayerJoin`. It reads
  `Deserialize<bool>(player.GetModdata("createCharacter"))`, sets class 0 with no gear when that
  is false, and sends `CharacterSelectedState { DidSelect }` on the channel `charselection`.
- Client, `CharacterSystem.Event_PlayerJoin` opens `GuiDialogCreateCharacter` and pauses the game
  when `didSelect` is false. `Event_IsPlayerReady` returns false until `didSelect`, with
  `handling = PreventSubsequent`. `GeneralPacketHandler.HandlePlayerData` sends packet 29 only when
  `TriggerIsPlayerReady()` is true. So the dialog is also what withholds `Playing`.
- No server config and no world setting reads `createCharacter` or skips the dialog. The only
  readers are the two sites above. `allowOneFreeClassChange` and `allowClassChangeAfterMonths` only
  gate re-selection.

**Why `PlayerCreate` is too late.** In `ServerMain.HandleRequestJoin`, `TriggerPlayerJoin(player)`
is raised near the start (the survival mod's handler runs in it), and `TriggerPlayerCreate` only at
the end, after `LevelFinalize`. A `PlayerCreate` handler would set the value after the mod had read
it and after the client had been told. Between identification and the join request no event
reaches a mod: `FinalizePlayerIdentification` raises none. A tick listener could set the value
there, but that is a race by design, and the spike did not use it.

**What works.** Mod handlers run in the order they registered (`OnPlayerJoin.GetInvocationList()`),
and registration happens in each mod's `StartServerSide`, ordered by `ExecuteOrder`. Atlas's bridge
mod `BridgeModsPreSystem` has `ExecuteOrder` -1,000,000 and already is the first mod hook. The spike
had it register one more `PlayerJoin` handler from `StartPre`, when the host has filled an
AppDomain data slot (`atlas.bridge.earlyJoin`), so that this handler sits first in the invocation
list, before the survival mod's. The host's handler sets
`createCharacter` to `SerializerUtil.Serialize(true)` only when
`Clients[player.ClientId].IsSinglePlayerClient` is false, that is for real connections only. The
whole change is about 40 lines, with no Harmony and no client mod.

Result, in the five runs of the check: the dialog never opens, the server polls `Playing` at +11.5 s, the client log
has no error and no warning. Screenshots show the loading screen at +11 s and the world with HUD,
minimap, hotbar, sky and superflat terrain at +16 s. The negative control, the same code with the
bypass off, shows the dialog again.

Side effects. The survival mod then skips `setCharacterClass`, so the real client has no
`characterClass` and the default skin. A mod that reads traits sees "no class", which the survival
mod treats as having every trait. A real tier may want to pick a class too. That the dummy players
stay untouched is established by the code (the `IsSinglePlayerClient` test), not by a run.

### Crash capture

A throwaway client mod, `SpikeCrashClientMod` (`Side = "Client"`, one dll, the mode named by the
folder it sits in, passed with `--addModPath`), throws 5 s after `LevelFinalize`. The server
accepted the client-only mod with no counterpart.

What the engine does (read): `ClientProgram` wraps its start in `CrashReporter.Start`.
`CrashReporter.Crash` writes `VSLastCrash.log` in `Path.GetTempPath()`, starts `VSCrashReporter`
(an Eto GUI) on the client's display, appends the text to `<logPath>/client-crash.log`, logs it as
Fatal, prints it to stdout, then calls `WindowExit("Game crashed", HardExit)`. Exceptions from
background threads reach the same method through `AppDomain.UnhandledException`.

Measured, times from `Playing`:

| Variant | Crash log appears | Process gone | Exit code | Server sees the disconnect | Reporter process |
|---|---|---|---|---|---|
| Exception in a renderer (Ortho stage), main thread | +5.1 s | 0.4 s after the log | 1 | within 0.04 s of the log | started, seen |
| Exception in a game tick listener, main thread | +5.6 s | same instant as the log | 1 | 0.4 s before the log | not sampled |
| Exception on a background thread | +5.05 s | 0.4 s after the log | 139 (SIGSEGV during the hard exit; no core, `RLIMIT_CORE` 0) | within 0.04 s of the log | started, seen |

- **Nothing hung.** No modal dialog, and the client was gone 0.4 s after the log. The code reading
  had predicted a hang for a crash off the main thread, waiting on a modal box; the runs did not
  show it. The supervisor's deadline stays, since other failures do hang.
- **Where the text lands**, all inside the run folder: `logs/client-crash.log` (the one to read),
  `logs/client-main.log` (a Fatal entry), `client.stdout`, and `tmp/VSLastCrash.log` in the client's
  private `TMPDIR` (absent in the background-thread run, not investigated).
- **The disconnect and the file come within 0.4 s of each other, in either order.** A scenario that
  sees the disconnect first should wait about 2 s for the file.
- **Content.** The line "Critical error occurred in the following mod: ..." with the mod's id and
  version, taken from the stack; the list of loaded mods; the stack trace with file information.
  The mod's own frame carries the PDB path of its source file, which is an absolute path under the
  home directory of whoever built it, and so holds the user name (once in each log). No uid and no
  player name appears in the text.
- **The exit code carries no signal**: 1, 139, 139 and 0 across the four runs, the last being the
  soft exit below. Detection rests on the file and the markers.
- **The reporter process.** `VSCrashReporter` is started and was seen as a descendant, but the
  sandbox ended its PID namespace right after the client exited (0.05 s later), so it never stayed
  on screen long enough to photograph. Outside a sandbox it would be an orphan GUI process on the
  client's display.
- **Poll cost.** Polling `/proc` every 50 ms for the process tree was fine.

**Stopping a healthy client.** SIGTERM to the exact PID, in game: the client logs "Exiting game
now ... SIGTERM or SIGINT received" and the process then ends in SIGSEGV, exit 139, in 5 of 5
in-game stops. The first three, before the core limit was set, each left a systemd-coredump file of
640 to 690 MB. The server sees the disconnect 0.1 to 17 s after the SIGTERM: 0.12 s and 0.17 s in
the two cleanest runs, 6 s twice, 17 s once, the client being slow to tear down on llvmpipe. SIGTERM
from a disconnect screen exits 0 in 0.24 s.

`ScreenManager.Platform.WindowExit(SoftExit)` called from a client callback is not a clean exit
either: it ends with a `NullReferenceException` in `GuiScreenRunningGame.RenderToPrimary` that the
crash reporter records as a crash (`client-crash.log`, "Game crashed", no mod attribution, exit code
0). **A soft exit can therefore write a false crash, and the crash detector must be disarmed before
any stop request.**

The stop ladder for the real tier: `ulimit -c 0`, SIGTERM, ignore the exit code and anything
written to `client-crash.log` after the stop request, SIGKILL after a bound. SIGKILL alone did not
damage the session in the one case measured (part 1, question 8), and the in-game SIGTERM stops
that ended in SIGSEGV did not damage it either (closing checks below).

### Rollback with a real client attached

- **A client that joined after the capture** is expelled: the server saw the disconnect 60 ms after
  the call, the call returned success in 0.8 s and the claims were released. The client stayed up on
  a disconnect screen showing Atlas's kick message ("Atlas world rollback: this player joined after
  the world snapshot was captured.") and exited 0 on SIGTERM. No crash.
- **A client present at the capture** is kept: same connection, state `Playing`, no dialog, still
  rendering, rollback done in 0.3 s. But the player's server-side position is not back at the
  captured one. The player had been teleported 40 blocks, and the position stayed at the teleported
  one 40 ms after the call and 8 s later. The likely reading is that the client owns its position
  and its next UDP position packets, every few ms, overwrite the restore, which sends no teleport
  packet to the client. The instant of the restore was not caught, so that is a reading, not a
  measurement. Inventory and watched attributes were not exercised.

So a real client is not confused in a harmful way, but it is not rolled back like a dummy player.
A real tier has to say that position is client-owned, and that a scenario class that rolls back
between scenarios must join its client before the first capture, or rejoin it after each rollback.

### Closing checks

After all the runs, a menu-only start with no `--connect` (and `RLIMIT_CORE` 0) logged "Cached
session key is valid, validating with server" then `Server validation response: Good`. Eleven
consecutive online validations were `Good` over part 2, none `Bad`, and the login screen never
appeared. Nothing was left running: no client, no Xvfb, no crash reporter, no extra listener.

## Identity leaks found

- **The player uid in the engine's identification line.** "Client N uid <uid> attempting
  identification" is mirrored to the console by Atlas, so the uid reaches test output. The spike
  piped every test run through a redaction script.
- **The player uid in inventory ids.** `client-debug.log` of every run carried it. It was replaced
  in the 17 local files concerned. It was never in the branch.
- **The home directory and user name in crash stacks**, as above, through the PDB file paths. The
  crash text is built with file information, so the paths of every mod loaded from a build
  directory would carry them.
- **Home paths in the spike branch.** The spike hard-coded two paths under the developer's home.
  The three commits were rewritten to derive them from the user profile and the branch was pushed
  again.
- **Atlas's own scratch** is deleted on a clean dispose (`tmp-tests/atlas` was empty after every
  run), so the account's playerdata row does not stay on disk. A host that had a real client must
  still refuse fixture harvest, and a retained scratch loses `Saves/` and `Playerdata/`.
- **Core dumps.** Five abrupt stops of the connected client, three in part 2 and two in part 1,
  before the sandbox set `RLIMIT_CORE` to 0, left core files of 640 to 690 MB in
  systemd-coredump's directory, with its default two-week retention. They are memory images of a
  logged-in client and so hold the session key. The developer deletes them by hand. Every run from
  the third on had the limit set, and `coredumpctl` lists the later SIGSEGVs with no file.

Two limits to write in the guide: the uid cannot be hidden behind an alias, and the text of a
user's own assertion cannot be redacted. The redaction has to cover paths as well as the name and
uid.

## Resources and auth service traffic

- Client memory: 3.2 to 3.4 GB (part 1), 2.9 GB resident in game (part 2).
- CPU: 350 to 980 percent on llvmpipe in spite of `maxFps` 15 (part 1). The July run measured 700
  to 1200 percent at a 30 fps cap. The cap only acts when `vsyncMode` is not 1, and the render loop
  only sleeps when `MaxFps` is between 10 and 241.
- Host boot 6.6 to 7.7 s, client at `Playing` 11.5 to 12.0 s after its own start.
- Auth service, part 2: 11 client starts (10 joins and one menu-only check), so 11 `clientvalidate`
  and 10 `clientrequestmptoken` calls in about 25 minutes, all `Good`, with no rate-limit symptom.
  The behaviour under a rate limit is unknown.

## Open points

**Terms of use.** Nobody has read the game's terms: no text of them is on the machine and none was
fetched. This document describes behaviour, not a legal reading.

What the design does: it starts the unmodified client from the developer's install; the developer
logs in with their own account on the game's own screen; later starts pass the game's own session
check; the embedded server runs with `VerifyPlayerAuth` off, a standard server setting, on
loopback. What it does not do: modify, intercept or replace login or session code; read, copy or
move the session; present a false identity to the studio; put a session anywhere but on the
machine where its owner logged in.

What it does anyway, and the developer has to know. It creates a second logged-in path for one
account, so a second copy of an account credential sits on disk. Every start sends a
`clientvalidate`, every join a `clientrequestmptoken`, plus two anonymous requests. A test loop
calls the service much more often than a player, and one "invalid" answer disconnects the path.
The maintainer decided not to ask the studio first: the tier only starts the developer's own
unmodified client with their own account, through the game's own login. The documentation says
what the tier does with that account, so each developer decides for themselves. The client with
no network stays out of the tier, since it would skip the studio's online check.

**One session per account.** The conflict and its three ways out are above, under part 1.

**Unknown without the studio:** what makes `clientvalidate` answer "invalid", whether `gamelogin`
issues one key per account or per login, and whether `gamelogout` ends one session or all.

**Not measured:** a 200 answer that is not JSON (part 1, question 11); a paused game thread or a
long rollback with a real client attached, where an expulsion on ping at 150 s is read from the code
(question 12); the client's own `SaveScreenshot` from inside the client (the sandbox took X screenshots
of the private display, which works, but the engine-side capture needs the bridge mod); input,
alias, and the state of a mod's client side; any engine other than 1.22.3 run live; Windows and
macOS.

## Lots

A lot is a focused pull request with its tests and docs, about three working days.

| Piece | Content | Low | High | In the smoke tier |
|---|---|---|---|---|
| Listener | TCP and UDP in loopback in slot 1 after `Launch()`, `VerifyPlayerAuth` false, random password, a chosen port, the host passed as `127.0.0.1:<port>` (never `vintagestoryjoin://`, which opens a confirmation screen) | 1 | 2 | yes |
| Launcher and supervisor | The stock apphost with fixed arguments, a state machine over log markers with its own deadline, crash detection by `client-crash.log`, the stop ladder | 2 | 2 | yes |
| Display guard | Private Xvfb, namespaces, environment allowlist, guardian, sweep | 2 | 2 | yes |
| `atlas client login`, `check`, `logout` | The type that owns the path, sentinel, seed, lock, marker | 1 | 1 | yes |
| Skip logic | The ladder as a result object, the third outcome, `Category=AtlasClient` | 1 | 1 | yes |
| Client bridge mod | `Side = "Client"`: wait in game, dialogs, hotkeys, chat, capture | 2 | 3 | no |
| Test API | `ITestClient`, `JoinRealClient`, `[AtlasRealClient]`, role, timeouts, `ClientCrashedException` | 1 | 2 | a slim part |
| Identity guards | Redaction of name, uid and home paths, refusal of `atlas fixture`, cleaning of retained scratch, name alias (the high figure) | 1 | 2 | the redaction |
| Docs | Guide page, one example scenario, the ADR | 1 | 1 | yes |
| **Total, pre-spike estimate** | | **12** | **17** | |

The pre-spike estimate's own sum for the five pieces it priced before docs, the slim test API and
the redaction (listener, launcher and supervisor, display guard, login commands, skip logic) is 7
to 8. The "about 4 lots" quoted before the spike does not follow from those figures. After the
spike, the corrected estimates are **6 to 7.5 lots for the smoke tier and 11 to 15.5 for the full
tier**. They are a correction made after the
spike and were not measured. The spike's concrete effect: the listener needs no shape probe on 1.22.3, and the
character gate is a 40-line handler in the existing bridge, so neither is a surprise to price.

The smoke tier is `world.JoinRealClient()` waiting for `Connected` plus level finalize, then
`client.AssertNoCrash(after: N s)` returning the `client-crash.log` and the engine's start-up
warnings block. The scenario prepares the world on the server side with the existing `ITestPlayer`.
To open a specific dialog, a mod author loads their own small client test mod of about ten lines.
What drops: the bridge, the command surface of `ITestClient`, captures, the alias, and the
character gate bypass, since a crash on join or on HUD composition needs no more than
`Connected`. That covers the need that started it all, the client GUI crash caused by an inventory
expansion. Pixels and visuals wait for the bridge. Waiting for a later version: synthetic keyboard
and mouse input, reading a mod's own client state (the Manifold and Chart need in the issue
comment), capture sequences, rollback with a live client, engines older than 1.22, several clients,
a visible window on request. Not possible: CI, real GPU fidelity, deterministic control of ticks
(the client runs in real time, so assertions poll), a client with no owned account, audio, a hidden
window on native Windows or macOS.

Upkeep, not in the table: the tier rests on about six engine log strings and on the slot 1 shapes,
and each engine minor needs a local run by someone logged in. Half a lot to one lot per minor, an
estimate. Not built: a display-type interface, a user-supplied display, a Windows or macOS route.

The code starts after 0.16.0 is released. All of it is additive (a new API, new CLI verbs,
possibly a new project) and belongs to 0.17.0 or later.

## The throwaway branch

`spike/client-own-login` holds three commits on top of `main`: the loopback listener (step A), the
real client joining the host with the character gate bypass and the crash measurement harness (steps
B, C and D), and the stop modes and rollback with a client attached (steps D and E). It adds a
`spike/Atlas.ClientSpike` test project and a `spike/SpikeCrashClientMod` project, neither part of
`Atlas.slnx`, an `InternalsVisibleTo` line, and about 190 lines in `src/` (the listener, its
endpoint record, the host's opt-in and handler, the bridge's early registration). It is a record of
what was run, kept for reading. It is never merged and never released.

## Engine references

Decompiled `VintagestoryLib` and the survival mod, kept outside this repository. Line numbers are
those of the 1.22.7 decompile used for the code reading, and of the 1.22.3 decompile where the text
says so.

- `SessionManager` (1.22.7): the local check at 19 to 40, the online answer and the wipe at 58 to
  72, logout at 202 to 212.
- `ScreenManager` (1.22.7): the login gate at 276 to 285, the validation response line at 364,
  `--connect` at 581 to 584 and 992 to 1013.
- `ClientProgram` (1.22.7): `--dataPath` at 69 to 72, the single-instance pipe at 95 to 103.
- `ClientSettings` and `SettingsBase` (1.22.7): where the session is stored and how the file is saved.
- `ServerMain` (1.22.7): the slot 1 sockets at 1408 to 1411, the password and authentication skip at
  916 and 959 to 964. `HandleRequestJoin` (1.22.3): 447 to 512, `PlayerJoin` at 477, `PlayerCreate`
  at the end, 496 to 499.
- `CharacterSystem` (1.22.3): server join handler at 578 to 584, client `Event_PlayerJoin` at 462,
  `Event_IsPlayerReady` at 488, and `GeneralPacketHandler.HandlePlayerData` at 246.
- `ServerHost.Pump` in Atlas, `main`: the continuous pump that serves a real-time client.
