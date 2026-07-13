# Worker JSONL protocol (v1)

Date: 2026-07-06
Status: implemented (stage 1 of [parallel scenario execution](2026-07-06-parallel-scenarios.md))
Consumer: the stage 2 orchestrator (`atlas run --parallel N`); any tool that wants
machine-readable Atlas results can use it too.

## Transport

- `atlas run <dll> --worker [--classes A,B] [--filter <substring>]` runs the given scenario
  classes (comma-separated fully qualified names, exact match; absent = the whole assembly)
  in one process, sequentially, exactly like plain `atlas run` otherwise.
- `atlas run <dll> --list --worker` performs discovery only: no server boots and
  `VINTAGE_STORY` is not required.
- Worker stdout carries EXCLUSIVELY protocol events: one JSON object per line, UTF-8,
  flushed after every line. The worker reroutes the process console (which the embedded
  server logs to) to stderr, so stderr carries all human and engine chatter and is the place
  to look for forensics.
- Every line is a complete JSON object; the stream never ends mid-line. A stream whose last
  line is not `run-end` means the worker process died: the orchestrator must treat the
  worker's in-flight class as failed.
- If the worker exits before emitting anything (usage error, assembly not found), stdout is
  empty, stderr carries the reason, and the exit code is 2.

## Versioning

Every line carries `v` (currently `1`) and `type` as its first two fields, so a consumer can
dispatch on them before reading any payload field.

- `v` is bumped when an existing field changes meaning or disappears.
- New fields and new event types may be added WITHOUT bumping `v`: consumers must ignore
  unknown fields and unknown event types.

## Exit codes

Same contract as plain `atlas run`:

| Code | Meaning |
|---|---|
| 0 | every scenario passed and at least one ran |
| 1 | at least one failure or runner error, or nothing ran (empty run, unknown class) |
| 2 | environment or usage error (`VINTAGE_STORY` missing, bad arguments) |

The final `run-end` line repeats the exit code the process is about to return.

## Events

### `run-start` (run mode only, always first)

| Field | Type | Meaning |
|---|---|---|
| `assembly` | string | Full path of the scenario assembly under execution |
| `classes` | string[] or null | The assigned class FQNs; null = whole assembly |
| `pid` | number | Worker process id |
| `atlasVersion` | string | Version of the atlas CLI emitting the stream |

### `discovered` (list mode only, one per scenario)

| Field | Type | Meaning |
|---|---|---|
| `class` | string | Fully qualified scenario class name |
| `test` | string | Scenario display name, as `dotnet test` would report it |

### `class-start`

| Field | Type | Meaning |
|---|---|---|
| `class` | string | Fully qualified scenario class name (its server boots after this line) |

### `test-pass` / `test-fail` / `test-skip`

| Field | Type | Applies to | Meaning |
|---|---|---|---|
| `class` | string | all | Fully qualified scenario class name |
| `test` | string | all | Scenario display name |
| `durationMs` | number | all | Execution time in whole milliseconds (0 for skips) |
| `message` | string | fail | "ExceptionType: message" of the failure |
| `stack` | string | fail | Stack trace; omitted when there is none |
| `reason` | string | skip | Skip reason |

Server crashes and watchdog timeouts surface as ordinary `test-fail` events: the in-process
watchdog already translates them (see the engine E2E suite for the exact failure shapes).

### `class-end`

| Field | Type | Meaning |
|---|---|---|
| `class` | string | Fully qualified scenario class name |
| `passed` / `failed` / `skipped` | number | Per-class counts |

### `class-summary`

The per-class isolation summary (capture/rollback/FreshWorld/restart counts and their
measured costs), the same line the harness prints to stderr when a class hands its host off.
Emitted between the class's last `test-*` line and its `class-end`: the hand-off fires while
the NEXT class's first scenario boots, or when the worker shuts the final host down before
closing the stream. Only present when the class ran any isolation mode at least once. Added
in 0.8 as an additive event under the versioning rules above: `v` stays `1`, and older
consumers ignore it. Issue #71 (post-0.8.0) widened the emission rule and the summary
WORDING, not the fields: FreshWorld-only classes, previously silent, now report their recycle
count and measured cost, and the lazy first capture of a rollback class is its own line item
("1 capture (1.2 s), 3 rollback(s) succeeded (0.4 s total)") instead of being folded into the
rollback count. The `summary` string is human-facing prose, not a parse contract.

| Field | Type | Meaning |
|---|---|---|
| `class` | string | Fully qualified scenario class name |
| `summary` | string | The formatted summary line, identical to the stderr line plain runs print |

