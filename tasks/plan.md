# Implementation Plan: Post-Review Hardening of the ST→TwinCAT Sync/Build Loop

## Overview
The `beckhoffAutomationInterface` sync/build engine (shipped MVP, see
`docs/ideas/st-source-twincat-sync.md`) works as a create/update/compile
pipeline, but a code review surfaced two gaps against its actual purpose —
a trustworthy edit→sync→compile **feedback loop** — and the repo's own
`TODO.md`/`TODO.xml` scratch notes independently confirm one of them and log
two more real bugs found through manual testing against the Shark project.
This plan closes all four before relying on the tool day-to-day.

## Architecture Decisions
- **Test harness first.** `StFileParser`, `StPouSource`, `IgnoreRules`,
  `StLinter`, `StFormatter` are pure C# with no COM dependency, but the repo
  has zero automated tests today (single `beckhoffAutomationInterface.csproj`,
  no test project). Adding a test project before touching parser/sync logic
  gives a fast regression net for changes that don't need a live TwinCAT
  instance to verify.
- **Known bugs before new features.** The two `TODO.md` bugs (duplicate
  library refs, unknown DUT types) are small, isolated, and already
  reproduced once — fix those before the larger provenance-mapping work so
  they don't tangle with it.
- **Provenance map, not a guess.** The error-location fix (Finding 1) needs
  ground truth on what TwinCAT actually reports per PLC-object kind before
  designing the mapping — the DUT case is confirmed (exported `.TcDUT` path,
  `Line: 1`), but FUNCTION_BLOCK/METHOD/PROPERTY/GVL are not yet confirmed.
- **Orphan/rename handling needs a policy decision from you, not just an
  engineering default** — silently deleting PLC objects has real blast
  radius on a shared project; silently leaving them stale is also wrong.
  Task 7 exists specifically to make that call explicit before Task 8 builds
  it.

## Task List

### Phase 1: Test harness (foundation)
- [x] Task 1: Add a unit test project covering existing parser/lint/format logic

### Checkpoint: Foundation
- [x] New test project builds and `dotnet test` passes (25/25)
- [x] Current parser behavior is now regression-protected before any of the
      following phases touch it

### Phase 2: Close the two known TODO.md bugs
- [x] Task 2: Fix duplicate library references (Tc2_Standard/Tc2_System/Tc3_Module)
      (not reproducible — verified no duplicates across repeated real syncs)
- [x] Task 3: Investigate and fix "Unknown type" DUT errors for EL3174/EL3214-derived types
      (root cause: MDP5001 suffix is a config-hash, revision-dependent — see todo.md)

### Checkpoint: Known bugs closed
- [x] Full sync+build against the real Shark project shows no duplicate
      library references and no `C0077 Unknown type` errors —
      BUILD PASSED 2026-07-14 (aliases now reference `MDP5001_320_5D7E181C`,
      this machine's actual TwinCAT-generated name)
- [x] `TODO.md` / `TODO.xml` deleted (superseded by this plan)

### Phase 3: Compile-error → source-line mapping (Finding 1)
- [x] Task 4: Confirm TwinCAT's reported FileName/Line shape per PLC-object kind
      (all kinds section-relative; see todo.md's findings table)
- [x] Task 5: Track `.st` file/line provenance through parsing and sync
      (StPouSource.SourceFileName/DeclarationStartLine/ImplementationStartLine/
      Get-SetStartLine, computed by StFileParser)
- [x] Task 6: Translate build errors back to `.st` path/line before printing
      (Sync/ErrorLocationResolver.cs; unmapped errors labeled, never dropped)

### Checkpoint: Feedback loop is trustworthy
- [x] Deliberately breaking one method in a multi-method FB file and running
      the tool prints that file and a line landing on/near the real error —
      verified 2026-07-14: every probe kind mapped to the EXACT broken line
      (e.g. a broken METHOD in a multi-method FB → `FB_BrokenMethod.st:16`)

### Phase 4: Drift detection / orphan handling (Finding 2)
- [x] Task 7: Decide the orphan/rename policy (warn-only vs. prune, and at what scope)
      (Option (c): warn always at both levels, prune stays opt-in where it
      already was — matches the repo's --confirm-delete/--confirm-delete-io
      safety-gate philosophy; full rationale in todo.md)
- [x] Task 8: Implement the chosen policy
      (Sync/KnownNamesTracker.cs + .st-known-names state + [drift] warnings
      in Program.RunSync; verified by scratch VS runs + 6 unit tests)

### Checkpoint: Complete
- [x] Renaming or deleting a METHOD/POU inside `.st` source no longer leaves
      invisible stale code compiling as part of the project
- [x] All four issues verified against a real TwinCAT/Visual Studio run,
      not just unit tests

## PLAN COMPLETE (2026-07-15) — all phases, tasks, and checkpoints closed.

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| Can't fully unit-test COM-facing code (`PouSyncEngine`, `LibrarySyncEngine`, `BuildRunner`) | Med | Keep pure logic (name diffing, provenance mapping, error translation) extracted into COM-free functions that *are* unit-testable; reserve manual TwinCAT runs for the COM glue only |
| TODO.md bugs (Tasks 2–3) may have a different root cause than hypothesized once reproduced | Med | Both tasks start with a repro step against the real project before writing a fix |
| Task 7's policy choice changes Task 8's scope significantly | Med | Task 7 is a standalone decision task with no code, specifically to avoid discovering this mid-implementation |
| TwinCAT error-location format (Task 4) turns out inconsistent/unreliable across object kinds | High | If so, Finding 1's fix may need to degrade gracefully (best-effort mapping + always show the raw value too) rather than promising exact line numbers everywhere |

## Open Questions
- Task 7: warn-only-by-default (matching the existing `--confirm-delete`
  opt-in philosophy) vs. prune-by-default for orphaned METHOD/PROPERTY
  children — which do you want?
- Is the real Shark project (referenced in `TODO.md`) available on this
  machine for the manual verification steps in Tasks 2–4 and the Phase 3/4
  checkpoints, or does repro need a smaller throwaway TwinCAT project?
