# ST-as-Source TwinCAT Sync Engine

## Problem Statement
How might we let an engineer maintain a TwinCAT PLC project as plain .st
text files in Git, and have those files automatically sync — create,
update, compile, and (eventually) IO-map/activate — into a real TwinCAT
XAE project via the Automation Interface, without ever hand-editing the
project inside the IDE?

## Recommended Direction
Build a diff-aware "Incremental ST Compiler" engine (Cluster B): maintain a
manifest mapping .st file path -> TwinCAT tree path, and on each run only
create/update/delete the POUs that changed, pushing text directly into
each POU's DeclarationText/ImplementationText rather than rebuilding the
whole project. Wrap it (Cluster C) with two interchangeable entry points
that share the same engine: a file-watcher "hot reload" loop for solo
work today, and a non-interactive CLI (`stsync build|ci`) for the
team/CI phase later — so growth doesn't require a rewrite, only a new
front-end. Add a drift-guard step so the .st files never silently
diverge from what's actually inside TwinCAT.

## Key Assumptions to Validate
- [x] Spike: can ITcPlcDeclaration/ITcPlcImplementation accept raw ST text
      for a freshly created POU, and does a subsequent build compile it
      correctly? **VALIDATED 2026-07-04** — see beckhoffAutomationInterface/Program.cs.
      Injected ST/Shark/MAIN.st verbatim into the MAIN POU's DeclarationText/
      ImplementationText (no XML wrapping needed), saved, built via
      dte.Solution.SolutionBuild.Build(), and it compiled with 0 errors.
      MAIN.TcPOU on disk matches MAIN.st content exactly.
- [x] Spike: can compiler errors be read back from the DTE (ErrorList /
      BuildEvents) reliably enough to produce a clean pass/fail signal?
      **VALIDATED 2026-07-04** — dte.ToolWindows.ErrorList.ErrorItems.Count
      gives a reliable pass/fail signal (0 = clean build); each ErrorItem
      exposes ErrorLevel/Description/FileName/Line for failure reporting.
- [x] Spike: can brand-new POUs be created (not just existing ones
      updated)? **VALIDATED 2026-07-04** — reflection on the local
      Interop.TCatSysManager.dll revealed the actual `TREEITEMTYPES` enum
      (namespace `Interop.TCatSysManager`), including
      `TREEITEMTYPE_PLCPOUPROG = 602` (PROGRAM), `_PLCPOUFB = 604`
      (FUNCTION_BLOCK), `_PLCPOUFUNC = 603` (FUNCTION), `_PLCFOLDER = 601`,
      `_PLCGVL = 615` (matches the soup01 tutorial's GVL type id exactly —
      cross-validated), `_PLCDUTSTRUCT = 606`, `_PLCLIBMAN = 617`.
      `pousFolder.CreateChild(name, 602, "", null)` creates a new PROGRAM
      POU (the 4th arg, vInfo, must be `null` — an empty string throws
      "Must specify valid information for parsing in the string").
      Ran a multi-file sync (existing MAIN.st updated, new Diagnostics.st
      created) in one pass, both compiled cleanly.
