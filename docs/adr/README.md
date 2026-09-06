# Architecture decision records

Each record here names one decision Atlas already made, in the same five parts: a title, a
status (accepted, superseded, or under review), the context that forced a choice, the
decision itself, and the consequences the project lives with because of it. A sixth part,
source files, points at the code that embodies the decision, with line numbers against the
commit that added or last revised the record. The records are short on purpose: the long
rationale stays in `docs/specs/`, and a record that disagrees with the code is a bug in the
record, not in the code. Numbering is sequential and permanent; a reversed decision gets a
new record and the old one is marked superseded rather than edited away.

| Record | Decision | Status |
|---|---|---|
| [0001](0001-embedded-server-on-a-dedicated-game-thread.md) | Boot a real `ServerMain` in-process on a dedicated game thread | Accepted |
| [0002](0002-one-live-host-per-process.md) | One live host per process, a static registry keyed by scenario class | Accepted |
| [0003](0003-engine-compatibility-by-shape-probing.md) | Engine-version compatibility by shape probing in one shim | Accepted |
| [0004](0004-bridge-rendezvous-through-appdomain-slots.md) | Bridge rendezvous through AppDomain data slots | Accepted, supersedes the shared-statics design |
| [0005](0005-pure-decision-core-thin-io-shell.md) | Pure decision core with a thin IO shell | Accepted |
| [0006](0006-outcome-objects-on-expected-degrade-paths.md) | Outcome objects, not exceptions, on expected degrade paths | Accepted |
| [0007](0007-cli-carries-no-harness-copy.md) | The CLI carries no harness copy and reaches the harness by name | Under review |
