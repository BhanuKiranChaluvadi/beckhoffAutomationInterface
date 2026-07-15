# Task List: Composable Stage-Based CLI

See `tasks/plan.md` for the target CLI API, phase table, and rationale.
Previous (completed) plan: `tasks/archive/2026-07-14-post-review-hardening/`.

---

## Task 0: Archive completed plan, write new plan/todo, commit current tree

**Description:** Move the completed hardening plan/todo into
`tasks/archive/2026-07-14-post-review-hardening/`, write this plan's files,
and commit the whole outstanding working tree first so the CLI refactor's
diff is clean and reviewable on its own.

**Acceptance criteria:**
- [x] Old plan/todo archived intact
- [x] Working tree committed (hardening work is one coherent commit)

**Verification:**
- [x] `git status` clean before Task 1 begins (except this plan's own files)

**Status: DONE (2026-07-15).** Commit 88995bc; `.st-sync-state`/`.st-known-names`
added to .gitignore (machine-local state); images/ committed (referenced by the
archived todo's investigation notes).

---

## Task 1: SyncStages flags in RunOptions (pure, unit-testable)

**Description:** Add a `[Flags] enum SyncStages { None, Code, Libraries, Io,
Events, Build }` and `RunOptions.Stages`: each `--sync-code/--sync-libs/
--sync-io/--sync-events/--build` flag ORs its stage in; no stage flag at all →
`All` (current full behavior). Add `--init` and `--check-events` booleans.
Aliases: `--build-only` → Build only (deprecated), `--events-only` →
`--check-events` (deprecated). Update `PrintUsage` with the new table.

**Acceptance criteria:**
- [ ] Each stage flag alone yields exactly that stage; combinations OR together
- [ ] No stage flags → Stages == All
- [ ] Aliases map as specified; old invocations keep working
- [ ] `--init`/`--check-events` parsed

**Verification:**
- [ ] New `RunOptionsTests.cs` covers: each flag alone, a combination,
      default=All, both aliases, --init; `dotnet test` green

**Dependencies:** Task 0
**Files:** `RunOptions.cs`, `beckhoffAutomationInterface.Tests/RunOptionsTests.cs`
**Estimated scope:** S

---

## Task 2: `--init` guard against silent bootstrap

**Description:** In `Program.Main`, before any Visual Studio launch: if
`options.SolutionFilePath` doesn't exist and `--init` wasn't given, print a
clear error naming the exact path checked and how to fix it (correct
--source/--dest/--name, or pass --init to create), and exit 1. This applies to
EVERY mode. `TwinCatProjectOpener.Open`'s bootstrap branch only runs under
`--init`.

**Acceptance criteria:**
- [ ] Missing solution without --init: exit 1, message names the checked path,
      no VS process launched, nothing created on disk
- [ ] Missing solution with --init: bootstraps exactly as today
- [ ] Existing solution: unaffected in every mode

**Verification:**
- [ ] Scratch run both ways; `$LASTEXITCODE` checked; empty dest confirmed
      still empty after the refused run

**Dependencies:** Task 1
**Files:** `Program.cs`, `TwinCatProjectOpener.cs`
**Estimated scope:** S

---

## Checkpoint A
- [ ] `dotnet test` green
- [ ] Scratch full run (with --init on first run) behaves exactly as before

---

## Task 3: Extract stage methods from RunSync (pure refactor)

**Description:** Mechanically split `Program.RunSync`'s existing blocks into
private methods with the state they need passed explicitly: `SyncCode`
(POUs + lint + drift + baselines), `SyncLibraries`, `SyncIoTree`,
`ApplyTsprojEdits` (both editors + the close/reopen dance), `SyncLinks`,
`RunBuild` (build + mapped error printing). Default path calls all of them in
the current order. NO behavior change.

**Acceptance criteria:**
- [ ] Default full-run log output is line-for-line the same shape as before
- [ ] All existing tests pass unmodified

**Verification:**
- [ ] `dotnet test` green; scratch full run: same stage messages, BUILD PASSED

**Dependencies:** Task 2
**Files:** `Program.cs`
**Estimated scope:** M

---

## Task 4: Wire stage selection + lazy VS lifecycle

**Description:** Execute only the selected stages in the fixed phase order —
A (VS open): code → libs → io-tree; B (VS closed): io's Create-PLC-Data-Type
edit + events' Event Class edit; C (VS open only if needed): io's links, build.
VS launches lazily: `--sync-events` alone runs with no VS at all; phase C's
reopen is skipped when nothing needs it. `--check-events`: read-only check,
exit 1 when any declared class is missing (0 otherwise), no VS.

**Acceptance criteria:**
- [ ] `--sync-events` alone: .tsproj edited, NO devenv spawned, second run
      "already present" with file unchanged
- [ ] `--sync-io` alone: tree + templates + links only; no POU/lib/build lines
- [ ] `--sync-code` alone: POUs only; no lib/IO/build lines
- [ ] `--sync-io --sync-events`: both stages, one invocation, one VS session
- [ ] No stage flags: full run identical to today
- [ ] `--check-events` exit codes: 1 missing / 0 present

**Verification:**
- [ ] Scratch matrix runs above, checking logs + `$LASTEXITCODE` + process list

**Dependencies:** Task 3
**Files:** `Program.cs`
**Estimated scope:** M

---

## Task 5: CI build exit codes

**Description:** `--build` (and any run whose selected stages include Build):
set `Environment.ExitCode = 1` on BUILD FAILED and on `BuildTimeoutException`;
0 on BUILD PASSED. Combined with Task 2, `--build` can never bootstrap — a
wrong path in CI fails loudly.

**Acceptance criteria:**
- [ ] Broken POU → `--build` exits 1, errors printed with mapped .st:line
- [ ] Fixed → exits 0

**Verification:**
- [ ] Scratch: break, `--build`, `echo $LASTEXITCODE` → 1; fix via
      `--sync-code`, `--build` → 0

**Dependencies:** Task 3 (RunBuild extracted)
**Files:** `Program.cs`
**Estimated scope:** XS

---

## Checkpoint B
- [ ] Full scratch matrix green (modes, combination, default, exit codes)

---

## Task 6: Docs + real Shark project smoke

**Description:** Update `README.md` and `PrintUsage` with the stage-flag table,
the per-stage VS lifecycle, `--init` semantics, and a CI recipe
(`--build` + exit code; note the githooks incremental worker is unaffected).
Then a light real-project smoke: `--check-events` (expect exit 0),
`--sync-events` (expect idempotent no-op), `--build` (expect BUILD PASSED,
exit 0). Commit.

**Acceptance criteria:**
- [ ] README documents every stage flag with its VS behavior + a CI example
- [ ] Real-project smoke passes as listed

**Verification:**
- [ ] The three real-project runs; final `dotnet test` green; committed

**Dependencies:** Tasks 4-5
**Files:** `README.md`, `RunOptions.cs`
**Estimated scope:** S

---

## Checkpoint: Complete
- [ ] All modes verified on scratch; real project smoke green
- [ ] `dotnet test` green
- [ ] Committed
