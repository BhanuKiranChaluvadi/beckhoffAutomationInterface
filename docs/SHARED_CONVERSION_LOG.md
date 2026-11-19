# Shared PLC conversion

2026-11-18

- Inspected the pinned release path, current incremental sync, PLC source format, test runners and workflow triggers. The repeated cost is object-by-object COM import; parsing and compilation are separate work.
- Created `feature/shared-plc-conversion` from the clean existing BAI branch. No existing work was reset.
- Wrote regression tests before implementing the conversion modules. Tests remain unrun at the user's request.
- Added a pure whole-object XML writer, an immutable content-addressed cache, and an offline project updater. Methods and properties travel with their owner; project IDs survive updates. Compiler pragmas remain source text.
- Connected the shared code stage used by init, sync, machine builds and PLC test projects. Adopted projects retain the existing shadow-copy/COM path. Kept unchanged projects retain the sync-manifest shortcut.
- Added `bai convert` to prepare shared source without opening XAE. Source content and converter identity choose cache entries; paths and machine names do not. Build identities, generated gas models and test doubles naturally have different content hashes.
- No PLC test suite, C# test suite, gate, runtime activation or release deployment has been run for this change.

- Compiled the tool and test project after the final refactor: build succeeded with zero errors and one pre-existing xUnit1031 warning in MachineCommandsTests. No tests were executed.
- Moved source hashing/file-kind decisions into the source model so XML conversion and the existing sync manifest share them without depending on each other's storage.
- Kept machine-dev sessions open and retained source-owned stale deletion before closed-project updates; untracked project members are preserved.
- Inspected crapper/dryer support: neither supports C#. Mutator was not run because it executes tests. XML acceptance and workflow contracts are explicitly pending.
- Recorded this implementation on an isolated local feature branch. No main merge/push, publication or tool-pin update is part of this untested change.

Pending acceptance (only when authorized): run the conversion regressions and a fresh TwinCAT build for each family plus representative property/interface and test projects. Measure cold/warm import times before claiming a speedup.

## Architecture follow-up: source updates

- Added regression cases first for mixed-case member owners, case-only member renames, tracked member removal during moves, and rollback after a project-file write failure. They have not been executed.
- Unified object grouping and case-insensitive identity across the manifest, converter, file merge, and COM owner lookup. Bumped the manifest format to invalidate records that could omit mixed-case owners.
- Moved source-stage ownership into PlcSourceUpdate. Closed managed projects now plan stale deletions, member pruning, moves and replacements together; hand-written untracked members and template program identities survive.
- Added atomic per-file publication and rollback of attempted writes/deletions on synchronous failure. This is rollback for reported I/O failures, not a durable transaction across power loss; source and disk hashes still force incomplete objects to be revisited.
- Shared COM-busy retry policy through ComInterop rather than depending on the pipeline from the source-update module.
- Compiled the tool and regression project successfully (one existing xUnit1031 warning). No tests, gates, XAE builds or runtime actions ran.

## Architecture follow-up: machine outcomes

- Wrote completion regressions first: I/O/variant/link errors still allow diagnostic compilation but cannot record successful sync or continue to activation; final safety validation can veto continuation; compilation failures retain completed sync; validation exceptions cannot pass.
- Added a single PipelineCompletion sequence used by SyncPipeline. PipelineOutcome distinguishes completed sync, compilation, final validation, and overall success. Forward stages report failures there; process exit status is set at the CLI boundary.
- Moved final safety-file checks before successful continuation. Native safety/axis checks now run before activation as part of final validation.
- Made MachineBuilder, native bootstrap, and machine-dev consult the outcome instead of process-global exit status or a successful compiler count. Expected link counts are part of machine-run success, and link decisions use values instead of parsing display strings.
- Machine state retains completed sync after compiler failures and marks sync/validation failures for a fresh rebuild. Existing activation and crash handling remains separate from compiler reporting.
- Compiled tool and test project successfully, with only the existing xUnit1031 warning. Tests and gates remain unrun.

## Architecture follow-up: scenario lifecycle

- Wrote lifecycle regressions through production RunAll first: one prepare deadline across idle/reset, slow-step timeout with restoration/cancellation and a fresh next-scenario budget, and cleanup time consumed by restoration before explicit commands.
- Centralized overall, phase, step and recovery deadline calculation in ScenarioBudget. Prepare and cleanup no longer restart their timeout for each sub-operation; step checks also reject elapsed budgets after synchronous calls return.
- Retained independent cleanup time after a step/overall timeout, fault restoration, failure diagnostics, simulation fault proofs, hardware restrictions and blocking later scenarios after cleanup failure.
- Renamed the internal step-only surface to RunSteps so it cannot be confused with the production RunAll lifecycle. Existing narrow step tests use that name; lifecycle cases call RunAll.
- Updated timeout documentation and described module ownership, domain terms and rollback limits in docs/ARCHITECTURE.md.
- Static review also found that file mirroring must exclude task configuration includes; added coverage before centralizing the object-file predicate. Added a case proving invalid paths cannot commit a planned stale deletion.
- Tests, gates, TwinCAT compilation and runtime actions remain unrun. C# compilation is the only executed code check.
- Final static review kept the warm-sync path from unnecessarily closing/reopening XAE: file work is selected only for changed source or actual mirror changes. A regression case covers a tracked move after standalone sync, where the manifest can legitimately skip unchanged source content.

