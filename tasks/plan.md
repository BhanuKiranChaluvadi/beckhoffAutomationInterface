# Implementation Plan: Composable Stage-Based CLI

## Overview
Split the monolithic sync+build pipeline into composable one-shot stages
selected by CLI flags, so one-time per-project setup (device tree, Create PLC
Data Type, Event Classes) can run in isolation — create/update, close Visual
Studio, exit — and CI pipelines get a compile-only mode with a real exit code
that can never accidentally bootstrap a fresh project.

Full CLI API, phase table, and rationale: see the approved plan copy at the
bottom of this file's sibling `todo.md` task descriptions; decisions
(2026-07-15): composable stage flags; `--init` guard on project creation;
build exits 0/1 and never bootstraps; previous (completed) plan archived to
`tasks/archive/2026-07-14-post-review-hardening/`.

## Target CLI

```
beckhoffAutomationInterface [stage flags] [options]

Stage flags (composable; omitting ALL of them = full run: code+libs+io+events+build):
  --sync-code       ST → PLC POUs only (parse, lint, drift warnings, sync, save). No build.
  --sync-libs       libraries.xml → PLC library references only.
  --sync-io         io-devices.xml → device tree + Create-PLC-Data-Type .tsproj
                    templates + <Links>. Warn-only orphans unless --confirm-delete-io.
  --sync-events     events.xml + event-classes/*.xml → missing Event Classes written
                    into the .tsproj. Alone, needs NO Visual Studio session.
  --build           Open, compile, report errors mapped to .st file:line.
                    Exit 0 = BUILD PASSED, 1 = failed/timeout. For CI.

Project lifecycle:
  --init            Allow creating the solution/TwinCAT/PLC project when missing.
                    Otherwise a missing solution is a hard error (exit 1) in every mode.

Checks (read-only, no VS):
  --check-events    Declared-vs-present Event Classes; exit 1 if any missing.
                    (--events-only kept as deprecated alias.)

Aliases: --build-only → --build (deprecated).
Everything else (--source/--dest/--name/--ignore/--incremental/--confirm-delete/
--confirm-delete-io/--export/--parse-only/--format-check) unchanged.
```

**Fixed stage order** for any selected subset:
Phase A (VS open): code → libs → io-tree. Phase B (VS closed): Create-PLC-Data-Type
edit (io), Event Class edit (events). Phase C (VS open, only if needed): links (io),
build. VS launches lazily — `--sync-events` alone never opens it.

## Architecture Decisions
- **No new engines** — every stage reuses an existing Sync/* class as-is
  (PouSyncEngine, LibrarySyncEngine, IoSyncEngine, TsprojPlcDataTypeEditor,
  TsprojEventClassEditor, VariableLinkEngine, BuildRunner, ErrorLocationResolver).
  The work is restructuring Program.RunSync + RunOptions only.
- **Refactor before rewire** — Task 3 extracts stage methods with zero behavior
  change; only Task 4 changes control flow. Keeps the risky VS-lifecycle diff small.
- **Explicit bootstrap** — `--init` prevents the wrong-path silent-bootstrap trap
  (real near-miss 2026-07-14) and makes CI fail loudly on a typo'd path.

## Task List

### Phase 1: Foundation
- [x] Task 0: Archive completed plan, commit current tree
- [ ] Task 1: SyncStages flags in RunOptions + RunOptionsTests
- [ ] Task 2: --init guard against silent bootstrap

### Checkpoint A
- [ ] All tests green; scratch full run behaves exactly as before (bootstrap via --init)

### Phase 2: Stage execution
- [ ] Task 3: Extract stage methods from RunSync (pure refactor, no behavior change)
- [ ] Task 4: Wire stage selection + lazy VS lifecycle (+ --check-events exit code)
- [ ] Task 5: CI build exit codes (0 pass / 1 fail or timeout)

### Checkpoint B
- [ ] Scratch matrix: each mode alone, --sync-io --sync-events combined, default full
      run, exit codes verified via $LASTEXITCODE

### Phase 3: Docs + real-project smoke
- [ ] Task 6: README/usage; real Shark smoke (--check-events 0, --sync-events no-op,
      --build BUILD PASSED exit 0); final commit

### Checkpoint: Complete
- [ ] All acceptance criteria in todo.md met; committed

## Risks and Mitigations
| Risk | Impact | Mitigation |
|------|--------|------------|
| VS lifecycle regressions when stages are skipped | Med | Pure extraction first (Task 3); lifecycle isolated in Task 4; per-mode scratch runs |
| --init breaks githooks/run-incremental-sync.ps1 | Low | Hook targets an existing project (reopen path) — unaffected; noted in README |
| Behavior drift in default mode | Med | Checkpoints compare scratch logs against pre-refactor output |
| Real Shark project touched during verification | Low | Task 6 smoke only: read-only check, idempotent events no-op, one build |

## Open Questions
- None blocking — all four design decisions confirmed by the user 2026-07-15.
