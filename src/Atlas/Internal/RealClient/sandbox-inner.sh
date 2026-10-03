#!/usr/bin/env bash
# The inside of the real-client sandbox: runs as PID 1 of fresh user, mount, IPC and PID namespaces
# (ClientSandbox starts it through unshare, see its design comment for the guarantees and their
# limits). Configuration arrives in ATLAS_SB_* variables; the client's environment and arguments
# arrive as parameters: KEY=VALUE pairs, then "--", then the arguments. It runs in two stages,
# the second being this same file started again:
#   1. With the capabilities of a user namespace's root: the run folder as working folder, the
#      private mounts, the loopback, the log redirections. Nothing here starts a program of the
#      client's side.
#   2. With none (setpriv empties every capability set and sets no_new_privs, and the stage
#      checks that it worked): the private Xvfb, the screenshot loop, the client and the
#      guardian. Without CAP_SYS_ADMIN nothing in here can unmount what stage 1 mounted, which is
#      what keeps the host's /tmp, /dev/shm and /run/user/<uid> out of reach of the client and of
#      any code that runs in it.
# It refuses to go on at the first sign that a mount, the capability drop or the display is not
# what it must be: running the client on the host's /tmp or display is the one outcome this
# sandbox exists to prevent.
set -u

RUN=${ATLAS_SB_RUN:?}
INSTALL=${ATLAS_SB_INSTALL:?}
PROGRAM=${ATLAS_SB_PROGRAM:?}
XVFB=${ATLAS_SB_XVFB:?}
SETPRIV=${ATLAS_SB_SETPRIV:?}
IMPORT=${ATLAS_SB_IMPORT:-}
HOST_UID=${ATLAS_SB_UID:?}
TIMEOUT=${ATLAS_SB_TIMEOUT:?}
KILL_AFTER=${ATLAS_SB_KILL_AFTER:?}
SHOTS=${ATLAS_SB_SHOTS:-0}
ISOLATE_NET=${ATLAS_SB_ISOLATE_NET:-0}
STAGE=${ATLAS_SB_STAGE:-1}

# Must stay the same as SandboxPlan.CapabilityDropFlags, which the availability probe uses (a
# test compares them).
DROP=("$SETPRIV" --no-new-privs --bounding-set=-all --inh-caps=-all --ambient-caps=-all)

note() { printf '%(%H:%M:%S)T %s\n' -1 "$*"; }
die() { note "sandbox refused to start: $*"; exit 70; }

if [ "$STAGE" = 1 ]; then
  # First of all, leave the host's working folder: every process of the sandbox inherits this
  # one, and /proc/<pid>/cwd of a process that kept a folder under the host's /tmp or
  # /run/user/<uid> opens that folder past the private mounts below.
  cd "$RUN" || die "cannot enter the run folder"

  # The host's end of the guardian pipe is this script's stdin: keep it on fd 4 and give nothing
  # else a stdin to read. Both redirections outlive the exec into stage 2.
  exec 4<&0 0</dev/null
  exec >>"$RUN/sandbox.log" 2>&1

  # A core dump of a logged-in client would hold its session key: never write one.
  ulimit -c 0

  mount -t tmpfs tmpfs /tmp || die "cannot mount a private /tmp"
  [ -z "$(ls -A /tmp)" ] || die "/tmp is not the private tmpfs"
  mkdir -m 1777 /tmp/.X11-unix || die "cannot create /tmp/.X11-unix"
  # The desktop's shared memory (the POSIX files of /dev/shm; the SysV segments are left out by
  # the IPC namespace): nothing of it may be readable, and nothing the sandbox writes there
  # may reach the host.
  if [ -d /dev/shm ]; then
    mount -t tmpfs tmpfs /dev/shm || die "cannot mount a private /dev/shm"
    [ -z "$(ls -A /dev/shm)" ] || die "/dev/shm is not the private tmpfs"
  fi
  # No session dir, no session bus, Wayland or audio socket to hide.
  if [ -d "/run/user/$HOST_UID" ]; then
    mount -t tmpfs tmpfs "/run/user/$HOST_UID" || die "cannot mount a private /run/user/$HOST_UID"
  fi
  if [ "$ISOLATE_NET" = 1 ]; then
    ip link set lo up || note "loopback is down in the isolated network namespace"
  fi

  "${DROP[@]}" true || die "cannot drop the capabilities"
  ATLAS_SB_STAGE=2 exec "${DROP[@]}" bash "$0" "$@"
fi

# This shell's own sets, not grep's: /proc/self would name the program that reads it.
for field in CapInh CapPrm CapEff CapBnd CapAmb; do
  grep -Eq "^$field:[[:space:]]+0+\$" "/proc/$$/status" || die "stage 2 still holds capabilities ($field)"
done

exec 3>"$RUN/display"
"$XVFB" -displayfd 3 -nolisten tcp -screen 0 1280x800x24 4<&- >"$RUN/xvfb.log" 2>&1 &
XVFB_PID=$!
for ((i = 0; i < 100; i++)); do
  [ -s "$RUN/display" ] && break
  kill -0 "$XVFB_PID" 2>/dev/null || break
  sleep 0.1
done
exec 3>&-
DISPLAY_NUMBER=$(head -1 "$RUN/display")
case $DISPLAY_NUMBER in
  '' | *[!0-9]*) die "Xvfb did not report a display number (see xvfb.log)" ;;
esac
[ -S "/tmp/.X11-unix/X$DISPLAY_NUMBER" ] || die "display :$DISPLAY_NUMBER has no socket in the private /tmp"
D=":$DISPLAY_NUMBER"
note "display $D, isolated network $ISOLATE_NET, ceiling ${TIMEOUT}s, no capabilities"

if [ -n "$IMPORT" ] && [ "$SHOTS" -gt 0 ]; then
  (
    exec 4<&-
    n=0
    while :; do
      sleep "$SHOTS"
      n=$((n + 1))
      "$IMPORT" -display "$D" -window root "$RUN/shots/$(printf '%04d' "$n")-$(date +%H%M%S).png" 2>/dev/null || true
    done
  ) &
fi

CLIENT_ENV=()
while [ $# -gt 0 ] && [ "$1" != -- ]; do CLIENT_ENV+=("$1"); shift; done
[ $# -gt 0 ] && shift

cd "$INSTALL" || die "cannot enter the install folder"
note "client start"
nice -n 10 timeout --signal=TERM --kill-after="$KILL_AFTER" "$TIMEOUT" \
  env -i "${CLIENT_ENV[@]}" "DISPLAY=$D" "$PROGRAM" "$@" \
  </dev/null 4<&- >"$RUN/client.stdout" 2>&1 &
CHAIN=$!

# The guardian. The host holds the write end of the pipe on fd 4; when it closes it, by stopping
# the sandbox or by dying (SIGKILL included), this read returns and the client is asked to stop:
# timeout forwards the SIGTERM to the client's process group and follows with SIGKILL after
# KILL_AFTER seconds. Armed after the client started, so a host that died during start-up is
# caught as well. When the client chain ends, this script ends, and the kernel kills what is
# left of the PID namespace (Xvfb, the screenshot loop, anything the client left behind).
( read -r _ <&4; note "guardian: the host closed the pipe, stopping the client"; kill -TERM "$CHAIN" 2>/dev/null ) &

wait "$CHAIN"
CODE=$?
note "client exit $CODE"
exit "$CODE"
