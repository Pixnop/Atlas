# 0012. A real client for tests runs on the developer's own login

Status: proposed (`docs/specs/2026-10-03-real-client-own-login.md`). Nothing here is in `main`:
the spike code lives on the throwaway branch `spike/client-own-login`, which is never merged,
and the real code starts after 0.16.0 is released. Two questions below, the studio's terms of
use and the one-session-per-account conflict, are open. They block a public release of the
tier, not the first local steps.

## Context

Atlas boots a real headless server inside `dotnet test` and joins dummy players to it. It has
no client, so nothing in a client GUI or renderer can be tested. Three mod authors asked for a
real client within one day: the pixels of a map mod, a client GUI crash caused by an inventory
expansion, and the visuals of a weapon mod that are checked by eye (issue #164, tier 1 of #100).

Two earlier studies ended without a way in. In July a real client subprocess on a virtual
display was measured viable (`2026-07-17-client-side-testing.md`, path B), with one obstacle:
the client honours `--connect` only when its data path holds a cached session key that passes
the engine's own check, an RSA signature plus a non-empty uid with no date. The ways past it
were a Harmony patch on the login check, which needs the studio's approval, and a supported
offline switch, which upstream issue 10012 asked for and which was closed as not planned. In
September an in-process client built outside `ScreenManager` failed inside `ClientMain.Start()`
on three missing pieces of state, with no end to the list in sight
(`2026-09-23-client-path-c.md`).

The owner's idea removes the obstacle instead of passing it. If the developer logs in once on a
dedicated data path, through the game's own login screen, the unmodified client passes its own
check legitimately on every later start. Nothing is patched and nothing is copied.

That sentence hides a cost. The session key is an account credential, not a play token: read
from the 1.22.7 engine, it also authorises `gameserverctrl` actions on a hosted server of the
account, such as deleting every mod. Every part of the design below is shaped by it.

A spike on 2026-10-03 (1.22.3, Linux, one machine) ran the idea for real. The stock client
joined Atlas's embedded host and reached `Playing` 11.5 to 12.0 s after its process appeared.
A client exception was captured with its log. It also showed that logging in on the test data
path logged the developer's own game out.

## Decision

The developer logs in, once, by hand, on a data path dedicated to Atlas's client tests.

`atlas client login` creates that path, marked with an Atlas sentinel, and starts the stock
client on it on the developer's desktop after asking. The developer logs in through the game's
login screen and quits through the main menu. `atlas client check` then starts the client
offscreen and waits for `Server validation response: Good` or `Offline`, and only then writes an
Atlas-owned `ready` marker next to the data path. The line `Cached session key is valid` is not
enough: it reports the local signature only.

From then on Atlas starts the unmodified client from the developer's install, on that data path,
inside a sandbox: private user, mount and PID namespaces, private `/tmp` and `/run/user`, a
private Xvfb display, an environment built from an allowlist, core dumps disabled, and every
process killed with the sandbox. The real display and the session bus are never reachable. The
client connects with `--connect` to a loopback listener Atlas opens on the embedded host
(`VerifyPlayerAuth` off, random password).

Atlas never opens, reads, parses, copies, moves, archives, prints or logs `clientsettings.json`,
its `.bkp` or its `.tmp`. It writes that file once, as a seed of non-secret keys with
`FileMode.CreateNew`, before any login exists. It accepts only a data path it created itself, so
it cannot be pointed at the game's own data folder. Whether a login exists is learned from the
client's log, under a `--logPath` Atlas controls.

Client scenarios are local only. A CI runner has no logged-in data path, and no session is ever
to go into a CI secret, so Atlas skips the scenarios there with a message that says why. The
same skip, with its own remedy, covers a missing login, platform, install, .NET runtime, Xvfb or
lock. Infrastructure trouble during a run (a session answered invalid, a validation that hangs,
a refused multiplayer token) is a third outcome, a skip with a distinct message and not a test
failure. `ATLAS_CLIENT=required` turns every skip into a failure.

Linux first. Other platforms skip.

The first deliverable is the client smoke tier, with no Atlas code inside the client:
`JoinRealClient()` waits for `Connected` plus level finalize, and `AssertNoCrash` returns the
`client-crash.log` and the engine's start-up warnings. The host gains the loopback listener, and
an early `PlayerJoin` handler that lets a real connection past the survival mod's character
dialog. A client bridge mod (dialogs, hotkeys, screenshots) is a later, separate decision.

## Consequences

- No engine code is patched and no credential is moved by Atlas. The cost is a second logged-in
  data path for the same account, which doubles the exposure of that credential to backups,
  sync tools and dotfile repositories. Mode 0700 does not help there. The key sits in the
  `.json`, in the `.bkp` (the previous copy) and briefly in the `.tmp`, and after a logout the
  old key stays in the `.bkp` until the next save.
- The guard covers Atlas's code only. The mod under test and everything staged with
  `--addModPath` run inside the logged-in process, where the key is a public static. That is
  the exposure of playing with the mod, and the guide has to say so.
- A core dump of the client holds the key. The sandbox sets `RLIMIT_CORE` to 0. Before it did,
  five abrupt stops of a connected client left systemd-coredump files of 640 to 690 MB each.
- One session per account, measured: logging in on the test data path logged the developer's
  own game out. The reverse follows from the same rule and was not exercised separately. A
  developer who plays and tests on one account logs in again at every switch. The three ways out
  are to accept the switch, to use a second account reserved for tests (one more licence), or to
  run the test client offline, which is the last one and only with the studio's agreement.
- Deleting the data path does not log out. Only the game's own logout link posts `gamelogout`,
  so `atlas client logout` has to open the game for the developer to click it.
- Every client start calls `clientvalidate` and every join calls `clientrequestmptoken`. A test
  loop calls the studio's auth service far more often than a player does, and one answer of
  "invalid" wipes the key from the data path. The spike made 11 validations and 10 token requests
  in about 25 minutes with no rate-limit symptom. That is one data point, not a limit.
- Nearly every failure leaves the client alive on some screen, and its exit code says nothing (1,
  139 or 0 across the spike's runs). The supervisor therefore needs its own deadline over log
  markers, and the client must die with the host.
- The developer's identity enters the run. The player uid is printed in the engine's
  identification line and sits in inventory ids of the client's debug log, and the crash text
  carries the home directory of whoever built the mod. All of it is redacted from anything Atlas
  prints or throws, and a host that had a real client refuses fixture harvest. An alias cannot
  mask the uid, and the text of a user's own assertion cannot be redacted.
- The real client gets no character class and the default skin, because the dialog is skipped.
- None of this can be tested in CI. The tier rests on about six engine log strings and on the
  shapes of the engine's slot 1, so each engine minor needs a local run by someone logged in:
  half a lot to one lot per minor, an estimate, to be written into the release rite.
- Linux only at first. Native Windows has no hidden window and a global single-instance pipe.
  WSL2 works only with a separate Linux client install and a separate login inside WSL.

## Alternatives considered

- **A Harmony patch on the login check.** What the July experiment used. It is a patch on the
  login code of a commercial client and needs the studio's approval first. Rejected.
- **Copying the developer's logged-in settings into per-run data paths.** Moves an account
  credential around and multiplies the copies, one per run. Rejected.
- **An in-process client** (the stub platform of path A, the direct `ClientMain` of path C).
  Weeks of work and a treadmill against every release for path A, and path C does not get past
  `ClientMain.Start()`. Rejected, for the reasons in the two earlier specs.
- **Asking the studio for a supported switch.** Already tried upstream and closed as not
  planned. The question is still worth asking in another shape (see below), but the design does
  not wait for an answer.
- **An offline client.** The engine has an offline mode: with no network the client of the test
  data path checks its key locally, reports "Offline Mode" and reaches the main menu, and a
  server with `VerifyPlayerAuth` off accepts it. It would end the one-session conflict and all
  traffic to the auth service. It also uses a key that may have been replaced elsewhere, with
  validation deliberately avoided. Not offered without the studio's agreement.
- **A proxy for the auth service**, to answer the validation locally. That sidesteps the
  studio's online check by a trick. Rejected.
- **`--rndWorld` with no Atlas server.** No token call and no identity in the artifacts, but the
  scenario cannot prepare the world and the client writes saves into the persistent path.
  Rejected in favour of the variant with a connection.

## Open questions

- The game's terms of use have not been read. Atlas would be telling third-party developers to
  automate their own licensed accounts, so the studio should answer before the tier is released
  publicly. A forum question is drafted. It does not block local work.
- Which way out of the one-session conflict the public documentation recommends.
- Unknown without the studio: what makes `clientvalidate` answer "invalid", whether `gamelogin`
  issues one key per account or per login, and whether `gamelogout` ends one session or all.
- Not measured: a non-JSON 200 answer from the validation (the design expects a hang that the
  deadline catches), a paused game thread or a long rollback with a real client attached (an
  expulsion on ping at 150 s is read from code), any engine other than 1.22.3 run live.
- Size. The smoke tier is estimated at 6 to 7.5 lots and the full tier at 11 to 15.5, neither
  measured. A lot is a focused pull request with its tests and docs, about three working days.

## Source files

Engine, 1.22.7 decompile kept outside this repository, symbols as read there:

- `SessionManager`: the local session check (`IsCachedSessionKeyValid`), the online answer and
  the key wipe, logout.
- `ScreenManager`: the login gate in `DoGameInitStage2`, the validation response line, the
  `--connect` handling.
- `ClientProgram`: `--dataPath`, the single-instance pipe.
- `ServerMain`: the slot 1 sockets, the password check and the authentication skip,
  `HandleRequestJoin` (where `PlayerJoin` and `PlayerCreate` are raised).

Atlas, on the throwaway branch `spike/client-own-login` only:

- `src/Atlas/Internal/Hosting/ClientListener.cs`: the loopback listener in slot 1.
- `src/Atlas/Internal/Hosting/ServerHost.cs`: `LetRealClientPastCharacterGate`.
- `src/Atlas.Bridge/BridgeModsPreSystem.cs`: the early `PlayerJoin` registration.

In `main`:

- `src/Atlas/Internal/Hosting/ServerHost.cs`: `Pump`, the continuous game-thread pump that also
  serves a real-time client.
- `docs/specs/2026-07-17-client-side-testing.md`, `docs/specs/2026-09-23-client-path-c.md`: the
  earlier studies this record supersedes in part.