## Final review and compilation

- Kept task files outside source-object mirroring, preserved the open-session shortcut for unchanged mirrors, and ensured a real folder move bypasses a content-only manifest skip. Added regression cases before those adjustments.
- Made completion record exceptions in their actual phase before rethrowing. A validation exception now forces the next machine run fresh; a compiler exception retains completed sync. Late sync failures also invalidate the authoritative sync result.
- Consolidated link success presentation around the same value-based decision used by machine continuation.
- Final C# compilation of the tool and test project succeeded with zero errors and the existing xUnit1031 warning at MachineCommandsTests.cs:222. No test suite, gate, XAE compilation, runtime activation or deployment was executed.
- Saved the work in ordered local feature-branch commits. The PLC repository tool pin remains unchanged; no main push or publication has been performed.

## Second review implementation: property preservation

- Added getter/setter preservation regressions before changing the merge policy; cases cover standalone preservation and full member replacement.
- Kept accessor declarations and IDs project-local while updating supplied accessor bodies. Preserved project XML attributes and unowned metadata; declaration/body text remains source-owned for roots, methods and properties.
- Compiled tool and test project successfully (zero errors, the existing xUnit1031 warning). Regression cases are written but unrun.
- Retained the existing isolated feature branch and left the PLC tool pin and repository work untouched.

## Second review implementation: trustworthy scenario evidence

- Added production RunAll regressions before changing execution: communication errors and slow checks cannot prove injected faults; observed command-answer mismatches can; successful late reads and acknowledgments cannot pass shorter step deadlines.
- Added typed ScenarioFailure evidence for expectation, command answer, deadline, transport and lifecycle failures. Fault proof now consumes evidence and step numbers instead of parsing rendered message prefixes.
- Kept polled, in-time expectation mismatches eligible for fault proof. A communication failure or a blocking read returning after its deadline is not condition evidence.
- Split step execution into focused operations and used ScenarioBudget values for both shorter step and shared-phase deadlines. Removed the raw StepDeadline calculation surface.
- Compiled the tool and regression project successfully with the existing xUnit1031 warning only. Tests and gates remain unrun.

## Second review implementation: complete recovery ownership

- Added lifecycle regressions first for partial reset timeout, mode guard refusal, a transport exception after reset, slow/throwing fault restoration, cancellation opportunity, and blocking later scenarios after failed reset recovery.
- Recorded recovery obligations before command/reset writes and kept them in one per-scenario state. A failed mode guard acquires none. The lifecycle now enters its recovery scope before preparation, so partial preparation no longer bypasses cleanup.
- Clear a pending simulation reset and attempt every original-value restoration before cancellation. Cancellation receives a separate bounded acknowledgment window after restoration so injected interlocks can be restored first and slow restoration cannot starve cancellation.
- Explicit cleanup retains its original deadline; restoration/cancellation overruns still fail cleanup and block subsequent scenarios. Recoverable errors in one action do not prevent the remaining essential recovery attempts.
- Centralized before/after phase checks around execution, including injected writes and cleanup steps. Added a case proving a slow injection cannot proceed into faulted steps.
- Updated architecture and user documentation to describe ownership, typed proof evidence and the precise recovery deadline policy.
- Static review corrected the accessor fixture to compare persisted XML text (XML normalizes line endings), and corrected the existing path-guard assertion to inspect Project children.
- Crapper and dryer do not support this C# codebase (already checked in the first implementation); mutation tooling would execute tests and remains prohibited.
- Final static review kept unowned XML metadata independent of whether it has a Name attribute, and clarified that stale line maps are omitted rather than regenerated by the converter.
- Final C# compilation succeeded with zero errors and only the existing xUnit1031 warning in MachineCommandsTests.cs:222. No tests, gates, XAE builds, runtime actions or deployments were run.
- Saved all four fixes in ordered local commits on feature/shared-plc-conversion. No push, release publication, or PLC tool-pin update has been performed.

## 2026-11-19: hand-written XML writer removed

- Owner decision: source reaches a project only through XAE (COM). Files that XAE saved are reused
  later (subtype 58 import); bai does not write PLC object XML itself.
- Baseline before the change: 1264 xUnit tests passed (the first run of the conversion tests).
- Removed `PlcObjectXml`, `PlcObjectCache`, `PlcProjectCode`, `ProjectFileBatch`, `bai convert`,
  their 33 tests and `e2e/shared-conversion.ps1`. `PlcSourceUpdate` again uses the COM object sync
  (with the sync-manifest skip) for every project; `KeepSessionOpen` existed only for the file path.
- Proof: build passes; 1231 xUnit tests pass. No XAE build ran.
