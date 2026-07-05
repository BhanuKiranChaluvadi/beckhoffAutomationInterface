# ST-as-Source TwinCAT Sync Engine

> **Status (2026-07-05): shipped end-to-end.** Editing only `.st` files +
> `libraries.xml` + `io-devices.xml`, the tool syncs POUs/DUTs/GVLs,
> library references, the full EtherCAT IO tree, and live variable\u2194IO
> channel links, then compiles and reports pass/fail \u2014 fully unattended.
> Only Activate-Configuration (runtime target), watch mode, and CLI/CI
> packaging remain as future work.

## Problem Statement
How might we let an engineer maintain a TwinCAT PLC project as plain .st
text files in Git, and have those files automatically sync \u2014 create,
update, compile, and IO-map (and eventually activate) \u2014 into a real TwinCAT
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
- [x] Spike: does the Automation Interface expose IO variable linking
      (not just device/box creation), and at what granularity?
      **VALIDATED (partially) 2026-07-05** — `ITcSysManager.LinkVariables(
      bstrV1, bstrV2, offs1, offs2, size)` / `UnlinkVariables(bstrV1,
      bstrV2)` exist and are callable on the TwinCAT 3 XAE `ITcSysManager`
      (confirmed via reflection on Interop.TCatSysManager.dll — inherited
      by all versions up to `ITcSysManager18`), matching Beckhoff's
      TwinCAT 2-era "How to link variables" documentation. Granularity is
      byte-level: `offs1`/`offs2` are byte offsets into each variable and
      `size` is the number of bytes to link, for cases where the two
      variables differ in size.
      **However**, calling it against real Shark project paths in the
      same `TIPC^...^GVLs^GVL_Shark^varName` tree-path convention used
      everywhere else in this engine failed both times with `COMException:
      Item '...' not found` — for a mismatched-type PLC<->PLC link *and*
      for a link to a nonexistent IO path. This means `LinkVariables`
      does **not** use the same declaration-tree addressing as
      `LookupTreeItem`/`CreateChild`; Beckhoff's own sample path
      (`"TIPC^Project1^Standard^Outputs^MyOutput"`) suggests it expects a
      variable that's already surfaced under a task's mapped I/O list
      (e.g. an instance/symbol path), not a raw GVL declaration path.
      **Conclusion**: the AI does expose variable linking, but wiring it
      into this engine would need further research into TwinCAT 3's
      actual instance/symbol path format for linkable variables (out of
      scope for this MVP) — recorded here so the next attempt doesn't
      have to rediscover this from scratch. Test code was added to
      `Program.cs`, run against the live project, and then removed (it
      was a one-off spike, not a shipped feature).
      **Follow-up (2026-07-05, same day)**: confirmed the root cause with
      a targeted `LookupTreeItem` test on real GVL variables
      (`bMotorRunSensor`/`bMotorEnableOutput`, added to `GVL_Shark` as
      `AT %I*`/`AT %Q*` placeholders). `LookupTreeItem("TIPC^Shark^Shark
      Project^GVLs^GVL_Shark")` **succeeds** (the GVL itself is a real
      tree item), but appending the variable name with either `^` or `.`
      (`...^GVL_Shark^bMotorRunSensor`, `...^GVL_Shark.bMotorRunSensor`)
      **fails** with "Subitem ... not found". This proves individual PLC
      variables (whether in a GVL or a POU) are **not** separate tree
      items in the Automation Interface's tree model at all — they only
      exist as text inside the GVL's/POU's `DeclarationText`. Only true
      hardware-side items (Device/Box/Terminal/Channel) and the
      GVL/POU/DUT containers themselves are tree items. This means
      `LinkVariables`'s variable-path arguments must use a fundamentally
      different addressing scheme (most likely a pure ADS symbol path,
      e.g. `GVL_Shark.bMotorRunSensor` without any `TIPC^...` tree
      prefix, resolvable only once the configuration is built/activated
      on a real or simulated running target) rather than anything
      `LookupTreeItem`/`CreateChild` can resolve. Confirming the exact
      format would require either a running target (TwinCAT allows
      "Activate Configuration" even without real I/O, using a local
      simulated runtime) or TwinCAT-3-specific documentation (the
      InfoSys pages found are all TwinCAT 2-era) — both out of scope for
      this pass. **This engine can declare `AT %I*`/`AT %Q*` I/O
      variables and use them in logic (compiles cleanly, validated
      end-to-end), but cannot yet automate the actual hardware linking
      step** — that still requires a human to use the IDE's I/O mapping
      view (or `ConsumeXml`/`LinkVariables` with the correct symbol path,
      once found) after this tool runs.
      **UPDATE (2026-07-05, RESOLVED — see "variable-to-IO LINKING solved"
      section below): automated hardware linking now works.** The correct
      symbol path is the mapped *instance* path
      (`TIPC^Shark^Shark Instance^PlcTask Inputs^GVL_Shark.bMotorRunSensor`),
      and `ITcPlcProject.CompileProject()` must run first to generate it.
      No human IDE step and no runtime target are needed after all.
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
      wrapper, and out of scope for the current one-shot CLI usage.)

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