Example (one line, wrapped here for readability):

```json
{"v":1,"type":"class-summary","class":"Sample.Scenarios.MarkerScenarios",
 "summary":"[Atlas] isolation summary for Sample.Scenarios.MarkerScenarios: 1 capture (1.2 s), 1 rollback(s) succeeded (0.2 s total), 0 degraded to a full host recycle, 0 FreshWorld recycle(s), 1 restart(s) (7.1 s total)."}
```

### `error`

A failure outside any single scenario: a runner-level error (crashed fixture, unhandled
collection exception) or an environment error that prevented the run. Errors force a
non-zero exit code.

| Field | Type | Meaning |
|---|---|---|
| `message` | string | "ExceptionType: message" of the error |

### `run-end` (always last, run and list mode alike)

| Field | Type | Meaning |
|---|---|---|
| `total` | number | Scenarios reported (list mode: scenarios discovered) |
| `passed` / `failed` / `skipped` | number | Counts (all 0 in list mode) |
| `errors` | number | Runner-level errors |
| `wallClockMs` | number | Wall-clock duration of the whole worker run |
| `exitCode` | number | The exit code the process is about to return |

## Sequence

Run mode: `run-start`, then per class in execution order `class-start`, one `test-*` per
scenario, an optional `class-summary` (only for classes with isolation activity),
`class-end`, and finally `run-end`. `error` lines may appear anywhere between `run-start` and
`run-end`. An environment failure produces exactly `run-start`, `error`, `run-end` (exit 2).
List mode: one `discovered` per scenario, then `run-end`.

A fail-safe closes the stream even when the run crashes: an unhandled exception yields a
synthetic `test-fail` for the in-flight class (so the orchestrator never sees a silently
shorter test list), the pending `class-end`, and `run-end`.

## Example transcript

`atlas run Sample.Scenarios.dll --worker --classes Sample.Scenarios.MarkerScenarios`
(captured 2026-07-06, exit code 0; one real server boot behind the `class-start` line):

```jsonl
{"v":1,"type":"run-start","assembly":"/home/user/dev/Atlas/samples/Sample.Scenarios/bin/Debug/net10.0/Sample.Scenarios.dll","classes":["Sample.Scenarios.MarkerScenarios"],"pid":87993,"atlasVersion":"0.1.0+fbc1f770aea55856832a13a918a4cae97efe00e2"}
{"v":1,"type":"class-start","class":"Sample.Scenarios.MarkerScenarios"}
{"v":1,"type":"test-pass","class":"Sample.Scenarios.MarkerScenarios","test":"Sample.Scenarios.MarkerScenarios.ExecuteCommand_Should_ReportFailure_When_CommandIsUnknown","durationMs":4}
{"v":1,"type":"test-pass","class":"Sample.Scenarios.MarkerScenarios","test":"Sample.Scenarios.MarkerScenarios.TimeCommand_Should_AdvanceCalendar_When_Executed","durationMs":39}
{"v":1,"type":"test-pass","class":"Sample.Scenarios.MarkerScenarios","test":"Sample.Scenarios.MarkerScenarios.SampleModBlock_Should_BePlaceable_When_ModIsLoaded","durationMs":172}
{"v":1,"type":"class-end","class":"Sample.Scenarios.MarkerScenarios","passed":3,"failed":0,"skipped":0}
{"v":1,"type":"run-end","total":3,"passed":3,"failed":0,"skipped":0,"errors":0,"wallClockMs":7574,"exitCode":0}
```

## Stage 2 notes

- The class inventory comes from the same discovery `--list` uses, run inside the
  orchestrator's own process (the orchestrator IS the CLI, so it needs no subprocess to
  discover); out-of-process consumers get the same inventory from `--list --worker`
  (`discovered` events). The orchestrator groups scenarios by `class` and dispatches one class
  per worker invocation.
- Worker self-invocation: the orchestrator re-invokes the dotnet muxer with the Atlas.Cli dll
  when the current process IS the muxer (`dotnet Atlas.Cli.dll`), and re-invokes its own
  process image otherwise (`dotnet run` apphost, published apphost, packed `dotnet tool` shim,
  whose assembly location points into the NuGet .store and is therefore not used). See
  `WorkerCommand.Resolve`.
- Result aggregation keys on (`class`, `test`): display names are unchanged from what
  `dotnet test` produces, preserving TRX/tooling continuity.
- The per-worker outer timeout, crash translation (no `run-end` = assignment failed) and
  scratch-data-path attachment are orchestrator concerns, deliberately not part of v1.
