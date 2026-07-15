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
- [x] Each stage flag alone yields exactly that stage; combinations OR together
- [x] No stage flags → Stages == All
- [x] Aliases map as specified; old invocations keep working
- [x] `--init`/`--check-events` parsed

**Verification:**
- [x] New `RunOptionsTests.cs` covers: each flag alone, a combination,
      default=All, both aliases, --init; `dotnet test` green (73/73)

**Status: DONE.**

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
- [x] Missing solution without --init: exit 1, message names the checked path,
      no VS process launched, nothing created on disk
- [x] Missing solution with --init: bootstraps exactly as today
- [x] Existing solution: unaffected in every mode

**Verification:**
- [x] Scratch run both ways; `$LASTEXITCODE` checked; empty dest confirmed
      still empty after the refused run (0 items, no devenv process)

**Status: DONE.** Guard is defense-in-depth: `Program.Main` refuses before VS
launches, and `TwinCatProjectOpener.Open` also throws if ever reached without
`--init`.

**Dependencies:** Task 1
**Files:** `Program.cs`, `TwinCatProjectOpener.cs`
**Estimated scope:** S

---

## Checkpoint A
- [x] `dotnet test` green
- [x] Scratch full run (with --init on first run) behaves exactly as before

---

## Task 3: Extract stage methods from RunSync (pure refactor)

**Description:** Mechanically split `Program.RunSync`'s existing blocks into
private methods with the state they need passed explicitly: `SyncCode`
(POUs + lint + drift + baselines), `SyncLibraries`, `SyncIoTree`,
`ApplyTsprojEdits` (both editors + the close/reopen dance), `SyncLinks`,
`RunBuild` (build + mapped error printing). Default path calls all of them in
the current order. NO behavior change.

**Acceptance criteria:**
- [x] Default full-run log output is line-for-line the same shape as before
- [x] All existing tests pass unmodified

**Verification:**
- [x] `dotnet test` green (73/73); scratch full run: same stage messages, BUILD PASSED

**Status: DONE.** `Program.RunSync` extracted into `SyncCode`/`SyncLibraries`/
`SyncIoTree`/`ApplyTsprojEdits`/`SyncLinks`/`RunBuild`, all taking a new
`TwinCatSession` (owns the VS/project/sysManager handles and knows how to
lazily open/close) instead of the old `ref VisualStudioSession` threading.

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
- [x] `--sync-events` alone: .tsproj edited, NO devenv spawned, second run
      "already present" with file unchanged
- [x] `--sync-io` alone: tree + templates + links only; no POU/lib/build lines
- [x] `--sync-code` alone: POUs only; no lib/IO/build lines
- [x] `--sync-io --sync-events`: both stages, one invocation, one VS session
- [x] No stage flags: full run identical to today
- [x] `--check-events` exit codes: 1 missing / 0 present

**Verification:**
- [x] Scratch matrix (8 runs) — all passed:
      1) `--sync-events` alone: 0 devenv processes, `BeckhoffLibEvents` present
      2) re-run: "already present", tsproj mtime unchanged
      3) `--sync-code` alone: only Sync-complete lines
      4) `--sync-io` alone: only IO-sync lines, `ScratchDev` created
      5) `--sync-io --sync-events` combined: both stages, one session
      6-7) `--build` with broken/fixed code: exit 1 → exit 0 (see Task 5)
      8) default (no flags): all stages + BUILD PASSED, exit 0

**Status: DONE.**

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
- [x] Broken POU → `--build` exits 1, errors printed with mapped .st:line
- [x] Fixed → exits 0

**Verification:**
- [x] Scratch: broken FB_Broken.st referenced from MAIN → `--build` exit 1,
      error mapped to `FB_Broken.st:5`; fixed + `--sync-code` + `--build` → exit 0

**Status: DONE.** `Environment.ExitCode = 1` set on both BUILD FAILED and
`BuildTimeoutException`; never bootstraps (Task 2's guard covers that).

**Dependencies:** Task 3 (RunBuild extracted)
**Files:** `Program.cs`
**Estimated scope:** XS

---

## Checkpoint B
- [x] Full scratch matrix green (modes, combination, default, exit codes)

---

## Task 6: Docs + real Shark project smoke

**Description:** Update `README.md` and `PrintUsage` with the stage-flag table,
the per-stage VS lifecycle, `--init` semantics, and a CI recipe
(`--build` + exit code; note the githooks incremental worker is unaffected).
Then a light real-project smoke: `--check-events` (expect exit 0),
`--sync-events` (expect idempotent no-op), `--build` (expect BUILD PASSED,
exit 0). Commit.

**Acceptance criteria:**
- [x] README documents every stage flag with its VS behavior + a CI example
- [x] Real-project smoke passes as listed

**Verification:**
- [x] Real Shark project: `--check-events` → exit 0 (1 declared, 1 present);
      `--sync-events` → "all declared classes already present", no VS launched;
      `--build` → BUILD PASSED, exit 0. Final `dotnet test`: 73/73 green.

**Status: DONE.**

**Dependencies:** Tasks 4-5
**Files:** `README.md`, `RunOptions.cs`
**Estimated scope:** S

---

## Checkpoint: Complete
- [x] All modes verified on scratch; real project smoke green
- [x] `dotnet test` green (73/73)
- [x] Committed

## PLAN COMPLETE (2026-07-15) — all tasks and checkpoints closed.

---

## Addendum: Follow-on maintainability pass (2026-07-15)

Immediately after this plan closed, a separate maintainability/readability
pass (SOLID principles + standard patterns) was requested and completed
against the code this plan produced. It never got its own `tasks/plan.md`
(planned and tracked via Claude's own plan-mode file + TaskCreate/TaskUpdate,
not the repo's `tasks/` convention), so it's recorded here for a single
unified history:

- `Clock.cs` — deduped 4 copies of the `Now()` timestamp helper.
- `ComInterop.cs` — deduped the `RPC_E_SERVERCALL_RETRYLATER` constant
  (deliberately did NOT merge the two different retry-loop bodies).
- `Sync/TsprojDataTypePool.cs` — extracted the load/find-or-create/hash/
  backup-save envelope duplicated across `TsprojPlcDataTypeEditor` and
  `TsprojEventClassEditor`, and `EventClassChecker`'s ad hoc read logic.
  Also corrected `EventClassChecker`'s stale doc comment.
- `ConsoleReport.cs` — `PrintLines()` collapsed ~19 repeated report-printing
  loops into single calls, verified byte-identical output.
- `SyncPipeline.cs` — extracted `RunSync` + all stage methods out of
  `Program.cs` into an instance class (constructor-injected session/options/
  ignore). `Program.cs` dropped from ~600 to 149 lines (CLI bootstrap only).

Explicitly deferred as over-engineering for this scale: an `ILogger`
abstraction, interfaces over the COM-backed engines, a stage-descriptor
registry replacing the `SyncStages` enum, splitting `StFileParser`,
redesigning the `*Report` classes' shapes.

Verified the same way as the rest of this project: 73/73 tests green and
unmodified at every step, plus a real scratch-project matrix and a real
Shark project `--check-events` smoke check. Committed as `d9be2dc`
("Maintainability pass: dedup helpers, extract shared editor envelope,
split Program.cs by SRP"), pushed to `origin/main` alongside this plan's own
commits.