## Extended 2026-07-05: GVLs and library references
Added the last two pieces of the original "config-as-data for everything
else" idea, both validated against the live Shark project:
- **GVL** — detected via a file whose first keyword is `VAR_GLOBAL` (e.g.
  `GVL_Shark.st`); like DUTs, a GVL has no Implementation section, so only
  `DeclarationText` (the whole file, including any `{attribute ...}` pragma)
  is set. Synced into the project's `GVLs` folder via `TREEITEMTYPE_PLCGVL`
  (615) — same `PouSyncEngine.SyncTopLevel` machinery as POUs/DUTs, just a
  third tier and tree path.
- **Library references** — not IEC source, so they don't fit the `.st`
  convention; instead a small `libraries.xml` manifest (`<Library Name="..."
  Version="..." Company="..." />`) is parsed by `LibraryManifestParser` and
  reconciled by `LibrarySyncEngine` against `ITcPlcLibraryManager`
  (`sysManager.LookupTreeItem("TIPC^Shark^Shark Project^References")` cast to
  `ITcPlcLibraryManager`), using `AddLibrary`/`RemoveReference`.

Two real bugs were found and fixed only by actually running this against
TwinCAT (not guessable from docs alone):
- **`ITcPlcReferences` is 0-based** — unlike `ITcSmTreeItem.Child`/`ErrorItems`
  (both 1-based), enumerating it with a 1-based loop threw
  `ArgumentOutOfRangeException`. Fixed to `for (int i = 0; i < references.Count; i++)`.
- **Our own duplicate-reference pre-check (reading `.References`) can be
  stale/incomplete right after (re)opening a project.** A second run against
  an already-synced project threw `ArgumentException: Managed Library
  '...' already contained!` from `AddLibrary` itself, even though our
  pre-check said it wasn't present. Fixed by treating that specific
  exception as an idempotent no-op — `AddLibrary`'s internal check is more
  authoritative than our own collection read.

After both fixes, three consecutive runs against the live project confirmed:
create (GVL + library added), then two idempotent re-syncs (both reporting
0 created/added, correct "updated" counts, no spurious errors), all with a
clean build.

## Extended 2026-07-05 (later same day): INTERFACE, ALIAS type, EXTENDS, abstract methods, PUBLIC/PRIVATE
Added the remaining OOP-flavored IEC 61131-3 constructs, all validated
against the live Shark project (`I_Motor`, `T_MotorSpeed`, `FB_MotorBase`):
- **INTERFACE** (`TREEITEMTYPE_PLCITF` = 618) with inline `METHOD` signatures
  (no body) using `TREEITEMTYPE_PLCITFMETH` (610) — a *different* type id
  than FB methods (609). Same single-file convention as FUNCTION_BLOCK
  (`I_Motor.st` contains `INTERFACE I_Motor ... METHOD Init ... METHOD
  Reset ...`); `StFileParser` now shares one `FindMethodBoundaries`/
  `ParseMethodSegments` helper between FB and INTERFACE parsing.
- **ALIAS DUT** (`TYPE T_MotorSpeed : LREAL; END_TYPE`) — detected when a
  `TYPE` file contains neither `STRUCT` nor `(` (enum literal list).
- **EXTENDS** for FUNCTION_BLOCK and INTERFACE, and **IMPLEMENTS** for
  FUNCTION_BLOCK — `StPouSource.BaseType` now carries the EXTENDS/alias
  target, extracted via regex from the header line.
- **Abstract FUNCTION_BLOCK/METHOD** — just an `{attribute 'abstract'}`-style
  pragma (`{abstract}`) before the keyword; no engine changes needed since
  attribute-pragma lines were already preserved as part of the declaration.
