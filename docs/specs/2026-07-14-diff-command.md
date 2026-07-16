# atlas diff: differential comparison of two TRX runs

Date: 2026-07-14
Status: implemented (issue #88, 0.10.0 roadmap)
Consumer: differential CI pipelines (the StratumParity pattern: run one suite against vanilla
and against a fork, then gate on what changed); any tool that wants a machine-readable
comparison can use `--json`.

## Command

```
atlas diff <baseline.trx> <candidate.trx> [--json] [--json-tests]
```

Compares the candidate run against the baseline run and reports what changed. Pure file
comparison: no server boots, no scenario assembly is loaded, `VINTAGE_STORY` is not required.

## Input

Both inputs are TRX reports. The reader round-trips everything Atlas itself writes
(`atlas run --parallel --trx`, whose aggregate covers plain `atlas run` results too) and
tolerates any spec-conforming TRX, including plain `dotnet test --logger trx` output: only
the `TestRun/Results/UnitTestResult` elements are read, extra elements (TestSettings,
TestDefinitions, ResultSummary) are ignored, and children resolve in the root's own namespace
so a report that dropped the TeamTest 2010 namespace still reads. A file whose root element is
not `TestRun` (or that is not XML, or cannot be opened) is a usage error: exit 2.

Per result, the diff reads `testName` (the identity), `outcome`, `duration`,
`Output/ErrorInfo/Message` and `Output/StdOut` (the captured console output; read unconditionally,
but only surfaced through `--json-tests`, see below). Results without a `testName` have no
identity to diff by and are dropped. The schema's outcome values fold onto three kinds:

| TRX outcome | Kind |
|---|---|
| Passed, PassedButRunAborted | passed |
| Failed, Error, Timeout, Aborted, Disconnected | failed |
| everything else (NotExecuted, Inconclusive, Pending, ..., missing, unknown) | skipped |

## Test identity

Comparison is keyed by the full `testName` exactly as the TRX reports it. Theory rows carry
their arguments in the name (`Ns.Class.Method(row: 2)`), so every row diffs on its own.
Duplicate names inside one report (rerun tooling writes one result per attempt) are merged
before diffing: the worst outcome wins (failed over passed over skipped), and among equal
outcomes the longer duration and the first available message are kept. A test that failed any
attempt therefore counts as failed. Stdout follows the kept result outright: whichever attempt
wins the merge (by outcome, or the first attempt on a tie) is the one whose `Output/StdOut` is
reported, with no fallback to the losing attempt's stdout the way the message falls back.

## Categories

Disjoint, each sorted by test name:

| Category | Definition |
|---|---|
| new failures | failed in candidate; passed, skipped or absent in baseline |
| fixed | failed in baseline; passed in candidate |
| vanished | present in baseline; absent from candidate (whatever its baseline outcome) |
| new tests | absent from baseline; passed or skipped in candidate (a new test that fails is a new failure instead) |
| still failing | failed in both runs |
| duration shifts | passed in both runs; notable duration change (below) |

Skips are neutral: a pass or failure that becomes skipped lands in no category (deliberately,
a skip carries no verdict; silencing a failure by skipping it is not "fixed"), and a skipped
test that starts failing is a new failure. If a "now skipped" category proves needed in the
field, it can be added additively.

## Duration shifts

Duration noise is real (JIT, disk cache, CI neighbors), so the rule is conservative and
requires BOTH conditions on a test that passed in both runs:

- the slower run took at least **2x** the faster one, and
- the absolute difference is at least **500 ms**.

A result without a parseable `duration` on either side is never a shift. Both directions are
reported (slower and faster), tagged with the direction; shifts are informational and never
affect the exit code.

## Exit codes

A **regression** is a new failure or a vanished test, nothing else.

| Code | Meaning |
|---|---|
| 0 | the candidate has no regressions (fixed/new/still-failing/shifts may all be non-empty) |
| 1 | at least one regression (new failure or vanished test) |
| 2 | usage or IO error (missing argument, unreadable file, not XML, root is not TestRun) |

## Console output

Plain text, like every other atlas output: the two inputs with their (merged) test counts, a
one-line summary counting every category, one compact listing per non-empty category (empty
categories print nothing), and the regression verdict. New failures carry the first line of
the failure message; the full message and stack live in the TRX.

## JSON output (v1)

`--json` replaces the console listing with one machine-readable JSON document on stdout,
versioned with the worker protocol's discipline:

- `v` (currently `1`) comes first, so a consumer can dispatch before reading any other field.
- `v` is bumped when an existing field changes meaning or disappears.
- New fields may be added WITHOUT bumping `v`: consumers must ignore unknown fields.
- Every category key is always present; empty categories are empty arrays, they never
  disappear. `message` on a new failure is the only optional field (omitted when the TRX
  carries none).

```json
{
  "v": 1,
  "baseline": { "path": "baseline.trx", "tests": 6 },
  "candidate": { "path": "candidate.trx", "tests": 6 },
  "counts": { "newFailures": 1, "fixed": 1, "vanished": 1, "new": 1, "stillFailing": 1, "durationShifts": 1 },
  "regressions": true,
  "exitCode": 1,
  "newFailures": [ { "test": "Ns.A.Breaks", "baseline": "passed", "message": "Assert.Equal() Failure" } ],
  "fixed": [ { "test": "Ns.A.GetsFixed" } ],
  "vanished": [ { "test": "Ns.A.Vanishes", "baseline": "passed" } ],
  "new": [ { "test": "Ns.A.Appears(row: 1)", "outcome": "passed" } ],
  "stillFailing": [ { "test": "Ns.A.KeepsFailing" } ],
  "durationShifts": [ { "test": "Ns.A.SlowsDown", "baselineMs": 200, "candidateMs": 1400, "direction": "slower" } ]
}
```

Field notes: `baseline`/`candidate.tests` count distinct test names after duplicate merging;
`baseline` on a new failure is `"passed"`, `"skipped"` or `"absent"`; `baseline` on a vanished
test is `"passed"`, `"failed"` or `"skipped"`; `outcome` on a new test is `"passed"` or
`"skipped"`; `direction` is `"slower"` or `"faster"`; `exitCode` repeats the code the process
is about to return (0 or 1; a document is never emitted on exit 2).

## Per-test listing (`--json-tests`)

`--json-tests` is an opt-in flag that implies `--json` (it works even without `--json` on the
command line) and adds one more field to the document: `tests`, an array with one entry per
merged test identity from either run. Additive under the same evolution rules as the rest of
the document, but unlike the category keys it is not always present: omitted entirely (not an
empty array) unless the flag is given, so the default `--json` payload is unchanged. It exists
for differential pipelines (StratumParity's markdown job summaries and history dashboard) that
need outcome, duration and stdout per test instead of hand-parsing the TRX themselves.

```json
{
  "v": 1,
  "...": "...",
  "tests": [
    {
      "test": "Ns.A.T",
      "baseline": { "outcome": "passed", "durationMs": 10 },
      "candidate": { "outcome": "failed", "durationMs": 12 },
      "stdout": "booting the guinea pig world..."
    },
    { "test": "Ns.A.Only", "baseline": { "outcome": "passed", "durationMs": 7 }, "candidate": null }
  ]
}
```

Field notes: `baseline`/`candidate` are each `{ "outcome": "passed"|"failed"|"skipped",
"durationMs": <number>|null }`, using the same merge as the category diff (duplicate names
merged worst-outcome-first, see Test identity above); `null` instead of an object means the
test is absent from that side entirely. `durationMs` is `null` when the TRX carries no
parseable duration, independent of the outcome. `stdout` is present only when the candidate's
merged result carries a captured `Output/StdOut` (empty string is a valid, present value,
distinct from an absent `StdOut` element); it is never sourced from the baseline side, and it
is omitted (not `null`) when the candidate has none or the test is absent from the candidate.

## Non-goals (v1)

Deliberately out of scope, proposable as additive extensions: threshold flags for the
duration rule, a "now skipped" category, HTML or markdown output, comparing more than two
runs, and reading formats other than TRX.