- [x] Spike: does ITcSmTreeItem/parent expose a delete method for removing
      a POU whose .st file was deleted from the source folder (completes
      the create/update/**delete** engine)? **VALIDATED 2026-07-04** —
      `ITcSmTreeItem.DeleteChild(bstrName)` on the parent (e.g. the POUs
      folder) removes a child POU by name. Simulated Diagnostics.st being
      deleted from source: detected it as orphaned (exists in project,
      no matching .st file), called `pousFolder.DeleteChild("Diagnostics")`,
      rebuilt with 0 errors, and confirmed Diagnostics.TcPOU was removed
      from disk. The full create/update/delete engine is now validated
      end-to-end with a real TwinCAT/Visual Studio instance.
- [ ] Spike: does the Automation Interface expose IO variable linking
      (not just device/box creation), and at what granularity?
- [x] Verify: does the tool reuse/reopen a persistent TwinCAT project
      across repeated runs instead of recreating it? **VALIDATED
      2026-07-04** — ran the assembled engine twice in a row: run 1
      bootstrapped a fresh "Shark" project (no solution found), run 2
      detected the existing Shark.sln and reopened it via
      `dte.Solution.Open()` instead of recreating anything, then synced
      and built successfully both times. (Note: this validates project
      persistence across separate process runs, not yet a single
      long-lived DTE session across N in-process builds — that's a
      distinct, still-open question for the eventual watch-mode/Cluster C
      wrapper.)

## MVP Engine — Assembled 2026-07-04
The validated spikes have been consolidated into a reusable engine, live in
beckhoffAutomationInterface/:
- `Sync/StPouSource.cs`, `Sync/StFileParser.cs` — .st → declaration/implementation
- `Sync/PouSyncEngine.cs` — create/update/delete reconciliation against TwinCAT
- `Sync/BuildRunner.cs`, `Sync/SyncReport.cs` — build + result reporting
- `Program.cs` — thin orchestrator: opens the persistent "Shark" project if it
  exists, bootstraps it if not, then runs the sync + build + report pipeline
  against ST/Shark/*.st

Ran twice in a row against a real TwinCAT/Visual Studio instance: first run
bootstraps, second run reopens the same project and re-syncs — both passed.

## Extended 2026-07-04 (later same day): ENUM, STRUCT, FUNCTION, FUNCTION_BLOCK + METHODs
Added support for the remaining core IEC 61131-3 object kinds, all validated
against the real Shark project:
- **ENUM/STRUCT DUTs** (with `{attribute ...}` pragmas) — `TREEITEMTYPE_PLCDUTENUM`
  (605) / `TREEITEMTYPE_PLCDUTSTRUCT` (606), synced into the project's `DUTs`
  folder. DUTs have no Implementation section — only `DeclarationText` is set.
- **FUNCTION** — `TREEITEMTYPE_PLCPOUFUNC` (603), synced like a PROGRAM.
- **FUNCTION_BLOCK with inline METHODs** — `TREEITEMTYPE_PLCPOUFB` (604) for the
  FB itself, `TREEITEMTYPE_PLCMETHOD` (609) for each method, created as a child
  of the FB's own tree item (not the POUs folder). Convention: a FUNCTION_BLOCK
  and all of its METHODs live in **one file** (e.g. `FB_Motor.st` contains
  `FUNCTION_BLOCK FB_Motor ... METHOD Init ... METHOD Reset ...`);
  `StFileParser` splits this into separate synced objects internally by
  scanning for `METHOD <name>` line boundaries (absorbing any immediately
  preceding attribute-pragma lines into that method's section). A standalone
  `<Owner>.<Method>.st` file is also still supported.
- Fixed the naive parser to split on the **last** `END_VAR` instead of the
  first, since FUNCTION_BLOCK/METHOD/FUNCTION headers commonly have multiple
  VAR/VAR_INPUT/VAR_OUTPUT blocks.
- Fixed a latent `BuildRunner` bug: it previously treated every entry in
  `ErrorList.ErrorItems` (including warnings/messages) as a build failure.
  Now only `vsBuildErrorLevelHigh` (=3) counts toward `Success`; everything
  else is reported separately as a warning.
- `PouSyncEngine` now reconciles three tiers per run: top-level POUs
  (PROGRAM/FUNCTION_BLOCK/FUNCTION), top-level DUTs (ENUM/STRUCT), and
  METHODs (synced/pruned per-FB after the FB itself is created/updated),
  with orphan deletion at each tier.

Ran against the live Shark project: first pass created `FB_Motor`,
`F_ClampSpeed`, `E_MotorState`, `ST_MotorStatus`, `FB_Motor.Init`, and
`FB_Motor.Reset` in one sync (6 created, 1 updated for MAIN), build passed.
After consolidating the FB + its two methods into a single `FB_Motor.st`
file, a re-run correctly matched all three as updates (not create/delete)
and still built clean.

## MVP Scope
One job, done well: sync + compile + report for POUs only, using Shark's
current MAIN.st as the test case.
- In: file->POU create/update/delete engine, direct text injection,
  build trigger, console pass/fail + error output, drift check.
- Out (for now): GVLs, library references, IO mapping/activation,
  watch mode, CLI/CI packaging, multi-target/team features.

## Not Doing (and Why)
- Full-project rebuild on every run — too slow/destructive once I/O
  config and hardware scans exist alongside .st-managed POUs.
- Auto-export TwinCAT -> .st as the primary flow — inverts the goal;
  .st must remain the human-edited source of truth.
- Team/CI tooling before the core engine is proven — validate the
  riskiest assumption (text injection + compile feedback) solo first.

## Open Questions
- Does Beckhoff's InfoSys confirm ITcPlcDeclaration/ITcPlcImplementation
  as the intended API for this, or is PlcOpenImport the only supported path?
- What's the actual object model for IO variable linking in the AI?
- How will conflicting edits be handled once this becomes a team/GitHub
  workflow (two people editing the same POU in parallel)?