- **PUBLIC/PRIVATE method modifiers** (`METHOD PUBLIC Init : BOOL`) — fixed
  `MethodHeaderRegex` to skip an optional access-modifier keyword before
  capturing the method name (previously "PUBLIC"/"PRIVATE" would have been
  misparsed as the method name itself).

Three more real bugs found only by running against TwinCAT, each with a
different, non-obvious `CreateChild` `vInfo` requirement:
- **INTERFACE requires a non-null base-class string even with no EXTENDS**
  — `""` means "no base interface"; passing `null` throws "Base class not
  specified!".
- **ALIAS requires the aliased base type name as `vInfo`** (e.g. `"LREAL"`)
  — omitting it throws the same "Base class not specified!" error.
  (`{attribute}`-style pragmas on aliases weren't the issue; the missing
  base type was.)
- **FUNCTION_BLOCK is the opposite of INTERFACE**: it requires `null` when
  there's no EXTENDS (passing `""` throws "Must specify valid information
  for parsing in the string") and the actual base FB name only when EXTENDS
  is present. So the same `vInfo` rule cannot be shared across kinds —
  `PouSyncEngine` now branches per-kind explicitly.

Also found and fixed two reliability issues unrelated to any single kind:
- **`Program.cs` never called `dte.Quit()`**, even on success — every run
  (crashed or not) leaked a `devenv.exe` process. Five had accumulated
  across this session's testing, and once enough piled up, COM calls
  started failing with `RPC_E_SERVERCALL_RETRYLATER` ("the application is
  busy") purely from resource contention. Fixed with a `try/finally`
  around the whole sync so VS always closes.
- **`RPC_E_SERVERCALL_RETRYLATER` can also happen transiently right after
  a large sync** (16 objects in one pass), while VS's background
  compiler/IntelliSense is still catching up, causing the very next COM
  call (`AddLibrary`) to fail. Generalized the existing `WaitForVsToLoad`
  retry pattern into a `RetryOnBusy(action, description)` helper and
  wrapped the library-sync and build steps with it.

After all fixes, a clean run synced all 16 objects (FB with EXTENDS +
IMPLEMENTS + abstract override + public/private methods, an interface with
methods, an alias, two DUTs, a GVL, a program, and a function) with 0
errors, and a second consecutive run reported 0 created / 16 updated / 0
deleted with no transient failures and a clean VS shutdown (no leaked
processes).

## Extended 2026-07-05 (later same day): IO hardware tree creation \u2014 BREAKTHROUGH
Following up on the "add an EL IO card" question, discovered the actual
working recipe for creating real EtherCAT hardware tree items, live
against the Shark project:

- **The key fix was `vInfo`, not the type constant.** Earlier attempts to
  create an EK1100 coupler passed `vInfo = null` (matching the pattern used
  for PROGRAM/FUNCTION_BLOCK creation) and failed with `Invalid item sub
  type`. The actual fix: pass the **product name as a plain string** for
  `vInfo` (e.g. `"EK1100"`, `"EL1008"`). Once that was tried, **all** of
  the following succeeded:
  - `ioDevicesRoot.CreateChild("Device 1 (EtherCAT)", 94 /* TSM_DEV_TYPE_ETHERCAT */, "", null)`
    \u2014 EtherCAT master device (vInfo can stay `null` here; it's a device,
    not a specific hardware model).
  - `device.CreateChild("Term 1 (EK1100)", 6 /* TREEITEMTYPE_TERM */, "", "EK1100")`
    \u2014 **succeeded**, as a child of the master device.
  - `coupler.CreateChild("Term 2 (EL1008)", 6, "", "EL1008")` and
    `coupler.CreateChild("Term 3 (EL2008)", 6, "", "EL2008")` \u2014 **both
    succeeded**, as children of the EK1100 coupler \u2014 matching the exact
    real-world E-bus topology (Master \u2192 Coupler \u2192 Terminals) the user
    asked about. The generic `TREEITEMTYPE_TERM = 6` type works for *any*
    Beckhoff product as long as `vInfo` is its product name string; the
    legacy per-model `TCSYSMANAGERBOXTYPES`/`TCSYSMANAGERDEVICETYPES` enums
    are unnecessary for terminals (only used for the master's own device
    type, `TSM_DEV_TYPE_ETHERCAT = 94`).
  - This means the full topology the user described \u2014 *EtherCAT Master \u2192
    EK1100 coupler \u2192 arbitrary EL cards* \u2014 **is fully scriptable** via
    `CreateChild`, generalizable to a declarative manifest (same pattern as
    `libraries.xml`): a small XML/text file listing Device \u2192 Box \u2192
    Terminal entries by product name, reconciled by a new `IoSyncEngine`
    analogous to `LibrarySyncEngine`. **Not yet built** \u2014 recorded here as
    the next concrete increment once the remaining risk below is resolved.
- **Individual channels are still not separate tree items pre-activation.**
  `elTerminal.ChildCount == 0` for a freshly created EL1008 in an
  unconfigured project \u2014 channels (e.g. "Channel 1", "Input") only likely
  appear as distinct `ITcSmTreeItem`s after the configuration is activated
  on a real/simulated target, or possibly only via the IDE's own linking
  UI. This still blocks fully automating `LinkVariables`.
- **`LinkVariables` PLC-side path still fails, with a new clue.** Retried
  with a real terminal now in the tree (IO-side argument =
  `TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)`) against three
  PLC-side path candidates \u2014 all failed:
  - `"GVL_Shark.bMotorRunSensor"` (bare ADS-style) \u2014 not found.
  - `".GVL_Shark.bMotorRunSensor"` (leading dot) \u2014 not found.
  - `"TIPC^Shark^Shark Project^GVLs^GVL_Shark^bMotorRunSensor"` (tree path)
    \u2014 not found, but with a telling difference: the error specifically
    says **`(Shark Project failed)`**, meaning resolution broke down at
    that particular segment. Combined with Beckhoff's own TC2 sample path
    for `LinkVariables` (`"TIPC^Project1^Standard^Outputs^MyOutput"`,
    where `"Standard"` is a **task name**, not a GVL folder) this strongly
    suggests the PLC-side argument must reference a **PLC task's
    Inputs/Outputs mapping node** (a per-task tree location that appears
    once variables are mapped to a task), not the GVL declaration tree at
    all. This remains unresolved \u2014 next step would be to find/guess the
    task tree path convention (e.g. `TIPC^Shark^Shark Project^Standard^
    Inputs^...`) and retry, or reverse-engineer it from a manually-linked
    project's `.tsproj` XML.
- **Operational risk confirmed again, and mitigated in the spike
  discipline**: every spike in this round created the full Device/Coupler/
  Terminal tree, tested what it needed to test, then **always deleted the
  master device before the build step** \u2014 specifically to avoid
  re-triggering the earlier-discovered blocking "needs sync master" popup.
  **Not yet tested**: whether leaving terminals attached (vs. a bare empty
  master) is enough to satisfy that validation and avoid the popup. This
  is the next thing to check before any IoSyncEngine can safely *persist*
  created hardware across a real build.

**Conclusion**: hardware topology creation (Device/Box/Terminal) is now a
solved problem \u2014 ready to become a real, shippable manifest + sync engine.
Actual variable linking (`LinkVariables`) is still blocked on finding the
correct PLC-side (task mapping) path format, and on confirming whether an
unlinked-but-populated I/O tree still blocks unattended builds.

## Extended 2026-07-05 (later same day): IO SyncEngine shipped + closed-loop build
Turned the IO-tree spike into a real, shipped feature and closed the
remaining loop so the whole pipeline runs unattended:

- **`io-devices.xml` manifest + `IoManifestParser` + `IoSyncEngine`** \u2014
  same "config data, not .st" pattern as `libraries.xml`. The manifest
  declares the hardware tree by product name:
  ```xml
  <IoTree>
    <Device Name="Device 1 (EtherCAT)" Disabled="true">
      <Box Name="Term 1 (EK1100)" Product="EK1100">
        <Terminal Name="Term 2 (EL1008)" Product="EL1008" />
        <Terminal Name="Term 3 (EL2008)" Product="EL2008" />
      </Box>
    </Device>
  </IoTree>
  ```
  `IoSyncEngine.Sync` reconciles Device\u2192Box\u2192Terminal against `TIID`,
  creating only what's missing (idempotent, append/update-only) and
  deleting only genuine orphans. Validated: first run created all 4 items;
  a re-run reported `0 created, 0 deleted, 0 state change(s)`.
- **The "needs sync master" popup is solved by DISABLING the master, not
  by clicking the dialog.** `ITcSmTreeItem.Disabled` (type `DISABLED_STATE`:
  `SMDS_NOT_DISABLED=0`, `SMDS_DISABLED=1`, `SMDS_PARENT_DISABLED=2`) lets us
  mark the unlinked EtherCAT master disabled. The hardware is still fully
  populated and visible in the tree (just grayed out), but TwinCAT skips the
  "at least one variable linked to a task" validation on Build \u2014 so the
  build passes with **zero popups and zero human interaction**. Declared
  per-device via the manifest's `Disabled="true"` attribute; flip to
  `false` once variable-linking is automated. (Confirmed by direct test:
  an *enabled* master blocks the build even WITH terminals attached \u2014 it's
  the missing task LINK, not missing hardware, that triggers the dialog.)
- **Build timeout + guaranteed process cleanup** (defensive, per user
  request): `BuildRunner.Build` now kicks off the build ASYNCHRONOUSLY
  (`SolutionBuild.Build(false)`) and polls `BuildState` to completion with a
  hard 5-minute deadline, throwing `BuildTimeoutException` instead of
  hanging forever if a modal dialog ever does block it. `Program.Main`'s
  `finally` attempts a graceful `dte.Quit()` on a background thread with a
  timeout, then ALWAYS verifies the devenv process actually exited
  (force-killing it otherwise) \u2014 because a graceful `Quit()` can *return*
  while the process lingers behind a modal dialog. The devenv PID is
  captured reliably by diffing the `devenv` process list before/after
  `CreateInstance` (HWND capture was fragile once a dialog was up). Proved
  end-to-end: with a temporary 30s timeout against a deliberately-blocking
  build, the tool timed out cleanly, printed a helpful message, and
  recovered the leaked devenv on its own with no human input.
- **Idempotency bug found + fixed via the tree dump.** TwinCAT enumerates
  EtherCAT terminals BOTH under their coupler AND flat under the device
  (with the same coupler-nested `PathName`). The device-level orphan prune
  therefore saw the terminals as device orphans and deleted them, then the
  box loop recreated them \u2014 a create/delete churn every run (which would
  also break any variable links). Fixed by only treating a child as an
  orphan candidate when it's a GENUINE direct child
  (`child.PathName == parent.PathName + "^" + child.Name`), which naturally
  excludes the flat-enumerated deeper terminals.

**Net result**: `Master \u2192 EK1100 \u2192 EL1008 + EL2008` is now declared in
`io-devices.xml`, synced idempotently into the project, and the whole
pipeline (POUs + DUTs + GVL + libraries + IO tree + build) runs green,
unattended, with no popups and no leaked processes. The only remaining
open item is automating the actual variable-to-task LINK (which would let
the master be enabled); everything else the user asked for is shipped.

## Extended 2026-07-05 (final): variable-to-IO LINKING solved \u2014 full closed loop
The last open item \u2014 automating the actual PLC-variable-to-hardware link \u2014
is now **solved and shipped**. The whole pipeline runs unattended with the
EtherCAT master ENABLED and its channels genuinely linked to the PLC
`%I*`/`%Q*` variables, build green, no popups.

Two missing pieces were found (the official Beckhoff sample
`example/.../Scripting.CSharp.Scripts/Scripts/EtherCATLinking.cs` was the key):
1. **The confirmed path format** (roots `TIPC^`/`TIID^` prepended by the engine):
   - PLC side: `TIPC^Shark^Shark Instance^PlcTask Inputs^GVL_Shark.bMotorRunSensor`
     (and `PlcTask Outputs^GVL_Shark.bMotorEnableOutput`) \u2014 the mapped
     *instance* image path, NOT the GVL declaration path that every earlier
     attempt (wrongly) used.
   - IO side: `TIID^Device 1 (EtherCAT)^Term 1 (EK1100)^Term 2 (EL1008)^Channel 1^Input`
     (and `...^Term 3 (EL2008)^Channel 1^Output`).
2. **`ITcPlcProject.CompileProject()` must be called first.** Casting the PLC
   project root (`TIPC^Shark`) to `ITcPlcProject` and calling `CompileProject()`
   generates the instance I/O image so the PLC-side path resolves. (Also
   discovered on `ITcPlcProject`: `GenerateBootProject(bool)`,
   `BootProjectAutostart`, `TmcFileCopy`.)

Crucial correction to an earlier assumption: the tree DUMPS showed
`Shark Instance` and each EL terminal with `ChildCount=0`, which looked like
"the paths don't exist without Activate Configuration." That was misleading \u2014
**`LinkVariables` resolves those paths directly by name even though the tree
enumeration doesn't expand them**. So after `CompileProject()`, both
`LinkVariables` calls succeed against a target-less dev environment, the
master (now having linked variables) no longer trips the "needs sync master"
validation, and the build passes. No Activate Configuration / runtime target
was needed after all.

Shipped as:
- **`<Links>` section in `io-devices.xml`** (`<Link PlcVar="..." IoChannel="..."/>`),
  parsed by `IoManifestParser.ParseLinks` into `LinkSpec`.
- **`VariableLinkEngine`**: `CompileProject()` then `LinkVariables(TIPC^..., TIID^...)`
  per declared link, reporting linked vs. unresolved. Naturally idempotent \u2014
  re-runs report `2 linked, 0 unresolved` every time with no churn or errors.
- **Graceful fallback**: if any declared link can't be resolved (e.g. a genuinely
  different environment where the paths don't materialize), `IoSyncEngine.
  DisableAllMasters` disables the master(s) so the build still stays green and
  unattended \u2014 so the tool never hangs on the popup regardless.

**Final state**: editing only `.st` files + `libraries.xml` + `io-devices.xml`,
the tool creates/updates the entire TwinCAT project \u2014 POUs, DUTs, GVLs,
libraries, the full EtherCAT IO tree, AND the live variable\u2194channel links \u2014
then compiles and reports pass/fail, fully unattended, with a 5-minute build
timeout and guaranteed devenv cleanup as safety nets. The original goal is
met end to end.

## MVP Scope
One job, done well: sync + compile + report for POUs only, using Shark's
current MAIN.st as the test case.
- In: file->POU create/update/delete engine, direct text injection,
  build trigger, console pass/fail + error output, drift check.
- Out (for now): watch mode, CLI/CI packaging, multi-target/team features.
  (GVLs, library references, the full EtherCAT IO tree, AND variable↔IO
  channel linking — originally listed here as deferred/out-of-scope — are
  now all implemented; see the dated "Extended 2026-07-05" sections above.)

## Not Doing (and Why)
- Full-project rebuild on every run — too slow/destructive once I/O
  config and hardware scans exist alongside .st-managed POUs.
- Auto-export TwinCAT -> .st as the primary flow — inverts the goal;
  .st must remain the human-edited source of truth.
- Team/CI tooling before the core engine is proven — validate the
  riskiest assumption (text injection + compile feedback) solo first.

## Open Questions
- ~~Does Beckhoff's InfoSys confirm ITcPlcDeclaration/ITcPlcImplementation
  as the intended API for this?~~ **Answered**: yes — InfoSys documents
  both interfaces directly (`DeclarationText`/`ImplementationText`,
  Get/Set, TwinCAT 3.1+), and this was independently confirmed by
  successfully round-tripping real `.st` content through them.
- ~~What's the actual object model for IO variable linking in the AI?~~
  **Answered & SHIPPED (2026-07-05)**: `ITcSysManager.LinkVariables` works
  once (1) `ITcPlcProject.CompileProject()` has generated the PLC instance
  image and (2) the mapped *instance* path is used. See the final
  "variable-to-IO LINKING solved" section — now automated end-to-end via
  `VariableLinkEngine` + the `<Links>` manifest section.
- ~~What's the exact TwinCAT 3 instance/symbol path format `LinkVariables`
  expects for a PLC-side variable (vs. the `TIPC^...^GVLs^...` declaration
  path that works for `LookupTreeItem`/`CreateChild`)?~~ **Answered**:
  `TIPC^<Plc>^<Plc> Instance^PlcTask Inputs|Outputs^<GVL/PROGRAM>.<var>`
  (the mapped instance path), resolvable after `CompileProject()`.
- Does adding a real (or virtual/simulated) EtherCAT IO device/terminal to
  the Shark project's I/O tree make `LinkVariables` succeed? **Tested
  2026-07-05, partial result**: reflection on the local
  Interop.TCatSysManager.dll found `TCSYSMANAGERDEVICETYPES` (has
  `TSM_DEV_TYPE_ETHERCAT = 94`) and `TCSYSMANAGERBOXTYPES` (has
  `TSM_BOX_TYPE_EK1100 = 9092`) — fixed legacy TC2-era type-ID enums.
  Live test against the real Shark project:
  - `sysManager.LookupTreeItem("TIID")` (the I/O Devices root) **succeeds**.
  - `ioDevices.CreateChild("Device 1 (EtherCAT)", 94, "", null)`
    **succeeds** — creates a real EtherCAT master device at
    `TIID^Device 1 (EtherCAT)`.
  - `device.CreateChild("Term 1 (EK1100)", 9092, "", null)` **fails**:
    `COMException: Invalid item sub type`. The legacy `TCSYSMANAGERBOXTYPES`
    enum is not a valid box sub-type scheme for a modern EtherCAT master's
    slaves — EtherCAT terminals (EK couplers, EL terminals) must be added
    a different way, almost certainly by selecting a device from the ESI
    catalog (an XML-based `vInfo`, not a fixed numeric constant). This
    engine has not yet reverse-engineered that XML schema.
  - Also note: `TCSYSMANAGERBOXTYPES` only has ~200 entries and covers
    older Lightbus/Profibus/CANopen/older Bus Terminal-era hardware (BK/BC
    couplers, a handful of EL67xx fieldbus gateways) — it does **not**
    include common modern EL-series IO terminals like EL1008/EL2008 at
    all. Even if EK1100 creation had worked, adding actual digital IO
    terminals would need the ESI-catalog route regardless.
  - **Important operational discovery**: leaving an EtherCAT master device
    in the project with **no slaves/no linked variables** makes TwinCAT
    pop a **blocking native modal dialog** on the next `Build` — *"Device
    'Device 1 (EtherCAT)' needs sync master (at least one variable linked
    to a tasked variable)"* — which hangs the whole automated run (and any
    unattended CI-style usage) until a human manually dismisses it. This
    was observed directly: the build step hung for ~20 minutes until the
    popup was closed by hand. **This means partially-configured/orphaned
    I/O devices are actively dangerous for this engine's unattended-build
    goal** — any future IO-tree sync engine must guarantee it never leaves
    a device in an incomplete state across a `Save()`+`Build()`, or must
    delete incomplete devices before building. The orphaned test device
    was removed via `ioDevices.DeleteChild("Device 1 (EtherCAT)")` in a
    follow-up run, which cleanly fixed the build (no popup, 0 errors).
  - **Conclusion**: EtherCAT master *device* creation is confirmed
    automatable via a fixed type constant. Coupler/terminal creation is
    not automatable via any fixed constant found so far — it needs the
    ESI-catalog XML approach, which requires further reverse-engineering
    (e.g. manually add one EK1100 + one EL1008 in the IDE, then use
    `ProduceXml` on each to capture the exact XML schema TwinCAT uses
    internally, and replay that as `vInfo` for `CreateChild`). Until that
    schema is known, this engine cannot safely auto-provision terminals,
    and must never leave a master device without slaves/links across a
    build step.

- How will conflicting edits be handled once this becomes a team/GitHub
  workflow (two people editing the same POU in parallel)?

## Other Automation Interface Capabilities (Beckhoff InfoSys survey, 2026-07-05)
While researching the IO-linking question, the InfoSys "How to..." index
for the (TwinCAT 2-era, but largely still applicable) Automation Interface
was surveyed for capabilities beyond what this engine already uses. None
of these have been implemented or spike-tested yet — recorded here as a
backlog for future work:
- **Enable/disable a tree item**, change a PLC project's path, force a
  rescan.
- **Change a fieldbus device's address**, exchange one fieldbus device for
  another of the same type.
- **Export/import child info as XML** (`ProduceXml`/`ConsumeXml`) — could
  snapshot or restore a whole subtree (e.g. an I/O configuration) instead
  of rebuilding it item-by-item.
- **Link an NC axis/encoder/drive to IO** — same underlying idea as
  `LinkVariables` but for motion objects; likely has the same PLC-side
  addressing question.
- **Scan devices and boxes** — requires `ITcSysManager3.SetTargetNetId`
  pointed at a real or TwinCAT-simulated running target; Shark currently
  has no target configured, so this is untested.
- **Add a route to a remote ADS target**, **broadcast search** for
  reachable targets on the network.

Of these, XML export/import is the most promising near-term addition
since it doesn't require a live target. Device scanning, NC linking, and
resolving the `LinkVariables` PLC-side path all require either real or
simulated hardware, or further reverse-engineering of TwinCAT 3's internal
addressing — out of scope until a target is available to test against.
