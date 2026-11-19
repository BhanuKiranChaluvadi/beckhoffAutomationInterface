# Build and scenario ownership

BAI separates PLC source updates from project configuration and machine execution. Source reaches a project only through XAE (COM).

| Module | Owns |
| --- | --- |
| `PlcSourceObject` | Validating source identities and grouping a root with all its members. PLC identifiers match without case; source spelling still participates in content hashes. |
| `PlcSourceUpdate` | Parsing, stale-object deletion, the COM object sync, and recording successful source updates. |
| `PlcObjectFileStore` | Object files that XAE saved, by source texts, TwinCAT version and bai; stored and returned only while they hold every source text. |
| `PlcObjectImport` | Which stored files a project imports (CreateChild subtype 58), and the proof from the saved project that each import is in its folder with all its texts. |
| `SyncManifest` | Evidence that source text and the saved object file still match. It uses the source grouping and object identity rules of `PlcSourceObject`. |
| `SyncPipeline` / `PipelineCompletion` | Ordering sync, diagnostic compilation, final file validation and successful continuation. |
| `PipelineOutcome` | The authoritative sync/build/validation outcome. Machine decisions consume values; the CLI translates failures into an exit code. |
| `ScenarioRunner.RunAll` | The production scenario lifecycle: prepare, steps, diagnostics, restoration, cleanup, fault proof and reporting. |
| `ScenarioBudget` | Overall and phase deadlines on an injected clock. Recovery receives a fresh budget after step timeout. |
| `ScenarioFailure` | Failure kind, step number and fault-proof eligibility, independent of rendered messages. |

A **source object** is a root POU, interface, DUT or GVL together with its methods and properties.
Standalone sync preserves omitted members. Machine mirroring removes previously tracked members that disappeared, retaining untracked additions.

A **completed sync** has no sync-stage, I/O, unresolved-link or stale-name problems; machine builds also require their declared link count.
A **passed compilation** means the requested compiler builds passed. It alone does not permit activation.
A **successful pipeline** also requires completed sync and final validation. Compilation can still run after sync errors to produce diagnostics.
The machine state keeps a completed project after a compiler error; sync and validation failures require a fresh project next time.

Each scenario's prepare phase shares one timeout across waiting for idle, acknowledging and resetting.
The steps share `timeoutS`; a shorter step timeout also constrains successful observations and command acknowledgments.
Execution checks phase deadlines before and after each operation, including fault injection and explicit cleanup.
Failure evidence distinguishes observed expectation/command-answer mismatches from transport errors and elapsed deadlines.
Fault proof requires an observed mismatch at the nominated step. An in-time value that continues to fail a polled condition is evidence; a late read or an ADS error is not.

The lifecycle records recovery obligations before preparation writes, and cleanup runs after partial preparation or recoverable transport exceptions.
The mode guard must pass before recovery writes. A failed guard acquires no preparation writes.
Cleanup first clears a pending simulation reset and attempts to restore all fault snapshots, even after the main deadline expires.
Cancellation then gets its own bounded prepare-timeout acknowledgment window, so slow restoration cannot prevent a cancellation attempt and injected interlocks are restored first.
Explicit cleanup steps share the original independent cleanup deadline. Exceeding that deadline fails cleanup even if cancellation succeeds.
Recovery attempts continue after individual recoverable errors; any cleanup failure blocks subsequent scenarios.
The overall budget remains `timeoutS + 2 * prepareTimeoutS + 30` seconds.
ADS calls and operator input are synchronous: the runner checks elapsed time when they return, then stops further steps if needed.
`RunSteps` is an internal narrow test surface; lifecycle regressions use `RunAll` with a fake PLC and clock.

See [the implementation log](SHARED_CONVERSION_LOG.md) for validation performed and acceptance still pending.
