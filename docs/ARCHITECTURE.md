# Build and scenario ownership

BAI separates reusable source conversion from project configuration and machine execution.

| Module | Owns |
| --- | --- |
| `PlcSourceObject` | Validating source identities and grouping a root with all its members. PLC identifiers match without case; source spelling still participates in content hashes. |
| `PlcObjectXml` / `PlcObjectCache` | Converting complete source objects and sharing immutable conversion results. Machine flags, I/O, tasks, safety and project IDs stay outside the cache. |
| `PlcSourceUpdate` | Parsing, choosing the closed-file or interactive COM adapter, and recording successful source updates. |
| `PlcProjectCode` / `ProjectFileBatch` | Planning project-local object changes before writing, preserving local IDs and untracked members, and rolling back reported write failures. |
| `SyncManifest` | Evidence that source text and the saved object file still match. It uses the converter's source grouping and object identity rules. |
| `SyncPipeline` / `PipelineCompletion` | Ordering sync, diagnostic compilation, final file validation and successful continuation. |
| `PipelineOutcome` | The authoritative sync/build/validation outcome. Machine decisions consume values; the CLI translates failures into an exit code. |
| `ScenarioRunner.RunAll` | The production scenario lifecycle: prepare, steps, diagnostics, restoration, cleanup, fault proof and reporting. |
| `ScenarioBudget` | Overall and phase deadlines on an injected clock. Recovery receives a fresh budget after step timeout. |

A **source object** is a root POU, interface, DUT or GVL together with its methods and properties.
A **conversion** contains source-derived XML; a **project update** combines that conversion with the receiving project's identities and member policy.
Standalone sync preserves omitted members. Machine mirroring removes previously tracked members that disappeared, retaining untracked additions.

File updates finish conversion and path validation before committing any writes or deletions.
Each replacement is atomic, and a reported write failure restores attempted files in reverse order.
This does not provide a durable transaction across power loss. Manifest hashes cause incomplete objects to be revisited on the next sync.
Closed managed projects use this path. Interactive and adopted projects retain COM updates; they do not claim file-batch rollback.

A **completed sync** has no sync-stage, I/O, unresolved-link or stale-name problems; machine builds also require their declared link count.
A **passed compilation** means the requested compiler builds passed. It alone does not permit activation.
A **successful pipeline** also requires completed sync and final validation. Compilation can still run after sync errors to produce diagnostics.
The machine state keeps a completed project after a compiler error; sync and validation failures require a fresh project next time.

Each scenario's prepare phase shares one timeout across waiting for idle, acknowledging and resetting.
The steps share `timeoutS`. Cleanup shares one independent prepare-timeout budget, including restoration and cancellation.
Restoration is attempted even after the main deadline expires. Cleanup failure blocks subsequent scenarios.
The overall budget remains `timeoutS + 2 * prepareTimeoutS + 30` seconds.
ADS calls and operator input are synchronous: the runner checks elapsed time when they return, then stops further steps if needed.
`RunSteps` is an internal narrow test surface; lifecycle regressions use `RunAll` with a fake PLC and clock.

See [the implementation log](SHARED_CONVERSION_LOG.md) for validation performed and acceptance still pending.
